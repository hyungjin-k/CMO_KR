#!/usr/bin/env python3
"""Extract English prose paragraphs from scenario HTML for the Japanese prose dictionary.

Reads the game's scenario HTML files and the plain <ScenDescription> of .scen files (read-only) and splits them into the same
units the Japanese engine sees inside WebView2: a run of inline content between
block boundaries or <br>, whitespace collapsed (see `normalize` and
src/ja/JapaneseProseTranslation.cs).  Nothing is written inside the game
directory or dict/.

    # worklist for translators: {"paragraph": ["file", ...]} of paragraphs not yet in ja_prose.json
    python scripts/extract_ja_prose.py worklist "<game>/Scenarios/Tutorials/Air Warfare Tutorials/Flight Tutorial" \
        --out build/ja-prose/flight_tutorial.json [--glob "FlightTutorial_1_*.html"]
    # coverage report: how many paragraphs of those files ja_prose.json translates
    python scripts/extract_ja_prose.py coverage "<dir>" [--glob ...]
"""
from __future__ import annotations

import argparse
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PROSE = ROOT / "dict" / "src" / "ja_prose.json"
# Must match BLOCK_TAGS in src/ja/JapaneseProseTranslation.cs (the in-page script).
BLOCK = {"address", "article", "aside", "blockquote", "body", "caption", "center", "dd", "div", "dl", "dt",
         "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header",
         "hr", "html", "li", "main", "nav", "ol", "p", "pre", "section", "table", "tbody", "td", "tfoot",
         "th", "thead", "tr", "ul"}
SKIP = {"script", "style", "head", "title", "textarea", "select", "button", "noscript", "template"}
WHITESPACE = re.compile(r"\s+")
LATIN_WORD = re.compile(r"[A-Za-z]{2,}")


def normalize(text: str) -> str:
    """Collapse every whitespace run (incl. NBSP and line breaks) to one space; trim."""
    return WHITESPACE.sub(" ", text).strip()


class _Paragraphs(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.out: list[str] = []
        self.buffer: list[str] = []
        self.skip = 0

    def _flush(self) -> None:
        text = normalize("".join(self.buffer))
        self.buffer = []
        if text:
            self.out.append(text)

    def handle_starttag(self, tag, attrs):
        if tag in SKIP:
            self.skip += 1
        if tag in BLOCK or tag == "br":
            self._flush()

    def handle_startendtag(self, tag, attrs):
        if tag in BLOCK or tag == "br":
            self._flush()

    def handle_endtag(self, tag):
        if tag in SKIP:
            self.skip = max(0, self.skip - 1)
        if tag in BLOCK:
            self._flush()

    def handle_data(self, data):
        if not self.skip:
            self.buffer.append(data)

    def close(self):
        super().close()
        self._flush()


def decode(raw: bytes) -> str:
    for encoding in ("utf-8-sig", "cp1252"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1")


def paragraphs(html: str) -> list[str]:
    parser = _Paragraphs()
    parser.feed(html)
    parser.close()
    return [p for p in parser.out if LATIN_WORD.search(p)]


LOADDOC_ONLY = re.compile(r"^\[LOADDOC\][^\[]*\[/LOADDOC\]$", re.I)


def scen_description(path: Path) -> str:
    """The plain <ScenDescription> HTML of a .scen file (shown in the Load Scenario window).

    Everything else in a .scen (sides, briefings, events) sits in the encrypted
    Scenario_Compressed block, which is deliberately not read.
    """
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return ""
    element = root.find("ScenDescription")
    return element.text or "" if element is not None else ""


def collect(directory: Path, pattern: str) -> dict[str, list[str]]:
    found: dict[str, list[str]] = {}
    for path in sorted(directory.rglob(pattern)):
        suffix = path.suffix.lower()
        if not path.is_file() or suffix not in (".html", ".scen"):
            continue
        name = str(path.relative_to(directory)).replace("\\", "/")
        if suffix == ".html":
            html = decode(path.read_bytes())
        else:
            html = scen_description(path)
            name += "#ScenDescription"
        for text in paragraphs(html):
            if LOADDOC_ONLY.match(text):
                continue   # the game replaces the marker with the referenced HTML file
            files = found.setdefault(text, [])
            if name not in files:
                files.append(name)
    return found


def translated() -> dict:
    return json.loads(PROSE.read_text(encoding="utf-8")) if PROSE.is_file() else {}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=("worklist", "coverage"))
    parser.add_argument("directory", type=Path)
    parser.add_argument("--glob", default="*", help="file name pattern (.html pages and .scen descriptions)")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args(argv)
    if not args.directory.is_dir():
        print(f"FAIL not a directory: {args.directory}", file=sys.stderr)
        return 1
    found = collect(args.directory, args.glob)
    done = translated()
    missing = {k: v for k, v in found.items() if k not in done}
    if args.command == "coverage":
        print(f"paragraphs={len(found)} translated={len(found) - len(missing)} missing={len(missing)}")
        for text, files in list(missing.items())[:20]:
            print(f"  MISSING [{files[0]}] {text[:100]}")
        return 0 if not missing else 3
    if not args.out:
        parser.error("worklist needs --out")
    out = args.out.resolve()
    for forbidden in (ROOT / "dict", args.directory.resolve()):
        if out == forbidden or forbidden in out.parents:
            print(f"FAIL refusing to write inside {forbidden}", file=sys.stderr)
            return 1
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(missing, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    print(f"worklist {out}: {len(missing)} of {len(found)} paragraphs need translation "
          f"({sum(len(k) for k in missing)} chars)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
