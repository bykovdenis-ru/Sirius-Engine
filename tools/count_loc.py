"""Counts non-blank, non-comment source lines and reports the C#/Python split."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXCLUDED = {"bin", "obj", "__pycache__", "dist", "build", ".venv", "venv"}


def count(base: Path, suffix: str) -> tuple[int, list[tuple[str, int]]]:
    total = 0
    files: list[tuple[str, int]] = []

    for path in base.rglob("*" + suffix):
        if any(part in EXCLUDED for part in path.parts):
            continue
        lines = 0
        for raw in path.read_text(encoding="utf-8", errors="ignore").splitlines():
            stripped = raw.strip()
            if not stripped or stripped.startswith("//") or stripped.startswith("#"):
                continue
            lines += 1
        if lines:
            total += lines
            files.append((str(path.relative_to(ROOT)), lines))

    return total, sorted(files, key=lambda item: -item[1])


def main() -> None:
    cs, cs_files = count(ROOT / "src", ".cs")
    py, py_files = count(ROOT / "src", ".py")
    total = cs + py or 1

    print(f"C#      {cs:6} lines  ({cs / total * 100:5.1f}%)")
    print(f"Python  {py:6} lines  ({py / total * 100:5.1f}%)")
    print(f"Total   {total:6} lines")

    print("\nC# files:")
    for name, lines in cs_files:
        print(f"  {lines:5}  {name}")

    print("\nPython files:")
    for name, lines in py_files:
        print(f"  {lines:5}  {name}")


if __name__ == "__main__":
    main()
