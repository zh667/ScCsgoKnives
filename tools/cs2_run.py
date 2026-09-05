#!/usr/bin/env python3
"""One way to run a child process, for every CS2 tool.

The Windows review of 0.16.5 found the same three faults at six call sites:

  * `capture_output=True, text=True` with no `encoding=`. Python then decodes with
    the locale codec, which on a Chinese Windows install is cp936, and any byte the
    codepage cannot map raises UnicodeDecodeError inside subprocess.run - the tool
    dies on the decode, not on anything it was checking.
  * `out.stdout.splitlines()` with no guard. stdout is None whenever capture was not
    requested, and '' when the child wrote nothing, so the failure surfaces as an
    AttributeError or an empty list rather than as the child's error.
  * no returncode check. A child that crashed and printed a partial line was read as
    a result.

Everything here is UTF-8 with replacement, checks the exit status, and puts the
child's stderr in front of whoever has to read the failure.
"""

from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def run(cmd, *, cwd=None, env=None, check=True, dotnet=False):
    """Run a command and return the CompletedProcess, decoded as UTF-8."""
    full_env = {**os.environ, **(env or {})}
    if dotnet:
        # The VPS has a newer runtime than these projects target.
        full_env.setdefault("DOTNET_ROLL_FORWARD", "Major")
    out = subprocess.run([str(c) for c in cmd], capture_output=True, text=True,
                         encoding="utf-8", errors="replace",
                         cwd=str(cwd or ROOT), env=full_env)
    if check and out.returncode != 0:
        raise SystemExit(fail(cmd, out, "exited with %d" % out.returncode))
    return out


def fail(cmd, out, why) -> str:
    return ("%s\n  command: %s\n  stdout: %s\n  stderr: %s"
            % (why, " ".join(str(c) for c in cmd),
               tail(out.stdout) or "(empty)", tail(out.stderr) or "(empty)"))


def tail(text, limit=2000) -> str:
    if not text:
        return ""
    text = text.strip()
    return text if len(text) <= limit else "..." + text[-limit:]


def run_json(cmd, *, cwd=None, env=None, dotnet=False, allow_exit=(0,)):
    """Run a command whose last stdout line starting with '{' or '[' is its result.

    Non-zero exits are allowed only when listed, because some checkers report a
    verdict through the exit code and still print a usable document.
    """
    out = run(cmd, cwd=cwd, env=env, check=False, dotnet=dotnet)
    if out.returncode not in allow_exit:
        raise SystemExit(fail(cmd, out, "exited with %d" % out.returncode))
    lines = [l for l in (out.stdout or "").splitlines() if l[:1] in ("{", "[")]
    if not lines:
        raise SystemExit(fail(cmd, out, "printed no JSON document"))
    try:
        return json.loads(lines[-1]), out
    except json.JSONDecodeError as exc:
        raise SystemExit(fail(cmd, out, "printed unparseable JSON: %s" % exc))
