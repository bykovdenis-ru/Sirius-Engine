"""Paths, configuration loading and persistence for the Sirius UI."""

from __future__ import annotations

import json
import os
import sys
from dataclasses import asdict, dataclass, fields
from pathlib import Path
from typing import Any

from version import HOME_URL, SEARCH_ENDPOINT, SEARCH_ENGINE


def is_frozen() -> bool:
    return bool(getattr(sys, "frozen", False))


def app_dir() -> Path:
    """Directory next to Sirius.exe (or the project root when run from source)."""
    if is_frozen():
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parents[2]


def resource_path(relative: str) -> Path:
    """Resolve a bundled resource both in development and inside PyInstaller."""
    if is_frozen():
        base = Path(getattr(sys, "_MEIPASS", app_dir()))
        candidate = base / relative
        if candidate.exists():
            return candidate
    return app_dir() / relative


def writable_dir(preferred: Path) -> Path:
    """Return the preferred directory if we may write to it, else LOCALAPPDATA/Sirius."""
    try:
        preferred.mkdir(parents=True, exist_ok=True)
        probe = preferred / ".write-test"
        probe.write_text("ok", encoding="utf-8")
        probe.unlink(missing_ok=True)
        return preferred
    except OSError:
        fallback = Path(os.environ.get("LOCALAPPDATA", Path.home())) / "Sirius"
        fallback.mkdir(parents=True, exist_ok=True)
        return fallback


@dataclass
class AppConfig:
    home_url: str = HOME_URL
    search_endpoint: str = SEARCH_ENDPOINT
    search_engine_name: str = SEARCH_ENGINE
    timeout_seconds: int = 30
    max_response_bytes: int = 8 * 1024 * 1024
    user_agent: str = ""

    start_url: str = HOME_URL
    width: int = 1180
    height: int = 780
    x: int | None = None
    y: int | None = None
    font_scale: float = 1.0
    show_search_bar: bool = True
    restore_last_page: bool = False
    last_url: str = ""

    @classmethod
    def load(cls, path: Path) -> "AppConfig":
        config = cls()
        if not path.exists():
            return config

        try:
            raw = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError):
            return config

        if not isinstance(raw, dict):
            return config

        mapping = {
            "home_url": ("homeUrl", "home_url"),
            "search_endpoint": ("searchEndpoint", "search_endpoint"),
            "search_engine_name": ("searchEngineName", "search_engine_name"),
            "timeout_seconds": ("timeoutSeconds", "timeout_seconds"),
            "max_response_bytes": ("maxResponseBytes", "max_response_bytes"),
            "user_agent": ("userAgent", "user_agent"),
            "start_url": ("startUrl", "start_url"),
            "width": ("windowWidth", "width"),
            "height": ("windowHeight", "height"),
            "x": ("windowX", "x"),
            "y": ("windowY", "y"),
            "font_scale": ("fontScale", "font_scale"),
            "show_search_bar": ("showSearchBar", "show_search_bar"),
            "restore_last_page": ("restoreLastPage", "restore_last_page"),
            "last_url": ("lastUrl", "last_url"),
        }

        for field in fields(cls):
            keys = mapping.get(field.name, (field.name,))
            for key in keys:
                if key not in raw:
                    continue
                value = raw[key]
                if field.name in ("width", "height", "timeout_seconds", "max_response_bytes"):
                    try:
                        value = int(value)
                    except (TypeError, ValueError):
                        break
                elif field.name == "font_scale":
                    try:
                        value = min(2.0, max(0.7, float(value)))
                    except (TypeError, ValueError):
                        break
                elif field.name in ("show_search_bar", "restore_last_page"):
                    value = bool(value)
                elif field.name in ("x", "y") and value is not None:
                    try:
                        value = int(value)
                    except (TypeError, ValueError):
                        break
                setattr(config, field.name, value)
                break

        return config

    def save(self, path: Path) -> None:
        payload: dict[str, Any] = {
            "homeUrl": self.home_url,
            "searchEndpoint": self.search_endpoint,
            "searchEngineName": self.search_engine_name,
            "timeoutSeconds": self.timeout_seconds,
            "maxResponseBytes": self.max_response_bytes,
            "startUrl": self.start_url,
            "windowWidth": self.width,
            "windowHeight": self.height,
            "windowX": self.x,
            "windowY": self.y,
            "fontScale": round(self.font_scale, 2),
            "showSearchBar": self.show_search_bar,
            "restoreLastPage": self.restore_last_page,
            "lastUrl": self.last_url,
        }
        if self.user_agent:
            payload["userAgent"] = self.user_agent

        path.parent.mkdir(parents=True, exist_ok=True)
        temp = path.with_suffix(".json.tmp")
        temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        temp.replace(path)


def find_host() -> tuple[list[str] | None, str]:
    """
    Locate Sirius.Host. Returns (command arguments, description).
    Prefers a published exe next to the app, then the .NET build output.
    """
    override = os.environ.get("SIRIUS_HOST")
    base = app_dir()

    candidates = []
    if override:
        candidates.append(Path(override))
    candidates += [
        base / "Sirius.Host.exe",
        base / "runtime" / "Sirius.Host.exe",
        base.parent / "Sirius.Host.exe",
        base / "src" / "Sirius.Host" / "bin" / "Release" / "net8.0" / "Sirius.Host.exe",
        base / "src" / "Sirius.Host" / "bin" / "Debug" / "net8.0" / "Sirius.Host.exe",
    ]

    for candidate in candidates:
        if candidate.is_file():
            return [str(candidate)], str(candidate)

    dll_candidates = [
        base / "Sirius.Host.dll",
        base / "runtime" / "Sirius.Host.dll",
        base / "src" / "Sirius.Host" / "bin" / "Release" / "net8.0" / "Sirius.Host.dll",
    ]
    for candidate in dll_candidates:
        if candidate.is_file():
            return ["dotnet", str(candidate)], str(candidate)

    return None, ""


def save_config(path: Path, config: AppConfig) -> None:
    try:
        config.save(path)
    except OSError:
        pass


__all__ = [
    "AppConfig",
    "app_dir",
    "find_host",
    "is_frozen",
    "resource_path",
    "save_config",
    "writable_dir",
]
