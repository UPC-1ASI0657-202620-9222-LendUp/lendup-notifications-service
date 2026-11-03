using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Infrastructure.Configuration;

namespace LendUp.Notifications.Api.Infrastructure.Scheduling;

/// <summary>
/// ReminderScheduler (Fig. 20): cada REMINDER_SCHEDULER_DELAY_MS activa los recordatorios vencidos (RF22) y envía
/// los correos pendientes. Un fallo en una vuelta no detiene las siguientes.
/// </summary>
public sealed class ReminderScheduler(IServiceScopeFactory scopes, SchedulerSettings settings, ILogger<ReminderScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Delay);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var activated = await scope.ServiceProvider.GetRequiredService<ReminderApplicationService>().ActivateDueAsync(settings.BatchSize, ct);
            var sent = await scope.ServiceProvider.GetRequiredService<NotificationApplicationService>().DispatchPendingEmailsAsync(settings.BatchSize, ct);
            if (activated > 0 || sent > 0) logger.LogInformation("Recordatorios activados: {Activated}; correos enviados: {Sent}", activated, sent);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning("Vuelta del ReminderScheduler fallida; se reintentará: {Error}", e.Message);
        }
    }
}
