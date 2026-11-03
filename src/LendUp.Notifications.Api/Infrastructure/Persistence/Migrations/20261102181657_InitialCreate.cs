using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LendUp.Notifications.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    destinatario_usuario_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    clave_notificacion = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tipo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origen_tipo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origen_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    evento_origen_id = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    titulo = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mensaje = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    estado = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    programada_para = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    disponible_en = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    leida_en = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    cancelada_en = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    enviar_correo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    estado_correo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    intentos_correo = table.Column<int>(type: "int", nullable: false),
                    ultimo_intento_correo_en = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    correo_enviado_en = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ultimo_error_correo = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    creada_en = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    actualizada_en = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificaciones", x => x.id);
                    table.CheckConstraint("ck_notificaciones_estado", "estado IN ('PROGRAMADA','DISPONIBLE','LEIDA','CANCELADA')");
                    table.CheckConstraint("ck_notificaciones_estado_correo", "estado_correo IN ('NO_APLICA','PENDIENTE','ENVIADO','FALLIDO')");
                    table.CheckConstraint("ck_notificaciones_origen_tipo", "origen_tipo IN ('SOLICITUD','RESERVA','PRESTAMO','PAGO','GARANTIA','EXTENSION','INCIDENCIA')");
                    table.CheckConstraint("ck_notificaciones_tipo", "tipo IN ('EVENTO','RECORDATORIO')");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    consumer = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    aggregate_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.event_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_notificaciones_0",
                table: "notificaciones",
                columns: new[] { "destinatario_usuario_id", "estado", "creada_en" });

            migrationBuilder.CreateIndex(
                name: "idx_notificaciones_1",
                table: "notificaciones",
                columns: new[] { "estado", "programada_para" });

            migrationBuilder.CreateIndex(
                name: "idx_notificaciones_2",
                table: "notificaciones",
                columns: new[] { "estado_correo", "ultimo_intento_correo_en" });

            migrationBuilder.CreateIndex(
                name: "idx_notificaciones_origen",
                table: "notificaciones",
                columns: new[] { "origen_tipo", "origen_id" });

            migrationBuilder.CreateIndex(
                name: "uq_notificaciones_0",
                table: "notificaciones",
                columns: new[] { "destinatario_usuario_id", "clave_notificacion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_processed_aggregate",
                table: "processed_events",
                columns: new[] { "aggregate_id", "aggregate_version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notificaciones");

            migrationBuilder.DropTable(
                name: "processed_events");
        }
    }
}
