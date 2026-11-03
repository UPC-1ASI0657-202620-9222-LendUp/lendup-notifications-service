using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Infrastructure.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LendUp.Notifications.Api.Infrastructure.Messaging;

/// <summary>Nombres comunes a todos los servicios (estándar de eventos de Reservas).</summary>
public static class RabbitMqTopology
{
    public const string Exchange = "lendup.domain.events";
    public const string DeadLetterExchange = "lendup.domain.events.dlx";
    public const string Queue = "notifications.events.v1";
    public const string DeadLetterQueue = "notifications.events.v1.dlq";

    public static async Task DeclareAsync(IChannel channel, IEnumerable<string> bindings, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange, ["x-dead-letter-routing-key"] = DeadLetterQueue },
            cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, DeadLetterQueue, cancellationToken: ct);
        foreach (var key in bindings) await channel.QueueBindAsync(Queue, Exchange, key, cancellationToken: ct);
    }
}

/// <summary>Estado del consumidor para el health check de readiness.</summary>
public sealed class MessagingStatus
{
    public volatile bool Connected;
}

/// <summary>
/// MessageBrokerAdapter (Fig. 20): consume notifications.events.v1 con ack manual. Cada mensaje se reintenta
/// 3 veces con espera creciente; si sigue fallando (o no cumple el envelope) se rechaza sin reencolar y RabbitMQ
/// lo envía a la DLQ notifications.events.v1.dlq.
/// </summary>
public sealed class MessageBrokerAdapter(IServiceScopeFactory scopes, ServiceSettings settings, MessagingStatus status, ILogger<MessageBrokerAdapter> logger)
    : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                status.Connected = false;
                logger.LogWarning("RabbitMQ no disponible ({Error}); reintento en 5 s", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
        status.Connected = false;
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = settings.RabbitMq.Host,
            Port = settings.RabbitMq.Port,
            UserName = settings.RabbitMq.User,
            Password = settings.RabbitMq.Password,
            ClientProvidedName = "lendup-notifications-service",
            AutomaticRecoveryEnabled = true,
        };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await RabbitMqTopology.DeclareAsync(channel, settings.Routes.Bindings(), ct);
        await channel.BasicQosAsync(0, 10, false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var accepted = await HandleAsync(delivery, ct);
            if (accepted) await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
            else await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        };
        await channel.BasicConsumeAsync(RabbitMqTopology.Queue, autoAck: false, consumer, ct);
        status.Connected = true;
        logger.LogInformation("Consumiendo {Queue} con bindings {Bindings}", RabbitMqTopology.Queue, string.Join(", ", settings.Routes.Bindings()));

        var closed = new TaskCompletionSource();
        connection.ConnectionShutdownAsync += (_, _) => { closed.TrySetResult(); return Task.CompletedTask; };
        await using (ct.Register(() => closed.TrySetCanceled(ct)))
        {
            await closed.Task;
        }
        throw new InvalidOperationException("La conexión con RabbitMQ se cerró");
    }

    /// <summary>true = ack (procesado o duplicado); false = nack sin reencolar (DLQ).</summary>
    internal async Task<bool> HandleAsync(BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EventProcessor>().ProcessAsync(delivery.RoutingKey, delivery.Body, ct);
                return true;
            }
            catch (InvalidMessageException e)
            {
                logger.LogError("Mensaje inválido en {RoutingKey}; se envía a la DLQ: {Error}", delivery.RoutingKey, e.Message);
                return false;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("Intento {Attempt}/{Max} fallido para {RoutingKey}: {Error}", attempt, MaxAttempts, delivery.RoutingKey, e.Message);
                if (attempt < MaxAttempts) await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), ct);
            }
        }
        return false;
    }
}
