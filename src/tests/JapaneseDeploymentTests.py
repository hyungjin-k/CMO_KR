#!/usr/bin/env python3
"""Synthetic game-directory tests for scripts/manage_ja_patch.py.

Everything happens in TemporaryDirectory; the installed game, the real
Command.exe.config and running processes are never touched (process detection
is replaced by a stub).  Every refusal case also proves "zero writes" by
comparing a byte snapshot of the whole synthetic directory.
"""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import manage_ja_patch as patcher  # noqa: E402

BOM = b"\xef\xbb\xbf"
# Shape of the real Command.exe.config: BOM, CRLF, tabs and mixed indentation.
CONFIG = BOM + (
    '<?xml version="1.0" encoding="utf-8"?>\r\n<configuration>\r\n'
    '  <startup useLegacyV2RuntimeActivationPolicy="true">\r\n'
    '    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />\r\n'
    '  </startup>\r\n<runtime>\r\n\t  <enforceFIPSPolicy enabled="false" />\r\n'
    '    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">\r\n'
    '\t\t<dependentAssembly>\r\n\t\t\t<assemblyIdentity name="KeraLua" publicKeyToken="6a194c04b9c89217" culture="neutral" />\r\n'
    '\t\t\t<bindingRedirect oldVersion="0.0.0.0-1.3.1.0" newVersion="1.3.1.0" />\r\n\t\t</dependentAssembly>\r\n'
    '    </assemblyBinding>\r\n</runtime>\r\n</configuration>\r\n').encode("utf-8")
CHINESE = CONFIG.replace(b"<runtime>\r\n", b'<runtime>\r\n<appDomainManagerType value="CMOLocalizer.LocalizerDomainManager" />\r\n'
                         b'<appDomainManagerAssembly value="CMOLocalizer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />\r\n')
NO_RUNTIME = ('<?xml version="1.0" encoding="utf-8"?>\n<configuration>\n  <startup>\n'
              '    <supportedRuntime version="v4.0" />\n  </startup>\n</configuration>\n').encode("utf-8")
passes = 0


def check(ok: bool, message: str) -> None:
    global passes
    if not ok:
        raise AssertionError(message)
    passes += 1
    print("PASS " + message, flush=True)


def snapshot(root: Path) -> dict[str, str]:
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(root.rglob("*")) if p.is_file()}


def run(*argv: str, running=()) -> tuple[int, str]:
    buffer = io.StringIO()
    with patch.object(patcher, "running_game_processes", lambda game: list(running)), \
            contextlib.redirect_stdout(buffer):
        code = patcher.main(list(argv))
    return code, buffer.getvalue()


@contextlib.contextmanager
def world(config: bytes = CONFIG):
    with tempfile.TemporaryDirectory(prefix="cmo-ja-deploy-") as directory:
        base = Path(directory)
        game, build = base / "game", base / "build"
        game.mkdir()
        build.mkdir()
        (game / "Command.exe").write_bytes(b"MZ fixture")
        (game / "Command.exe.config").write_bytes(config)
        files = {}
        for name, data in (("CMOLocalizerJa.dll", b"MZ japanese engine"), ("CMOLocalizerJa.json", b'{"_meta":{}}\n')):
            (build / name).write_bytes(data)
            files[name] = {"sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}
        manifest = {"profile": "ja-minimal-ui-v1", "assembly": patcher.LOADER_ASSEMBLY,
                    "domain_manager_type": patcher.LOADER_TYPE, "files": files}
        (build / patcher.MANIFEST).write_text(json.dumps(manifest), encoding="utf-8")
        yield game, build


def args(command: str, game: Path, build: Path | None = None, *extra: str) -> list[str]:
    result = [command, "--game-dir", str(game)]
    if build is not None:
        result += ["--build-dir", str(build)]
    return result + list(extra)


def main() -> int:
    # Probe self-test: the zero-write detector must notice a write.
    with world() as (game, build):
        before = snapshot(game)
        (game / "Command.exe.config").write_bytes(CONFIG + b" ")
        check(snapshot(game) != before, "negative control: snapshot detects a one-byte change")

    with world() as (game, build):
        before = snapshot(game)
        code, text = run(*args("apply", game, build))
        check(code == 0 and "dry-run" in text and snapshot(game) == before, "apply without --yes writes nothing")
        code, text = run(*args("plan", game, build))
        check(code == 0 and snapshot(game) == before, "plan writes nothing")

        code, text = run(*args("apply", game, build, "--yes"))
        config = (game / "Command.exe.config").read_bytes()
        check(code == 0, "apply --yes succeeds")
        check(config.startswith(BOM) and config.count(b"\r\n") == CONFIG.count(b"\r\n") + 2
              and b"\n" not in config.replace(b"\r\n", b""), "BOM and CRLF preserved, exactly two lines added")
        check(config.replace(patcher.loader_block(config.decode("utf-8")).encode("utf-8"), b"", 1) == CONFIG,
              "every original byte is unchanged around the inserted loader")
        patcher.verify_loader(config)
        check(True, "patched config parses and holds exactly one Japanese loader")
        check((game / "CMOLocalizerJa.dll").read_bytes() == b"MZ japanese engine", "engine installed from the candidate")
        state = json.loads((game / patcher.STATE).read_text(encoding="utf-8"))
        backup = game / state["backup_dir"]
        check((backup / "Command.exe.config").read_bytes() == CONFIG, "byte-exact config backup written")

        applied = snapshot(game)
        code, text = run(*args("apply", game, build, "--yes"))
        check(code == 2 and "already applied" in text and snapshot(game) == applied, "second apply refused, zero writes")

        code, text = run(*args("restore", game, None, "--yes"), running=[str(game / "Command.exe")])
        check(code == 2 and "running" in text and snapshot(game) == applied, "restore refused while the game runs")

        code, text = run(*args("restore", game))
        check(code == 0 and "dry-run" in text and snapshot(game) == applied, "restore without --yes writes nothing")
        code, text = run(*args("restore", game, None, "--yes"))
        check(code == 0 and (game / "Command.exe.config").read_bytes() == CONFIG, "restore returns the byte-exact config")
        check(not (game / "CMOLocalizerJa.dll").exists() and not (game / "CMOLocalizerJa.json").exists()
              and not (game / patcher.STATE).exists(), "restore removes installed files and state")

    with world(CHINESE) as (game, build):
        before = snapshot(game)
        code, text = run(*args("apply", game, build, "--yes"))
        check(code == 2 and "another patch" in text and snapshot(game) == before,
              "existing Chinese loader: refused, zero writes")

    with world() as (game, build):
        before = snapshot(game)
        code, text = run(*args("apply", game, build, "--yes"), running=[str(game / "Command.exe")])
        check(code == 2 and "running" in text and snapshot(game) == before, "running game: apply refused, zero writes")
        code, text = run(*args("apply", game, build, "--yes"), running=[r"D:\Other\Command.exe"])
        check(code == 0, "a Command.exe from another directory does not block")
        run(*args("restore", game, None, "--yes"))

    with world() as (game, build):
        before = snapshot(game)
        with patch.object(patcher, "running_game_processes", lambda game: None):
            buffer = io.StringIO()
            with contextlib.redirect_stdout(buffer):
                code = patcher.main(args("apply", game, build, "--yes"))
        check(code == 2 and "cannot determine" in buffer.getvalue() and snapshot(game) == before,
              "unknown process state: refused, zero writes")

    with world() as (game, build):
        (build / "CMOLocalizerJa.dll").write_bytes(b"MZ tampered")
        before = snapshot(game)
        code, text = run(*args("apply", game, build, "--yes"))
        check(code == 2 and "manifest hash" in text and snapshot(game) == before, "tampered candidate: refused, zero writes")

    # Game update after apply: config replaced by a fresh one without our loader.
    with world() as (game, build):
        run(*args("apply", game, build, "--yes"))
        updated = CONFIG.replace(b"1.3.1.0", b"1.3.2.0")
        (game / "Command.exe.config").write_bytes(updated)
        code, text = run(*args("restore", game, None, "--yes"))
        check(code == 0 and (game / "Command.exe.config").read_bytes() == updated and "left as is" in text,
              "drift without loader: the old backup is not copied over the updated config")
        check(not (game / "CMOLocalizerJa.dll").exists(), "drift without loader: installed files still removed")

    # Manual edit after apply that kept our loader: only our lines are removed.
    with world() as (game, build):
        run(*args("apply", game, build, "--yes"))
        edited = (game / "Command.exe.config").read_bytes().replace(b"1.3.1.0", b"1.3.9.0")
        (game / "Command.exe.config").write_bytes(edited)
        code, text = run(*args("restore", game, None, "--yes"))
        check(code == 0 and (game / "Command.exe.config").read_bytes() == CONFIG.replace(b"1.3.1.0", b"1.3.9.0"),
              "drift with loader: only the loader lines removed, the newer edit kept")

    with world() as (game, build):
        run(*args("apply", game, build, "--yes"))
        (game / "CMOLocalizerJa.dll").write_bytes(b"MZ replaced by someone else")
        applied = snapshot(game)
        code, text = run(*args("restore", game, None, "--yes"))
        check(code == 2 and "changed since apply" in text and snapshot(game) == applied,
              "changed installed file: restore refuses to delete it, zero writes")

    with world() as (game, build):
        (game / "CMOLocalizerJa.json").write_bytes(b"older unmanaged copy")
        run(*args("apply", game, build, "--yes"))
        run(*args("restore", game, None, "--yes"))
        check((game / "CMOLocalizerJa.json").read_bytes() == b"older unmanaged copy",
              "pre-existing file is backed up and put back")

    with world(NO_RUNTIME) as (game, build):
        code, _ = run(*args("apply", game, build, "--yes"))
        patcher.verify_loader((game / "Command.exe.config").read_bytes())
        run(*args("restore", game, None, "--yes"))
        check(code == 0 and (game / "Command.exe.config").read_bytes() == NO_RUNTIME,
              "config without <runtime>: element created, restore is byte-exact")

    # A failure in the middle of apply rolls everything back.
    with world() as (game, build):
        before = snapshot(game)
        real = patcher.atomic_write

        def failing(path, data):
            if path.name == "Command.exe.config" and data != CONFIG:
                raise OSError("simulated failure while writing the config")
            return real(path, data)

        with patch.object(patcher, "atomic_write", failing):
            try:
                run(*args("apply", game, build, "--yes"))
                raised = False
            except OSError:
                raised = True
        after = {k: v for k, v in snapshot(game).items() if not k.startswith(patcher.BACKUP_ROOT)}
        check(raised and after == before, "failed apply is rolled back (only the backup directory remains)")

    with world() as (game, build):
        config = (game / "Command.exe.config").read_bytes()
        (game / "Command.exe.config").write_bytes(patcher.insert_loader(config))
        code, text = run(*args("restore", game, None, "--yes"))
        check(code == 2 and "--force-strip" in text, "loader without state: refused unless --force-strip")
        code, text = run(*args("restore", game, None, "--yes", "--force-strip"))
        check(code == 0 and (game / "Command.exe.config").read_bytes() == CONFIG, "--force-strip removes exactly the loader")

    print(f"ALL PASS ({passes})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
