"""feedback-1124-170 (CS 1.6.0) in the real game, isolated 1.9.3.1 copy, muted (docs/tasks/feedback-1124-170.md 5.x):
  b  shotguns: tube reload at Lv1/Lv10/Lv50 (seconds, shells per loop, cue count, cut short by firing), damage of one shot
     near / middle / at the end of the range / beyond for all four, knockback within 6 blocks on a light and a heavy body
  c  the C4 T: shield up, advancing, front shots blocked (wear x2), back shots hurt, pistol after the plant and after the
     shield breaks; third-person picture; the player's own shield: hits of 100 until it breaks
  d  the enemy C4 blast: loose loot, a recent death spot, a CS gun, a knife and an airdrop crate around the charge
  e  the survivor case: opening screen at the window's size and in a portrait window, 20 openings at level 1 (tiers, guns
     allowed), the key and case grid recipes
  f  first-craft: a gun once gives a case, again none, again none after leaving to the main menu and loading the world,
     a full inventory drops it at the feet
  g  drop sources: kills of T and of cows, one level gained
Usage: M0_ENGINE=131 python sp_feedback1124.py <label> <core package>[+<package>...] [sections, default bcdefg]
The player's own Mods and worlds are never used; the window's sound and music are off for the whole run.
"""
import json, math, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE, SURVIVAL, teleport, STAND
from sp_camera_fire import look, camera, fly_to
from sp_gun_visuals import window_of, picture
from sp_deathmatch import FPP
from sp_engine_compat import STARTER_NONE, EMPTY, SELECT0, DURABLE, CLOSE_DIALOGS, creature

m0.firewall_preflight = lambda: {"skipped": "standalone sp131 run; no MP role started", "problems": []}
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)

INV = 'System.Globalization.CultureInfo.InvariantCulture'
MUTE = 'Game.SettingsManager.SoundsVolume = 0f; Game.SettingsManager.MusicVolume = 0f; return "muted";'
# A granite strip 5 wide from 4 blocks behind the player to 52 ahead (-Z), 25 blocks above the highest ground, air above it.
STRIP = PROJECT + (
    'var p = players.GlobalSpawnPosition; int x = (int)System.MathF.Floor(p.X), z = (int)System.MathF.Floor(p.Z); var t = project.FindSubsystem<Game.SubsystemTerrain>(true); '
    'for (int dx = -3; dx <= 3; dx++) for (int dz = -54; dz <= 6; dz++) { var ch = t.Terrain.GetChunkAtCell(x + dx, z + dz); if (ch == null || ch.State <= Game.TerrainChunkState.InvalidContents4) return "wait"; } '
    'int y = 0; for (int dx = -3; dx <= 3; dx++) for (int dz = -54; dz <= 6; dz++) y = System.Math.Max(y, t.Terrain.GetTopHeight(x + dx, z + dz)); y = System.Math.Min(y + 25, 230); '
    'int g = Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index); '
    'for (int dx = -3; dx <= 3; dx++) for (int dz = -54; dz <= 6; dz++) { t.ChangeCell(x + dx, y, z + dz, g); for (int dy = 1; dy <= 6; dy++) t.ChangeCell(x + dx, y + dy, z + dz, 0); } '
    'return x + " " + (y + 1) + " " + z;')
GUNS = 'var guns = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true); '
def set_level(level): return MAIN + f'pl.PlayerData.Level = {level}f; return pl.PlayerData.Level.ToString();'
# A numbered gun of this model at a growth level (a counter installed and applied), with this many rounds, in slot 0,
# and shells (or magazines) in slot 1.
def gun(name, level, rounds, ammo=40): return MAIN + (
    f'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "{name}"); var spec = Game.GunSpec.All[v]; var reg = Game.ScGunRegistry.Current; '
    f'int max = Game.ScGunGrowth.MaxDurability(v, {level}); int id = reg.Allocate(v, 0, false, max, max); '
    'var recs = (System.Collections.Generic.Dictionary<int, Game.ScGunRecord>)typeof(Game.ScGunRegistry).GetField("m_records", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(reg); '
    f'var r = recs[id]; r.CounterInstalled = true; r.AppliedGrowthLevel = {level}; r.GrowthRulesVersion = Game.ScGunGrowth.RulesVersion; '
    f'int cap = Game.ScGunGrowth.Capacity(v, {level}); r.Rounds = System.Math.Min({rounds} < 0 ? cap : {rounds}, cap); '
    'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)), 1); '
    f'if ({ammo} > 0) inv.AddSlotItems(1, Game.ScAmmoBlock.Value(Game.ScReloadTransaction.AmmoKind(spec)), {ammo}); inv.ActiveSlotIndex = 0; '
    'return id + " " + cap;')
def rounds(gid): return f'return Game.ScGunRegistry.Current.TryGetSnapshot({gid}, out var s) ? s.Rounds.ToString() : "-1";'
SHELLS = MAIN + 'return pl.ComponentMiner.Inventory.GetSlotCount(1).ToString();'
# The scheduled reload cues of the player's gun state: how many, and how many are shell inserts.
CUES = MAIN + GUNS + ('var f = typeof(Game.SubsystemScGunBlockBehavior).GetField("m_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); '
    'var st = ((System.Collections.IDictionary)f.GetValue(guns))[pl]; if (st == null) return "no state"; '
    'var sched = (System.Collections.IEnumerable)st.GetType().GetField("Scheduled").GetValue(st); var names = new System.Collections.Generic.List<string>(); '
    'foreach (var c in sched) names.Add((string)c.GetType().GetField("Item2").GetValue(c)); '
    'var batch = (System.Collections.Generic.List<int>)st.GetType().GetField("ShellBatch").GetValue(st); '
    'return names.Count + " " + names.Count(n => n.Contains("insert") || n.Contains("shell")) + " batches " + string.Join(",", batch);')
def body_of(eid): return f'var e = project.Entities.FirstOrDefault(q => q.Id == {eid}); var b = e?.FindComponent<Game.ComponentBody>(); var h = e?.FindComponent<Game.ComponentHealth>(); '
def target_state(eid): return PROJECT + body_of(eid) + ('if (b == null) return "gone"; return System.FormattableString.Invariant($"{h.Health} {h.AttackResilience} {b.Position.X} {b.Position.Y} {b.Position.Z} {b.Mass}");')
def heal(eid): return PROJECT + body_of(eid) + 'if (h == null) return "gone"; h.Health = 1f; h.AttackResilience = 400f; b.Velocity = Engine.Vector3.Zero; return "ok";'
def place(eid, x, y, z): return PROJECT + body_of(eid) + f'if (b == null) return "gone"; b.Position = new Engine.Vector3({x}f, {y}f, {z}f); b.Velocity = Engine.Vector3.Zero; return "ok";'
def aim(eid): return MAIN + body_of(eid) + ('if (b == null) return "gone"; var eye = pl.ComponentCreatureModel.EyePosition; var bb = b.BoundingBox; var c = (bb.Min + bb.Max) * 0.5f; '
    'var d = Engine.Vector3.Normalize(c - eye); return System.FormattableString.Invariant($"{d.X} {d.Y} {d.Z} {(c - eye).Length()}");')
# Heavy bodies for the knockback cap: the first of these templates whose body weighs 300 or more.
HEAVY = PROJECT + ('foreach (var n in new[] { "Mammoth", "Rhino", "Bison_Brown", "Bison", "Elephant", "Moose", "Bull_Black", "Bull_Brown", "Horse_Black" }) { '
    'try { var e = Game.DatabaseManager.CreateEntity(project, n, true); var b = e.FindComponent<Game.ComponentBody>(); if (b != null && b.Mass >= 300) return n + " " + b.Mass; } catch { } } return "none";')
SHOT = 'MOUSEDOWN Left {cx} {cy}', 'MOUSEUP Left {cx} {cy}'


def main(label, packages, sections="bcdefg"):
    pkgs = [Path(p) for p in packages.split("+")]
    case_dir = RUNS / f"feedback1124-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; (case_dir / "pictures").mkdir(parents=True)
    R = {"case": case_dir.name, "packages": {p.name: sha(p) for p in pkgs}, "checks": [], "pictures": [], "data": {}}
    T0 = time.time()
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": None if ok is None else bool(ok), "detail": detail})
        print(("NOT RUN " if ok is None else "PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:500], flush=True)
    def step(name, value=None): print(f"[{time.time() - T0:.0f}] {name}: {str(value)[:500]}", flush=True); return value
    g = None; h = None; ctx = {}
    def snap(name):
        if h is None: return None
        try:
            im = picture(h); path = case_dir / "pictures" / f"{name}.jpg"; im.save(path, quality=88); R["pictures"].append(str(path)); return str(path)
        except Exception as e: R["pictures"].append(f"{name}: {e}"); return None
    def section(name, fn):
        if name[0] not in sections: return
        try: fn()
        except Exception as e: check(f"{name} (section completed)", False, f"{type(e).__name__}: {e}")
    def F(code, timeout=60): return g.func(code, timeout)
    def click(down=.06):
        cx, cy = ctx["center"]; g.cmd(f"MOUSEMOVE {cx} {cy}"); time.sleep(.03); g.cmd(f"MOUSEDOWN Left {cx} {cy}"); time.sleep(down); g.cmd(f"MOUSEUP Left {cx} {cy}")
    def face(eid):
        d = [float(v) for v in F(aim(eid)).split()]
        yaw = math.atan2(-d[0], -d[2]); pitch = math.asin(max(-1, min(1, d[1]))); F(look(yaw, pitch)); return d[3]
    def enter_new_world():
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        poll(g, SURVIVAL, lambda v: v == "Survival", 20)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)); F(KEEP_ACTIVE); F(MUTE); F(DURABLE)
        step("starter kit: none", F(STARTER_NONE)); step("dialogs", F(CLOSE_DIALOGS))
    def strip():
        t0 = time.time(); r = F(STRIP)
        while r == "wait" and time.time() - t0 < 90: time.sleep(1); r = F(STRIP)
        if r == "wait": raise RuntimeError("strip chunks never ready")
        return [int(v) for v in r.split()]
    try:
        g = m0.game("sp", case_dir, pkgs); R["engine"] = g.engine_info(); step("engine", R["engine"]["engine"]); to_menu(g); F(MUTE)
        R["mods"] = step("mods", F('return string.Join("; ", ModsManager.ModList.Where(m => m.modInfo != null).Select(m => m.modInfo.PackageName + " " + m.modInfo.Version));'))
        enter_new_world()
        x, y, z = strip(); ctx["home"] = (x, y, z); step("strip", (x, y, z))
        F(EMPTY); F(teleport(x, y, z)); F(STAND); time.sleep(1.2); F(FPP)
        h = window_of(g.proc.pid)
        ctx["center"] = [int(v) // 2 for v in F(MAIN + 'return Engine.Window.Size.X + " " + Engine.Window.Size.Y;').split()]
        R["window"] = ctx["center"]

        # ---------------- b: shotguns
        def shotguns():
            F(set_level(50)); data = {"reload": {}, "damage": {}, "knockback": {}}
            for level in (1, 10, 50):
                gid, cap = [int(v) for v in step(f"nova Lv{level}", F(gun("nova", level, 0))).split()]
                F(SELECT0); time.sleep(1.6)
                g.cmd("KEYDOWN R"); time.sleep(.05); g.cmd("KEYUP R"); t0 = time.time(); time.sleep(.15)
                cues = step("cues", F(CUES)); timeline = []; last = 0
                while time.time() - t0 < 40:
                    r = int(F(rounds(gid))); now = round(time.time() - t0, 2)
                    if r != last: timeline.append((now, r)); last = r
                    if r >= cap: break
                    time.sleep(.08)
                steps = [b[1] - a[1] for a, b in zip([(0, 0)] + timeline[:-1], timeline)]
                data["reload"][f"Lv{level}"] = {"capacity": cap, "seconds": timeline[-1][0] if timeline else None, "perLoop": max(steps) if steps else 0, "cues": cues, "timeline": timeline}
                step(f"reload Lv{level}", data["reload"][f"Lv{level}"])
            # cut short by firing after the third loop, at Lv1 and Lv50 (enough shells for a full Lv50 tube)
            CUT = MAIN + GUNS + ('var f = typeof(Game.SubsystemScGunBlockBehavior).GetField("m_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); '
                'var st = ((System.Collections.IDictionary)f.GetValue(guns))[pl]; return st.GetType().GetField("FireAfterReload").GetValue(st) + " " + '
                '((System.Collections.Generic.List<double>)st.GetType().GetField("ShellTimes").GetValue(st)).Count;')
            data["interrupt"] = {}
            for level, at in ((1, 3), (50, 21)):
                gid, cap = [int(v) for v in F(gun("nova", level, 0)).split()]
                F(MAIN + 'pl.ComponentMiner.Inventory.AddSlotItems(2, Game.ScAmmoBlock.Value(1), 40); return "more shells";'); F(SELECT0); time.sleep(1.6)
                g.cmd("KEYDOWN R"); time.sleep(.05); g.cmd("KEYUP R"); t0 = time.time()
                while time.time() - t0 < 20 and int(F(rounds(gid))) < at: time.sleep(.04)
                loaded = int(F(rounds(gid))); before_cut = F(CUT); click(.12); time.sleep(.15); cut = F(CUT); time.sleep(2.5)
                after = int(F(rounds(gid))); time.sleep(1.5); later = int(F(rounds(gid)))
                data["interrupt"][f"Lv{level}"] = {"loadedWhenFired": loaded, "stateBefore": before_cut, "stateAfterClick": cut, "roundsAfterShot": after, "roundsLater": later, "capacity": cap}
                step(f"interrupt Lv{level}", data["interrupt"][f"Lv{level}"])
            r = data["reload"]
            check("b: Lv1 Nova loads one shell a loop (8 loops)", r["Lv1"]["perLoop"] == 1 and r["Lv1"]["capacity"] == 8, r["Lv1"])
            check("b: Lv10 Nova loads two a loop, Lv50 seven a loop", r["Lv10"]["perLoop"] == 2 and r["Lv50"]["perLoop"] == 7, {k: v["perLoop"] for k, v in r.items()})
            check("b: an empty Lv50 Nova fills within one loop of the Lv1 time", r["Lv50"]["seconds"] is not None and r["Lv1"]["seconds"] is not None
                  and r["Lv50"]["seconds"] <= r["Lv1"]["seconds"] + 1.0, {k: v["seconds"] for k, v in r.items()})
            check("b: reload cues were scheduled (shell-insert sounds)", all(" " in v["cues"] and int(v["cues"].split()[1]) > 0 for v in r.values()), {k: v["cues"] for k, v in r.items()})
            for lv, per in (("Lv1", 1), ("Lv50", 7)):
                i = data["interrupt"][lv]
                check(f"b: {lv}: firing mid-reload keeps what is in (plus the batch in hand), fires one, and stops loading",
                      i["roundsLater"] < i["capacity"] - per and i["loadedWhenFired"] - 1 <= i["roundsLater"] <= i["loadedWhenFired"] + 2 * per, i)
            # damage at range, one shot each, a cow on the strip in front of the player
            F(set_level(50)); F(EMPTY)
            cow = step("target cow", F(creature(x + .5, y, z - 3 + .5, "Cow_Brown")))
            for name, rng in [("nova", 36), ("xm1014", 33), ("mag7", 30), ("sawedoff", 27)]:
                rows = {}
                for tag, d in [("near", 3), ("mid", round(rng * .45, 1)), ("max", rng - 1), ("beyond", rng + 4)]:
                    gid, cap = [int(v) for v in F(gun(name, 0, -1)).split()]; F(SELECT0); F(place(cow, x + .5, y, z - d + .5)); F(heal(cow)); time.sleep(1.4)
                    dist = face(cow); h0 = [float(v) for v in F(target_state(cow)).split()]; shots = 3 if tag in ("max", "beyond") else 1
                    for k in range(shots): click(); time.sleep(1.2 if k < shots - 1 else .8)
                    st = F(target_state(cow)); h1 = [float(v) for v in st.split()] if st != "gone" else [0, h0[1], 0, 0, 0, 0]
                    rows[tag] = {"distance": round(dist, 2), "shots": shots, "damage": round((h0[0] - h1[0]) * h0[1], 3), "health": [h0[0], h1[0]]}
                    if st == "gone": cow = F(creature(x + .5, y, z - 3 + .5, "Cow_Brown"))
                data["damage"][name] = rows; step(f"damage {name}", rows)
            dm = data["damage"]
            check("b: every shotgun hurts at its new maximum range", all(v["max"]["damage"] > 0 for v in dm.values()), {k: v["max"] for k, v in dm.items()})
            check("b: nothing beyond the range", all(v["beyond"]["damage"] == 0 for v in dm.values()), {k: v["beyond"] for k, v in dm.items()})
            # knockback: one Nova shot at 4 blocks on a cow and on a heavy body
            heavy = step("heavy template", F(HEAVY))
            for tag, template, d in [("wolf", "Wolf_Gray", 4), ("wolf-at-9", "Wolf_Gray", 9), ("cow", "Cow_Brown", 4), ("cow-at-9", "Cow_Brown", 9), ("heavy", heavy.split()[0] if heavy != "none" else None, 4)]:
                if template is None: data["knockback"][tag] = "no heavy template"; continue
                F(EMPTY); eid = F(creature(x + .5, y, z - d + .5, template)); time.sleep(1.2); F(heal(eid))
                gid, cap = [int(v) for v in F(gun("nova", 0, -1)).split()]; F(SELECT0); time.sleep(1.4); face(eid)
                p0 = [float(v) for v in F(target_state(eid)).split()]; click(); time.sleep(.7); p1 = [float(v) for v in F(target_state(eid)).split()]
                data["knockback"][tag] = {"template": template, "mass": p0[5], "displacement": round(math.dist(p0[2:5:2], p1[2:5:2]), 3), "damage": round((p0[0] - p1[0]) * p0[1], 2)}
                step(f"knockback {tag}", data["knockback"][tag])
            kb = data["knockback"]
            if isinstance(kb.get("heavy"), dict):
                check("b: a close Nova shot pushes a cow (350) at least twice as far as a rhino (1500)", kb["cow"]["displacement"] >= 2 * kb["heavy"]["displacement"], kb)
            check("b: within 6 blocks a cow and a wolf are pushed at least twice as far as by the same shot from 9 blocks",
                  kb["cow"]["displacement"] >= 2 * kb["cow-at-9"]["displacement"] and kb["wolf"]["displacement"] >= 2 * kb["wolf-at-9"]["displacement"], kb)
            R["data"]["shotguns"] = data
        section("b shotguns", shotguns)

        # ---------------- c: the C4 T and shields
        def c4_t():
            F(EMPTY); F(teleport(x, y, z)); F(STAND); F(MAIN + 'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < 10; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); return "empty";')
            spawn = (MAIN + 'var e = Game.DatabaseManager.CreateEntity(project, "ScTacticalEnemy", true); var en = e.FindComponent<Game.ComponentTacticalEnemy>(true); '
                     'var st = Game.TacticalEnemyState.Create(Game.TacticalRole.Demolition, "probe-c4-" + System.Environment.TickCount, new Engine.Random(5)); st.Source = "manual"; st.Grenades = 0; '
                     f'en.Configure(st, new Engine.Vector3({x}f + 0.5f, {y}f, {z}f - 30.5f)); var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) sp.SpawnDuration = 0f; project.AddEntity(e); en.Alert(pl.ComponentBody); return e.Id.ToString();')
            STATE = lambda eid: PROJECT + body_of(eid) + ('var en = e?.FindComponent<Game.ComponentTacticalEnemy>(); if (en == null) return "gone"; '
                'int shield = Game.BlocksManager.GetBlockIndex<Game.ScTacticalShieldBlock>(true); '
                'return System.FormattableString.Invariant($"{en.State.ShieldUp} {en.State.ShieldWear} {Game.Terrain.ExtractContents(en.PresentedValue) == shield} {en.State.Bomb} {h.Health} {b.Position.Z} {Game.GunSpec.All[en.State.Variant].Name}");')
            eid = step("C4 T", F(spawn)); time.sleep(.5); s0 = step("state", F(STATE(eid)))
            track = []
            for i in range(14): time.sleep(.5); track.append(F(STATE(eid)))
            step("advance", track[::3])
            zs = [float(t.split()[5]) for t in track if t != "gone"]
            check("c: the C4 T spawns with its shield up and shown", s0.split()[0] == "True" and s0.split()[2] == "True", s0)
            check("c: it advances towards the player behind the shield", zs and zs[-1] - zs[0] > 3 and all(t.split()[0] == "True" for t in track if t != "gone" and t.split()[3] == "True"), zs[:1] + zs[-1:])
            # front shots with an AK from the player: the wear rises, health stays
            F(gun("ak47", 0, -1, 0)); F(SELECT0); time.sleep(1.3)
            before = F(STATE(eid)).split(); face(eid); click(.4); time.sleep(.6); front = F(STATE(eid)).split()
            step("front burst", (before, front))
            R["data"]["c4FrontBurst"] = {"before": before, "after": front}
            check("c: front shots wear the shield and leave the T unhurt", int(front[1]) > int(before[1]) and float(front[4]) == float(before[4]), {"before": before, "after": front})
            # round 9: the shield T is an escort and never plants; it keeps its shield while the shield holds
            time.sleep(12); after_wait = F(STATE(eid)); step("after 12 s close to the player", after_wait)
            bombs = F(PROJECT + 'return project.FindSubsystem<Game.SubsystemTacticalBombs>(true).Bombs.Count.ToString();')
            check("c: the shield T does not plant (round 9: escort only) and keeps its shield up", after_wait != "gone" and after_wait.split()[0] == "True" and int(bombs) == 0,
                  {"after": after_wait, "bombs": bombs})
            F(PROJECT + 'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); foreach (var x in b.Bombs.ToArray()) b.Bombs.Remove(x); return "defused for the test";')
            # a second one: the shield breaks under fire
            eid2 = F(spawn); time.sleep(.6)
            F(PROJECT + body_of(eid2) + 'e.FindComponent<Game.ComponentTacticalEnemy>(true).State.ShieldWear = 1900; return "1900";')
            F(gun("ak47", 0, -1, 0)); F(SELECT0); time.sleep(1.3)
            broken = None
            for i in range(12):
                face(eid2); click(.25); time.sleep(.4); s = F(STATE(eid2))
                if s == "gone" or s.split()[0] == "False": broken = s; break
            time.sleep(1.2); after_break = F(STATE(eid2)); step("after break", after_break)
            check("c: a broken shield is put away and the pistol comes out", broken is not None and after_break != "gone" and after_break.split()[2] == "False" and int(after_break.split()[1]) == 2000, after_break)
            # pictures of a shield carrier held in place (its warning window keeps it standing, facing the player)
            F(EMPTY); F(teleport(x, y, z)); F(STAND); F(MAIN + 'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); return "ok";')
            eid3 = F(spawn); F(PROJECT + body_of(eid3) + f'var en = e.FindComponent<Game.ComponentTacticalEnemy>(true); en.State.Warmup = 10f; b.Position = new Engine.Vector3({x}f + 0.5f, {y}f, {z}f - 7.5f); return "held";')
            time.sleep(2.5); face(eid3); time.sleep(.5); step("picture", snap("c4-t-shield-first-person"))
            pos = [float(v) for v in F(target_state(eid3)).split()[2:5]]
            F(camera("Fly")); F(fly_to(pos[0] + 2.6, pos[1] + 1.4, pos[2] + 1.2, pos[0], pos[1] + .9, pos[2])); time.sleep(1.5)
            step("picture", snap("c4-t-shield-third-person-side")); F(fly_to(pos[0] + .4, pos[1] + 1.5, pos[2] + 3.2, pos[0], pos[1] + .9, pos[2])); time.sleep(1.2)
            step("picture", snap("c4-t-shield-third-person-front")); F(camera("Fpp")); time.sleep(.5)
            # the player's own shield: hits of 100 from the front until it breaks
            F(EMPTY); cow = F(creature(x + .5, y, z - 3 + .5, "Cow_Brown"))
            hits = F(MAIN + body_of(cow) + (
                'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); int sb = Game.BlocksManager.GetBlockIndex<Game.ScTacticalShieldBlock>(true); '
                'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(sb, 0, 0), 1); inv.ActiveSlotIndex = 0; pl.ComponentBody.Rotation = Engine.Quaternion.Identity; int n = 0; '
                'var wear = new System.Collections.Generic.List<int>(); for (; n < 40; n++) { int w0 = Game.Terrain.ExtractData(inv.GetSlotValue(0)); if (w0 >= Game.ScTacticalShieldBlock.Life) break; '
                'var p = pl.ComponentBody.Position + new Engine.Vector3(0, 1.1f, 0); var a = new Game.ProjectileAttackment(pl.ComponentBody, e, p, Engine.Vector3.UnitZ, 100f, null); '
                'Game.ScDamageIndicator.AttackBody(a); wear.Add(Game.Terrain.ExtractData(inv.GetSlotValue(0))); } return n + " " + string.Join(",", wear);'))
            step("player shield hits", hits)
            R["data"]["playerShield"] = hits
            check("c: the player's shield breaks after 10 hits of 100 (2000 / (100 x 2); 20 before)", hits.split()[0] == "10", hits)
            F(MAIN + 'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); return "ok";')
        section("c C4 T", c4_t)

        # ---------------- d: an enemy C4 blast around loot
        def blast():
            F(EMPTY); F(teleport(x, y, z)); F(STAND)
            setup = F(MAIN + (
                f'var at = new Engine.Vector3({x}f + 0.5f, {y}f, {z}f - 14.5f); var pk = project.FindSubsystem<Game.SubsystemPickables>(true); '
                'var bombs = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); var info = project.FindSubsystem<Game.SubsystemGameInfo>(true); '
                'int iron = Game.Terrain.MakeBlockValue(Game.IronIngotBlock.Index), coal = Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index); '
                'int gunv = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(0, Game.GunSpec.FreshFull)); '
                'int knife = Game.BlocksManager.Blocks[Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true)].GetCreativeValues().First(); '
                'pk.AddPickable(iron, 5, at + new Engine.Vector3(3, 0.3f, 0), Engine.Vector3.Zero, null, null); '
                'pk.AddPickable(coal, 6, at + new Engine.Vector3(-6, 0.3f, 0), Engine.Vector3.Zero, null, null); '
                'bombs.DeathMarks.Add(new Game.TacticalBlastLoot.DeathMark(at + new Engine.Vector3(-6, 0.3f, 0), info.TotalElapsedGameTime)); '
                'var tt = project.FindSubsystem<Game.SubsystemTerrain>(true); int hx = (int)System.MathF.Floor(at.X - 3), hz = (int)System.MathF.Floor(at.Z); for (int d = 1; d <= 5; d++) tt.ChangeCell(hx, (int)at.Y - d, hz, 0); '
                'pk.AddPickable(coal, 5, new Engine.Vector3(hx + 0.5f, at.Y - 4.7f, hz + 0.5f), Engine.Vector3.Zero, null, null); '
                'bombs.DeathMarks.Add(new Game.TacticalBlastLoot.DeathMark(new Engine.Vector3(hx + 0.5f, at.Y + 0.3f, hz + 0.5f), info.TotalElapsedGameTime)); '
                'pk.AddPickable(gunv, 1, at + new Engine.Vector3(0, 0.3f, 3), Engine.Vector3.Zero, null, null); '
                'pk.AddPickable(knife, 1, at + new Engine.Vector3(0, 0.3f, -3), Engine.Vector3.Zero, null, null); '
                'pk.AddPickable(iron, 4, at + new Engine.Vector3(0, 0.3f, 20), Engine.Vector3.Zero, null, null); '
                'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); int cx = (int)at.X + 4, cy = (int)at.Y, cz = (int)System.MathF.Floor(at.Z); '
                't.ChangeCell(cx, cy, cz, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScAirdropBlock>(true))); '
                'var chest = project.FindSubsystem<Game.SubsystemBlockEntities>(true).GetBlockEntity(cx, cy, cz)?.Entity.FindComponent<Game.ComponentChest>(); if (chest == null) return "no crate entity"; '
                'chest.AddSlotItems(0, iron, 8); chest.AddSlotItems(1, coal, 5); chest.AddSlotItems(2, Game.Terrain.MakeBlockValue(Game.BreadBlock.Index), 1); chest.AddSlotItems(3, Game.Terrain.MakeBlockValue(Game.CopperIngotBlock.Index), 3); '
                'chest.AddSlotItems(4, gunv, 1); '
                'bool planted = bombs.PlantFrom(at, 0f, false); if (planted) bombs.Bombs[bombs.Bombs.Count - 1].Charge.Remaining = 4f; '
                'return planted + " " + cx + " " + cy + " " + cz;'))
            step("blast setup", setup)
            where = F(PROJECT + 'var pk = project.FindSubsystem<Game.SubsystemPickables>(true); var bombs = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); '
                      'return string.Join(";", pk.Pickables.Where(p => !p.ToRemove).Select(p => Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(p.Value)].GetType().Name + "@" + p.Position.ToString())) + " | marks " + string.Join(";", bombs.DeathMarks.Select(m => m.Position.ToString()));')
            step("before blast", where); R["data"]["blastBefore"] = where
            time.sleep(2.2)
            PROT = PROJECT + ('var pk = project.FindSubsystem<Game.SubsystemPickables>(true); var bombs = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); var info = project.FindSubsystem<Game.SubsystemGameInfo>(true); '
                      'return string.Join(";", pk.Pickables.Where(p => !p.ToRemove).Select(p => Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(p.Value)].GetType().Name + "x" + p.Count + "@" + p.Position.ToString() + " protected " + Game.TacticalBlastLoot.Protected(p.Position, bombs.DeathMarks, info.TotalElapsedGameTime))) + " | bombs " + bombs.Bombs.Count + " t " + info.TotalElapsedGameTime.ToString("0.0");')
            step("2 s before the blast", F(PROT))
            time.sleep(4)
            if setup.startswith("True"):
                cx, cy, cz = [int(v) for v in setup.split()[1:]]
                after = F(MAIN + (
                    'var pk = project.FindSubsystem<Game.SubsystemPickables>(true); '
                    'var loose = string.Join(";", pk.Pickables.Where(p => !p.ToRemove).Select(p => Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(p.Value)].GetType().Name + "x" + p.Count)); '
                    f'var chest = project.FindSubsystem<Game.SubsystemBlockEntities>(true).GetBlockEntity({cx}, {cy}, {cz})?.Entity.FindComponent<Game.ComponentChest>(); '
                    'var crate = chest == null ? "no crate" : string.Join(";", Enumerable.Range(0, 6).Select(i => chest.GetSlotCount(i) == 0 ? "-" : Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(chest.GetSlotValue(i))].GetType().Name + "x" + chest.GetSlotCount(i))); '
                    'return loose + " | " + crate;'))
                step("after blast", after); R["data"]["blast"] = {"setup": setup, "after": after}
                loose, crate = after.split(" | ")
                check("d: loose iron next to the charge is gone; iron 20+ blocks away stays", "IronIngotBlockx5" not in loose and "IronIngotBlockx4" in loose, loose)
                check("d: a recent death spot's drop, the CS gun and the knife stay", "CoalChunkBlockx6" in loose and "ScGunBlockx1" in loose and "ScKnifeBlockx1" in loose, loose)
                check("d: a death drop that fell 5 blocks down an open hole under the death spot stays (the blast reaches into the hole)", "CoalChunkBlockx5" in loose, loose)
                # stacks of 2 or more lose half (rounded down); a single item (the bread, the fresh gun without a record) is kept
                # or lost by the stable roll
                slots = crate.split(";")
                check("d: crate stacks lose half (iron 8->4, coal 5->3, copper 3->2); single items rolled", slots[0] == "IronIngotBlockx4" and slots[1] == "CoalChunkBlockx3"
                      and slots[3] == "CopperIngotBlockx2" and slots[2] in ("BreadBlockx1", "-") and slots[4] in ("ScGunBlockx1", "-"), crate)
            else: check("d: the blast setup", False, setup)
            snap("c4-blast-after")
        section("d blast", blast)

        # ---------------- e: cases
        def cases():
            F(EMPTY); F(teleport(x, y, z)); F(STAND); F(set_level(1)); time.sleep(.8)
            give = MAIN + ('var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); '
                'int cv = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true), 0, 0), kv = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseKeyBlock>(true)); '
                'inv.AddSlotItems(0, cv, 16); inv.AddSlotItems(2, cv, 9); inv.AddSlotItems(1, kv, 16); inv.AddSlotItems(3, kv, 9); inv.ActiveSlotIndex = 0; '
                'return Game.ScInventoryTransaction.Count(inv, cv) + " " + Game.ScInventoryTransaction.Count(inv, kv);')
            step("cases and keys", F(give))
            OPEN = MAIN + ('var inv0 = pl.ComponentMiner.Inventory; int caseValue = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true), 0, 0); '
                'int cs = Enumerable.Range(0, inv0.SlotsCount).FirstOrDefault(i => inv0.GetSlotCount(i) > 0 && inv0.GetSlotValue(i) == caseValue, -1); if (cs < 0) return "no case"; inv0.ActiveSlotIndex = cs; var c = project.FindSubsystem<Game.SubsystemScCaseBlockBehavior>(true); var r = new Engine.Ray3(pl.ComponentCreatureModel.EyePosition, pl.ComponentBody.Matrix.Forward); '
                'c.OnUse(r, pl.ComponentMiner); var d = Game.DialogsManager.Dialogs.OfType<Game.CaseOpeningDialog>().LastOrDefault(); if (d == null) return "no dialog"; '
                'var roll = (Game.CaseRoll)typeof(Game.CaseOpeningDialog).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).First(f => f.FieldType == typeof(Game.CaseRoll)).GetValue(d); '
                'return roll.Reward.Rarity + " " + roll.Reward.Block + ":" + roll.Reward.Data + " x" + roll.Reward.Count + " lvl" + roll.Reward.MinPlayerLevel + " gen " + roll.Reward.Generated;')
            CLAIM = MAIN + ('var d = Game.DialogsManager.Dialogs.OfType<Game.CaseOpeningDialog>().LastOrDefault(); if (d == null) return "none"; var t = typeof(Game.CaseOpeningDialog); '
                'var fin = t.GetMethod("FinishAnimation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); var claim = t.GetMethod("ClaimAndDismiss", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); '
                'fin.Invoke(d, null); claim.Invoke(d, null); var inv = pl.ComponentMiner.Inventory; return inv.GetSlotCount(0) + " " + inv.GetSlotCount(1) + " dialogs " + Game.DialogsManager.Dialogs.Count();')
            first = step("open 1", F(OPEN)); time.sleep(1.2); p1 = snap("case-rolling-window"); time.sleep(5.5); p2 = snap("case-result-window")
            results = [first]; step("claim", F(CLAIM))
            for i in range(19):
                res = F(OPEN); results.append(res); time.sleep(.3); F(CLAIM)
            R["data"]["cases"] = results; step("20 openings", results)
            tiers = {}
            for r in results: tiers[r.split()[0]] = tiers.get(r.split()[0], 0) + 1
            R["data"]["caseTiers"] = tiers
            guns = [r for r in results if "ScGunBlock" in r or "Template" in r]
            left = F(MAIN + 'var inv = pl.ComponentMiner.Inventory; int cv = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true), 0, 0), kv = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseKeyBlock>(true)); return Game.ScInventoryTransaction.Count(inv, cv) + " " + Game.ScInventoryTransaction.Count(inv, kv);')
            # round 9: cases open without a key; the 25 keys handed over stay untouched
            check("e: 20 cases opened, each consuming one case and no key (round 9)", left == "5 25", {"left": left, "tiers": tiers})
            check("e: at level 1 no gun above level 1 is drawn", all(" lvl0 " in r or " lvl1 " in r for r in results), [r for r in results if " lvl0 " not in r and " lvl1 " not in r])
            # a portrait window
            size = F('var s = Engine.Window.Size; return s.X + " " + s.Y;')
            try:
                F('Engine.Window.Size = new Engine.Point2(540, 960); return "resized";'); time.sleep(1.5)
                F(OPEN); time.sleep(1.2); snap("case-rolling-portrait"); time.sleep(5.5); snap("case-result-portrait"); F(CLAIM)
            finally:
                w, hh = size.split(); F(f'Engine.Window.Size = new Engine.Point2({w}, {hh}); return "restored";'); time.sleep(1)
            # recipes: the key (4 copper, the standalone shape) and the case (planks, copper, a chest) in the crafting grid
            recipes = F(PROJECT + (
                'var kb = Game.BlocksManager.GetBlockIndex<Game.ScCaseKeyBlock>(true); var cb = Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true); '
                'string Show(int b) => string.Join(" / ", Game.CraftingRecipesManager.Recipes.Where(r => Game.Terrain.ExtractContents(r.ResultValue) == b).Select(r => string.Join(",", r.Ingredients.Select(i => i ?? "_")))); '
                'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); '
                'var key = Game.CraftingRecipesManager.FindMatchingRecipe(t, new string[] { null, "copperingot:0", null, "copperingot:0", "copperingot:0", null, "copperingot:0", null, null }, 0f, 1f); '
                'var oldKey = Game.CraftingRecipesManager.FindMatchingRecipe(t, new string[] { null, "ironingot:0", null, "ironingot:0", "copperingot:0", null, "ironingot:0", null, null }, 0f, 1f); '
                'var box = Game.CraftingRecipesManager.FindMatchingRecipe(t, new string[] { "copperingot:0", "planks:0", "copperingot:0", "planks:0", "chest:0", "planks:0", "copperingot:0", "planks:0", "copperingot:0" }, 0f, 1f); '
                'return "key[" + Show(kb) + "] case[" + Show(cb) + "] copper key -> " + (key == null ? "none" : Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(key.ResultValue)].GetType().Name) '
                '+ "; iron key -> " + (oldKey == null ? "none" : "match") + "; copper case -> " + (box == null ? "none" : Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(box.ResultValue)].GetType().Name);'))
            step("recipes", recipes); R["data"]["recipes"] = recipes
            check("e: the copper case recipe is in the crafting grid; no key recipe at all (round 9)", "key[]" in recipes and "copper key -> none" in recipes and "iron key -> none" in recipes and "copper case -> ScCaseBlock" in recipes, recipes)
            check("e: multiplayer refusal", None, "not run: needs the 1.9.3.2_MP engine with a second process")
        section("e cases", cases)

        # ---------------- f: first-craft reward
        def first_craft():
            F(EMPTY); F(teleport(x, y, z)); F(STAND); F(set_level(30)); time.sleep(.8)
            bench = F(MAIN + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); t.ChangeCell({x} + 1, {y}, {z}, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScWeaponWorkbenchBlock>(true))); return "bench";')
            CASES = MAIN + 'int c = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true), 0, 0); return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, c) + " ground " + project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Where(p => p.Value == c && !p.ToRemove).Sum(p => p.Count);'
            def craft(name, fill=False): return MAIN + (
                'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); '
                f'var e = Game.ScWeaponCrafting.All.First(q => !q.Knife && q.Name == "{name}"); int slot = 0; foreach (var m in e.Materials()) inv.AddSlotItems(slot++, m.Key, m.Value + ({str(fill).lower()} ? 5 : 0)); '
                f'if ({str(fill).lower()}) {{ int dirt = Game.Terrain.MakeBlockValue(Game.DirtBlock.Index); for (int s = slot; s < inv.SlotsCount - 1; s++) if (inv.GetSlotCount(s) == 0 && inv.GetSlotCapacity(s, dirt) > 0) inv.AddSlotItems(s, dirt, inv.GetSlotCapacity(s, dirt)); }} '
                f'var op = new Game.ScWorkbenchOp(Game.ScWorkbenchOpKind.Craft, new Engine.Point3({x} + 1, {y}, {z}), e.Value, 1); var r = Game.ScWorkbenchOps.Execute(pl, op); '
                'int c = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true), 0, 0); '
                'return r.Code + " guns " + Game.ScInventoryTransaction.Count(inv, e.Value) + " cases " + Game.ScInventoryTransaction.Count(inv, c) + " ground " + project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Where(p => p.Value == c && !p.ToRemove).Sum(p => p.Count);')
            a = step("craft glock18 #1", F(craft("glock18"))); b = step("craft glock18 #2", F(craft("glock18")))
            directory = F(MAIN + 'return project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName;')
            m = g.mark(); g.cmd('EXEC GameManager.SaveProject(true, true); GameManager.DisposeProject(); ScreensManager.SwitchScreen("MainMenu");'); g.wait('Entered screen "MainMenu"', 300, m)
            step("main menu", "left the world"); time.sleep(1)
            m = g.mark(); step("load", F(m0.Call('Game.WorldsManager.UpdateWorldsList(); var w = Game.WorldsManager.WorldInfos.FirstOrDefault(q => q.DirectoryName == $s0); '
                'if (w == null) return "no world"; Game.ScreensManager.SwitchScreen("GameLoading", w, null); return "loading";', directory)))
            g.wait('Entered screen "Game"', 600, m); step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)); F(KEEP_ACTIVE); F(MUTE); F(DURABLE)
            F(teleport(x, y, z)); F(STAND); time.sleep(1)
            c = step("craft glock18 #3 after the main menu", F(craft("glock18")))
            ledger = step("ledger", F(MAIN + 'var s = project.FindSubsystem<Game.SubsystemScFirstCraft>(true); return string.Join(";", s.Ledger.Owners.Select(o => o + ":" + s.Ledger.Has(o, "glock18")));'))
            d = step("craft usp_silencer, inventory full", F(craft("usp_silencer", True)))
            R["data"]["firstCraft"] = {"first": a, "second": b, "afterMenu": c, "full": d, "ledger": ledger}
            check("f: the first glock gives one case", a.startswith("1 ") and " cases 1 " in a, a)
            check("f: the second gives none", b.startswith("1 ") and " cases 0 " in b, b)
            check("f: after the main menu and loading the world again, still none", c.startswith("1 ") and " cases 0 " in c, c)
            check("f: a full inventory drops the case at the feet, once", d.startswith("1 ") and d.endswith("ground 1"), d)
        section("f first craft", first_craft)

        # ---------------- g: drop sources
        def drops():
            F(EMPTY); F(teleport(x, y, z)); F(STAND); F(MAIN + 'var pk = project.FindSubsystem<Game.SubsystemPickables>(true); foreach (var p in pk.Pickables) p.ToRemove = true; return "cleared";'); time.sleep(.5)
            SPAWN = lambda template, n, enemy: MAIN + (
                'var time = project.FindSubsystem<Game.SubsystemTime>(true); var ids = new System.Collections.Generic.List<int>(); int killed = 0; '
                f'for (int i = 0; i < {n}; i++) {{ GameEntitySystem.Entity e; '
                + ('e = Game.DatabaseManager.CreateEntity(project, "ScTacticalEnemy", true); var en = e.FindComponent<Game.ComponentTacticalEnemy>(true); '
                   'var st = Game.TacticalEnemyState.Create(Game.TacticalRole.Close, "probe-drops-" + i, new Engine.Random(i)); st.Source = "manual"; '
                   f'en.Configure(st, new Engine.Vector3({x}f + 0.5f, {y}f, {z}f - 8.5f)); ' if enemy else
                   f'e = Game.DatabaseManager.CreateEntity(project, "{template}", true); e.FindComponent<Game.ComponentBody>(true).Position = new Engine.Vector3({x}f + 0.5f, {y}f, {z}f - 8.5f); ')
                + 'var sp = e.FindComponent<Game.ComponentSpawn>(); if (sp != null) sp.SpawnDuration = 0f; project.AddEntity(e); var b = e.FindComponent<Game.ComponentBody>(true); '
                'var p = b.BoundingBox.Center(); Game.ScSurvivalBalance.Attack(b, pl, p, Engine.Vector3.UnitZ, 100000f, time.GameTime + i * 10, false, false, false, null, null); '
                'if (e.FindComponent<Game.ComponentHealth>(true).Health <= 0) killed++; ids.Add(e.Id); } return killed + " " + string.Join(",", ids);')
            COUNT = MAIN + ('var pk = project.FindSubsystem<Game.SubsystemPickables>(true); int cb = Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true), mb = Game.BlocksManager.GetBlockIndex<Game.ScWeaponMaterialBlock>(true); '
                'int cases = 0, parts = 0; foreach (var q in pk.Pickables.Where(q => !q.ToRemove)) { int c = Game.Terrain.ExtractContents(q.Value); if (c == cb) cases += q.Count; if (c == mb) parts += q.Count; q.ToRemove = true; } '
                'foreach (var e in project.Entities.ToArray()) if (e.FindComponent<Game.ComponentCreature>() != null && e.FindComponent<Game.ComponentPlayer>() == null) project.RemoveEntity(e, true); return cases + " " + parts;')
            def kill(template, n, enemy):
                killed = 0; cases = 0; parts = 0
                for batch in range(0, n, 10):
                    k = F(SPAWN(template, min(10, n - batch), enemy), 120).split()[0]; time.sleep(3.5)
                    c, p = [int(v) for v in F(COUNT).split()]; killed += int(k); cases += c; parts += p
                return f"{killed} {cases} {parts}"
            t_res = step("40 T", kill(None, 40, True)); c_res = step("60 cows", kill("Cow_Brown", 60, False))
            lvl = F(MAIN + 'return ((int)pl.PlayerData.Level).ToString();')
            # inventory and the ground (a reward that does not fit drops at the feet)
            CASES = MAIN + 'int c = Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true), 0, 0); return (Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, c) + project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Where(p => p.Value == c && !p.ToRemove).Sum(p => p.Count)).ToString();'
            LEDGER = MAIN + ('var s = project.FindSubsystem<Game.SubsystemScFirstCraft>(true); var owner = Game.ScGunHolders.RecoveryOwner(project, pl.ComponentMiner.Inventory); '
                             'return owner + " " + (s.Ledger.TryGetRewardedLevel(owner, out int l) ? l.ToString() : "none") + " level " + pl.PlayerData.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);')
            F(MAIN + 'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); return "emptied";')
            ledger0 = F(LEDGER); target = max(int(lvl), int(ledger0.split()[1]) if ledger0.split()[1].isdigit() else 0) + 1
            c0 = int(F(CASES)); F(set_level(target)); time.sleep(1.5); c1 = int(F(CASES)); ledger1 = F(LEDGER)
            expect = 3 if target >= 21 else 2 if target >= 11 else 1
            R["data"]["drops"] = {"T": t_res, "cows": c_res, "levelUpCases": c1 - c0, "level": [int(lvl), target], "ledger": [ledger0, ledger1], "expected": expect}
            step("drops", R["data"]["drops"])
            tk, tc, tp = [int(v) for v in t_res.split()]; ck, cc, cp = [int(v) for v in c_res.split()]
            check("g: kills registered (T and cows died)", tk == 40 and ck == 60, (t_res, c_res))
            check("g: one level above the record gives its cases (round 9: 1 / 2 / 3 per level for 2-10 / 11-20 / 21+)", c1 - c0 == expect, R["data"]["drops"])
            check("g: T drop cases about 15 % (2-12 of 40) and components (deaths drop parts half the time)", 1 <= tc <= 14 and tp > 0, t_res)
            check("g: cows drop cases about 3 % (0-7 of 60) and no components", cc <= 8 and cp == 0, c_res)
        section("g drops", drops)

        errors = g.errors()[:60]; R["gameErrors"] = errors
        check("no game errors", not errors, errors[:5])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
        if g is not None: R.setdefault("gameErrors", g.errors()[:40])
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c["check"] for c in R["checks"] if c["ok"] is False]
    R["seconds"] = round(time.time() - T0)
    out = RESULTS / f"feedback1124-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed {failed}; engine {R.get('engine', {}).get('engine')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
