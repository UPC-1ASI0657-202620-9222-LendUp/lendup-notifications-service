using System.Globalization;
using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Api.Application;

public sealed record NotificationDraft(Guid RecipientUserId, string NotificationKey, OriginType OriginType, Guid OriginId, string Title, string Message);

public sealed record ReservationSchedule(Guid ReservationId, Guid LenderUserId, Guid BorrowerUserId, DateTime FromLocal, DateTime ToLocal);

/// <summary>Qué hacer con un evento: avisos a crear, recordatorios a programar o una reserva cancelada.</summary>
public sealed record EventPlan(IReadOnlyList<NotificationDraft> Notifications, ReservationSchedule? Schedule = null, Guid? CancelledReservationId = null)
{
    public static readonly EventPlan Ignore = new(Array.Empty<NotificationDraft>());
}

/// <summary>
/// Traduce cada evento consumido a avisos en español con el mismo tono del monolito. La clave de notificación es
/// "&lt;tipo de evento&gt;:&lt;entidad&gt;", única por destinatario, de modo que un mismo hecho no genera dos avisos.
/// </summary>
public sealed class EventNotificationCatalog(EventRoutingOptions routes)
{
    public EventPlan Plan(IntegrationEvent e)
    {
        var key = e.RoutingKey;
        if (key == EventRoutingOptions.RequestCreated)
        {
            var request = e.Id("solicitud_id");
            return Of(Draft(e.Id("prestamista_usuario_id"), "SolicitudCreada", request, OriginType.Solicitud, "Nueva solicitud", "Recibiste una solicitud para tu objeto"));
        }
        if (key == EventRoutingOptions.ReservationConfirmed)
        {
            var reservation = e.Id("reserva_id");
            var lender = e.Id("prestamista_usuario_id");
            var borrower = e.Id("prestatario_usuario_id");
            return new EventPlan(
                [
                    Draft(borrower, "ReservaConfirmada", reservation, OriginType.Reserva, "Reserva confirmada", "Tu solicitud fue aceptada"),
                    Draft(lender, "ReservaConfirmada", reservation, OriginType.Reserva, "Reserva confirmada", "Confirmaste una reserva"),
                ],
                new ReservationSchedule(reservation, lender, borrower, e.LocalDateTime("desde"), e.LocalDateTime("hasta")));
        }
        if (key == EventRoutingOptions.ReservationCancelled)
        {
            // El evento de Reservas solo trae reserva_id: los destinatarios salen de los avisos de esa reserva.
            return new EventPlan(Array.Empty<NotificationDraft>(), CancelledReservationId: e.Id("reserva_id"));
        }
        if (key.StartsWith("pagos.", StringComparison.Ordinal)) return PaymentPlan(e);
        if (key == routes.LoanActivated)
            return Both(e, "PrestamoActivado", e.Id("prestamo_id"), OriginType.Prestamo, "Préstamo activo", "El prestatario confirmó la recepción y el préstamo está activo.");
        if (key == routes.ExtensionAccepted)
            return Both(e, "ExtensionAceptada", e.Id("cambio_fecha_prestamo_id"), OriginType.Extension, "Extensión aceptada", "La extensión del préstamo fue aceptada.");
        if (key == routes.LoanFinished)
            return Both(e, "PrestamoFinalizado", e.Id("prestamo_id"), OriginType.Prestamo, "Préstamo finalizado", "El préstamo finalizó correctamente.");
        if (key == routes.IncidentReported)
            return Both(e, "IncidenciaReportada", e.Id("incidencia_id"), OriginType.Incidencia, "Incidencia reportada", "Se ha reportado una incidencia en tu préstamo.");
        if (key == routes.IncidentResolved)
            return Both(e, "IncidenciaResuelta", e.Id("incidencia_id"), OriginType.Incidencia, "Incidencia resuelta", e.OptionalText("justificacion_resolucion") ?? "Un administrador resolvió la incidencia de tu préstamo.");
        return EventPlan.Ignore;
    }

    private static EventPlan PaymentPlan(IntegrationEvent e)
    {
        switch (e.EventType)
        {
            case "PagoConfirmado":
            {
                var tx = e.Id("transaccion_id");
                var extension = e.OptionalText("tipo") == "PAGO_EXTENSION";
                return Of(
                    Draft(e.Id("prestatario_usuario_id"), e.EventType, tx, OriginType.Pago, extension ? "Pago de extensión confirmado" : "Pago confirmado",
                        $"Mercado Pago confirmó tu pago de {Money(e, "monto")}."),
                    Draft(e.Id("prestamista_usuario_id"), e.EventType, tx, OriginType.Pago, extension ? "Extensión pagada" : "Pago recibido",
                        extension ? "El prestatario pagó la extensión del préstamo." : "El prestatario pagó la tarifa del préstamo."));
            }
            case "PagoRechazado":
                return Of(Draft(e.Id("prestatario_usuario_id"), e.EventType, e.Id("transaccion_id"), OriginType.Pago, "Pago no confirmado",
                    "Mercado Pago no confirmó tu pago. Puedes intentarlo nuevamente."));
            case "GarantiaConstituida":
            {
                var guarantee = e.Id("garantia_id");
                return Of(
                    Draft(e.Id("prestatario_usuario_id"), e.EventType, guarantee, OriginType.Garantia, "Garantía constituida",
                        $"Tu garantía de {Money(e, "monto_constituido")} quedó constituida."),
                    Draft(e.Id("prestamista_usuario_id"), e.EventType, guarantee, OriginType.Garantia, "Garantía constituida",
                        "El prestatario constituyó la garantía del préstamo."));
            }
            case "TarifaLiberada":
                return Of(Draft(e.Id("prestamista_usuario_id"), e.EventType, e.Id("transaccion_id"), OriginType.Pago, "Tarifa liberada",
                    $"Se liberó la tarifa del préstamo: {Money(e, "monto_neto_prestamista")}."));
            case "GarantiaDevuelta":
                return Of(Draft(e.Id("prestatario_usuario_id"), $"{e.EventType}:{e.Text("transaccion_id")}", e.Id("garantia_id"), OriginType.Garantia,
                    "Garantía devuelta", $"Se devolvieron {Money(e, "monto_devuelto")} de tu garantía."));
            case "GarantiaRetenida":
            {
                var guarantee = e.Id("garantia_id");
                var keyType = $"{e.EventType}:{e.Text("transaccion_id")}";
                var message = $"Se retuvieron {Money(e, "monto_retenido")} de la garantía por la resolución de una incidencia.";
                return Of(
                    Draft(e.Id("prestatario_usuario_id"), keyType, guarantee, OriginType.Garantia, "Garantía retenida", message),
                    Draft(e.Id("prestamista_usuario_id"), keyType, guarantee, OriginType.Garantia, "Garantía retenida", message));
            }
            case "ReembolsoProcesado":
                return Of(Draft(e.Id("prestatario_usuario_id"), e.EventType, e.Id("transaccion_id"), OriginType.Pago, "Reembolso procesado",
                    $"Se reembolsaron {Money(e, "monto")} por la cancelación de la reserva."));
            default:
                return EventPlan.Ignore;
        }
    }

    private static EventPlan Both(IntegrationEvent e, string type, Guid originId, OriginType origin, string title, string message) =>
        Of(Draft(e.Id("prestatario_usuario_id"), type, originId, origin, title, message),
           Draft(e.Id("prestamista_usuario_id"), type, originId, origin, title, message));

    private static EventPlan Of(params NotificationDraft[] drafts) => new(drafts);

    private static NotificationDraft Draft(Guid recipient, string keyType, Guid originId, OriginType origin, string title, string message)
    {
        var key = keyType.Contains(':') ? keyType : $"{keyType}:{originId}";
        return new NotificationDraft(recipient, key, origin, originId, title, message);
    }

    private static string Money(IntegrationEvent e, string amountField)
    {
        var amount = e.OptionalAmount(amountField) ?? 0m;
        return $"{amount.ToString("0.00", CultureInfo.InvariantCulture)} {e.OptionalText("moneda") ?? "PEN"}";
    }
}
