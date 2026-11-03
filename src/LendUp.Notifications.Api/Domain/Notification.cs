namespace LendUp.Notifications.Api.Domain;

/// <summary>
/// Agregado Notificacion (Fig. 20): un registro por destinatario, con su propio estado de lectura y de envío por
/// correo. Los recordatorios nacen PROGRAMADA y pasan a DISPONIBLE a su hora; solo una DISPONIBLE puede leerse.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string NotificationKey { get; private set; } = "";
    public NotificationType Type { get; private set; }
    public OriginType OriginType { get; private set; }
    public Guid OriginId { get; private set; }
    public string? SourceEventId { get; private set; }
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";
    public NotificationState State { get; private set; }
    public DateTime? ScheduledFor { get; private set; }
    public DateTime? AvailableAt { get; private set; }
    public DateTime? ReadAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public bool SendEmail { get; private set; }
    public EmailState EmailState { get; private set; }
    public int EmailAttempts { get; private set; }
    public DateTime? LastEmailAttemptAt { get; private set; }
    public DateTime? EmailSentAt { get; private set; }
    public string? LastEmailError { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Notification() { }

    /// <summary>Aviso generado por un evento: disponible de inmediato (RF48).</summary>
    public static Notification ForEvent(Guid recipientUserId, string notificationKey, OriginType originType, Guid originId,
        string? sourceEventId, string title, string message, bool sendEmail, DateTime now)
    {
        var notification = Create(recipientUserId, notificationKey, NotificationType.Evento, originType, originId, sourceEventId, title, message, sendEmail, now);
        notification.State = NotificationState.Disponible;
        notification.AvailableAt = now;
        return notification;
    }

    /// <summary>Recordatorio de una fecha del préstamo (RF22): queda PROGRAMADA hasta scheduledFor.</summary>
    public static Notification ForReminder(Guid recipientUserId, string notificationKey, OriginType originType, Guid originId,
        string? sourceEventId, string title, string message, bool sendEmail, DateTime scheduledFor, DateTime now)
    {
        NotificationValidator.ValidateSchedule(scheduledFor);
        var notification = Create(recipientUserId, notificationKey, NotificationType.Recordatorio, originType, originId, sourceEventId, title, message, sendEmail, now);
        notification.State = NotificationState.Programada;
        notification.ScheduledFor = scheduledFor;
        return notification;
    }

    private static Notification Create(Guid recipientUserId, string notificationKey, NotificationType type, OriginType originType, Guid originId,
        string? sourceEventId, string title, string message, bool sendEmail, DateTime now)
    {
        NotificationValidator.ValidateNew(recipientUserId, notificationKey, originId, title, message);
        if (sourceEventId is { Length: > NotificationValidator.MaxSourceEventIdLength }) sourceEventId = sourceEventId[..NotificationValidator.MaxSourceEventIdLength];
        return new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = recipientUserId,
            NotificationKey = notificationKey,
            Type = type,
            OriginType = originType,
            OriginId = originId,
            SourceEventId = sourceEventId,
            Title = title,
            Message = message,
            SendEmail = sendEmail,
            EmailState = sendEmail ? EmailState.Pendiente : EmailState.NoAplica,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void MarkRead(DateTime now)
    {
        if (State != NotificationState.Disponible) throw new InvalidOperationException("Aviso no disponible");
        State = NotificationState.Leida;
        ReadAt = now;
        UpdatedAt = now;
    }

    public bool IsDue(DateTime now) => State == NotificationState.Programada && ScheduledFor <= now;

    public void MakeAvailable(DateTime now)
    {
        if (State != NotificationState.Programada) throw new InvalidOperationException("Solo un recordatorio programado puede activarse");
        State = NotificationState.Disponible;
        AvailableAt = now;
        UpdatedAt = now;
    }

    /// <summary>Cancela un recordatorio que todavía no se mostró; devuelve false si ya no aplica.</summary>
    public bool CancelReminder(DateTime now)
    {
        if (State != NotificationState.Programada) return false;
        State = NotificationState.Cancelada;
        CancelledAt = now;
        if (EmailState == EmailState.Pendiente) EmailState = EmailState.NoAplica;
        UpdatedAt = now;
        return true;
    }

    /// <summary>El correo solo se envía cuando el aviso ya es visible para el estudiante.</summary>
    public bool EmailReady => EmailState == EmailState.Pendiente && State is NotificationState.Disponible or NotificationState.Leida;

    public void RegisterEmailSent(DateTime now)
    {
        if (EmailState != EmailState.Pendiente) throw new InvalidOperationException("El correo no está pendiente");
        EmailAttempts++;
        EmailState = EmailState.Enviado;
        LastEmailAttemptAt = now;
        EmailSentAt = now;
        LastEmailError = null;
        UpdatedAt = now;
    }

    public void RegisterEmailFailure(string error, DateTime now, int maxAttempts)
    {
        if (EmailState != EmailState.Pendiente) throw new InvalidOperationException("El correo no está pendiente");
        EmailAttempts++;
        LastEmailAttemptAt = now;
        LastEmailError = error.Length > 1000 ? error[..1000] : error;
        if (EmailAttempts >= maxAttempts) EmailState = EmailState.Fallido;
        UpdatedAt = now;
    }
}
