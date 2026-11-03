using System.Text.Json;
using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using LendUp.Notifications.Api.Infrastructure.Configuration;
using LendUp.Notifications.Api.Infrastructure.Email;
using LendUp.Notifications.Api.Infrastructure.Health;
using LendUp.Notifications.Api.Infrastructure.Identity;
using LendUp.Notifications.Api.Infrastructure.Messaging;
using LendUp.Notifications.Api.Infrastructure.Persistence;
using LendUp.Notifications.Api.Infrastructure.Scheduling;
using LendUp.Notifications.Api.Infrastructure.Security;
using LendUp.Notifications.Api.Interfaces.Rest;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SendGrid;

var builder = WebApplication.CreateBuilder(args);
var settings = ServiceSettings.From(builder.Configuration);
builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Web.Port}");
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.UseUtcTimestamp = true; });
}

// Configuración
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(settings.Database);
builder.Services.AddSingleton(settings.RabbitMq);
builder.Services.AddSingleton(settings.Firebase);
builder.Services.AddSingleton(settings.Identity);
builder.Services.AddSingleton(settings.SendGrid);
builder.Services.AddSingleton(settings.Scheduler);
builder.Services.AddSingleton(settings.Notifications);
builder.Services.AddSingleton(settings.Routes);
builder.Services.AddSingleton(TimeProvider.System);

// Persistencia
builder.Services.AddDbContext<NotificationsDbContext>(o =>
    o.UseMySql(settings.Database.ConnectionString, MySqlVersion.Server, mysql => mysql.EnableRetryOnFailure(3)));
builder.Services.AddScoped<NotificationRepository>();
builder.Services.AddScoped<INotificationRepository>(sp => sp.GetRequiredService<NotificationRepository>());
builder.Services.AddScoped<IInboxStore>(sp => sp.GetRequiredService<NotificationRepository>());

// Aplicación
builder.Services.AddSingleton<EventNotificationCatalog>();
builder.Services.AddSingleton(new ReminderPolicy(settings.Notifications.DeliveryReminderLead, settings.Notifications.ReturnReminderLead));
builder.Services.AddScoped<ReminderApplicationService>();
builder.Services.AddScoped<NotificationApplicationService>();
builder.Services.AddScoped<EventProcessor>();

// Integraciones
builder.Services.AddHttpClient<IIdentityPort, IdentityRestAdapter>(client =>
    client.Timeout = TimeSpan.FromMilliseconds(settings.Identity.ConnectTimeoutMs + settings.Identity.ReadTimeoutMs));
if (string.IsNullOrWhiteSpace(settings.SendGrid.ApiKey))
{
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
}
else
{
    builder.Services.AddSingleton<ISendGridClient>(new SendGridClient(settings.SendGrid.ApiKey));
    builder.Services.AddSingleton<IEmailSender, SendGridEmailAdapter>();
}
builder.Services.AddSingleton<MessagingStatus>();
if (settings.RabbitMq.Enabled) builder.Services.AddHostedService<MessageBrokerAdapter>();
if (settings.Scheduler.Enabled) builder.Services.AddHostedService<ReminderScheduler>();

// Seguridad
builder.Services.AddSingleton<ITokenVerifier>(_ => new FirebaseTokenVerifier(settings.Firebase));
builder.Services.AddAuthentication(FirebaseAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>(FirebaseAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(settings.Web.CorsOrigins)
    .WithMethods("GET", "PATCH", "OPTIONS").WithHeaders("Authorization", "Content-Type", CorrelationIdMiddleware.Header)
    .WithExposedHeaders(CorrelationIdMiddleware.Header).SetPreflightMaxAge(TimeSpan.FromHours(1))));

// Web
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ActorContext>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
    new BadRequestObjectResult(new ErrorResponse(400, "SOLICITUD_INVALIDA", "Solicitud inválida", DateTime.UtcNow)));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("mysql", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
if (settings.Web.SwaggerEnabled) builder.Services.AddNotificationsSwagger();

var app = builder.Build();

// Falla rápido si Firebase no está configurado (no hay bypass de producción).
app.Services.GetRequiredService<ITokenVerifier>();
if (settings.Database.MigrateOnStartup)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/actuator/health/liveness", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealth }).AllowAnonymous();
app.MapHealthChecks("/actuator/health/readiness", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteHealth }).AllowAnonymous();
if (settings.Web.SwaggerEnabled)
{
    var swagger = app.MapSwagger();
    if (settings.Web.SwaggerPublic) swagger.AllowAnonymous();
    app.UseSwaggerUI();
}

await app.RunAsync();

// Mismo cuerpo que Spring Boot Actuator: {"status":"UP"} o {"status":"DOWN"}.
static Task WriteHealth(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status == HealthStatus.Healthy ? "UP" : "DOWN" }));
}

public partial class Program;
