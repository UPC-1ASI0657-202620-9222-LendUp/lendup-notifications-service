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
/// Routing keys consumidas. Reservas, Pagos y Préstamos ya publican las suyas. Las de Evidencias (aún sin microservicio)
/// y la de Reputación (provisional según su repositorio) se configuran por variables de entorno.
/// </summary>
public sealed record EventRoutingOptions
{
    public const string RequestCreated = "reservas.solicitud-creada.v1";
    public const string RequestRejected = "reservas.solicitud-rechazada.v1";
    public const string RequestCancelled = "reservas.solicitud-cancelada.v1";
    public const string ReservationConfirmed = "reservas.confirmada.v1";
    public const string ReservationCancelled = "reservas.cancelada.v1";
    public const string PaymentsPattern = "pagos.*.v1";
    public const string ObjectDelivered = "prestamos.objeto-entregado.v1";
    public const string ReturnRegistered = "prestamos.devolucion-registrada.v1";
    public const string LoanOverdue = "prestamos.vencido.v1";
    public const string CancellationRejected = "prestamos.cancelacion-rechazada.v1";
    public const string RecoveryPending = "prestamos.recuperacion-pendiente.v1";
    public const string ExtensionRequested = "prestamos.extension-solicitada.v1";
    public const string ExtensionRejected = "prestamos.extension-rechazada.v1";
    public const string RescheduleProposed = "prestamos.reprogramacion-propuesta.v1";
    public const string RescheduleRejected = "prestamos.reprogramacion-rechazada.v1";
    public const string ReturnDateUpdated = "prestamos.fecha-devolucion-actualizada.v1";

    public string LoanActivated { get; init; } = "prestamos.activado.v1";
    public string ExtensionAccepted { get; init; } = "prestamos.extension-aceptada.v1";
    public string LoanFinished { get; init; } = "prestamos.finalizado.v1";
    public string IncidentReported { get; init; } = "evidencias.incidencia-reportada.v1";
    public string IncidentResolved { get; init; } = "evidencias.incidencia-resuelta.v1";
    public string RatingRegistered { get; init; } = "reputation.rating-registered.v1";

    public IReadOnlyList<string> Bindings() =>
        new[]
            {
                RequestCreated, RequestRejected, RequestCancelled, ReservationConfirmed, ReservationCancelled, PaymentsPattern,
                ObjectDelivered, LoanActivated, ReturnRegistered, LoanOverdue, LoanFinished, CancellationRejected, RecoveryPending,
                ExtensionRequested, ExtensionAccepted, ExtensionRejected, RescheduleProposed, RescheduleRejected, ReturnDateUpdated,
                IncidentReported, IncidentResolved, RatingRegistered,
            }
            .Where(key => !string.IsNullOrWhiteSpace(key)).Distinct().ToList();
}
