using LendUp.Notifications.Api.Application;
using Microsoft.AspNetCore.Mvc;

namespace LendUp.Notifications.Api.Interfaces.Rest;

/// <summary>NotificacionController (Fig. 20): mismo contrato que el monolito.</summary>
[ApiController]
[Route("api/v1/notificaciones")]
[Produces("application/json")]
[Tags("Notificaciones")]
public sealed class NotificationsController(NotificationApplicationService notifications, ActorContext actor) : ControllerBase
{
    /// <summary>Avisos visibles (DISPONIBLE y LEIDA) del estudiante autenticado, del más reciente al más antiguo (máx. 200).</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<NotificationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<Infrastructure.Security.ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IEnumerable<NotificationResponse>> List(CancellationToken ct)
    {
        var userId = await actor.UserIdAsync(ct);
        return (await notifications.ListAsync(userId, ct)).Select(NotificationResponse.From);
    }

    /// <summary>Marca el aviso como LEIDA. 404 si no existe, 403 si es de otro estudiante, 409 si no está DISPONIBLE.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Infrastructure.Security.ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Infrastructure.Security.ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<Infrastructure.Security.ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<NotificationResponse> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = await actor.UserIdAsync(ct);
        return NotificationResponse.From(await notifications.MarkReadAsync(userId, id, ct));
    }
}
