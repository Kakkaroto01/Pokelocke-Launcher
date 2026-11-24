#Requires -Version 7.0
<#
.SYNOPSIS
    Publica PokeLocke Launcher como instalador Velopack, con el vendor dentro, y
    opcionalmente lo sube a las Releases de GitHub.

.DESCRIPTION
    1. Publica la app en Release, autocontenida (no requiere .NET en el PC del jugador).
    2. Copia la carpeta vendor\ (emuladores, JRE y randomizer) dentro del paquete.
    3. Genera el instalador con vpk: PokeLockeLauncher-win-Setup.exe.
    4. Si se indica -RepoUrl y hay token, crea/publica la Release en GitHub.

    La carpeta dist\velopack\releases NO se borra: Velopack la usa para generar los
    paquetes delta de las próximas versiones.

.PARAMETER Version
    Versión SemVer de esta publicación (ej. 0.1.0). Debe coincidir con la del ejecutable.

.PARAMETER RepoUrl
    URL del repositorio público, sin .git (ej. https://github.com/usuario/pokelocke-launcher).

.PARAMETER Token
    Token de GitHub con permiso Contents: Read and write. Por defecto se lee de la
    variable de entorno VELOPACK_GITHUB_TOKEN.

.PARAMETER VendorDir
    Carpeta vendor a incluir. Si se omite, se busca en POKELOCKE_VENDOR_DIR, junto al
    proyecto y en la instalación oficial de PokeLobby.

.PARAMETER SinVendor
    Genera un instalador ligero (~70 MB). El jugador deberá tener PokeLobby instalado o
    elegir su carpeta vendor en Ajustes.

.PARAMETER FrameworkDependent
    No incluye .NET en el paquete (~20 MB). Solo para pruebas: el jugador necesitaría
    instalar el .NET 10 Desktop Runtime.

.PARAMETER NoUpload
    Solo genera el instalador; no sube nada a GitHub.

.EXAMPLE
    pwsh -File publicar.ps1 -Version 0.1.0 -NoUpload

.EXAMPLE
    pwsh -File publicar.ps1 -Version 0.1.1 `
      -RepoUrl https://github.com/TU_USUARIO/pokelocke-launcher
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$RepoUrl = "",
    [string]$Token = $env:VELOPACK_GITHUB_TOKEN,
    [string]$VendorDir = "",
    [string]$Channel = "win",
    [switch]$SinVendor,
    [switch]$FrameworkDependent,
    [switch]$NoUpload
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$') {
    throw "Versión no válida: '$Version'. Usa SemVer, por ejemplo 0.1.0 o 0.2.0-beta.1."
}

$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $raiz
$proyecto = Join-Path $raiz "src\PokeLobby.LauncherUi\PokeLobby.LauncherUi.csproj"
$dist = Join-Path $repo "dist\velopack"
$publish = Join-Path $dist "publish"
$releases = Join-Path $dist "releases"
$leeme = Join-Path $raiz "LEEME.txt"

# --- vendor -----------------------------------------------------------------
if (-not $SinVendor) {
    if ([string]::IsNullOrWhiteSpace($VendorDir)) {
        $candidatos = @(
            $env:POKELOCKE_VENDOR_DIR,
            (Join-Path $raiz "vendor"),
            (Join-Path $env:LOCALAPPDATA "PokeLobby.Launcher\current\vendor"),
            (Join-Path $env:ProgramFiles "PokeLobby.Launcher\current\vendor")
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }

        $VendorDir = $candidatos | Select-Object -First 1
    }

    if ([string]::IsNullOrWhiteSpace($VendorDir) -or -not (Test-Path $VendorDir)) {
        throw "No encuentro la carpeta vendor. Indícala con -VendorDir o usa -SinVendor."
    }

    $VendorDir = (Resolve-Path $VendorDir).Path
    $tamVendor = (Get-ChildItem $VendorDir -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ("→ vendor: {0} ({1:N1} MB)" -f $VendorDir, ($tamVendor / 1MB)) -ForegroundColor Cyan
}

# --- vpk --------------------------------------------------------------------
$vpk = Join-Path $repo ".tools\vpk\vpk.exe"
if (-not (Test-Path $vpk)) {
    $cmd = Get-Command vpk -ErrorAction SilentlyContinue
    if ($cmd) { $vpk = $cmd.Source }
}
if (-not $vpk -or -not (Test-Path $vpk)) {
    throw "No encuentro vpk. Instálalo con: dotnet tool install -g vpk --version 1.2.161"
}

# --- publicar la app --------------------------------------------------------
Write-Host "→ Publicando PokeLockeLauncher $Version ..." -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

$argumentos = @(
    "publish", $proyecto,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $(if ($FrameworkDependent) { "false" } else { "true" }),
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    "--source", "https://api.nuget.org/v3/index.json",
    "-o", $publish,
    "--nologo"
)

& dotnet @argumentos
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló con código $LASTEXITCODE" }
$global:LASTEXITCODE = 0

if (Test-Path $leeme) {
    Copy-Item $leeme (Join-Path $publish "LEEME.txt") -Force
}

# --- publicar el desinstalador ------------------------------------------------
$uninstallerProyecto = Join-Path $raiz "src\PokeLobby.LauncherUi.Uninstaller\PokeLobby.LauncherUi.Uninstaller.csproj"
$uninstallerPublish = Join-Path $dist "publish-uninstaller"
if (Test-Path $uninstallerPublish) { Remove-Item $uninstallerPublish -Recurse -Force }
Write-Host "→ Publicando desinstalador ..." -ForegroundColor Cyan
dotnet publish $uninstallerProyecto -c Release -r win-x64 --self-contained true -o $uninstallerPublish --nologo 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del desinstalador falló con código $LASTEXITCODE" }
$global:LASTEXITCODE = 0
# Copiar al publish principal
$uninstallerDest = Join-Path $publish "uninstaller"
if (Test-Path $uninstallerDest) { Remove-Item $uninstallerDest -Recurse -Force }
Copy-Item $uninstallerPublish $uninstallerDest -Recurse -Force
Write-Host "✔ Desinstalador incluido en el paquete" -ForegroundColor Green

# --- copiar vendor ----------------------------------------------------------
if (-not $SinVendor) {
    $destinoVendor = Join-Path $publish "vendor"
    Write-Host "→ Copiando vendor al paquete (puede tardar un par de minutos) ..." -ForegroundColor Cyan
    robocopy $VendorDir $destinoVendor /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy falló con código $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
}

$tam = (Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("✔ Contenido a empaquetar: {0:N1} MB" -f ($tam / 1MB)) -ForegroundColor Green

# --- empaquetar -------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $releases | Out-Null

# vpk usa %TEMP%\velopack; en algunos equipos esa ruta no es escribible. Usamos una
# carpeta temporal propia dentro de dist para que el empaquetado sea reproducible.
$temp = Join-Path $dist "temp"
New-Item -ItemType Directory -Force -Path $temp | Out-Null
$env:TEMP = $temp
$env:TMP = $temp

Write-Host "→ Generando instalador Velopack ..." -ForegroundColor Cyan
& $vpk pack `
    --packId PokeLockeLauncher `
    --packVersion $Version `
    --packDir $publish `
    --mainExe PokeLockeLauncher.exe `
    --packTitle "PokeLocke Launcher" `
    --packAuthors "PokeLocke" `
    --outputDir $releases `
    --channel $Channel `
    --runtime win-x64

if ($LASTEXITCODE -ne 0) { throw "vpk pack falló con código $LASTEXITCODE" }
$global:LASTEXITCODE = 0

$setup = Get-ChildItem $releases -Filter "*Setup.exe" -File |
    Sort-Object LastWriteTime |
    Select-Object -Last 1

if ($setup) {
    Write-Host ("✔ Instalador: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB)) -ForegroundColor Green
}

# --- subir a GitHub ---------------------------------------------------------
$subir = -not $NoUpload -and -not [string]::IsNullOrWhiteSpace($RepoUrl)
if ($subir) {
    if ([string]::IsNullOrWhiteSpace($Token)) {
        throw "Falta el token. Define VELOPACK_GITHUB_TOKEN o pasa -Token."
    }

    Write-Host "→ Subiendo la release a GitHub ..." -ForegroundColor Cyan
    & $vpk upload github `
        --outputDir $releases `
        --repoUrl $RepoUrl `
        --token $Token `
        --channel $Channel `
        --publish

    if ($LASTEXITCODE -ne 0) { throw "vpk upload github falló con código $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
    Write-Host "✔ Release publicada. Los jugadores la recibirán al abrir el launcher." -ForegroundColor Green
}
elseif (-not $NoUpload) {
    Write-Host "Aviso: sin -RepoUrl no se ha subido nada. El instalador está en:" -ForegroundColor Yellow
    Write-Host "  $releases" -ForegroundColor Gray
}
