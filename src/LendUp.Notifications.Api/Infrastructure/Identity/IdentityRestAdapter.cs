using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Infrastructure.Configuration;

namespace LendUp.Notifications.Api.Infrastructure.Identity;

/// <summary>
/// Contratos con Identidad, todavía pendientes y configurables (igual que en Reservas y Pagos):
/// IDENTITY_FIREBASE_USER_URI_TEMPLATE ({uid}) → {"usuario_id"} o {"usuario":{"id"}} (monolito /estudiantes/me),
/// IDENTITY_USER_EMAIL_URI_TEMPLATE ({usuario_id}) → {"correo"} o {"correo_institucional"}.
/// </summary>
public sealed class IdentityRestAdapter(HttpClient http, IdentitySettings settings) : IIdentityPort
{
    public async Task<Guid> ResolveUserIdAsync(string firebaseUid, string bearerToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.FirebaseUserUriTemplate))
            throw new DependencyUnavailableException("Integración de resolución de identidad con Identidad no configurada");
        var token = string.IsNullOrWhiteSpace(settings.ServiceToken) ? bearerToken : settings.ServiceToken;
        if (string.IsNullOrWhiteSpace(token)) throw new DependencyUnavailableException("No hay credencial para consultar Identidad");
        using var document = await GetAsync(Expand(settings.FirebaseUserUriTemplate, firebaseUid), token, ct, forbiddenMeansNoProfile: true);
        var root = document.RootElement;
        var id = root.TryGetProperty("usuario_id", out var direct) ? direct.GetString()
            : root.TryGetProperty("usuario", out var user) && user.TryGetProperty("id", out var nested) ? nested.GetString() : null;
        return Guid.TryParse(id, out var userId) ? userId : throw new DependencyUnavailableException("Identidad no vinculó el token a un usuario interno");
    }

    public async Task<string?> TryGetEmailAsync(Guid userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.UserEmailUriTemplate) || string.IsNullOrWhiteSpace(settings.ServiceToken))
            return null;
        using var document = await GetAsync(Expand(settings.UserEmailUriTemplate, userId.ToString()), settings.ServiceToken, ct, forbiddenMeansNoProfile: false);
        var root = document.RootElement;
        var email = root.TryGetProperty("correo", out var c) ? c.GetString() : root.TryGetProperty("correo_institucional", out var ci) ? ci.GetString() : null;
        return string.IsNullOrWhiteSpace(email) ? throw new DependencyUnavailableException("Identidad no entregó el correo del usuario") : email;
    }

    private async Task<JsonDocument> GetAsync(string path, string token, CancellationToken ct, bool forbiddenMeansNoProfile)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { throw new DependencyUnavailableException("Identidad no disponible", e); }
        using (response)
        {
            if (forbiddenMeansNoProfile && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                throw new ForbiddenException("Registra tu perfil en LendUp");
            if (!response.IsSuccessStatusCode) throw new DependencyUnavailableException($"Identidad respondió {(int)response.StatusCode}");
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
    }

    private static string Expand(string template, string value) =>
        template.Replace("{uid}", Uri.EscapeDataString(value)).Replace("{usuario_id}", Uri.EscapeDataString(value));
}
