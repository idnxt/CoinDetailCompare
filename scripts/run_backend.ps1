# CoinDetailCompare - run the Python backend (FastAPI)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$backend = Join-Path $root "src\CoinDetailCompare.Backend"
$activate = Join-Path $backend ".venv\Scripts\Activate.ps1"

if (Test-Path $activate) { . $activate }

Set-Location $backend
python run.py @args
