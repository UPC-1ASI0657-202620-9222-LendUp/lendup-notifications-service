using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using LendUp.Notifications.Api.Infrastructure.Messaging;
using LendUp.Notifications.Api.Infrastructure.Persistence;
using LendUp.Notifications.Api.Infrastructure.Security;
using LendUp.Notifications.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace LendUp.Notifications.Tests.Integration;

/// <summary>Servicio completo contra MySQL (migraciones EF Core) y RabbitMQ reales.</summary>
public sealed class RealInfrastructureFactory(ContainersFixture containers) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var mysql = containers.MySql!;
        var rabbit = containers.RabbitMq!;
        builder.UseEnvironment("Testing");
        builder.UseSetting("FIREBASE_PROJECT_ID", "test-project");
        builder.UseSetting("DB_HOST", mysql.Hostname);
        builder.UseSetting("DB_PORT", mysql.GetMappedPublicPort(3306).ToString());
        builder.UseSetting("DB_NAME", "lendup_notificaciones");
        builder.UseSetting("DB_USERNAME", "test");
        builder.UseSetting("DB_PASSWORD", "test");
        builder.UseSetting("RABBITMQ_HOST", rabbit.Hostname);
        builder.UseSetting("RABBITMQ_PORT", rabbit.GetMappedPublicPort(5672).ToString());
        builder.UseSetting("RABBITMQ_USERNAME", "test");
        builder.UseSetting("RABBITMQ_PASSWORD", "test");
        builder.UseSetting("REMINDER_SCHEDULER_ENABLED", "false");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ITokenVerifier, FakeTokenVerifier>();
            services.AddSingleton<IIdentityPort, FakeIdentity>();
        });
    }
}

public class MessagingIntegrationTests(ContainersFixture containers) : IClassFixture<ContainersFixture>
{
    [SkippableFact]
    public async Task ConsumeDesdeElBrokerDeduplicaYExponePorLaApi()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker no disponible");
        await using var factory = new RealInfrastructureFactory(containers);
        var client = factory.CreateClient();
        await WaitUntilAsync(async () => (await client.GetAsync("/actuator/health/readiness")).StatusCode == HttpStatusCode.OK);

        var borrower = FakeIdentity.UserFor("prestatario-it");
        var lender = FakeIdentity.UserFor("prestamista-it");
        var reservation = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var lima = DateTime.UtcNow.AddHours(-5);
        var payload = new Dictionary<string, object?>
        {
            ["reserva_id"] = reservation.ToString(), ["prestamista_usuario_id"] = lender.ToString(), ["prestatario_usuario_id"] = borrower.ToString(),
            ["desde"] = lima.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss"), ["hasta"] = lima.AddDays(3).ToString("yyyy-MM-ddTHH:mm:ss"),
            ["tarifa_diaria_acordada"] = 20.0, ["garantia_monetaria_acordada"] = null, ["moneda_acordada"] = "PEN",
        };
        await PublishAsync("reservas.confirmada.v1", NotificationsFixture.Envelope("ReservaConfirmada", payload, eventId));
        await PublishAsync("reservas.confirmada.v1", NotificationsFixture.Envelope("ReservaConfirmada", payload, eventId));

        await WaitUntilAsync(async () => { await using var db = Db(factory); return await db.ProcessedEvents.AnyAsync(p => p.EventId == eventId); });
        await Task.Delay(500);
        await using (var db = Db(factory))
        {
            Assert.Equal(1, await db.ProcessedEvents.CountAsync(p => p.EventId == eventId));
            Assert.Equal(6, await db.Notifications.CountAsync(n => n.OriginId == reservation));
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "prestatario-it");
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/v1/notificaciones"));
        Assert.Contains(list.RootElement.EnumerateArray(), row => row.GetProperty("titulo").GetString() == "Reserva confirmada");
    }

    [SkippableFact]
    public async Task RechazoDeSolicitudCambioDeFechaYCalificacionFuncionanConBaseYBrokerReales()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker no disponible");
        await using var factory = new RealInfrastructureFactory(containers);
        var client = factory.CreateClient();
        await WaitUntilAsync(async () => (await client.GetAsync("/actuator/health/readiness")).StatusCode == HttpStatusCode.OK);

        Guid borrower = Guid.NewGuid(), lender = Guid.NewGuid(), request = Guid.NewGuid(), reservation = Guid.NewGuid(), loan = Guid.NewGuid(), change = Guid.NewGuid(), last = Guid.NewGuid();
        var people = new Dictionary<string, object?> { ["prestamista_usuario_id"] = lender.ToString(), ["prestatario_usuario_id"] = borrower.ToString() };
        Dictionary<string, object?> With(params (string Key, object? Value)[] extra) => new(people.Concat(extra.Select(x => KeyValuePair.Create(x.Key, x.Value))));
        var returnLocal = DateTime.UtcNow.AddHours(-5).AddDays(9);
        await PublishAsync("reservas.solicitud-creada.v1", NotificationsFixture.Envelope("SolicitudCreada", With(("solicitud_id", request.ToString())), Guid.NewGuid()));
        await PublishAsync("reservas.solicitud-rechazada.v1", NotificationsFixture.Envelope("SolicitudRechazada", new Dictionary<string, object> { ["solicitud_id"] = request.ToString(), ["motivo_rechazo"] = "No disponible" }, Guid.NewGuid()));
        await PublishAsync("reservas.confirmada.v1", NotificationsFixture.Envelope("ReservaConfirmada", With(("reserva_id", reservation.ToString()),
            ("desde", DateTime.UtcNow.AddHours(-5).AddDays(2).ToString("yyyy-MM-ddTHH:mm:ss")), ("hasta", DateTime.UtcNow.AddHours(-5).AddDays(5).ToString("yyyy-MM-ddTHH:mm:ss"))), Guid.NewGuid()));
        await PublishAsync("prestamos.fecha-devolucion-actualizada.v1", NotificationsFixture.Envelope("FechaDevolucionActualizada", With(("prestamo_id", loan.ToString()), ("reserva_id", reservation.ToString()),
            ("cambio_fecha_prestamo_id", change.ToString()), ("devolucion_vigente_en", returnLocal.ToString("yyyy-MM-ddTHH:mm:ss"))), Guid.NewGuid()));
        await PublishAsync("reputation.rating-registered.v1", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["event_id"] = last.ToString(), ["correlation_id"] = last.ToString(), ["event_type"] = "CalificacionRegistrada", ["event_version"] = 1, ["evaluacion_id"] = Guid.NewGuid().ToString(),
            ["prestamo_id"] = loan.ToString(), ["evaluador_usuario_id"] = lender.ToString(), ["evaluado_usuario_id"] = borrower.ToString(), ["puntaje"] = 4, ["registrada_en"] = "2026-11-25T18:00:00Z",
        })));
        await WaitUntilAsync(async () => { await using var db = Db(factory); return await db.ProcessedEvents.AnyAsync(p => p.EventId == last); });

        await using var stored = Db(factory);
        var mine = await stored.Notifications.Where(n => n.RecipientUserId == borrower).ToListAsync();
        Assert.Contains(mine, n => n.Title == "Solicitud rechazada" && n.Message.EndsWith("No disponible"));
        Assert.Contains(mine, n => n.Title == "Nueva fecha de devolución");
        Assert.Contains(mine, n => n.Title == "Nueva calificación");
        var reminders = mine.Where(n => n.Title == "Recordatorio de devolución").ToList();
        Assert.Single(reminders, r => r.State == NotificationState.Cancelada);
        var moved = Assert.Single(reminders, r => r.State == NotificationState.Programada);
        Assert.InRange(moved.ScheduledFor!.Value, returnLocal.AddHours(5).AddDays(-1).AddSeconds(-2), returnLocal.AddHours(5).AddDays(-1).AddSeconds(2));
    }

    [SkippableFact]
    public async Task RecordatorioVencidoSeActivaConLaBaseReal()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker no disponible");
        await using var factory = new RealInfrastructureFactory(containers);
        factory.CreateClient();
        var reservation = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var reminders = scope.ServiceProvider.GetRequiredService<ReminderApplicationService>();
            var lima = DateTime.UtcNow.AddHours(-5);
            await reminders.ScheduleForReservationAsync(new ReservationSchedule(reservation, Guid.NewGuid(), Guid.NewGuid(), lima.AddHours(1), lima.AddDays(5)), "evt", default);
            await scope.ServiceProvider.GetRequiredService<INotificationRepository>().SaveChangesAsync(default);
        }
        using (var scope = factory.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<ReminderApplicationService>().ActivateDueAsync(100, default) >= 2);
        }
        await using var db = Db(factory);
        var states = await db.Notifications.Where(n => n.OriginId == reservation).Select(n => new { n.Title, n.State }).ToListAsync();
        Assert.All(states.Where(s => s.Title == "Recordatorio de entrega"), s => Assert.Equal(NotificationState.Disponible, s.State));
        Assert.All(states.Where(s => s.Title == "Recordatorio de devolución"), s => Assert.Equal(NotificationState.Programada, s.State));
    }

    [SkippableFact]
    public async Task ClaveDuplicadaPorDestinatarioViolaLaUnicidad()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker no disponible");
        await using var factory = new RealInfrastructureFactory(containers);
        factory.CreateClient();
        await using var db = Db(factory);
        var user = Guid.NewGuid();
        var origin = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Notifications.Add(Notification.ForEvent(user, "clave-unica", OriginType.Pago, origin, null, "t", "m", false, now));
        db.Notifications.Add(Notification.ForEvent(user, "clave-unica", OriginType.Pago, origin, null, "t", "m", false, now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task MensajeInvalidoTerminaEnLaDlq()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, "Docker no disponible");
        await using var factory = new RealInfrastructureFactory(containers);
        var client = factory.CreateClient();
        await WaitUntilAsync(async () => (await client.GetAsync("/actuator/health/readiness")).StatusCode == HttpStatusCode.OK);
        var body = Encoding.UTF8.GetBytes("{\"event_id\":\"no-es-uuid\"}");
        await PublishAsync("reservas.cancelada.v1", body);

        await using var connection = await Factory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        BasicGetResult? dead = null;
        await WaitUntilAsync(async () => (dead = await channel.BasicGetAsync(RabbitMqTopology.DeadLetterQueue, autoAck: true)) is not null);
        Assert.Equal(body, dead!.Body.ToArray());
    }

    private ConnectionFactory Factory() => new()
    {
        HostName = containers.RabbitMq!.Hostname, Port = containers.RabbitMq.GetMappedPublicPort(5672), UserName = "test", Password = "test",
    };

    private async Task PublishAsync(string routingKey, byte[] body)
    {
        await using var connection = await Factory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.BasicPublishAsync(RabbitMqTopology.Exchange, routingKey, mandatory: false, new BasicProperties { ContentType = "application/json" }, body);
    }

    private static NotificationsDbContext Db(WebApplicationFactory<Program> factory) =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<NotificationsDbContext>();

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try { if (await condition()) return; } catch (Exception) { /* todavía arrancando */ }
            await Task.Delay(250);
        }
        Assert.Fail($"La condición no se cumplió en {seconds} s");
    }
}
