"""Sirius 0.0.1 — entry point for the Tkinter front-end."""

from __future__ import annotations

import ctypes
import os
import sys
import traceback
from pathlib import Path

# Make sibling modules importable both from source and from a PyInstaller bundle.
BASE_DIR = Path(__file__).resolve().parent
if str(BASE_DIR) not in sys.path:
    sys.path.insert(0, str(BASE_DIR))

import tkinter as tk  # noqa: E402
from tkinter import messagebox  # noqa: E402

from config import AppConfig, app_dir, writable_dir, save_config  # noqa: E402
from engine_client import HostClient  # noqa: E402
from logging_setup import setup_logging  # noqa: E402
from ui.window import BrowserWindow  # noqa: E402
from version import APP_NAME, VERSION  # noqa: E402


def _enable_windows_dpi() -> None:
    if os.name != "nt":
        return

    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)
    except Exception:  # noqa: BLE001
        try:
            ctypes.windll.user32.SetProcessDPIAware()
        except Exception:  # noqa: BLE001
            pass

    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(
            f"Sirius.Browser.{VERSION}"
        )
    except Exception:  # noqa: BLE001
        pass


def _config_path() -> Path:
    base = app_dir()
    candidate = base / "config.json"
    try:
        probe = base / ".sirius-write-test"
        probe.write_text("ok", encoding="utf-8")
        probe.unlink(missing_ok=True)
        return candidate
    except OSError:
        folder = writable_dir(Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "Sirius")
        return folder / "config.json"


def main() -> int:
    _enable_windows_dpi()

    base = app_dir()
    logger = setup_logging(base / "sirius.log")
    logger.info("%s %s starting", APP_NAME, VERSION)

    config_path = _config_path()
    config = AppConfig.load(config_path)
    try:
        save_config(config_path, config)
    except Exception:  # noqa: BLE001
        logger.exception("Не удалось сохранить стартовую конфигурацию")

    root = tk.Tk()
    root.tk.call("tk", "scaling", max(1.0, root.winfo_fpixels("1i") / 72.0))

    client = HostClient(config_path, logger)
    window = BrowserWindow(root, config, logger, client, config_path)
    root.protocol("WM_DELETE_WINDOW", window.on_close)

    try:
        root.mainloop()
    finally:
        try:
            client.close()
        except Exception:  # noqa: BLE001
            logger.exception("Ошибка при остановке движка")

    logger.info("%s stopped", APP_NAME)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        details = traceback.format_exc()
        try:
            log = app_dir() / "sirius.log"
            with open(log, "a", encoding="utf-8") as handle:
                handle.write(details + "\n")
        except OSError:
            pass

        try:
            root = tk.Tk()
            root.withdraw()
            messagebox.showerror(
                "Sirius — критическая ошибка",
                "Не удалось запустить Sirius.\n\n" + details[-1200:],
            )
            root.destroy()
        except Exception:  # noqa: BLE001
            print(details, file=sys.stderr)
        raise SystemExit(1)
