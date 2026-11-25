#language: es
Característica: RF48 Notificaciones por eventos
  Como estudiante quiero recibir avisos en la plataforma y por correo cuando ocurre un evento relevante
  de mis solicitudes, reservas, pagos o garantías, para enterarme sin revisar cada pantalla.

  Escenario: Una solicitud nueva avisa al prestamista
    Cuando Reservas publica una solicitud nueva
    Entonces el prestamista tiene 1 aviso "Nueva solicitud"
    Y el prestatario tiene 1 aviso "Solicitud enviada"

  Escenario: Una solicitud rechazada avisa al prestatario con el motivo
    Dado que Reservas publica una solicitud nueva
    Cuando el prestamista rechaza la solicitud con el motivo "No estará disponible"
    Entonces el prestatario tiene un aviso con el mensaje "El prestamista rechazó tu solicitud. Motivo: No estará disponible"
    Y el prestamista tiene 0 avisos "Solicitud rechazada"

  Escenario: Una solicitud cancelada por el prestatario avisa al prestamista
    Dado que Reservas publica una solicitud nueva
    Cuando el prestatario cancela la solicitud
    Entonces el prestamista tiene 1 aviso "Solicitud cancelada"
    Y el prestatario tiene 0 avisos "Solicitud cancelada"

  Esquema del escenario: El ciclo del préstamo avisa a quien debe actuar
    Cuando Préstamos publica "<evento>"
    Entonces el prestatario tiene <prestatario> avisos "<titulo>"
    Y el prestamista tiene <prestamista> avisos "<titulo>"

    Ejemplos:
      | evento                  | titulo                   | prestatario | prestamista |
      | ObjetoEntregado         | Objeto entregado         | 1           | 0           |
      | PrestamoActivado        | Préstamo activo          | 1           | 1           |
      | DevolucionRegistrada    | Devolución registrada    | 0           | 1           |
      | PrestamoVencido         | Préstamo vencido         | 1           | 1           |
      | PrestamoFinalizado      | Préstamo finalizado      | 1           | 1           |
      | CancelacionRechazada    | Cancelación no aceptada  | 1           | 1           |
      | ExtensionSolicitada     | Extensión solicitada     | 0           | 1           |
      | ExtensionAceptada       | Extensión aceptada       | 1           | 1           |
      | ExtensionRechazada      | Extensión rechazada      | 1           | 0           |
      | ReprogramacionPropuesta | Reprogramación propuesta | 1           | 0           |
      | ReprogramacionRechazada | Reprogramación rechazada | 0           | 1           |

  Escenario: Un reembolso que Mercado Pago no completa se informa al prestatario
    Cuando Pagos publica "ReembolsoRechazado" por 40.00 PEN
    Entonces el prestatario tiene un aviso con el mensaje "No se pudo completar el reembolso de 40.00 PEN. LendUp lo revisará."

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
