#!/usr/bin/env python3
"""Apply / restore the Japanese minimal UI candidate in a CMO game directory.

Touches exactly three things: `CMOLocalizerJa.dll`, `CMOLocalizerJa.json` and two
loader elements inside `<runtime>` of `Command.exe.config`.  Nothing else in the
game directory is read for modification (no DB templates, HTML, maps, images).

    python scripts/manage_ja_patch.py status  [--game-dir DIR]
    python scripts/manage_ja_patch.py plan    [--game-dir DIR] [--build-dir build/ja-candidate]
    python scripts/manage_ja_patch.py apply   [--game-dir DIR] [--build-dir ...]   # dry-run
    python scripts/manage_ja_patch.py apply   ... --yes                            # writes
    python scripts/manage_ja_patch.py restore [--game-dir DIR] [--yes]

Safety rules
  * Dry-run unless --yes.  Refuses while that directory's Command.exe is running.
  * Refuses when any appDomainManager is already configured (e.g. the Chinese patch):
    only one loader can exist, and silently replacing another patch is not allowed.
  * The candidate's SHA256 must match build/ja-candidate/ja-candidate.manifest.json.
  * The original config bytes (BOM, CRLF, tabs) are kept: two lines are inserted
    after `<runtime>`; a hash-checked backup is written before any change, and a
    failed apply is rolled back.
  * restore puts the backed-up config back only if the config is still exactly the
    one we wrote.  If it changed since (game update, Steam file verification, manual
    edit) the old backup is NOT copied over it; only our two loader lines are removed.
    Installed files are deleted only if their hash is still the one we installed.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_GAME = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Command - Modern Operations")
DEFAULT_BUILD = ROOT / "build" / "ja-candidate"
MANIFEST = "ja-candidate.manifest.json"
DLL = "CMOLocalizerJa.dll"
JSON = "CMOLocalizerJa.json"
PAYLOAD = (DLL, JSON)
CONFIG = "Command.exe.config"
STATE = "CMOLocalizerJa_state.json"
BACKUP_ROOT = "CMOLocalizerJa_backup"
LOADER_ASSEMBLY = "CMOLocalizerJa, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
LOADER_TYPE = "CMOLocalizer.Japanese.JapaneseDomainManager"
OWN_LOADER = re.compile(
    r'[ \t]*<appDomainManagerAssembly\s+value="CMOLocalizerJa,[^"]*"\s*/>\r?\n?'
    r'|[ \t]*<appDomainManagerType\s+value="CMOLocalizer\.Japanese\.JapaneseDomainManager"\s*/>\r?\n?')


class Refused(Exception):
    """A precondition failed; nothing was written."""


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def file_sha(path: Path) -> str | None:
    return sha256(path.read_bytes()) if path.is_file() else None


def out(message: str) -> None:
    print(message, flush=True)


# ── game process detection (patched by tests) ───────────────────────────────

def running_game_processes(game_dir: Path) -> list[str] | None:
    """Executable paths of running Command.exe processes, or None if unknown."""
    command = ("Get-CimInstance Win32_Process -Filter \"Name='Command.exe'\" | "
               "ForEach-Object { $_.ExecutablePath }")
    try:
        result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", command],
                                capture_output=True, text=True, timeout=60)
    except (OSError, subprocess.SubprocessError):
        return None
    if result.returncode != 0:
        return None
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def ensure_game_closed(game_dir: Path, assume_closed: bool) -> None:
    if assume_closed:
        return
    paths = running_game_processes(game_dir)
    if paths is None:
        raise Refused("cannot determine whether CMO is running (use --assume-closed only if you are sure)")
    target = os.path.normcase(str((game_dir / "Command.exe").resolve()))
    if any(os.path.normcase(str(Path(p).resolve())) == target for p in paths):
        raise Refused("Command.exe from this directory is running; close the game first")


# ── config editing (byte-preserving) ────────────────────────────────────────

def newline_of(text: str) -> str:
    return "\r\n" if "\r\n" in text else "\n"


def loader_block(text: str) -> str:
    nl = newline_of(text)
    return (f'{nl}    <appDomainManagerAssembly value="{LOADER_ASSEMBLY}" />'
            f'{nl}    <appDomainManagerType value="{LOADER_TYPE}" />')


def insert_loader(original: bytes) -> bytes:
    text = original.decode("utf-8")
    if "appDomainManager" in text:
        raise Refused("Command.exe.config already configures an appDomainManager "
                      "(another patch such as the Chinese localization is installed); restore it first")
    if re.search(r"<runtime\s*/>", text):
        raise Refused("self-closing <runtime/> is not supported; edit the config manually")
    found = re.search(r"<runtime\s*>", text)
    if found:
        new = text[:found.end()] + loader_block(text) + text[found.end():]
    else:
        end = re.search(r"</configuration\s*>", text)
        if not end:
            raise Refused("Command.exe.config has no </configuration>")
        nl = newline_of(text)
        new = text[:end.start()] + "  <runtime>" + loader_block(text) + nl + "  </runtime>" + nl + text[end.start():]
    data = new.encode("utf-8")
    verify_loader(data)
    return data


def verify_loader(data: bytes) -> None:
    try:
        root = ET.fromstring(data)
    except ET.ParseError as exc:
        raise Refused(f"patched config is not valid XML: {exc}") from None
    runtime = root.find("runtime")
    if runtime is None:
        raise Refused("patched config has no <runtime>")
    assembly = runtime.findall("appDomainManagerAssembly")
    kind = runtime.findall("appDomainManagerType")
    if len(assembly) != 1 or len(kind) != 1 or assembly[0].get("value") != LOADER_ASSEMBLY \
            or kind[0].get("value") != LOADER_TYPE:
        raise Refused("patched config does not contain exactly one Japanese loader")


def strip_loader(data: bytes) -> bytes:
    text = data.decode("utf-8")
    block = loader_block(text)
    if block in text:
        return text.replace(block, "", 1).encode("utf-8")
    return OWN_LOADER.sub("", text).encode("utf-8")


def has_own_loader(data: bytes) -> bool:
    return bool(OWN_LOADER.search(data.decode("utf-8", errors="replace")))


# ── payload ─────────────────────────────────────────────────────────────────

def load_candidate(build_dir: Path) -> dict[str, bytes]:
    manifest_path = build_dir / MANIFEST
    if not manifest_path.is_file():
        raise Refused(f"no candidate manifest at {manifest_path}; run scripts/build_ja.ps1 first")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if manifest.get("assembly") != LOADER_ASSEMBLY or manifest.get("domain_manager_type") != LOADER_TYPE:
        raise Refused("candidate manifest has an unexpected CLR entry point")
    payload = {}
    for name in PAYLOAD:
        path = build_dir / name
        expected = (manifest.get("files") or {}).get(name, {}).get("sha256")
        if not path.is_file() or not expected:
            raise Refused(f"candidate is missing {name}")
        data = path.read_bytes()
        if sha256(data) != expected:
            raise Refused(f"{name} does not match its manifest hash (stale or tampered candidate)")
        payload[name] = data
    return payload


def atomic_write(path: Path, data: bytes) -> None:
    tmp = path.with_name(path.name + ".ja_tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)
    if path.read_bytes() != data:
        raise OSError(f"verification after writing {path} failed")


def read_state(game_dir: Path) -> dict | None:
    path = game_dir / STATE
    return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else None


# ── commands ────────────────────────────────────────────────────────────────

def require_game(game_dir: Path) -> None:
    if not (game_dir / "Command.exe").is_file() or not (game_dir / CONFIG).is_file():
        raise Refused(f"{game_dir} does not look like a CMO directory (Command.exe / {CONFIG} missing)")


def cmd_status(args) -> int:
    game = args.game_dir
    require_game(game)
    config = (game / CONFIG).read_bytes()
    state = read_state(game)
    out(f"game directory : {game}")
    out(f"config sha256  : {sha256(config)}")
    out(f"japanese loader: {'present' if has_own_loader(config) else 'absent'}")
    other = "appDomainManager" in config.decode("utf-8", errors="replace") and not has_own_loader(config)
    out(f"other loader   : {'PRESENT (another patch)' if other else 'absent'}")
    for name in PAYLOAD:
        out(f"{name:<20}: {file_sha(game / name) or 'absent'}")
    if state:
        drift = sha256(config) != state["config"]["installed_sha256"]
        out(f"state          : applied {state['applied_at']} (backup {state['backup_dir']})"
            + ("; CONFIG CHANGED SINCE APPLY" if drift else ""))
    else:
        out("state          : not applied by this tool")
    for name in ("CMOLocalizerJa.log", "CMOLocalizerJa_untranslated.tsv"):
        if (game / name).is_file():
            out(f"diagnostics    : {name} ({(game / name).stat().st_size} bytes)")
    return 0


def plan_apply(args) -> tuple[dict[str, bytes], bytes, bytes]:
    game = args.game_dir
    require_game(game)
    if read_state(game):
        raise Refused("already applied by this tool; run restore first")
    payload = load_candidate(args.build_dir)
    original = (game / CONFIG).read_bytes()
    patched = insert_loader(original)
    out(f"game directory : {game}")
    out(f"candidate      : {args.build_dir}")
    for name, data in payload.items():
        existing = file_sha(game / name)
        out(f"  write {name} ({len(data)} bytes, sha256 {sha256(data)[:16]}...)"
            + (f"; existing file {existing[:16]}... is backed up" if existing else ""))
    out(f"  edit  {CONFIG}: insert 2 loader lines into <runtime> "
        f"({len(original)} -> {len(patched)} bytes; backup first)")
    return payload, original, patched


def cmd_plan(args) -> int:
    plan_apply(args)
    out("plan only: nothing was written")
    return 0


def cmd_apply(args) -> int:
    payload, original, patched = plan_apply(args)
    if not args.yes:
        out("dry-run: nothing was written (add --yes to apply)")
        return 0
    game = args.game_dir
    ensure_game_closed(game, args.assume_closed)
    stamp = _dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = game / BACKUP_ROOT / stamp
    suffix = 1
    while backup.exists():
        suffix += 1
        backup = game / BACKUP_ROOT / f"{stamp}-{suffix}"
    backup.mkdir(parents=True)
    record = {"config": {"original_sha256": sha256(original)}, "files": {}}
    (backup / CONFIG).write_bytes(original)
    preexisting = {}
    for name in PAYLOAD:
        if (game / name).is_file():
            preexisting[name] = (game / name).read_bytes()
            (backup / name).write_bytes(preexisting[name])
    for name in [CONFIG, *preexisting]:
        if file_sha(backup / name) != (sha256(original) if name == CONFIG else sha256(preexisting[name])):
            raise OSError(f"backup verification failed for {name}")
    written = []
    try:
        for name in PAYLOAD:
            atomic_write(game / name, payload[name])
            written.append(name)
        atomic_write(game / CONFIG, patched)
        written.append(CONFIG)
        for name in PAYLOAD:
            record["files"][name] = {"preexisting": name in preexisting,
                                     "preexisting_sha256": sha256(preexisting[name]) if name in preexisting else None,
                                     "installed_sha256": sha256(payload[name])}
        record.update({"profile": "ja-minimal-ui-v1",
                       "applied_at": _dt.datetime.now().isoformat(timespec="seconds"),
                       "backup_dir": str(backup.relative_to(game)),
                       "candidate": str(args.build_dir)})
        record["config"]["installed_sha256"] = sha256(patched)
        (backup / "backup.json").write_text(json.dumps(record, indent=1) + "\n", encoding="utf-8")
        atomic_write(game / STATE, (json.dumps(record, indent=1) + "\n").encode("utf-8"))
    except BaseException:
        # Roll back everything this run wrote; the backup directory stays as evidence.
        if CONFIG in written:
            atomic_write(game / CONFIG, original)
        for name in PAYLOAD:
            if name in written:
                if name in preexisting:
                    atomic_write(game / name, preexisting[name])
                else:
                    (game / name).unlink(missing_ok=True)
        (game / STATE).unlink(missing_ok=True)
        raise
    out(f"applied; backup at {backup}")
    out("start CMO normally; diagnostics go to CMOLocalizerJa.log and CMOLocalizerJa_untranslated.tsv")
    return 0


def cmd_restore(args) -> int:
    game = args.game_dir
    require_game(game)
    state = read_state(game)
    config = (game / CONFIG).read_bytes()
    actions: list[tuple[str, object]] = []
    if state is None:
        if not has_own_loader(config):
            out("nothing to restore: no state file and no Japanese loader in the config")
            return 0
        if not args.force_strip:
            raise Refused("Japanese loader present but no state file; use --force-strip to remove only the loader lines")
        actions.append(("config", strip_loader(config)))
    else:
        backup = game / state["backup_dir"]
        if sha256(config) == state["config"]["installed_sha256"]:
            original = (backup / CONFIG).read_bytes()
            if sha256(original) != state["config"]["original_sha256"]:
                raise Refused(f"backup config {backup / CONFIG} does not match its recorded hash")
            actions.append(("config", original))
        elif has_own_loader(config):
            out("config changed since apply (game update / file verification / manual edit): "
                "the old backup is not copied back; removing only the Japanese loader lines")
            actions.append(("config", strip_loader(config)))
        else:
            out("config changed since apply and no longer loads the Japanese engine: left as is")
        for name, info in state["files"].items():
            current = file_sha(game / name)
            if current is None:
                continue
            if current != info["installed_sha256"] and not args.force:
                raise Refused(f"{name} changed since apply ({current[:16]}...); refusing to delete it (use --force)")
            if info["preexisting"]:
                data = (backup / name).read_bytes()
                if sha256(data) != info["preexisting_sha256"]:
                    raise Refused(f"backup of {name} does not match its recorded hash")
                actions.append((name, data))
            else:
                actions.append((name, None))
    for target, data in actions:
        name = CONFIG if target == "config" else target
        out(f"  {'restore' if data is not None else 'delete '} {name}")
    if not args.yes:
        out("dry-run: nothing was written (add --yes to restore)")
        return 0
    ensure_game_closed(game, args.assume_closed)
    for target, data in actions:
        path = game / (CONFIG if target == "config" else target)
        if data is None:
            path.unlink()
        else:
            atomic_write(path, data)
    if has_own_loader((game / CONFIG).read_bytes()):
        raise OSError("the Japanese loader is still present after restore")
    (game / STATE).unlink(missing_ok=True)
    out("restored. Backups under " + BACKUP_ROOT + " and the CMOLocalizerJa diagnostics files were kept.")
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Apply/restore the Japanese minimal UI candidate (dry-run by default).")
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("status", "plan", "apply", "restore"):
        p = sub.add_parser(name)
        p.add_argument("--game-dir", type=Path, default=DEFAULT_GAME)
        if name in ("plan", "apply"):
            p.add_argument("--build-dir", type=Path, default=DEFAULT_BUILD)
        if name in ("apply", "restore"):
            p.add_argument("--yes", action="store_true", help="actually write (default: dry-run)")
            p.add_argument("--assume-closed", action="store_true",
                           help="skip the running-game check (only if process detection is unavailable)")
        if name == "restore":
            p.add_argument("--force", action="store_true", help="delete installed files even if they changed")
            p.add_argument("--force-strip", action="store_true",
                           help="no state file: remove only the Japanese loader lines")
    args = parser.parse_args(argv)
    args.game_dir = args.game_dir.resolve()
    if hasattr(args, "build_dir"):
        args.build_dir = args.build_dir.resolve()
    handler = {"status": cmd_status, "plan": cmd_plan, "apply": cmd_apply, "restore": cmd_restore}[args.command]
    try:
        return handler(args)
    except Refused as exc:
        out(f"REFUSED {exc}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
