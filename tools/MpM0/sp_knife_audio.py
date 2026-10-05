"""Quick-scroll knife/weapon sounds, single player (post-mp-bugs-20260930 §4): actual engine wheel path + a recording.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/sp_knife_audio.py <package path> <label>
One game in the fixed runtime role "server" (single player; no network is used), TestAutomation and one CS package
(a single-player test package, or the package currently in output/ as the baseline). Hotbar: knife in slot 0, CS guns 1-4,
non-CS blocks after them. Each scenario is driven inside the game frame by frame: a wheel step is queued for the next
frame's Dispatcher pass, i.e. exactly where the engine takes a real wheel event (Mouse.ProcessMouseWheel, read by
ComponentInput as ScrollInventory, applied by ComponentPlayer). Recorded per scenario: every wheel step (UTC, frame), the
active slot and the engine's count of playing one-shot sounds (Mixer.m_soundsToStopPoll) whenever they change, the mod's
own sound journal where the package has it (ScPresentationSound.Record), and one recording of the game process's own
audio (tools/ProcessAudioCapture: process loopback, nothing else on the machine) with its UTC start. The report gives,
per scenario, the time the wheel stopped, how long the recording stays audible after it (10 ms RMS against the pre-roll
floor), the most sounds playing at once and the draws/sound starts seen.
"""
import json, subprocess, sys, threading, time, wave
from datetime import datetime, timezone
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, RESULTS, RUNS, TA_BUILT, ROOT, sha, to_menu, poll
from mp_m1 import PROJECT, MAIN, SURVIVAL, KEEP_ACTIVE

CAPTURE_PROJECT = ROOT / "tools/ProcessAudioCapture/ProcessAudioCapture.csproj"
CAPTURE = ROOT / "tools/ProcessAudioCapture/bin/Release/net10.0-windows/ProcessAudioCapture.exe"
# The mod is loaded in its own context: its types are found among the loaded assemblies (absent from the c15 package).
def mod_type(name): return f'System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("{name}")).FirstOrDefault(x => x != null)'
T_SOUND = mod_type("Game.ScPresentationSound")
T_KNIFE = mod_type("Game.KnifeAnimationController")
# A sealed stone room deep underground at the player's column inside 9 blocks of solid granite: no sky (wind, rain), no
# animals, no water or magma near enough for their ambient loops (run 03 of 2026-09-30 had one at -20 dB throughout).
QUIET_ROOM = MAIN + ('var t = project.FindSubsystem<Game.SubsystemTerrain>(true); var p = pl.ComponentBody.Position; '
    'int x = (int)System.MathF.Floor(p.X), z = (int)System.MathF.Floor(p.Z), y = 14; '
    'for (int dx = -9; dx <= 9; dx++) for (int dz = -9; dz <= 9; dz++) for (int dy = -5; dy <= 8; dy++) { '
    'bool inside = System.Math.Abs(dx) <= 2 && System.Math.Abs(dz) <= 2 && dy >= 0 && dy <= 3; '
    't.ChangeCell(x + dx, y + dy, z + dz, inside ? 0 : Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)); } '
    'pl.ComponentBody.Position = new Engine.Vector3(x + .5f, y, z + .5f); pl.ComponentBody.Velocity = Engine.Vector3.Zero; return x + " " + y + " " + z;')

# 10 visible slots (the survival hotbar widget shows 7 unless its maximum is raised; creative and the inventory have 10).
SETUP = MAIN + (
    'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); '
    'int knifeBlock = Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true); int plain = -1, fly = -1; '
    'for (int v = 0; v < Game.CsmcKnifeRig.KnifeCount; v++) { string n = Game.CsmcKnifeRig.GetAssetName(v); if (n == "butterfly") fly = v; else if (plain < 0 && Game.ScKnifeBlock.IsKnown(Game.Terrain.MakeBlockValue(knifeBlock, 0, v))) plain = v; } '
    'int Gun(string name) { int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == name); int id = Game.ScGunRegistry.Current.Allocate(v, Game.GunSpec.All[v].Magazine, false, Game.ScGunDurability.Full(v)); '
    '  return Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)); } '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(knifeBlock, 0, plain), 1); inv.AddSlotItems(1, Gun("ak47"), 1); inv.AddSlotItems(2, Game.ScGrenadeBlock.Value(0), 3); '
    'inv.AddSlotItems(3, Gun("deagle"), 1); inv.AddSlotItems(4, Game.ScGrenadeBlock.Value(2), 3); inv.AddSlotItems(5, Gun("awp"), 1); inv.AddSlotItems(6, Game.ScGrenadeBlock.Value(1), 2); '
    'inv.AddSlotItems(7, Game.ScGrenadeBlock.Value(3), 1); inv.AddSlotItems(8, Game.Terrain.MakeBlockValue(Game.DirtBlock.Index), 1); inv.AddSlotItems(9, Game.ScC4Block.Value, 1); '
    'Game.SettingsManager.ShortInventoryLooping = false; '
    'return plain + " " + fly + " " + Game.CsmcKnifeRig.GetAssetName(plain) + " visible " + inv.VisibleSlotsCount;')
# The survival inventory gives hotbar slots beyond the visible count no capacity: widen first, fill a frame later.
WIDEN = MAIN + 'pl.ComponentGui.ShortInventoryWidget.MaxVisibleSlotsCount = 10; pl.ComponentMiner.Inventory.VisibleSlotsCount = 10; return pl.ComponentMiner.Inventory.VisibleSlotsCount.ToString();'
# Rain is heard from precipitation shafts within 7 blocks, covered or not (SubsystemWeather): no weather for the recording.
NO_RAIN = PROJECT + ('var w = project.FindSubsystem<Game.SubsystemWeather>(true); project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings.AreWeatherEffectsEnabled = false; '
    'w.ManualPrecipitationEnd(); w.PrecipitationIntensity = 0f; if (w.m_rainSound != null) w.m_rainSound.Volume = 0f; '
    'return "precipitation " + w.IsPrecipitationStarted + " intensity " + w.PrecipitationIntensity + " rain volume " + (w.m_rainSound?.Volume ?? 0f);')
HOTBAR = MAIN + 'var inv = pl.ComponentMiner.Inventory; return inv.VisibleSlotsCount + ": " + string.Join(", ", System.Linq.Enumerable.Range(0, 10).Select(s => s + "=" + Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(inv.GetSlotValue(s))].GetType().Name + "x" + inv.GetSlotCount(s)));'
def knife(variant): return MAIN + (f'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); '
    f'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true), 0, {variant}), 1); return "ok";')
def select(slot): return MAIN + f'pl.ComponentMiner.Inventory.ActiveSlotIndex = {slot}; return pl.ComponentMiner.Inventory.ActiveSlotIndex.ToString();'
JOURNAL_ON = f'var t = {T_SOUND}; var f = t?.GetField("Record"); if (f == null) return "no journal (package without ScPresentationSound.Record)"; f.SetValue(null, true); t.GetMethod("ClearJournal").Invoke(null, null); return "on";'
JOURNAL = f'var p = {T_SOUND}?.GetProperty("JournalText"); return p == null ? "" : (string)p.GetValue(null);'
JOURNAL_CLEAR = f'var m = {T_SOUND}?.GetMethod("ClearJournal"); if (m != null) m.Invoke(null, null); return "ok";'
def follow_held(on): return f'var f = {T_KNIFE}?.GetField("FollowHeldItemOnly"); if (f == null) return "absent"; f.SetValue(null, {"true" if on else "false"}); return "{on}";'
# Slot 1 becomes a block for the dwell scenarios: knife (0) -> block (1), held past the middle of vanilla's swap animation
# toward the block (m_value is then the block), and back to the knife before that animation ends.
BLOCK_IN_SLOT_1 = MAIN + 'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(1, inv.GetSlotCount(1)); inv.AddSlotItems(1, Game.Terrain.MakeBlockValue(Game.DirtBlock.Index), 1); return "ok";'


def driver(label, steps, gap, slots=None):
    """Installs one scenario in the game loop: wheel steps (or direct slot selections) <gap> ms apart (one value or one per
    step), per-frame sampling, then 4 quiet seconds. Its log and "done" flag live in the AppDomain (works with any package)."""
    arr = ", ".join(str(s) for s in (slots or steps))
    act = ('Engine.Input.Mouse.ProcessMouseWheel(v); log.Add(System.DateTime.UtcNow.ToString("HH:mm:ss.fff") + " frame " + Engine.Time.FrameIndex + " wheel " + v);'
           if slots is None else
           'pl.ComponentMiner.Inventory.ActiveSlotIndex = v; log.Add(System.DateTime.UtcNow.ToString("HH:mm:ss.fff") + " frame " + Engine.Time.FrameIndex + " select " + v);')
    return MAIN + (
        f'var d = System.AppDomain.CurrentDomain; var log = new System.Collections.Generic.List<string>(); d.SetData("sc.test.log", log); d.SetData("sc.test.done", null); '
        f'int[] steps = new[] {{ {arr} }}; int[] gaps = new[] {{ {", ".join(str(x) for x in (gap if isinstance(gap, list) else [gap] * len(slots or steps)))} }}; int i = 0, lastSlot = -2, lastCount = -1; double next = Engine.Time.RealTime + .4, end = -1; '
        # Every sound the engine starts or finishes, named through the content cache (asset name of its buffer).
        'var cachesField = typeof(Game.ContentManager).GetField("Caches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); '
        'var names = new System.Collections.Generic.Dictionary<object, string>(System.Collections.Generic.ReferenceEqualityComparer.Instance); '
        'string NameOf(Engine.Audio.Sound snd) { object b = snd.SoundBuffer; if (b == null) return "(streamed)"; if (!names.TryGetValue(b, out var n)) { names.Clear(); '
        '  foreach (System.Collections.DictionaryEntry e in (System.Collections.IDictionary)cachesField.GetValue(null)) foreach (var o in (System.Collections.IEnumerable)e.Value) if (o is Engine.Audio.SoundBuffer) names[o] = (string)e.Key; '
        '  names.TryGetValue(b, out n); } return n ?? "?"; } '
        # A finished sound may already be disposed (no buffer): its name is kept from when it started.
        'var named = new System.Collections.Generic.Dictionary<Engine.Audio.Sound, string>(); foreach (var snd in Engine.Audio.Mixer.m_soundsToStopPoll) named[snd] = NameOf(snd); '
        'log.Add(System.DateTime.UtcNow.ToString("HH:mm:ss.fff") + " frame " + Engine.Time.FrameIndex + " playing at start: " + string.Join(", ", named.Values)); '
        'System.Action handler = null; handler = () => { '
        '  int slot = pl.ComponentMiner.Inventory.ActiveSlotIndex, count = Engine.Audio.Mixer.m_soundsToStopPoll.Count; '
        '  string now = System.DateTime.UtcNow.ToString("HH:mm:ss.fff"); '
        '  foreach (var snd in Engine.Audio.Mixer.m_soundsToStopPoll) if (!named.ContainsKey(snd)) { named[snd] = NameOf(snd); log.Add(now + " frame " + Engine.Time.FrameIndex + " sound+ " + named[snd]); } '
        '  foreach (var snd in named.Keys.Where(x => !Engine.Audio.Mixer.m_soundsToStopPoll.Contains(x)).ToList()) { log.Add(now + " frame " + Engine.Time.FrameIndex + " sound- " + named[snd]); named.Remove(snd); } '
        '  if (slot != lastSlot || count != lastCount) { log.Add(System.DateTime.UtcNow.ToString("HH:mm:ss.fff") + " frame " + Engine.Time.FrameIndex + " slot " + slot + " sounds " + count); lastSlot = slot; lastCount = count; } '
        '  if (i < steps.Length && Engine.Time.RealTime >= next) { next = Engine.Time.RealTime + gaps[i] / 1000.0; int v = steps[i++]; '
        f'    lock (Engine.Dispatcher.m_actionInfos) Engine.Dispatcher.m_actionInfos.Add(new Engine.Dispatcher.ActionInfo {{ Action = () => {{ {act} }} }}); '
        '    if (i == steps.Length) end = Engine.Time.RealTime + 4; } '
        f'  if (end >= 0 && Engine.Time.RealTime >= end) {{ Engine.Window.Frame -= handler; d.SetData("sc.test.done", "{label}"); }} '
        '}; Engine.Window.Frame += handler; return "installed";')
LOG = 'var l = (System.Collections.Generic.List<string>)System.AppDomain.CurrentDomain.GetData("sc.test.log"); return l == null ? "" : string.Join("\\n", l);'
DONE = 'return System.Convert.ToString(System.AppDomain.CurrentDomain.GetData("sc.test.done")) ?? "";'
EXIT_WORLD = 'Game.GameManager.SaveProject(true, true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("MainMenu"); return "left";'

# name, start slot, wheel steps (+1 = wheel up: toward slot 0), milliseconds between steps, direct slots instead of wheel
UP, DOWN = 1, -1
SCENARIOS = [
    ("cs-fast-end-knife", 3, [UP] * 3 + [DOWN] * 3 + [UP] * 3 + [DOWN] * 3 + [UP] * 3, 33, None),
    ("cs-fast-end-he-grenade", 3, [UP] * 3 + [DOWN] * 3 + [UP] * 3 + [DOWN] * 2, 33, None),
    ("all-slots-fast-end-c4", 9, [UP] * 9 + [DOWN] * 9 + [UP] * 9 + [DOWN] * 9, 22, None),
    ("across-to-non-cs", 8, [UP] * 8 + [DOWN] * 8 + [UP] * 8 + [DOWN] * 8, 22, None),
    ("across-end-knife", 8, [UP] * 8 + [DOWN] * 8 + [UP] * 8, 22, None),
    ("slow", 3, [UP] * 3 + [DOWN] * 3, 280, None),
    ("direct-select", 8, None, 33, [0, 2, 0, 4, 0, 6, 0, 8, 0, 7, 0, 8]),
]


def main(package, label):
    # "output-lite"/"output-full": the packages currently in output/ (their names are not ASCII); each report records the hash.
    pkg = {"output-lite": m0.LITE, "output-full": m0.FULL}.get(package) or Path(package); ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"knife-audio-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"knife-audio-{label}", "package": {pkg.name: sha(pkg)}, "testAutomation": sha(ta), "scenarios": [], "steps": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    if not CAPTURE.exists() or CAPTURE.stat().st_mtime < (CAPTURE_PROJECT.parent / "Program.cs").stat().st_mtime:
        subprocess.run(["dotnet", "build", str(CAPTURE_PROJECT), "-c", "Release", "-v", "q", "-nologo"], check=True)
    R["captureTool"] = sha(CAPTURE)
    g = None; rec = None
    try:
        g = Game("server", case_dir, [ta, pkg]); to_menu(g)
        g.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.MusicVolume = 0; SettingsManager.SoundsVolume = 1; SettingsManager.SaveSettings();')
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        poll(g, SURVIVAL, lambda v: v == "Survival", 20)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait("Player into playing.", 600, m)
        step("window kept active (sounds and wheel need it)", g.func(KEEP_ACTIVE))
        step("sealed quiet room underground (x y z)", g.func(QUIET_ROOM)); time.sleep(4)
        step("weather off", g.func(NO_RAIN))
        step("hotbar widened to", g.func(WIDEN)); time.sleep(1)
        plain, fly, name, _, visible = step("hotbar (plain knife, butterfly, name, visible slots)", g.func(SETUP)).split()[:5]
        time.sleep(1); step("hotbar as the game shows it (visible slots: slot=block x count)", g.func(HOTBAR))
        step("mod sound journal", g.func(JOURNAL_ON))
        step("drawing hook follows the held item only (default)", g.func(follow_held(True)))
        time.sleep(3)
        wav = case_dir / "game-audio.wav"
        total = 40 + 9 * (len(SCENARIOS) + 4) + 25
        step("rain before recording", g.func(NO_RAIN))
        rec = subprocess.Popen([str(CAPTURE), str(g.proc.pid), str(wav), str(total)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        time.sleep(2.5)
        runs = [(n, s, st, gap, sl, None) for n, s, st, gap, sl in SCENARIOS]
        runs.append(("butterfly-across-to-non-cs", 8, [UP] * 8 + [DOWN] * 8 + [UP] * 8 + [DOWN] * 8, 22, None, ("knife", fly)))
        runs.append(("across-to-non-cs-hook-unfixed", 8, [UP] * 8 + [DOWN] * 8 + [UP] * 8 + [DOWN] * 8, 22, None, ("hook", False)))
        # 320 ms on the block (past the swap's 250 ms midpoint, m_value is then the block), back to the knife before it ends.
        runs.append(("dwell-block-back-to-knife", 0, [DOWN, UP, DOWN, UP], [320, 500, 320, 500], None, ("dwell", True)))
        runs.append(("dwell-block-back-to-knife-hook-unfixed", 0, [DOWN, UP, DOWN, UP], [320, 500, 320, 500], None, ("dwell", False)))
        for name_, start, steps_, gap, slots, extra in runs:
            if extra and extra[0] == "knife": g.func(knife(extra[1]))
            if extra and extra[0] == "hook": step("drawing hook switched OFF for this scenario (A/B)", g.func(follow_held(False)))
            if extra and extra[0] == "dwell":
                g.func(BLOCK_IN_SLOT_1)
                if not extra[1]: step("drawing hook switched OFF for this scenario (A/B)", g.func(follow_held(False)))
            g.func(select(start)); time.sleep(2.2)
            g.func(JOURNAL_CLEAR)
            installed = g.func(driver(name_, steps_, gap, slots))
            done = poll(g, DONE, lambda v: v == name_, 60)
            entry = {"scenario": name_, "startSlot": start, "steps": steps_ or slots, "gapFrames": gap, "installed": installed, "completed": done == name_,
                     "log": g.func(LOG).splitlines(), "journal": g.func(JOURNAL).splitlines()}
            R["scenarios"].append(entry); step(f"scenario {name_}", {"completed": entry["completed"], "logLines": len(entry["log"]), "journalLines": len(entry["journal"])})
            if extra and extra[0] == "knife": g.func(knife(plain))
            if extra and extra[0] in ("hook", "dwell"): g.func(follow_held(True))
            time.sleep(1)
        # Leaving the world right after a draw started.
        g.func(select(3)); time.sleep(2); g.func(JOURNAL_CLEAR)
        t_exit = datetime.now(timezone.utc).strftime("%H:%M:%S.%f")[:-3]
        g.func(select(0)); time.sleep(.08); g.func(EXIT_WORLD)
        m = g.mark(); time.sleep(4)
        R["scenarios"].append({"scenario": "leave-world-during-draw", "selectKnifeUtc": t_exit, "journal": g.func(JOURNAL).splitlines(), "log": []})
        step("left the world during a draw", R["scenarios"][-1]["journal"][-4:])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if rec is not None:
            try: out, err = rec.communicate(timeout=120); R["capture"] = {"stdout": out.strip()[-800:], "stderr": err.strip()[-800:], "exit": rec.returncode}
            except Exception as e: rec.kill(); R["capture"] = {"failure": str(e)}
        if g is not None:
            R["gameErrors"] = g.errors(); g.close()
        m0.RUNTIME_OWNER.release()
    try: R["analysis"] = analyse(case_dir / "game-audio.wav", R)
    except Exception as e: R["analysis"] = {"failure": f"{type(e).__name__}: {e}"}
    RESULTS.mkdir(parents=True, exist_ok=True)
    out = RESULTS / f"knife-audio-{label}.json"
    out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    brief = {k: v for k, v in R["analysis"].items() if k != "scenarios"}
    brief["scenarios"] = [{k: v for k, v in s.items() if k != "soundsAfterStop"} for s in R["analysis"].get("scenarios", [])]
    print(json.dumps({"case": R["case"], "failure": R.get("failure"), "capture": R.get("capture"), "analysis": brief}, ensure_ascii=False, indent=1)[:14000])
    print("report", out, "case", case_dir)
    return 0 if "failure" not in R else 1


def utc_seconds(hms, day):
    h, m, s = hms.split(":"); return day + int(h) * 3600 + int(m) * 60 + float(s)


def analyse(wav_path, R):
    import numpy as np
    meta = json.loads(Path(str(wav_path) + ".json").read_text("utf-8"))
    start = datetime.fromisoformat(meta["startUtc"].replace("Z", "+00:00"))
    day = start.replace(hour=0, minute=0, second=0, microsecond=0).timestamp(); t0 = start.timestamp()
    with wave.open(str(wav_path)) as w:
        rate, ch = w.getframerate(), w.getnchannels(); data = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    mono = data.reshape(-1, ch).mean(axis=1); win = rate // 100
    n = len(mono) // win; rms = np.sqrt((mono[: n * win].reshape(n, win) ** 2).mean(axis=1) + 1e-12); db = 20 * np.log10(rms)
    def at(sec): return int((sec - t0) * 100)
    out = {"sampleRate": rate, "seconds": round(len(mono) / rate, 2), "paddedFrames": meta.get("paddedFrames"), "scenarios": []}
    image_rows = []
    for sc in R["scenarios"]:
        wheel = [l for l in sc.get("log", []) if " wheel " in l or " select " in l]
        if not wheel:
            if sc["scenario"] == "leave-world-during-draw": out["scenarios"].append({"scenario": sc["scenario"], "journalTail": sc["journal"][-6:]})
            continue
        first, last = utc_seconds(wheel[0].split()[0], day), utc_seconds(wheel[-1].split()[0], day)
        pre = db[max(0, at(first - 1.6)): max(1, at(first - .1))]
        floor = float(np.percentile(pre, 50)) if len(pre) else -90.0
        threshold = max(floor + 12, -62.0)
        after = db[at(last): at(last + 3.8)]
        # Audible after the stop: until the recording first stays under the threshold for 0.4 s (short unrelated sounds
        # of the world can follow later; the engine's own count says when the scenario's sounds were gone).
        tail = None
        for k in range(len(after)):
            if (after[k:k + 40] <= threshold).all() and k + 40 <= len(after): tail = k / 100; break
        loud = np.nonzero(after > threshold)[0]
        last_loud = float((loud[-1] + 1) / 100) if len(loud) else 0.0
        counts = [int(l.split(" sounds ")[1]) for l in sc.get("log", []) if " sounds " in l]
        base = counts[0] if counts else 0
        slots = [l for l in sc.get("log", []) if " slot " in l]
        final_slot = int(slots[-1].split(" slot ")[1].split()[0]) if slots else -1
        # Engine's playing-sound count: most at once, and when it returned to its level before the scenario.
        back = None
        for l in sc.get("log", []):
            if " sounds " in l and utc_seconds(l.split()[0], day) >= last and int(l.split(" sounds ")[1]) <= base:
                back = round(utc_seconds(l.split()[0], day) - last, 3); break
        # Named sounds that were still playing when the wheel stopped or began after it, with their end (seconds after the stop).
        after_stop = {}
        active = {}
        for l in sc.get("log", []):
            if " sound+ " in l or " sound- " in l:
                t = utc_seconds(l.split()[0], day) - last; nm = l.split(" sound")[1][2:]
                if " sound+ " in l: active.setdefault(nm, []).append(t)
                elif active.get(nm):
                    started = active[nm].pop(0)
                    if t > 0: after_stop.setdefault(nm, []).append([round(started, 3), round(t, 3)])
        for nm, starts in active.items():
            for started in starts: after_stop.setdefault(nm, []).append([round(started, 3), None])
        # Only this mod's sounds (the world's own - water, lava, ambience - are listed above but not counted here).
        ours = {nm: v for nm, v in after_stop.items() if "/ScCsgoKnives/" in nm}
        ours_end = max([e for v in ours.values() for _, e in v if e is not None] + [0.0])
        ours_open = sorted(nm for nm, v in ours.items() for _, e in v if e is None)
        j = sc.get("journal", [])
        row = {"scenario": sc["scenario"], "wheelSteps": len(wheel), "scrollSeconds": round(last - first, 3), "floorDb": round(floor, 1), "thresholdDb": round(threshold, 1),
               "audibleAfterStopSeconds": None if tail is None else round(tail, 2), "lastLoudWithin3.8s": round(last_loud, 2), "peakDbAfterStop": round(float(after.max()), 1) if len(after) else None,
               "engineSoundsMax": max(counts) if counts else None, "engineSoundsBefore": base, "engineSoundsBackAfterStop": back, "finalSlot": final_slot,
               "draws": sum(" draw " in l for l in j), "heldStarts": sum(" start #" in l for l in j), "releases": sum(" release " in l for l in j),
               "stoppedByRelease": sum(" stop #" in l for l in j), "endedNaturally": sum(" end #" in l for l in j),
               "modSoundsLastEndAfterStop": round(ours_end, 3), "modSoundsStillPlayingAtLogEnd": ours_open, "soundsAfterStop": after_stop}
        out["scenarios"].append(row)
        image_rows.append((sc["scenario"], db[max(0, at(first - .5)): at(last + 3.8)], at(last) - max(0, at(first - .5)), threshold, [at(utc_seconds(l.split()[0], day)) - max(0, at(first - .5)) for l in wheel]))
    try: out["image"] = str(envelope_image(wav_path.parent / "envelope.png", image_rows))
    except Exception as e: out["image"] = f"not drawn: {e}"
    return out


def envelope_image(path, rows):
    from PIL import Image, ImageDraw
    width = 1200; row_h = 110; img = Image.new("RGB", (width, row_h * len(rows) + 10), "white"); d = ImageDraw.Draw(img)
    for k, (name, env, stop, threshold, marks) in enumerate(rows):
        top = 5 + k * row_h; scale = width / max(1, len(env))
        def y(v): return top + 90 - int((max(-80, min(0, v)) + 80) / 80 * 80)
        d.text((5, top), name, fill="black")
        d.line([(0, y(threshold)), (width, y(threshold))], fill=(200, 200, 255))
        for mk in marks: d.line([(mk * scale, top + 12), (mk * scale, top + 92)], fill=(255, 200, 120))
        d.line([(stop * scale, top + 10), (stop * scale, top + 95)], fill="red", width=2)
        pts = [(i * scale, y(v)) for i, v in enumerate(env)]
        if len(pts) > 1: d.line(pts, fill="black")
        for s in range(0, int(len(env) / 100) + 1): d.text((s * 100 * scale + 2, top + 92), f"{s}s", fill="gray")
    img.save(path); return path


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
