# CoinDetailCompare - stop the Python backend (FastAPI)
#
# Kills whatever process currently listens on the port (default 8300).
# Quietly does nothing if nothing is listening.
$ErrorActionPreference = "Stop"

$port = $args[0]
if (-not $port) { $port = "8300" }

$p = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
     Select-Object -First 1 -ExpandProperty OwningProcess
if ($p) {
    Stop-Process -Id $p -Force -ErrorAction SilentlyContinue
}
