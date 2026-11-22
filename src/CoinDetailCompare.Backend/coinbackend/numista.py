"""Numista API client (textual search) and manual-link parsing.

Docs: https://ru.numista.com/api/doc/index.php
Base URL / key come from config. Requests use the public `requests` library.
"""
from __future__ import annotations

import re
from urllib.parse import urlparse

from .config import Config


class NumistaClient:
    def __init__(self, cfg: Config) -> None:
        self.cfg = cfg

    def search(self, query: str, limit: int = 20) -> list[dict]:
        import requests  # type: ignore

        url = f"{self.cfg.numista_base_url}/search"
        headers = {}
        if self.cfg.numista_api_key:
            # Numista API expects the token in the Authorization header.
            headers["Authorization"] = f"Token {self.cfg.numista_api_key}"
        params = {"searchtext": query, "limit": limit}
        try:
            resp = requests.get(url, params=params, headers=headers, timeout=20)
            resp.raise_for_status()
            data = resp.json()
        except Exception as exc:  # network / parse / auth
            return [{"error": str(exc)}]
        return self._parse(data)

    def _parse(self, data) -> list[dict]:
        """Best-effort mapping of the API response to normalized items.

        Finalize field names against the live API doc in Phase 6.
        """
        items: list[dict] = []
        # Expected: {"results": [ { "id": int, "title": str, "image": {...}, ... } ]}
        results = data.get("results", data.get("items", [])) if isinstance(data, dict) else []
        for r in results:
            if not isinstance(r, dict):
                continue
            items.append(
                {
                    "numista_id": _as_int(r.get("id") or r.get("numista_id")),
                    "title": r.get("title") or r.get("name"),
                    "url": _numista_url(r.get("id") or r.get("numista_id"), r.get("url")),
                    "country": r.get("country"),
                    "denomination": r.get("denomination") or r.get("value"),
                    "year": str(r.get("date") or r.get("year") or ""),
                }
            )
        return items


def _as_int(v) -> int | None:
    try:
        return int(v)
    except (TypeError, ValueError):
        return None


def _numista_url(num_id, explicit=None) -> str | None:
    if explicit:
        return str(explicit)
    n = _as_int(num_id)
    return f"https://www.numista.com/{n}" if n is not None else None


def parse_manual_numista_link(link: str) -> dict:
    """Parse a manually provided Numista link.

    Supports both series (https://www.numista.com/966) and
    coin (https://www.numista.com/coin/3023) forms.
    """
    link = (link or "").strip()
    parsed = urlparse(link if "://" in link else f"https://{link}")
    host = (parsed.netloc or "").lower()
    if "numista" not in host:
        raise ValueError("Not a Numista URL")
    parts = [p for p in parsed.path.split("/") if p]
    kind = None
    num_id = None
    for i, p in enumerate(parts):
        if p in ("series", "coin") and i + 1 < len(parts) and parts[i + 1].isdigit():
            kind = p
            num_id = int(parts[i + 1])
            break
    if num_id is None and len(parts) == 1 and parts[0].isdigit():
        num_id = int(parts[0])
        kind = "series"
    if num_id is None:
        raise ValueError("Could not extract a Numista ID from the link")
    return {"numista_id": num_id, "kind": kind, "url": link}
