using System.Security.Claims;
using System.Text.RegularExpressions;
using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Diagnostics;

namespace LendUp.Notifications.Api.Interfaces.Rest;

/// <summary>Resuelve el usuario interno a partir del UID verificado; nunca confía en ids del body o de headers.</summary>
public sealed class ActorContext(IHttpContextAccessor accessor, IIdentityPort identity)
{
    public Task<Guid> UserIdAsync(CancellationToken ct)
    {
        var context = accessor.HttpContext ?? throw new ForbiddenException("Identidad no autenticada");
        var uid = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ForbiddenException("Identidad no autenticada");
        var raw = context.Items[FirebaseAuthenticationHandler.RawTokenItem] as string ?? "";
        return identity.ResolveUserIdAsync(uid, raw, ct);
    }
}

/// <summary>Traduce excepciones al formato de error de Reservas y Pagos.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, message) = exception switch
        {
            NotFoundException e => (404, "NO_ENCONTRADO", e.Message),
            ForbiddenException e => (403, "NO_AUTORIZADO", e.Message),
            BusinessConflictException e => (409, "CONFLICTO_NEGOCIO", e.Message),
            DependencyUnavailableException e => (503, "DEPENDENCIA_NO_DISPONIBLE", e.Message),
            BadHttpRequestException or ArgumentException => (400, "SOLICITUD_INVALIDA", Safe(exception.Message)),
            _ => (500, "ERROR_INTERNO", "Error interno"),
        };
        if (status == 500) logger.LogError(exception, "Error no controlado");
        await ErrorWriter.WriteAsync(context.Response, status, code, message);
        return true;
    }

    private static string Safe(string message) => message.Length > 300 ? message[..300] : message;
}

/// <summary>Propaga X-Correlation-ID igual que los servicios Java y lo agrega al scope de logs.</summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(id) || !ValidId().IsMatch(id)) id = Guid.NewGuid().ToString();
        context.Response.Headers[Header] = id;
        using (logger.BeginScope(new Dictionary<string, object> { ["correlation_id"] = id }))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex ValidId();
}
