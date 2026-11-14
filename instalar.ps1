# Instala Isla Dinámica para el usuario actual (no pide permisos de administrador).
param([switch]$ConWindows, [switch]$SinPreguntar)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $here 'Isla.exe'
if (-not (Test-Path $src)) {
    Write-Host 'No encontré Isla.exe junto al instalador; lo compilo desde el código...'
    & (Join-Path $here 'compilar.ps1')
}

$dst = Join-Path $env:LOCALAPPDATA 'Programs\IslaDinamica'
$exe = Join-Path $dst 'Isla.exe'
if (Test-Path $exe) { & $exe --salir; Start-Sleep -Seconds 1 }
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item $src $exe -Force
Unblock-File $exe

$ws = New-Object -ComObject WScript.Shell
function Atajo([string]$ruta, [string]$argumentos) {
    $s = $ws.CreateShortcut($ruta)
    $s.TargetPath = $exe; $s.Arguments = $argumentos; $s.WorkingDirectory = $dst
    $s.Save()
}
$menu = [Environment]::GetFolderPath('Programs')
Atajo (Join-Path $menu 'Isla Dinámica.lnk') ''
Atajo (Join-Path $menu 'Cerrar Isla Dinámica.lnk') '--salir'

$inicio = $ConWindows
if (-not $ConWindows -and -not $SinPreguntar) {
    $r = Read-Host '¿Quieres que la isla se abra sola al iniciar Windows? (s/n)'
    $inicio = $r -match '^(s|si|sí|y|yes)$'
}
if ($inicio) {
    Atajo (Join-Path ([Environment]::GetFolderPath('Startup')) 'Isla Dinámica.lnk') ''
    Write-Host 'Se abrirá sola al iniciar Windows.'
}

Start-Process $exe
Write-Host ''
Write-Host "Listo. Isla Dinámica instalada en $dst"
Write-Host 'Para cerrarla: clic derecho sobre la isla abierta, o "Cerrar Isla Dinámica" en el menú Inicio.'
