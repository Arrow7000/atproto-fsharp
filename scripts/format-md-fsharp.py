#!/usr/bin/env python3
"""Format F# code blocks inside markdown files using Fantomas."""

import re
import subprocess
import sys
import tempfile
from pathlib import Path


def format_fsharp(code: str, project_root: Path) -> str | None:
    """Run Fantomas on a snippet, return formatted code or None on failure."""
    with tempfile.NamedTemporaryFile(
        suffix=".fsx", mode="w", delete=False, dir=project_root
    ) as f:
        f.write(code)
        tmp = Path(f.name)

    try:
        result = subprocess.run(
            ["dotnet", "fantomas", str(tmp)],
            capture_output=True,
            text=True,
            cwd=project_root,
        )
        if result.returncode != 0:
            return None
        return tmp.read_text()
    finally:
        tmp.unlink(missing_ok=True)


def process_markdown(path: Path, project_root: Path, check_only: bool) -> bool:
    """Process one markdown file. Returns True if changes were made/needed."""
    text = path.read_text()
    parts = re.split(r"(```fsharp\n)(.*?)(```)", text, flags=re.DOTALL)

    if len(parts) == 1:
        return False

    changed = False
    # parts: [before, opener, code, closer, between, opener, code, closer, ...]
    i = 1
    while i < len(parts):
        opener, code, closer = parts[i], parts[i + 1], parts[i + 2]
        formatted = format_fsharp(code, project_root)
        if formatted is not None:
            # Fantomas may add/remove trailing newline; normalize to match original
            if code.endswith("\n") and not formatted.endswith("\n"):
                formatted += "\n"
            if not code.endswith("\n") and formatted.endswith("\n"):
                formatted = formatted.rstrip("\n")
            if formatted != code:
                parts[i + 1] = formatted
                changed = True
        i += 4

    if changed:
        if check_only:
            print(f"  needs formatting: {path}")
        else:
            path.write_text("".join(parts))
            print(f"  formatted: {path}")
    return changed


def main():
    check_only = "--check" in sys.argv
    project_root = Path(__file__).resolve().parent.parent

    md_files = sorted(
        list(project_root.glob("docs/**/*.md")) + list(project_root.glob("*.md"))
    )

    total_changed = 0
    total_files = len(md_files)

    for path in md_files:
        if process_markdown(path, project_root, check_only):
            total_changed += 1

    verb = "need formatting" if check_only else "formatted"
    print(f"\n  {total_changed}/{total_files} files {verb}")

    if check_only and total_changed > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
