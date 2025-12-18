param(
    # Objetivo GLOBAL: 1 RAW por segundo (entre TODAS las cámaras)
    [int]$SendIntervalMs = 1000
)

# 3 cámaras por tipo de evento (9 total).
# Nota Windows: el listener escucha en ANY; el worker se conectará por IP/puerto.
$cameras = @(
    # Grid
    @{ IP = "127.0.0.1"; Port = 2321; Mode = "grid";   Name = "ESTANTERIA-A" },
    @{ IP = "127.0.0.2"; Port = 2322; Mode = "grid";   Name = "ESTANTERIA-B" },
    @{ IP = "127.0.0.3"; Port = 2323; Mode = "grid";   Name = "ESTANTERIA-C" },

    # Pallet
    @{ IP = "127.0.0.4"; Port = 2324; Mode = "pallet"; Name = "PALLET-LINEA1" },
    @{ IP = "127.0.0.5"; Port = 2325; Mode = "pallet"; Name = "PALLET-LINEA2" },
    @{ IP = "127.0.0.6"; Port = 2326; Mode = "pallet"; Name = "PALLET-LINEA3" },

    # Camión
    @{ IP = "127.0.0.7"; Port = 2327; Mode = "camion"; Name = "CAMION-MUELLE1" },
    @{ IP = "127.0.0.8"; Port = 2328; Mode = "camion"; Name = "CAMION-MUELLE2" },
    @{ IP = "127.0.0.9"; Port = 2329; Mode = "camion"; Name = "CAMION-MUELLE3" }
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Minimal Camera Simulators" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RAW interval: 1 mensaje/segundo TOTAL (global)" -ForegroundColor White
Write-Host "Press Ctrl+C to stop" -ForegroundColor Yellow
Write-Host ""

# Gate GLOBAL entre procesos/jobs: solo 1 envío por segundo total.
# Usamos un mutex con nombre + un archivo en TEMP para recordar el último segundo enviado.
$GlobalGateMutexName = 'Local\\ProyectoFinal_WmsSimGate'
$GlobalGateFilePath = Join-Path $env:TEMP 'ProyectoFinal_WmsSimGate.lastSecond.txt'
try { Set-Content -Path $GlobalGateFilePath -Value '' -Force -Encoding ascii } catch {}

function Start-CameraJob {
    param(
        [Parameter(Mandatory)]$Cam,
        [Parameter(Mandatory)][int]$IntervalMs,
        [Parameter(Mandatory)][string]$GateMutexName,
        [Parameter(Mandatory)][string]$GateFilePath
    )

    Start-Job -Name "$($Cam.Name)@$($Cam.IP):$($Cam.Port)" -ArgumentList $Cam, $IntervalMs, $GateMutexName, $GateFilePath -ScriptBlock {
        param($Cam, $IntervalMs, $GateMutexName, $GateFilePath)

        $port = [int]$Cam.Port
        $name = [string]$Cam.Name
        $mode = [string]$Cam.Mode
        $ip   = [string]$Cam.IP

        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $port)
        # backlog alto para evitar rechazos si el cliente reconecta rápido
        $listener.Start(50)
        Write-Output "[$mode] Listening on $ip`:$port ($name)"

        try {
            $gateMutex = New-Object System.Threading.Mutex($false, $GateMutexName)

            $client = $null
            $writer = $null
            $i = 0
            $nextTryAt = [DateTime]::UtcNow

            while ($true) {
                # (Re)conectar si no hay cliente
                if ($null -eq $client -or -not $client.Connected) {
                    $client = $listener.AcceptTcpClient()  # bloqueante
                    $client.NoDelay = $true
                    Write-Output "[$name] Client connected"

                    $stream = $client.GetStream()
                    $writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
                    $writer.NewLine = "`r`n"
                    $writer.AutoFlush = $true
                    $nextSendAt = [DateTime]::UtcNow
                }

                # Si el cliente reconecta rápido, aceptar y reemplazar (evita backlog lleno -> refused)
                if ($listener.Pending()) {
                    $newClient = $listener.AcceptTcpClient()
                    $newClient.NoDelay = $true
                    Write-Output "[$name] Client reconnected (replacing)"

                    try { $client.Close() } catch {}

                    $client = $newClient
                    $stream = $client.GetStream()
                    $writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
                    $writer.NewLine = "`r`n"
                    $writer.AutoFlush = $true
                    $nextSendAt = [DateTime]::UtcNow
                }

                $now = [DateTime]::UtcNow
                if ($now -lt $nextTryAt) {
                    Start-Sleep -Milliseconds 20
                    continue
                }

                # Gate global: máximo 1 envío por segundo total
                $canSend = $false
                $sec = $now.ToString('yyyyMMddHHmmss')
                $locked = $false
                try {
                    $locked = $gateMutex.WaitOne(200)
                    if ($locked) {
                        $last = ''
                        try {
                            if (Test-Path $GateFilePath) {
                                $last = (Get-Content -Path $GateFilePath -ErrorAction SilentlyContinue | Select-Object -First 1)
                            }
                        } catch { }

                        if ($last -ne $sec) {
                            try { Set-Content -Path $GateFilePath -Value $sec -Force -Encoding ascii } catch { }
                            $canSend = $true
                        }
                    }
                }
                finally {
                    if ($locked) {
                        try { $gateMutex.ReleaseMutex() } catch { }
                    }
                }

                if (-not $canSend) {
                    # Reintentar pronto (otro job ya envió este segundo)
                    $nextTryAt = $now.AddMilliseconds([Math]::Min(200, [Math]::Max(50, [int]($IntervalMs / 4))))
                    continue
                }

                $msg = $null
                switch ($mode) {
                    'grid' {
                        $shelf = if ($name -like '*-A*') { 'ESTANTE-A' } elseif ($name -like '*-B*') { 'ESTANTE-B' } else { 'ESTANTE-C' }
                        if (($i % 2) -eq 0) { $msg = "$shelf:VACIO" }
                        else {
                            if ($shelf -eq 'ESTANTE-A') { $msg = "$shelf:CAJA-1|CAJA-2|CAJA-3" }
                            elseif ($shelf -eq 'ESTANTE-B') { $msg = "$shelf:CAJA-101|CAJA-102|CAJA-103" }
                            else { $msg = "$shelf:CAJA-201|CAJA-202|CAJA-203" }
                        }
                    }
                    'pallet' {
                        $line = if ($name -like '*LINEA1*') { 1 } elseif ($name -like '*LINEA2*') { 2 } else { 3 }
                        if (($i % 3) -eq 0) { $msg = 'PALLET:VACIO' }
                        elseif (($i % 3) -eq 1) { $msg = "PALLET:CAJA-$($line)10|CAJA-$($line)11|CAJA-$($line)12" }
                        else { $msg = "PALLET:CAJA-$($line)20|CAJA-$($line)21" }
                    }
                    'camion' {
                        $muelle = if ($name -like '*MUELLE1*') { 1 } elseif ($name -like '*MUELLE2*') { 2 } else { 3 }
                        $r1 = if ($muelle -eq 1 -and (($i % 2) -eq 0)) { 'CAMION101' } else { 'VACIO' }
                        $r2 = if ($muelle -eq 2 -and (($i % 2) -eq 0)) { 'CAMION102' } else { 'VACIO' }
                        $r3 = if ($muelle -eq 3 -and (($i % 2) -eq 0)) { 'CAMION103' } else { 'VACIO' }
                        $msg = "RESERVA1:$r1|RESERVA2:$r2|RESERVA3:$r3"
                    }
                    default { $msg = 'VACIO' }
                }

                try {
                    $writer.WriteLine($msg)
                    Write-Output "[$name] -> $msg"
                }
                catch {
                    Write-Output "[$name] Disconnected (write failed)"
                    try { $client.Close() } catch {}
                    $client = $null
                    $writer = $null
                }

                $i++
                # El próximo envío global será el siguiente segundo.
                $nextTryAt = $now.AddMilliseconds([Math]::Max(50, [int]($IntervalMs / 2)))
            }
        }
        finally {
            try { $listener.Stop() } catch {}
            Write-Output "[$name] Listener stopped"
        }
    }
}

$jobs = @()
foreach ($cam in $cameras) {
    $jobs += Start-CameraJob -Cam $cam -IntervalMs $SendIntervalMs -GateMutexName $GlobalGateMutexName -GateFilePath $GlobalGateFilePath
}

Write-Host "✅ Started $($jobs.Count) simulators" -ForegroundColor Green
Write-Host ""

try {
    while ($true) {
        foreach ($j in $jobs) {
            # IMPORTANTE: no usar -Keep porque reimprime TODO el buffer cada vez (parecen "pulsos").
            Receive-Job -Job $j -ErrorAction SilentlyContinue | ForEach-Object {
                Write-Host $_
            }
        }
        Start-Sleep -Milliseconds 250
    }
}
catch {
    Write-Host "Stopping simulators..." -ForegroundColor Yellow
}
finally {
    foreach ($j in $jobs) {
        try { Stop-Job $j -Force -ErrorAction SilentlyContinue } catch {}
        try { Remove-Job $j -Force -ErrorAction SilentlyContinue } catch {}
    }
    Write-Host "✅ All simulators stopped" -ForegroundColor Green
}
