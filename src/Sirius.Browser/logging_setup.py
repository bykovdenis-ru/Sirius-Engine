"""Logging configuration. Every Sirius session writes sirius.log."""

from __future__ import annotations

import logging
import sys
from logging.handlers import RotatingFileHandler
from pathlib import Path

from config import is_frozen, writable_dir


def setup_logging(log_path: Path) -> logging.Logger:
    logger = logging.getLogger("sirius")
    logger.setLevel(logging.DEBUG)
    logger.propagate = False

    for handler in list(logger.handlers):
        logger.removeHandler(handler)
        handler.close()

    target = writable_dir(log_path.parent) / log_path.name
    formatter = logging.Formatter(
        fmt="%(asctime)s %(levelname)-7s %(name)-14s %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    try:
        file_handler = RotatingFileHandler(
            target, maxBytes=1_500_000, backupCount=3, encoding="utf-8"
        )
    except OSError:
        target = Path.home() / "sirius.log"
        file_handler = RotatingFileHandler(
            target, maxBytes=1_500_000, backupCount=3, encoding="utf-8"
        )

    file_handler.setLevel(logging.DEBUG)
    file_handler.setFormatter(formatter)
    logger.addHandler(file_handler)

    if not is_frozen() and sys.stderr is not None:
        console = logging.StreamHandler(sys.stderr)
        console.setLevel(logging.INFO)
        console.setFormatter(formatter)
        logger.addHandler(console)

    logger.info("=" * 72)
    logger.info("Sirius UI starting, log: %s", target)
    return logger


def log_path_for(base_dir: Path) -> Path:
    return base_dir / "sirius.log"
