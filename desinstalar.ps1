# Quita Isla Dinámica: la cierra, borra sus accesos directos y su carpeta.
$ErrorActionPreference = 'SilentlyContinue'
$dst = Join-Path $env:LOCALAPPDATA 'Programs\IslaDinamica'
$exe = Join-Path $dst 'Isla.exe'
if (Test-Path $exe) { & $exe --salir; Start-Sleep -Seconds 1 }
Get-Process Isla | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
$menu = [Environment]::GetFolderPath('Programs')
Remove-Item (Join-Path $menu 'Isla Dinámica.lnk'), (Join-Path $menu 'Cerrar Isla Dinámica.lnk'), (Join-Path ([Environment]::GetFolderPath('Startup')) 'Isla Dinámica.lnk')
Remove-Item $exe
if ((Get-ChildItem $dst -Force | Measure-Object).Count -eq 0) { Remove-Item $dst }
Write-Host 'Isla Dinámica desinstalada.'
