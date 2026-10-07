"""current-direction-20260929 §6 M0: isolated feasibility prototype for the 1.9.3.2_MP engine (Windows only).

Run through the project wrapper:  ./tools/dev.ps1 python tools/MpM0/m0.py <step> [...]
Steps
  prepare   build the snapshot's Survivalcraft.TestAutomation mod (win-x64) and collect the MP engine's managed DLLs
            (game folder + CompatNet/Multiplayer internal mods) into refs/mp, with hashes
  abi       metadata-only binding check of the delivered DLLs (and the probe) against that MP build (tools/MpAbiCheck)
  build     build probe, adapter (Net/ScCsgoNet.bin) and the 1.9.3.1 harness from a detached copy of tools/MpM0, and
            compose TEST-ONLY packages: the delivered c15 Lite/Full with the probe and adapter added (never delivered)
  run131 <lite|full>             the test package in an isolated copy of the 1.9.3.1 game (player's Mods/doc never copied)
  runmp <handshake|timeout|full> MP server + client processes from the fixed, locked runtime (.tmp/mp-test-runtime/<role>),
            driven through TestAutomation's UDP command/log ports (loopback only)
  tapatch   the isolated TestAutomation copy: loopback-only command port with a source check, rebuilt into mods/
  preflight the fixed runtime's lock, firewall setup and stale-process check, without starting a game
  devbuild <tag>  current sources through the release pipeline's prepare/build into a new stage (M1+ development)
  devpkg <tag>    src/ScCsgoNet against that stage + the MP build; TEST-ONLY dev packages (c15 packages with the stage's DLLs)
Everything lives under .tmp/mp-m0-20260929; the player's game folder is only read.
"""
import hashlib, json, os, re, shutil, socket, subprocess, sys, threading, time, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
M0 = ROOT / ".tmp/mp-m0-20260929"
SRC = M0 / "src"
MPBIN = SRC / "Survivalcraft.Windows/bin/Release/net10.0-windows/win-x64"
TA_BUILT = SRC / "Survivalcraft.Test/bin/Release/net10.0-windows/win-x64/Mods/Survivalcraft.TestAutomation.scmod"
GAME131 = Path(r"D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1")
OUT = ROOT / "output"
# the delivered packages carry the family's version (src/ScCsgoKnives/modinfo.json); the deathmatch one has no 包 suffix (2026-10-06)
VERSION = json.loads((ROOT / "src/ScCsgoKnives/modinfo.json").read_text("utf8"))["Version"]
def pkgname(label, version=None): return f"[API1.9]CS武器{version or VERSION}-{label}{'' if label == '死亡竞赛' else '包'}.scmod"
LITE = OUT / pkgname("轻量")
FULL = OUT / pkgname("全量")
AGENTS = OUT / pkgname("探员")
DM = OUT / pkgname("死亡竞赛")
REFS = M0 / "refs/mp"
MODS = M0 / "mods"
PKG = M0 / "pkg"
BUILD = M0 / "build"
RESULTS = M0 / "results"
RUNS = M0 / "run"
# User data and per-install state of the 1.9.3.1 game that an isolated copy must not take.
SKIP131_DIRS = {"bugs", "doc", "mods", "command", "techtrees", "screencapture", "modscache", "processmodlists", "shadercaches"}
SKIP131_FILES = ("ScCsgo", "RecipaediaEX")
# Standalone acceptance runs on an ISOLATED copy of SurvivalcraftAPI 1.9.3.1 (AGENTS.md 2026-10-01); the MP snapshot is for
# multiplayer paths only. M0_ENGINE selects the engine the single-player scripts drive: "mp" (snapshot + its TestAutomation
# mod) or "131" (isolated 1.9.3.1 copy + ScTestAutomation131, the port of that mod: ta131 step).
ENGINE = os.environ.get("M0_ENGINE", "mp")
TA131 = PKG / "ScTestAutomation131.scmod"
SP131 = "sp131"   # the isolated 1.9.3.1 copy's folder in the fixed runtime (same lock, job object and process accounting)


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


def run(cmd, cwd=None, log=None, check=True):
    print(">", " ".join(str(c) for c in cmd), flush=True)
    r = subprocess.run([str(c) for c in cmd], cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = r.stdout + r.stderr
    if log: Path(log).parent.mkdir(parents=True, exist_ok=True); Path(log).write_text(text, "utf-8")
    tail = "\n".join(l for l in text.splitlines() if " error " in l or "error CS" in l or "Build succeeded" in l or "Warn" in l[:5])[-3000:]
    print(tail or text[-1500:], flush=True)
    if check and r.returncode: raise SystemExit(f"command failed ({r.returncode}): {cmd[0]}")
    return r.returncode


def managed(path):
    """True for a .NET assembly (PE with a CLI header)."""
    try:
        b = Path(path).read_bytes()
        pe = int.from_bytes(b[0x3C:0x40], "little")
        if b[pe:pe + 4] != b"PE\0\0": return False
        opt = pe + 24; magic = int.from_bytes(b[opt:opt + 2], "little")
        dd = opt + (96 if magic == 0x10B else 112)
        return int.from_bytes(b[dd + 14 * 8:dd + 14 * 8 + 4], "little") != 0
    except Exception:
        return False


def framework_names():
    names = set()
    base = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "dotnet/shared"
    for fw in ["Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App"]:
        for d in sorted((base / fw).glob("10.*")): names |= {p.name.lower() for p in d.glob("*.dll")}
    return names


# ---------------------------------------------------------------- prepare / abi / build
def prepare():
    run(["dotnet", "build", SRC / "Survivalcraft.TestAutomation/Survivalcraft.TestAutomation.csproj", "-c", "Release", "-p:WindowsRuntimeIdentifier=win-x64"],
        cwd=SRC, log=M0 / "logs/build-testautomation.log")
    MODS.mkdir(parents=True, exist_ok=True)
    shutil.copy2(TA_BUILT, MODS / TA_BUILT.name)
    if REFS.exists(): shutil.rmtree(REFS)
    REFS.mkdir(parents=True)
    fw = framework_names(); taken = {}
    for p in sorted(MPBIN.glob("*.dll")):
        if p.name.lower() not in fw and managed(p): shutil.copy2(p, REFS / p.name); taken[p.name] = {"from": str(p.relative_to(M0)), "sha256": sha(p)}
    for mod in sorted((MPBIN / "InternalMods").glob("*.scmod")):
        with zipfile.ZipFile(mod) as z:
            for n in z.namelist():
                if n.lower().endswith(".dll") and "/" not in n.strip("/") and Path(n).name.lower() not in fw and Path(n).name not in taken:
                    (REFS / Path(n).name).write_bytes(z.read(n))
                    if managed(REFS / Path(n).name): taken[Path(n).name] = {"from": f"{mod.relative_to(M0)}!{n}", "sha256": sha(REFS / Path(n).name)}
                    else: (REFS / Path(n).name).unlink()
    record = {"mpBuild": {p.name: sha(p) for p in [MPBIN / "Survivalcraft.dll", MPBIN / "Engine.dll", MPBIN / "EntitySystem.dll", MPBIN / "Survivalcraft.exe"]},
              "internalMods": {p.name: sha(p) for p in sorted((MPBIN / "InternalMods").glob("*.scmod"))},
              "testAutomation": {"path": str((MODS / TA_BUILT.name).relative_to(M0)), "sha256": sha(MODS / TA_BUILT.name)},
              "refs": taken}
    (M0 / "refs/refs.json").write_text(json.dumps(record, indent=1), "utf-8")
    print(json.dumps({k: v for k, v in record.items() if k != "refs"}, indent=1)); print("refs", len(taken))


TA_SOURCE = SRC / "Survivalcraft.TestAutomation/TestAutomationModLoader.cs"
TA_PATCH = [
    # The command port accepts EXEC/FUNC scripts: listen on loopback only (it listened on every interface) ...
    ("udpServer = new UdpClient(cmdPort);", "udpServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, cmdPort)); // sc-mp-test: loopback only"),
    # ... and drop anything that still arrives from elsewhere (same line: the file's line endings are left as they are).
    ("var remoteEP = receiveResult.RemoteEndPoint;",
     "var remoteEP = receiveResult.RemoteEndPoint; if (!IPAddress.IsLoopback(remoteEP.Address)) { Log.Warning($\"[Survivalcraft.TestAutomation] command from {remoteEP} ignored (loopback only)\"); continue; } // sc-mp-test"),
]


def tapatch():
    """Test infrastructure only: the isolated snapshot copy of Survivalcraft.TestAutomation (never the player's platform,
    never a published package) gets a loopback-only command port with a source check, and is rebuilt into mods/."""
    with open(TA_SOURCE, encoding="utf-8", newline="") as f: text = f.read()  # line endings kept as they are
    before = sha(TA_SOURCE)
    if "sc-mp-test: loopback only" not in text:
        original = TA_SOURCE.with_suffix(".cs.orig")
        if not original.exists(): shutil.copy2(TA_SOURCE, original)
        for old, new in TA_PATCH:
            assert text.count(old) == 1, f"TestAutomation source differs from the audited snapshot: {old[:60]}"
            text = text.replace(old, new)
        assert "using System.Net;" in text
        with open(TA_SOURCE, "w", encoding="utf-8", newline="") as f: f.write(text)
    run(["dotnet", "build", SRC / "Survivalcraft.TestAutomation/Survivalcraft.TestAutomation.csproj", "-c", "Release", "-p:WindowsRuntimeIdentifier=win-x64"],
        cwd=SRC, log=M0 / "logs/build-testautomation.log")
    MODS.mkdir(parents=True, exist_ok=True); shutil.copy2(TA_BUILT, MODS / TA_BUILT.name)
    record = {"source": str(TA_SOURCE.relative_to(M0)), "sourceBefore": before, "sourceAfter": sha(TA_SOURCE),
              "original": sha(TA_SOURCE.with_suffix(".cs.orig")), "package": sha(MODS / TA_BUILT.name)}
    (M0 / "logs/tapatch.json").write_text(json.dumps(record, indent=1), "utf-8"); print(json.dumps(record, indent=1))


def ta131():
    """Test infrastructure only: builds tools/MpM0/TestAutomation131 (the 1.9.3.1 port of the snapshot's TestAutomation mod)
    from a detached copy and packages it with the Roslyn scripting assemblies into pkg/ScTestAutomation131.scmod, for the
    isolated 1.9.3.1 copy only (never the player's Mods, never a published package)."""
    work = BUILD / "src-ta131"
    if work.exists(): shutil.rmtree(work)
    shutil.copytree(ROOT / "tools/MpM0/TestAutomation131", work, ignore=shutil.ignore_patterns("bin", "obj"))
    out = BUILD / "out/ta131"
    if out.exists(): shutil.rmtree(out)
    run(["dotnet", "build", work / "ScTestAutomation131.csproj", "-c", "Release", "-o", out], log=M0 / "logs/build-ta131.log")
    PKG.mkdir(parents=True, exist_ok=True)
    members = [out / "ScTestAutomation131.dll"] + sorted(out.glob("Microsoft.CodeAnalysis*.dll"))
    assert len(members) >= 5, [m.name for m in members]   # the mod plus CodeAnalysis, CSharp, Scripting, CSharp.Scripting
    with zipfile.ZipFile(TA131, "w", zipfile.ZIP_DEFLATED) as z:
        for m in members: z.write(m, m.name)
        z.writestr("modinfo.json", json.dumps({"Name": "ScTestAutomation131 (isolated 1.9.3.1 test copies only)", "Version": "0.0.1", "ApiVersion": "1.9.3.1",
                                               "PackageName": "zh667.ScTestAutomation131", "LoadOrder": 5000, "Dependencies": {}, "Description": "test only"}))
    record = {"engine": "1.9.3.1", "sources": {p.relative_to(work).as_posix(): sha(p) for p in sorted(work.rglob("*")) if p.is_file()},
              "members": {m.name: {"bytes": m.stat().st_size, "sha256": sha(m)} for m in members}, "package": sha(TA131), "bytes": TA131.stat().st_size}
    (PKG / "ta131.json").write_text(json.dumps(record, indent=1), "utf-8")
    print(json.dumps({k: v for k, v in record.items() if k != "sources"}, indent=1))


def extract(package, members, dest):
    dest.mkdir(parents=True, exist_ok=True); got = {}
    with zipfile.ZipFile(package) as z:
        for m in members:
            (dest / Path(m).name).write_bytes(z.read(m)); got[m] = sha(dest / Path(m).name)
    return got


def tool_build(name):
    src = BUILD / "tools-src" / name
    if src.exists(): shutil.rmtree(src)
    shutil.copytree(ROOT / "tools" / name, src, ignore=shutil.ignore_patterns("bin", "obj"))
    run(["dotnet", "build", src, "-c", "Release", "-o", BUILD / "tools" / name], log=M0 / f"logs/build-{name}.log")
    return BUILD / "tools" / name / f"{name}.dll"


def abi():
    check = tool_build("MpAbiCheck")
    full = extract(FULL, ["ScCsgoKnives.dll", "ScCsgoTactical.dll", "ScCsgoVoice.dll", "Integrations/ScCsgoAppearance.bin"], BUILD / "c15/full")
    lite = extract(LITE, ["ScCsgoKnives.dll"], BUILD / "c15/lite")
    (BUILD / "c15/full/ScCsgoAppearance.dll").write_bytes((BUILD / "c15/full/ScCsgoAppearance.bin").read_bytes())
    RESULTS.mkdir(parents=True, exist_ok=True); codes = {}
    sets = {"abi-mp-full": [BUILD / "c15/full" / n for n in ["ScCsgoKnives.dll", "ScCsgoTactical.dll", "ScCsgoVoice.dll"]],
            "abi-mp-lite": [BUILD / "c15/lite/ScCsgoKnives.dll"],
            "abi-mp-appearance": [BUILD / "c15/full/ScCsgoKnives.dll", BUILD / "c15/full/ScCsgoTactical.dll", BUILD / "c15/full/ScCsgoAppearance.dll"]}
    probe = BUILD / "out/probe/ScCsgoNetProbe.dll"
    if probe.exists(): sets["abi-mp-probe"] = [probe]
    for key, dlls in sets.items():
        codes[key] = run(["dotnet", check, RESULTS / f"{key}.json", REFS] + dlls, check=False)
    print(json.dumps({"exit": codes, "fullMembers": full, "liteMembers": lite}, indent=1))


def build():
    work = BUILD / "src"
    if work.exists(): shutil.rmtree(work)
    shutil.copytree(ROOT / "tools/MpM0", work, ignore=shutil.ignore_patterns("bin", "obj", "*.py"))
    out = BUILD / "out"
    if out.exists(): shutil.rmtree(out)
    run(["dotnet", "build", work / "Probe/ScCsgoNetProbe.csproj", "-c", "Release", "-o", out / "probe"], log=M0 / "logs/build-probe.log")
    run(["dotnet", "build", work / "Harness131/ScM0Harness131.csproj", "-c", "Release", "-o", out / "harness"], log=M0 / "logs/build-harness.log")
    refs = BUILD / "adapter-refs"
    if refs.exists(): shutil.rmtree(refs)
    shutil.copytree(REFS, refs)
    shutil.copy2(out / "probe/ScCsgoNetProbe.dll", refs)
    extract(LITE, ["ScCsgoKnives.dll"], refs)
    run(["dotnet", "build", work / "Adapter/ScCsgoNet.csproj", "-c", "Release", "-o", out / "adapter", f"-p:Refs={refs}"], log=M0 / "logs/build-adapter.log")
    PKG.mkdir(parents=True, exist_ok=True)
    probe, adapter, harness = out / "probe/ScCsgoNetProbe.dll", out / "adapter/ScCsgoNet.dll", out / "harness/ScM0Harness131.dll"
    record = {"probe": sha(probe), "adapter": sha(adapter), "harness": sha(harness), "sources": {p.relative_to(work).as_posix(): sha(p) for p in sorted(work.rglob("*")) if p.is_file()}}
    with zipfile.ZipFile(PKG / "ScM0Harness131.scmod", "w", zipfile.ZIP_DEFLATED) as z:
        z.write(harness, "ScM0Harness131.dll")
        z.writestr("modinfo.json", json.dumps({"Name": "M0 test harness (isolated copies only)", "Version": "0.0.1", "ApiVersion": "1.9.3.1",
                                               "PackageName": "zh667.ScM0Harness", "LoadOrder": 5000, "Dependencies": {}, "Description": "test only"}))
    for label, source in [("lite", LITE), ("full", FULL)]:
        target = PKG / f"test-{label}-c15+netM0.scmod"
        with zipfile.ZipFile(source) as src, zipfile.ZipFile(target, "w") as dst:
            for info in src.infolist(): dst.writestr(info, src.read(info), compress_type=info.compress_type)
            dst.write(probe, "ScCsgoNetProbe.dll", zipfile.ZIP_DEFLATED)
            dst.write(adapter, "Net/ScCsgoNet.bin", zipfile.ZIP_DEFLATED)
        record[target.name] = {"base": source.name, "baseSha256": sha(source), "sha256": sha(target), "bytes": target.stat().st_size}
    record[PKG.joinpath("ScM0Harness131.scmod").name] = sha(PKG / "ScM0Harness131.scmod")
    (PKG / "packages.json").write_text(json.dumps(record, indent=1), "utf-8")
    print(json.dumps({k: v for k, v in record.items() if k != "sources"}, indent=1))


# ---------------------------------------------------------------- development packages (M1+, test only)
STAGES = ROOT / ".tmp/completion-140-20260929"


def devbuild(tag):
    """Builds the current sources' DLLs through the release pipeline's own prepare/build steps (new stage <tag>)."""
    run([sys.executable, ROOT / "tools/completion_140.py", tag, "prepare,build"], cwd=ROOT, log=M0 / f"logs/devbuild-{tag}.log")


def stage_dll(tag, edition, part, name):
    return STAGES / tag / edition / part / "source/bin/Release/net10.0" / f"{name}.dll"


def devpkg(tag):
    """Adapter against this stage's core and the MP build; dev packages = the output/ packages with this stage's DLLs + Net/ScCsgoNet.bin."""
    refs = BUILD / f"net-refs-{tag}"
    if refs.exists(): shutil.rmtree(refs)
    shutil.copytree(REFS, refs)
    shutil.copy2(stage_dll(tag, "lite", "core", "ScCsgoKnives"), refs / "ScCsgoKnives.dll")
    src = BUILD / f"net-src-{tag}"
    if src.exists(): shutil.rmtree(src)
    shutil.copytree(ROOT / "src/ScCsgoNet", src, ignore=shutil.ignore_patterns("bin", "obj"))
    out = BUILD / f"net-out-{tag}"
    if out.exists(): shutil.rmtree(out)
    run(["dotnet", "build", src / "ScCsgoNet.csproj", "-c", "Release", "-o", out, f"-p:Refs={refs}"], log=M0 / f"logs/build-net-{tag}.log")
    adapter = out / "ScCsgoNet.dll"
    # 2026-10-02: the CompatNet corrections are a second, optional payload (src/ScCsgoNetCompat).
    compat_src = BUILD / f"net-compat-src-{tag}"
    if compat_src.exists(): shutil.rmtree(compat_src)
    shutil.copytree(ROOT / "src/ScCsgoNetCompat", compat_src, ignore=shutil.ignore_patterns("bin", "obj"))
    compat_out = BUILD / f"net-compat-out-{tag}"
    if compat_out.exists(): shutil.rmtree(compat_out)
    run(["dotnet", "build", compat_src / "ScCsgoNetCompat.csproj", "-c", "Release", "-o", compat_out, f"-p:Refs={refs}"], log=M0 / f"logs/build-net-compat-{tag}.log")
    compat = compat_out / "ScCsgoNetCompat.dll"
    PKG.mkdir(parents=True, exist_ok=True)
    record = {"tag": tag, "adapter": sha(adapter), "adapterSources": {p.name: sha(p) for p in sorted(src.glob("*.cs"))}}
    for label, source, swaps in [
            ("lite", LITE, {"ScCsgoKnives.dll": stage_dll(tag, "lite", "core", "ScCsgoKnives")}),
            ("full", FULL, {"ScCsgoKnives.dll": stage_dll(tag, "full", "core", "ScCsgoKnives"), "ScCsgoTactical.dll": stage_dll(tag, "full", "agents", "ScCsgoTactical"),
                            "ScCsgoVoice.dll": stage_dll(tag, "full", "voice", "ScCsgoVoice")})]:
        target = PKG / f"dev-{tag}-{label}.scmod"
        with zipfile.ZipFile(source) as z, zipfile.ZipFile(target, "w") as dst:
            names = set(z.namelist())
            for info in z.infolist():
                if info.filename in swaps: dst.write(swaps[info.filename], info.filename, zipfile.ZIP_DEFLATED)
                elif info.filename not in (ScNetPayload, ScNetCompatPayload): dst.writestr(info, z.read(info), compress_type=info.compress_type)
            assert all(n in names for n in swaps), f"{label}: members to replace are missing"
            dst.write(adapter, ScNetPayload, zipfile.ZIP_DEFLATED); dst.write(compat, ScNetCompatPayload, zipfile.ZIP_DEFLATED)
        record[target.name] = {"base": source.name, "baseSha256": sha(source), "sha256": sha(target), "bytes": target.stat().st_size,
                               "swapped": {k: sha(v) for k, v in swaps.items()}}
    (PKG / f"dev-{tag}.json").write_text(json.dumps(record, indent=1), "utf-8")
    print(json.dumps(record, indent=1))


ScNetPayload = "Net/ScCsgoNet.bin"
ScNetCompatPayload = "Net/ScCsgoNetCompat.bin"


def candpkg(tag):
    """The release pipeline's own candidates (with Net/ScCsgoNet.bin inside) as this tag's MP test packages
    dev-<tag>-{lite,full,agents}.scmod: the final acceptance runs on exactly what would be delivered."""
    PKG.mkdir(parents=True, exist_ok=True); record = {"tag": tag, "kind": "release candidates copied unchanged"}
    for label, name in [("lite", pkgname("轻量")), ("full", pkgname("全量")), ("agents", pkgname("探员"))]:
        source = STAGES / tag / "candidate" / name; target = PKG / f"dev-{tag}-{label}.scmod"
        with zipfile.ZipFile(source) as z: assert (ScNetPayload in z.namelist()) == (label != "agents"), f"{source.name}: {ScNetPayload} presence"
        shutil.copy2(source, target)
        record[target.name] = {"candidate": str(source), "sha256": sha(target), "bytes": target.stat().st_size}
    (PKG / f"dev-{tag}.json").write_text(json.dumps(record, indent=1), "utf-8"); print(json.dumps(record, indent=1))


EDITION_PACKAGES = {"full": ["full"], "lite": ["lite"], "lite+agents": ["lite", "agents"]}
def cs_packages(tag, spec):
    """The CS packages of the server and of the clients for a run spec: one edition for every process ("full", "lite", or
    "lite+agents": the split Lite core with the agents package), or "<server>/<clients>" for mixed editions
    (first-person-eye-shot-20261001: phones on Lite + agents and computers on Full in one session)."""
    def one(edition): return [PKG / f"dev-{tag}-{n}.scmod" for n in EDITION_PACKAGES[edition]]
    server, clients = spec.split("/", 1) if "/" in spec else (spec, spec)
    return one(server), one(clients)
def run_label(spec):
    """A file-name label for a run spec ("full/lite+agents" -> "full-host-lite-agents-clients")."""
    return spec.replace("+", "-").replace("/", "-host-") + ("-clients" if "/" in spec else "")


def sppkg(tag):
    """Single-player test packages (post-mp-bugs-20260930): the delivered output/ packages with this stage's DLLs and nothing
    else changed. No network adapter: the member must be absent from the base and stays absent. For focused checks and
    runtime tests only; release candidates come from the release pipeline itself."""
    PKG.mkdir(parents=True, exist_ok=True)
    record = {"tag": tag, "kind": "single-player test package; no Net/ScCsgoNet.bin"}
    for label, source, swaps in [
            ("lite", LITE, {"ScCsgoKnives.dll": stage_dll(tag, "lite", "core", "ScCsgoKnives")}),
            ("full", FULL, {"ScCsgoKnives.dll": stage_dll(tag, "full", "core", "ScCsgoKnives"), "ScCsgoTactical.dll": stage_dll(tag, "full", "agents", "ScCsgoTactical"),
                            "ScCsgoVoice.dll": stage_dll(tag, "full", "voice", "ScCsgoVoice")})]:
        target = PKG / f"sp-{tag}-{label}.scmod"
        with zipfile.ZipFile(source) as z, zipfile.ZipFile(target, "w") as dst:
            names = set(z.namelist())
            assert all(n in names for n in swaps), f"{label}: unexpected base members"
            for info in z.infolist():
                if info.filename in swaps: dst.write(swaps[info.filename], info.filename, zipfile.ZIP_DEFLATED)
                elif info.filename == ScNetPayload: continue   # the delivered packages carry the MP adapter since r3k; the single-player test package never does
                else: dst.writestr(info, z.read(info), compress_type=info.compress_type)
        with zipfile.ZipFile(target) as z: assert ScNetPayload not in z.namelist()
        record[target.name] = {"base": source.name, "baseSha256": sha(source), "sha256": sha(target), "bytes": target.stat().st_size,
                               "swapped": {k: sha(v) for k, v in swaps.items()}}
    (PKG / f"sp-{tag}.json").write_text(json.dumps(record, indent=1), "utf-8")
    print(json.dumps(record, indent=1))


# ---------------------------------------------------------------- isolated 1.9.3.1 run
def copy_tree(src, dst, skip_dir, skip_file):
    for root, dirs, files in os.walk(src):
        rel = Path(root).relative_to(src)
        if rel == Path("."): dirs[:] = [d for d in dirs if not skip_dir(d)]
        (dst / rel).mkdir(parents=True, exist_ok=True)
        for f in files:
            if rel == Path(".") and skip_file(f): continue
            shutil.copy2(Path(root) / f, dst / rel / f)


def run131(label):
    # One fixed folder for the 1.9.3.1 single-player check as well (it opens no network port); its state is cleared first.
    game = ROOT / ".tmp/mp-test-runtime-131/game"
    if game.exists(): shutil.rmtree(game)
    copy_tree(GAME131, game, lambda d: d.lower() in SKIP131_DIRS, lambda f: f.startswith(SKIP131_FILES))
    (game / "Mods").mkdir()
    for p in [PKG / "ScM0Harness131.scmod", PKG / f"test-{label}-c15+netM0.scmod"]: shutil.copy2(p, game / "Mods" / p.name)
    report = game / "ScM0-standalone.json"; t0 = time.time()
    proc = subprocess.Popen([str(game / "Survivalcraft.exe")], cwd=game, env=dict(os.environ, SCCS_DIAGNOSTICS="1"))
    try:
        while time.time() - t0 < 600 and proc.poll() is None and not report.exists(): time.sleep(2)
        time.sleep(3)
    finally:
        if proc.poll() is None: proc.kill(); proc.wait(10)
    log = (game / "Bugs/Game.log").read_text("utf-8", errors="replace") if (game / "Bugs/Game.log").exists() else ""
    lines = log.splitlines()
    result = {"case": f"131-{label}", "game": str(GAME131), "gameExe": sha(GAME131 / "Survivalcraft.exe"), "gameDll": sha(GAME131 / "Survivalcraft.dll"),
              "package": sha(PKG / f"test-{label}-c15+netM0.scmod"), "seconds": round(time.time() - t0, 1), "exitCode": proc.returncode,
              "report": json.loads(report.read_text("utf-8")) if report.exists() else None,
              "net": [l for l in lines if "[ScCsgoNet]" in l or "[ScM0Harness]" in l],
              "errors": [l for l in lines if "[Error]" in l or "Exception" in l][:40], "logLines": len(lines)}
    RESULTS.mkdir(parents=True, exist_ok=True)
    (RESULTS / f"131-{label}.json").write_text(json.dumps(result, ensure_ascii=False, indent=1), "utf-8")
    (RESULTS / f"131-{label}-Game.log").write_text(log, "utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=1)[:12000])


# ---------------------------------------------------------------- MP processes: the fixed, locked test runtime
# docs/tasks/mp-unattended-network-20260930.md. Every MP game process runs from one of four FIXED role folders, so the
# Windows firewall rules for exactly these four executables (E:/Develop/AgentBridge/Configure-ScMpTestNetwork.ps1: UDP from
# 127.0.0.1 allowed, every other unicast inbound source blocked) cover every run and no run ever creates a new program path.
# Reports and logs stay in each run's case_dir. One OS lock owns the whole runtime from the first game of a run until all
# of its processes have exited; each run starts from archived-and-cleaned role state and a verified engine/Mods copy.
RUNTIME = ROOT / ".tmp/mp-test-runtime"
ROLES = ("server", "client", "client1", "client2")
ARCHIVE = RUNTIME / "_archive"
FW_GROUP = "ScCsgo isolated MP tests (loopback only)"
FW_RECEIPT = Path(r"E:/Develop/AgentBridge/acceptance/sc-mp-network-setup.json")
LOCK_WAIT = int(os.environ.get("SC_MP_LOCK_WAIT", "900"))  # seconds another run may wait for the runtime, then "busy"
KEEP_ARCHIVES = 3


class PreflightError(RuntimeError): pass
class RuntimeBusy(RuntimeError): pass


def free_udp():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.bind(("127.0.0.1", 0)); port = s.getsockname()[1]; s.close(); return port


def powershell(script, timeout=60):
    r = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout, creationflags=subprocess.CREATE_NO_WINDOW)
    return r.returncode, r.stdout.strip(), r.stderr.strip()


def firewall_preflight():
    """The one-time administrator setup must exist and read back: the receipt, and for every role an enabled inbound UDP
    allow rule for 127.0.0.1 on that exact executable plus an enabled block of every other unicast source. Fails at
    once otherwise (a missing rule would mean a Windows prompt waiting for a click, which an unattended run must not do)."""
    problems = []
    if not FW_RECEIPT.exists(): problems.append(f"no setup receipt {FW_RECEIPT} (run E:/Develop/AgentBridge/Enable-ScMpTestNetwork.cmd once and approve UAC)")
    code, out, err = powershell(
        f"$g='{FW_GROUP}'; $o=@(); foreach($x in @(Get-NetFirewallRule -Group $g -ErrorAction SilentlyContinue)){{ "
        "$o += [pscustomobject]@{Name=$x.Name;Enabled=[string]$x.Enabled;Action=[string]$x.Action;Direction=[string]$x.Direction;"
        "Program=($x|Get-NetFirewallApplicationFilter).Program;Protocol=[string]($x|Get-NetFirewallPortFilter).Protocol;"
        "Remote=((($x|Get-NetFirewallAddressFilter).RemoteAddress) -join ',')} }; ConvertTo-Json -InputObject @($o) -Compress")
    rules = {}
    try: rules = {r["Name"]: r for r in (json.loads(out) if out else [])}
    except Exception: problems.append(f"firewall rules not readable: {err or out[:300]}")
    for role in ROLES:
        exe = str(RUNTIME / role / "Survivalcraft.exe").lower()
        allow, block = rules.get(f"ScCsgo-MpTest-{role}-Loopback"), rules.get(f"ScCsgo-MpTest-{role}-ExternalBlock")
        if not (allow and allow["Enabled"] == "True" and allow["Action"] == "Allow" and allow["Direction"] == "Inbound"
                and (allow["Program"] or "").lower() == exe and allow["Protocol"].upper() == "UDP"
                and allow["Remote"] == "127.0.0.1"):  # Windows refuses ::1 in a rule; the tests use IPv4 loopback only
            problems.append(f"{role}: loopback allow rule missing or different")
        if not (block and block["Enabled"] == "True" and block["Action"] == "Block" and (block["Program"] or "").lower() == exe
                and "1.0.0.0-126.255.255.255" in block["Remote"] and "128.0.0.0-223.255.255.255" in block["Remote"] and "::2-" in block["Remote"]):
            problems.append(f"{role}: external block rule missing or different")
    return {"receipt": FW_RECEIPT.exists(), "rules": sorted(rules), "problems": problems}


def runtime_processes():
    """Processes running any executable under the fixed runtime root (pid, path)."""
    root = str(RUNTIME).replace("'", "''")
    code, out, err = powershell("ConvertTo-Json -Compress -InputObject @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and "
                                f"$_.ExecutablePath.StartsWith('{root}', [StringComparison]::OrdinalIgnoreCase) }} | ForEach-Object {{ [pscustomobject]@{{Id=$_.ProcessId;Path=$_.ExecutablePath}} }})")
    try: return [(p["Id"], p["Path"]) for p in (json.loads(out) if out else [])]
    except Exception: return []


def inside_runtime(path):
    path = Path(path).resolve(); root = RUNTIME.resolve()
    return path != root and root in path.parents


class _Runtime:
    """The single owner of the fixed runtime for this process (held until every game process of the run has exited)."""
    def __init__(self):
        self.lockfile = None; self.case_dir = None; self.games = []; self.active = set(); self.record = None; self.job = None

    def acquire(self, case_dir):
        if self.lockfile is not None:
            if Path(case_dir) != self.case_dir: raise RuntimeError("one run per process: the runtime already belongs to " + str(self.case_dir))
            return
        import msvcrt, atexit
        RUNTIME.mkdir(parents=True, exist_ok=True)
        f = open(RUNTIME / ".runtime.lock", "a+b"); f.seek(0); t0 = time.time()
        while True:
            try: msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1); break
            except OSError:
                if time.time() - t0 > LOCK_WAIT:
                    owner = (RUNTIME / ".owner.json").read_text("utf-8") if (RUNTIME / ".owner.json").exists() else "unknown"
                    f.close(); raise RuntimeBusy(f"MP test runtime busy for {LOCK_WAIT} s (owner {owner})")
                time.sleep(5)
        self.lockfile, self.case_dir = f, Path(case_dir)
        atexit.register(self.release)
        (RUNTIME / ".owner.json").write_text(json.dumps({"pid": os.getpid(), "case": str(case_dir), "since": time.strftime("%Y-%m-%d %H:%M:%S")}), "utf-8")
        record = {"runtime": str(RUNTIME), "case": str(case_dir), "started": time.strftime("%Y-%m-%d %H:%M:%S")}
        try:
            record["firewall"] = fw = firewall_preflight()
            if fw["problems"]: raise PreflightError("MP test network not configured: " + "; ".join(fw["problems"]))
            # Leftovers of a run that died without releasing its processes: the lock proves nobody owns them.
            stale = runtime_processes(); record["staleProcesses"] = stale
            for pid, path in stale: subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True)
            for i in range(40):
                if not runtime_processes(): break
                time.sleep(0.5)
            else: raise RuntimeError("processes from the test runtime do not exit: " + str(runtime_processes()))
            record["archived"] = self._archive_previous_state()
            self.job = _job_object()
        except Exception:
            self.release(); raise
        self.record = record; self._write()

    def _archive_previous_state(self):
        """Moves every role folder's run state (anything that is not the engine: doc, Bugs, Mods, caches) into
        _archive/<previous run>, keeps the last few archives, and leaves the role folders holding the engine only."""
        engine = {p.name.lower() for p in MPBIN.iterdir()}
        engine131 = {p.name.lower() for p in GAME131.iterdir() if p.name.lower() not in SKIP131_DIRS and not p.name.startswith(SKIP131_FILES)}
        prev = json.loads((RUNTIME / ".round.json").read_text("utf-8")).get("case", "unknown") if (RUNTIME / ".round.json").exists() else "unknown"
        dest = ARCHIVE / (time.strftime("%Y%m%d-%H%M%S") + "-" + Path(prev).name)
        moved = []
        for role in ROLES + (SP131,):
            d = RUNTIME / role
            if not d.exists(): continue
            for entry in d.iterdir():
                if entry.name.lower() in (engine131 if role == SP131 else engine): continue
                assert inside_runtime(entry), entry
                target = dest / role / entry.name; target.parent.mkdir(parents=True, exist_ok=True)
                if entry.name.lower() == "mods": shutil.rmtree(entry)  # packages are inputs, recorded by hash, not state
                else: shutil.move(str(entry), str(target)); moved.append(f"{role}/{entry.name}")
        archives = sorted(p for p in ARCHIVE.iterdir() if p.is_dir()) if ARCHIVE.exists() else []
        for old in archives[:-KEEP_ARCHIVES]:
            assert inside_runtime(old), old; shutil.rmtree(old)
        (RUNTIME / ".round.json").write_text(json.dumps({"case": str(self.case_dir)}), "utf-8")
        return {"to": str(dest) if moved else None, "moved": moved}

    def prepare_role(self, role, mods):
        if role not in ROLES: raise ValueError(f"role {role!r} has no configured program path (roles: {', '.join(ROLES)})")
        if role in self.active: raise RuntimeError(f"role {role} is already running in this run")
        d = RUNTIME / role; d.mkdir(parents=True, exist_ok=True)
        _mirror(MPBIN, d, top_skip={"doc", "bugs", "mods"})
        if (d / "Mods").exists(): assert inside_runtime(d / "Mods"); shutil.rmtree(d / "Mods")
        (d / "Mods").mkdir()
        for m in mods: shutil.copy2(m, d / "Mods" / m.name)
        hashes = {"exe": sha(d / "Survivalcraft.exe"), "dll": {n: sha(d / n) for n in ["Survivalcraft.dll", "Engine.dll", "EntitySystem.dll"]},
                  "mods": {m.name: sha(d / "Mods" / m.name) for m in mods}}
        assert hashes["exe"] == sha(MPBIN / "Survivalcraft.exe") and all(hashes["dll"][n] == sha(MPBIN / n) for n in hashes["dll"])
        self.active.add(role); self.record.setdefault("roles", []).append({"role": role, **hashes}); self._write()
        return d, hashes

    def prepare_131(self, mods):
        """The isolated SurvivalcraftAPI 1.9.3.1 copy: the engine files of the pristine download mirrored into sp131 (its user
        state dirs and the player's mod settings never taken), the packages under test as its only Mods."""
        if SP131 in self.active: raise RuntimeError(f"{SP131} is already running in this run")
        d = RUNTIME / SP131; d.mkdir(parents=True, exist_ok=True)
        _mirror(GAME131, d, top_skip=set(SKIP131_DIRS) | {"mods"})
        for f in d.iterdir():
            if f.is_file() and f.name.startswith(SKIP131_FILES): assert inside_runtime(f); f.unlink()
        if (d / "Mods").exists(): assert inside_runtime(d / "Mods"); shutil.rmtree(d / "Mods")
        (d / "Mods").mkdir()
        for m in mods: shutil.copy2(m, d / "Mods" / m.name)
        hashes = {"exe": sha(d / "Survivalcraft.exe"), "dll": {n: sha(d / n) for n in ["Survivalcraft.dll", "Engine.dll", "EntitySystem.dll"]},
                  "mods": {m.name: sha(d / "Mods" / m.name) for m in mods}}
        assert hashes["exe"] == sha(GAME131 / "Survivalcraft.exe") and all(hashes["dll"][n] == sha(GAME131 / n) for n in hashes["dll"])
        self.active.add(SP131); self.record.setdefault("roles", []).append({"role": SP131, "engine": "1.9.3.1", "source": str(GAME131), **hashes}); self._write()
        return d, hashes

    def _write(self):
        if self.case_dir and self.record is not None: (self.case_dir / "runtime.json").write_text(json.dumps(self.record, ensure_ascii=False, indent=1), "utf-8")

    def release(self):
        if self.lockfile is None: return
        try:
            for g in list(self.games):
                try:
                    if g.proc and g.proc.poll() is None: g.proc.kill(); g.proc.wait(20)
                except Exception: pass
            for i in range(60):
                if not runtime_processes(): break
                time.sleep(0.5)
            if self.record is not None: self.record["releasedWithProcesses"] = runtime_processes(); self.record["released"] = time.strftime("%Y-%m-%d %H:%M:%S"); self._write()
        finally:
            try: (RUNTIME / ".owner.json").unlink(missing_ok=True)
            except Exception: pass
            try:
                import msvcrt; self.lockfile.seek(0); msvcrt.locking(self.lockfile.fileno(), msvcrt.LK_UNLCK, 1)
            except Exception: pass
            self.lockfile.close(); self.lockfile = None


RUNTIME_OWNER = _Runtime()


def _mirror(src, dst, top_skip):
    """dst becomes an exact copy of src (changed files copied, extra files removed), apart from the top-level state folders."""
    for root, dirs, files in os.walk(src):
        rel = Path(root).relative_to(src)
        if rel == Path("."): dirs[:] = [d for d in dirs if d.lower() not in top_skip]
        (dst / rel).mkdir(parents=True, exist_ok=True)
        for f in files:
            a, b = Path(root) / f, dst / rel / f
            if not b.exists() or b.stat().st_size != a.stat().st_size or int(b.stat().st_mtime) != int(a.stat().st_mtime): shutil.copy2(a, b)
    for root, dirs, files in os.walk(dst, topdown=True):
        rel = Path(root).relative_to(dst)
        if rel == Path("."): dirs[:] = [d for d in dirs if d.lower() not in top_skip]
        for f in files:
            if not (src / rel / f).exists():
                assert inside_runtime(Path(root) / f); (Path(root) / f).unlink()
        for d in list(dirs):
            if not (src / rel / d).exists():
                assert inside_runtime(Path(root) / d); shutil.rmtree(Path(root) / d); dirs.remove(d)


def _job_object():
    """A Windows job object that kills every assigned process when this runner exits, however it exits."""
    import ctypes
    from ctypes import wintypes
    k = ctypes.WinDLL("kernel32", use_last_error=True)
    k.CreateJobObjectW.restype = wintypes.HANDLE
    job = k.CreateJobObjectW(None, None)
    class BASIC(ctypes.Structure):
        _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64), ("LimitFlags", wintypes.DWORD),
                    ("MinimumWorkingSetSize", ctypes.c_size_t), ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
                    ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD), ("SchedulingClass", wintypes.DWORD)]
    class IO(ctypes.Structure):
        _fields_ = [(n, ctypes.c_uint64) for n in ["R", "W", "O", "RT", "WT", "OT"]]
    class EXT(ctypes.Structure):
        _fields_ = [("Basic", BASIC), ("Io", IO), ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                    ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]
    info = EXT(); info.Basic.LimitFlags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    if not k.SetInformationJobObject(job, 9, ctypes.byref(info), ctypes.sizeof(info)): raise OSError(ctypes.get_last_error(), "SetInformationJobObject")
    return (k, job)


class Call:
    """A FUNC snippet whose varying values are arguments: $f<i> float, $i<i> int, $s<i> string. The isolated 1.9.3.1 copy
    gets it as FUNCA (compiled once and cached by the automation mod, values read through ScArgs); the MP snapshot's
    automation has no FUNCA, so the values are written into the code there."""
    PH = re.compile(r"\$([fis])(\d+)")
    def __init__(self, template, *args): self.template, self.args = template, list(args)
    def literal(self):
        def value(m):
            v, kind = self.args[int(m.group(2))], m.group(1)
            return repr(float(v)) + "f" if kind == "f" else str(int(v)) if kind == "i" else json.dumps(str(v))
        return self.PH.sub(value, self.template)
    def placeholders(self): return self.PH.sub(lambda m: f"ScTestAutomation131.ScArgs.{m.group(1).upper()}({m.group(2)})", self.template)
    def argv(self): return [repr(float(a)) if isinstance(a, float) else str(a) for a in self.args]


class Game:
    """One MP game process in its fixed role folder (server, client, client1, client2), driven through TestAutomation's
    loopback command and log ports. `name` labels its log in the run's case_dir; `role` defaults to the name."""
    engine = "1.9.3.2_MP"
    supports_args = False   # the snapshot's TestAutomation has no FUNCA
    def __init__(self, name, case_dir, mods, role=None):
        self.name, self.role = name, role or name
        RUNTIME_OWNER.acquire(case_dir)
        self.dir, self.hashes = self._prepare(mods)
        self.mods = self.hashes["mods"]
        self.case_dir = Path(case_dir)
        self.logsock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); self.logsock.bind(("127.0.0.1", 0)); self.logsock.settimeout(0.5)
        self.cmdsock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); self.cmdsock.bind(("127.0.0.1", 0))
        self.log_port, self.cmd_port, self.game_port = self.logsock.getsockname()[1], free_udp(), free_udp()
        self.lines, self.lock, self.stop, self.proc = [], threading.Lock(), False, None
        self.logfile = open(self.case_dir / f"{name}.log", "w", encoding="utf-8")
        RUNTIME_OWNER.games.append(self)
        threading.Thread(target=self._pump, daemon=True).start()

    def _prepare(self, mods): return RUNTIME_OWNER.prepare_role(self.role, mods)
    def engine_info(self): return {"engine": self.engine, "folder": str(self.dir), **self.hashes}

    def _pump(self):
        while not self.stop:
            try: data, _ = self.logsock.recvfrom(65536)
            except socket.timeout: continue
            except OSError: break
            text = data.decode("utf-8", "replace")
            with self.lock:
                for l in text.splitlines():
                    if l.strip(): self.lines.append(l)
                self.logfile.write(text); self.logfile.flush()

    def start(self):
        if self.proc and self.proc.poll() is None: raise RuntimeError(f"{self.role} is already running")
        self.proc = subprocess.Popen([str(self.dir / "Survivalcraft.exe"), "--log-port", str(self.log_port), "--cmd-port", str(self.cmd_port), "--game-port", str(self.game_port)], cwd=self.dir,
                                     env=dict(os.environ, SCCS_DIAGNOSTICS="1"))  # test diagnostics (KnifeLog.Diagnostics): [CS_MEM], [CS_PERF], spawn, net detail
        k, job = RUNTIME_OWNER.job
        if not k.AssignProcessToJobObject(job, int(self.proc._handle)): print(f"warning: {self.role} not in the job object", flush=True)
        self.t0 = time.time()

    def mark(self):
        with self.lock: return len(self.lines)

    def wait(self, texts, timeout=180, since=0):
        texts = [texts] if isinstance(texts, str) else texts; end = time.time() + timeout
        while time.time() < end:
            with self.lock:
                for i in range(since, len(self.lines)):
                    for t in texts:
                        if t in self.lines[i]: return t
            if self.proc and self.proc.poll() is not None: raise RuntimeError(f"{self.name} exited ({self.proc.returncode}) waiting for {texts}")
            time.sleep(0.3)
        raise TimeoutError(f"{self.name}: no {texts} within {timeout} s")

    def cmd(self, command, timeout=60):
        self.cmdsock.sendto(command.encode("utf-8"), ("127.0.0.1", self.cmd_port))
        if not command.startswith(("FUNC ", "EXEC ", "FUNCA ")): return None
        self.cmdsock.settimeout(timeout); data, _ = self.cmdsock.recvfrom(1 << 20); text = data.decode("utf-8", "replace")
        if text.startswith("OK:"): return json.loads(text[3:])
        raise RuntimeError(f"{self.name}: {command[:120]} -> {text[:800]}")

    def func(self, code, timeout=60):
        if isinstance(code, Call):
            if self.supports_args: return self.cmd("FUNCA " + json.dumps(code.argv()) + "\n" + code.placeholders(), timeout)
            code = code.literal()
        return self.cmd("FUNC " + code, timeout)

    def errors(self):
        with self.lock: return [l for l in self.lines if "[Error]" in l][:60]

    def net_lines(self):
        with self.lock: return [l for l in self.lines if "[ScCsgoNet]" in l]

    def close(self):
        if self.proc and self.proc.poll() is None: self.proc.kill(); self.proc.wait(20)
        self.stop = True; time.sleep(0.6); self.logsock.close(); self.cmdsock.close(); self.logfile.close()
        game_log = self.dir / "Bugs/Game.log"
        if game_log.exists(): shutil.copy2(game_log, self.case_dir / f"{self.name}-Game.log")
        RUNTIME_OWNER.active.discard(self.role)


class Game131(Game):
    """The isolated SurvivalcraftAPI 1.9.3.1 copy (standalone acceptance) in the fixed runtime's sp131 folder, driven by
    ScTestAutomation131 over the same loopback log/command protocol; the ports travel as environment variables because
    the 1.9.3.1 program takes no arguments. Same lock, job object and process accounting as the MP roles."""
    engine = "1.9.3.1"
    supports_args = True
    IN_WORLD = {"Player into playing.": 'Entered screen "Game"'}   # 1.9.3.1 has no player screen: NewWorld Play -> GameLoading -> Game

    def __init__(self, name, case_dir, mods): super().__init__(name, case_dir, mods, role=SP131)
    def _prepare(self, mods): return RUNTIME_OWNER.prepare_131(mods)

    def start(self):
        if self.proc and self.proc.poll() is None: raise RuntimeError(f"{self.role} is already running")
        env = dict(os.environ, SC_TEST_CMD_PORT=str(self.cmd_port), SC_TEST_LOG_PORT=str(self.log_port), SCCS_DIAGNOSTICS="1")
        self.proc = subprocess.Popen([str(self.dir / "Survivalcraft.exe")], cwd=self.dir, env=env)
        k, job = RUNTIME_OWNER.job
        if not k.AssignProcessToJobObject(job, int(self.proc._handle)): print(f"warning: {self.role} not in the job object", flush=True)
        self.t0 = time.time()

    def wait(self, texts, timeout=180, since=0):
        texts = [texts] if isinstance(texts, str) else texts
        return super().wait([self.IN_WORLD.get(t, t) for t in texts], timeout, since)


def game(name, case_dir, mods):
    """A game process for the selected engine (M0_ENGINE): the MP snapshot with its TestAutomation mod, or the isolated
    1.9.3.1 copy with ScTestAutomation131. `mods` are the packages under test; the automation mod is added here."""
    if ENGINE == "131":
        if not TA131.exists(): raise RuntimeError(f"{TA131} missing: run m0.py ta131 first")
        return Game131(name, case_dir, [TA131] + list(mods))
    if ENGINE != "mp": raise ValueError(f"M0_ENGINE={ENGINE!r}: expected mp or 131")
    return Game(name, case_dir, [MODS / TA_BUILT.name] + list(mods))


PLAYER_SPAWNED = ('var project = Game.GameManager.Project; if (project == null) return "no project"; var players = project.FindSubsystem<Game.SubsystemPlayers>(true); '
                  'return (players.PlayersData.Count > 0 && players.PlayersData[0].ComponentPlayer != null).ToString();')
SURVIVAL_MODE = 'if (ScreensManager.CurrentScreen is not NewWorldScreen s || s.m_worldSettings == null) return "not-new-world"; s.m_worldSettings.GameMode = GameMode.Survival; return s.m_worldSettings.GameMode.ToString();'
def enter_world(g):
    """Main menu -> new survival world -> playing, on either engine (the MP snapshot shows its player screen first)."""
    mp = g.engine.startswith("1.9.3.2")
    if mp: g.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
    m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
    m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
    poll(g, SURVIVAL_MODE, lambda v: v == "Survival", 20)
    # Both engines show the player screen for a new world (1.9.3.1: SubsystemPlayers switches to it while it has no player;
    # its PlayButton adds the player data and returns to the Game screen, the entity spawns once its chunk is ready).
    m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
    m = g.mark(); g.cmd("CLICK_WIDGET PlayButton")
    if mp: g.wait("Player into playing.", 600, m)
    else:
        g.wait('Entered screen "Game"', 600, m)
        spawned = poll(g, PLAYER_SPAWNED, lambda v: v == "True", 240)
        if spawned != "True": raise RuntimeError("1.9.3.1: no player entity after the player screen: " + str(spawned))


PROBE = 'return Game.ScNetProbe.Decision + " || " + string.Join(" / ", Game.ScNetProbe.Evidence);'
BRIDGE = 'var b = Game.ScNetProbe.Bridge; return b == null ? "no bridge" : b.Role + " | remote " + b.RemoteClients + " | " + b.Handshake + " | " + b.HandshakeDetail;'
HANDSHAKE = 'return Game.ScNetProbe.Bridge == null ? "none" : Game.ScNetProbe.Bridge.Handshake.ToString();'
LAST = 'return Game.ScNetProbe.Bridge == null ? "" : Game.ScNetProbe.Bridge.LastStatus;'
IMAGES = ('return string.Join("; ", ModsManager.ModList.Where(m => m.modInfo != null && m.modInfo.PackageName == "zh667.ScCsgoKnives")'
          '.SelectMany(m => m.GetAssemblyImages()).Select(i => i.FileName + (i.IsModified ? " MODIFIED by a preload mod" : " unmodified")));')
SNAPSHOT = ('var p = GameManager.Project; var a = p.FindSubsystem<Game.SubsystemScArmor>(false); '
            'return string.Join(" | ", p.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers.Select(c => { var inv = c.ComponentMiner.Inventory; '
            'return c.PlayerData.PlayerIndex + " hp=" + c.ComponentHealth.Health.ToString("R") + " armor=" + (a == null ? "none" : a.Get(Game.SubsystemScArmor.PlayerKey(c.PlayerData.PlayerIndex)).Encode()) '
            '+ " inv=" + string.Join(",", Enumerable.Range(0, inv.SlotsCount).Select(i => inv.GetSlotValue(i) + "x" + inv.GetSlotCount(i))); }));')
SUBSYSTEMS = ('var p = GameManager.Project; if (p == null) return "no project"; '
              'return string.Join(", ", new[] { "SubsystemScArmor", "SubsystemScTactical", "SubsystemTacticalEnemies", "SubsystemTacticalCompanions" }'
              '.Select(n => n + "=" + p.Subsystems.Any(s => s.GetType().Name == n)));')


def to_menu(g):
    g.start(); g.wait('Entered screen "MainMenu"', 600)


def create_world(g):
    g.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
    m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
    m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
    m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
    m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait("Player into playing.", 600, m)


# TEST HARNESS ONLY, never shipped: 1.9.3.2_MP's built-in CommandCompatNetAdapter (the compatibility layer for the zh.command
# mod, registered even when that mod is absent) broadcasts every player's state from the server every 0.01 s, and a client
# receiving it skips its own ComponentPlayer/ComponentBody/ComponentHealth/ComponentVitalStats/ComponentLocomotion updates
# for 2 s - so on the unmodified platform a client never walks, never processes use/interact/hit/dig input and never
# sends its slot changes (evidence: mp_vanilla.py, no CS package). Our layer is tested against the platform as it is meant
# to behave: the test server's broadcast timer is held, nothing else is touched. The platform defect is reported apart.
HOLD_COMMAND_SNAPSHOTS = ('var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => { try { return a.GetTypes().FirstOrDefault(x => x.Name == "CommandCompatNetRuntime"); } catch { return null; } }).FirstOrDefault(x => x != null); '
    'if (t == null) return "no CommandCompatNetRuntime"; '
    'var f = t.GetField("s_stateBroadcastTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); if (f == null) return "no timer field"; '
    'if (System.AppDomain.CurrentDomain.GetData("sc-hold-command-snapshots") == null) { System.AppDomain.CurrentDomain.SetData("sc-hold-command-snapshots", 1); Engine.Window.Frame += () => f.SetValue(null, 1e9f); } '
    'f.SetValue(null, 1e9f); return "held";')
KEEP_ACTIVE_CLIENT = ('var windowType = typeof(Engine.Window); var stateType = windowType.GetNestedType("State", System.Reflection.BindingFlags.NonPublic); '
    'var field = windowType.GetField("m_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); var active = System.Enum.ToObject(stateType, 2); field.SetValue(null, active); '
    'if (System.AppDomain.CurrentDomain.GetData("sc-keep-active") == null) { System.AppDomain.CurrentDomain.SetData("sc-keep-active", 1); '
    'Engine.Window.Deactivated += () => field.SetValue(null, active); Engine.Window.Frame += () => field.SetValue(null, active); } return Engine.Window.IsActive;')
# Round 3 (post-mp-bugs-20260930 item 4): the platform is taken as it is. The product's own adapter now carries the
# freeze workaround and the late-join terrain reconciliation, so the acceptance runs use no test-side compensation. The
# hold below stays only for the platform A/B (mp_vanilla.py, HOLD explicitly requested).
PLATFORM_AS_DESIGNED = False


def join(client, server):
    if PLATFORM_AS_DESIGNED: server.func(HOLD_COMMAND_SNAPSHOTS)
    m = client.mark()
    client.cmd(f'EXEC ScreensManager.SwitchScreen("GameLoading", null, null, System.Net.IPEndPoint.Parse("127.0.0.1:{server.game_port}"), null);')
    client.wait(f"Connected: 127.0.0.1:{server.game_port}", 300, m)
    client.wait('Entered screen "Player"', 600, m)
    m = client.mark(); client.cmd("CLICK_WIDGET PlayButton"); client.wait("Player into playing.", 600, m)
    client.func(KEEP_ACTIVE_CLIENT)


def poll(g, code, done, timeout=40):
    end = time.time() + timeout; value = None
    while time.time() < end:
        value = g.func(code)
        if done(value): return value
        time.sleep(0.5)
    return value


def runmp(case):
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"mp-{case}-{stamp}"; case_dir.mkdir(parents=True)
    ta = MODS / TA_BUILT.name
    test = {"handshake": "lite", "timeout": "lite", "full": "full"}[case]
    with_net = PKG / f"test-{test}-c15+netM0.scmod"
    server_mods = [ta, LITE] if case == "timeout" else [ta, with_net]
    R = {"case": case, "mpBuild": sha(MPBIN / "Survivalcraft.dll"), "steps": []}
    def step(name, value): R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True)
    T0 = time.time(); server = client = None
    try:
        server = Game("server", case_dir, server_mods); R["serverMods"] = server.mods
        to_menu(server); step("server menu", round(time.time() - server.t0, 1))
        step("server probe", server.func(PROBE) if case != "timeout" else "plain c15 package (no probe)")
        create_world(server); step("server in world", round(time.time() - server.t0, 1))
        if case != "timeout":
            step("server bridge (MP local world, no client yet)", server.func(BRIDGE))
            step("server core assembly images", server.func(IMAGES))
        if case == "full": step("server CS subsystems", server.func(SUBSYSTEMS))
        client = Game("client", case_dir, [ta, with_net]); R["clientMods"] = client.mods
        to_menu(client); step("client menu", round(time.time() - client.t0, 1))
        step("client probe", client.func(PROBE))
        join(client, server); step("client in world", round(time.time() - client.t0, 1))
        hs = poll(client, HANDSHAKE, lambda v: v not in ("Pending", "NotApplicable"), 40)
        step("client bridge after join", client.func(BRIDGE))
        if case != "timeout": step("server bridge after join", server.func(BRIDGE))
        if case == "full": step("client CS subsystems", client.func(SUBSYSTEMS))
        step("client core assembly images", client.func(IMAGES))
        if case != "timeout" and hs == "Accepted":
            before = server.func(SNAPSHOT); step("server state before status request", before)
            rid = client.func("return Game.ScNetProbe.Bridge.RequestStatus();"); step("client status request id", rid)
            step("client status answer", poll(client, LAST, lambda v: f'"Request":{rid},' in (v or ""), 20))
            after = server.func(SNAPSHOT); step("server state after status request", after)
            R["sideEffectFree"] = before == after; step("server state unchanged", before == after)
            m = server.mark(); client.func(f"return ((Game.ScCsgoNetAdapter)Game.ScNetProbe.Bridge).RequestStatusForTest({rid});")
            step("repeat of the same request id", server.wait("is a repeat", 20, m))
            if case == "handshake":
                client.func("return ((Game.ScCsgoNetAdapter)Game.ScNetProbe.Bridge).SendHelloForTest(5);")
                step("hello claiming item layout 5", poll(client, BRIDGE, lambda v: "Rejected" in v, 20))
                step("local status request while rejected", client.func("return Game.ScNetProbe.Bridge.RequestStatus();"))
                m = server.mark(); client.func("return ((Game.ScCsgoNetAdapter)Game.ScNetProbe.Bridge).RequestStatusForTest(900);")
                step("forced status request while rejected (server)", server.wait("refused (no accepted handshake)", 20, m))
                step("forced status request while rejected (client)", poll(client, LAST, lambda v: '"Request":900,' in (v or ""), 20))
                client.func("return ((Game.ScCsgoNetAdapter)Game.ScNetProbe.Bridge).SendHelloForTest(-1);")
                step("compatible hello again", poll(client, BRIDGE, lambda v: "Accepted" in v, 20))
                rid = client.func("return Game.ScNetProbe.Bridge.RequestStatus();")
                step("status after re-acceptance", poll(client, LAST, lambda v: f'"Request":{rid},' in (v or ""), 20))
        R["handshake"] = client.func(HANDSHAKE)
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        for g in [client, server]:
            if g is None: continue
            R[g.name] = {"errors": g.errors(), "net": g.net_lines(), "logLines": g.mark(), "exit": g.proc.poll() if g.proc else None}
            g.close()
        RESULTS.mkdir(parents=True, exist_ok=True)
        (RESULTS / f"mp-{case}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
        for g in ["server", "client"]:
            if (case_dir / f"{g}.log").exists(): shutil.copy2(case_dir / f"{g}.log", RESULTS / f"mp-{case}-{g}.log")
            shutil.rmtree(case_dir / g, ignore_errors=True)
    print(json.dumps({k: v for k, v in R.items() if k not in ("server", "client")}, ensure_ascii=False, indent=1)[:15000])
    for g in ["server", "client"]:
        if g in R: print(g, "errors", len(R[g]["errors"]), R[g]["errors"][:8], "\n", g, "net", R[g]["net"][:30])
    return 0 if "failure" not in R else 1


if __name__ == "__main__":
    step = sys.argv[1]
    if step == "prepare": prepare()
    elif step == "abi": abi()
    elif step == "build": build()
    elif step == "run131": run131(sys.argv[2])
    elif step == "runmp": sys.exit(runmp(sys.argv[2]))
    elif step == "devbuild": devbuild(sys.argv[2])
    elif step == "devpkg": devpkg(sys.argv[2])
    elif step == "sppkg": sppkg(sys.argv[2])
    elif step == "candpkg": candpkg(sys.argv[2])
    elif step == "tapatch": tapatch()
    elif step == "ta131": ta131()
    elif step == "preflight":
        case = RUNS / time.strftime("preflight-%Y%m%d-%H%M%S"); case.mkdir(parents=True)
        try: RUNTIME_OWNER.acquire(case); print(json.dumps(RUNTIME_OWNER.record, ensure_ascii=False, indent=1)); RUNTIME_OWNER.release()
        except (PreflightError, RuntimeBusy) as e: print("PREFLIGHT FAILED:", e); sys.exit(2)
    else: raise SystemExit(__doc__)
