"""Visual theme for the Sirius chrome (dark) and reading surface (light)."""

from __future__ import annotations

import tkinter as tk
from tkinter import font as tkfont
from tkinter import ttk

COLORS = {
    "bg": "#0b0e14",
    "chrome": "#11151f",
    "chrome_alt": "#161b26",
    "chrome_hover": "#1e2635",
    "border": "#232b3a",
    "text": "#e5e7eb",
    "muted": "#8b95a7",
    "accent": "#6d5efc",
    "accent_hover": "#8579ff",
    "accent_soft": "#2a2a55",
    "cyan": "#22d3ee",
    "paper": "#ffffff",
    "paper_text": "#20242e",
    "danger": "#f87171",
    "success": "#34d399",
}

PREFERRED_SANS = (
    "Segoe UI Variable Text",
    "Segoe UI",
    "Inter",
    "Helvetica Neue",
    "Arial",
)
PREFERRED_MONO = ("Cascadia Mono", "Consolas", "JetBrains Mono", "Courier New")


class Theme:
    def __init__(self, root: tk.Misc) -> None:
        families = {name.lower(): name for name in tkfont.families(root)}
        self.sans = self._pick(families, PREFERRED_SANS)
        self.mono = self._pick(families, PREFERRED_MONO)
        self._root = root
        self._style = ttk.Style(root)
        try:
            self._style.theme_use("clam")
        except tk.TclError:
            pass
        self._configure_ttk()

    @staticmethod
    def _pick(families: dict[str, str], candidates: tuple[str, ...]) -> str:
        for candidate in candidates:
            if candidate.lower() in families:
                return families[candidate.lower()]
        return candidates[-1]

    def font(
        self,
        size: int,
        *,
        bold: bool = False,
        italic: bool = False,
        mono: bool = False,
        underline: bool = False,
        overstrike: bool = False,
    ) -> tuple[str, int, str] | tuple[str, int]:
        family = self.mono if mono else self.sans
        styles = []
        if bold:
            styles.append("bold")
        if italic:
            styles.append("italic")
        if underline:
            styles.append("underline")
        if overstrike:
            styles.append("overstrike")
        if styles:
            return family, size, " ".join(styles)
        return family, size

    def _configure_ttk(self) -> None:
        style = self._style
        style.configure(
            "Sirius.Vertical.TScrollbar",
            background=COLORS["chrome_alt"],
            troughcolor=COLORS["paper"],
            bordercolor=COLORS["paper"],
            arrowcolor=COLORS["muted"],
            darkcolor=COLORS["chrome_alt"],
            lightcolor=COLORS["chrome_alt"],
            gripcount=0,
            width=12,
        )
        style.map(
            "Sirius.Vertical.TScrollbar",
            background=[("active", COLORS["border"])],
            arrowcolor=[("active", COLORS["text"])],
        )
        style.configure(
            "Sirius.Horizontal.TProgressbar",
            troughcolor=COLORS["chrome_alt"],
            background=COLORS["accent"],
            bordercolor=COLORS["chrome_alt"],
            lightcolor=COLORS["accent"],
            darkcolor=COLORS["accent"],
            thickness=3,
        )


class ToolTip:
    """Small dark tooltip used by toolbar buttons."""

    def __init__(self, widget: tk.Widget, text: str, theme: Theme) -> None:
        self._widget = widget
        self._text = text
        self._theme = theme
        self._window: tk.Toplevel | None = None
        self._after_id: str | None = None
        widget.bind("<Enter>", self._schedule, add="+")
        widget.bind("<Leave>", self._hide, add="+")
        widget.bind("<ButtonPress>", self._hide, add="+")

    def _schedule(self, _event: tk.Event) -> None:
        self._cancel()
        self._after_id = self._widget.after(550, self._show)

    def _cancel(self) -> None:
        if self._after_id:
            try:
                self._widget.after_cancel(self._after_id)
            except tk.TclError:
                pass
            self._after_id = None

    def _show(self) -> None:
        if self._window is not None or not self._text:
            return

        x = self._widget.winfo_rootx() + self._widget.winfo_width() // 2
        y = self._widget.winfo_rooty() + self._widget.winfo_height() + 8

        window = tk.Toplevel(self._widget)
        window.wm_overrideredirect(True)
        window.configure(bg=COLORS["border"])
        label = tk.Label(
            window,
            text=self._text,
            bg=COLORS["chrome_alt"],
            fg=COLORS["text"],
            font=self._theme.font(9),
            padx=8,
            pady=4,
        )
        label.pack(padx=1, pady=1)
        window.update_idletasks()
        window.wm_geometry(f"+{x - window.winfo_width() // 2}+{y}")
        self._window = window

    def _hide(self, _event: tk.Event | None = None) -> None:
        self._cancel()
        if self._window is not None:
            self._window.destroy()
            self._window = None


class ToolButton(tk.Button):
    """Flat chrome button with hover feedback."""

    def __init__(
        self,
        parent: tk.Misc,
        text: str,
        command,
        theme: Theme,
        *,
        tooltip: str | None = None,
        width: int = 3,
        font_size: int = 11,
        accent: bool = False,
    ) -> None:
        self._theme = theme
        self._accent = accent
        super().__init__(
            parent,
            text=text,
            command=command,
            width=width,
            font=theme.font(font_size, bold=accent),
            bg=COLORS["accent"] if accent else COLORS["chrome_alt"],
            fg="#ffffff" if accent else COLORS["text"],
            activebackground=COLORS["accent_hover"] if accent else COLORS["chrome_hover"],
            activeforeground="#ffffff",
            relief="flat",
            bd=0,
            highlightthickness=0,
            padx=8,
            pady=6,
            cursor="hand2",
            takefocus=False,
            disabledforeground=COLORS["muted"],
        )
        self.bind("<Enter>", self._on_enter, add="+")
        self.bind("<Leave>", self._on_leave, add="+")
        if tooltip:
            ToolTip(self, tooltip, theme)

    def _on_enter(self, _event: tk.Event) -> None:
        self.configure(bg=COLORS["accent_hover"] if self._accent else COLORS["chrome_hover"])

    def _on_leave(self, _event: tk.Event) -> None:
        self.configure(bg=COLORS["accent"] if self._accent else COLORS["chrome_alt"])


class PlaceholderEntry(tk.Entry):
    """Entry with placeholder text and focus ring."""

    def __init__(
        self,
        parent: tk.Misc,
        theme: Theme,
        placeholder: str = "",
        *,
        font_size: int = 11,
        background: str | None = None,
    ) -> None:
        self._placeholder = placeholder
        self._showing_placeholder = False
        self._normal_bg = background or COLORS["chrome_alt"]

        super().__init__(
            parent,
            font=theme.font(font_size),
            bg=self._normal_bg,
            fg=COLORS["text"],
            insertbackground=COLORS["cyan"],
            relief="flat",
            bd=0,
            highlightthickness=1,
            highlightbackground=COLORS["border"],
            highlightcolor=COLORS["accent"],
            selectbackground=COLORS["accent_soft"],
            selectforeground="#ffffff",
        )

        self.bind("<FocusIn>", self._on_focus_in, add="+")
        self.bind("<FocusOut>", self._on_focus_out, add="+")
        self._apply_placeholder()

    def _apply_placeholder(self) -> None:
        if self._placeholder and not super().get():
            self._showing_placeholder = True
            self.configure(fg=COLORS["muted"])
            super().insert(0, self._placeholder)

    def _on_focus_in(self, _event: tk.Event) -> None:
        if self._showing_placeholder:
            super().delete(0, "end")
            self.configure(fg=COLORS["text"])
            self._showing_placeholder = False

    def _on_focus_out(self, _event: tk.Event) -> None:
        if not super().get():
            self._apply_placeholder()

    def get(self) -> str:  # type: ignore[override]
        return "" if self._showing_placeholder else super().get()

    def set_text(self, text: str) -> None:
        self._showing_placeholder = False
        self.configure(fg=COLORS["text"])
        super().delete(0, "end")
        if text:
            super().insert(0, text)
        elif self.focus_get() is not self:
            self._apply_placeholder()

    def clear(self) -> None:
        self.set_text("")


__all__ = [
    "COLORS",
    "PlaceholderEntry",
    "Theme",
    "ToolButton",
    "ToolTip",
]
