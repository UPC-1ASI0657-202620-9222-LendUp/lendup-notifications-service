using System.Globalization;
using LendUp.Notifications.Api.Application;

namespace LendUp.Notifications.Api.Infrastructure.Configuration;

public sealed record DatabaseSettings(string Host, int Port, string Name, string User, string Password, bool MigrateOnStartup)
{
    public string ConnectionString =>
        $"Server={Host};Port={Port};Database={Name};User={User};Password={Password};SslMode=Preferred;AllowPublicKeyRetrieval=True;ConnectionTimeout=3";
}

public sealed record RabbitMqSettings(bool Enabled, string Host, int Port, string User, string Password);

public sealed record FirebaseSettings(string ProjectId);

public sealed record IdentitySettings(string BaseUrl, string ServiceToken, string FirebaseUserUriTemplate, string UserEmailUriTemplate, int ConnectTimeoutMs, int ReadTimeoutMs);

public sealed record SendGridSettings(string ApiKey, string FromEmail, string FromName);

public sealed record SchedulerSettings(bool Enabled, TimeSpan Delay, int BatchSize);

public sealed record WebSettings(int Port, string[] CorsOrigins, bool SwaggerEnabled, bool SwaggerPublic);

/// <summary>
/// Configuración externalizada con los mismos nombres de variables de entorno que los servicios Java
/// (SERVER_PORT, DB_*, RABBITMQ_*, FIREBASE_PROJECT_ID, IDENTITY_*, ...). No hay secretos con valor por defecto.
/// </summary>
public sealed record ServiceSettings(
    WebSettings Web, DatabaseSettings Database, RabbitMqSettings RabbitMq, FirebaseSettings Firebase, IdentitySettings Identity,
    SendGridSettings SendGrid, SchedulerSettings Scheduler, NotificationOptions Notifications, EventRoutingOptions Routes)
{
    public static ServiceSettings From(IConfiguration c)
    {
        string S(string key, string fallback = "") => string.IsNullOrWhiteSpace(c[key]) ? fallback : c[key]!.Trim();
        int I(string key, int fallback) => int.TryParse(c[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        double D(string key, double fallback) => double.TryParse(c[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        bool B(string key, bool fallback) => bool.TryParse(c[key], out var v) ? v : fallback;

        return new ServiceSettings(
            new WebSettings(I("SERVER_PORT", 8083), S("CORS_ORIGINS").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                B("SWAGGER_ENABLED", false), B("SWAGGER_PUBLIC", false)),
            new DatabaseSettings(S("DB_HOST", "localhost"), I("DB_PORT", 3312), S("DB_NAME", "lendup_notificaciones"), S("DB_USERNAME", "lendup_notificaciones"),
                S("DB_PASSWORD"), B("DB_MIGRATE_ON_STARTUP", true)),
            new RabbitMqSettings(B("RABBITMQ_ENABLED", true), S("RABBITMQ_HOST", "localhost"), I("RABBITMQ_PORT", 5676), S("RABBITMQ_USERNAME"), S("RABBITMQ_PASSWORD")),
            new FirebaseSettings(S("FIREBASE_PROJECT_ID")),
            new IdentitySettings(S("IDENTITY_SERVICE_BASE_URL"), S("IDENTITY_SERVICE_TOKEN"), S("IDENTITY_FIREBASE_USER_URI_TEMPLATE"),
                S("IDENTITY_USER_EMAIL_URI_TEMPLATE"), I("IDENTITY_CONNECT_TIMEOUT_MS", 2000), I("IDENTITY_READ_TIMEOUT_MS", 3000)),
            new SendGridSettings(S("SENDGRID_API_KEY"), S("SENDGRID_FROM_EMAIL"), S("SENDGRID_FROM_NAME", "LendUp")),
            new SchedulerSettings(B("REMINDER_SCHEDULER_ENABLED", true), TimeSpan.FromMilliseconds(I("REMINDER_SCHEDULER_DELAY_MS", 60000)), I("REMINDER_SCHEDULER_BATCH_SIZE", 100)),
            new NotificationOptions
            {
                DeliveryReminderLead = TimeSpan.FromHours(D("REMINDER_DELIVERY_LEAD_HOURS", 24)),
                ReturnReminderLead = TimeSpan.FromHours(D("REMINDER_RETURN_LEAD_HOURS", 24)),
                EmailMaxAttempts = I("EMAIL_MAX_ATTEMPTS", 5),
            },
            new EventRoutingOptions
            {
                LoanActivated = S("LOAN_ACTIVATED_ROUTING_KEY", "prestamos.activado.v1"),
                ExtensionAccepted = S("EXTENSION_ACCEPTED_ROUTING_KEY", "prestamos.extension-aceptada.v1"),
                LoanFinished = S("LOAN_FINISHED_ROUTING_KEY", "prestamos.finalizado.v1"),
                IncidentReported = S("INCIDENT_REPORTED_ROUTING_KEY", "evidencias.incidencia-reportada.v1"),
                IncidentResolved = S("INCIDENT_RESOLVED_ROUTING_KEY", "evidencias.incidencia-resuelta.v1"),
            });
    }
}
