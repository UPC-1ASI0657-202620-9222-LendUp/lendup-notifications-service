using Testcontainers.MySql;
using Testcontainers.RabbitMq;

namespace LendUp.Notifications.Tests.Integration;

/// <summary>Las pruebas de integración se omiten (no fallan) si no hay Docker; una omisión no equivale a éxito.</summary>
public static class DockerAvailability
{
    public static bool IsAvailable { get; } = Detect();

    private static bool Detect()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST"))) return true;
        if (!OperatingSystem.IsWindows()) return File.Exists("/var/run/docker.sock");
        try
        {
            return Directory.GetFiles(@"\\.\pipe\").Any(p => p.EndsWith("docker_engine", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("dockerDesktopLinuxEngine", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException) { return false; }
    }
}

/// <summary>MySQL 8.4 y RabbitMQ reales compartidos por las pruebas de integración.</summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    public MySqlContainer? MySql { get; private set; }
    public RabbitMqContainer? RabbitMq { get; private set; }

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable) return;
        MySql = new MySqlBuilder("mysql:8.4").WithDatabase("lendup_notificaciones").WithUsername("test").WithPassword("test").Build();
        RabbitMq = new RabbitMqBuilder("rabbitmq:4.2-management-alpine").WithUsername("test").WithPassword("test").Build();
        await Task.WhenAll(MySql.StartAsync(), RabbitMq.StartAsync());
    }

    public async Task DisposeAsync()
    {
        if (MySql is not null) await MySql.DisposeAsync();
        if (RabbitMq is not null) await RabbitMq.DisposeAsync();
    }
}
