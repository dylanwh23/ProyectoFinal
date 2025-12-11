using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelnetInterceptor.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddSucursalField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sucursal",
                table: "EventosGuardados",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sucursal",
                table: "Eventos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sucursal",
                table: "EventosGuardados");

            migrationBuilder.DropColumn(
                name: "Sucursal",
                table: "Eventos");
        }
    }
}
