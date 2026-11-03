# Arquitectura

## Alcance

Notificaciones posee la tabla `notificaciones` (Fig. 13) y la inbox `processed_events` en su base `lendup_notificaciones`. Usuarios, reservas, préstamos, transacciones e incidencias son referencias externas por UUID; no hay claves foráneas a otros servicios. El servicio **no publica eventos** (Fig. 20), por eso no tiene outbox; tampoco tiene `registro_auditoria`, que el informe reserva para servicios con operaciones críticas (Reservas, Préstamos, Pagos, Evidencias).

Tecnología: C# · ASP.NET Core sobre .NET 10 (LTS), EF Core con Pomelo para MySQL 8.4, RabbitMQ.Client 7, FirebaseAdmin, Swashbuckle, SDK de SendGrid. Contratos REST y de eventos idénticos a los de los servicios Java.

## Capas y componentes (Fig. 20)

```
src/LendUp.Notifications.Api
├── Domain           Notification (agregado), NotificationValidator, ReminderPolicy, enums
├── Application      NotificationApplicationService, ReminderApplicationService, EventNotificationCatalog, puertos
├── Infrastructure   Persistence (EF Core + migraciones), Messaging, Email, Identity, Scheduling, Security, Health, Configuration
└── Interfaces/Rest  NotificationsController, DTO, manejo de errores, correlation id
```

| Componente de la Fig. 20 | Clase |
|---|---|
| NotificacionController | `Interfaces/Rest/NotificationsController` |
| NotificacionApplicationService | `Application/NotificationApplicationService` |
| NotificacionValidator | `Domain/NotificationValidator` |
| Notification (aggregate root) | `Domain/Notification` |
| ReminderScheduler | `Infrastructure/Scheduling/ReminderScheduler` (`BackgroundService`) |
| RecordatorioApplicationService | `Application/ReminderApplicationService` |
| NotificacionRepository | `Application/INotificationRepository` |
| NotificacionRepositoryImpl | `Infrastructure/Persistence/NotificationRepository` (EF Core) |
| MessageBrokerAdapter | `Infrastructure/Messaging/MessageBrokerAdapter` (`BackgroundService`) + `EventProcessor` |
| EmailAdapter | `Infrastructure/Email/SendGridEmailAdapter` (y `LoggingEmailSender` en local) |

Adicionales: `EventNotificationCatalog` (traduce cada evento a avisos en español), `ReminderPolicy` (horarios de recordatorio) e `IdentityRestAdapter` (UID → usuario y correo).

## Flujos

1. **Evento → aviso (RF48).** `MessageBrokerAdapter` consume `notifications.events.v1` con ack manual. `EventProcessor` abre una transacción: si el `event_id` ya está en `processed_events` lo descarta; si no, crea un aviso `DISPONIBLE` por destinatario y registra el `event_id`, todo junto. Además, `(destinatario_usuario_id, clave_notificacion)` es único: el mismo hecho con otro `event_id` tampoco se duplica.
2. **Reserva confirmada → recordatorios (RF22).** Se crean 4 recordatorios `PROGRAMADA` (entrega y devolución, para prestatario y prestamista). `programada_para` = fecha de la reserva (hora de Lima convertida a UTC) menos `REMINDER_*_LEAD_HOURS` (24 h por defecto); si ya pasó, sale de inmediato.
3. **Reserva cancelada.** El evento de Reservas solo trae `reserva_id`; los destinatarios se toman de los avisos ya creados para esa reserva. Se avisa "Reserva cancelada" y los recordatorios pendientes pasan a `CANCELADA`.
4. **ReminderScheduler.** Cada `REMINDER_SCHEDULER_DELAY_MS` pasa a `DISPONIBLE` los recordatorios vencidos y envía los correos pendientes de avisos visibles.
5. **Correo.** Todos los avisos llevan `enviar_correo = true`. El correo sale con SendGrid; si falla se reintenta con espera creciente (0, 1, 5, 30 min, 2 h) hasta `EMAIL_MAX_ATTEMPTS` y luego queda `FALLIDO`. Sin `SENDGRID_API_KEY` se registra en el log y queda `ENVIADO` (R24: LendUp decide cuándo enviar; SendGrid solo entrega).

## Mensajería

Exchange `lendup.domain.events` (topic), DLX `lendup.domain.events.dlx`, cola `notifications.events.v1` y DLQ `notifications.events.v1.dlq`. Bindings: `reservas.solicitud-creada.v1`, `reservas.confirmada.v1`, `reservas.cancelada.v1`, `pagos.*.v1` y las routing keys configurables de Préstamos y Evidencias. Cada mensaje se reintenta 3 veces (0,5 s, 1 s); si sigue fallando, o no cumple el envelope, se rechaza sin reencolar y va a la DLQ. Si RabbitMQ cae, el consumidor reconecta cada 5 s y readiness responde `DOWN`.

## Seguridad

Esquema de autenticación propio (`FirebaseAuthenticationHandler`): valida el ID token con FirebaseAdmin, incluida la revocación, exige `email_verified` y nunca lee roles de headers públicos. La política por defecto exige usuario autenticado en todo salvo liveness y readiness. El UID se resuelve a `usuario_id` con Identidad y solo se muestran o modifican avisos del propio usuario. Sin `FIREBASE_PROJECT_ID` o sin credenciales el servicio no arranca (no hay bypass). El contenedor corre sin privilegios y con filesystem de solo lectura.

## Decisiones y desvíos

- `LEIDA` se agregó al enum `estado` de la Fig. 13 porque el monolito y el frontend lo usan.
- Ids `char(36)`, como el monolito y la Fig. 13.
- EF Core 9 con Pomelo 9 sobre .NET 10 ([ADR 0001](decisions/0001-dotnet-stack-and-pomelo.md)); MySqlConnector fijado en 2.6.2 por una vulnerabilidad de la 2.4.0.
- Recordatorios y correo ([ADR 0002](decisions/0002-reminders-and-email-delivery.md)).
- Los cambios de fecha de devolución (extensión o reprogramación) no reprograman el recordatorio de devolución: requiere que Préstamos publique la nueva fecha (contrato pendiente).
