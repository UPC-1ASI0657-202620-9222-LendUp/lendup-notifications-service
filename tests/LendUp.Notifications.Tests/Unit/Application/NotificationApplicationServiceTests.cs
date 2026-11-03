using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using LendUp.Notifications.Api.Infrastructure.Messaging;
using LendUp.Notifications.Tests.Support;

namespace LendUp.Notifications.Tests.Unit.Application;

public class NotificationApplicationServiceTests
{
    private readonly NotificationsFixture _f = new();

    [Fact]
    public async Task SolicitudCreadaAvisaAlPrestamistaConElTextoDelMonolito()
    {
        var request = Guid.NewGuid();
        await _f.PublishAsync("reservas.solicitud-creada.v1", "SolicitudCreada", new Dictionary<string, object>
        {
            ["solicitud_id"] = request.ToString(), ["publicacion_id"] = Guid.NewGuid().ToString(),
            ["prestatario_usuario_id"] = _f.Borrower.ToString(), ["prestamista_usuario_id"] = _f.Lender.ToString(),
            ["desde"] = "2026-11-10T09:00:00", ["hasta"] = "2026-11-12T09:00:00",
        });
        var n = Assert.Single(_f.Repository.Stored);
        Assert.Equal(_f.Lender, n.RecipientUserId);
        Assert.Equal(("Nueva solicitud", "Recibiste una solicitud para tu objeto"), (n.Title, n.Message));
        Assert.Equal((OriginType.Solicitud, request), (n.OriginType, n.OriginId));
        Assert.Equal(NotificationState.Disponible, n.State);
    }

    [Fact]
    public async Task ReservaConfirmadaAvisaAAmbosYProgramaCuatroRecordatorios()
    {
        var reservation = Guid.NewGuid();
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(reservation));
        Assert.Equal("Tu solicitud fue aceptada", _f.For(_f.Borrower).Single(n => n.Type == NotificationType.Evento).Message);
        Assert.Equal("Confirmaste una reserva", _f.For(_f.Lender).Single(n => n.Type == NotificationType.Evento).Message);
        var reminders = _f.Repository.Stored.Where(n => n.Type == NotificationType.Recordatorio).ToList();
        Assert.Equal(4, reminders.Count);
        Assert.All(reminders, r => Assert.Equal(NotificationState.Programada, r.State));
        // Entrega 10/11 09:00 Lima = 14:00 UTC, menos 24 h.
        Assert.Contains(reminders, r => r.Title == "Recordatorio de entrega" && r.ScheduledFor == new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc));
        Assert.Contains(reminders, r => r.Title == "Recordatorio de devolución" && r.ScheduledFor == new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ElMismoEventoNoDuplicaAvisosNiPorInboxNiPorClave()
    {
        var reservation = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        Assert.True(await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(reservation), eventId));
        Assert.False(await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(reservation), eventId));
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(reservation));
        Assert.Equal(6, _f.Repository.Stored.Count);
    }

    [Fact]
    public async Task ReservaCanceladaAvisaALosParticipantesYCancelaRecordatorios()
    {
        var reservation = Guid.NewGuid();
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(reservation));
        await _f.PublishAsync("reservas.cancelada.v1", "ReservaCancelada", new Dictionary<string, object> { ["reserva_id"] = reservation.ToString(), ["solicitud_id"] = Guid.NewGuid().ToString() });
        Assert.Equal(2, _f.Repository.Stored.Count(n => n.Title == "Reserva cancelada"));
        Assert.All(_f.Repository.Stored.Where(n => n.Type == NotificationType.Recordatorio), r => Assert.Equal(NotificationState.Cancelada, r.State));
    }

    [Fact]
    public async Task CancelacionDeReservaDesconocidaNoFalla()
    {
        await _f.PublishAsync("reservas.cancelada.v1", "ReservaCancelada", new Dictionary<string, object> { ["reserva_id"] = Guid.NewGuid().ToString() });
        Assert.Empty(_f.Repository.Stored);
    }

    [Theory]
    [InlineData("pagos.pago-confirmado.v1", "PagoConfirmado", 2, "Pago confirmado")]
    [InlineData("pagos.pago-rechazado.v1", "PagoRechazado", 1, "Pago no confirmado")]
    [InlineData("pagos.garantia-constituida.v1", "GarantiaConstituida", 2, "Garantía constituida")]
    [InlineData("pagos.tarifa-liberada.v1", "TarifaLiberada", 1, "Tarifa liberada")]
    [InlineData("pagos.garantia-devuelta.v1", "GarantiaDevuelta", 1, "Garantía devuelta")]
    [InlineData("pagos.garantia-retenida.v1", "GarantiaRetenida", 2, "Garantía retenida")]
    [InlineData("pagos.reembolso-procesado.v1", "ReembolsoProcesado", 1, "Reembolso procesado")]
    public async Task EventosDePagosGeneranAvisos(string routingKey, string eventType, int expected, string title)
    {
        await _f.PublishAsync(routingKey, eventType, _f.Participants(("transaccion_id", Guid.NewGuid().ToString()), ("garantia_id", Guid.NewGuid().ToString()),
            ("monto", 60.00m), ("monto_constituido", 50.00m), ("monto_devuelto", 30.00m), ("monto_retenido", 20.00m), ("monto_neto_prestamista", 57.61m)));
        Assert.Equal(expected, _f.Repository.Stored.Count);
        Assert.Contains(_f.Repository.Stored, n => n.Title == title);
    }

    [Fact]
    public async Task PagoConfirmadoMuestraMontoYMoneda()
    {
        await _f.PublishAsync("pagos.pago-confirmado.v1", "PagoConfirmado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString()), ("tipo", "PAGO_TARIFA"), ("monto", 60m)));
        var borrower = _f.For(_f.Borrower).Single();
        Assert.Equal("Mercado Pago confirmó tu pago de 60.00 PEN.", borrower.Message);
        Assert.Equal(OriginType.Pago, borrower.OriginType);
    }

    [Fact]
    public async Task EventoDePagosDesconocidoSeIgnoraSinError()
    {
        Assert.True(await _f.PublishAsync("pagos.unknown.v1", "Otro", new Dictionary<string, object> { ["x"] = 1 }));
        Assert.Empty(_f.Repository.Stored);
    }

    [Fact]
    public async Task EventosPendientesUsanRoutingKeysConfigurables()
    {
        var incident = Guid.NewGuid();
        await _f.PublishAsync("prestamos.activado.v1", "PrestamoActivado", _f.Participants());
        await _f.PublishAsync("evidencias.incidencia-resuelta.v1", "IncidenciaResuelta", _f.Participants(("incidencia_id", incident.ToString()), ("justificacion_resolucion", "Daño leve comprobado con evidencias.")));
        Assert.Equal(2, _f.Repository.Stored.Count(n => n.Title == "Préstamo activo"));
        Assert.Equal(2, _f.Repository.Stored.Count(n => n.OriginType == OriginType.Incidencia && n.Message == "Daño leve comprobado con evidencias."));
    }

    [Fact]
    public async Task PayloadIncompletoEsErrorParaLaDlq() =>
        await Assert.ThrowsAsync<ArgumentException>(() => _f.PublishAsync("pagos.pago-confirmado.v1", "PagoConfirmado", new Dictionary<string, object> { ["monto"] = 1 }));

    [Fact]
    public void EnvelopeInvalidoNoSeReintenta()
    {
        Assert.Throws<InvalidMessageException>(() => EventProcessor.Parse("reservas.confirmada.v1", "{\"event_id\":\"x\"}"u8.ToArray()));
        Assert.Throws<InvalidMessageException>(() => EventProcessor.Parse("reservas.confirmada.v1", "no-json"u8.ToArray()));
    }

    [Fact]
    public async Task ListarSoloMuestraAvisosVisiblesDelUsuarioDelMasReciente()
    {
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(Guid.NewGuid()));
        _f.Time.Advance(TimeSpan.FromMinutes(5));
        await _f.PublishAsync("pagos.pago-rechazado.v1", "PagoRechazado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString())));
        var list = await _f.Notifications.ListAsync(_f.Borrower, default);
        Assert.Equal(2, list.Count);
        Assert.Equal("Pago no confirmado", list[0].Title);
        Assert.DoesNotContain(list, n => n.State == NotificationState.Programada);
    }

    [Fact]
    public async Task MarcarLeidaValidaExistenciaDuenioYEstado()
    {
        await _f.PublishAsync("pagos.pago-rechazado.v1", "PagoRechazado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString())));
        var n = _f.Repository.Stored.Single();
        await Assert.ThrowsAsync<NotFoundException>(() => _f.Notifications.MarkReadAsync(_f.Borrower, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => _f.Notifications.MarkReadAsync(_f.Lender, n.Id, default));
        var read = await _f.Notifications.MarkReadAsync(_f.Borrower, n.Id, default);
        Assert.Equal(NotificationState.Leida, read.State);
        var conflict = await Assert.ThrowsAsync<BusinessConflictException>(() => _f.Notifications.MarkReadAsync(_f.Borrower, n.Id, default));
        Assert.Equal("Aviso no disponible", conflict.Message);
    }

    [Fact]
    public async Task CorreosPendientesSeEnvianUnaVez()
    {
        await _f.PublishAsync("pagos.pago-rechazado.v1", "PagoRechazado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString())));
        Assert.Equal(1, await _f.Notifications.DispatchPendingEmailsAsync(10, default));
        Assert.Equal(0, await _f.Notifications.DispatchPendingEmailsAsync(10, default));
        var mail = Assert.Single(_f.Email.Sent);
        Assert.Equal("Pago no confirmado", mail.Subject);
        Assert.NotNull(mail.ToAddress);
        Assert.Equal(EmailState.Enviado, _f.Repository.Stored.Single().EmailState);
    }

    [Fact]
    public async Task SendGridCaidoReintentaYLuegoQuedaFallido()
    {
        await _f.PublishAsync("pagos.pago-rechazado.v1", "PagoRechazado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString())));
        _f.Email.Fail = true;
        for (var i = 0; i < _f.Options.EmailMaxAttempts; i++) await _f.Notifications.DispatchPendingEmailsAsync(10, default);
        var n = _f.Repository.Stored.Single();
        Assert.Equal(EmailState.Fallido, n.EmailState);
        Assert.Equal(_f.Options.EmailMaxAttempts, n.EmailAttempts);
    }

    [Fact]
    public async Task SinContratoDeCorreoConSendGridFallaPeroEnModoLogSeRegistra()
    {
        _f.Identity.EmailContractConfigured = false;
        await _f.PublishAsync("pagos.pago-rechazado.v1", "PagoRechazado", _f.Participants(("transaccion_id", Guid.NewGuid().ToString())));
        await _f.Notifications.DispatchPendingEmailsAsync(10, default);
        Assert.Empty(_f.Email.Sent);
        Assert.Equal(1, _f.Repository.Stored.Single().EmailAttempts);

        var local = new NotificationsFixture(emailRequiresAddress: false);
        local.Identity.EmailContractConfigured = false;
        await local.PublishAsync("pagos.pago-rechazado.v1", "PagoRechazado", local.Participants(("transaccion_id", Guid.NewGuid().ToString())));
        await local.Notifications.DispatchPendingEmailsAsync(10, default);
        Assert.Null(Assert.Single(local.Email.Sent).ToAddress);
    }

    [Fact]
    public async Task RecordatoriosSeActivanASuHoraYRecienAhiEnvianCorreo()
    {
        await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(Guid.NewGuid()));
        await _f.Notifications.DispatchPendingEmailsAsync(50, default);
        Assert.Equal(2, _f.Email.Sent.Count);
        Assert.Equal(0, await _f.Reminders.ActivateDueAsync(50, default));
        _f.Time.Now = new DateTimeOffset(2026, 11, 9, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, await _f.Reminders.ActivateDueAsync(50, default));
        await _f.Notifications.DispatchPendingEmailsAsync(50, default);
        Assert.Equal(4, _f.Email.Sent.Count);
        Assert.Contains(_f.Email.Sent, m => m.Subject == "Recordatorio de entrega");
    }
}
