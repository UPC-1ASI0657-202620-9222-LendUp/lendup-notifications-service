using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Api.Application;

/// <summary>NotificacionRepository (Fig. 20).</summary>
public interface INotificationRepository
{
    /// <summary>Avisos visibles del destinatario (DISPONIBLE o LEIDA), del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<Notification>> ListVisibleAsync(Guid recipientUserId, int limit, CancellationToken ct);
    Task<Notification?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(Guid recipientUserId, string notificationKey, CancellationToken ct);
    Task<IReadOnlyList<Notification>> FindByOriginAsync(OriginType originType, Guid originId, CancellationToken ct);
    Task<IReadOnlyList<Notification>> FindDueRemindersAsync(DateTime nowUtc, int limit, CancellationToken ct);
    Task<IReadOnlyList<Notification>> FindPendingEmailsAsync(DateTime nowUtc, int limit, CancellationToken ct);
    void Add(Notification notification);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Inbox: event_id ya procesados (dedupe del consumidor).</summary>
public interface IInboxStore
{
    Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct);
    void MarkProcessed(Guid eventId, string consumer, Guid aggregateId, long aggregateVersion, DateTime processedAtUtc);
}

/// <summary>Contratos con Identidad (pendientes de acordar; configurables por variables de entorno).</summary>
public interface IIdentityPort
{
    Task<Guid> ResolveUserIdAsync(string firebaseUid, string bearerToken, CancellationToken ct);

    /// <summary>Correo del destinatario, o null si el contrato no está configurado.</summary>
    Task<string?> TryGetEmailAsync(Guid userId, CancellationToken ct);
}

public sealed record EmailMessage(Guid RecipientUserId, string? ToAddress, string Subject, string PlainText);

/// <summary>EmailAdapter (Fig. 20). LendUp decide cuándo enviar; el proveedor solo entrega el correo (R24).</summary>
public interface IEmailSender
{
    /// <summary>false cuando el envío solo se registra en el log (desarrollo local sin SENDGRID_API_KEY).</summary>
    bool RequiresAddress { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
