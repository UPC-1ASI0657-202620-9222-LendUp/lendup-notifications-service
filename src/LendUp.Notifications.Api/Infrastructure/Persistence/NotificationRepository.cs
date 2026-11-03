using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LendUp.Notifications.Api.Infrastructure.Persistence;

/// <summary>NotificacionRepositoryImpl (Fig. 20) e inbox sobre MySQL con EF Core.</summary>
public sealed class NotificationRepository(NotificationsDbContext db) : INotificationRepository, IInboxStore
{
    private static readonly TimeSpan[] EmailBackoff = [TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

    public async Task<IReadOnlyList<Notification>> ListVisibleAsync(Guid recipientUserId, int limit, CancellationToken ct) =>
        await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == recipientUserId && (n.State == NotificationState.Disponible || n.State == NotificationState.Leida))
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Take(limit).ToListAsync(ct);

    public Task<Notification?> FindAsync(Guid id, CancellationToken ct) => db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);

    /// <summary>Considera también los avisos agregados en esta misma unidad de trabajo, aún sin guardar.</summary>
    public async Task<bool> ExistsAsync(Guid recipientUserId, string notificationKey, CancellationToken ct) =>
        db.Notifications.Local.Any(n => n.RecipientUserId == recipientUserId && n.NotificationKey == notificationKey)
        || await db.Notifications.AnyAsync(n => n.RecipientUserId == recipientUserId && n.NotificationKey == notificationKey, ct);

    public async Task<IReadOnlyList<Notification>> FindByOriginAsync(OriginType originType, Guid originId, CancellationToken ct)
    {
        var stored = await db.Notifications.Where(n => n.OriginType == originType && n.OriginId == originId).ToListAsync(ct);
        var pending = db.Notifications.Local.Where(n => n.OriginType == originType && n.OriginId == originId && !stored.Contains(n));
        return stored.Concat(pending).ToList();
    }

    public async Task<IReadOnlyList<Notification>> FindDueRemindersAsync(DateTime nowUtc, int limit, CancellationToken ct) =>
        await db.Notifications.Where(n => n.State == NotificationState.Programada && n.ScheduledFor <= nowUtc)
            .OrderBy(n => n.ScheduledFor).Take(limit).ToListAsync(ct);

    public async Task<IReadOnlyList<Notification>> FindPendingEmailsAsync(DateTime nowUtc, int limit, CancellationToken ct)
    {
        var candidates = await db.Notifications
            .Where(n => n.EmailState == EmailState.Pendiente && (n.State == NotificationState.Disponible || n.State == NotificationState.Leida))
            .OrderBy(n => n.LastEmailAttemptAt).ThenBy(n => n.CreatedAt).Take(limit * 2).ToListAsync(ct);
        // Reintentos espaciados: 0, 1 min, 5 min, 30 min y 2 h según los intentos ya realizados.
        return candidates.Where(n => n.LastEmailAttemptAt is null
                || n.LastEmailAttemptAt.Value + EmailBackoff[Math.Min(n.EmailAttempts, EmailBackoff.Length - 1)] <= nowUtc)
            .Take(limit).ToList();
    }

    public void Add(Notification notification) => db.Notifications.Add(notification);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct) => db.ProcessedEvents.AnyAsync(p => p.EventId == eventId, ct);

    public void MarkProcessed(Guid eventId, string consumer, Guid aggregateId, long aggregateVersion, DateTime processedAtUtc) =>
        db.ProcessedEvents.Add(new ProcessedEvent { EventId = eventId, Consumer = consumer, AggregateId = aggregateId, AggregateVersion = aggregateVersion, ProcessedAt = processedAtUtc });
}
