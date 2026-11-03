namespace LendUp.Notifications.Api.Domain;

/// <summary>
/// Calcula cuándo mostrar los recordatorios de entrega y devolución (RF22). Los periodos de la reserva llegan como
/// hora local de negocio America/Lima sin offset; programada_para se guarda en UTC.
/// </summary>
public sealed class ReminderPolicy
{
    private static readonly TimeZoneInfo Lima = ResolveLima();
    private readonly TimeSpan _deliveryLead;
    private readonly TimeSpan _returnLead;

    public ReminderPolicy(TimeSpan deliveryLead, TimeSpan returnLead)
    {
        if (deliveryLead < TimeSpan.Zero || returnLead < TimeSpan.Zero) throw new ArgumentException("La anticipación no puede ser negativa");
        _deliveryLead = deliveryLead;
        _returnLead = returnLead;
    }

    public static DateTime LimaToUtc(DateTime localDateTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified), Lima);

    /// <summary>Si la anticipación ya pasó (reserva para dentro de pocas horas) el recordatorio sale de inmediato.</summary>
    public DateTime DeliveryReminderAt(DateTime fromLocal, DateTime nowUtc) => NotBefore(LimaToUtc(fromLocal) - _deliveryLead, nowUtc);

    public DateTime ReturnReminderAt(DateTime toLocal, DateTime nowUtc) => NotBefore(LimaToUtc(toLocal) - _returnLead, nowUtc);

    private static DateTime NotBefore(DateTime value, DateTime nowUtc) => value < nowUtc ? nowUtc : value;

    private static TimeZoneInfo ResolveLima()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
        // Perú no usa horario de verano: si el sistema no trae la base de zonas (imagen mínima), UTC-5 es equivalente.
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("America/Lima", TimeSpan.FromHours(-5), "America/Lima", "America/Lima");
        }
    }
}
