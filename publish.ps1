#requires -version 5
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "artifacts"
)

# Produces a single self-contained GlobalAutocorrect.exe that runs on Windows without installing .NET.
$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "src\Autocorrect.App\Autocorrect.App.csproj"
$outputPath = Join-Path $PSScriptRoot $OutputDir

dotnet publish $projectPath `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $outputPath

$exe = Join-Path $outputPath "GlobalAutocorrect.exe"
if (Test-Path $exe) {
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Build succeeded: $exe ($sizeMb MB)" -ForegroundColor Green
    Write-Host "Share this single file. Users double-click it; no .NET install needed." -ForegroundColor Green
}
else {
    throw "Publish finished but $exe was not found."
}
