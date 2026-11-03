# Entrega a AWS

## Artefacto

Imagen: `DOCKERHUB_USERNAME/lendup-notifications-service:<semver|sha>`, `linux/amd64`, puerto 8083, liveness `/actuator/health/liveness`, readiness `/actuator/health/readiness` (mismas rutas que los servicios Java). Corre como el usuario sin privilegios de la imagen oficial de ASP.NET (UID 1654) y soporta filesystem raíz de solo lectura con `/tmp` temporal.

## Configuración

Obligatorias: `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USERNAME`, `DB_PASSWORD`, `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USERNAME`, `RABBITMQ_PASSWORD`, `FIREBASE_PROJECT_ID`, credenciales ADC de Firebase (`GOOGLE_APPLICATION_CREDENTIALS`), `CORS_ORIGINS`, `IDENTITY_SERVICE_BASE_URL`, `IDENTITY_SERVICE_TOKEN`, `IDENTITY_FIREBASE_USER_URI_TEMPLATE`, `IDENTITY_USER_EMAIL_URI_TEMPLATE`, `SENDGRID_API_KEY` y `SENDGRID_FROM_EMAIL` (remitente verificado en SendGrid).

Opcionales: `SERVER_PORT=8083`, `DB_MIGRATE_ON_STARTUP=true`, `SENDGRID_FROM_NAME`, `EMAIL_MAX_ATTEMPTS=5`, `REMINDER_DELIVERY_LEAD_HOURS=24`, `REMINDER_RETURN_LEAD_HOURS=24`, `REMINDER_SCHEDULER_DELAY_MS=60000`, `REMINDER_SCHEDULER_BATCH_SIZE=100`, `IDENTITY_*_TIMEOUT_MS`, routing keys de Préstamos y Evidencias cuando existan. Mantener `SWAGGER_ENABLED=false` y `SWAGGER_PUBLIC=false` en producción. Secretos en Secrets Manager/Parameter Store, nunca en la imagen.

## Base de datos y broker

Base `lendup_notificaciones` (MySQL 8+, InnoDB) en la instancia compartida del entorno, con usuario limitado a ese esquema. Las migraciones de EF Core se aplican al arrancar (`DB_MIGRATE_ON_STARTUP`); en producción conviene ejecutarlas en una única tarea antes de escalar y arrancar las réplicas con `DB_MIGRATE_ON_STARTUP=false`. Script SQL equivalente: `dotnet ef migrations script --project src/LendUp.Notifications.Api`.

Con varias réplicas, el `ReminderScheduler` corre en todas; la activación de recordatorios es idempotente, pero un correo podría enviarse dos veces si dos réplicas toman el mismo aviso a la vez. Para el sprint se recomienda una réplica del scheduler (`REMINDER_SCHEDULER_ENABLED=false` en las demás).

RabbitMQ con TLS en producción; la consola 15672 no se expone. MySQL y RabbitMQ en subredes privadas.

## Gateway

Enrutar `/api/v1/notificaciones` y subrutas a este servicio, propagando `Authorization` y `X-Correlation-ID`. Salida HTTPS hacia `api.sendgrid.com`.

## Despliegue y rollback

1. Aprovisionar base, broker y secretos; verificar el remitente en SendGrid.
2. Ejecutar migraciones, desplegar una instancia, comprobar readiness y luego escalar.
3. Publicar un evento de prueba y verificar el aviso, el correo y la DLQ.

Rollback: volver al digest anterior sin revertir migraciones (deben ser compatibles hacia atrás).
