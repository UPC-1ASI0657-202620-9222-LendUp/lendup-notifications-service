#language: es
Característica: RF48 Notificaciones por eventos
  Como estudiante quiero recibir avisos en la plataforma y por correo cuando ocurre un evento relevante
  de mis solicitudes, reservas, pagos o garantías, para enterarme sin revisar cada pantalla.

  Escenario: Una solicitud nueva avisa al prestamista
    Cuando Reservas publica una solicitud nueva
    Entonces el prestamista tiene 1 aviso "Nueva solicitud"
    Y el prestatario tiene 0 avisos

  Escenario: Una reserva confirmada avisa a ambos participantes
    Cuando Reservas confirma una reserva
    Entonces el prestatario tiene 1 aviso "Reserva confirmada"
    Y el prestamista tiene 1 aviso "Reserva confirmada"

  Escenario: Un pago confirmado por Mercado Pago avisa con el monto
    Cuando Pagos publica "PagoConfirmado" por 60.00 PEN
    Entonces el prestatario tiene un aviso con el mensaje "Mercado Pago confirmó tu pago de 60.00 PEN."
    Y el prestamista tiene 1 aviso "Pago recibido"

  Escenario: Una devolución de garantía avisa al prestatario
    Cuando Pagos publica "GarantiaDevuelta" por 50.00 PEN
    Entonces el prestatario tiene un aviso con el mensaje "Se devolvieron 50.00 PEN de tu garantía."

  Escenario: El mismo evento entregado dos veces genera un solo aviso
    Cuando Reservas confirma una reserva y el broker entrega el mismo evento 2 veces
    Entonces el prestatario tiene 1 aviso "Reserva confirmada"

  Escenario: El estudiante marca un aviso como leído
    Dado que Reservas confirma una reserva
    Cuando el prestatario marca como leído su aviso "Reserva confirmada"
    Entonces el aviso queda en estado "LEIDA"
    Y volver a marcarlo es rechazado con "Aviso no disponible"

  Escenario: Los avisos también se envían por correo
    Dado que Reservas confirma una reserva
    Cuando corre el envío de correos
    Entonces se envían 2 correos con el asunto "Reserva confirmada"

  Escenario: Si el proveedor de correo falla se reintenta y luego queda fallido
    Dado que Reservas confirma una reserva
    Y que el proveedor de correo no responde
    Cuando corre el envío de correos 5 veces
    Entonces los avisos de "Reserva confirmada" quedan con estado de correo "FALLIDO"
