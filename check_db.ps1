# Script para verificar el contenido de app.db
Add-Type -Path "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.0\System.Data.Common.dll" -ErrorAction SilentlyContinue

$dbPath = "D:\FACULTAD ESTUDIOS\CURE\PROYECTOFINAL\ProyectoFinal\src\TelnetInterceptor.Worker\app.db"

Write-Host "=== Verificando contenido de app.db ===" -ForegroundColor Cyan
Write-Host ""

# Usar dotnet ef o simplemente hacer un query directo con el proyecto
cd "D:\FACULTAD ESTUDIOS\CURE\PROYECTOFINAL\ProyectoFinal\src\TelnetInterceptor.Worker"

# Crear un script temporal en C# para leer la BD
$scriptContent = @"
using System;
using System.Data.SQLite;

var dbPath = @"$dbPath";
using var connection = new SQLiteConnection(`$"Data Source={dbPath}"`);
connection.Open();

var cmd = connection.CreateCommand();
cmd.CommandText = "SELECT IpCamara, Puerto, Nombre, TipoEvento FROM Eventos";
using var reader = cmd.ExecuteReader();

Console.WriteLine("IpCamara\t\tPuerto\tNombre\t\t\tTipoEvento");
Console.WriteLine("============================================================");
while (reader.Read())
{
    Console.WriteLine(`$"{reader[0]}\t{reader[1]}\t{reader[2]}\t\t{reader[3]}"`);
}
"@

Write-Host "Cámaras registradas en la BD:" -ForegroundColor Yellow
dotnet run -- 2>&1 | Select-String "Eventos|Camaras|cámara|IpCamara" -Context 2
