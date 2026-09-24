using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SIGER.Domain.Enums;

#nullable disable

namespace SIGER.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:estado_mesa", "Disponible,FueraServicio,Ocupada,Reservada")
                .Annotation("Npgsql:Enum:estado_orden", "Cancelada,EnPreparacion,Lista,Pagada,Pendiente,Servida")
                .Annotation("Npgsql:Enum:estado_pago", "Completado,Fallido,Pendiente,Reembolsado")
                .Annotation("Npgsql:Enum:estado_reserva", "Cancelada,Completada,Confirmada,Pendiente")
                .Annotation("Npgsql:Enum:metodo_pago", "Efectivo,Otro,Tarjeta,Transferencia")
                .Annotation("Npgsql:Enum:origen_orden", "Desktop,Web")
                .Annotation("Npgsql:Enum:tipo_orden", "Mesa,ParaLlevar");

            migrationBuilder.CreateTable(
                name: "categoria",
                columns: table => new
                {
                    id_categoria = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categoria", x => x.id_categoria);
                });

            migrationBuilder.CreateTable(
                name: "mesa",
                columns: table => new
                {
                    id_mesa = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    capacidad = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<TableStatus>(type: "estado_mesa", nullable: false),
                    ubicacion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mesa", x => x.id_mesa);
                });

            migrationBuilder.CreateTable(
                name: "promocion",
                columns: table => new
                {
                    id_promocion = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    porcentaje_descuento = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    fecha_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promocion", x => x.id_promocion);
                });

            migrationBuilder.CreateTable(
                name: "rol",
                columns: table => new
                {
                    id_rol = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rol", x => x.id_rol);
                });

            migrationBuilder.CreateTable(
                name: "producto",
                columns: table => new
                {
                    id_producto = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_categoria = table.Column<long>(type: "bigint", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    precio = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    disponible = table.Column<bool>(type: "boolean", nullable: false),
                    imagen_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_producto", x => x.id_producto);
                    table.ForeignKey(
                        name: "fk_producto_categoria",
                        column: x => x.id_categoria,
                        principalTable: "categoria",
                        principalColumn: "id_categoria",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usuario",
                columns: table => new
                {
                    id_usuario = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_rol = table.Column<long>(type: "bigint", nullable: false),
                    auth_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    apellido = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    correo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario", x => x.id_usuario);
                    table.ForeignKey(
                        name: "fk_usuario_rol",
                        column: x => x.id_rol,
                        principalTable: "rol",
                        principalColumn: "id_rol",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promocion_producto",
                columns: table => new
                {
                    id_promocion = table.Column<long>(type: "bigint", nullable: false),
                    id_producto = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promocion_producto", x => new { x.id_promocion, x.id_producto });
                    table.ForeignKey(
                        name: "fk_promocion_producto_producto",
                        column: x => x.id_producto,
                        principalTable: "producto",
                        principalColumn: "id_producto",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_promocion_producto_promocion",
                        column: x => x.id_promocion,
                        principalTable: "promocion",
                        principalColumn: "id_promocion",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auditoria",
                columns: table => new
                {
                    id_auditoria = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_usuario = table.Column<long>(type: "bigint", nullable: true),
                    accion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entidad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    id_entidad = table.Column<long>(type: "bigint", nullable: true),
                    datos_anteriores = table.Column<string>(type: "jsonb", nullable: true),
                    datos_nuevos = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria", x => x.id_auditoria);
                    table.ForeignKey(
                        name: "fk_auditoria_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "orden",
                columns: table => new
                {
                    id_orden = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_mesa = table.Column<long>(type: "bigint", nullable: true),
                    id_usuario = table.Column<long>(type: "bigint", nullable: false),
                    id_cliente = table.Column<long>(type: "bigint", nullable: true),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<OrderStatus>(type: "estado_orden", nullable: false),
                    origen = table.Column<OrderOrigin>(type: "origen_orden", nullable: false),
                    tipo = table.Column<OrderType>(type: "tipo_orden", nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cuenta_solicitada = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orden", x => x.id_orden);
                    table.ForeignKey(
                        name: "fk_orden_cliente",
                        column: x => x.id_cliente,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orden_mesa",
                        column: x => x.id_mesa,
                        principalTable: "mesa",
                        principalColumn: "id_mesa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orden_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reserva",
                columns: table => new
                {
                    id_reserva = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_usuario = table.Column<long>(type: "bigint", nullable: false),
                    id_mesa = table.Column<long>(type: "bigint", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cantidad_personas = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<ReservationStatus>(type: "estado_reserva", nullable: false),
                    notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reserva", x => x.id_reserva);
                    table.ForeignKey(
                        name: "fk_reserva_mesa",
                        column: x => x.id_mesa,
                        principalTable: "mesa",
                        principalColumn: "id_mesa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reserva_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detalle_orden",
                columns: table => new
                {
                    id_detalle = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_orden = table.Column<long>(type: "bigint", nullable: false),
                    id_producto = table.Column<long>(type: "bigint", nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    nota = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_detalle_orden", x => x.id_detalle);
                    table.ForeignKey(
                        name: "fk_detalle_orden_orden",
                        column: x => x.id_orden,
                        principalTable: "orden",
                        principalColumn: "id_orden",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_detalle_orden_producto",
                        column: x => x.id_producto,
                        principalTable: "producto",
                        principalColumn: "id_producto",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pago",
                columns: table => new
                {
                    id_pago = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_orden = table.Column<long>(type: "bigint", nullable: false),
                    id_usuario = table.Column<long>(type: "bigint", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    metodo = table.Column<PaymentMethod>(type: "metodo_pago", nullable: false),
                    estado = table.Column<PaymentStatus>(type: "estado_pago", nullable: false),
                    referencia = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    fecha_pago = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pago", x => x.id_pago);
                    table.ForeignKey(
                        name: "fk_pago_orden",
                        column: x => x.id_orden,
                        principalTable: "orden",
                        principalColumn: "id_orden",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pago_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_fecha_hora",
                table: "auditoria",
                column: "fecha_hora");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_id_usuario",
                table: "auditoria",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ux_categoria_nombre",
                table: "categoria",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_detalle_orden_id_orden",
                table: "detalle_orden",
                column: "id_orden");

            migrationBuilder.CreateIndex(
                name: "ix_detalle_orden_id_producto",
                table: "detalle_orden",
                column: "id_producto");

            migrationBuilder.CreateIndex(
                name: "ix_mesa_estado",
                table: "mesa",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ux_mesa_numero",
                table: "mesa",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orden_estado",
                table: "orden",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_orden_fecha_hora",
                table: "orden",
                column: "fecha_hora");

            migrationBuilder.CreateIndex(
                name: "ix_orden_id_cliente",
                table: "orden",
                column: "id_cliente");

            migrationBuilder.CreateIndex(
                name: "ix_orden_id_mesa",
                table: "orden",
                column: "id_mesa");

            migrationBuilder.CreateIndex(
                name: "ix_orden_id_usuario",
                table: "orden",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ix_pago_id_orden",
                table: "pago",
                column: "id_orden");

            migrationBuilder.CreateIndex(
                name: "ix_pago_id_usuario",
                table: "pago",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ix_producto_disponible",
                table: "producto",
                column: "disponible");

            migrationBuilder.CreateIndex(
                name: "ix_producto_id_categoria",
                table: "producto",
                column: "id_categoria");

            migrationBuilder.CreateIndex(
                name: "ix_promocion_vigencia",
                table: "promocion",
                columns: new[] { "activo", "fecha_inicio", "fecha_fin" });

            migrationBuilder.CreateIndex(
                name: "ix_promocion_producto_id_producto",
                table: "promocion_producto",
                column: "id_producto");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_fecha_hora",
                table: "reserva",
                column: "fecha_hora");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_id_mesa",
                table: "reserva",
                column: "id_mesa");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_id_usuario",
                table: "reserva",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ux_rol_nombre",
                table: "rol",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_id_rol",
                table: "usuario",
                column: "id_rol");

            migrationBuilder.CreateIndex(
                name: "ux_usuario_auth_user_id",
                table: "usuario",
                column: "auth_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_usuario_correo",
                table: "usuario",
                column: "correo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria");

            migrationBuilder.DropTable(
                name: "detalle_orden");

            migrationBuilder.DropTable(
                name: "pago");

            migrationBuilder.DropTable(
                name: "promocion_producto");

            migrationBuilder.DropTable(
                name: "reserva");

            migrationBuilder.DropTable(
                name: "orden");

            migrationBuilder.DropTable(
                name: "producto");

            migrationBuilder.DropTable(
                name: "promocion");

            migrationBuilder.DropTable(
                name: "usuario");

            migrationBuilder.DropTable(
                name: "mesa");

            migrationBuilder.DropTable(
                name: "categoria");

            migrationBuilder.DropTable(
                name: "rol");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:Enum:estado_mesa", "Disponible,FueraServicio,Ocupada,Reservada")
                .OldAnnotation("Npgsql:Enum:estado_orden", "Cancelada,EnPreparacion,Lista,Pagada,Pendiente,Servida")
                .OldAnnotation("Npgsql:Enum:estado_pago", "Completado,Fallido,Pendiente,Reembolsado")
                .OldAnnotation("Npgsql:Enum:estado_reserva", "Cancelada,Completada,Confirmada,Pendiente")
                .OldAnnotation("Npgsql:Enum:metodo_pago", "Efectivo,Otro,Tarjeta,Transferencia")
                .OldAnnotation("Npgsql:Enum:origen_orden", "Desktop,Web")
                .OldAnnotation("Npgsql:Enum:tipo_orden", "Mesa,ParaLlevar");
        }
    }
}
