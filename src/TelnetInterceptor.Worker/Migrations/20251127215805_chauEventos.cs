using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelnetInterceptor.Worker.Migrations
{
    /// <inheritdoc />
    public partial class chauEventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Eventos",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "CamaraIp",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "EventoPuerto",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "NombreEvento",
                table: "Eventos");

            migrationBuilder.RenameColumn(
                name: "RutaImagen",
                table: "Eventos",
                newName: "IpCamara");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Eventos",
                newName: "MensajesRecibidos");

            migrationBuilder.AlterColumn<int>(
                name: "MensajesRecibidos",
                table: "Eventos",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<int>(
                name: "Puerto",
                table: "Eventos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EstaConectada",
                table: "Eventos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "HoraUltimoMensaje",
                table: "Eventos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UltimoMensaje",
                table: "Eventos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Eventos",
                table: "Eventos",
                columns: new[] { "IpCamara", "Puerto" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Eventos",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "Puerto",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "EstaConectada",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "HoraUltimoMensaje",
                table: "Eventos");

            migrationBuilder.DropColumn(
                name: "UltimoMensaje",
                table: "Eventos");

            migrationBuilder.RenameColumn(
                name: "MensajesRecibidos",
                table: "Eventos",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "IpCamara",
                table: "Eventos",
                newName: "RutaImagen");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Eventos",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "CamaraIp",
                table: "Eventos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EventoPuerto",
                table: "Eventos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NombreEvento",
                table: "Eventos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Eventos",
                table: "Eventos",
                column: "Id");
        }
    }
}
