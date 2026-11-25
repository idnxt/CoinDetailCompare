# CoinDetailCompare - restart the Python backend (FastAPI)
#
# Stops whatever process currently listens on the port (default 8300),
# then starts the backend hidden using the python that matches this
# installation (backend venv python if present, otherwise python on PATH).
# Used by the app's in-place GPU setup so a new torch build is picked up
# without the user touching anything.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$backend = Join-Path $root "src\CoinDetailCompare.Backend"
$envVenvPy = Join-Path $root "env\venv\Scripts\python.exe"
$venvPy = Join-Path $backend ".venv\Scripts\python.exe"

$python = $null
if (Test-Path $envVenvPy) {
    $python = $envVenvPy
} elseif (Test-Path $venvPy) {
    $python = $venvPy
} else {
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Write-Error "python not found (no venv and no 'python' on PATH)"
        exit 1
    }
    $python = $cmd.Source
}

$port = $args[0]
if (-not $port) { $port = "8300" }

$p = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
     Select-Object -First 1 -ExpandProperty OwningProcess
if ($p) {
    try { Stop-Process -Id $p -Force -ErrorAction Stop } catch {}
    Start-Sleep -Seconds 2
}
# Log uvicorn's output so a crash is diagnosable (the window is hidden).
$logDir = Join-Path $root "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Start-Process -WindowStyle Hidden -FilePath $python -ArgumentList "run.py" `
  -WorkingDirectory $backend `
  -RedirectStandardOutput (Join-Path $logDir "backend.out.log") `
  -RedirectStandardError (Join-Path $logDir "backend.err.log")
