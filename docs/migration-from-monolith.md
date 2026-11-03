# Migración desde el monolito

El monolito escribe avisos en su tabla `notificaciones` desde `FlowSupport.notifyUser` (solicitudes y reservas) e `IncidentsService.notifyParticipants` (incidencias). Pasos para el cutover:

1. Respaldar `notificaciones` del monolito. Las columnas son las mismas (ids `CHAR(36)`), así que la copia es directa: `INSERT INTO lendup_notificaciones.notificaciones SELECT ... FROM <monolito>.notificaciones`, convirtiendo las fechas a UTC si no lo están. Los estados del monolito (`DISPONIBLE`, `LEIDA`) ya existen aquí.
2. Revisar duplicados de `(destinatario_usuario_id, clave_notificacion)`: el monolito usaba claves `origen:id:destinatario` e `IncidentsService` claves aleatorias; ambas son compatibles con el índice único.
3. Desactivar en el monolito `notifyUser` y `notifyParticipants` y publicar en su lugar los eventos de Préstamos y Evidencias (`PENDIENTES.md`, sección C). Mientras convivan, el mismo hecho podría aparecer dos veces (uno del monolito, otro de este servicio).
4. Cambiar el Gateway para `/api/v1/notificaciones/**`.
5. Los recordatorios (RF22) no existían en el monolito: solo se programan para reservas confirmadas después del cutover. Para reservas vigentes se puede reenviar su `reservas.confirmada.v1` desde Reservas o publicarlo con la colección Postman.
