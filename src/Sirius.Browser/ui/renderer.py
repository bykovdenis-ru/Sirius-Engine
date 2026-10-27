"""Paints engine blocks into a Tkinter Text widget."""

from __future__ import annotations

import tkinter as tk
import webbrowser
from typing import Any, Callable

from ui.theme import COLORS, Theme

HEADING_SIZES = {1: 27, 2: 21, 3: 17, 4: 15, 5: 13, 6: 12}


class PageRenderer:
    def __init__(
        self,
        text: tk.Text,
        theme: Theme,
        on_link: Callable[[str], None],
        on_status: Callable[[str], None],
        *,
        font_scale: float = 1.0,
    ) -> None:
        self.text = text
        self.theme = theme
        self.on_link = on_link
        self.on_status = on_status
        self.font_scale = font_scale

        self._font_tags: dict[tuple, str] = {}
        self._color_tags: dict[str, str] = {}
        self._background_tags: dict[str, str] = {}
        self._block_tags: dict[tuple, str] = {}
        self._link_tags: dict[str, str] = {}
        self._link_urls: dict[str, str] = {}
        self._hr_frames: list[tk.Frame] = []
        self._current_page: dict[str, Any] | None = None
        self._context_menu = self._build_context_menu()
        self._context_url: str | None = None

        text.bind("<Motion>", self._on_motion, add="+")
        text.bind("<Leave>", lambda _e: self.on_status(""), add="+")
        text.bind("<Configure>", self._resize_hrs, add="+")

    # ------------------------------------------------------------------ public API

    def render(self, page: dict[str, Any]) -> None:
        self._current_page = page
        self._hr_frames.clear()
        self._link_tags.clear()
        self._link_urls.clear()

        text = self.text
        text.configure(state="normal")
        text.delete("1.0", "end")
        text.tag_remove("find", "1.0", "end")
        text.tag_remove("find_active", "1.0", "end")

        blocks = page.get("blocks") or []
        for index, block in enumerate(blocks):
            self._render_block(block, index)

        text.configure(state="disabled")
        text.mark_set("insert", "1.0")
        text.yview_moveto(0.0)
        text.update_idletasks()
        self._resize_hrs(None)

    def set_scale(self, scale: float) -> None:
        self.font_scale = max(0.7, min(2.0, scale))
        if self._current_page is not None:
            self.render(self._current_page)

    def rerender(self) -> None:
        if self._current_page is not None:
            self.render(self._current_page)

    # --------------------------------------------------------------------- blocks

    def _render_block(self, block: dict[str, Any], index: int) -> None:
        kind = str(block.get("kind") or "paragraph")
        level = int(block.get("level") or 0)
        indent = max(0, min(6, int(block.get("indent") or 0)))
        spans = block.get("spans") or []
        before = max(0, min(40, int(block.get("spaceBefore") or 0)))
        after = max(0, min(30, int(block.get("spaceAfter") or 0)))
        align = block.get("align") or "left"
        marker = block.get("marker") or ""

        if kind == "hr":
            self._insert_divider(index)
            return

        if not spans:
            return

        block_tag = self._block_tag(kind, level, indent, align, before, after)
        base_family, base_size, base_bold, base_italic = self._base_font(kind, level)

        if kind == "quote":
            self._insert_text("▎ ", block_tag, self._font_tag(base_family, base_size, True, False), self._color_tag("#c7cdd8"))
            self._insert_text(" ", block_tag)

        if marker:
            self._insert_text(marker + " ", block_tag, self._font_tag(base_family, base_size, True, False), self._color_tag(COLORS["accent"]))

        for span in spans:
            self._render_span(span, block_tag, base_family, base_size, base_bold, base_italic)

        self._insert_text("\n", block_tag)

    def _render_span(
        self,
        span: dict[str, Any],
        block_tag: str,
        base_family: str,
        base_size: int,
        base_bold: bool,
        base_italic: bool,
    ) -> None:
        text = str(span.get("text") or "")
        if text == "":
            return

        bold = base_bold or bool(span.get("bold"))
        italic = base_italic or bool(span.get("italic"))
        mono = bool(span.get("code"))
        underline = bool(span.get("underline")) or bool(span.get("link"))
        strike = bool(span.get("strike"))

        family = self.theme.mono if mono else base_family
        size = max(8, base_size - 1) if mono else base_size

        tags: list[str] = [block_tag, self._font_tag(family, size, bold, italic, underline, strike)]

        color = span.get("color")
        if color:
            tags.append(self._color_tag(str(color)))
        if span.get("mark"):
            tags.append(self._background_tag("#ffe58a", "mark"))
        background = span.get("background")
        if background:
            tags.append(self._background_tag(str(background), "bg"))

        link = span.get("link")
        if link:
            tags.append(self._link_tag(str(link)))

        self.text.insert("end", text, tuple(tags))

    def _insert_text(
        self,
        content: str,
        *tags: str,
    ) -> None:
        self.text.insert("end", content, tuple(t for t in tags if t) or None)

    def _insert_divider(self, index: int) -> None:
        self._insert_text("\n")
        frame = tk.Frame(self.text, bg="#e6e9ef", height=1, bd=0, highlightthickness=0)
        self.text.window_create("end", window=frame, padx=12, pady=0)
        self._hr_frames.append(frame)
        self._insert_text("\n")

    def _resize_hrs(self, _event: tk.Event | None) -> None:
        width = max(120, self.text.winfo_width() - 48)
        for frame in self._hr_frames:
            try:
                frame.configure(width=width, height=1)
            except tk.TclError:
                pass

    # ---------------------------------------------------------------------- fonts

    def _block_tag(
        self,
        kind: str,
        level: int,
        indent: int,
        align: str,
        before: int,
        after: int,
    ) -> str:
        key = (kind, level, indent, align, before, after)
        tag = self._block_tags.get(key)
        if tag:
            return tag

        left = 8 + indent * 26
        right = 10
        hanging = left

        if kind == "list_item":
            left = 8 + indent * 26
            hanging = left + 24
        elif kind == "quote":
            left = 12 + indent * 26
            hanging = left
            right = 18
        elif kind == "pre":
            left = 12 + indent * 26
            hanging = left
            right = 14
        elif kind == "heading":
            left = 6 + indent * 26
            hanging = left

        justify = align if align in ("left", "center", "right") else "left"

        tag = f"block_{len(self._block_tags)}"
        self.text.tag_configure(
            tag,
            lmargin1=left,
            lmargin2=hanging,
            rmargin=right,
            spacing1=before,
            spacing3=after,
            justify=justify,
        )
        self._block_tags[key] = tag
        return tag

    def _scaled(self, size: int) -> int:
        return max(8, int(round(size * self.font_scale)))

    def _base_font(self, kind: str, level: int) -> tuple[str, int, bool, bool]:
        family = self.theme.sans
        bold = False
        italic = False

        if kind == "heading":
            size = self._scaled(HEADING_SIZES.get(level, 14))
            bold = True
        elif kind == "pre":
            family = self.theme.mono
            size = self._scaled(10)
        elif kind == "table_row":
            size = self._scaled(10)
        elif kind == "quote":
            size = self._scaled(11)
            italic = True
        elif kind == "notice":
            size = self._scaled(10)
            italic = True
        else:
            size = self._scaled(11)

        return family, size, bold, italic

    def _font_tag(
        self,
        family: str,
        size: int,
        bold: bool,
        italic: bool,
        underline: bool = False,
        strike: bool = False,
    ) -> str:
        key = (family, size, bold, italic, underline, strike)
        tag = self._font_tags.get(key)
        if tag:
            return tag

        tag = f"font_{len(self._font_tags)}"
        self.text.tag_configure(tag, font=self.theme.font(size, bold=bold, italic=italic,
                                                         underline=underline, overstrike=strike))
        self._font_tags[key] = tag
        return tag

    def _color_tag(self, color: str) -> str:
        tag = self._color_tags.get(color)
        if tag:
            return tag

        normalized = color if color.startswith("#") and len(color) == 7 else None
        if normalized is None:
            normalized = self._css_color(color) or COLORS["paper_text"]

        tag = f"color_{len(self._color_tags)}"
        self.text.tag_configure(tag, foreground=normalized)
        self._color_tags[color] = tag
        return tag

    def _background_tag(self, color: str, prefix: str) -> str:
        key = f"{prefix}:{color}"
        tag = self._background_tags.get(key)
        if tag:
            return tag

        normalized = color if color.startswith("#") and len(color) == 7 else None
        if normalized is None:
            normalized = self._css_color(color) or "#eef1f6"

        tag = f"{prefix}_{len(self._background_tags)}"
        self.text.tag_configure(tag, background=normalized)
        self._background_tags[key] = tag
        return tag

    @staticmethod
    def _css_color(value: str) -> str | None:
        # The engine already sends hex, but tolerate plain names just in case.
        basic = {
            "black": "#000000", "white": "#ffffff", "red": "#ff0000", "green": "#008000",
            "blue": "#0000ff", "gray": "#808080", "grey": "#808080", "silver": "#c0c0c0",
            "maroon": "#800000", "navy": "#000080", "teal": "#008080", "orange": "#ffa500",
        }
        return basic.get(value.lower())

    # ---------------------------------------------------------------------- links

    def _link_tag(self, url: str) -> str:
        tag = self._link_tags.get(url)
        if tag:
            return tag

        tag = f"link_{len(self._link_tags)}"
        self._link_tags[url] = tag
        self._link_urls[tag] = url
        self.text.tag_bind(tag, "<Button-1>", lambda _event, u=url: self._open_link(u))
        self.text.tag_bind(tag, "<Button-3>", self._on_link_context)
        return tag

    def _open_link(self, url: str) -> None:
        if url.startswith(("http://", "https://", "sirius://", "about:")):
            self.on_link(url)
        elif url.startswith("mailto:"):
            try:
                webbrowser.open(url)
            except OSError:
                pass
        else:
            self.on_link(url)

    def _link_at_current(self) -> str | None:
        for name in self.text.tag_names("current"):
            if name in self._link_urls:
                return self._link_urls[name]
        return None

    def _on_motion(self, _event: tk.Event) -> None:
        url = self._link_at_current()
        if url:
            self.text.configure(cursor="hand2")
            self.on_status(f"Ссылка: {url}")
        else:
            self.text.configure(cursor="arrow")
            self.on_status("")

    # ---------------------------------------------------------------- context menu

    def _build_context_menu(self) -> tk.Menu:
        menu = tk.Menu(self.text, tearoff=0, bg=COLORS["chrome_alt"], fg=COLORS["text"],
                       activebackground=COLORS["accent"], activeforeground="#ffffff",
                       bd=0, font=self.theme.font(9))
        menu.add_command(label="Открыть ссылку", command=self._open_context_link)
        menu.add_command(label="Копировать адрес ссылки", command=self._copy_context_link)
        menu.add_separator()
        menu.add_command(label="Открыть в системном браузере", command=self._open_context_external)
        return menu

    def _on_link_context(self, event: tk.Event) -> str:
        url = self._link_at_current()
        if not url:
            return "break"
        self._context_url = url
        try:
            self._context_menu.tk_popup(event.x_root, event.y_root)
        finally:
            self._context_menu.grab_release()
        return "break"

    def _open_context_link(self) -> None:
        if self._context_url:
            self._open_link(self._context_url)

    def _copy_context_link(self) -> None:
        if self._context_url:
            self.text.clipboard_clear()
            self.text.clipboard_append(self._context_url)

    def _open_context_external(self) -> None:
        if self._context_url and self._context_url.startswith(("http://", "https://")):
            try:
                webbrowser.open(self._context_url)
            except OSError:
                pass


__all__ = ["PageRenderer"]
