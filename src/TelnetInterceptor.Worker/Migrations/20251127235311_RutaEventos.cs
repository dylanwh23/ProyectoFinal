using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelnetInterceptor.Worker.Migrations
{
    /// <inheritdoc />
    public partial class RutaEventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RutaCarpeta",
                table: "Eventos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RutaCarpeta",
                table: "Eventos");
        }
    }
}
