"""Isolated candidates for r2-c4-completion-20260929 (R2 postures/NPC/third party, C4 planting, natural footing,
thin trajectory), headshot-armor-balance-20260929 and current-direction-20260929 (numeric protection, three enemy
configurations, natural encounters), on top of the 1.4.0 packages currently in output/ (c08).

Usage (Windows, via tools/dev.ps1): python tools/completion_140.py <tag> <step>[,<step>...]|all
Same stage layout and steps as tools/video_fix_140.py (and followup_140.py), under .tmp/completion-140-20260929/<tag>.
Never writes output/, Mods or worlds; third-party packages are only read.

Added gates:
  c4        the C4 checkset (--c4-checkset) on the Full and the Lite core
  main      the default PackageCheck suite on the Full and the Lite core
  ai        the tactical AI set only (Full, and Lite with the agents package): the quick gate for enemy/armour work
  compat    family matrix (1.0.0/1.2.0 readers and the delivered 1.3.0 core), native hooks, Lite world resources,
            inventory and the Full/Lite integration matrix
  baseline  the same test tools (built from this stage's tree) against the unchanged output/ packages, so a new
            regression is shown to reproduce the old defect and pre-existing failures are known, not hidden

Reuse (current-direction-20260929 §3): the clip bake and the baseline runs are keyed by the hash of their complete
inputs (sources, tools, pinned resource files, package bytes) and kept under .tmp/completion-140-20260929/cache; an
unchanged key copies the earlier result instead of re-running, a changed one runs and replaces it (two keys at most).
A failed prepare, build or package step stops the steps after it.
"""
import hashlib, json, os, sys, zipfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import video_fix_140 as vf
base = vf.base

base.BASE = ROOT / ".tmp/completion-140-20260929"
# The 1.4.0 packages in output/: r7b (memory-residency-20261001, phone-corrected: package copies and Assets streams replaced
# by re-readable sources after loading, decoded texture pixels really freed, Android-safe install, [CS_MEM] logs), delivered
# 2026-10-01. r7a was 9f1df6c9 / a96805ec / 0dea4191; r6x was e0ccfff9 / 4c0d81d0 / 0dea4191; r5x 6652b73f / cfbefd6c / 8caf6714.
# (dma2, deathmatch-addon, delivered 2026-10-03 as a candidate with the new deathmatch package 3c4bdc99 beside the three;
# mpf1 was 37cda147 / 902cbb6f / ad5c7b85, mpe1 46befae3 / ee3e38eb / 8443c77b, mpd2 7239fc30 / 72de5c42 / a1b7602d,
# mpc3 e15a6052 / 41674d3a / 9c09b521, mpb dc714993 / 1dd379bd / d0a49c56.)
# Now dmr6f (deathmatch-addon round 6, delivered 2026-10-05 with the deathmatch package 9c05fb7c: the earlier muzzle flash back
# at the user's request, dmr6d's muzzle sheets retired); dmr6d (a0864d6f: CS2's muzzle particle systems) was 62294248 /
# fe9abd3e / 4bf17317; dmr6a (543ecdc5: CS2's penetration in deathmatch worlds) was daecca83 / 1e224c4d /
# 3ee77806; dmr5b was 81714602 / e6b66afd / 62ecbdf2 (deathmatch 6e42ae06: CS2's spray patterns,
# hitboxes and running speeds in deathmatch worlds; the core's mode hooks Recoil / HitCapsules and the Neck / Stomach
# regions); dmr4a was a96c44a1 / 186a9421 / e811169c (deathmatch 0ebb4d0f: the CS armour HUD as CS2's readout); dmr3g was 11c4148b / 4f2cb698 / ae174abb (deathmatch 0e56aae4), sw2 3347c64c / f9551477 / d478349e (deathmatch
# 312125cd), dma3/dma2 6141ac01 / 408d9f35 / e804b11c.
# Every official release from 1.4.0 on, as delivered (AGENTS: bidirectional save switching against each; the compat gate
# reads their cores): the 1.4.0 family (dmr6f, now output/history-1.4.0/) and the 1.5.0 Full of 2026-10-06 12:18
# (tools/release_full_150.py; output/history-1.5.0/<sha256>/). A release is looked up in output/, output/history-<version>/
# and output/history-<version>/<sha256>/ by its package name.
OFFICIAL_RELEASES = [
    ("1.4.0", {"全量": "ec949ff420c509b58935e1203bd8582381f7790107f9a51aa0e35d884ff74b94", "轻量": "b9598e861ff7e3a39196086d10944b6158a9cb89a712b5cacbd984ca0e10d5fe"}),
    ("1.5.0", {"全量": "7448e3400e68f6a22d08b057123c24eddc4a61320f78921c84b1cd300015a01f"}),
]
# The 1.5.0 family in output/ (r150a, delivered 2026-10-06 22:50: the whole main d56c607 - the 1.5.0 Full's survival/squad
# feedback and flash fixes, the sub-world airdrops with their assets, the deathmatch UI with CS2 icons, the tri tracers).
# The 1.4.0 family (dmr6f: ec949ff4 / b9598e86 / 23f7a65d, deathmatch 9c05fb7c) is in output/history-1.4.0/.
base.BASELINES = {
    "全量": "fb513611e132017c6dc8c94cc42182067c829bc8d968caf0ac3374a91f55b3e4",
    "轻量": "dff871b1b027c4b0e12899958478b215dbeccaaefbe0093a676883c9cf85b007",
    "探员": "20fa0a05bfa5d68dfe1375811e4d57d11627dd46b8563ce58d835a60e9d38482",
}
# The S0 regression reads the real Slower Creature Spawns package, read-only. The user removed it from the installed
# Mods folder; an identical copy (same SHA-256 as the one diagnosed) stays in the download folder.
SLOWER_SHA = "101886580c11f5e7167c975765a0b0318da7b42b9ba63a4abeb788cae5c2965b"
for candidate in [base.GAME / "Mods/[1SlowerCreatureSpawnsMod.scmod", Path(r"D:\下载\Mods\[1SlowerCreatureSpawnsMod.scmod")]:
    if candidate.exists() and hashlib.sha256(candidate.read_bytes()).hexdigest() == SLOWER_SHA:
        os.environ["SC_SLOWER_CHECK_PACKAGE"] = str(candidate); break
else:
    os.environ["SC_SLOWER_CHECK_PACKAGE"] = str(base.GAME / "Mods/[1SlowerCreatureSpawnsMod.scmod")  # the test reports it missing
# Third-party character models for the third-person checks (read-only).
os.environ["SC_NMM_MODEL_PACKAGE"] = str(base.GAME / "Mods/[API1.9]NekoMeko Model-v1.1.scmod")

# mpc3-feedback-subworld-20261002: the sub-world provider the user plays with (AncientWorld 0.41.16), read in memory by
# PackageCheck's ItemTravelRegression (its own inventory snapshot code, as shipped and with the three bridge calls).
# Never unpacked, written or repackaged. Without it the default suite fails: that check is part of this stage's evidence.
ANCIENT_PACKAGE = Path(os.environ.get("SC_ANCIENT_PACKAGE", r"D:\下载\AncientWorld_v0.41.16.zip"))
ANCIENT_SHA = "e88b68f6edaf53b419b89db24ed8dd487734bf0aff0830af30824145f0977a66"
def ancient_package():
    ok = ANCIENT_PACKAGE.is_file() and hashlib.sha256(ANCIENT_PACKAGE.read_bytes()).hexdigest() == ANCIENT_SHA
    print("AncientWorld 0.41.16 package", "found" if ok else "NOT found (or another file) at", ANCIENT_PACKAGE, flush=True)
    if ok: os.environ["SC_ANCIENT_PACKAGE"] = str(ANCIENT_PACKAGE)
    else: os.environ.pop("SC_ANCIENT_PACKAGE", None)
    return ok

from pack_single_scmods import raw_member, write_archive
# headshot-armor-balance-20260929 H4: CS2 hit sounds imported by tools/import_cs2_hit_sounds.py, pinned by its record.
# They are new members of both core packages (Full and Lite); the agents package carries no core code and gets none.
SOUND_RECORD = ROOT / "docs/tasks/headshot-armor-balance-20260929-sounds.json"
# current-direction-20260929: protection is numbers only; the c10 armour item assets are no core member any more.
# The helmet dink (CS2 headshot_armor_e1), the helmet spark atlas and the protection HUD icons (per edition: Full PNG,
# Lite WebP) come from tools/import_cs2_armor_feedback.py; the older headshot_armor.ogg (headshot_armor_01, another CS2
# event layer) is retired from both core packages, member and resource-marker line.
FEEDBACK_RECORD = ROOT / "docs/tasks/current-direction-20260929-armor-feedback-assets.json"
# round9-tp-aim-cs2-tracer-hud-20261001: the tracer streak's CS2 colour lookups baked into copies of the streak textures and
# the sniper wisp's two rope textures (tools/import_cs2_tracer_round9.py; Full PNG, Lite WebP).
TRACER_RECORD = ROOT / "docs/tasks/round9-tp-aim-cs2-tracer-hud-20261001-assets.json"
# deathmatch round 4 (2026-10-04): CS2's armour HUD badges - the shield and the shield with the helmet on it - in CS2's
# own colours (tools/import_cs2_armor_hud.py; Full PNG, Lite WebP); the CS armour HUD shows them as CS2 does.
ARMOR_HUD_RECORD = ROOT / "docs/tasks/armor-hud-cs2-20261004-assets.json"
# (deathmatch round 6 R6-5's CS2 muzzle sheets, docs/tasks/muzzle-fx-cs2-20261005-assets.json, shipped once in dmr6d and are no
# core member since dmr6e: the user asked for the earlier muzzle flash back; the runtime is in tools/archive/muzzle-fx-r6-5.)
CORE_RECORDS = [SOUND_RECORD, FEEDBACK_RECORD, TRACER_RECORD, ARMOR_HUD_RECORD]
RETIRED = {"Assets/Audio/ScCsgoKnives/Hits/headshot_armor.ogg"}
# dmr6e: the packages are built on the delivered ones, so dmr6d's CS2 muzzle sheets would stay as unused members; they are
# retired from both core packages and from the resource marker (the members their import record names).
RETIRED |= {r["member"] for r in json.loads((ROOT / "docs/tasks/muzzle-fx-cs2-20261005-assets.json").read_text("utf8"))}
CACHE = base.BASE / "cache"

def tool(S, name): return S / "tree/tools" / name / "bin/Release/net10.0" / (name + ".dll")
def report(S, key, failed_key="failed"):
    p = S / f"{key}.json"
    if not p.exists(): print(key, "no report"); return None
    d = json.loads(p.read_text("utf-8-sig")); checks = d.get("checks", [])
    bad = [c.get("name", c.get("Name")) for c in checks if not c.get("ok", c.get("Ok"))]
    print(key, "failed", len(bad), "of", len(checks), bad[:16], flush=True); return set(bad)

# mp-state-consistency-20261002: the Sushi packages the user plays with (玲兰辅助 v3.0.3 = SushiBase, 玲兰科技 v3.0.4 = SushiTool),
# read from the user's 1.9.3.1 Mods folder, never written. The C4 check set then also runs the inventory, shared-channel
# and stacking checks against those assemblies (the JSON's sushi-inventory/actual-dlls line records their SHA-256).
SUSHI_MODS = Path(os.environ.get("SC_SUSHI_MODS", r"D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods"))
def sushi_arguments():
    found = SUSHI_MODS.is_dir() and any("玲兰" in p.name for p in SUSHI_MODS.glob("*.scmod"))
    print("sushi packages", "found in" if found else "NOT found in", SUSHI_MODS, flush=True)
    return ["--sushi-inventory-mods", SUSHI_MODS] if found else []

def c4(S):
    """The C4 check set (with who hears a plant in multiplayer), and the Sushi inventory checks when its packages are there.
    Without the Sushi packages the step fails: those checks are part of this stage's evidence, not optional."""
    pk = json.loads((S / "packages.json").read_text("utf8")); ok = True
    sushi = sushi_arguments(); ok &= bool(sushi)
    for key, core in [("c4-full", S / "candidate" / pk["全量"]["file"]), ("c4-lite", S / "candidate" / pk["轻量"]["file"])]:
        ok &= base.run(S, key, ["dotnet", tool(S, "PackageCheck"), "--scmod", core, "--c4-checkset", *sushi, "--json", S / f"{key}.json"]); report(S, key)
        if (S / f"{key}.json").exists():
            names = [c["name"] for c in json.loads((S / f"{key}.json").read_text("utf-8-sig"))["checks"]]
            counts = {prefix: sum(n.startswith(prefix) for n in names) for prefix in ["c4/", "c4-net-sound/", "sushi-inventory/", "sushi-sync/", "sushi-stacking/"]}
            print(key, "checks by set", counts, flush=True)
            ok &= counts["c4-net-sound/"] >= 8 and (not sushi or (counts["sushi-sync/"] >= 20 and counts["sushi-stacking/"] >= 12))
    return ok

def main(S):
    """The default suite. Failures that the unchanged output packages also show (see baseline) are reported as known."""
    pk = json.loads((S / "packages.json").read_text("utf8")); content = base.GAME / "Content.zip"; ok = True
    known = set()
    for key in ["baseline-main-full", "baseline-main-lite"]:
        if (S / f"{key}.json").exists(): known |= report(S, key) or set()
    ancient = ancient_package(); ok &= ancient
    for key, core in [("main-full", S / "candidate" / pk["全量"]["file"]), ("main-lite", S / "candidate" / pk["轻量"]["file"])]:
        base.run(S, key, ["dotnet", tool(S, "PackageCheck"), "--scmod", core, "--vanilla-content", content, "--json", S / f"{key}.json"])
        bad = report(S, key)
        if bad is None: ok = False; continue
        new = sorted(bad - known); print(key, "new failures (not in the baseline)", new, flush=True); ok &= not new
        # The travel core's checks, and the provider's real assembly among them (not skipped).
        checks = json.loads((S / f"{key}.json").read_text("utf-8-sig"))["checks"]
        travel = [c for c in checks if c["name"].startswith("item-travel/")]; real = [c for c in travel if c["name"].endswith("ancient-world-0.41.16-real-assembly")]
        ran = bool(real) and real[0]["ok"] and not real[0]["detail"].startswith("NOT RUN")
        print(key, "item-travel checks", len(travel), "failed", [c["name"] for c in travel if not c["ok"]], "| provider assembly:", real[0]["detail"][:400] if real else "no result", flush=True)
        ok &= len(travel) >= 20 and all(c["ok"] for c in travel) and ran
        # mpd2-ammo-jitter-20261002: the shot schedule in single player (ShotCadenceRegression), required by name: the
        # delivered build fails five of these, so the baseline must never excuse them.
        cadence = [c for c in checks if c["name"].startswith("shot-cadence/")]
        print(key, "shot-cadence checks", len(cadence), "failed", [c["name"] for c in cadence if not c["ok"]], flush=True)
        for c in cadence: print("  ", c["name"], "::", c["detail"].splitlines()[0][:200], flush=True)
        ok &= len(cadence) >= 14 and all(c["ok"] for c in cadence)
        # quick-throw-20261002: letting go throws at once (QuickThrowRegression, and the two timeline items of
        # InteractionRegression that replaced "early release waits for the pin"), required by name: the delivered build
        # fails the tap items, so the baseline must never excuse them.
        quick = [c for c in checks if c["name"].startswith("quick-throw/") or c["name"].startswith("interaction/early-release-throws-at-once/")
                 or c["name"].startswith("interaction/release-during-the-draw-waits-for-the-draw-only/")]
        print(key, "quick-throw checks", len(quick), "failed", [c["name"] for c in quick if not c["ok"]], flush=True)
        for c in quick:
            if c["name"].startswith("quick-throw/") and "/grenade_" not in c["name"]: print("  ", c["name"], "::", c["detail"].splitlines()[0][:260], flush=True)
        ok &= len(quick) >= 49 + 24 and all(c["ok"] for c in quick)
    return ok

def tree_hash(S, *parts):
    """Hash of every file under the stage tree paths (relative names and bytes), in a stable order."""
    h = hashlib.sha256()
    for part in parts:
        root = S / "tree" / part
        files = [root] if root.is_file() else sorted(p for p in root.rglob("*") if p.is_file() and not {"bin", "obj", "__pycache__"} & set(p.parts))
        for f in files: h.update(f.relative_to(S / "tree").as_posix().encode() + b"\0" + hashlib.sha256(f.read_bytes()).digest())
    return h.hexdigest()

def cached(kind, key, produce, outputs, S):
    """Copies the outputs (stage-relative) saved under cache/<kind>-<key> into S, or runs produce() and saves them."""
    import shutil
    slot = CACHE / f"{kind}-{key[:16]}"; record = slot / "record.json"
    if record.exists() and json.loads(record.read_text("utf8")).get("key") == key:
        for rel in json.loads(record.read_text("utf8"))["outputs"]:
            src, dst = slot / "files" / rel, S / rel; dst.parent.mkdir(parents=True, exist_ok=True)
            (shutil.copytree(src, dst, dirs_exist_ok=True) if src.is_dir() else shutil.copyfile(src, dst))
        print(kind, "reused", key[:16], "from", json.loads(record.read_text("utf8"))["from"], flush=True); return True
    ok = produce()
    if ok is False: return False
    for old in sorted(CACHE.glob(kind + "-*"), key=lambda p: p.stat().st_mtime)[:-1]: shutil.rmtree(old)  # at most two keys
    if slot.exists(): shutil.rmtree(slot)
    saved = []
    for rel in outputs:
        src = S / rel
        if not src.exists(): continue
        dst = slot / "files" / rel; dst.parent.mkdir(parents=True, exist_ok=True)
        (shutil.copytree(src, dst) if src.is_dir() else shutil.copyfile(src, dst)); saved.append(rel)
    base.dump(record, dict(key=key, outputs=saved, **{"from": S.name})); print(kind, "cached", key[:16], flush=True)
    return ok

def baseline(S):
    """This stage's test tools against the unchanged output/ packages (reproduction and known failures). Reused when
    the packages and the test tool sources are the same as the saved run."""
    content = base.GAME / "Content.zip"
    full, lite, agents = base.baseline("全量"), base.baseline("轻量"), base.baseline("探员")
    for label, p in [("全量", full), ("轻量", lite), ("探员", agents)]:
        assert hashlib.sha256(p.read_bytes()).hexdigest() == base.BASELINES[label], f"output {label} changed"
    runs = [("baseline-vf-full", ["--scmod", full, "--video-feedback-checkset", "--vanilla-content", content]),
            ("baseline-ai-full", ["--scmod", full, "--tactical-package", full, "--tactical-ai-only", "--vanilla-content", content]),
            ("baseline-c4-full", ["--scmod", full, "--c4-checkset"]),
            ("baseline-main-full", ["--scmod", full, "--vanilla-content", content]),
            ("baseline-main-lite", ["--scmod", lite, "--vanilla-content", content])]
    def produce():
        for key, args in runs: base.run(S, key, ["dotnet", tool(S, "PackageCheck"), *args, "--json", S / f"{key}.json"])
        return True
    key = hashlib.sha256(json.dumps([base.BASELINES, tree_hash(S, "tools/PackageCheck"), os.environ.get("SC_SLOWER_CHECK_PACKAGE", "")]).encode()).hexdigest()
    cached("baseline", key, produce, [f"{k}.json" for k, _ in runs] + [f"logs/{k}.log" for k, _ in runs], S)
    for key, _ in runs: report(S, key)
    return True

CLIP_INPUTS = ["tools/actor_air_clips.py", "tools/actor_throw_clips.py", "tools/prepare_actor_geometry.py", "tools/ActorLoadCheck", "tools/video_fix_140.py", "tools/followup_140.py",
               "src/ScCsgoTactical/Assets/Animations/ScTactical.json", "src/ScCsgoTactical/Assets/Animations/ScTacticalHostage.json"]
CLIP_OUTPUTS = ["clips.json"] + [f"resources/{e}/Assets/Animations/{n}" for e in ["full", "lite"] for n in ["ScCsgoTactical/ct.scanim", "ScCsgoTactical/t.scanim", "ScTactical.json", "ScTacticalHostage.json"]]

def clip_key(S):
    h = hashlib.sha256()
    for f in [base.DENSE / "ct.glb", base.DENSE / "t.glb", base.EXPORTS["ct"], base.EXPORTS["t"]]: h.update(f.name.encode() + hashlib.sha256(f.read_bytes()).digest())
    h.update(tree_hash(S, *CLIP_INPUTS).encode()); return h.hexdigest()

def clips(S):
    """The R2 clip bake (video_fix_140.clips), reused while its inputs are unchanged: the clip tools, the actor loader,
    the pipeline code, the animation configs and the pinned dense actors and CS2 exports. Without a cached result an
    earlier stage of this task whose inputs hash the same (and that baked) supplies it; otherwise the bake runs.
    The work directory (dense actors, bake reports) is not kept: later steps read only the staged resources."""
    import shutil
    key = clip_key(S)
    def produce():
        for earlier in sorted((p for p in base.BASE.iterdir() if p.is_dir() and p != S and p.name != "cache" and (p / "tree").exists()), key=lambda p: p.stat().st_mtime, reverse=True):
            if all((earlier / o).exists() for o in CLIP_OUTPUTS) and clip_key(earlier) == key:
                for o in CLIP_OUTPUTS: (S / o).parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(earlier / o, S / o)
                print("clips reused from stage", earlier.name, "(same inputs)", key[:16], flush=True); return True
        return vf.STEPS["clips"](S)
    return cached("clips", key, produce, CLIP_OUTPUTS, S)

def ai(S):
    """The tactical AI set only (Full; Lite with the agents package): the quick gate while enemy/armour work iterates."""
    pk = json.loads((S / "packages.json").read_text("utf8")); content = base.GAME / "Content.zip"; c = lambda l: S / "candidate" / pk[l]["file"]; ok = True
    for key, core, addon in [("ai-full", c("全量"), c("全量")), ("ai-lite", c("轻量"), c("探员"))]:
        ok &= base.run(S, key, ["dotnet", tool(S, "PackageCheck"), "--scmod", core, "--tactical-package", addon, "--tactical-ai-only", "--vanilla-content", content, "--json", S / f"{key}.json"]); report(S, key)
    return ok

def gates(S):
    """Every gate after packaging: AI/split/native, video-feedback set, tactical suite, C4 set, default suite."""
    a = vf.checks(S); b = c4(S); m = main(S)
    return a and b and m

def core_members(S):
    """Adds the pinned core resources (hit sounds, armour assets) to the Full and Lite candidates after the ordinary package step."""
    missing = [p.name for p in CORE_RECORDS if not p.exists()]
    if missing: print("core resources: no import record", missing, flush=True); return False
    records = [r for p in CORE_RECORDS for r in json.loads(p.read_text("utf8")) if r["member"] not in RETIRED]; pk = json.loads((S / "packages.json").read_text("utf8"))
    staged = S / "resources/core"
    for r in records:
        data = (ROOT / r["target"]).read_bytes(); assert base.sha(data) == r["sha256"], "core resource changed since its import: " + r["target"]
        dst = staged / r.get("edition", "both") / r["member"]; dst.parent.mkdir(parents=True, exist_ok=True); dst.write_bytes(data)
    for label in ["全量", "轻量"]:
        path = S / "candidate" / pk[label]["file"]
        with zipfile.ZipFile(path) as z:
            entries = {i.filename: (i.compress_type, i.CRC, i.file_size, raw_member(z, i)) for i in z.infolist()}
            hashes = {n: base.sha(z.read(n)) for n in z.namelist()}
        edition = "full" if label == "全量" else "lite"
        todo = [r for r in records if r.get("edition", "both") in (edition, "both") and not (r["member"] in hashes and hashes[r["member"]] == r["sha256"])]
        manifest = "Assets/ScCompatibilityManifest.xml"; manifest_text = None
        if manifest in hashes:
            with zipfile.ZipFile(path) as z: old = z.read(manifest).decode("utf8")
            new = components_with(old, (S / "tree/src/ScCsgoKnives" / manifest).read_text("utf8"))
            if new != old: manifest_text = new
        retire = sorted(RETIRED & set(hashes))
        # tactical assets the package step put into the Full (followup_140.tactical_assets: the airdrop model and textures) are
        # resources too: the core's marker must list them, or main's "standalone/resource-manifest-complete" fails (r150a, 2nd run)
        marker = "Assets/ScCsgoResources.xml"; listed = ""
        if marker in hashes:
            with zipfile.ZipFile(path) as z: listed = z.read(marker).decode("utf8")
        unlisted = {m: base.sha(data) for m, data in (base.tactical_assets("full") if label == "全量" else [])
                    if m.startswith(("Assets/Textures/", "Assets/Models/", "Assets/Audio/")) and hashes.get(m) == base.sha(data) and f'<File Path="{m}"' not in listed}
        if not todo and manifest_text is None and not retire and not unlisted: print(label, "core resources already packaged", flush=True); continue
        for n in retire: del entries[n]; del hashes[n]; pk[label].setdefault("removed", []).append(n)
        if manifest_text is not None:
            data = manifest_text.encode("utf8"); entries[manifest] = base.member(data); hashes[manifest] = base.sha(data); pk[label]["changed"][manifest] = hashes[manifest]
        for r in todo:
            assert r["member"] not in hashes, "core resource already packaged with other bytes: " + r["member"]
            data = (staged / r.get("edition", "both") / r["member"]).read_bytes(); entries[r["member"]] = base.member(data); hashes[r["member"]] = base.sha(data)
        # The core's resource marker lists every texture/model/audio member with its hash (ScRequiredResources).
        if marker in hashes:
            additions = {r["member"]: r["sha256"] for r in todo if r["member"].startswith(("Assets/Textures/", "Assets/Models/", "Assets/Audio/"))}; additions.update(unlisted)
            text = marker_with(marker_without(listed, retire), additions); data = text.encode("utf8")
            entries[marker] = base.member(data); hashes[marker] = base.sha(data); pk[label]["changed"][marker] = hashes[marker]
        write_archive(path, entries)
        with zipfile.ZipFile(path) as out: assert out.testzip() is None and {n: base.sha(out.read(n)) for n in out.namelist()} == hashes
        pk[label].update(bytes=path.stat().st_size, sha256=base.sha(path.read_bytes())); pk[label]["changed"].update({r["member"]: r["sha256"] for r in todo})
        print(label, "core resources added", len(todo), "marker lines for packaged tactical assets", sorted(unlisted), "retired", retire, path.stat().st_size, pk[label]["sha256"][:16], flush=True)
    base.dump(S / "packages.json", pk); return True

def components_with(packaged, source):
    """The packaged compatibility manifest with every <Component/> line of the source manifest it lacks, each inserted
    before the first packaged component that sorts after it; the packaged lines, their order and line endings kept."""
    import re
    nl = "\r\n" if "\r\n" in packaged else "\n"; lines = packaged.split(nl)
    have = {l.strip() for l in lines}
    for line in sorted(l.strip() for l in source.splitlines() if l.strip().startswith("<Component ")):
        if line in have: continue
        comps = [i for i, l in enumerate(lines) if l.strip().startswith("<Component ")]
        assert comps, "packaged manifest has no components"
        at = next((i for i in comps if lines[i].strip() > line), comps[-1] + 1)
        lines.insert(at, "  " + line); have.add(line)
    return nl.join(lines)

def marker_with(text, additions):
    """The resource marker with more <File/> entries: each inserted before the first listed path that sorts after it
    (ordinal), every existing entry, its order and the header kept byte for byte."""
    import re
    entries = re.findall(r'<File Path="[^"]+" Sha256="[0-9a-f]{64}" />', text)
    assert len(entries) == text.count("<File ") and text.endswith("</Resources>"), "unexpected resource marker layout"
    paths = [re.match(r'<File Path="([^"]+)"', e).group(1) for e in entries]
    assert not set(additions) & set(paths), "already listed"
    for path, digest in sorted(additions.items()):
        at = next((i for i, p in enumerate(paths) if p > path), len(paths))
        entries.insert(at, f'<File Path="{path}" Sha256="{digest}" />'); paths.insert(at, path)
    return text[:text.index("<File ")] + "".join(entries) + "</Resources>"

def compat(S):
    """Compatibility gates for save-affecting work (current-direction-20260929: protection values in a new subsystem):
    the family matrix with the capacity-compatible 1.0.0/1.2.0 readers and the delivered 1.3.0 core (protection values
    through two older-reader rounds), the native compatibility hooks, and the Full/Lite integration matrix."""
    import concurrent.futures
    cap = ROOT / ".tmp/capacity-fix-20260927"; content = base.GAME / "Content.zip"; pk = json.loads((S / "packages.json").read_text("utf8"))
    c = lambda l: S / "candidate" / pk[l]["file"]; full, lite, agents = c("全量"), c("轻量"), c("探员")
    for t in ["CompatibilityCheck", "InventoryCheck"]:
        if not base.run(S, "build-tool-" + t, ["dotnet", "build", S / "tree/tools" / t / (t + ".csproj"), "-c", "Release", "--nologo", "-v:q"]): return False
    old = S / "old130"; old.mkdir(exist_ok=True)
    source130 = next(p for p in (ROOT / "output").glob("*1.3.0*.scmod") if p.stat().st_size > 400_000_000)
    with zipfile.ZipFile(source130) as z:
        for n in ["ScCsgoKnives.dll", "ScCsgoResources.dll"]:
            if n in z.namelist(): (old / n).write_bytes(z.read(n))
    base.dump(S / "old130.json", dict(package=source130.name, sha256=base.sha(source130.read_bytes()), members={p.name: base.sha(p.read_bytes()) for p in old.iterdir()}))
    latest = S / "full/core/source/bin/Release/net10.0/ScCsgoKnives.dll"
    # Every official release from 1.4.0 on, as released (AGENTS, bidirectional save switching): the readers are their cores,
    # OFFICIAL_RELEASES. Every assembly of a package goes beside its core: a Full core's block registry names types of
    # ScCsgoTactical.dll, and a reader loaded alone fails "preserves-all-later-item-type-identities" (r150a, first run).
    def extract(package, folder):
        folder.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(package) as z:
            for n in z.namelist():
                if n.endswith(".dll"): (folder / Path(n).name).write_bytes(z.read(n))
        return folder / "ScCsgoKnives.dll"
    def official(version, label, digest):
        name = base.name(label, version); out = ROOT / "output"
        for p in [out / name, out / f"history-{version}" / name, *(out / f"history-{version}").glob("*/" + name)]:
            if p.exists() and base.sha(p.read_bytes()) == digest: return p
        raise SystemExit(f"official {version} {label} ({digest[:12]}) not found in output/ or output/history-{version}/")
    cores = {}; found = {}
    for version, packages in OFFICIAL_RELEASES:
        for label, digest in packages.items():
            p = official(version, label, digest); short = "full" if label == "全量" else "lite"
            cores[(version, short)] = extract(p, S / f"old{version.replace('.', '')}" / short); found[f"{version}/{label}"] = {"package": str(p), "sha256": digest}
    base.dump(S / "official-releases.json", found)
    jobs = [("switching-140", ["dotnet", tool(S, "CompatibilityCheck"), cores[("1.4.0", "full")], cores[("1.4.0", "lite")], latest, S / "switching-140.json"])]
    jobs += [(f"switching-{v.replace('.', '')}", ["dotnet", tool(S, "CompatibilityCheck"), cores[(v, "full")], cores[("1.4.0", "full")], latest, S / f"switching-{v.replace('.', '')}.json"])
             for v, _ in OFFICIAL_RELEASES if v != "1.4.0"]
    jobs += [("family", ["dotnet", tool(S, "CompatibilityCheck"), cap / "1.0.0/src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll",
                        cap / "1.2.0/src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll", latest, S / "family.json", old / "ScCsgoKnives.dll"]),
            ("native-hooks", ["dotnet", tool(S, "TacticalLoadCheck"), "--compat-native", full, content, S / "native-hooks.json"]),
            ("native-lite", ["dotnet", tool(S, "TacticalLoadCheck"), "--world-resource-gate", lite, S / "native-lite.json"]),
            ("native-full", ["dotnet", tool(S, "TacticalLoadCheck"), "--world-resource-gate", full, S / "native-full.json"]),
            ("inventory", ["dotnet", tool(S, "InventoryCheck"), latest, base.GAME / "Mods", S / "inventory.json"])]
    for role, core, addon in [("full", full, full), ("lite", lite, agents)]:
        for variant in ["both", "reversed", "none"]:
            jobs.append((f"integration-{role}-{variant}", ["dotnet", tool(S, "TacticalLoadCheck"), ROOT, content, addon, variant, S / f"integration-{role}-{variant}.json", core]))
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        results = dict(zip([j[0] for j in jobs], pool.map(lambda j: base.run(S, *j), jobs)))
    base.dump(S / "compat.json", results); report(S, "family")
    for key in [j[0] for j in jobs if j[0].startswith("switching-")]: report(S, key)
    return all(results.values())

def ui(S):
    """FollowupCheck: settings semantics and offline renders, with the Full candidate's own textures (the protection HUD
    icons and the helmet spark) for the renders that need them."""
    _, dll = base.native_tool(S, "FollowupCheck")
    os.environ["ALSOFT_DRIVERS"] = "null"; pk = json.loads((S / "packages.json").read_text("utf8"))
    ok = base.run(S, "ui", ["dotnet", dll, base.GAME / "Content.zip", S / "ui", S / "candidate" / pk["全量"]["file"]])
    if (S / "ui/followup.json").exists(): print(json.dumps(json.loads((S / "ui/followup.json").read_text("utf-8-sig"))["failures"], ensure_ascii=False), flush=True)
    return ok

def core_database(S):
    """The core database (Assets/ScCsgoKnivesDatabase.xdb: project subsystems such as ScArmor) of the Full and Lite core,
    replaced with the stage tree's bytes when they differ (the ordinary package step replaces only the tactical one)."""
    member = "Assets/ScCsgoKnivesDatabase.xdb"; data = (S / "tree/src/ScCsgoKnives" / member).read_bytes(); pk = json.loads((S / "packages.json").read_text("utf8"))
    for label in ["全量", "轻量"]:
        path = S / "candidate" / pk[label]["file"]
        with zipfile.ZipFile(path) as z:
            entries = {i.filename: (i.compress_type, i.CRC, i.file_size, raw_member(z, i)) for i in z.infolist()}
            hashes = {n: base.sha(z.read(n)) for n in z.namelist()}
        assert member in hashes, f"{label}: no {member}"
        if hashes[member] == base.sha(data): print(label, "core database unchanged", flush=True); continue
        entries[member] = base.member(data); hashes[member] = base.sha(data); write_archive(path, entries)
        with zipfile.ZipFile(path) as out: assert out.testzip() is None and {n: base.sha(out.read(n)) for n in out.namelist()} == hashes
        pk[label].update(bytes=path.stat().st_size, sha256=base.sha(path.read_bytes())); pk[label]["changed"][member] = hashes[member]
        print(label, "core database replaced", path.stat().st_size, pk[label]["sha256"][:16], flush=True)
    base.dump(S / "packages.json", pk); return True

def marker_without(text, members):
    """The resource marker without the entries of retired members (every other byte kept)."""
    import re
    for m in members:
        entry = re.search(r'<File Path="' + re.escape(m) + r'" Sha256="[0-9a-f]{64}" />', text)
        if entry: text = text[:entry.start()] + text[entry.end():]
    return text

# Round 3 (post-mp-bugs-20260930 item 4): the network adapter ships inside the ordinary Full and Lite packages, built
# against the pinned 1.9.3.2_MP platform references (collected by tools/MpM0/m0.py prepare; SHA-256 below). On the 1.9.3.1
# engine the core never loads it (ScNet.Decide); on the preview platform it loads only when the CompatNet contract matches.
MP_REFS = ROOT / ".tmp/mp-m0-20260929/refs/mp"
MP_REF_HASHES = {"Survivalcraft.dll": "622976cce1f9da24", "Survivalcraft.CompatNet.dll": "4288904cbbbcbdfc", "Survivalcraft.Multiplayer.dll": "3e0b24efbf51b213",
                 "Engine.dll": "41ec4fc808a645b0", "EntitySystem.dll": "9a6d892e01896818", "Survivalcraft.Compat23.dll": "96b60590d436b787", "0Harmony.dll": "70e7647876b6c3d8"}
NET_MEMBER = "Net/ScCsgoNet.bin"
# mp-user-logs-20261002: the transport no longer needs the platform's CompatNet internal mod (the platform's Android
# build has none); the one CompatNet correction ships as a second, optional payload.
NET_COMPAT_MEMBER = "Net/ScCsgoNetCompat.bin"
base.NEW_MEMBERS.add(NET_MEMBER); base.NEW_MEMBERS.add(NET_COMPAT_MEMBER)

def net_build(S):
    """Builds src/ScCsgoNet (against the pinned platform references WITHOUT CompatNet: it must not need it) and
    src/ScCsgoNetCompat (with it), both against this stage's Lite core. Returns (adapter bytes, compat bytes, core path)."""
    import shutil, subprocess
    for name, short in MP_REF_HASHES.items():
        p = MP_REFS / name
        if not p.exists() or hashlib.sha256(p.read_bytes()).hexdigest()[:16] != short: print("net: platform reference missing or changed:", p, flush=True); return None
    refs = S / "net/refs"; plain = S / "net/refs-nocompat"; out = S / "net/out"
    for d in [refs, plain, out, S / "net/src", S / "net/src-compat"]:
        if d.exists(): shutil.rmtree(d)
    shutil.copytree(MP_REFS, refs)
    core = S / "lite/core/source/bin/Release/net10.0/ScCsgoKnives.dll"; shutil.copy2(core, refs / "ScCsgoKnives.dll")
    shutil.copytree(refs, plain); (plain / "Survivalcraft.CompatNet.dll").unlink()
    built = {}
    for project, source, references in [("ScCsgoNet", S / "net/src", plain), ("ScCsgoNetCompat", S / "net/src-compat", refs)]:
        shutil.copytree(ROOT / "src" / project, source, ignore=shutil.ignore_patterns("bin", "obj"))
        log = S / f"logs/build-net-{project}.log"; log.parent.mkdir(parents=True, exist_ok=True)
        r = subprocess.run(["dotnet", "build", str(source / f"{project}.csproj"), "-c", "Release", "-o", str(out / project), f"-p:Refs={references}", "--nologo", "-v:q"], capture_output=True, text=True)
        log.write_text(r.stdout + r.stderr, "utf8")
        if r.returncode != 0: print(f"net: {project} build failed, see", log, flush=True); return None
        built[project] = (out / project / f"{project}.dll").read_bytes()
    return built["ScCsgoNet"], built["ScCsgoNetCompat"], core

def net_member(S):
    """Adds the adapter payloads to the Full and Lite candidates. Records the reference hashes and the payloads' hashes (net.json)."""
    built = net_build(S)
    if built is None: return False
    adapter, compat, core = built
    members = {NET_MEMBER: adapter, NET_COMPAT_MEMBER: compat}
    pk = json.loads((S / "packages.json").read_text("utf8"))
    for label in ["全量", "轻量"]:
        path = S / "candidate" / pk[label]["file"]
        with zipfile.ZipFile(path) as z:
            entries = {i.filename: (i.compress_type, i.CRC, i.file_size, raw_member(z, i)) for i in z.infolist()}
            hashes = {n: base.sha(z.read(n)) for n in z.namelist()}
        if all(hashes.get(m) == base.sha(data) for m, data in members.items()): print(label, "adapter payloads already packaged", flush=True); continue
        for m, data in members.items(): entries[m] = base.member(data); hashes[m] = base.sha(data)
        write_archive(path, entries)
        with zipfile.ZipFile(path) as z: assert z.testzip() is None and all(base.sha(z.read(m)) == base.sha(data) for m, data in members.items())
        for m in members: pk[label]["changed"][m] = hashes[m]
        pk[label]["sha256"] = base.sha(path.read_bytes()); pk[label]["bytes"] = path.stat().st_size
        print(label, "adapter payloads added", {m: hashes[m][:16] for m in members}, flush=True)
    base.dump(S / "packages.json", pk)
    base.dump(S / "net.json", {"member": NET_MEMBER, "sha256": base.sha(adapter), "bytes": len(adapter), "core": base.sha(core.read_bytes()),
                               "compatMember": NET_COMPAT_MEMBER, "compatSha256": base.sha(compat), "compatBytes": len(compat),
                               "references": {n: hashlib.sha256((MP_REFS / n).read_bytes()).hexdigest() for n in MP_REF_HASHES},
                               "sources": {p.name: base.sha(p.read_bytes()) for d in ["net/src", "net/src-compat"] for p in sorted((S / d).glob("*.cs"))}})
    return True

def netloop(S):
    """mp-user-logs-20261002: the transport end to end on the real platform assemblies, offline (tools/NetLoopCheck): once
    without CompatNet in the process (the platform's Android build) and once with it. The payloads are the packaged ones."""
    import shutil, subprocess
    pk = json.loads((S / "packages.json").read_text("utf8")); net = json.loads((S / "net.json").read_text("utf8"))
    work = S / "net/loop"
    if work.exists(): shutil.rmtree(work)
    work.mkdir(parents=True)
    with zipfile.ZipFile(S / "candidate" / pk["轻量"]["file"]) as z:
        (work / "ScCsgoNet.dll").write_bytes(z.read(NET_MEMBER)); (work / "ScCsgoNetCompat.dll").write_bytes(z.read(NET_COMPAT_MEMBER)); core = z.read("ScCsgoKnives.dll")
    assert base.sha((work / "ScCsgoNet.dll").read_bytes()) == net["sha256"] and base.sha((work / "ScCsgoNetCompat.dll").read_bytes()) == net["compatSha256"], "packaged payloads differ from net.json"
    refs = work / "refs"; plain = work / "refs-nocompat"
    shutil.copytree(MP_REFS, refs); (refs / "ScCsgoKnives.dll").write_bytes(core)
    shutil.copytree(refs, plain); (plain / "Survivalcraft.CompatNet.dll").unlink()
    src = work / "src"; shutil.copytree(ROOT / "tools/NetLoopCheck", src, ignore=shutil.ignore_patterns("bin", "obj"))
    if not base.run(S, "build-tool-NetLoopCheck", ["dotnet", "build", src / "NetLoopCheck.csproj", "-c", "Release", "-o", work / "out", f"-p:Refs={refs}", "--nologo", "-v:q"]): return False
    ok = True
    for key, references, compat, flag in [("netloop-nocompat", plain, "-", []), ("netloop-compat", refs, work / "ScCsgoNetCompat.dll", ["--with-compatnet"])]:
        ok &= base.run(S, key, ["dotnet", work / "out/NetLoopCheck.dll", references, work / "ScCsgoNet.dll", compat, S / f"{key}.json", *flag])
        if (S / f"{key}.json").exists():
            d = json.loads((S / f"{key}.json").read_text("utf-8-sig")); failed = [c["Name"] for c in d["checks"] if not c["Ok"]]
            print(key, "failed", d["failed"], "of", len(d["checks"]), failed, flush=True); ok &= d["failed"] == 0 and len(d["checks"]) >= 20
        else: ok = False
    # mp-state-consistency-20261002 (tools/NetLoopCheck/StateLoop.cs, StateCases.cs): gun state between a server and two
    # clients through the platform's own packets and inventories. On the candidate every case and the four target assertions
    # must pass. The same target assertions are then run against the unchanged output/ Lite package: where that build has the
    # faults the user reported they FAIL there, which is the reproduction; it is printed and never fails this step.
    # mpc3-feedback-subworld-20261002: every mode registers the message handlers by the game's own calls (ScNet.RegisterCore,
    # then the optional packages' loaders): the agents package's assembly as packaged, the appearance integration's as built
    # for this stage. GunLoop.cs then runs the real gun state machine (SubsystemScGunBlockBehavior.Update) on a client end
    # and a server end over that wire. The same target assertions and the same gun loop are run against the unchanged
    # output/ Lite package: where that build has the reported faults they FAIL there (printed; never fails this step).
    modules = []
    with zipfile.ZipFile(S / "candidate" / pk["探员"]["file"]) as z: (work / "ScCsgoTactical.dll").write_bytes(z.read("ScCsgoTactical.dll")); modules.append(work / "ScCsgoTactical.dll")
    appearance = S / "lite/appearance/source/bin/Release/net10.0/ScCsgoAppearance.dll"
    if appearance.exists(): shutil.copy2(appearance, work / "ScCsgoAppearance.dll"); modules.append(work / "ScCsgoAppearance.dll")
    else: print("netloop: no appearance integration assembly in this stage; its two message numbers are not in the table check", flush=True); ok = False
    def state(key, references, adapter, flag, least, packages=modules):
        arguments = ["dotnet", work / "out/NetLoopCheck.dll", references, adapter, "-", S / f"{key}.json", flag]
        if packages: arguments.append("--modules=" + ";".join(str(m) for m in packages))
        done = base.run(S, key, arguments)
        if not (S / f"{key}.json").exists(): print(key, "no result", flush=True); return None
        d = json.loads((S / f"{key}.json").read_text("utf-8-sig")); failed = [f"[{c['Case']}] {c['Name']}" for c in d["checks"] if not c["Ok"]]
        print(key, "failed", d["failed"], "of", d["total"], "handlers", d.get("registered"), "packages", d.get("modules"), failed, flush=True)
        return done and d["failed"] == 0 and d["total"] >= least and len(d.get("modules", [])) == len(packages)
    ok &= state("netstate", plain, work / "ScCsgoNet.dll", "--state", 105) is True
    ok &= state("netstate-targets", plain, work / "ScCsgoNet.dll", "--baseline", 12) is True
    ok &= state("netgunloop", plain, work / "ScCsgoNet.dll", "--gunloop", 122) is True
    delivered = base.baseline("轻量")
    if delivered.exists() and hashlib.sha256(delivered.read_bytes()).hexdigest() == base.BASELINES["轻量"]:
        old = work / "refs-delivered"; shutil.copytree(plain, old)
        with zipfile.ZipFile(delivered) as z: (old / "ScCsgoKnives.dll").write_bytes(z.read("ScCsgoKnives.dll")); (work / "ScCsgoNet-delivered.dll").write_bytes(z.read(NET_MEMBER))
        for key, flag, least in [("netstate-delivered", "--baseline", 12), ("netgunloop-delivered", "--gunloop", 122)]:
            result = state(key, old, work / "ScCsgoNet-delivered.dll", flag, least, packages=[])
            print(f"{key}: the delivered output/ Lite package", "passes every assertion (nothing reproduced)" if result else "fails assertions as listed above: the reported faults, reproduced on the delivered build", flush=True)
    else: print("netstate-delivered: output/ Lite is not the recorded baseline package, reproduction not run", flush=True)
    return ok

def appnet(S):
    """mp-user-logs-20261002: the appearance integration's multiplayer corrections (NEO item-bone buffer, CS model choice
    sync) against the real NMM 1.1 / NEO 1.4 assemblies and this stage's own DLLs, offline (tools/AppearanceNetCheck)."""
    import shutil
    third = [ROOT / ".tmp/creature-audit-20260917/10/neorxna.dll", ROOT / ".tmp/third-party-refs/sc-nekomekomodel.dll"]
    ok = True
    for edition in ["full", "lite"]:
        work = S / "net/appnet" / edition
        if work.exists(): shutil.rmtree(work)
        refs = work / "refs"; refs.mkdir(parents=True)
        for p in third: shutil.copy2(p, refs / p.name)
        shutil.copy2(S / edition / "appearance/source/bin/Release/net10.0/ScCsgoAppearance.dll", refs)
        for part in ["core", "agents"]:
            for p in (S / edition / part / "source/bin/Release/net10.0").glob("ScCsgo*.dll"):
                if not (refs / p.name).exists(): shutil.copy2(p, refs)
        src = work / "src"; shutil.copytree(ROOT / "tools/AppearanceNetCheck", src, ignore=shutil.ignore_patterns("bin", "obj"))
        if not base.run(S, f"build-tool-AppearanceNetCheck-{edition}", ["dotnet", "build", src / "AppearanceNetCheck.csproj", "-c", "Release", "-o", work / "out", f"-p:Refs={refs}", "--nologo", "-v:q"]): ok = False; continue
        key = f"appnet-{edition}"
        ok &= base.run(S, key, ["dotnet", work / "out/AppearanceNetCheck.dll", S / f"{key}.json"])
        if (S / f"{key}.json").exists():
            d = json.loads((S / f"{key}.json").read_text("utf-8-sig")); failed = [c["Name"] for c in d["checks"] if not c["Ok"]]
            print(key, "failed", d["failed"], "of", len(d["checks"]), failed, flush=True); ok &= d["failed"] == 0 and len(d["checks"]) >= 15
        else: ok = False
    return ok

def identity(S):
    """Every core and agents assembly in the three candidates carries this stage's gameplay identity (prepare), so Full and
    split Lite (+ agents) of this release accept each other in multiplayer (Game.ScNetIdentity)."""
    stamp = json.loads((S / "identity.json").read_text("utf8"))["identity"].encode("ascii")
    pk = json.loads((S / "packages.json").read_text("utf8")); found = {}
    for label, members in [("全量", ["ScCsgoKnives.dll", "ScCsgoTactical.dll"]), ("轻量", ["ScCsgoKnives.dll"]), ("探员", ["ScCsgoTactical.dll"])]:
        with zipfile.ZipFile(S / "candidate" / pk[label]["file"]) as z:
            for m in members: found[f"{label}/{m}"] = stamp in z.read(m)
    print("identity", stamp.decode(), json.dumps(found, ensure_ascii=False), flush=True)
    return all(found.values())

def package(S):
    ok = vf.STEPS["package"](S)
    return core_members(S) and core_database(S) and net_member(S) and identity(S) and ok is not False

# Deathmatch round 6 (2026-10-05, R6-5): embedded data newer than the release-140 stage the core is built from (its csproj
# embeds AnimationData/*.json from that stage; followup_140.prepare overlays only the .cs sources). Each file listed is copied
# from the source tree into both editions' core source after the ordinary prepare; the bytes are recorded in embedded.json.
# dmr6d had AnimationData/cs2_muzzle_fx.json (R6-5, archived since dmr6e). Tracers (2026-10-05): cs2_effects.json with CS2's
# texture controls (tools/cs2_effects.py).
EMBEDDED_ADDITIONS = ["AnimationData/cs2_effects.json"]
def prepare(S):
    vf.STEPS["prepare"](S)
    added = {}
    for rel in EMBEDDED_ADDITIONS:
        data = (ROOT / "src/ScCsgoKnives" / rel).read_bytes()
        for edition in ["lite", "full"]:
            source = S / edition / "core/source"
            project = next(source.glob("*.csproj")).read_text("utf8")
            assert "AnimationData/*.json" in project, f"{edition} core no longer embeds AnimationData/*.json"
            dest = source / rel; previous = base.sha(dest.read_bytes()) if dest.exists() else None
            dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(data)
            added[f"{edition}/{rel}"] = {"sha256": base.sha(data), "bytes": len(data), "replaced": previous}
    base.dump(S / "embedded.json", added)
    print("embedded additions", {k: v["sha256"][:16] for k, v in added.items()}, flush=True)

STEPS = dict(vf.STEPS)
STEPS.update({"prepare": prepare, "c4": c4, "main": main, "ai": ai, "compat": compat, "ui": ui, "baseline": baseline, "gates": gates, "package": package, "clips": clips, "netloop": netloop, "appnet": appnet})
ORDER = ["prepare", "build", "clips", "appearance", "package", "netloop", "appnet", "baseline", "gates", "motion", "throw", "hotspots", "ui"]
PREREQUISITES = {"prepare", "build", "clips", "appearance", "package"}

if __name__ == "__main__":
    tag, wanted = sys.argv[1], sys.argv[2]; S = base.BASE / tag
    chosen = ORDER if wanted == "all" else wanted.split(",")
    unknown = [s for s in chosen if s not in STEPS]; assert not unknown, unknown
    failed = []
    for step in chosen:
        try: ok = STEPS[step](S) is not False
        except SystemExit as e: ok = not e.code
        if not ok:
            failed.append(step)
            if step in PREREQUISITES: print("stopped after the failed prerequisite", step, "- later steps not run:", chosen[chosen.index(step) + 1:], flush=True); break
    print("failed steps:", failed); raise SystemExit(1 if failed else 0)
