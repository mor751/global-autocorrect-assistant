#requires -version 5
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "artifacts",
    [switch]$AppOnly,
    [switch]$CliOnly
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$artifactPath = Join-Path $root $OutputDir
$woodyInstall = Join-Path $env:LOCALAPPDATA "Programs\woody"

function Publish-App {
    $projectPath = Join-Path $root "src\Autocorrect.App\Autocorrect.App.csproj"
    dotnet publish $projectPath `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $artifactPath

    $exe = Join-Path $artifactPath "GlobalAutocorrect.exe"
    if (-not (Test-Path $exe)) {
        throw "Publish finished but $exe was not found."
    }

    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "GlobalAutocorrect: $exe ($sizeMb MB)" -ForegroundColor Green
}

function Publish-WoodyCli {
    $projectPath = Join-Path $root "src\Autocorrect.Cli\Autocorrect.Cli.csproj"
    New-Item -ItemType Directory -Force -Path $woodyInstall | Out-Null

    dotnet publish $projectPath `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -o $woodyInstall

    $exe = Join-Path $woodyInstall "woody.exe"
    if (-not (Test-Path $exe)) {
        throw "Publish finished but $exe was not found."
    }

    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($userPath -notlike "*$woodyInstall*") {
        [Environment]::SetEnvironmentVariable("Path", "$userPath;$woodyInstall", "User")
        $env:Path = "$env:Path;$woodyInstall"
        Write-Host "Added woody to user PATH: $woodyInstall" -ForegroundColor Yellow
    }

    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "woody CLI: $exe ($sizeMb MB)" -ForegroundColor Green
}

if (-not $CliOnly) { Publish-App }
if (-not $AppOnly) { Publish-WoodyCli }

Write-Host ""
Write-Host "Update complete. Restart GlobalAutocorrect if it is running." -ForegroundColor Cyan
Write-Host "CLI: woody reload   (sync brain when files changed)" -ForegroundColor Cyan
Write-Host "CLI: woody reload --force   (full re-index)" -ForegroundColor Cyan
