# CoinDetailCompare - run the Python backend (FastAPI)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$backend = Join-Path $root "src\CoinDetailCompare.Backend"

$py = $null
foreach ($cand in @(
    (Join-Path $root "env\venv\Scripts\python.exe"),
    (Join-Path $backend ".venv\Scripts\python.exe")
)) {
    if (Test-Path $cand) { $py = $cand; break }
}
if (-not $py) { $py = (Get-Command python).Source }

Set-Location $backend
& $py run.py @args
