namespace LendUp.Notifications.Api.Domain;

/// <summary>NotificacionValidator (Fig. 20): reglas para registrar notificaciones y recordatorios.</summary>
public static class NotificationValidator
{
    public const int MaxTitleLength = 180;
    public const int MaxKeyLength = 200;
    public const int MaxSourceEventIdLength = 150;

    public static void ValidateNew(Guid recipientUserId, string notificationKey, Guid originId, string title, string message)
    {
        if (recipientUserId == Guid.Empty) throw new ArgumentException("La notificación requiere un destinatario");
        if (originId == Guid.Empty) throw new ArgumentException("La notificación requiere el identificador de origen");
        if (string.IsNullOrWhiteSpace(notificationKey) || notificationKey.Length > MaxKeyLength)
            throw new ArgumentException($"clave_notificacion debe tener entre 1 y {MaxKeyLength} caracteres");
        if (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitleLength)
            throw new ArgumentException($"El título debe tener entre 1 y {MaxTitleLength} caracteres");
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("El mensaje es obligatorio");
    }

    public static void ValidateSchedule(DateTime scheduledFor)
    {
        if (scheduledFor == default) throw new ArgumentException("El recordatorio requiere programada_para");
        if (scheduledFor.Kind != DateTimeKind.Utc) throw new ArgumentException("programada_para debe estar en UTC");
    }
}
