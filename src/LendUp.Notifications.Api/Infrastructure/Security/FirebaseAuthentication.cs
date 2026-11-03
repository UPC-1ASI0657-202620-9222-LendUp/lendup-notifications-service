using System.Security.Claims;
using System.Text.Encodings.Web;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using LendUp.Notifications.Api.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LendUp.Notifications.Api.Infrastructure.Security;

public sealed record VerifiedToken(string Uid, bool EmailVerified, bool Administrator);

public interface ITokenVerifier
{
    Task<VerifiedToken> VerifyAsync(string idToken, CancellationToken ct);
}

/// <summary>Valida el Firebase ID token en el propio servicio, incluida la revocación (igual que Reservas).</summary>
public sealed class FirebaseTokenVerifier : ITokenVerifier
{
    private readonly FirebaseAuth _auth;

    public FirebaseTokenVerifier(FirebaseSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProjectId)) throw new InvalidOperationException("FIREBASE_PROJECT_ID es obligatorio");
        var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
        {
            // Application Default Credentials (GOOGLE_APPLICATION_CREDENTIALS); no existe bypass de producción.
            Credential = GoogleCredential.GetApplicationDefault(),
            ProjectId = settings.ProjectId,
        });
        if (app.Options.ProjectId != settings.ProjectId) throw new InvalidOperationException("El Project ID de Firebase no coincide con la aplicación inicializada");
        _auth = FirebaseAuth.GetAuth(app);
    }

    public async Task<VerifiedToken> VerifyAsync(string idToken, CancellationToken ct)
    {
        var token = await _auth.VerifyIdTokenAsync(idToken, checkRevoked: true, ct);
        var emailVerified = token.Claims.TryGetValue("email_verified", out var verified) && verified is true;
        var administrator = token.Claims.TryGetValue("role", out var role) && "ADMINISTRADOR".Equals(role as string)
            || token.Claims.TryGetValue("admin", out var admin) && admin is true;
        return new VerifiedToken(token.Uid, emailVerified, administrator);
    }
}

/// <summary>
/// Esquema "Firebase": sin header no autentica (la política decide 401); token inválido → 401 TOKEN_INVALIDO;
/// correo no verificado → 403 EMAIL_NOT_VERIFIED. Los roles nunca se leen de headers públicos.
/// </summary>
public sealed class FirebaseAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ITokenVerifier verifier) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Firebase";
    public const string RawTokenItem = "lendup.raw_token";
    private const string ErrorItem = "lendup.auth_error";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var raw = header["Bearer ".Length..].Trim();
        VerifiedToken token;
        try { token = await verifier.VerifyAsync(raw, Context.RequestAborted); }
        catch (Exception)
        {
            Context.Items[ErrorItem] = "TOKEN_INVALIDO";
            return AuthenticateResult.Fail("Token Firebase no válido");
        }
        if (!token.EmailVerified)
        {
            Context.Items[ErrorItem] = "EMAIL_NOT_VERIFIED";
            return AuthenticateResult.Fail("Correo no verificado");
        }
        Context.Items[RawTokenItem] = raw;
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, token.Uid) };
        if (token.Administrator) claims.Add(new Claim(ClaimTypes.Role, "ADMINISTRADOR"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Context.Items[ErrorItem] switch
        {
            "EMAIL_NOT_VERIFIED" => ErrorWriter.WriteAsync(Response, 403, "EMAIL_NOT_VERIFIED", "Verifica tu correo para ingresar a LendUp"),
            "TOKEN_INVALIDO" => ErrorWriter.WriteAsync(Response, 401, "TOKEN_INVALIDO", "Token Firebase no válido"),
            _ => ErrorWriter.WriteAsync(Response, 401, "AUTENTICACION_REQUERIDA", "Autenticación requerida"),
        };

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ErrorWriter.WriteAsync(Response, 403, "NO_AUTORIZADO", "No autorizado");
}

/// <summary>Formato de error común: {status, error, message, timestamp}.</summary>
public static class ErrorWriter
{
    public static Task WriteAsync(HttpResponse response, int status, string error, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new ErrorResponse(status, error, message, DateTime.UtcNow));
    }
}

public sealed record ErrorResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
    [property: System.Text.Json.Serialization.JsonPropertyName("error")] string Error,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message,
    [property: System.Text.Json.Serialization.JsonPropertyName("timestamp")] DateTime Timestamp);
