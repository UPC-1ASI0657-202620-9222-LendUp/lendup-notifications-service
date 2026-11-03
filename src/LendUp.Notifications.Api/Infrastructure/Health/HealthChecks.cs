using LendUp.Notifications.Api.Infrastructure.Configuration;
using LendUp.Notifications.Api.Infrastructure.Messaging;
using LendUp.Notifications.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LendUp.Notifications.Api.Infrastructure.Health;

public sealed class DatabaseHealthCheck(NotificationsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("MySQL no disponible");
}

public sealed class RabbitMqHealthCheck(MessagingStatus status, RabbitMqSettings settings) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(!settings.Enabled || status.Connected ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Consumidor RabbitMQ desconectado"));
}
