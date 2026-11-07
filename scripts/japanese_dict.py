#!/usr/bin/env python3
"""Small, scoped Japanese UI dictionaries; no Chinese merge or pool injection.

Inputs are only `dict/src/ja_menu.json` and `dict/src/ja_options.json`
(English display text -> reviewed Japanese).  The Chinese builder
(`scripts/build_dict.py`) skips every `ja_*.json`, and this builder never reads
the Chinese sources, glossary, briefing tables or `dict/CMOLocalizer.json`.

The output keeps the six-section shape of the Chinese dictionary so existing
parsers can read it, but `offsets`, `words`, `partial` and `comments` are always
empty: the Japanese engine (`src/ja/JapaneseUiLocalization.cs`) only performs
display-level exact replacement inside the scopes listed in `_meta.scopes`.

Usage
    python scripts/japanese_dict.py --check                 # validate + determinism, no write
    python scripts/japanese_dict.py --out build/ja-candidate/CMOLocalizerJa.json
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "dict" / "src"
DICT = ROOT / "dict"
OUTPUT_NAME = "CMOLocalizerJa.json"

PROFILE = "ja-minimal-ui-v1"
SOURCES = {"menu": "ja_menu.json", "options": "ja_options.json"}
# Hiragana/katakana, CJK ideographs (incl. extension A) and half-width katakana.
JAPANESE = re.compile("[%s-%s%s-%s%s-%s]" % tuple(chr(c) for c in (0x3040, 0x30FF, 0x3400, 0x9FFF, 0xFF66, 0xFF9F)))
TOKENS = re.compile(r"\{[^{}]*\}|<%[^%]*%>|%(?:\d+\$)?[-+#0]*\d*(?:\.\d+)?[sdifouxXeEgGc]|\b(?:Ctrl|Shift|Alt)\s*\+\s*(?:(?:Ctrl|Shift|Alt)\s*\+\s*)*[A-Za-z0-9]+|\bF(?:1[0-2]|[1-9])\b")
CODE = re.compile(r"ScenEdit_|\bSELECT\s+.+\s+FROM\b|\b(?:INSERT INTO|DELETE FROM|DROP TABLE)\b|<\s*/?\s*(?:script|html|body)\b", re.I)
TOP_LEVEL_SECTIONS = ("_meta", "comments", "offsets", "exact", "partial", "words")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key!r}")
        result[key] = value
    return result


def validate_pair(source: str, target: str) -> None:
    if not source.strip() or not isinstance(target, str) or not target.strip():
        raise ValueError(f"empty/non-string translation: {source!r}")
    if source == target or not JAPANESE.search(target):
        raise ValueError(f"expected a Japanese translation: {source!r}")
    if CODE.search(source) or CODE.search(target):
        raise ValueError(f"code is not a UI translation: {source!r}")
    if Counter(TOKENS.findall(source)) != Counter(TOKENS.findall(target)):
        raise ValueError(f"placeholder or shortcut changed: {source!r}")
    for char in ("\n", "\r", "\t", "&"):
        if source.count(char) != target.count(char):
            raise ValueError(f"layout/mnemonic token changed: {source!r}")
    for opened, closed in (("(", ")"), ("[", "]"), ("{", "}")):
        if source.count(opened) - source.count(closed) != target.count(opened) - target.count(closed):
            raise ValueError(f"unbalanced delimiters: {source!r}")
    # The engine also matches the trimmed text and re-applies the original
    # padding, so leading/trailing whitespace must survive translation.
    lead, trail = len(source) - len(source.lstrip()), len(source) - len(source.rstrip())
    if source[:lead] != target[:len(target) - len(target.lstrip())] or \
            source[len(source) - trail:] != target[len(target.rstrip()):]:
        raise ValueError(f"leading/trailing whitespace changed: {source!r}")


def validate_scope(scope: str, pairs: dict) -> None:
    """A translated value must never be another source key of the same scope.

    The engine re-applies translations whenever the game writes text back; if
    a value were also a key, T(T(x)) != T(x) and a control could oscillate.
    """
    keys = set(pairs)
    trimmed = {k.strip() for k in keys}
    for source, target in pairs.items():
        if target in keys or target.strip() in trimmed:
            raise ValueError(f"{scope}: translation is also a source key: {source!r} -> {target!r}")


def build(source_dir: Path, version: str, game_build: str) -> dict:
    exact = {}
    scopes = {}
    for scope, filename in SOURCES.items():
        data = json.loads((source_dir / filename).read_text(encoding="utf-8"),
                          object_pairs_hook=unique_object)
        if not isinstance(data, dict):
            raise ValueError(f"{filename}: expected an object")
        pairs = {}
        for source, target in data.items():
            if source.startswith("_"):
                continue
            validate_pair(source, target)
            if source in exact and exact[source] != target:
                raise ValueError(f"conflicting Japanese translations: {source!r}")
            exact[source] = target
            pairs[source] = target
        if not pairs:
            raise ValueError(f"{filename}: empty scope")
        validate_scope(scope, pairs)
        scopes[scope] = sorted(pairs)
    # Six sections retain the existing format contract. Scope information stays
    # in metadata and the dedicated entrypoint never loads these into the pool.
    return {
        "_meta": {"engine": "CMOLocalizer", "version": version,
                  "language": "ja", "profile": PROFILE, "game_build": game_build,
                  "scopes": scopes,
                  "counts": {"n_offsets": 0, "n_exact": len(exact),
                             "n_partial": 0, "n_words": 0}},
        "comments": {}, "offsets": {},
        "exact": {key: exact[key] for key in sorted(exact)},
        "partial": {}, "words": {},
    }


def serialize(doc: dict) -> bytes:
    if tuple(doc) != TOP_LEVEL_SECTIONS:
        raise ValueError(f"unexpected top-level sections: {list(doc)}")
    return (json.dumps(doc, ensure_ascii=False, indent=1) + "\n").encode("utf-8")


def read_anchor(name: str) -> str:
    return (ROOT / name).read_text(encoding="utf-8").strip()


def guard_output(path: Path) -> Path:
    resolved = path.resolve()
    dict_dir = DICT.resolve()
    if resolved == dict_dir or dict_dir in resolved.parents:
        raise ValueError(f"refusing to write a Japanese build artifact inside {dict_dir}")
    return resolved


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, help="write the dictionary here (never inside dict/)")
    parser.add_argument("--check", action="store_true", help="validate and prove byte-stable output; no write")
    parser.add_argument("--source-dir", type=Path, default=SRC)
    args = parser.parse_args(argv)
    if not args.out and not args.check:
        parser.error("choose --check and/or --out")
    version, game_build = read_anchor("VERSION"), read_anchor("GAME_BUILD")
    try:
        first = serialize(build(args.source_dir, version, game_build))
        second = serialize(build(args.source_dir, version, game_build))
        if first != second:
            raise ValueError("two builds over identical sources differ")
        doc = json.loads(first.decode("utf-8"))
        counts = {scope: len(keys) for scope, keys in doc["_meta"]["scopes"].items()}
        if args.out:
            out = guard_output(args.out)
            out.parent.mkdir(parents=True, exist_ok=True)
            tmp = out.with_name(out.name + ".tmp")
            tmp.write_bytes(first)
            tmp.replace(out)
            print(f"built {out}")
    except (ValueError, OSError) as exc:
        print(f"FAIL {exc}", file=sys.stderr)
        return 1
    print(f"PASS japanese dictionary profile={PROFILE} exact={len(doc['exact'])} "
          f"scopes={counts} bytes={len(first)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
