using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using LendUp.Notifications.Api.Infrastructure.Security;
using LendUp.Notifications.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LendUp.Notifications.Tests.Contracts;

/// <summary>API sin base de datos ni broker: repositorio en memoria, token e Identidad falsos.</summary>
public sealed class InMemoryApiFactory : WebApplicationFactory<Program>
{
    public InMemoryNotificationRepository Repository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("FIREBASE_PROJECT_ID", "test-project");
        builder.UseSetting("RABBITMQ_ENABLED", "false");
        builder.UseSetting("REMINDER_SCHEDULER_ENABLED", "false");
        builder.UseSetting("DB_MIGRATE_ON_STARTUP", "false");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ITokenVerifier, FakeTokenVerifier>();
            services.AddSingleton<IIdentityPort, FakeIdentity>();
            services.AddSingleton<INotificationRepository>(Repository);
            services.AddSingleton<IInboxStore>(Repository);
        });
    }
}

/// <summary>
/// Contrato con el frontend (services/notifications.service.ts y mapNotification): mismas rutas, campos snake_case,
/// códigos y formato de error {status, error, message} del monolito.
/// </summary>
public class FrontendContractTests(InMemoryApiFactory factory) : IClassFixture<InMemoryApiFactory>
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private HttpClient Client(string? uid = "prestatario")
    {
        var client = factory.CreateClient();
        if (uid is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", uid);
        return client;
    }

    private Notification Seed(string uid, bool reminder = false)
    {
        var origin = Guid.NewGuid();
        var n = reminder
            ? Notification.ForReminder(FakeIdentity.UserFor(uid), $"RecordatorioEntrega:{origin}", OriginType.Reserva, origin, "evt", "Recordatorio de entrega", "Mañana", true, Now.AddDays(1), Now)
            : Notification.ForEvent(FakeIdentity.UserFor(uid), $"ReservaConfirmada:{origin}", OriginType.Reserva, origin, "evt", "Reserva confirmada", "Tu solicitud fue aceptada", true, Now);
        factory.Repository.Stored.Add(n);
        return n;
    }

    [Fact]
    public async Task ListaDevuelveLosCamposQueLeeElMapperSinRecordatoriosFuturos()
    {
        var uid = $"lista-{Guid.NewGuid():N}";
        var visible = Seed(uid);
        Seed(uid, reminder: true);
        Seed("otro-usuario");
        var response = await Client(uid).GetAsync("/api/v1/notificaciones");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(visible.Id.ToString(), row.GetProperty("id").GetString());
        Assert.Equal(FakeIdentity.UserFor(uid).ToString(), row.GetProperty("destinatario_usuario_id").GetString());
        Assert.Equal("RESERVA", row.GetProperty("origen_tipo").GetString());
        Assert.Equal(visible.OriginId.ToString(), row.GetProperty("origen_id").GetString());
        Assert.Equal("Reserva confirmada", row.GetProperty("titulo").GetString());
        Assert.Equal("Tu solicitud fue aceptada", row.GetProperty("mensaje").GetString());
        Assert.Equal("DISPONIBLE", row.GetProperty("estado").GetString());
        Assert.Equal("EVENTO", row.GetProperty("tipo").GetString());
        Assert.Equal("2026-11-08T12:00:00Z", row.GetProperty("creada_en").GetString());
        foreach (var field in new[] { "clave_notificacion", "evento_origen_id", "programada_para", "disponible_en", "leida_en", "cancelada_en", "enviar_correo",
                     "estado_correo", "intentos_correo", "ultimo_intento_correo_en", "correo_enviado_en", "ultimo_error_correo", "actualizada_en" })
            Assert.True(row.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task PatchMarcaLeidaYDevuelveLaFila()
    {
        var uid = $"leer-{Guid.NewGuid():N}";
        var n = Seed(uid);
        var response = await Client(uid).PatchAsync($"/api/v1/notificaciones/{n.Id}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("LEIDA", json.RootElement.GetProperty("estado").GetString());
        Assert.NotEqual(JsonValueKind.Null, json.RootElement.GetProperty("leida_en").ValueKind);
    }

    [Fact]
    public async Task PatchErroresConFormatoDelMonolito()
    {
        var uid = $"errores-{Guid.NewGuid():N}";
        var n = Seed(uid);
        await AssertError(await Client(uid).PatchAsync($"/api/v1/notificaciones/{Guid.NewGuid()}", null), 404, "NO_ENCONTRADO", "No encontrado");
        await AssertError(await Client("intruso").PatchAsync($"/api/v1/notificaciones/{n.Id}", null), 403, "NO_AUTORIZADO", "Operación no permitida");
        await Client(uid).PatchAsync($"/api/v1/notificaciones/{n.Id}", null);
        await AssertError(await Client(uid).PatchAsync($"/api/v1/notificaciones/{n.Id}", null), 409, "CONFLICTO_NEGOCIO", "Aviso no disponible");
    }

    [Fact]
    public async Task SeguridadExigeTokenValidoYCorreoVerificado()
    {
        await AssertError(await Client(null).GetAsync("/api/v1/notificaciones"), 401, "AUTENTICACION_REQUERIDA", "Autenticación requerida");
        await AssertError(await Client("invalido").GetAsync("/api/v1/notificaciones"), 401, "TOKEN_INVALIDO", "Token Firebase no válido");
        await AssertError(await Client("sin-verificar").GetAsync("/api/v1/notificaciones"), 403, "EMAIL_NOT_VERIFIED", "Verifica tu correo para ingresar a LendUp");
        var basic = factory.CreateClient();
        basic.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");
        Assert.Equal(HttpStatusCode.Unauthorized, (await basic.GetAsync("/api/v1/notificaciones")).StatusCode);
    }

    [Fact]
    public async Task LivenessEsPublicoYPropagaCorrelationId()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "req-123");
        var response = await client.GetAsync("/actuator/health/liveness");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"UP\"", await response.Content.ReadAsStringAsync());
        Assert.Equal("req-123", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    private static async Task AssertError(HttpResponseMessage response, int status, string error, string message)
    {
        Assert.Equal(status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(error, json.RootElement.GetProperty("error").GetString());
        Assert.Equal(message, json.RootElement.GetProperty("message").GetString());
    }
}
