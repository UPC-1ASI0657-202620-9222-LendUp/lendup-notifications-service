using System.Text;
using System.Text.Json;
using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using LendUp.Notifications.Api.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LendUp.Notifications.Tests.Support;

/// <summary>Servicios de aplicación reales con persistencia en memoria, Identidad falsa y correo registrado.</summary>
public sealed class NotificationsFixture
{
    public InMemoryNotificationRepository Repository { get; } = new();
    public FakeIdentity Identity { get; } = new();
    public RecordingEmailSender Email { get; }
    public MutableTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    public EventRoutingOptions Routes { get; } = new();
    public NotificationOptions Options { get; } = new();
    public ReminderApplicationService Reminders { get; }
    public NotificationApplicationService Notifications { get; }

    public Guid Lender { get; } = Guid.NewGuid();
    public Guid Borrower { get; } = Guid.NewGuid();

    public NotificationsFixture(bool emailRequiresAddress = true)
    {
        Email = new RecordingEmailSender(emailRequiresAddress);
        Reminders = new ReminderApplicationService(Repository, new ReminderPolicy(Options.DeliveryReminderLead, Options.ReturnReminderLead), Time);
        Notifications = new NotificationApplicationService(Repository, new EventNotificationCatalog(Routes), Reminders, Identity, Email, Options, Time,
            NullLogger<NotificationApplicationService>.Instance);
    }

    /// <summary>Simula el EventProcessor: inbox + avisos en una unidad de trabajo.</summary>
    public async Task<bool> PublishAsync(string routingKey, string eventType, object payload, Guid? eventId = null)
    {
        var e = EventProcessor.Parse(routingKey, Envelope(eventType, payload, eventId ?? Guid.NewGuid()));
        if (await Repository.IsProcessedAsync(e.EventId, default)) return false;
        await Notifications.ApplyEventAsync(e, default);
        Repository.MarkProcessed(e.EventId, "test", e.AggregateId, e.AggregateVersion, Time.GetUtcNow().UtcDateTime);
        await Repository.SaveChangesAsync(default);
        return true;
    }

    public static byte[] Envelope(string eventType, object payload, Guid eventId) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["event_id"] = eventId.ToString(), ["event_type"] = eventType, ["event_version"] = 1, ["aggregate_version"] = 1,
            ["aggregate_id"] = Guid.NewGuid().ToString(), ["occurred_at"] = "2026-11-08T12:00:00Z", ["correlation_id"] = "test", ["payload"] = payload,
        }));

    public object ReservationConfirmed(Guid reservation, string from = "2026-11-10T09:00:00", string to = "2026-11-12T09:00:00") => new Dictionary<string, object?>
    {
        ["reserva_id"] = reservation.ToString(), ["solicitud_id"] = Guid.NewGuid().ToString(), ["publicacion_id"] = Guid.NewGuid().ToString(),
        ["prestamista_usuario_id"] = Lender.ToString(), ["prestatario_usuario_id"] = Borrower.ToString(), ["desde"] = from, ["hasta"] = to,
        ["tarifa_diaria_acordada"] = 20.00m, ["garantia_monetaria_acordada"] = null, ["moneda_acordada"] = "PEN",
    };

    public object Participants(params (string Key, object Value)[] extra)
    {
        var payload = new Dictionary<string, object?>
        {
            ["prestamo_id"] = Guid.NewGuid().ToString(), ["prestatario_usuario_id"] = Borrower.ToString(), ["prestamista_usuario_id"] = Lender.ToString(), ["moneda"] = "PEN",
        };
        foreach (var (key, value) in extra) payload[key] = value;
        return payload;
    }

    public IEnumerable<Notification> For(Guid user) => Repository.All.Where(n => n.RecipientUserId == user);
}
