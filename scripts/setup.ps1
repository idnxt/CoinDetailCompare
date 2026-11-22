# CoinDetailCompare - one-time Python environment setup
# Creates a venv and installs backend dependencies.
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$backend = Join-Path $root "src\CoinDetailCompare.Backend"
$venv = Join-Path $backend ".venv"

Write-Host "Creating virtual environment..." -ForegroundColor Cyan
python -m venv $venv
$activate = Join-Path $venv "Scripts\Activate.ps1"
. $activate

Write-Host "Installing requirements..." -ForegroundColor Cyan
python -m pip install --upgrade pip
python -m pip install -r (Join-Path $backend "requirements.txt")

Write-Host "Done. Activate with:  . $activate" -ForegroundColor Green
Write-Host "NOTE: pin torch (CPU/GPU) + faiss per your hardware; install dinov3/anomalib per docs."
