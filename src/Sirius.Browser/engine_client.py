"""JSON-over-stdio client for Sirius.Host (the C# engine process)."""

from __future__ import annotations

import json
import logging
import os
import subprocess
import threading
import time
from collections import deque
from pathlib import Path
from typing import Any

from config import find_host


class EngineError(RuntimeError):
    """Raised when the engine process cannot fulfil a request."""


class HostClient:
    """Owns the long-running Sirius.Host process and serialises requests to it."""

    def __init__(self, config_path: Path, logger: logging.Logger) -> None:
        self._config_path = config_path
        self._logger = logger
        self._lock = threading.Lock()
        self._process: subprocess.Popen[str] | None = None
        self._sequence = 0
        self._stderr_tail: deque[str] = deque(maxlen=40)
        self._command: list[str] | None = None
        self._host_description = ""
        self._started_at: float | None = None
        self._engine_info: dict[str, Any] = {}

        command, description = find_host()
        self._command = command
        self._host_description = description
        if command is None:
            self._logger.error("Sirius.Host не найден рядом с приложением: %s", os.getcwd())

    # ------------------------------------------------------------------ lifecycle

    @property
    def available(self) -> bool:
        return self._command is not None

    @property
    def description(self) -> str:
        return self._host_description

    @property
    def engine_info(self) -> dict[str, Any]:
        return dict(self._engine_info)

    def start(self) -> None:
        with self._lock:
            self._ensure_process_locked(ping=True)

    def _ensure_process_locked(self, ping: bool = False) -> subprocess.Popen[str]:
        if self._process is not None and self._process.poll() is None:
            return self._process

        if self._command is None:
            raise EngineError(
                "Не найден Sirius.Host.exe. Соберите движок: dotnet build src\\Sirius.Host"
            )

        creation_flags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
        args = list(self._command) + ["--config", str(self._config_path)]

        self._logger.info("Запуск движка: %s", " ".join(args))
        try:
            self._process = subprocess.Popen(
                args,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                errors="replace",
                bufsize=1,
                creationflags=creation_flags,
                cwd=str(self._config_path.parent),
            )
        except OSError as exc:
            raise EngineError(f"Не удалось запустить движок: {exc}") from exc

        self._started_at = time.time()
        threading.Thread(target=self._read_stderr, name="sirius-host-stderr", daemon=True).start()

        if ping:
            reply = self._send_locked({"cmd": "ping"})
            self._engine_info = reply.get("info") or {}
            self._logger.info(
                "Движок готов: %s v%s (%s)",
                self._engine_info.get("product", "Sirius"),
                self._engine_info.get("version", "?"),
                self._engine_info.get("runtime", "?"),
            )

        return self._process

    def _read_stderr(self) -> None:
        process = self._process
        if process is None or process.stderr is None:
            return

        for line in process.stderr:
            line = line.rstrip()
            if not line:
                continue
            self._stderr_tail.append(line)
            self._logger.debug("engine | %s", line)

    # ------------------------------------------------------------------- requests

    def request(self, command: str, **payload: Any) -> dict[str, Any]:
        with self._lock:
            self._ensure_process_locked()
            return self._send_locked({"cmd": command, **payload})

    def _send_locked(self, message: dict[str, Any]) -> dict[str, Any]:
        process = self._process
        if process is None or process.stdin is None or process.stdout is None:
            raise EngineError("Движок не запущен.")

        if process.poll() is not None:
            raise EngineError("Процесс движка завершился. " + self._tail_message())

        self._sequence += 1
        message["id"] = self._sequence

        try:
            process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
            process.stdin.flush()
        except OSError as exc:
            raise EngineError(f"Не удалось отправить запрос движку: {exc}") from exc

        try:
            line = process.stdout.readline()
        except OSError as exc:
            raise EngineError(f"Не удалось прочитать ответ движка: {exc}") from exc

        if not line:
            raise EngineError("Движок закрыл соединение. " + self._tail_message())

        try:
            reply = json.loads(line)
        except json.JSONDecodeError as exc:
            raise EngineError(f"Некорректный ответ движка: {line[:200]}") from exc

        if not reply.get("success"):
            raise EngineError(reply.get("error") or "Движок вернул ошибку.")

        return reply

    def _tail_message(self) -> str:
        if not self._stderr_tail:
            return ""
        return "Последние сообщения: " + " | ".join(list(self._stderr_tail)[-3:])

    # -------------------------------------------------------------------- helpers

    def navigate(self, url: str) -> dict[str, Any]:
        return self.request("navigate", url=url)

    def search(self, query: str) -> dict[str, Any]:
        return self.request("search", query=query)

    def back(self) -> dict[str, Any]:
        return self.request("back")

    def forward(self) -> dict[str, Any]:
        return self.request("forward")

    def reload(self) -> dict[str, Any]:
        return self.request("reload")

    def home(self) -> dict[str, Any]:
        return self.request("home")

    def info(self) -> dict[str, Any]:
        reply = self.request("info")
        self._engine_info = reply.get("info") or self._engine_info
        return self._engine_info

    # ----------------------------------------------------------------------- close

    def close(self) -> None:
        process = self._process
        if process is None:
            return

        self._logger.info("Остановка движка")
        if process.poll() is None:
            try:
                if process.stdin is not None:
                    process.stdin.write('{"cmd":"shutdown"}\n')
                    process.stdin.flush()
            except OSError:
                pass

            try:
                process.wait(timeout=2.0)
            except subprocess.TimeoutExpired:
                process.terminate()
                try:
                    process.wait(timeout=2.0)
                except subprocess.TimeoutExpired:
                    process.kill()

        self._process = None
        self._logger.info("Движок остановлен")


__all__ = ["EngineError", "HostClient"]
