# Contrato API

Base externa: `/api/v1`. Ambos endpoints requieren `Authorization: Bearer <Firebase ID token>` y usan JSON `snake_case`. Contrato formal: [`contracts/openapi/notifications-v1.yaml`](../contracts/openapi/notifications-v1.yaml).

| Método | Ruta | Resultado |
|---|---|---|
| GET | `/notificaciones` | 200, avisos `DISPONIBLE` y `LEIDA` del estudiante autenticado, del más reciente al más antiguo (máx. 200) |
| PATCH | `/notificaciones/{id}` | 200, aviso en `LEIDA` con `leida_en`; sin cuerpo |

Errores: `{ "status": 409, "error": "CONFLICTO_NEGOCIO", "message": "Aviso no disponible", "timestamp": "...Z" }`.

| Caso | Respuesta (igual que el monolito) |
|---|---|
| Sin token / token inválido | 401 `AUTENTICACION_REQUERIDA` / `TOKEN_INVALIDO` |
| Correo de Firebase no verificado | 403 `EMAIL_NOT_VERIFIED` |
| El aviso no existe | 404 `NO_ENCONTRADO` "No encontrado" |
| El aviso es de otro estudiante | 403 `NO_AUTORIZADO` "Operación no permitida" |
| El aviso no está `DISPONIBLE` (ya leído, programado o cancelado) | 409 `CONFLICTO_NEGOCIO` "Aviso no disponible" |
| Identidad no configurada o caída | 503 `DEPENDENCIA_NO_DISPONIBLE` |

Cada fila trae las columnas de la tabla `notificaciones`: `id`, `destinatario_usuario_id`, `clave_notificacion`, `tipo`, `origen_tipo`, `origen_id`, `evento_origen_id`, `titulo`, `mensaje`, `estado`, `programada_para`, `disponible_en`, `leida_en`, `cancelada_en`, `enviar_correo`, `estado_correo`, `intentos_correo`, `ultimo_intento_correo_en`, `correo_enviado_en`, `ultimo_error_correo`, `creada_en`, `actualizada_en`. `mapNotification` del frontend usa `id`, `destinatario_usuario_id`, `titulo`, `mensaje`, `creada_en`, `estado` (`LEIDA` → leído), `origen_tipo` y `origen_id`.

## Diferencias respecto al monolito

- La lista excluye recordatorios `PROGRAMADA` (todavía no deben verse) y `CANCELADA`, y se ordena por `creada_en` descendente (el monolito no ordenaba).
- Fechas en UTC con `Z`, como Reservas y Pagos.
- Nuevos `origen_tipo` de la Fig. 13: `SOLICITUD`, `PAGO`, `GARANTIA` y `EXTENSION`. El frontend hoy solo enlaza `RESERVA`, `PRESTAMO` e `INCIDENCIA`; los demás llevan a `/notifications` (ver `PENDIENTES.md`, sección B).

## Contratos internos pendientes con Identidad

- `IDENTITY_FIREBASE_USER_URI_TEMPLATE` con `{uid}`: con `IDENTITY_SERVICE_TOKEN` se espera `{ "usuario_id": "UUID" }`; sin token se reenvía el ID token del usuario (en local, `GET /api/v1/estudiantes/me` del monolito, que devuelve `{ "usuario": { "id": "UUID" } }`). Igual que Pagos.
- `IDENTITY_USER_EMAIL_URI_TEMPLATE` con `{usuario_id}` y token de servicio: `{ "correo": "..." }` (o `correo_institucional`). Sin este contrato, con SendGrid configurado el correo se reintenta y termina `FALLIDO`; sin SendGrid se registra en el log.
