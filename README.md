# LendUp Notifications Service

Microservicio C# · ASP.NET Core (.NET 10 LTS) para el bounded context de Notificaciones. Conserva las dos rutas del monolito consumidas por el frontend (`GET /api/v1/notificaciones` y `PATCH /api/v1/notificaciones/{id}`), genera avisos a partir de los eventos de Reservas y de Pagos (RF48), programa recordatorios de entrega y devolución (RF22) y envía correos con SendGrid. Usa MySQL/InnoDB con migraciones de EF Core (Pomelo), RabbitMQ con inbox y DLQ, y valida el Firebase ID token en el propio servicio.

## Estado y límites

- **No publica eventos.** Consume `reservas.solicitud-creada.v1`, `reservas.confirmada.v1`, `reservas.cancelada.v1`, `pagos.*.v1` y, cuando existan, los eventos de Préstamos y Evidencias (routing keys configurables; contratos propuestos en `contracts/events/v1`). Ver [eventos](docs/events.md).
- **Identidad:** UID → `usuario_id` con `IDENTITY_FIREBASE_USER_URI_TEMPLATE` (igual que Pagos) y `usuario_id` → correo con `IDENTITY_USER_EMAIL_URI_TEMPLATE`. Ambos contratos están pendientes con ese equipo. Sin el primero, las rutas responden `503`.
- **Correo:** con `SENDGRID_API_KEY` se envía por SendGrid con reintentos; sin ella (local) el correo se registra en el log.
- **EF Core 9 + Pomelo 9** sobre .NET 10, porque Pomelo aún no publica versión para EF Core 10 ([ADR 0001](docs/decisions/0001-dotnet-stack-and-pomelo.md)).

Detalles: [arquitectura](docs/architecture.md), [API](docs/api-contract.md), [pruebas](docs/testing.md), [AWS](docs/aws-handoff.md), [migración](docs/migration-from-monolith.md).

## Inicio local

Requisitos: .NET SDK 10 (ver `global.json`), Docker con Compose v2 y credencial Firebase de desarrollo.

```powershell
Copy-Item .env.example .env
New-Item -ItemType Directory -Force .secrets
# Colocar una credencial de desarrollo en .secrets/firebase-service-account.json; nunca versionarla.
dotnet test
docker compose up --build
```

API: `http://localhost:8083/api/v1`; readiness: `http://localhost:8083/actuator/health/readiness`. MySQL en `127.0.0.1:3309`, RabbitMQ en `127.0.0.1:5674` y su consola en `http://localhost:15674` (puertos distintos a Reservas y Pagos para poder levantar los tres). Para recibir los eventos reales de Reservas y Pagos, los tres servicios deben apuntar al mismo broker.

Para ejecutar fuera de Docker (con MySQL y RabbitMQ del compose):

```powershell
docker compose up -d mysql rabbitmq
$env:DB_PASSWORD="change-me"; $env:RABBITMQ_USERNAME="lendup_notificaciones"; $env:RABBITMQ_PASSWORD="change-me"
$env:FIREBASE_PROJECT_ID="tu-proyecto"; $env:GOOGLE_APPLICATION_CREDENTIALS="$PWD\.secrets\firebase-service-account.json"
dotnet run --project src/LendUp.Notifications.Api
```

Swagger está deshabilitado por defecto; para desarrollo local: `SWAGGER_ENABLED=true` (el documento `/swagger/v1/swagger.json` requiere token salvo `SWAGGER_PUBLIC=true`, que no debe usarse en producción). El contrato también está en [`contracts/openapi/notifications-v1.yaml`](contracts/openapi/notifications-v1.yaml).

Migraciones: se aplican al arrancar (`DB_MIGRATE_ON_STARTUP=true`). Para crear una nueva: `dotnet tool restore` y `dotnet ef migrations add <Nombre> --project src/LendUp.Notifications.Api --output-dir Infrastructure/Persistence/Migrations`.

## Imagen

```powershell
docker build --platform linux/amd64 -t lendup-notifications-service:local .
docker run --rm -p 8083:8083 --env-file .env -v "${PWD}\.secrets\firebase-service-account.json:/run/secrets/firebase-service-account.json:ro" lendup-notifications-service:local
docker tag lendup-notifications-service:local DOCKERHUB_USERNAME/lendup-notifications-service:0.1.0
docker login -u DOCKERHUB_USERNAME
docker push DOCKERHUB_USERNAME/lendup-notifications-service:0.1.0
```

No ejecutar `docker push` con un namespace literal; sustituir `DOCKERHUB_USERNAME`. La publicación normal se realiza por tag `v*.*.*` mediante GitHub Actions (`.github/workflows/docker-publish.yml`, requiere la variable `DOCKERHUB_USERNAME` y el secreto `DOCKERHUB_TOKEN`).
