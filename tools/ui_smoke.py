"""Development smoke test: drives the Tkinter window, captures screenshots.

Usage: python tools/ui_smoke.py [url]
"""

from __future__ import annotations

import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "src" / "Sirius.Browser"))

import tkinter as tk  # noqa: E402

from config import AppConfig  # noqa: E402
from engine_client import HostClient  # noqa: E402
from logging_setup import setup_logging  # noqa: E402
from ui.window import BrowserWindow  # noqa: E402

OUT = Path(tempfile.gettempdir()) / "opencode"
OUT.mkdir(parents=True, exist_ok=True)


def main() -> int:
    target = sys.argv[1] if len(sys.argv) > 1 else "https://html.duckduckgo.com/html/?q=sirius+browser+engine"
    workdir = Path(tempfile.mkdtemp(prefix="sirius-smoke-"))
    config_path = workdir / "config.json"

    config = AppConfig()
    config.start_url = target
    config.width = 1180
    config.height = 780
    config.show_search_bar = True

    logger = setup_logging(workdir / "sirius.log")
    root = tk.Tk()
    client = HostClient(config_path, logger)
    app = BrowserWindow(root, config, logger, client, config_path)
    root.protocol("WM_DELETE_WINDOW", app.on_close)
    root.attributes("-topmost", True)
    root.lift()
    root.focus_force()

    results: dict[str, str] = {}

    def capture(name: str) -> None:
        try:
            from PIL import ImageGrab

            root.update_idletasks()
            x, y = root.winfo_rootx(), root.winfo_rooty()
            w, h = root.winfo_width(), root.winfo_height()
            path = OUT / f"sirius_{name}.png"
            try:
                frame = int(root.wm_frame(), 16)
                ImageGrab.grab(window=frame).save(path)
            except Exception:
                ImageGrab.grab(bbox=(x, y, x + w, y + h)).save(path)
            page = app._page or {}
            results[name] = (
                f"title={app.root.title()} | address={app.address.get()} | "
                f"status={app.status_label.cget('text')} | stats={app.stats_label.cget('text')} | "
                f"blocks={len(page.get('blocks') or [])} | shot={path}"
            )
        except Exception as exc:  # noqa: BLE001
            results[name] = f"capture failed: {exc}"

    def next_page() -> None:
        app.search("python tkinter")
        root.after(6000, lambda: after_search())

    def after_search() -> None:
        capture("search")
        root.after(200, app.on_close)

    root.after(8000, lambda: capture("first"))
    root.after(8300, next_page)

    root.mainloop()

    for name, value in results.items():
        print(f"[{name}] {value}")
    print(f"log: {workdir / 'sirius.log'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
