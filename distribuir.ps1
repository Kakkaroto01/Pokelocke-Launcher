#Requires -Version 7.0
<#
.SYNOPSIS
    Genera el paquete que se reparte a los jugadores (carpeta publicada + ZIP).

.DESCRIPTION
    Publica PokeLockeLauncher en Release, autocontenido (no hace falta instalar .NET en el
    equipo de destino) y comprime el resultado en un ZIP con la versión en el nombre.

    El ZIP se puede subir a Google Drive, WeTransfer, Discord o a una Release de GitHub.
    Los datos del jugador (ajustes.json, library.json, huellas.json, carátulas) NO van en el
    paquete: viven en %APPDATA%\PokeLockeLauncher (ajustes) y
    %APPDATA%\PokeLockeLauncher\datos (biblioteca y carátulas), así que actualizar el
    launcher no borra biblioteca ni sesión. Las carátulas incluidas sí viajan en covers\.

.PARAMETER Version
    Versión que se graba en el ejecutable y en el nombre del ZIP.

.PARAMETER FrameworkDependent
    Genera un paquete de ~20 MB que exige tener instalado el ".NET 10 Desktop Runtime".
    Por defecto se publica autocontenido (~160 MB) para que funcione en cualquier PC.

.PARAMETER NoZip
    Solo publica la carpeta, sin crear el ZIP.

.EXAMPLE
    pwsh -File distribuir.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [switch]$FrameworkDependent,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$proyecto = Join-Path $raiz "src\PokeLobby.LauncherUi\PokeLobby.LauncherUi.csproj"
$dist = Join-Path (Split-Path -Parent $raiz) "dist"
$salida = Join-Path $dist "win-x64"
$zip = Join-Path $dist "PokeLockeLauncher-$Version-win-x64.zip"

# NuGet.Config del repo limpia los orígenes (se compila con la caché local). Para publicar
# autocontenido hacen falta los runtime packs, así que se añade nuget.org solo en esta llamada.
$argumentos = @(
    "publish", $proyecto,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $(if ($FrameworkDependent) { "false" } else { "true" }),
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    "--source", "https://api.nuget.org/v3/index.json",
    "-o", $salida,
    "--nologo"
)

Write-Host "→ Publicando PokeLockeLauncher $Version ..." -ForegroundColor Cyan
if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }

& dotnet @argumentos
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló con código $LASTEXITCODE" }

Copy-Item (Join-Path $raiz "LEEME.txt") (Join-Path $salida "LEEME.txt") -Force

$tam = (Get-ChildItem $salida -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("✔ Carpeta publicada: {0} ({1:N1} MB)" -f $salida, ($tam / 1MB)) -ForegroundColor Green

if (-not $NoZip) {
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Write-Host "→ Comprimiendo ..." -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $salida "*") -DestinationPath $zip -CompressionLevel Optimal
    $tamZip = (Get-Item $zip).Length
    Write-Host ("✔ ZIP listo: {0} ({1:N1} MB)" -f $zip, ($tamZip / 1MB)) -ForegroundColor Green
    Write-Host ""
    Write-Host "Reparte ese ZIP (Drive/Discord/GitHub Release). El jugador lo descomprime y ejecuta" -ForegroundColor Gray
    Write-Host "PokeLockeLauncher.exe. Sus datos y su sesión se guardan aparte, así que actualizar" -ForegroundColor Gray
    Write-Host "el launcher no le borra la biblioteca." -ForegroundColor Gray
}
