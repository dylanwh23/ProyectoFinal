using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelnetInterceptor.Worker.Migrations
{
    /// <inheritdoc />
    public partial class UpdateExistingCameraSucursal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Set sucursal for existing cameras that have null value
            migrationBuilder.Sql(
                "UPDATE Eventos SET Sucursal = 'Sucursal centro' WHERE Sucursal IS NULL OR Sucursal = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert back to null
            migrationBuilder.Sql(
                "UPDATE Eventos SET Sucursal = NULL WHERE Sucursal = 'Sucursal centro';");
        }
    }
}
