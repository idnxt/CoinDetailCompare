"""Full environment setup for the CoinDetailCompare backend.

Everything is installed into <repo>/env so the user never installs anything
by hand:

    env/venv/     backend virtualenv (all dependencies incl. torch)

The venv is based on an already installed Python (any 3.10+ on the
machine). If no Python exists at all, one is downloaded from python.org
and installed silently into env/python. NOTE: the python.org installer is
a no-op (exit code 0) on machines that already have a Python of the same
version — it "detects" the existing installation — so the script first
looks for a system Python and reuses it.

Steps:
    1. NVIDIA driver check (nvidia-smi). The DRIVER itself is the only
       manual step — if it is missing we install the CPU torch build and
       say so.
    2. Python: existing system python, otherwise silent install into
       env/python.
    3. venv env/venv + project requirements.
    4. PyTorch: CUDA 12.8 build when the driver is present (the cu124
       build lacked sm_120 kernels for the RTX 5070 Ti and silently fell
       back to CPU), CPU build otherwise.
    5. Kernel probe in a fresh interpreter: cuda available + real matmul
       on the device.

The script uses the standard library only, so it can be started by ANY
python (including the small embeddable one the app downloads as a
bootstrap).
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ENV = ROOT / "env"
ENV_PY = ENV / "python" / "python.exe"
ENV_VENV_PY = ENV / "venv" / "Scripts" / "python.exe"
BACKEND = ROOT / "src" / "CoinDetailCompare.Backend"

PYTHON_VERSION = "3.12.10"
PYTHON_INSTALLER = f"https://www.python.org/ftp/python/{PYTHON_VERSION}/python-{PYTHON_VERSION}-amd64.exe"
TORCH_INDEX = "https://download.pytorch.org/whl/cu128"


def run_logged(cmd: list[str]) -> int:
    print("+", " ".join(str(c) for c in cmd), flush=True)
    return subprocess.call(cmd)


def run_capture(cmd: list[str]) -> str:
    r = subprocess.run(cmd, capture_output=True, text=True)
    return (r.stdout or "").strip()


def find_system_python() -> Path | None:
    """Find any usable Python 3.10+ on the machine (outside env/)."""
    candidates: list[Path] = []
    for name in ("python", "python3"):
        p = shutil.which(name)
        if p:
            candidates.append(Path(p))
    for base in (
        Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Python",
        Path(r"C:\Program Files"),
        Path(r"C:\Program Files (x86)"),
    ):
        if base and base.is_dir():
            candidates.extend(sorted(base.glob("Python3*/python.exe"), reverse=True))
    seen: set[str] = set()
    for cand in candidates:
        key = str(cand).lower()
        if key in seen:
            continue
        seen.add(key)
        try:
            if ENV.resolve() in cand.resolve().parents:
                continue  # skip env/_bootstrap and env/python
        except OSError:
            continue
        v = run_capture([str(cand), "-c",
                         "import sys; print('%d.%d' % sys.version_info[:2])"])
        parts = v.split(".")
        if len(parts) == 2 and all(p.isdigit() for p in parts) and (
            int(parts[0]), int(parts[1])
        ) >= (3, 10):
            return cand
    return None


def main() -> int:
    print(f"python: {sys.executable}")
    print(f"env dir: {ENV}")

    # ---- 1. NVIDIA driver (the only manual step) -----------------------
    nvidia_smi = shutil.which("nvidia-smi")
    if nvidia_smi is None:
        print(
            "WARNING: nvidia-smi not found. Install the NVIDIA driver "
            "manually (https://www.nvidia.com/drivers). Installing the CPU "
            "torch build for now."
        )
        has_gpu = False
    else:
        info = run_capture([nvidia_smi, "--query-gpu=name,driver_version,memory.total",
                            "--format=csv,noheader"])
        print("GPU:", info or "unknown")
        has_gpu = bool(info)

    # ---- 2. Python base for the venv ------------------------------------
    # Priority: env/python (from a previous clean install) -> any existing
    # system Python 3.10+ -> silent download+install into env/python.
    base_py: Path
    if ENV_PY.exists():
        print(f"python: {ENV_PY}")
        base_py = ENV_PY
    else:
        found = find_system_python()
        if found is not None:
            ver = run_capture([str(found), "--version"])
            print(f"using existing python ({ver}): {found}")
            base_py = found
        else:
            print("no python found on this machine — installing into env/python")
            ENV.mkdir(exist_ok=True)
            installer = ENV / "python-installer.exe"
            print(f"downloading Python {PYTHON_VERSION} ...")
            with urllib.request.urlopen(PYTHON_INSTALLER) as r, open(installer, "wb") as f:
                shutil.copyfileobj(r, f)
            print("installing python into env/python (silent) ...")
            rc = run_logged([
                str(installer),
                "/quiet", "Wait=1",
                f"TargetDir={ENV / 'python'}",
                "InstallAllUsers=0",
                "Include_launcher=0",
                "Include_test=0",
                "Include_tcltk=0",
                "PrependPath=0",
            ])
            if rc != 0 or not ENV_PY.exists():
                print(f"ERROR: python installer exited with code {rc}")
                return 1
            print("python installed:", run_capture([str(ENV_PY), "--version"]))
            try:
                installer.unlink()
            except OSError:
                pass
            base_py = ENV_PY

    # ---- 3. venv + requirements ----------------------------------------
    if ENV_VENV_PY.exists():
        print(f"venv already exists: {ENV / 'venv'}")
    else:
        rc = run_logged([str(base_py), "-m", "venv", str(ENV / "venv")])
        if rc != 0:
            print("ERROR: failed to create venv")
            return 1

    rc = run_logged([str(ENV_VENV_PY), "-m", "pip", "install", "--upgrade", "pip"])
    if rc != 0:
        print("ERROR: pip upgrade failed")
        return 1

    # PyTorch FIRST (cu128 wheel when the driver is present); then the rest
    # of requirements.txt keeps it, because the version already satisfies
    # the "torch>=2.3" pin.
    if has_gpu:
        rc = run_logged([
            str(ENV_VENV_PY), "-m", "pip", "install", "--upgrade",
            "torch", "torchvision", "--index-url", TORCH_INDEX,
        ])
        if rc != 0:
            print("ERROR: torch install failed")
            return 1
    rc = run_logged([
        str(ENV_VENV_PY), "-m", "pip", "install", "-r", str(BACKEND / "requirements.txt"),
    ])
    if rc != 0:
        print("ERROR: requirements install failed")
        return 1

    # ---- 4. Kernel probe in a fresh interpreter ------------------------
    probe = (
        "import torch\n"
        "print('torch', torch.__version__, 'cuda_available', torch.cuda.is_available())\n"
        "if torch.cuda.is_available():\n"
        "    d = torch.device('cuda')\n"
        "    a = torch.randn(64, 64, device=d)\n"
        "    (a @ a).sum().item()\n"
        "    print('kernel probe OK on', torch.cuda.get_device_name(0))\n"
        "else:\n"
        "    print('CPU build (no CUDA)')\n"
    )
    rc = run_logged([str(ENV_VENV_PY), "-c", probe])
    if rc != 0:
        print("ERROR: kernel probe failed")
        return 1

    print("SETUP OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
