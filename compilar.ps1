# Compila Isla.exe con el compilador de C# que ya trae Windows (.NET Framework 4.x).
# No hace falta instalar Visual Studio ni el SDK de .NET.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path "$fw\csc.exe")) { throw "No se encontró csc.exe en $fw (.NET Framework 4.x)" }

# Metadatos de WinRT: los del SDK de Windows si está instalado; si no, los que trae el propio Windows.
$winmd = @()
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\UnionMetadata" -Directory -ErrorAction SilentlyContinue |
       Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'Windows.winmd')) } |
       Sort-Object Name -Descending | Select-Object -First 1
if ($sdk) { $winmd = @(Join-Path $sdk.FullName 'Windows.winmd') }
else {
    $sys = "$env:WINDIR\System32\WinMetadata"
    $winmd = @('Windows.Foundation.winmd', 'Windows.Media.winmd', 'Windows.Storage.winmd') | ForEach-Object { Join-Path $sys $_ }
}

$refs = @(
    "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll",
    "$fw\System.Runtime.dll", "$fw\System.Runtime.WindowsRuntime.dll", "$fw\System.Runtime.InteropServices.WindowsRuntime.dll",
    "$fw\System.Threading.Tasks.dll", "$fw\System.ObjectModel.dll", "$fw\System.IO.dll"
) + $winmd | ForEach-Object { '/r:"' + $_ + '"' }

$out = Join-Path $here 'Isla.exe'
& "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$out" @refs (Join-Path $here 'Isla.cs')
if ($LASTEXITCODE -ne 0) { throw "Error de compilación" }
"Compilado: $out"
