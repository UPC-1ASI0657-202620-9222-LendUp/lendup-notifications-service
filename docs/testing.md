# Pruebas

`dotnet test` (desde la raíz) ejecuta el proyecto `tests/LendUp.Notifications.Tests`:

- **Unitarias de dominio** (`Unit/Domain`): estados del agregado `Notification` (evento disponible, recordatorio programado, lectura solo desde `DISPONIBLE`, cancelación de recordatorios, reintentos de correo hasta `FALLIDO`), `NotificationValidator`, conversión de enums y `ReminderPolicy` (Lima → UTC, anticipación, recordatorio inmediato si ya pasó la hora).
- **Unitarias de aplicación** (`Unit/Application`): con repositorio en memoria, Identidad falsa y correo registrado: cada evento de Reservas, Pagos, Préstamos y Reputación (`LifecycleNotificationsTests`), recordatorios que se mueven o se anulan, dedupe por inbox y por clave, cancelación que encuentra destinatarios y cancela recordatorios, eventos con routing keys configurables, payload incompleto, envelope inválido, lista y marcado como leído (404/403/409), envío de correo, reintentos, modo log sin API key, y activación de recordatorios.
- **Contrato con el frontend** (`Contracts/FrontendContractTests`): la API real con `WebApplicationFactory` (sin MySQL ni RabbitMQ): campos que lee `mapNotification`, formato de error del monolito, seguridad (sin token, token inválido, correo no verificado, Basic) y liveness con `X-Correlation-ID`.
- **Aceptación (Reqnroll, `Features/*.feature`, Gherkin en español):** `RF48_NotificacionesPorEvento.feature` (22 escenarios) y `RF22_Recordatorios.feature` (6 escenarios).
- **Integración (Testcontainers, `Integration/`):** MySQL 8.4 y RabbitMQ reales con la aplicación completa: migraciones EF Core, consumo desde el broker con dedupe y consulta por la API, activación de recordatorios con la base real, rechazo de solicitud, cambio de fecha y calificación por el broker, unicidad `(destinatario, clave)` y mensaje inválido en la DLQ.

Las pruebas de integración usan `[SkippableFact]`: sin Docker se marcan omitidas. Una omisión no equivale a éxito. Con Docker activo se ejecutan las 108, sin omitidas (también corren en GitHub Actions).

Prueba manual mínima:

```powershell
dotnet test
docker compose up --build -d
Invoke-RestMethod http://localhost:8083/actuator/health/readiness
docker compose logs notifications --tail 100
docker compose down
```

La colección `docs/postman/lendup-notifications.postman_collection.json` sigue este orden:

1. **0b. Preparación:** token de Firebase (o pegar `firebase_token`) y `usuario.id` en Identidad.
2. **1. Eventos simulados:** se publican en RabbitMQ (consola en el puerto 15676). Cada uno debe responder `routed=true`.
3. **2. Notificaciones:** consultar y marcar los avisos.

Se corrió con newman sobre un broker compartido con Pagos: 13/13 verificaciones. También se comprobó que los avisos *Garantía constituida*, *Pago confirmado*, *Reembolso procesado* y *Garantía devuelta* se crean a partir de los eventos que publica Pagos.
