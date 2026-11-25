# Eventos

Notificaciones **solo consume**; no publica eventos (Fig. 20). Exchange durable `lendup.domain.events` (topic), DLX `lendup.domain.events.dlx`, cola `notifications.events.v1`, DLQ `notifications.events.v1.dlq`. Envelope estándar: `event_id`, `event_type`, `event_version`, `aggregate_version`, `aggregate_id`, `occurred_at` UTC, `correlation_id`, `payload`. Schemas y ejemplos en `contracts/events/v1`.

| Routing key | Productor | Aviso (`titulo`) | Destinatarios | `origen_tipo` |
|---|---|---|---|---|
| `reservas.solicitud-creada.v1` | Reservas | Nueva solicitud / Solicitud enviada | Prestamista / Prestatario | SOLICITUD |
| `reservas.solicitud-rechazada.v1` | Reservas | Solicitud rechazada (con motivo) | Prestatario | SOLICITUD |
| `reservas.solicitud-cancelada.v1` | Reservas | Solicitud cancelada (con motivo) | Prestamista | SOLICITUD |
| `reservas.confirmada.v1` | Reservas | Reserva confirmada + recordatorios de entrega y devolución | Ambos | RESERVA |
| `reservas.cancelada.v1` | Reservas | Reserva cancelada (cancela recordatorios) | Quienes recibieron avisos de esa reserva | RESERVA |
| `pagos.pago-confirmado.v1` | Pagos | Pago confirmado / Pago recibido (o de extensión) | Ambos | PAGO |
| `pagos.pago-rechazado.v1` | Pagos | Pago no confirmado, Garantía no confirmada o Pago de extensión no confirmado | Prestatario | PAGO |
| `pagos.garantia-constituida.v1` | Pagos | Garantía constituida | Ambos | GARANTIA |
| `pagos.tarifa-liberada.v1` | Pagos | Tarifa liberada (monto neto) | Prestamista | PAGO |
| `pagos.garantia-devuelta.v1` | Pagos | Garantía devuelta | Prestatario | GARANTIA |
| `pagos.garantia-retenida.v1` | Pagos | Garantía retenida | Ambos | GARANTIA |
| `pagos.reembolso-procesado.v1` | Pagos | Reembolso procesado | Prestatario | PAGO |
| `pagos.reembolso-rechazado.v1` | Pagos | Reembolso no completado | Prestatario | PAGO |
| `prestamos.objeto-entregado.v1` | Préstamos | Objeto entregado (anula el recordatorio de entrega) | Prestatario | PRESTAMO |
| `prestamos.activado.v1` (`LOAN_ACTIVATED_ROUTING_KEY`) | Préstamos | Préstamo activo | Ambos | PRESTAMO |
| `prestamos.extension-solicitada.v1` | Préstamos | Extensión solicitada | Prestamista | EXTENSION |
| `prestamos.extension-aceptada.v1` (`EXTENSION_ACCEPTED_ROUTING_KEY`) | Préstamos | Extensión aceptada (monto por pagar) | Ambos | EXTENSION |
| `prestamos.extension-rechazada.v1` | Préstamos | Extensión rechazada (con motivo) | Prestatario | EXTENSION |
| `prestamos.reprogramacion-propuesta.v1` | Préstamos | Reprogramación propuesta | Prestatario | EXTENSION |
| `prestamos.reprogramacion-rechazada.v1` | Préstamos | Reprogramación rechazada (con motivo) | Prestamista | EXTENSION |
| `prestamos.fecha-devolucion-actualizada.v1` | Préstamos | Nueva fecha de devolución (mueve el recordatorio) | Ambos | EXTENSION |
| `prestamos.devolucion-registrada.v1` | Préstamos | Devolución registrada (anula el recordatorio de devolución) | Prestamista | PRESTAMO |
| `prestamos.vencido.v1` | Préstamos | Préstamo vencido | Ambos | PRESTAMO |
| `prestamos.finalizado.v1` (`LOAN_FINISHED_ROUTING_KEY`) | Préstamos | Préstamo finalizado | Ambos | PRESTAMO |
| `prestamos.cancelacion-rechazada.v1` | Préstamos | Cancelación no aceptada | Ambos | PRESTAMO |
| `reputation.rating-registered.v1` (`RATING_REGISTERED_ROUTING_KEY`) | Reputación | Nueva calificación | Evaluado | PRESTAMO |
| `evidencias.incidencia-reportada.v1` (`INCIDENT_REPORTED_ROUTING_KEY`) | Evidencias (sin microservicio) | Incidencia reportada | Ambos | INCIDENCIA |
| `evidencias.incidencia-resuelta.v1` (`INCIDENT_RESOLVED_ROUTING_KEY`) | Evidencias (sin microservicio) | Incidencia resuelta (mensaje = justificación) | Ambos | INCIDENCIA |

Los pagos se enlazan con el comodín `pagos.*.v1`; un tipo que no esté en el catálogo se marca procesado y se ignora. Los de Préstamos se enlazan uno por uno, para no hacer creer al productor que los demás tienen quien los reciba.

Reputación publica sus campos en la raíz del mensaje, sin `payload`: se aceptan así solo fuera de los contextos que usan el envelope estándar (`reservas`, `pagos`, `prestamos`, `evidencias`). Para que sus mensajes lleguen, Reputación debe usar `RABBITMQ_EXCHANGE=lendup.domain.events`.

## Contratos pendientes

Solo los de Evidencias, que aún no tiene microservicio: se espera `incidencia_id`, `prestatario_usuario_id` y `prestamista_usuario_id` (`consumed-events.schema.json`).

## Errores

- Envelope inválido (sin `event_id` UUID, `event_type` o `payload` objeto): directo a la DLQ, sin reintentos.
- Payload incompleto o fallo de base de datos: 3 intentos y luego DLQ. Los mensajes de la DLQ se pueden reenviar desde la consola de RabbitMQ.
