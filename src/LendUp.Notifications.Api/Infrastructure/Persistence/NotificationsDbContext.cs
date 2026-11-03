using LendUp.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LendUp.Notifications.Api.Infrastructure.Persistence;

/// <summary>Base propia lendup_notificaciones: tabla notificaciones (Fig. 13 + LEIDA) e inbox processed_events.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Notification>(e =>
        {
            e.ToTable("notificaciones", t =>
            {
                t.HasCheckConstraint("ck_notificaciones_tipo", "tipo IN ('EVENTO','RECORDATORIO')");
                t.HasCheckConstraint("ck_notificaciones_origen_tipo", "origen_tipo IN ('SOLICITUD','RESERVA','PRESTAMO','PAGO','GARANTIA','EXTENSION','INCIDENCIA')");
                t.HasCheckConstraint("ck_notificaciones_estado", "estado IN ('PROGRAMADA','DISPONIBLE','LEIDA','CANCELADA')");
                t.HasCheckConstraint("ck_notificaciones_estado_correo", "estado_correo IN ('NO_APLICA','PENDIENTE','ENVIADO','FALLIDO')");
            });
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasColumnName("id").HasColumnType("char(36)").ValueGeneratedNever();
            e.Property(n => n.RecipientUserId).HasColumnName("destinatario_usuario_id").HasColumnType("char(36)");
            e.Property(n => n.NotificationKey).HasColumnName("clave_notificacion").HasMaxLength(200).IsRequired();
            e.Property(n => n.Type).HasColumnName("tipo").HasMaxLength(20).HasConversion(EnumConverter<NotificationType>());
            e.Property(n => n.OriginType).HasColumnName("origen_tipo").HasMaxLength(20).HasConversion(EnumConverter<OriginType>());
            e.Property(n => n.OriginId).HasColumnName("origen_id").HasColumnType("char(36)");
            e.Property(n => n.SourceEventId).HasColumnName("evento_origen_id").HasMaxLength(150);
            e.Property(n => n.Title).HasColumnName("titulo").HasMaxLength(180).IsRequired();
            e.Property(n => n.Message).HasColumnName("mensaje").HasColumnType("text").IsRequired();
            e.Property(n => n.State).HasColumnName("estado").HasMaxLength(20).HasConversion(EnumConverter<NotificationState>());
            e.Property(n => n.ScheduledFor).HasColumnName("programada_para");
            e.Property(n => n.AvailableAt).HasColumnName("disponible_en");
            e.Property(n => n.ReadAt).HasColumnName("leida_en");
            e.Property(n => n.CancelledAt).HasColumnName("cancelada_en");
            e.Property(n => n.SendEmail).HasColumnName("enviar_correo");
            e.Property(n => n.EmailState).HasColumnName("estado_correo").HasMaxLength(20).HasConversion(EnumConverter<EmailState>());
            e.Property(n => n.EmailAttempts).HasColumnName("intentos_correo");
            e.Property(n => n.LastEmailAttemptAt).HasColumnName("ultimo_intento_correo_en");
            e.Property(n => n.EmailSentAt).HasColumnName("correo_enviado_en");
            e.Property(n => n.LastEmailError).HasColumnName("ultimo_error_correo").HasColumnType("text");
            e.Property(n => n.CreatedAt).HasColumnName("creada_en");
            e.Property(n => n.UpdatedAt).HasColumnName("actualizada_en");
            // Mismos índices que schema.sql del monolito.
            e.HasIndex(n => new { n.RecipientUserId, n.NotificationKey }).IsUnique().HasDatabaseName("uq_notificaciones_0");
            e.HasIndex(n => new { n.RecipientUserId, n.State, n.CreatedAt }).HasDatabaseName("idx_notificaciones_0");
            e.HasIndex(n => new { n.State, n.ScheduledFor }).HasDatabaseName("idx_notificaciones_1");
            e.HasIndex(n => new { n.EmailState, n.LastEmailAttemptAt }).HasDatabaseName("idx_notificaciones_2");
            e.HasIndex(n => new { n.OriginType, n.OriginId }).HasDatabaseName("idx_notificaciones_origen");
        });

        model.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("processed_events");
            e.HasKey(p => p.EventId);
            e.Property(p => p.EventId).HasColumnName("event_id").HasColumnType("char(36)").ValueGeneratedNever();
            e.Property(p => p.Consumer).HasColumnName("consumer").HasMaxLength(100).IsRequired();
            e.Property(p => p.AggregateId).HasColumnName("aggregate_id").HasColumnType("char(36)");
            e.Property(p => p.AggregateVersion).HasColumnName("aggregate_version");
            e.Property(p => p.ProcessedAt).HasColumnName("processed_at");
            e.HasIndex(p => new { p.AggregateId, p.AggregateVersion }).HasDatabaseName("idx_processed_aggregate");
        });

        // Todas las fechas son UTC: MySQL las devuelve sin Kind y se marcan como UTC al leer.
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTime)) property.SetValueConverter(UtcConverter);
                else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(NullableUtcConverter);
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) property.SetColumnType("datetime(6)");
            }
        }
    }

    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(v => DateTime.SpecifyKind(v, DateTimeKind.Utc), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter =
        new(v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    private static ValueConverter<TEnum, string> EnumConverter<TEnum>() where TEnum : struct, Enum =>
        new(v => EnumText.ToText(v), v => EnumText.Parse<TEnum>(v));
}

public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = "";
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public DateTime ProcessedAt { get; set; }
}
