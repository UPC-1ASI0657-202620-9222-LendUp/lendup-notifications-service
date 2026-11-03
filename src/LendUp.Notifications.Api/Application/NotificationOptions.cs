namespace LendUp.Notifications.Api.Application;

/// <summary>Reglas de negocio configurables del servicio.</summary>
public sealed record NotificationOptions
{
    public TimeSpan DeliveryReminderLead { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan ReturnReminderLead { get; init; } = TimeSpan.FromHours(24);
    public int EmailMaxAttempts { get; init; } = 5;
    public int ListLimit { get; init; } = 200;
}

/// <summary>
/// Routing keys consumidas. Reservas y Pagos ya publican las suyas; las de Préstamos y Evidencias son contratos
/// pendientes y se configuran por variables de entorno.
/// </summary>
public sealed record EventRoutingOptions
{
    public const string RequestCreated = "reservas.solicitud-creada.v1";
    public const string ReservationConfirmed = "reservas.confirmada.v1";
    public const string ReservationCancelled = "reservas.cancelada.v1";
    public const string PaymentsPattern = "pagos.*.v1";

    public string LoanActivated { get; init; } = "prestamos.activado.v1";
    public string ExtensionAccepted { get; init; } = "prestamos.extension-aceptada.v1";
    public string LoanFinished { get; init; } = "prestamos.finalizado.v1";
    public string IncidentReported { get; init; } = "evidencias.incidencia-reportada.v1";
    public string IncidentResolved { get; init; } = "evidencias.incidencia-resuelta.v1";

    public IReadOnlyList<string> Bindings() =>
        new[] { RequestCreated, ReservationConfirmed, ReservationCancelled, PaymentsPattern, LoanActivated, ExtensionAccepted, LoanFinished, IncidentReported, IncidentResolved }
            .Where(key => !string.IsNullOrWhiteSpace(key)).Distinct().ToList();
}
