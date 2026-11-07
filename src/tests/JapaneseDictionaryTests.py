#!/usr/bin/env python3
"""Japanese dictionary regressions (R80). Only temporary directories are written.

1. The Chinese builder never reads `dict/src/ja_*.json` (R80: kanji-bearing Japanese
   values passed `sanitize` and overwrote 70 exact + 37 pool-injection entries).
2. The Japanese builder reads only its two sources, is byte-stable, keeps the
   six-section shape with empty injection/word maps, and rejects unsafe pairs.
3. Every source key is a real game string (string pool or menu dump).
"""
from __future__ import annotations

import io
import json
import re
import sys
import tempfile
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import build_dict  # noqa: E402
import japanese_dict as ja  # noqa: E402

# Keys confirmed neither in dict/strings_pool.json nor docs/menu_strings_dump.txt.
# Harmless (exact display match only); remove if the live untranslated TSV never shows them.
UNVERIFIED_KEYS = {"Performance"}
passes = 0


def check(ok: bool, message: str) -> None:
    global passes
    if not ok:
        raise AssertionError(message)
    passes += 1
    print("PASS " + message, flush=True)


def put(path: Path, data: dict) -> None:
    path.write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")


def rejects(menu: dict, options: dict | None = None) -> str | None:
    """Return the ValueError text, or None if the sources were accepted."""
    with tempfile.TemporaryDirectory(prefix="cmo-ja-dict-") as directory:
        source = Path(directory)
        put(source / "ja_menu.json", menu)
        put(source / "ja_options.json", options if options is not None else {"Apply": "適用"})
        try:
            ja.build(source, "fixture", "0")
        except ValueError as exc:
            return str(exc)
    return None


def main() -> int:
    # ── 1. Chinese builder isolation ──
    names = [name for name, _ in build_dict.iter_source_maps()]
    check(not any(n.startswith("ja_") for n in names) and "menu_terms.json" in names,
          f"Chinese builder skips ja_*.json in the real tree ({len(names)} sources)")
    with tempfile.TemporaryDirectory(prefix="cmo-ja-zh-") as directory:
        source = Path(directory)
        put(source / "menu_terms.json", {"View": "视图"})
        put(source / "ja_menu.json", {"View": "表示"})
        put(source / "jx_menu.json", {"Zoom In": "放大"})  # negative control: same shape, not the ja_ prefix
        with patch.object(build_dict, "SRC", source):
            maps = dict(build_dict.iter_source_maps())
        check("ja_menu.json" not in maps and maps["menu_terms.json"]["View"] == "视图",
              "a kanji-bearing Japanese value cannot reach the Chinese merge")
        check("jx_menu.json" in maps, "negative control: the fixture glob does see other files")

    # ── 2. Japanese builder ──
    version = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    game_build = (ROOT / "GAME_BUILD").read_text(encoding="utf-8").strip()
    first = ja.serialize(ja.build(ja.SRC, version, game_build))
    second = ja.serialize(ja.build(ja.SRC, version, game_build))
    check(first == second, f"two builds are byte-identical ({len(first)} bytes)")
    doc = json.loads(first.decode("utf-8"))
    check(list(doc) == list(ja.TOP_LEVEL_SECTIONS), "six-section format kept")
    check(all(doc[s] == {} for s in ("comments", "offsets", "partial", "words")),
          "no pool injection, words, partial or comments")
    meta = doc["_meta"]
    check(meta["language"] == "ja" and meta["profile"] == ja.PROFILE and meta["game_build"] == game_build,
          "metadata declares language, profile and game build")
    menu = json.loads((ja.SRC / "ja_menu.json").read_text(encoding="utf-8"))
    options = json.loads((ja.SRC / "ja_options.json").read_text(encoding="utf-8"))
    prose = json.loads((ja.SRC / "ja_prose.json").read_text(encoding="utf-8"))
    check(meta["scopes"]["menu"] == sorted(k for k in menu if not k.startswith("_"))
          and meta["scopes"]["options"] == sorted(k for k in options if not k.startswith("_"))
          and meta["scopes"]["prose"] == sorted(k for k in prose if not k.startswith("_")),
          f"scopes mirror the three sources (menu={len(meta['scopes']['menu'])}, "
          f"options={len(meta['scopes']['options'])}, prose={len(meta['scopes']['prose'])})")
    check(all(ja.JAPANESE.search(v) for v in doc["exact"].values()), "every value contains Japanese script")
    check(sum(1 for v in doc["exact"].values() if not re.search(r"[一-鿿]", v)) > 50,
          "kana-only values are kept (they were dropped by the Chinese sanitize)")
    check(b"\r\n" not in first and first.endswith(b"}\n"), "LF output with one trailing newline")

    with tempfile.TemporaryDirectory(prefix="cmo-ja-only-") as directory:
        source = Path(directory)
        for name in ja.SOURCES.values():
            (source / name).write_bytes((ja.SRC / name).read_bytes())
        check(ja.serialize(ja.build(source, version, game_build)) == first,
              "a directory holding only the three Japanese sources builds the same bytes (no other input)")

    bad = {
        "untranslated value": {"File": "File"},
        "Latin-only value": {"File": "Fairu"},
        "dropped placeholder": {"{0} units": "ユニット"},
        "changed shortcut": {"Log [Ctrl + Shift + M]": "ログ [Ctrl + M]"},
        "changed mnemonic": {"&File": "ファイル"},
        "unbalanced parenthesis": {"Mode (A)": "モード (A"},
        "code": {"ScenEdit_SetUnit": "ユニット"},
        "SQL": {"SELECT * FROM units": "ユニット"},
        "padding lost": {"Display all ": "すべて表示"},
        "value equals another key": {"File": "ファイル", "ファイル": "ファイル表示"},
        "empty value": {"File": ""},
    }
    for label, pairs in bad.items():
        error = rejects(pairs)
        check(error is not None, f"reject {label} ({error})")
    check(rejects({"Apply": "適用する"}, {"Apply": "適用"}) is not None, "reject conflicting translations across scopes")
    check(rejects({"_note": "only metadata"}) is not None, "reject an empty scope")
    with tempfile.TemporaryDirectory(prefix="cmo-ja-dup-") as directory:
        source = Path(directory)
        (source / "ja_menu.json").write_text('{"File": "ファイル", "File": "ファイル"}', encoding="utf-8")
        put(source / "ja_options.json", {"Apply": "適用"})
        try:
            ja.build(source, "fixture", "0")
            duplicate = None
        except ValueError as exc:
            duplicate = str(exc)
        check(duplicate is not None, f"reject duplicate JSON keys ({duplicate})")
    check(rejects({"File": "ファイル"}) is None, "negative control: a valid pair is accepted")
    check(rejects({"Select tanker from mission:": "ミッションから給油機を選択:"}) is None,
          "natural 'Select ... from' menu text is not mistaken for SQL (live false positive)")

    # ── prose (scenario descriptions, briefings, messages) ──
    ok_pair = ('Select one of them and click on the "Ready/Arm" button.',
               'そのうち1機を選択し、"Ready/Arm" ボタンをクリックします。')
    ja.validate_prose_pair(*ok_pair)
    check(True, "negative control: a valid prose pair is accepted")
    long_labels = ('Uncheck "Investigate unknown contacts outside the patrol area" and "Investigate unknown contacts within weapons range".',
                   '"Investigate unknown contacts outside the patrol area" と "Investigate unknown contacts within weapons range" のチェックを外します。')
    ja.validate_prose_pair(*long_labels)
    check(True, "quoted labels longer than 40 characters pair correctly (live false positive)")
    bad_prose = {
        "number lost": ("You have 6 F-16I Sufa.", "F-16I Sufa が数機あります。"),
        "quoted UI label translated": ('Click on "OK-Ready".', "「準備完了」をクリックします。"),
        "F-key lost": ("Press F6 to open it.", "ファンクションキーで開きます。"),
        "key name lost": ("Press CTRL + SHIFT + F9.", "コントロール + シフト + F9 を押します。"),
        "path component translated": (r"It is at C:\Program Files (x86)\Steam\Docs.",
                                      r"C:\プログラム (x86)\Steam\Docs にあります。"),
        "key not normalized": ("Good  morning!", "おはようございます！"),
        "line break in value": ("Good morning!", "おはよう" + chr(10) + "ございます！"),
        "English only": ("Good morning!", "Good morning!"),
    }
    for label, (source, target) in bad_prose.items():
        try:
            ja.validate_prose_pair(source, target)
            error = None
        except ValueError as exc:
            error = str(exc)
        check(error is not None, f"reject prose: {label} ({error})")
    check(not any(k for k in prose if not k.startswith("_") and k != ja.normalize_prose(k)),
          f"every prose key is whitespace-normalized ({len(prose) - 1} paragraphs)")

    # ── extractor: .scen descriptions, LOADDOC markers, the encrypted block is never read ──
    sys.path.insert(0, str(ROOT / "scripts"))
    import extract_ja_prose as ex
    with tempfile.TemporaryDirectory(prefix="cmo-ja-scen-") as directory:
        folder = Path(directory)
        description = "&lt;BODY&gt;&lt;P&gt;Welcome to the fixture.&lt;/P&gt;&lt;P&gt;[LOADDOC]x.html[/LOADDOC]&lt;/P&gt;&lt;/BODY&gt;"
        (folder / "Fixture.scen").write_text(
            '<?xml version="1.0"?><ScenContainer><ScenTitle>Fixture Title</ScenTitle>'
            f"<ScenDescription>{description}</ScenDescription>"
            "<Scenario_Compressed>Hidden briefing sentence should never be read.</Scenario_Compressed></ScenContainer>",
            encoding="utf-8")
        (folder / "page.html").write_text("<html><body><p>Page paragraph here.</p><br>Second line here.</body></html>",
                                          encoding="utf-8")
        found = ex.collect(folder, "*")
        check(set(found) == {"Welcome to the fixture.", "Page paragraph here.", "Second line here."},
              f"extractor reads .scen descriptions and HTML, skips LOADDOC markers ({sorted(found)})")
        check(found["Welcome to the fixture."] == ["Fixture.scen#ScenDescription"], "extractor records the .scen source")
        check(not any("Hidden" in k or "Fixture Title" in k for k in found),
              "negative control: the encrypted Scenario_Compressed block and the title are not read")

    # ── CLI ──
    target = ROOT / "dict" / "ja-probe.json"
    sink = io.StringIO()
    with redirect_stdout(sink), redirect_stderr(sink):
        code = ja.main(["--out", str(target)])
    check(code == 1 and not target.exists(), "refuse to write a build artifact inside dict/")
    with redirect_stdout(sink), redirect_stderr(sink):
        code = ja.main(["--check"])
    check(code == 0, "--check passes on the real sources")

    # ── 3. keys are real game strings ──
    pool = set(json.loads((ROOT / "dict" / "strings_pool.json").read_text(encoding="utf-8")).values())
    dump = set()
    for line in (ROOT / "docs" / "menu_strings_dump.txt").read_text(encoding="utf-8").splitlines():
        found = re.match(r"\[\d+\] (.*?)  ==>  ", line)
        if found:
            dump.add(found.group(1))
    # Strings read from the live game's CMOLocalizerJa_untranslated.tsv, recorded per game build.
    live = set()
    for key, value in menu.items():
        if key.startswith("_live_observed_"):
            check(isinstance(value, list) and all(v in menu for v in value),
                  f"{key}: every live-observed key is a translated menu entry ({len(value)})")
            live.update(value)
    # Prose keys come from scenario HTML; `scripts/extract_ja_prose.py coverage` checks them against the game.
    unknown = sorted(k for k in doc["exact"] if k not in prose
                     and k not in pool and k not in dump and k not in live and k not in UNVERIFIED_KEYS)
    check(not unknown, f"every key is a game string from the pool, the menu dump or the live game (unknown: {unknown})")
    check(all(k in doc["exact"] for k in UNVERIFIED_KEYS), "the unverified allowlist has no stale entries")

    print(f"ALL PASS ({passes})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
