using System.Globalization;
using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Api.Application;

public sealed record NotificationDraft(Guid RecipientUserId, string NotificationKey, OriginType OriginType, Guid OriginId, string Title, string Message);

public sealed record ReservationSchedule(Guid ReservationId, Guid LenderUserId, Guid BorrowerUserId, DateTime FromLocal, DateTime ToLocal);

/// <summary>
/// Aviso para quien ya recibió otro de la misma solicitud: Reservas publica el rechazo y la cancelación solo con
/// solicitud_id, así que el destinatario es quien tiene el aviso con la clave indicada.
/// </summary>
public sealed record RequestFollowUp(Guid RequestId, string KnownBy, string KeyType, string Title, string Message);

/// <summary>Cambios en los recordatorios de una reserva: los que ya no corresponden y la nueva fecha de devolución.</summary>
public sealed record ReminderChange(Guid ReservationId, bool CancelDelivery = false, bool CancelReturn = false, DateTime? NewReturnLocal = null, string? ChangeId = null);

/// <summary>Qué hacer con un evento: avisos a crear y cambios sobre recordatorios, reservas o solicitudes ya avisadas.</summary>
public sealed record EventPlan(IReadOnlyList<NotificationDraft> Notifications, ReservationSchedule? Schedule = null, Guid? CancelledReservationId = null,
    RequestFollowUp? Request = null, ReminderChange? Reminders = null)
{
    public static readonly EventPlan Ignore = new(Array.Empty<NotificationDraft>());
}

/// <summary>
/// Traduce cada evento consumido a avisos en español con el mismo tono del monolito. La clave de notificación es
/// "&lt;tipo de evento&gt;:&lt;entidad&gt;", única por destinatario, de modo que un mismo hecho no genera dos avisos.
/// </summary>
public sealed class EventNotificationCatalog(EventRoutingOptions routes)
{
    private const string RequestReceivedKey = "SolicitudCreada";
    private const string RequestSentKey = "SolicitudEnviada";

    public EventPlan Plan(IntegrationEvent e)
    {
        var key = e.RoutingKey;
        if (key == EventRoutingOptions.RequestCreated)
        {
            var request = e.Id("solicitud_id");
            return Of(
                Draft(e.Id("prestamista_usuario_id"), RequestReceivedKey, request, OriginType.Solicitud, "Nueva solicitud", "Recibiste una solicitud para tu objeto"),
                Draft(e.Id("prestatario_usuario_id"), RequestSentKey, request, OriginType.Solicitud, "Solicitud enviada", "Tu solicitud fue enviada al prestamista."));
        }
        if (key == EventRoutingOptions.RequestRejected)
        {
            return EventPlan.Ignore with
            {
                Request = new RequestFollowUp(e.Id("solicitud_id"), RequestSentKey, "SolicitudRechazada", "Solicitud rechazada",
                    WithReason("El prestamista rechazó tu solicitud.", e.OptionalText("motivo_rechazo"))),
            };
        }
        if (key == EventRoutingOptions.RequestCancelled)
        {
            return EventPlan.Ignore with
            {
                Request = new RequestFollowUp(e.Id("solicitud_id"), RequestReceivedKey, "SolicitudCancelada", "Solicitud cancelada",
                    WithReason("El prestatario canceló su solicitud.", e.OptionalText("motivo_cancelacion"))),
            };
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
        if (LoanPlan(e) is { } loan) return loan;
        if (key == routes.IncidentReported)
            return Both(e, "IncidenciaReportada", e.Id("incidencia_id"), OriginType.Incidencia, "Incidencia reportada", "Se ha reportado una incidencia en tu préstamo.");
        if (key == routes.IncidentResolved)
            return Both(e, "IncidenciaResuelta", e.Id("incidencia_id"), OriginType.Incidencia, "Incidencia resuelta", e.OptionalText("justificacion_resolucion") ?? "Un administrador resolvió la incidencia de tu préstamo.");
        if (key == routes.RatingRegistered)
        {
            return Of(Draft(e.Id("evaluado_usuario_id"), $"CalificacionRegistrada:{e.Id("evaluacion_id")}", e.Id("prestamo_id"), OriginType.Prestamo, "Nueva calificación",
                $"Recibiste una calificación de {e.Text("puntaje")} de 5 por tu préstamo."));
        }
        return EventPlan.Ignore;
    }

    private EventPlan? LoanPlan(IntegrationEvent e)
    {
        var key = e.RoutingKey;
        if (key == EventRoutingOptions.ObjectDelivered)
            return Of(ForBorrower(e, "ObjetoEntregado", OriginType.Prestamo, "prestamo_id", "Objeto entregado", "El prestamista registró la entrega. Confirma que recibiste el objeto."))
                with { Reminders = Change(e, cancelDelivery: true) };
        if (key == routes.LoanActivated)
            return Both(e, "PrestamoActivado", e.Id("prestamo_id"), OriginType.Prestamo, "Préstamo activo", "El prestatario confirmó la recepción y el préstamo está activo.")
                with { Reminders = Change(e, cancelDelivery: true) };
        if (key == EventRoutingOptions.ReturnRegistered)
            return Of(ForLender(e, "DevolucionRegistrada", OriginType.Prestamo, "prestamo_id", "Devolución registrada", "El prestatario registró la devolución. Confirma que recibiste el objeto."))
                with { Reminders = Change(e, cancelReturn: true) };
        if (key == routes.LoanFinished)
            return Both(e, "PrestamoFinalizado", e.Id("prestamo_id"), OriginType.Prestamo, "Préstamo finalizado", "El préstamo finalizó correctamente.")
                with { Reminders = Change(e, cancelDelivery: true, cancelReturn: true) };
        if (key == EventRoutingOptions.LoanOverdue)
        {
            // Un préstamo puede vencer otra vez después de una extensión pagada tarde: la fecha forma parte de la clave.
            var loan = e.Id("prestamo_id");
            var due = e.LocalDateTime("devolucion_vigente_en");
            var keyType = $"PrestamoVencido:{loan}:{due:yyyyMMddHHmm}";
            return Of(
                Draft(e.Id("prestatario_usuario_id"), keyType, loan, OriginType.Prestamo, "Préstamo vencido", $"La devolución venció el {LocalText(due)}. Devuelve el objeto o coordina con el prestamista."),
                Draft(e.Id("prestamista_usuario_id"), keyType, loan, OriginType.Prestamo, "Préstamo vencido", $"El prestatario no registró la devolución prevista para el {LocalText(due)}."));
        }
        if (key == EventRoutingOptions.RecoveryPending)
            return Both(e, "RecuperacionPendiente", e.Id("prestamo_id"), OriginType.Prestamo,
                "Recuperación pendiente", "La cancelación se completa cuando el prestamista confirma que recuperó el objeto.");
        if (key == EventRoutingOptions.CancellationRejected)
        {
            var loan = e.Id("prestamo_id");
            return Both(e, $"CancelacionRechazada:{loan}:{e.AggregateVersion}", loan, OriginType.Prestamo, "Cancelación no aceptada",
                $"La reserva sigue vigente: {Lower(e.OptionalText("motivo") ?? "el préstamo ya inició")}.");
        }
        if (key == EventRoutingOptions.ExtensionRequested)
            return Of(ForLender(e, "ExtensionSolicitada", OriginType.Extension, "cambio_fecha_prestamo_id", "Extensión solicitada",
                $"El prestatario pide devolver el objeto el {LocalText(e.LocalDateTime("fecha_propuesta_en"))}. Acepta o rechaza la extensión."));
        if (key == routes.ExtensionAccepted)
            return Of(
                ForBorrower(e, "ExtensionAceptada", OriginType.Extension, "cambio_fecha_prestamo_id", "Extensión aceptada",
                    e.OptionalAmount("costo_adicional") is null ? "La extensión del préstamo fue aceptada." : $"El prestamista aceptó la extensión. Paga {Money(e, "costo_adicional")} para aplicarla."),
                ForLender(e, "ExtensionAceptada", OriginType.Extension, "cambio_fecha_prestamo_id", "Extensión aceptada", "La extensión del préstamo fue aceptada."));
        if (key == EventRoutingOptions.ExtensionRejected)
            return Of(ForBorrower(e, "ExtensionRechazada", OriginType.Extension, "cambio_fecha_prestamo_id", "Extensión rechazada",
                WithReason("El prestamista rechazó la extensión.", e.OptionalText("motivo_rechazo"))));
        if (key == EventRoutingOptions.RescheduleProposed)
            return Of(ForBorrower(e, "ReprogramacionPropuesta", OriginType.Extension, "cambio_fecha_prestamo_id", "Reprogramación propuesta",
                $"El prestamista propone la devolución para el {LocalText(e.LocalDateTime("fecha_propuesta_en"))}, sin costo. Acepta o rechaza la propuesta."));
        if (key == EventRoutingOptions.RescheduleRejected)
            return Of(ForLender(e, "ReprogramacionRechazada", OriginType.Extension, "cambio_fecha_prestamo_id", "Reprogramación rechazada",
                WithReason("El prestatario rechazó la reprogramación.", e.OptionalText("motivo_rechazo"))));
        if (key == EventRoutingOptions.ReturnDateUpdated)
        {
            var due = e.LocalDateTime("devolucion_vigente_en");
            return Both(e, "FechaDevolucionActualizada", e.Id("cambio_fecha_prestamo_id"), OriginType.Extension, "Nueva fecha de devolución", $"La devolución quedó para el {LocalText(due)}.")
                with { Reminders = new ReminderChange(e.Id("reserva_id"), NewReturnLocal: due, ChangeId: e.Text("cambio_fecha_prestamo_id")) };
        }
        return null;
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
            {
                var (title, subject) = e.OptionalText("tipo") switch
                {
                    "CONSTITUCION_GARANTIA" => ("Garantía no confirmada", "tu garantía"),
                    "PAGO_EXTENSION" => ("Pago de extensión no confirmado", "el pago de la extensión"),
                    _ => ("Pago no confirmado", "tu pago"),
                };
                return Of(Draft(e.Id("prestatario_usuario_id"), e.EventType, e.Id("transaccion_id"), OriginType.Pago, title,
                    $"Mercado Pago no confirmó {subject}. Puedes intentarlo nuevamente."));
            }
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
                    e.OptionalText("cambio_fecha_prestamo_id") is null
                        ? $"Se reembolsaron {Money(e, "monto")} por la cancelación de la reserva."
                        : $"Se reembolsaron {Money(e, "monto")} de la extensión porque ya no correspondía aplicarla."));
            case "ReembolsoRechazado":
                return Of(Draft(e.Id("prestatario_usuario_id"), e.EventType, e.Id("transaccion_id"), OriginType.Pago, "Reembolso no completado",
                    $"No se pudo completar {(e.OptionalText("tipo") == "DEVOLUCION_GARANTIA" ? "la devolución de tu garantía" : "el reembolso")} de {Money(e, "monto")}. LendUp lo revisará."));
            default:
                return EventPlan.Ignore;
        }
    }

    private static EventPlan Both(IntegrationEvent e, string type, Guid originId, OriginType origin, string title, string message) =>
        Of(Draft(e.Id("prestatario_usuario_id"), type, originId, origin, title, message),
           Draft(e.Id("prestamista_usuario_id"), type, originId, origin, title, message));

    private static NotificationDraft ForBorrower(IntegrationEvent e, string type, OriginType origin, string originField, string title, string message) =>
        Draft(e.Id("prestatario_usuario_id"), type, e.Id(originField), origin, title, message);

    private static NotificationDraft ForLender(IntegrationEvent e, string type, OriginType origin, string originField, string title, string message) =>
        Draft(e.Id("prestamista_usuario_id"), type, e.Id(originField), origin, title, message);

    /// <summary>Los recordatorios cuelgan de la reserva; un evento sin reserva_id no los toca.</summary>
    private static ReminderChange? Change(IntegrationEvent e, bool cancelDelivery = false, bool cancelReturn = false) =>
        Guid.TryParse(e.OptionalText("reserva_id"), out var reservation) ? new ReminderChange(reservation, cancelDelivery, cancelReturn) : null;

    private static string WithReason(string text, string? reason) => string.IsNullOrWhiteSpace(reason) ? text : $"{text} Motivo: {reason.Trim()}";

    private static string LocalText(DateTime local) => local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Lower(string text) => char.ToLowerInvariant(text[0]) + text[1..].TrimEnd('.');

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
