# CoinDetailCompare - build and run the C# desktop app
# Requires the .NET 8 SDK (the runtime alone is not enough).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root "src\CoinDetailCompare.App"

Set-Location $app
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit $LASTEXITCODE }
dotnet run -c Release
