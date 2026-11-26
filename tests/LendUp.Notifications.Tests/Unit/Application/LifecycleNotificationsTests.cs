using System.Text;
using System.Text.Json;
using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using LendUp.Notifications.Api.Infrastructure.Messaging;
using LendUp.Notifications.Tests.Support;

namespace LendUp.Notifications.Tests.Unit.Application;

/// <summary>Avisos del ciclo de solicitudes, préstamos, cambios de fecha, reembolsos y calificaciones (US18, US20–US23, US41, US43, US48, US49).</summary>
public class LifecycleNotificationsTests
{
    private readonly NotificationsFixture _f = new();
    private readonly Guid _loan = Guid.NewGuid();
    private readonly Guid _reservation = Guid.NewGuid();
    private readonly Guid _change = Guid.NewGuid();

    private Task<bool> Loan(string routingKey, string eventType, params (string Key, object Value)[] extra) =>
        _f.PublishAsync(routingKey, eventType, _f.Participants([("prestamo_id", _loan.ToString()), ("reserva_id", _reservation.ToString()), .. extra]));

    private Task<bool> Request(Guid request) => _f.PublishAsync("reservas.solicitud-creada.v1", "SolicitudCreada", new Dictionary<string, object>
    {
        ["solicitud_id"] = request.ToString(), ["prestatario_usuario_id"] = _f.Borrower.ToString(), ["prestamista_usuario_id"] = _f.Lender.ToString(),
    });

    private Notification Only(Guid user, string title) => Assert.Single(_f.For(user), n => n.Title == title);
    private void None(Guid user, string title) => Assert.DoesNotContain(_f.For(user), n => n.Title == title);
    private List<Notification> Reminders(string title) => _f.Repository.Stored.Where(n => n.Type == NotificationType.Recordatorio && n.Title == title).ToList();

    // ---- Solicitudes (RF15) ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SolicitudRechazadaAvisaSoloAlPrestatarioConElMotivo()
    {
        var request = Guid.NewGuid();
        await Request(request);
        await _f.PublishAsync("reservas.solicitud-rechazada.v1", "SolicitudRechazada", new Dictionary<string, object> { ["solicitud_id"] = request.ToString(), ["motivo_rechazo"] = "No estará disponible" });
        var n = Only(_f.Borrower, "Solicitud rechazada");
        Assert.Equal("El prestamista rechazó tu solicitud. Motivo: No estará disponible", n.Message);
        Assert.Equal((OriginType.Solicitud, request), (n.OriginType, n.OriginId));
        None(_f.Lender, "Solicitud rechazada");
    }

    [Fact]
    public async Task SolicitudCanceladaAvisaSoloAlPrestamista()
    {
        var request = Guid.NewGuid();
        await Request(request);
        await _f.PublishAsync("reservas.solicitud-cancelada.v1", "SolicitudCancelada", new Dictionary<string, object>
        {
            ["solicitud_id"] = request.ToString(), ["motivo_cancelacion"] = "Ya no lo necesito", ["cancelada_por_usuario_id"] = _f.Borrower.ToString(),
        });
        Assert.Equal("El prestatario canceló su solicitud. Motivo: Ya no lo necesito", Only(_f.Lender, "Solicitud cancelada").Message);
        None(_f.Borrower, "Solicitud cancelada");
    }

    [Fact]
    public async Task SolicitudDesconocidaRechazadaNoFalla()
    {
        Assert.True(await _f.PublishAsync("reservas.solicitud-rechazada.v1", "SolicitudRechazada", new Dictionary<string, object> { ["solicitud_id"] = Guid.NewGuid().ToString() }));
        Assert.Empty(_f.Repository.Stored);
    }

    // ---- Entrega, devolución y vencimiento (US18, US20, US23) -----------------------------------------------------

    [Fact]
    public async Task ObjetoEntregadoPideAlPrestatarioConfirmarLaRecepcion()
    {
        await Loan("prestamos.objeto-entregado.v1", "ObjetoEntregado");
        var n = Only(_f.Borrower, "Objeto entregado");
        Assert.Equal("El prestamista registró la entrega. Confirma que recibiste el objeto.", n.Message);
        Assert.Equal((OriginType.Prestamo, _loan), (n.OriginType, n.OriginId));
        Assert.Empty(_f.For(_f.Lender));
    }

    [Fact]
    public async Task DevolucionRegistradaPideAlPrestamistaConfirmarla()
    {
        await Loan("prestamos.devolucion-registrada.v1", "DevolucionRegistrada");
        Assert.Equal("El prestatario registró la devolución. Confirma que recibiste el objeto.", Only(_f.Lender, "Devolución registrada").Message);
        Assert.Empty(_f.For(_f.Borrower));
    }

    [Fact]
    public async Task PrestamoVencidoAvisaAAmbosYUnaVezPorFecha()
    {
        await Loan("prestamos.vencido.v1", "PrestamoVencido", ("devolucion_vigente_en", "2026-11-12T09:00:00"));
        await Loan("prestamos.vencido.v1", "PrestamoVencido", ("devolucion_vigente_en", "2026-11-12T09:00:00"));
        Assert.Equal("La devolución venció el 12/11/2026 09:00. Devuelve el objeto o coordina con el prestamista.", Only(_f.Borrower, "Préstamo vencido").Message);
        Assert.Equal("El prestatario no registró la devolución prevista para el 12/11/2026 09:00.", Only(_f.Lender, "Préstamo vencido").Message);
        await Loan("prestamos.vencido.v1", "PrestamoVencido", ("devolucion_vigente_en", "2026-11-15T09:00:00"));
        Assert.Equal(2, _f.For(_f.Borrower).Count(n => n.Title == "Préstamo vencido"));
    }

    [Fact]
    public async Task CancelacionRechazadaExplicaElMotivoAAmbos()
    {
        await Loan("prestamos.cancelacion-rechazada.v1", "CancelacionRechazada", ("motivo", "El objeto ya fue entregado"));
        Assert.Equal("La reserva sigue vigente: el objeto ya fue entregado.", Only(_f.Borrower, "Cancelación no aceptada").Message);
        Only(_f.Lender, "Cancelación no aceptada");
    }

    // ---- Extensiones y reprogramaciones (US21, US22, US43) --------------------------------------------------------

    [Fact]
    public async Task ExtensionSolicitadaAvisaAlPrestamista()
    {
        await Loan("prestamos.extension-solicitada.v1", "ExtensionSolicitada", ("cambio_fecha_prestamo_id", _change.ToString()), ("fecha_propuesta_en", "2026-11-14T09:00:00"));
        var n = Only(_f.Lender, "Extensión solicitada");
        Assert.Equal("El prestatario pide devolver el objeto el 14/11/2026 09:00. Acepta o rechaza la extensión.", n.Message);
        Assert.Equal((OriginType.Extension, _change), (n.OriginType, n.OriginId));
        Assert.Empty(_f.For(_f.Borrower));
    }

    [Fact]
    public async Task ExtensionAceptadaIndicaAlPrestatarioCuantoPagar()
    {
        await Loan("prestamos.extension-aceptada.v1", "ExtensionAceptada", ("cambio_fecha_prestamo_id", _change.ToString()), ("costo_adicional", 40.00m));
        Assert.Equal("El prestamista aceptó la extensión. Paga 40.00 PEN para aplicarla.", Only(_f.Borrower, "Extensión aceptada").Message);
        Assert.Equal("La extensión del préstamo fue aceptada.", Only(_f.Lender, "Extensión aceptada").Message);
    }

    [Fact]
    public async Task ExtensionRechazadaAvisaAlPrestatarioConElMotivo()
    {
        await Loan("prestamos.extension-rechazada.v1", "ExtensionRechazada", ("cambio_fecha_prestamo_id", _change.ToString()), ("motivo_rechazo", "Lo necesito"));
        Assert.Equal("El prestamista rechazó la extensión. Motivo: Lo necesito", Only(_f.Borrower, "Extensión rechazada").Message);
        Assert.Empty(_f.For(_f.Lender));
    }

    [Fact]
    public async Task ReprogramacionPropuestaAvisaAlPrestatarioYSuRechazoAlPrestamista()
    {
        await Loan("prestamos.reprogramacion-propuesta.v1", "ReprogramacionPropuesta", ("cambio_fecha_prestamo_id", _change.ToString()), ("fecha_propuesta_en", "2026-11-13T18:30:00"));
        Assert.Equal("El prestamista propone la devolución para el 13/11/2026 18:30, sin costo. Acepta o rechaza la propuesta.", Only(_f.Borrower, "Reprogramación propuesta").Message);
        await Loan("prestamos.reprogramacion-rechazada.v1", "ReprogramacionRechazada", ("cambio_fecha_prestamo_id", _change.ToString()));
        Assert.Equal("El prestatario rechazó la reprogramación.", Only(_f.Lender, "Reprogramación rechazada").Message);
    }

    [Fact]
    public async Task FechaDeDevolucionActualizadaAvisaYMueveSoloElRecordatorioDeDevolucion()
    {
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(_reservation));
        await Loan("prestamos.fecha-devolucion-actualizada.v1", "FechaDevolucionActualizada", ("cambio_fecha_prestamo_id", _change.ToString()), ("tipo", "EXTENSION"),
            ("devolucion_anterior_en", "2026-11-12T09:00:00"), ("devolucion_vigente_en", "2026-11-14T09:00:00"));
        Assert.Equal("La devolución quedó para el 14/11/2026 09:00.", Only(_f.Borrower, "Nueva fecha de devolución").Message);
        Only(_f.Lender, "Nueva fecha de devolución");

        var returns = Reminders("Recordatorio de devolución");
        Assert.Equal(2, returns.Count(r => r.State == NotificationState.Cancelada));
        var moved = returns.Where(r => r.State == NotificationState.Programada).ToList();
        Assert.Equal(2, moved.Count);
        Assert.All(moved, r => Assert.Equal(new DateTime(2026, 11, 13, 14, 0, 0, DateTimeKind.Utc), r.ScheduledFor));
        Assert.All(moved, r => Assert.Contains("14/11/2026 09:00", r.Message));
        Assert.All(Reminders("Recordatorio de entrega"), r => Assert.Equal(NotificationState.Programada, r.State));
    }

    [Fact]
    public async Task VolverAUnaFechaAnteriorTambienReprogramaElRecordatorio()
    {
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(_reservation));
        foreach (var date in new[] { "2026-11-14T09:00:00", "2026-11-12T09:00:00" })
            await Loan("prestamos.fecha-devolucion-actualizada.v1", "FechaDevolucionActualizada", ("cambio_fecha_prestamo_id", Guid.NewGuid().ToString()), ("devolucion_vigente_en", date));
        var scheduled = Reminders("Recordatorio de devolución").Where(r => r.State == NotificationState.Programada).ToList();
        Assert.Equal(2, scheduled.Count);
        Assert.All(scheduled, r => Assert.Equal(new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Utc), r.ScheduledFor));
    }

    // ---- Recordatorios que ya no corresponden (US49) --------------------------------------------------------------

    [Theory]
    [InlineData("prestamos.objeto-entregado.v1", "ObjetoEntregado", "Recordatorio de entrega", "Recordatorio de devolución")]
    [InlineData("prestamos.devolucion-registrada.v1", "DevolucionRegistrada", "Recordatorio de devolución", "Recordatorio de entrega")]
    public async Task UnHechoAnticipadoAnulaSuRecordatorioPendiente(string routingKey, string eventType, string cancelled, string kept)
    {
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(_reservation));
        await Loan(routingKey, eventType);
        Assert.All(Reminders(cancelled), r => Assert.Equal(NotificationState.Cancelada, r.State));
        Assert.All(Reminders(kept), r => Assert.Equal(NotificationState.Programada, r.State));
    }

    [Fact]
    public async Task AlFinalizarElPrestamoNoQuedanRecordatoriosPendientes()
    {
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(_reservation));
        await Loan("prestamos.finalizado.v1", "PrestamoFinalizado");
        Assert.All(_f.Repository.Stored.Where(n => n.Type == NotificationType.Recordatorio), r => Assert.Equal(NotificationState.Cancelada, r.State));
    }

    // ---- Pagos (US17, US33, US41) ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("CONSTITUCION_GARANTIA", "Garantía no confirmada", "Mercado Pago no confirmó tu garantía. Puedes intentarlo nuevamente.")]
    [InlineData("PAGO_EXTENSION", "Pago de extensión no confirmado", "Mercado Pago no confirmó el pago de la extensión. Puedes intentarlo nuevamente.")]
    [InlineData("PAGO_TARIFA", "Pago no confirmado", "Mercado Pago no confirmó tu pago. Puedes intentarlo nuevamente.")]
    public async Task PagoRechazadoDiceQueConceptoNoSeConfirmo(string tipo, string title, string message)
    {
        await _f.PublishAsync("pagos.pago-rechazado.v1", "PagoRechazado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString()), ("tipo", tipo)));
        Assert.Equal(message, Only(_f.Borrower, title).Message);
    }

    [Theory]
    [InlineData("REEMBOLSO_TARIFA", "No se pudo completar el reembolso de 40.00 PEN. LendUp lo revisará.")]
    [InlineData("DEVOLUCION_GARANTIA", "No se pudo completar la devolución de tu garantía de 40.00 PEN. LendUp lo revisará.")]
    public async Task ReembolsoRechazadoInformaAlPrestatario(string tipo, string message)
    {
        await _f.PublishAsync("pagos.reembolso-rechazado.v1", "ReembolsoRechazado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString()), ("tipo", tipo), ("monto", 40.00m)));
        Assert.Equal(message, Only(_f.Borrower, "Reembolso no completado").Message);
        Assert.Empty(_f.For(_f.Lender));
    }

    [Fact]
    public async Task ReembolsoDeUnaExtensionSinEfectoNoHablaDeCancelacion()
    {
        await _f.PublishAsync("pagos.reembolso-procesado.v1", "ReembolsoProcesado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString()), ("monto", 40.00m), ("cambio_fecha_prestamo_id", _change.ToString())));
        Assert.Equal("Se reembolsaron 40.00 PEN de la extensión porque ya no correspondía aplicarla.", Only(_f.Borrower, "Reembolso procesado").Message);
    }

    // ---- Reputación -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task CalificacionRegistradaLlegaPlanaComoLaPublicaReputacion()
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["event_id"] = Guid.NewGuid().ToString(), ["correlation_id"] = Guid.NewGuid().ToString(), ["event_type"] = "CalificacionRegistrada", ["event_version"] = 1,
            ["evaluacion_id"] = Guid.NewGuid().ToString(), ["prestamo_id"] = _loan.ToString(), ["evaluador_usuario_id"] = _f.Lender.ToString(),
            ["evaluado_usuario_id"] = _f.Borrower.ToString(), ["puntaje"] = 5, ["registrada_en"] = "2026-11-25T18:00:00Z",
        }));
        await _f.Notifications.ApplyEventAsync(EventProcessor.Parse("reputation.rating-registered.v1", body), default);
        await _f.Repository.SaveChangesAsync(default);
        var n = Only(_f.Borrower, "Nueva calificación");
        Assert.Equal("Recibiste una calificación de 5 de 5 por tu préstamo.", n.Message);
        Assert.Equal((OriginType.Prestamo, _loan), (n.OriginType, n.OriginId));
        Assert.Empty(_f.For(_f.Lender));
    }

    [Theory]
    [InlineData("reservas.confirmada.v1")]
    [InlineData("pagos.pago-confirmado.v1")]
    [InlineData("prestamos.activado.v1")]
    [InlineData("evidencias.incidencia-reportada.v1")]
    public void UnEventoConEnvelopeEstandarSinPayloadSigueSiendoInvalido(string routingKey) =>
        Assert.Throws<InvalidMessageException>(() => EventProcessor.Parse(routingKey, Encoding.UTF8.GetBytes($"{{\"event_id\":\"{Guid.NewGuid()}\",\"event_type\":\"X\"}}")));

    // ---- Suscripciones --------------------------------------------------------------------------------------------

    [Fact]
    public void LaColaSeSuscribeATodoLoQueTieneAviso()
    {
        string[] expected =
        [
            "reservas.solicitud-creada.v1", "reservas.solicitud-rechazada.v1", "reservas.solicitud-cancelada.v1", "reservas.confirmada.v1", "reservas.cancelada.v1", "pagos.*.v1",
            "prestamos.objeto-entregado.v1", "prestamos.activado.v1", "prestamos.devolucion-registrada.v1", "prestamos.vencido.v1", "prestamos.finalizado.v1",
            "prestamos.cancelacion-rechazada.v1", "prestamos.recuperacion-pendiente.v1", "prestamos.extension-solicitada.v1", "prestamos.extension-aceptada.v1", "prestamos.extension-rechazada.v1",
            "prestamos.reprogramacion-propuesta.v1", "prestamos.reprogramacion-rechazada.v1", "prestamos.fecha-devolucion-actualizada.v1",
            "evidencias.incidencia-reportada.v1", "evidencias.incidencia-resuelta.v1", "reputation.rating-registered.v1",
        ];
        Assert.Equal(expected.Order(), new EventRoutingOptions().Bindings().Order());
    }
}
