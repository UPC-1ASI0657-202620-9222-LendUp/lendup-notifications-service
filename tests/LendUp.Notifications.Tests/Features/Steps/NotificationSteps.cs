using System.Globalization;
using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Domain;
using LendUp.Notifications.Tests.Support;
using Reqnroll;

namespace LendUp.Notifications.Tests.Features.Steps;

/// <summary>Pasos sobre los servicios de aplicación reales con persistencia en memoria (una instancia por escenario).</summary>
[Binding]
public sealed class NotificationSteps
{
    private readonly NotificationsFixture _f = new();
    private Guid _reservation = Guid.NewGuid();
    private Notification? _current;

    [When("Reservas publica una solicitud nueva")]
    public Task RequestCreated() => _f.PublishAsync("reservas.solicitud-creada.v1", "SolicitudCreada", new Dictionary<string, object>
    {
        ["solicitud_id"] = Guid.NewGuid().ToString(), ["publicacion_id"] = Guid.NewGuid().ToString(), ["prestatario_usuario_id"] = _f.Borrower.ToString(),
        ["prestamista_usuario_id"] = _f.Lender.ToString(), ["desde"] = "2026-11-10T09:00:00", ["hasta"] = "2026-11-12T09:00:00",
    });

    [Given("que Reservas confirma una reserva")]
    [When("Reservas confirma una reserva")]
    public Task ReservationConfirmed() => _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(_reservation));

    [Given("que Reservas confirma una reserva con entrega el {string} y devolución el {string}")]
    public Task ReservationConfirmedWithDates(string from, string to) =>
        _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(_reservation, from, to));

    [When("Reservas confirma una reserva y el broker entrega el mismo evento {int} veces")]
    public async Task DuplicatedDelivery(int times)
    {
        var eventId = Guid.NewGuid();
        for (var i = 0; i < times; i++) await _f.PublishAsync("reservas.confirmada.v1", "ReservaConfirmada", _f.ReservationConfirmed(_reservation), eventId);
    }

    [When("Reservas cancela la reserva")]
    public Task ReservationCancelled() =>
        _f.PublishAsync("reservas.cancelada.v1", "ReservaCancelada", new Dictionary<string, object> { ["reserva_id"] = _reservation.ToString(), ["solicitud_id"] = Guid.NewGuid().ToString() });

    [When("Pagos publica {string} por {double} PEN")]
    public Task PaymentEvent(string eventType, double value)
    {
        var routingKey = eventType switch
        {
            "PagoConfirmado" => "pagos.pago-confirmado.v1",
            "GarantiaDevuelta" => "pagos.garantia-devuelta.v1",
            _ => throw new ArgumentException(eventType),
        };
        return _f.PublishAsync(routingKey, eventType, _f.Participants(("transaccion_id", Guid.NewGuid().ToString()), ("garantia_id", Guid.NewGuid().ToString()),
            ("tipo", "PAGO_TARIFA"), ("monto", (decimal)value), ("monto_devuelto", (decimal)value)));
    }

    [Then("el prestatario tiene {int} aviso(s) {string}")]
    public void BorrowerHas(int count, string title) => Assert.Equal(count, _f.For(_f.Borrower).Count(n => n.Title == title));

    [Then("el prestamista tiene {int} aviso(s) {string}")]
    public void LenderHas(int count, string title) => Assert.Equal(count, _f.For(_f.Lender).Count(n => n.Title == title));

    [Then("el prestatario tiene {int} avisos")]
    public void BorrowerTotal(int count) => Assert.Equal(count, _f.For(_f.Borrower).Count());

    [Then("el prestatario tiene un aviso con el mensaje {string}")]
    public void BorrowerMessage(string message) => Assert.Contains(_f.For(_f.Borrower), n => n.Message == message);

    [When("el prestatario marca como leído su aviso {string}")]
    public async Task MarkRead(string title)
    {
        var notification = _f.For(_f.Borrower).Single(n => n.Title == title);
        _current = await _f.Notifications.MarkReadAsync(_f.Borrower, notification.Id, default);
    }

    [Then("el aviso queda en estado {string}")]
    public void CurrentState(string state) => Assert.Equal(state, EnumText.ToText(_current!.State));

    [Then("volver a marcarlo es rechazado con {string}")]
    public async Task MarkAgain(string message)
    {
        var error = await Assert.ThrowsAsync<BusinessConflictException>(() => _f.Notifications.MarkReadAsync(_f.Borrower, _current!.Id, default));
        Assert.Equal(message, error.Message);
    }

    [Given("que el proveedor de correo no responde")]
    public void EmailDown() => _f.Email.Fail = true;

    [When("corre el envío de correos")]
    public Task Dispatch() => _f.Notifications.DispatchPendingEmailsAsync(100, default);

    [When("corre el envío de correos {int} veces")]
    public async Task DispatchTimes(int times)
    {
        for (var i = 0; i < times; i++) await _f.Notifications.DispatchPendingEmailsAsync(100, default);
    }

    [Then("se envían {int} correos con el asunto {string}")]
    public void EmailsSent(int count, string subject) => Assert.Equal(count, _f.Email.Sent.Count(m => m.Subject == subject));

    [Then("los avisos de {string} quedan con estado de correo {string}")]
    public void EmailState(string title, string state) =>
        Assert.All(_f.Repository.Stored.Where(n => n.Title == title), n => Assert.Equal(state, EnumText.ToText(n.EmailState)));

    [Then("hay {int} recordatorios en estado {string}")]
    public void RemindersInState(int count, string state) =>
        Assert.Equal(count, _f.Repository.Stored.Count(n => n.Type == NotificationType.Recordatorio && EnumText.ToText(n.State) == state));

    [Then("hay {int} recordatorios {string} en estado {string}")]
    public void RemindersWithTitle(int count, string title, string state) =>
        Assert.Equal(count, _f.Repository.Stored.Count(n => n.Type == NotificationType.Recordatorio && n.Title == title && EnumText.ToText(n.State) == state));

    [Then("el prestatario todavía no ve recordatorios en su lista")]
    public async Task NoRemindersVisible() =>
        Assert.DoesNotContain(await _f.Notifications.ListAsync(_f.Borrower, default), n => n.Type == NotificationType.Recordatorio);

    [Then("el prestatario ve el aviso {string} en su lista")]
    public async Task VisibleInList(string title) =>
        Assert.Contains(await _f.Notifications.ListAsync(_f.Borrower, default), n => n.Title == title);

    [When("llega el {string} y corre el programador de recordatorios")]
    public async Task TimeArrives(string instant)
    {
        _f.Time.Now = DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);
        await _f.Reminders.ActivateDueAsync(100, default);
        await _f.Notifications.DispatchPendingEmailsAsync(100, default);
    }
}
