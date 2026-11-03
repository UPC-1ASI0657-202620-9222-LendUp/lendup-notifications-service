# Eventos

Notificaciones **solo consume**; no publica eventos (Fig. 20). Exchange durable `lendup.domain.events` (topic), DLX `lendup.domain.events.dlx`, cola `notifications.events.v1`, DLQ `notifications.events.v1.dlq`. Envelope estándar: `event_id`, `event_type`, `event_version`, `aggregate_version`, `aggregate_id`, `occurred_at` UTC, `correlation_id`, `payload`. Schemas y ejemplos en `contracts/events/v1`.

| Routing key | Productor | Estado | Avisos (`titulo`) | Destinatarios | `origen_tipo` |
|---|---|---|---|---|---|
| `reservas.solicitud-creada.v1` | Reservas | Disponible | Nueva solicitud | Prestamista | SOLICITUD |
| `reservas.confirmada.v1` | Reservas | Disponible | Reserva confirmada + recordatorios de entrega y devolución | Ambos | RESERVA |
| `reservas.cancelada.v1` | Reservas | Disponible | Reserva cancelada (cancela recordatorios) | Quienes recibieron avisos de esa reserva | RESERVA |
| `pagos.pago-confirmado.v1` | Pagos | Disponible | Pago confirmado / Pago recibido (o de extensión) | Ambos | PAGO |
| `pagos.pago-rechazado.v1` | Pagos | Disponible | Pago no confirmado | Prestatario | PAGO |
| `pagos.garantia-constituida.v1` | Pagos | Disponible | Garantía constituida | Ambos | GARANTIA |
| `pagos.tarifa-liberada.v1` | Pagos | Disponible | Tarifa liberada | Prestamista | PAGO |
| `pagos.garantia-devuelta.v1` | Pagos | Disponible | Garantía devuelta | Prestatario | GARANTIA |
| `pagos.garantia-retenida.v1` | Pagos | Disponible | Garantía retenida | Ambos | GARANTIA |
| `pagos.reembolso-procesado.v1` | Pagos | Disponible | Reembolso procesado | Prestatario | PAGO |
| `LOAN_ACTIVATED_ROUTING_KEY` (`prestamos.activado.v1`) | Préstamos | **Pendiente** | Préstamo activo | Ambos | PRESTAMO |
| `EXTENSION_ACCEPTED_ROUTING_KEY` (`prestamos.extension-aceptada.v1`) | Préstamos | **Pendiente** | Extensión aceptada | Ambos | EXTENSION |
| `LOAN_FINISHED_ROUTING_KEY` (`prestamos.finalizado.v1`) | Préstamos | **Pendiente** | Préstamo finalizado | Ambos | PRESTAMO |
| `INCIDENT_REPORTED_ROUTING_KEY` (`evidencias.incidencia-reportada.v1`) | Evidencias | **Pendiente** | Incidencia reportada | Ambos | INCIDENCIA |
| `INCIDENT_RESOLVED_ROUTING_KEY` (`evidencias.incidencia-resuelta.v1`) | Evidencias | **Pendiente** | Incidencia resuelta (mensaje = justificación) | Ambos | INCIDENCIA |

Los pagos se enlazan con el binding comodín `pagos.*.v1`; un tipo de evento de Pagos que no esté en el catálogo se marca procesado y se ignora. Los textos de solicitudes, reservas e incidencias son los del monolito.

## Contratos pendientes

Los productores de Préstamos y Evidencias **aún no existen**. Notificaciones espera que sus payloads incluyan `prestatario_usuario_id` y `prestamista_usuario_id` (como ya hacen los eventos de Pagos), además del id de la entidad (`prestamo_id`, `cambio_fecha_prestamo_id`, `incidencia_id`). Están definidos en `consumed-events.schema.json` y se prueban con los ejemplos de `examples.json` (pruebas unitarias, escenarios Reqnroll y la carpeta "Eventos simulados" de la colección Postman). Las routing keys por defecto coinciden con las que espera Pagos.

## Errores

- Envelope inválido (sin `event_id` UUID, `event_type` o `payload` objeto): directo a la DLQ, sin reintentos.
- Payload incompleto o fallo de base de datos: 3 intentos y luego DLQ. Los mensajes de la DLQ se pueden reenviar desde la consola de RabbitMQ.
