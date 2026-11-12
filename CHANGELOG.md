# Changelog

## Unreleased

### Corregido
- Puertos locales que chocaban con Identidad (MySQL 3309): ahora son 3312, 5676 y 15676, configurables con `MYSQL_HOST_PORT`, `RABBITMQ_HOST_PORT` y `RABBITMQ_MANAGEMENT_HOST_PORT`.
- `.env.example` alineado al equipo: Firebase `lendup-desarrollo`, Identidad en `:8081`, frontend en `:3000`.
- Colección Postman: los eventos simulados respetan las variables de entorno (`pm.variables`).

### Agregado
- Opción de broker compartido (`RABBITMQ_HOST`/`RABBITMQ_PORT`), para recibir los eventos reales de Reservas y Pagos.
- Postman: login de Firebase y `usuario.id` desde Identidad, para que los eventos simulados lleguen a tu usuario.


- Servicio inicial de Notificaciones en C# · ASP.NET Core (.NET 10) con capas Domain, Application, Infrastructure e Interfaces y los componentes de la Fig. 20.
- Base propia `lendup_notificaciones` con migraciones de EF Core (Pomelo): tabla `notificaciones` con los índices del monolito y la inbox `processed_events`.
- Endpoints compatibles con el frontend: `GET /api/v1/notificaciones` y `PATCH /api/v1/notificaciones/{id}`, con el mismo formato de error.
- Consumo de eventos de Reservas y de `pagos.*.v1` con inbox, unicidad por destinatario y clave, reintentos y DLQ; routing keys configurables para Préstamos y Evidencias.
- Recordatorios de entrega y devolución (RF22) con `ReminderScheduler` (`BackgroundService`) y cancelación al cancelar la reserva.
- Correo con SendGrid y reintentos hasta `FALLIDO`; registro en el log sin API key.
- Validación del Firebase ID token en el servicio, health checks de liveness y readiness, Swagger opcional, imagen sin privilegios.
- Pruebas xUnit unitarias y de contrato, Testcontainers (MySQL y RabbitMQ) y escenarios Reqnroll en español (RF48 y RF22).
- Colección Postman, OpenAPI, ADRs y workflows de CI y publicación en Docker Hub.
