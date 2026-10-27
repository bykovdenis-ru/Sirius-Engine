"""Main Sirius window: toolbar, reading surface, status bar and navigation."""

from __future__ import annotations

import os
import queue
import threading
import tkinter as tk
from pathlib import Path
from tkinter import messagebox, ttk
from typing import Any
from urllib.parse import urlsplit

from config import AppConfig, resource_path, save_config
from engine_client import EngineError, HostClient
from ui.renderer import PageRenderer
from ui.theme import COLORS, PlaceholderEntry, Theme, ToolButton, ToolTip
from version import APP_NAME, VERSION


def format_bytes(value: Any) -> str:
    try:
        size = float(value)
    except (TypeError, ValueError):
        return "—"
    if size < 1024:
        return f"{int(size)} Б"
    if size < 1024 * 1024:
        return f"{size / 1024:.1f} КБ"
    return f"{size / (1024 * 1024):.1f} МБ"


def plural(number: int, one: str, few: str, many: str) -> str:
    if number % 10 == 1 and number % 100 != 11:
        return one
    if 2 <= number % 10 <= 4 and not 12 <= number % 100 <= 14:
        return few
    return many


class BrowserWindow:
    def __init__(
        self,
        root: tk.Tk,
        config: AppConfig,
        logger,
        client: HostClient,
        config_path: Path,
    ) -> None:
        self.root = root
        self.config = config
        self.logger = logger
        self.client = client
        self.config_path = config_path

        self.theme = Theme(root)
        self._queue: queue.Queue[tuple[int, dict[str, Any] | None, Exception | None]] = queue.Queue()
        self._active_seq = 0
        self._poll_id: str | None = None
        self._loading = False
        self._closing = False
        self._page: dict[str, Any] | None = None
        self._last_url = config.start_url
        self._find_matches = 0

        self._configure_root()
        self._build_menu()
        self._build_chrome()
        self._build_content()
        self._build_status()
        self._build_find_bar()
        self._bind_shortcuts()

        self.renderer = PageRenderer(
            self.text,
            self.theme,
            on_link=self.navigate,
            on_status=self._set_hover_status,
            font_scale=self.config.font_scale,
        )

        self._set_nav_state(False, False)
        self.text.configure(state="normal")
        self._insert_welcome()
        self.text.configure(state="disabled")

        start_url = config.start_url
        if config.restore_last_page and config.last_url:
            start_url = config.last_url
        self.root.after(80, lambda: self._request("navigate", start_url, label="Открываем стартовую страницу…"))

    # --------------------------------------------------------------------- setup

    def _configure_root(self) -> None:
        root = self.root
        root.title(f"{APP_NAME} {VERSION}")
        root.configure(bg=COLORS["bg"])
        root.minsize(880, 560)

        width = max(880, int(self.config.width or 1180))
        height = max(560, int(self.config.height or 780))
        root.geometry(f"{width}x{height}")

        if self.config.x is not None and self.config.y is not None:
            root.geometry(f"+{int(self.config.x)}+{int(self.config.y)}")
        else:
            root.update_idletasks()
            screen_w = root.winfo_screenwidth()
            screen_h = root.winfo_screenheight()
            x = max(0, (screen_w - width) // 2)
            y = max(0, (screen_h - height) // 2 - 20)
            root.geometry(f"+{x}+{y}")

        icon = resource_path("assets/sirius.ico")
        try:
            if icon.exists():
                root.iconbitmap(default=str(icon))
        except tk.TclError:
            pass

        png = resource_path("assets/icon.png")
        try:
            if png.exists():
                self._icon_image = tk.PhotoImage(file=str(png))
                root.iconphoto(True, self._icon_image)
        except tk.TclError:
            pass

        self._apply_dark_titlebar()

    def _apply_dark_titlebar(self) -> None:
        """Ask DWM for a dark caption on Windows 10/11."""
        if os.name != "nt":
            return
        try:
            import ctypes

            self.root.update_idletasks()
            hwnd = ctypes.windll.user32.GetParent(self.root.winfo_id())
            if not hwnd:
                return

            enabled = ctypes.c_int(1)
            for attribute in (20, 19):
                ctypes.windll.dwmapi.DwmSetWindowAttribute(
                    hwnd, attribute, ctypes.byref(enabled), ctypes.sizeof(enabled)
                )

            caption = ctypes.c_int(0x00140E0B)  # COLORREF for #0b0e14
            caption_text = ctypes.c_int(0x00EBE7E5)  # COLORREF for #e5e7eb
            for attribute, value in ((35, caption), (36, caption_text)):
                ctypes.windll.dwmapi.DwmSetWindowAttribute(
                    hwnd, attribute, ctypes.byref(value), ctypes.sizeof(value)
                )
        except Exception:  # noqa: BLE001 - cosmetic only
            pass

    def _build_menu(self) -> None:
        menubar = tk.Menu(
            self.root,
            tearoff=0,
            bg=COLORS["chrome"],
            fg=COLORS["text"],
            activebackground=COLORS["accent"],
            activeforeground="#ffffff",
            bd=0,
        )

        file_menu = self._submenu(menubar)
        file_menu.add_command(label="Новая вкладка", accelerator="Ctrl+T", command=self.go_home)
        file_menu.add_command(label="Открыть README.md", command=self.open_readme)
        file_menu.add_separator()
        file_menu.add_command(label="Выход", accelerator="Ctrl+Q", command=self.on_close)
        menubar.add_cascade(label="Файл", menu=file_menu)

        edit_menu = self._submenu(menubar)
        edit_menu.add_command(label="Копировать", accelerator="Ctrl+C", command=self.copy_selection)
        edit_menu.add_command(label="Выделить всё", accelerator="Ctrl+A", command=self.select_all)
        edit_menu.add_separator()
        edit_menu.add_command(label="Найти на странице", accelerator="Ctrl+F", command=self.show_find)
        menubar.add_cascade(label="Правка", menu=edit_menu)

        nav_menu = self._submenu(menubar)
        nav_menu.add_command(label="Назад", accelerator="Alt+←", command=self.go_back)
        nav_menu.add_command(label="Вперёд", accelerator="Alt+→", command=self.go_forward)
        nav_menu.add_command(label="Обновить", accelerator="F5", command=self.reload_page)
        nav_menu.add_command(label="Домой", accelerator="Ctrl+Home", command=self.go_home)
        menubar.add_cascade(label="Переход", menu=nav_menu)

        view_menu = self._submenu(menubar)
        view_menu.add_command(label="Увеличить", accelerator="Ctrl++", command=lambda: self.zoom(0.1))
        view_menu.add_command(label="Уменьшить", accelerator="Ctrl+-", command=lambda: self.zoom(-0.1))
        view_menu.add_command(label="Сбросить масштаб", accelerator="Ctrl+0", command=lambda: self.zoom(0, absolute=1.0))
        view_menu.add_separator()
        self._search_bar_var = tk.BooleanVar(value=self.config.show_search_bar)
        view_menu.add_checkbutton(
            label="Строка поиска DuckDuckGo",
            variable=self._search_bar_var,
            command=self._toggle_search_bar,
        )
        menubar.add_cascade(label="Вид", menu=view_menu)

        help_menu = self._submenu(menubar)
        help_menu.add_command(label="О Sirius", command=lambda: self.navigate("sirius://about"))
        help_menu.add_command(label="README.md", command=self.open_readme)
        help_menu.add_command(label="Папка с журналом", command=self.open_log_folder)
        menubar.add_cascade(label="Справка", menu=help_menu)

        self.root.config(menu=menubar)

    def _submenu(self, parent: tk.Menu) -> tk.Menu:
        return tk.Menu(
            parent,
            tearoff=0,
            bg=COLORS["chrome_alt"],
            fg=COLORS["text"],
            activebackground=COLORS["accent"],
            activeforeground="#ffffff",
            bd=0,
        )

    def _build_chrome(self) -> None:
        chrome = tk.Frame(self.root, bg=COLORS["chrome"], padx=12, pady=10)
        chrome.pack(side="top", fill="x")
        self.chrome = chrome

        row = tk.Frame(chrome, bg=COLORS["chrome"])
        row.pack(fill="x")

        self.back_button = ToolButton(row, "←", self.go_back, self.theme, tooltip="Назад (Alt+←)")
        self.back_button.pack(side="left", padx=(0, 4))
        self.forward_button = ToolButton(row, "→", self.go_forward, self.theme, tooltip="Вперёд (Alt+→)")
        self.forward_button.pack(side="left", padx=(0, 4))
        self.reload_button = ToolButton(row, "⟳", self.reload_page, self.theme, tooltip="Обновить (F5)")
        self.reload_button.pack(side="left", padx=(0, 4))
        self.home_button = ToolButton(row, "⌂", self.go_home, self.theme, tooltip="Домой (Ctrl+Home)")
        self.home_button.pack(side="left", padx=(0, 10))

        self.address = PlaceholderEntry(
            row, self.theme, "Введите адрес или поисковый запрос", font_size=11
        )
        self.address.pack(side="left", fill="x", expand=True, ipady=5)
        self.address.bind("<Return>", self._on_address_enter)
        self.address.bind("<Escape>", lambda _e: self.text.focus_set())
        ToolTip(self.address, "Адрес страницы. Можно писать запрос — его найдёт DuckDuckGo.", self.theme)

        self.go_button = ToolButton(
            row, "Перейти", self._on_address_enter, self.theme, tooltip="Открыть адрес",
            width=8, accent=True,
        )
        self.go_button.pack(side="left", padx=(8, 0))

        # Second row: explicit DuckDuckGo search.
        self.search_row = tk.Frame(chrome, bg=COLORS["chrome"])
        self.search_row.pack(fill="x", pady=(8, 0))

        badge = tk.Frame(self.search_row, bg=COLORS["chrome_alt"], padx=8, pady=3)
        badge.pack(side="left")
        tk.Label(
            badge, text="●", bg=COLORS["chrome_alt"], fg=COLORS["cyan"],
            font=self.theme.font(8),
        ).pack(side="left")
        tk.Label(
            badge, text="DuckDuckGo", bg=COLORS["chrome_alt"], fg=COLORS["text"],
            font=self.theme.font(9, bold=True),
        ).pack(side="left", padx=(4, 0))

        self.search_entry = PlaceholderEntry(
            self.search_row, self.theme, "Поиск в DuckDuckGo…", font_size=10
        )
        self.search_entry.pack(side="left", fill="x", expand=True, padx=8, ipady=4)
        self.search_entry.bind("<Return>", self._on_search_enter)
        self.search_entry.bind("<Escape>", lambda _e: self.text.focus_set())

        self.search_button = ToolButton(
            self.search_row, "Найти", self._on_search_enter, self.theme,
            tooltip="Найти в DuckDuckGo (Ctrl+K)", width=8,
        )
        self.search_button.pack(side="left")

        if not self.config.show_search_bar:
            self.search_row.pack_forget()

    def _build_content(self) -> None:
        outer = tk.Frame(self.root, bg=COLORS["bg"], padx=10, pady=4)
        outer.pack(fill="both", expand=True, pady=(4, 8))
        self.content_frame = outer

        header = tk.Frame(outer, bg=COLORS["bg"])
        header.pack(fill="x", pady=(0, 6))
        self.page_title_label = tk.Label(
            header, text="", bg=COLORS["bg"], fg=COLORS["text"],
            font=self.theme.font(10, bold=True), anchor="w",
        )
        self.page_title_label.pack(side="left")
        self.page_host_label = tk.Label(
            header, text="", bg=COLORS["bg"], fg=COLORS["muted"],
            font=self.theme.font(9), anchor="e",
        )
        self.page_host_label.pack(side="right")

        border = tk.Frame(outer, bg=COLORS["border"], padx=1, pady=1)
        border.pack(fill="both", expand=True)

        paper = tk.Frame(border, bg=COLORS["paper"])
        paper.pack(fill="both", expand=True)

        self.text = tk.Text(
            paper,
            wrap="word",
            bd=0,
            highlightthickness=0,
            bg=COLORS["paper"],
            fg=COLORS["paper_text"],
            insertbackground=COLORS["paper_text"],
            insertwidth=1,
            selectbackground="#bfdbfe",
            selectforeground="#0f172a",
            font=self.theme.font(11),
            padx=28,
            pady=20,
            spacing1=0,
            spacing2=3,
            spacing3=0,
            cursor="arrow",
            state="disabled",
        )
        scrollbar = ttk.Scrollbar(
            paper, orient="vertical", command=self.text.yview,
            style="Sirius.Vertical.TScrollbar",
        )
        self.text.configure(yscrollcommand=scrollbar.set)
        scrollbar.pack(side="right", fill="y")
        self.text.pack(side="left", fill="both", expand=True)

        self.text.tag_configure("find", background="#fde68a", foreground="#111827")
        self.text.tag_configure("find_active", background="#f59e0b", foreground="#111827")

    def _build_status(self) -> None:
        status = tk.Frame(self.root, bg=COLORS["chrome"], padx=12, pady=6)
        status.pack(side="bottom", fill="x")

        self.status_label = tk.Label(
            status, text="Готов", bg=COLORS["chrome"], fg=COLORS["muted"],
            font=self.theme.font(9), anchor="w",
        )
        self.status_label.pack(side="left", fill="x", expand=True)

        self.progress = ttk.Progressbar(
            status, mode="indeterminate", length=110,
            style="Sirius.Horizontal.TProgressbar",
        )

        self.stats_label = tk.Label(
            status, text="", bg=COLORS["chrome"], fg=COLORS["muted"],
            font=self.theme.font(9), anchor="e",
        )
        self.stats_label.pack(side="right", padx=(12, 12))

        self.version_label = tk.Label(
            status, text=f"Sirius {VERSION}", bg=COLORS["chrome"],
            fg=COLORS["accent"], font=self.theme.font(9, bold=True),
        )
        self.version_label.pack(side="right")

        ToolTip(self.version_label, "Sirius Browser Engine 0.0.1", self.theme)

    def _build_find_bar(self) -> None:
        self.find_bar = tk.Frame(self.chrome, bg=COLORS["chrome"])
        tk.Label(
            self.find_bar, text="Найти:", bg=COLORS["chrome"], fg=COLORS["muted"],
            font=self.theme.font(9),
        ).pack(side="left", padx=(0, 6))

        self.find_entry = PlaceholderEntry(
            self.find_bar, self.theme, "Текст на странице", font_size=9
        )
        self.find_entry.pack(side="left", fill="x", expand=True, ipady=3)
        self.find_entry.bind("<Return>", lambda _e: self.find_next())
        self.find_entry.bind("<Escape>", lambda _e: self.hide_find())
        self.find_entry.bind("<KeyRelease>", lambda _e: self._highlight_find())

        self.find_count_label = tk.Label(
            self.find_bar, text="", bg=COLORS["chrome"], fg=COLORS["muted"],
            font=self.theme.font(9), width=16,
        )
        self.find_count_label.pack(side="left", padx=8)

        ToolButton(self.find_bar, "Далее", self.find_next, self.theme, width=7, font_size=9).pack(side="left")
        ToolButton(self.find_bar, "Закрыть", self.hide_find, self.theme, width=7, font_size=9).pack(side="left", padx=(4, 0))

    def _bind_shortcuts(self) -> None:
        bindings = {
            "<Control-l>": self._focus_address,
            "<Control-k>": self._focus_search,
            "<Control-f>": self.show_find,
            "<F5>": self.reload_page,
            "<Control-r>": self.reload_page,
            "<Control-t>": self.go_home,
            "<Control-Home>": self.go_home,
            "<Alt-Left>": self.go_back,
            "<Alt-Right>": self.go_forward,
            "<Control-q>": self.on_close,
            "<Control-plus>": lambda: self.zoom(0.1),
            "<Control-equal>": lambda: self.zoom(0.1),
            "<Control-minus>": lambda: self.zoom(-0.1),
            "<Control-0>": lambda: self.zoom(0, absolute=1.0),
            "<Control-c>": self._copy_or_default,
            "<Control-a>": self._select_or_default,
            "<Escape>": self._escape,
        }
        for sequence, callback in bindings.items():
            self.root.bind(sequence, callback, add="+")

        self.text.bind("<Control-MouseWheel>", self._on_zoom_wheel, add="+")

    # ------------------------------------------------------------------ chrome UI

    def _focus_address(self, _event: tk.Event | None = None) -> str:
        self.address.focus_set()
        self.address.select_range(0, "end")
        return "break"

    def _focus_search(self, _event: tk.Event | None = None) -> str:
        if not self.config.show_search_bar:
            self._toggle_search_bar()
        self.search_entry.focus_set()
        self.search_entry.select_range(0, "end")
        return "break"

    def _toggle_search_bar(self) -> None:
        self.config.show_search_bar = bool(self._search_bar_var.get())
        if self.config.show_search_bar:
            self.search_row.pack(fill="x", pady=(8, 0), after=self.chrome.winfo_children()[0])
        else:
            self.search_row.pack_forget()

    def _escape(self, _event: tk.Event | None = None) -> str | None:
        if self.find_bar.winfo_ismapped():
            self.hide_find()
            return "break"
        self.text.focus_set()
        return "break"

    def _set_status(self, text: str, *, error: bool = False) -> None:
        self.status_label.configure(
            text=text,
            fg=COLORS["danger"] if error else COLORS["muted"],
        )

    def _set_hover_status(self, text: str) -> None:
        if self._loading:
            return
        if text:
            if len(text) > 110:
                text = text[:109] + "…"
            self.status_label.configure(text=text, fg=COLORS["muted"])
        else:
            self.status_label.configure(text="Готов", fg=COLORS["muted"])

    def _set_loading(self, loading: bool) -> None:
        self._loading = loading
        if loading:
            if not self.progress.winfo_ismapped():
                self.progress.pack(side="right", padx=(6, 6))
            self.progress.start(14)
        else:
            self.progress.stop()
            if self.progress.winfo_ismapped():
                self.progress.pack_forget()

    def _set_nav_state(self, back: bool, forward: bool) -> None:
        for button, enabled in ((self.back_button, back), (self.forward_button, forward)):
            button.configure(state="normal" if enabled else "disabled")
            button.configure(fg=COLORS["text"] if enabled else COLORS["muted"])

    def _insert_welcome(self) -> None:
        self.text.insert(
            "1.0",
            "\n\n  Sirius 0.0.1\n\n  Загрузка движка…\n",
            ("welcome",),
        )
        self.text.tag_configure(
            "welcome",
            font=self.theme.font(14),
            foreground="#4b5563",
            justify="left",
            lmargin1=20,
            lmargin2=20,
            spacing1=10,
            spacing3=4,
        )

    # ------------------------------------------------------------------- navigation

    def _on_address_enter(self, _event: tk.Event | None = None) -> str:
        target = self.address.get().strip()
        if not target:
            return "break"
        self.navigate(target)
        return "break"

    def _on_search_enter(self, _event: tk.Event | None = None) -> str:
        query = self.search_entry.get().strip()
        if not query:
            self.search_entry.focus_set()
            return "break"
        self.search(query)
        return "break"

    def navigate(self, target: str) -> None:
        target = (target or "").strip()
        if not target:
            return
        self._request("navigate", target, label="Загрузка…")

    def search(self, query: str) -> None:
        query = (query or "").strip()
        if not query:
            return
        self._request("search", query, label=f"Поиск в DuckDuckGo: {query}")

    def go_back(self) -> None:
        if str(self.back_button["state"]) != "disabled":
            self._request("back", label="Назад…")

    def go_forward(self) -> None:
        if str(self.forward_button["state"]) != "disabled":
            self._request("forward", label="Вперёд…")

    def reload_page(self) -> None:
        self._request("reload", label="Обновление…")

    def go_home(self) -> None:
        self._request("home", label="Домашняя страница…")

    def _request(self, method: str, *args: Any, label: str = "Загрузка…") -> None:
        if self._closing:
            return

        if not self.client.available:
            self._show_engine_failure(
                "Не найден Sirius.Host.exe.\n\n"
                "Соберите движок командой:\n"
                "dotnet build src\\Sirius.Host -c Release"
            )
            return

        self._active_seq += 1
        sequence = self._active_seq
        self._set_loading(True)
        self._set_status(label)
        self.logger.info("UI request #%d: %s %s", sequence, method, " ".join(map(str, args)))

        thread = threading.Thread(
            target=self._worker,
            args=(sequence, method, args),
            name=f"sirius-request-{sequence}",
            daemon=True,
        )
        thread.start()
        if self._poll_id is None:
            self._poll_id = self.root.after(60, self._poll_queue)

    def _worker(self, sequence: int, method: str, args: tuple[Any, ...]) -> None:
        try:
            reply = getattr(self.client, method)(*args)
            self._queue.put((sequence, reply, None))
        except Exception as exc:  # noqa: BLE001 - forwarded to the UI thread
            self.logger.exception("Запрос движка %s завершился ошибкой", method)
            self._queue.put((sequence, None, exc))

    def _poll_queue(self) -> None:
        self._poll_id = None
        if self._closing:
            return

        if self._active_seq == 0:
            return

        while True:
            try:
                sequence, reply, error = self._queue.get_nowait()
            except queue.Empty:
                break

            if sequence != self._active_seq:
                self.logger.debug("Пропущен устаревший ответ #%d", sequence)
                continue

            self._set_loading(False)

            if error is not None:
                self._handle_error(error)
            elif reply is not None:
                self._handle_reply(reply)

        if self._loading:
            self._poll_id = self.root.after(70, self._poll_queue)

    def _handle_reply(self, reply: dict[str, Any]) -> None:
        result = reply.get("result") or {}
        page = result.get("page")

        if not page:
            self._set_status(result.get("error") or "Пустая страница", error=True)
            return

        self._page = page
        self.renderer.render(page)
        self._last_url = page.get("url") or self._last_url
        self.address.set_text(self._last_url)
        self._update_header(page)
        self._set_nav_state(bool(result.get("canGoBack")), bool(result.get("canGoForward")))

        title = page.get("title") or urlsplit(self._last_url).netloc or self._last_url
        self.root.title(f"{title} — {APP_NAME} {VERSION}")

        blocks = len(page.get("blocks") or [])
        words = int(page.get("wordCount") or 0)
        self.stats_label.configure(
            text=f"{blocks} {plural(blocks, 'блок', 'блока', 'блоков')} · "
                 f"{words} {plural(words, 'слово', 'слова', 'слов')} · "
                 f"{format_bytes(page.get('bytes'))} · {int(page.get('elapsedMs') or 0)} мс"
        )

        error = result.get("error")
        if error:
            self._set_status(str(error), error=True)
        else:
            self._set_status("Готов")

        self.logger.info(
            "Страница: %s | %s | статус %s | %s мс",
            self._last_url, title, result.get("statusCode"), page.get("elapsedMs"),
        )

        if self.find_bar.winfo_ismapped() and self.find_entry.get().strip():
            self._highlight_find()

    def _handle_error(self, error: Exception) -> None:
        message = str(error) or error.__class__.__name__
        self._set_status(message, error=True)
        self.logger.error("Ошибка движка: %s", message)

        page = {
            "title": "Ошибка движка",
            "url": self._last_url,
            "blocks": [
                {
                    "kind": "heading",
                    "level": 1,
                    "spaceAfter": 12,
                    "spans": [{"text": "Движок не отвечает"}],
                },
                {
                    "kind": "paragraph",
                    "spans": [{"text": message, "color": "#b91c1c"}],
                },
                {
                    "kind": "paragraph",
                    "spans": [
                        {
                            "text": "Проверьте, что Sirius.Host.exe лежит рядом с Sirius.exe, "
                                    "или соберите проект заново.",
                        }
                    ],
                },
                {
                    "kind": "notice",
                    "spans": [
                        {
                            "text": "dotnet build src\\Sirius.Host\\Sirius.Host.csproj -c Release",
                            "code": True,
                        }
                    ],
                },
            ],
        }
        self.renderer.render(page)

    def _show_engine_failure(self, message: str) -> None:
        self.logger.error(message)
        self._set_status("Движок недоступен", error=True)
        self._handle_error(EngineError(message))
        messagebox.showerror("Sirius", message, parent=self.root)

    def _update_header(self, page: dict[str, Any]) -> None:
        title = str(page.get("title") or "").strip()
        self.page_title_label.configure(text=title[:110])

        url = str(page.get("url") or "")
        host = urlsplit(url).netloc if url.startswith(("http://", "https://")) else url
        if page.get("siteName"):
            host = f"{page['siteName']} · {host}"
        if page.get("language"):
            host += f" · {page['language']}"
        self.page_host_label.configure(text=host[:90])

    # ---------------------------------------------------------------------- find

    def show_find(self, _event: tk.Event | None = None) -> str:
        if not self.find_bar.winfo_ismapped():
            self.find_bar.pack(fill="x", pady=(8, 0))
        self.find_entry.focus_set()
        self.find_entry.select_range(0, "end")
        self._highlight_find()
        return "break"

    def hide_find(self) -> None:
        self.find_bar.pack_forget()
        self.text.tag_remove("find", "1.0", "end")
        self.text.tag_remove("find_active", "1.0", "end")
        self.text.focus_set()

    def _highlight_find(self) -> None:
        term = self.find_entry.get().strip()
        self.text.tag_remove("find", "1.0", "end")
        self.text.tag_remove("find_active", "1.0", "end")

        if len(term) < 2:
            self.find_count_label.configure(text="")
            self._find_matches = 0
            return

        count = 0
        start = "1.0"
        while count < 500:
            position = self.text.search(term, start, stopindex="end", nocase=True)
            if not position:
                break
            end = f"{position}+{len(term)}c"
            self.text.tag_add("find", position, end)
            start = end
            count += 1

        self._find_matches = count
        self.find_count_label.configure(text=f"{count} совпадений" if count else "не найдено")

    def find_next(self) -> None:
        term = self.find_entry.get().strip()
        if len(term) < 2:
            return

        self.text.tag_remove("find_active", "1.0", "end")
        start = self.text.index("insert")

        position = self.text.search(term, start, stopindex="end", nocase=True)
        if not position:
            position = self.text.search(term, "1.0", stopindex="end", nocase=True)
        if not position:
            self.find_count_label.configure(text="не найдено")
            return

        end = f"{position}+{len(term)}c"
        self.text.tag_add("find_active", position, end)
        self.text.mark_set("insert", end)
        self.text.see(position)

    # -------------------------------------------------------------------- clipboard

    def copy_selection(self) -> None:
        try:
            selected = self.text.get("sel.first", "sel.last")
        except tk.TclError:
            return
        if selected:
            self.root.clipboard_clear()
            self.root.clipboard_append(selected)
            self._set_status("Скопировано в буфер обмена")

    def _copy_or_default(self, _event: tk.Event) -> str | None:
        if isinstance(self.root.focus_get(), tk.Entry):
            return None
        self.copy_selection()
        return "break"

    def select_all(self) -> None:
        if isinstance(self.root.focus_get(), tk.Entry):
            self.root.focus_get().select_range(0, "end")
            return
        self.text.tag_add("sel", "1.0", "end-1c")
        self.text.mark_set("insert", "1.0")

    def _select_or_default(self, _event: tk.Event) -> str | None:
        if isinstance(self.root.focus_get(), tk.Entry):
            return None
        self.select_all()
        return "break"

    # ------------------------------------------------------------------------ zoom

    def zoom(self, delta: float, absolute: float | None = None) -> None:
        scale = absolute if absolute is not None else self.config.font_scale + delta
        self.config.font_scale = round(max(0.7, min(2.0, scale)), 2)
        self.renderer.set_scale(self.config.font_scale)
        self._set_status(f"Масштаб: {int(self.config.font_scale * 100)}%")

    def _on_zoom_wheel(self, event: tk.Event) -> str:
        self.zoom(0.1 if event.delta > 0 else -0.1)
        return "break"

    # ---------------------------------------------------------------------- misc

    def open_readme(self) -> None:
        path = resource_path("README.md")
        if path.exists():
            try:
                os.startfile(str(path))  # type: ignore[attr-defined]
                return
            except OSError:
                pass
        messagebox.showinfo("Sirius", "README.md не найден рядом с приложением.", parent=self.root)

    def open_log_folder(self) -> None:
        try:
            os.startfile(str(self.config_path.parent))  # type: ignore[attr-defined]
        except OSError:
            pass

    # ---------------------------------------------------------------------- close

    def on_close(self) -> None:
        if self._closing:
            return
        self._closing = True

        try:
            self.config.width = max(880, self.root.winfo_width())
            self.config.height = max(560, self.root.winfo_height())
            self.config.x = self.root.winfo_x()
            self.config.y = self.root.winfo_y()
            self.config.last_url = self._last_url
            save_config(self.config_path, self.config)
        except tk.TclError:
            pass

        try:
            self.client.close()
        except Exception:  # noqa: BLE001
            self.logger.exception("Не удалось корректно остановить движок")

        self.root.destroy()


__all__ = ["BrowserWindow"]
