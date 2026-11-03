using System.Globalization;
using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Api.Application;

/// <summary>RecordatorioApplicationService (Fig. 20): programa, cancela y activa los recordatorios de RF22.</summary>
public sealed class ReminderApplicationService(INotificationRepository repository, ReminderPolicy policy, TimeProvider time)
{
    public async Task ScheduleForReservationAsync(ReservationSchedule schedule, string? sourceEventId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var deliveryAt = policy.DeliveryReminderAt(schedule.FromLocal, now);
        var returnAt = policy.ReturnReminderAt(schedule.ToLocal, now);
        foreach (var recipient in new[] { schedule.BorrowerUserId, schedule.LenderUserId }.Distinct())
        {
            await AddIfAbsentAsync(Notification.ForReminder(recipient, $"RecordatorioEntrega:{schedule.ReservationId}", OriginType.Reserva, schedule.ReservationId,
                sourceEventId, "Recordatorio de entrega", $"La entrega del objeto está programada para el {Format(schedule.FromLocal)}.", true, deliveryAt, now), ct);
            await AddIfAbsentAsync(Notification.ForReminder(recipient, $"RecordatorioDevolucion:{schedule.ReservationId}", OriginType.Reserva, schedule.ReservationId,
                sourceEventId, "Recordatorio de devolución", $"La devolución del objeto está programada para el {Format(schedule.ToLocal)}.", true, returnAt, now), ct);
        }
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

    private async Task AddIfAbsentAsync(Notification notification, CancellationToken ct)
    {
        if (!await repository.ExistsAsync(notification.RecipientUserId, notification.NotificationKey, ct)) repository.Add(notification);
    }

    private static string Format(DateTime local) => local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
