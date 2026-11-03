using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using LendUp.Notifications.Api.Infrastructure.Security;

namespace LendUp.Notifications.Tests.Support;

/// <summary>Repositorio en memoria con la misma semántica que el de EF Core (unicidad destinatario + clave).</summary>
public sealed class InMemoryNotificationRepository : INotificationRepository, IInboxStore
{
    private readonly List<Notification> _pending = [];
    public List<Notification> Stored { get; } = [];
    public HashSet<Guid> Processed { get; } = [];
    private readonly HashSet<Guid> _pendingProcessed = [];

    public IEnumerable<Notification> All => Stored.Concat(_pending);

    public Task<IReadOnlyList<Notification>> ListVisibleAsync(Guid recipientUserId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Notification>>(Stored.Where(n => n.RecipientUserId == recipientUserId && n.State is NotificationState.Disponible or NotificationState.Leida)
            .OrderByDescending(n => n.CreatedAt).Take(limit).ToList());

    public Task<Notification?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(All.FirstOrDefault(n => n.Id == id));

    public Task<bool> ExistsAsync(Guid recipientUserId, string notificationKey, CancellationToken ct) =>
        Task.FromResult(All.Any(n => n.RecipientUserId == recipientUserId && n.NotificationKey == notificationKey));

    public Task<IReadOnlyList<Notification>> FindByOriginAsync(OriginType originType, Guid originId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Notification>>(All.Where(n => n.OriginType == originType && n.OriginId == originId).ToList());

    public Task<IReadOnlyList<Notification>> FindDueRemindersAsync(DateTime nowUtc, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Notification>>(Stored.Where(n => n.IsDue(nowUtc)).Take(limit).ToList());

    public Task<IReadOnlyList<Notification>> FindPendingEmailsAsync(DateTime nowUtc, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Notification>>(Stored.Where(n => n.EmailReady).Take(limit).ToList());

    public void Add(Notification notification) => _pending.Add(notification);

    public Task SaveChangesAsync(CancellationToken ct)
    {
        Stored.AddRange(_pending);
        _pending.Clear();
        Processed.UnionWith(_pendingProcessed);
        _pendingProcessed.Clear();
        return Task.CompletedTask;
    }

    public Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct) => Task.FromResult(Processed.Contains(eventId));

    public void MarkProcessed(Guid eventId, string consumer, Guid aggregateId, long aggregateVersion, DateTime processedAtUtc) => _pendingProcessed.Add(eventId);
}

public sealed class FakeIdentity : IIdentityPort
{
    public Dictionary<Guid, string> Emails { get; } = [];
    public bool EmailContractConfigured { get; set; } = true;

    public static Guid UserFor(string uid) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(uid)));

    public Task<Guid> ResolveUserIdAsync(string firebaseUid, string bearerToken, CancellationToken ct) => Task.FromResult(UserFor(firebaseUid));

    public Task<string?> TryGetEmailAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(EmailContractConfigured ? (Emails.TryGetValue(userId, out var email) ? email : $"{userId:N}@upc.edu.pe") : null);
}

public sealed class RecordingEmailSender(bool requiresAddress = true) : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public bool Fail { get; set; }
    public bool RequiresAddress { get; } = requiresAddress;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (Fail) throw new DependencyUnavailableException("SendGrid no disponible (simulado)");
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan delta) => Now += delta;
}

/// <summary>El token de prueba es el propio UID ("Bearer prestatario"); "invalido" falla y "sin-verificar" no tiene correo verificado.</summary>
public sealed class FakeTokenVerifier : ITokenVerifier
{
    public Task<VerifiedToken> VerifyAsync(string idToken, CancellationToken ct) => idToken switch
    {
        "invalido" => throw new InvalidOperationException("token inválido"),
        "sin-verificar" => Task.FromResult(new VerifiedToken(idToken, false, false)),
        _ => Task.FromResult(new VerifiedToken(idToken, true, false)),
    };
}
