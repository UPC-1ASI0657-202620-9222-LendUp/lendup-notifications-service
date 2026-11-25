# ADR 0002: recordatorios programados y envío de correo con reintentos

Estado: aceptada.

## Contexto

RF22 pide recordatorios antes de la entrega y la devolución; RF48 pide avisos en la plataforma y por correo; R24 exige SendGrid y que LendUp decida cuándo enviar. El correo del destinatario vive en Identidad (contrato pendiente) y SendGrid puede fallar.

## Decisión

- Al recibir `reservas.confirmada.v1` se guardan los recordatorios como filas `RECORDATORIO` en estado `PROGRAMADA` con `programada_para` (UTC), uno por participante y fecha. No hay una cola de mensajes diferidos: la tabla es la agenda.
- Un `BackgroundService` (`ReminderScheduler`) revisa cada minuto los vencidos y los pasa a `DISPONIBLE`. Una reserva cancelada, o la entrega o devolución anticipada, pasa los pendientes que correspondan a `CANCELADA`.
- El correo se envía desde el mismo scheduler solo para avisos ya visibles, usando las columnas de la Fig. 13 (`estado_correo`, `intentos_correo`, `ultimo_intento_correo_en`, `ultimo_error_correo`): reintentos espaciados y `FALLIDO` tras `EMAIL_MAX_ATTEMPTS`. Así un fallo de SendGrid o de Identidad nunca bloquea el consumo de eventos.
- Sin `SENDGRID_API_KEY` (desarrollo local) el correo se registra en el log.

## Consecuencias

Un recordatorio se muestra con hasta un minuto de retraso (configurable). Con varias réplicas conviene que una sola ejecute el scheduler. Con `prestamos.fecha-devolucion-actualizada.v1` el recordatorio de devolución pendiente se anula y se crea otro en la nueva fecha, con una clave propia del cambio: así queda el historial y sirve aunque la fecha vuelva a una anterior.
