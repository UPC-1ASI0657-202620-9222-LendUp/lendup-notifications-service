using LendUp.Notifications.Api.Domain;

namespace LendUp.Notifications.Tests.Unit.Domain;

public class NotificationTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Origin = Guid.NewGuid();

    private static Notification Event(bool sendEmail = true) =>
        Notification.ForEvent(User, $"ReservaConfirmada:{Origin}", OriginType.Reserva, Origin, "evt-1", "Reserva confirmada", "Tu solicitud fue aceptada", sendEmail, Now);

    private static Notification Reminder(DateTime at) =>
        Notification.ForReminder(User, $"RecordatorioEntrega:{Origin}", OriginType.Reserva, Origin, "evt-1", "Recordatorio de entrega", "Mañana", true, at, Now);

    [Fact]
    public void EventoQuedaDisponibleConCorreoPendiente()
    {
        var n = Event();
        Assert.Equal(NotificationType.Evento, n.Type);
        Assert.Equal(NotificationState.Disponible, n.State);
        Assert.Equal(Now, n.AvailableAt);
        Assert.Equal(EmailState.Pendiente, n.EmailState);
        Assert.True(n.EmailReady);
        Assert.Equal(EmailState.NoAplica, Event(sendEmail: false).EmailState);
    }

    [Fact]
    public void SoloUnAvisoDisponiblePuedeLeerse()
    {
        var n = Event();
        n.MarkRead(Now.AddMinutes(1));
        Assert.Equal(NotificationState.Leida, n.State);
        Assert.Equal(Now.AddMinutes(1), n.ReadAt);
        Assert.Throws<InvalidOperationException>(() => n.MarkRead(Now));
        Assert.Throws<InvalidOperationException>(() => Reminder(Now.AddDays(1)).MarkRead(Now));
    }

    [Fact]
    public void RecordatorioProgramadoSeActivaASuHoraYNoEnviaCorreoAntes()
    {
        var n = Reminder(Now.AddHours(2));
        Assert.Equal(NotificationState.Programada, n.State);
        Assert.False(n.EmailReady);
        Assert.False(n.IsDue(Now.AddHours(1)));
        Assert.True(n.IsDue(Now.AddHours(2)));
        n.MakeAvailable(Now.AddHours(2));
        Assert.Equal(NotificationState.Disponible, n.State);
        Assert.True(n.EmailReady);
        Assert.Throws<InvalidOperationException>(() => n.MakeAvailable(Now));
    }

    [Fact]
    public void CancelarSoloAfectaRecordatoriosProgramados()
    {
        var pending = Reminder(Now.AddDays(1));
        Assert.True(pending.CancelReminder(Now));
        Assert.Equal(NotificationState.Cancelada, pending.State);
        Assert.Equal(EmailState.NoAplica, pending.EmailState);
        var shown = Reminder(Now);
        shown.MakeAvailable(Now);
        Assert.False(shown.CancelReminder(Now));
        Assert.Equal(NotificationState.Disponible, shown.State);
    }

    [Fact]
    public void CorreoFallidoSeReintentaHastaElMaximo()
    {
        var n = Event();
        n.RegisterEmailFailure("timeout", Now, maxAttempts: 2);
        Assert.Equal(EmailState.Pendiente, n.EmailState);
        Assert.Equal(1, n.EmailAttempts);
        n.RegisterEmailFailure("timeout", Now, maxAttempts: 2);
        Assert.Equal(EmailState.Fallido, n.EmailState);
        Assert.Equal("timeout", n.LastEmailError);
        Assert.Throws<InvalidOperationException>(() => n.RegisterEmailSent(Now));
    }

    [Fact]
    public void CorreoEnviadoRegistraFechaEIntento()
    {
        var n = Event();
        n.RegisterEmailSent(Now);
        Assert.Equal(EmailState.Enviado, n.EmailState);
        Assert.Equal(1, n.EmailAttempts);
        Assert.Equal(Now, n.EmailSentAt);
    }

    [Theory]
    [InlineData("", "Título", "Mensaje")]
    [InlineData("clave", "", "Mensaje")]
    [InlineData("clave", "Título", " ")]
    public void ValidadorRechazaDatosIncompletos(string key, string title, string message) =>
        Assert.Throws<ArgumentException>(() => Notification.ForEvent(User, key, OriginType.Pago, Origin, null, title, message, true, Now));

    [Fact]
    public void ValidadorExigeDestinatarioOrigenYFechaUtc()
    {
        Assert.Throws<ArgumentException>(() => Notification.ForEvent(Guid.Empty, "k", OriginType.Pago, Origin, null, "t", "m", true, Now));
        Assert.Throws<ArgumentException>(() => Notification.ForEvent(User, "k", OriginType.Pago, Guid.Empty, null, "t", "m", true, Now));
        Assert.Throws<ArgumentException>(() => Notification.ForEvent(User, "k", OriginType.Pago, Origin, null, new string('x', 181), "m", true, Now));
        Assert.Throws<ArgumentException>(() => Notification.ForReminder(User, "k", OriginType.Reserva, Origin, null, "t", "m", true, DateTime.SpecifyKind(Now, DateTimeKind.Local), Now));
    }

    [Theory]
    [InlineData(EmailState.NoAplica, "NO_APLICA")]
    [InlineData(NotificationState.Programada, "PROGRAMADA")]
    [InlineData(OriginType.Garantia, "GARANTIA")]
    public void EnumsSePersistenEnMayusculasConGuionBajo(Enum value, string text)
    {
        var method = typeof(EnumText).GetMethod(nameof(EnumText.ToText))!.MakeGenericMethod(value.GetType());
        Assert.Equal(text, method.Invoke(null, [value]));
        Assert.Equal(EmailState.NoAplica, EnumText.Parse<EmailState>("NO_APLICA"));
        Assert.Throws<ArgumentException>(() => EnumText.Parse<EmailState>("OTRO"));
    }
}
