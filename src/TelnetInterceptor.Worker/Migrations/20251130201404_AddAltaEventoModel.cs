using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelnetInterceptor.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddAltaEventoModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventosGuardados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    IpCamara = table.Column<string>(type: "TEXT", nullable: false),
                    Puerto = table.Column<int>(type: "INTEGER", nullable: false),
                    RutaCarpeta = table.Column<string>(type: "TEXT", nullable: false),
                    EsEventoGuardado = table.Column<bool>(type: "INTEGER", nullable: false),
                    FrameInicio = table.Column<int>(type: "INTEGER", nullable: true),
                    FrameFin = table.Column<int>(type: "INTEGER", nullable: true),
                    FechaEvento = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Descripcion = table.Column<string>(type: "TEXT", nullable: true),
                    FromFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    ToFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    EstaConectada = table.Column<bool>(type: "INTEGER", nullable: false),
                    FramePath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosGuardados", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosGuardados");
        }
    }
}
