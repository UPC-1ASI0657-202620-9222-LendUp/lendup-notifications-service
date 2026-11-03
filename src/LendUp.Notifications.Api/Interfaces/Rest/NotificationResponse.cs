using System.Text.Json.Serialization;
using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Api.Interfaces.Rest;

/// <summary>Fila de notificaciones con los mismos campos snake_case que devolvía el monolito.</summary>
public sealed record NotificationResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("destinatario_usuario_id")] Guid RecipientUserId,
    [property: JsonPropertyName("clave_notificacion")] string NotificationKey,
    [property: JsonPropertyName("tipo")] string Type,
    [property: JsonPropertyName("origen_tipo")] string OriginType,
    [property: JsonPropertyName("origen_id")] Guid OriginId,
    [property: JsonPropertyName("evento_origen_id")] string? SourceEventId,
    [property: JsonPropertyName("titulo")] string Title,
    [property: JsonPropertyName("mensaje")] string Message,
    [property: JsonPropertyName("estado")] string State,
    [property: JsonPropertyName("programada_para")] DateTime? ScheduledFor,
    [property: JsonPropertyName("disponible_en")] DateTime? AvailableAt,
    [property: JsonPropertyName("leida_en")] DateTime? ReadAt,
    [property: JsonPropertyName("cancelada_en")] DateTime? CancelledAt,
    [property: JsonPropertyName("enviar_correo")] bool SendEmail,
    [property: JsonPropertyName("estado_correo")] string EmailState,
    [property: JsonPropertyName("intentos_correo")] int EmailAttempts,
    [property: JsonPropertyName("ultimo_intento_correo_en")] DateTime? LastEmailAttemptAt,
    [property: JsonPropertyName("correo_enviado_en")] DateTime? EmailSentAt,
    [property: JsonPropertyName("ultimo_error_correo")] string? LastEmailError,
    [property: JsonPropertyName("creada_en")] DateTime CreatedAt,
    [property: JsonPropertyName("actualizada_en")] DateTime UpdatedAt)
{
    public static NotificationResponse From(Notification n) => new(
        n.Id, n.RecipientUserId, n.NotificationKey, EnumText.ToText(n.Type), EnumText.ToText(n.OriginType), n.OriginId, n.SourceEventId,
        n.Title, n.Message, EnumText.ToText(n.State), n.ScheduledFor, n.AvailableAt, n.ReadAt, n.CancelledAt, n.SendEmail,
        EnumText.ToText(n.EmailState), n.EmailAttempts, n.LastEmailAttemptAt, n.EmailSentAt, n.LastEmailError, n.CreatedAt, n.UpdatedAt);
}
