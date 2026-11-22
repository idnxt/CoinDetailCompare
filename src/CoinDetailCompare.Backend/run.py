"""Run the CoinDetailCompare backend locally.

Usage:
    python run.py                 # uses config/settings.json (or example)
    python run.py --host 127.0.0.1 --port 8300
"""
from __future__ import annotations

import argparse

from coinbackend.config import Config


def main() -> None:
    parser = argparse.ArgumentParser(description="CoinDetailCompare backend")
    parser.add_argument("--config", default=None, help="Path to settings.json")
    parser.add_argument("--host", default=None)
    parser.add_argument("--port", type=int, default=None)
    args = parser.parse_args()

    cfg = Config.load(args.config)
    if args.host:
        cfg.host = args.host
    if args.port:
        cfg.port = args.port

    import uvicorn  # type: ignore
    from coinbackend.app import app  # imports build singletons from Config.load()

    uvicorn.run(app, host=cfg.host, port=cfg.port, reload=False)


if __name__ == "__main__":
    main()
