namespace LendUp.Notifications.Api.Domain;

// Los valores se guardan y exponen en MAYÚSCULAS_CON_GUION_BAJO (EnumText), igual que en el monolito y la Fig. 13.

/// <summary>EVENTO para avisos de eventos (RF48) y RECORDATORIO para fechas programadas (RF22).</summary>
public enum NotificationType { Evento, Recordatorio }

/// <summary>Fig. 13 más LEIDA, que usan el monolito y el frontend (ver PENDIENTES.md, sección G).</summary>
public enum NotificationState { Programada, Disponible, Leida, Cancelada }

public enum EmailState { NoAplica, Pendiente, Enviado, Fallido }

public enum OriginType { Solicitud, Reserva, Prestamo, Pago, Garantia, Extension, Incidencia }

/// <summary>Convierte enums PascalCase al texto persistido (NoAplica ⇄ NO_APLICA).</summary>
public static class EnumText
{
    public static string ToText<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) builder.Append('_');
            builder.Append(char.ToUpperInvariant(name[i]));
        }
        return builder.ToString();
    }

    public static TEnum Parse<TEnum>(string text) where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            if (ToText(value) == text) return value;
        }
        throw new ArgumentException($"Valor no válido para {typeof(TEnum).Name}: {text}");
    }
}
