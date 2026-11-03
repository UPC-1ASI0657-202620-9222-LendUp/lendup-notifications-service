#language: es
Característica: RF22 Recordatorios de entrega y devolución
  Como participante de un préstamo quiero recibir recordatorios antes de la entrega y la devolución
  del objeto, para anticipar mis próximas obligaciones.

  Antecedentes:
    Dado que Reservas confirma una reserva con entrega el "2026-11-10T09:00:00" y devolución el "2026-11-12T09:00:00"

  Escenario: Se programan recordatorios para ambos participantes
    Entonces hay 4 recordatorios en estado "PROGRAMADA"
    Y el prestatario todavía no ve recordatorios en su lista

  Escenario: El recordatorio de entrega se muestra 24 horas antes y se envía por correo
    Cuando llega el "2026-11-09T14:00:00Z" y corre el programador de recordatorios
    Entonces hay 2 recordatorios "Recordatorio de entrega" en estado "DISPONIBLE"
    Y hay 2 recordatorios "Recordatorio de devolución" en estado "PROGRAMADA"
    Y se envían 2 correos con el asunto "Recordatorio de entrega"
    Y el prestatario ve el aviso "Recordatorio de entrega" en su lista

  Escenario: El recordatorio de devolución se muestra 24 horas antes de la fecha acordada
    Cuando llega el "2026-11-11T14:00:00Z" y corre el programador de recordatorios
    Entonces hay 2 recordatorios "Recordatorio de devolución" en estado "DISPONIBLE"

  Escenario: La cancelación de la reserva cancela los recordatorios pendientes
    Cuando Reservas cancela la reserva
    Entonces hay 4 recordatorios en estado "CANCELADA"
    Y el prestatario tiene 1 aviso "Reserva cancelada"
    Y el prestamista tiene 1 aviso "Reserva cancelada"
