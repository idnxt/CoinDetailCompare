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

    def _headers(self) -> dict:
        headers = {"User-Agent": "CoinDetailCompare/0.1 (local catalog tool)"}
        if self.cfg.numista_api_key:
            # Numista API expects the token in the Authorization header.
            headers["Authorization"] = f"Token {self.cfg.numista_api_key}"
        return headers

    def search(self, query: str, limit: int = 20) -> list[dict]:
        import requests  # type: ignore

        url = f"{self.cfg.numista_base_url}{self.cfg.numista_search_path}"
        params = {"searchtext": query, "q": query, "limit": limit, "page": 1}
        try:
            resp = requests.get(
                url, params=params, headers=self._headers(), timeout=20
            )
            resp.raise_for_status()
            data = resp.json()
        except Exception as exc:  # network / parse / auth
            return [{"error": str(exc)}]
        return self._parse(data)

    def _parse(self, data) -> list[dict]:
        """Best-effort mapping of the API response to normalized items.

        Tolerates several known/plausible response shapes: a bare list, or a
        dict wrapping the list under any of results/items/series/data.
        """
        if isinstance(data, dict) and data.get("error"):
            return [{"error": str(data["error"])}]

        results = []
        if isinstance(data, list):
            results = data
        elif isinstance(data, dict):
            for key in ("results", "items", "series", "data", "hits", "rows"):
                v = data.get(key)
                if isinstance(v, list):
                    results = v
                    break
                if isinstance(v, dict):  # nested, e.g. {"data": {"items": [...]}}
                    for k2 in ("items", "results", "series", "list"):
                        v2 = v.get(k2)
                        if isinstance(v2, list):
                            results = v2
                            break
                    if results:
                        break

        items: list[dict] = []
        for r in results:
            if not isinstance(r, dict):
                continue
            num_id = _as_int(
                r.get("numista_id") or r.get("id") or r.get("series_id") or r.get("coin_id")
            )
            items.append(
                {
                    "numista_id": num_id,
                    "title": r.get("title") or r.get("name") or r.get("name_ru"),
                    "url": _numista_url(num_id, r.get("url")),
                    "country": r.get("country") or r.get("country_name"),
                    "denomination": r.get("denomination") or r.get("value") or r.get("name"),
                    "year": str(r.get("date") or r.get("year") or ""),
                }
            )
        return items

    def fetch_meta(self, link: str) -> dict:
        """Validate a manual link and fetch a bit of metadata for it.

        Tries the API (series/coin by id) first, then falls back to the
        public web page <title>. Returns the parse result plus optional
        `title`/`source`.
        """
        info = parse_manual_numista_link(link)
        info["title"] = None
        info["source"] = None
        import requests  # type: ignore

        num_id = info["numista_id"]
        # 1) API endpoint (best effort)
        for path in (f"/series/{num_id}", f"/coins/{num_id}"):
            try:
                resp = requests.get(
                    f"{self.cfg.numista_base_url}{path}",
                    headers=self._headers(),
                    timeout=15,
                )
                if resp.status_code == 200:
                    data = resp.json()
                    title = (
                        (data.get("title") or data.get("name") or data.get("name_ru"))
                        if isinstance(data, dict)
                        else None
                    )
                    if title:
                        info["title"] = title
                        info["source"] = "api"
                        return info
            except Exception:
                continue
        # 2) Public page <title>
        page_url = f"https://ru.numista.com/{num_id}"
        try:
            resp = requests.get(page_url, headers=self._headers(), timeout=15)
            if resp.status_code == 200:
                m = re.search(r"<title[^>]*>(.*?)</title>", resp.text, re.S | re.I)
                if m:
                    info["title"] = m.group(1).strip()
                    info["source"] = "page"
        except Exception:
            pass
        return info


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
