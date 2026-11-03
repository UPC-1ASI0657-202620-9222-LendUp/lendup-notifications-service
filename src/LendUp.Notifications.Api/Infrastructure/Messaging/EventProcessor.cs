using System.Text.Json;
using LendUp.Notifications.Api.Application;
using Microsoft.EntityFrameworkCore;
using LendUp.Notifications.Api.Infrastructure.Persistence;

namespace LendUp.Notifications.Api.Infrastructure.Messaging;

/// <summary>El mensaje no cumple el envelope: no se reintenta, va directo a la DLQ.</summary>
public sealed class InvalidMessageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Procesa un mensaje en una transacción local: si el event_id ya está en processed_events se descarta; si no, se
/// generan los avisos y se registra el event_id en la misma transacción (inbox).
/// </summary>
public sealed class EventProcessor(NotificationsDbContext db, NotificationApplicationService notifications, IInboxStore inbox, TimeProvider time,
    ILogger<EventProcessor> logger)
{
    public const string Consumer = RabbitMqTopology.Queue;

    public async Task<bool> ProcessAsync(string routingKey, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var e = Parse(routingKey, body);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (await inbox.IsProcessedAsync(e.EventId, ct))
            {
                logger.LogInformation("Evento {EventId} ({RoutingKey}) ya procesado; se descarta", e.EventId, routingKey);
                return false;
            }
            var created = await notifications.ApplyEventAsync(e, ct);
            inbox.MarkProcessed(e.EventId, Consumer, e.AggregateId, e.AggregateVersion, time.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            logger.LogInformation("Evento {EventType} {EventId} procesado: {Created} avisos nuevos", e.EventType, e.EventId, created);
            return true;
        });
    }

    public static IntegrationEvent Parse(string routingKey, ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            Guid Guid(string name) => System.Guid.Parse(root.GetProperty(name).GetString()!);
            var payload = root.GetProperty("payload");
            if (payload.ValueKind != JsonValueKind.Object) throw new InvalidMessageException("payload debe ser un objeto");
            var aggregateId = root.TryGetProperty("aggregate_id", out var aggregate) && aggregate.ValueKind == JsonValueKind.String
                ? System.Guid.Parse(aggregate.GetString()!) : System.Guid.Empty;
            var version = root.TryGetProperty("aggregate_version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0L;
            var correlation = root.TryGetProperty("correlation_id", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "";
            return new IntegrationEvent(Guid("event_id"), root.GetProperty("event_type").GetString()!, routingKey, aggregateId, version, correlation, payload.Clone());
        }
        catch (InvalidMessageException) { throw; }
        catch (Exception e) { throw new InvalidMessageException("Mensaje sin el envelope estándar: " + e.Message, e); }
    }
}
