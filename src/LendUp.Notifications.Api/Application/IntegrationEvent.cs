using System.Globalization;
using System.Text.Json;

namespace LendUp.Notifications.Api.Application;

/// <summary>Envelope estándar de lendup.domain.events ya validado, con el payload sin interpretar.</summary>
public sealed record IntegrationEvent(Guid EventId, string EventType, string RoutingKey, Guid AggregateId, long AggregateVersion,
    string CorrelationId, JsonElement Payload)
{
    public string Text(string name)
    {
        if (Payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        throw new ArgumentException($"Evento {EventType} sin {name}");
    }

    public string? OptionalText(string name) =>
        Payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText())
            : null;

    public Guid Id(string name) => Guid.TryParse(Text(name), out var id) ? id : throw new ArgumentException($"Evento {EventType} con {name} inválido");

    public decimal? OptionalAmount(string name)
    {
        var text = OptionalText(name);
        return text is null ? null : decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public DateTime LocalDateTime(string name) => DateTime.Parse(Text(name), CultureInfo.InvariantCulture, DateTimeStyles.None);
}
