<#
.SYNOPSIS
    Publica una nueva versión de la app y la sube a GitHub Releases (Velopack).
    Empaqueta en un solo comando: dotnet publish -> vpk pack -> vpk upload.

.DESCRIPTION
    La versión se lee automáticamente de <Version> en SistemaGestion\SistemaGestion.csproj,
    así que solo tienes que subir ese número antes de ejecutar este script.

.PARAMETER Token
    Token de GitHub con permiso sobre el repo de releases.
    Por defecto usa la variable de entorno GITHUB_TOKEN.

.PARAMETER Version
    (Opcional) Fuerza una versión concreta en vez de leerla del csproj.

.EXAMPLE
    $env:GITHUB_TOKEN = "ghp_xxx"
    .\publicar.ps1

.EXAMPLE
    .\publicar.ps1 -Token ghp_xxx -Version 1.2.0
#>

param(
    [string]$Token   = $env:GITHUB_TOKEN,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

# ─── Configuración (ajústala una sola vez si cambia algo) ──────────────────────
$PackId    = "SistemaGestion"
$MainExe   = "SistemaGestion.exe"
$PackTitle = "Sistema de Gestión"
$RepoUrl   = "https://github.com/maister1122/WpfAppVba"
$Csproj    = "SistemaGestion\SistemaGestion.csproj"
$Icon      = "SistemaGestion\icono.ico"
$PublishDir = ".\publish"

# Trabajar siempre desde la carpeta del script (raíz del repo).
Set-Location -Path $PSScriptRoot

# ─── 0. Validaciones previas ───────────────────────────────────────────────────
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "No se encontró 'vpk'. Instálalo una vez con: dotnet tool install -g vpk"
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "Falta el token de GitHub. Define `$env:GITHUB_TOKEN o pásalo con -Token."
}

# Leer la versión del csproj si no se forzó por parámetro.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $match = Select-String -Path $Csproj -Pattern '<Version>\s*([^<]+?)\s*</Version>' | Select-Object -First 1
    if (-not $match) {
        throw "No se pudo leer <Version> de $Csproj. Agrégalo o usa -Version."
    }
    $Version = $match.Matches[0].Groups[1].Value.Trim()
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Publicando $PackTitle  v$Version" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# ─── 1. Publish (self-contained, single-file) ──────────────────────────────────
Write-Host "`n[1/3] dotnet publish..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish $Csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=none `
    -o $PublishDir

# ─── 2. Pack (genera Setup.exe + .nupkg full/delta + RELEASES) ─────────────────
Write-Host "`n[2/3] vpk pack..." -ForegroundColor Yellow
vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $PublishDir `
    --mainExe $MainExe `
    --packTitle $PackTitle `
    --icon $Icon

# ─── 3. Upload (sube las releases al repo público) ─────────────────────────────
Write-Host "`n[3/3] vpk upload github..." -ForegroundColor Yellow
vpk upload github `
    --repoUrl $RepoUrl `
    --publish `
    --releaseName "v$Version" `
    --tag "v$Version" `
    --token $Token

Write-Host "`n✅ Listo. Versión v$Version publicada." -ForegroundColor Green
Write-Host "   Los usuarios verán el botón 'Actualizar' al abrir la app." -ForegroundColor Green
