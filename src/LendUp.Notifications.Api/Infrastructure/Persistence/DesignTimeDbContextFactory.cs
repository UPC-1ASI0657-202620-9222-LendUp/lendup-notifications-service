using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LendUp.Notifications.Api.Infrastructure.Persistence;

/// <summary>Solo para `dotnet ef migrations`: no abre conexión porque la versión del servidor es fija.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseMySql("Server=localhost;Database=lendup_notificaciones;User=design;Password=design", MySqlVersion.Server)
            .Options;
        return new NotificationsDbContext(options);
    }
}

public static class MySqlVersion
{
    public static readonly MySqlServerVersion Server = new(new Version(8, 4, 0));
}
