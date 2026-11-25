using System.Globalization;
using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Api.Application;

/// <summary>RecordatorioApplicationService (Fig. 20): programa, cancela y activa los recordatorios de RF22.</summary>
public sealed class ReminderApplicationService(INotificationRepository repository, ReminderPolicy policy, TimeProvider time)
{
    private const string DeliveryKey = "RecordatorioEntrega";
    private const string ReturnKey = "RecordatorioDevolucion";

    public async Task ScheduleForReservationAsync(ReservationSchedule schedule, string? sourceEventId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var deliveryAt = policy.DeliveryReminderAt(schedule.FromLocal, now);
        var returnAt = policy.ReturnReminderAt(schedule.ToLocal, now);
        foreach (var recipient in new[] { schedule.BorrowerUserId, schedule.LenderUserId }.Distinct())
        {
            await AddIfAbsentAsync(Notification.ForReminder(recipient, $"{DeliveryKey}:{schedule.ReservationId}", OriginType.Reserva, schedule.ReservationId,
                sourceEventId, "Recordatorio de entrega", $"La entrega del objeto está programada para el {Format(schedule.FromLocal)}.", true, deliveryAt, now), ct);
            await AddReturnAsync(recipient, $"{ReturnKey}:{schedule.ReservationId}", schedule.ReservationId, schedule.ToLocal, returnAt, sourceEventId, now, ct);
        }
    }

    /// <summary>
    /// Anula los recordatorios que ya no corresponden (entrega o devolución hechas) y, si cambió la fecha de devolución,
    /// reemplaza los pendientes por otros en la nueva fecha para los mismos destinatarios.
    /// </summary>
    public async Task ApplyChangeAsync(ReminderChange change, string? sourceEventId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var reminders = (await repository.FindByOriginAsync(OriginType.Reserva, change.ReservationId, ct)).Where(n => n.Type == NotificationType.Recordatorio).ToList();
        if (change.CancelDelivery) CancelAll(reminders, DeliveryKey, now);
        if (change.CancelReturn || change.NewReturnLocal is not null) CancelAll(reminders, ReturnKey, now);
        if (change.NewReturnLocal is not { } returnLocal) return;
        var returnAt = policy.ReturnReminderAt(returnLocal, now);
        foreach (var recipient in reminders.Select(r => r.RecipientUserId).Distinct())
            await AddReturnAsync(recipient, $"{ReturnKey}:{change.ReservationId}:{change.ChangeId}", change.ReservationId, returnLocal, returnAt, sourceEventId, now, ct);
    }

    public async Task<int> CancelForReservationAsync(Guid reservationId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var cancelled = 0;
        foreach (var notification in await repository.FindByOriginAsync(OriginType.Reserva, reservationId, ct))
        {
            if (notification.Type == NotificationType.Recordatorio && notification.CancelReminder(now)) cancelled++;
        }
        return cancelled;
    }

    /// <summary>Llamado por ReminderScheduler: pasa a DISPONIBLE los recordatorios cuya hora llegó.</summary>
    public async Task<int> ActivateDueAsync(int limit, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var due = await repository.FindDueRemindersAsync(now, limit, ct);
        foreach (var reminder in due) reminder.MakeAvailable(now);
        if (due.Count > 0) await repository.SaveChangesAsync(ct);
        return due.Count;
    }

    private static void CancelAll(IEnumerable<Notification> reminders, string keyPrefix, DateTime now)
    {
        foreach (var reminder in reminders.Where(r => r.NotificationKey.StartsWith(keyPrefix + ":", StringComparison.Ordinal))) reminder.CancelReminder(now);
    }

    private Task AddReturnAsync(Guid recipient, string key, Guid reservationId, DateTime returnLocal, DateTime returnAt, string? sourceEventId, DateTime now, CancellationToken ct) =>
        AddIfAbsentAsync(Notification.ForReminder(recipient, key, OriginType.Reserva, reservationId, sourceEventId, "Recordatorio de devolución",
            $"La devolución del objeto está programada para el {Format(returnLocal)}.", true, returnAt, now), ct);

    private async Task AddIfAbsentAsync(Notification notification, CancellationToken ct)
    {
        if (!await repository.ExistsAsync(notification.RecipientUserId, notification.NotificationKey, ct)) repository.Add(notification);
    }

    private static string Format(DateTime local) => local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
