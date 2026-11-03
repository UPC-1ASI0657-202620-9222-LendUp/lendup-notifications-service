using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Api.Application;

/// <summary>
/// NotificacionApplicationService (Fig. 20): consulta y marcado de avisos, generación de avisos a partir de eventos
/// de otros contextos y envío de los correos pendientes.
/// </summary>
public sealed class NotificationApplicationService(
    INotificationRepository repository,
    EventNotificationCatalog catalog,
    ReminderApplicationService reminders,
    IIdentityPort identity,
    IEmailSender email,
    NotificationOptions options,
    TimeProvider time,
    ILogger<NotificationApplicationService> logger)
{
    public Task<IReadOnlyList<Notification>> ListAsync(Guid userId, CancellationToken ct) => repository.ListVisibleAsync(userId, options.ListLimit, ct);

    public async Task<Notification> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct)
    {
        var notification = await repository.FindAsync(notificationId, ct) ?? throw new NotFoundException("No encontrado");
        if (notification.RecipientUserId != userId) throw new ForbiddenException("Operación no permitida");
        try { notification.MarkRead(time.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException e) { throw new BusinessConflictException(e.Message); }
        await repository.SaveChangesAsync(ct);
        return notification;
    }

    /// <summary>
    /// Aplica un evento sin guardar: quien llama (el consumidor) confirma avisos e inbox en la misma transacción.
    /// Devuelve la cantidad de avisos nuevos.
    /// </summary>
    public async Task<int> ApplyEventAsync(IntegrationEvent e, CancellationToken ct)
    {
        var plan = catalog.Plan(e);
        var created = 0;
        var now = time.GetUtcNow().UtcDateTime;
        var sourceEventId = e.EventId.ToString();
        foreach (var draft in plan.Notifications)
        {
            if (await AddIfAbsentAsync(Notification.ForEvent(draft.RecipientUserId, draft.NotificationKey, draft.OriginType, draft.OriginId,
                    sourceEventId, draft.Title, draft.Message, true, now), ct)) created++;
        }
        if (plan.Schedule is { } schedule) await reminders.ScheduleForReservationAsync(schedule, sourceEventId, ct);
        if (plan.CancelledReservationId is { } reservationId)
        {
            var recipients = (await repository.FindByOriginAsync(OriginType.Reserva, reservationId, ct)).Select(n => n.RecipientUserId).Distinct().ToList();
            if (recipients.Count == 0) logger.LogWarning("Reserva {ReservationId} cancelada sin avisos previos; no se conocen sus participantes", reservationId);
            foreach (var recipient in recipients)
            {
                if (await AddIfAbsentAsync(Notification.ForEvent(recipient, $"ReservaCancelada:{reservationId}", OriginType.Reserva, reservationId, sourceEventId,
                        "Reserva cancelada", "La reserva fue cancelada y el periodo quedó liberado.", true, now), ct)) created++;
            }
            await reminders.CancelForReservationAsync(reservationId, ct);
        }
        return created;
    }

    /// <summary>Envía los correos pendientes de avisos visibles; ante error reintenta hasta EmailMaxAttempts.</summary>
    public async Task<int> DispatchPendingEmailsAsync(int limit, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var sent = 0;
        foreach (var notification in await repository.FindPendingEmailsAsync(now, limit, ct))
        {
            try
            {
                var address = await identity.TryGetEmailAsync(notification.RecipientUserId, ct);
                if (address is null && email.RequiresAddress) throw new DependencyUnavailableException("Contrato de correo con Identidad no configurado");
                await email.SendAsync(new EmailMessage(notification.RecipientUserId, address, notification.Title, notification.Message), ct);
                notification.RegisterEmailSent(now);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("No se pudo enviar el correo de la notificación {NotificationId}: {Error}", notification.Id, e.Message);
                notification.RegisterEmailFailure(e.Message, now, options.EmailMaxAttempts);
            }
            await repository.SaveChangesAsync(ct);
        }
        return sent;
    }

    private async Task<bool> AddIfAbsentAsync(Notification notification, CancellationToken ct)
    {
        if (await repository.ExistsAsync(notification.RecipientUserId, notification.NotificationKey, ct)) return false;
        repository.Add(notification);
        return true;
    }
}
