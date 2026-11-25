"""1.6.0 round 9 (user decisions 2026-11-25) in the real game, isolated 1.9.3.1 copy, muted, cursor released by the harness:
  s  squads: five-man composition (shield escort + pistol bomber), escort keeps between the threat and the bomber (positions
     every 0.5 s, pictures from above and behind), the shield covers the bomber, the bomber plants, the plant alert (PC and
     portrait pictures, cue intervals), C4 drops (bomber before the plant: 1, after: 0, escort: 0), the player picks the
     dropped C4 up and plants it, a three-man squad, no T other than a bomber ever plants
  x  enemy blast: drops on a lower step, down an open hole, behind a wall, in an airdrop crate
  k  cases without keys: 25 openings with no key in the inventory, no key in the creative list or recipes, rewards only CS
     and vanilla (other mods' packages given on the command line are loaded)
  l  level-up cases: one level, a jump, the same levels again after dying, losing levels and regaining them, a full
     inventory, leaving to the main menu and back, leaving without saving and back
  i  iron: the metal part and mechanical parts recipes at the workbench
Usage: M0_ENGINE=131 python sp_round9.py <label> <core package>[+<package>...] [sections, default sxkli]
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

MUTE = 'Game.SettingsManager.SoundsVolume = 0f; Game.SettingsManager.MusicVolume = 0f; return "muted";'
STRIP = PROJECT + (
    'var p = players.GlobalSpawnPosition; int x = (int)System.MathF.Floor(p.X), z = (int)System.MathF.Floor(p.Z); var t = project.FindSubsystem<Game.SubsystemTerrain>(true); '
    'for (int dx = -12; dx <= 12; dx++) for (int dz = -60; dz <= 8; dz++) { var ch = t.Terrain.GetChunkAtCell(x + dx, z + dz); if (ch == null || ch.State <= Game.TerrainChunkState.InvalidContents4) return "wait"; } '
    'int y = 0; for (int dx = -12; dx <= 12; dx++) for (int dz = -60; dz <= 8; dz++) y = System.Math.Max(y, t.Terrain.GetTopHeight(x + dx, z + dz)); y = System.Math.Min(y + 22, 225); '
    'int g = Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index); '
    'for (int dx = -12; dx <= 12; dx++) for (int dz = -60; dz <= 8; dz++) { t.ChangeCell(x + dx, y, z + dz, g); for (int dy = 1; dy <= 8; dy++) t.ChangeCell(x + dx, y + dy, z + dz, 0); } '
    'return x + " " + (y + 1) + " " + z;')
def body_of(eid): return f'var e = project.Entities.FirstOrDefault(q => q.Id == {eid}); var b = e?.FindComponent<Game.ComponentBody>(); var h = e?.FindComponent<Game.ComponentHealth>(); '
def set_level(level): return MAIN + f'pl.PlayerData.Level = {level}f; return pl.PlayerData.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);'
CASE = 'Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScCaseBlock>(true), 0, 0)'
CASES = MAIN + f'int c = {CASE}; return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, c) + " " + project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Where(p => p.Value == c && !p.ToRemove).Sum(p => p.Count);'
CLEAR_INV = MAIN + 'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); return "emptied";'
CLEAR_DROPS = PROJECT + 'foreach (var p in project.FindSubsystem<Game.SubsystemPickables>(true).Pickables) p.ToRemove = true; return "cleared";'
LEDGER = MAIN + ('var s = project.FindSubsystem<Game.SubsystemScFirstCraft>(true); var owner = Game.ScGunHolders.RecoveryOwner(project, pl.ComponentMiner.Inventory); '
                 'return owner + " " + (s.Ledger.TryGetRewardedLevel(owner, out int l) ? l.ToString() : "none");')
# The squad members of the latest squad: id role bomber shield carry weapon x z health wear
def squad_state(squad=None): return PROJECT + (
    'var dir = project.FindSubsystem<Game.SubsystemTacticalEnemies>(true); var all = dir.Enemies.Where(e => e.State != null).ToList(); '
    + (f'var sq = "{squad}"; ' if squad else 'var sq = all.Count == 0 ? "" : all.OrderByDescending(e => e.Entity.Id).First().State.Squad; ')
    + 'return sq + "|" + string.Join(";", all.Where(e => e.State.Squad == sq).Select(e => System.FormattableString.Invariant('
    '$"{e.Entity.Id},{e.State.Role},{e.State.Bomber},{e.State.ShieldUp},{e.State.CarriesBomb},{Game.GunSpec.All[e.State.Weapon].Name},{e.Creature.ComponentBody.Position.X:0.00},{e.Creature.ComponentBody.Position.Z:0.00},{e.Creature.ComponentHealth.Health:0.000},{e.State.ShieldWear}")));')
BOMBS = PROJECT + 'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); return b.Bombs.Count + " " + string.Join(";", b.Bombs.Select(x => System.FormattableString.Invariant($"{x.Charge.Position.X:0.0},{x.Charge.Position.Z:0.0},{x.Charge.Remaining:0.0},{x.Charge.BeepLeft:0.00}")));'
C4_ITEMS = PROJECT + 'int c4 = Game.ScC4Block.Value; return project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Where(p => p.Value == c4 && !p.ToRemove).Sum(p => p.Count).ToString();'
ALERT = MAIN + ('var w = pl.ComponentGui.ControlsContainerWidget.AllChildren.OfType<Game.LabelWidget>().FirstOrDefault(l => l.IsVisible && l.Text != null && l.Text.Contains("C4") && l.Text.Contains("·") && !pl.ComponentGui.m_messageWidget.m_messages.Any(m => m.LabelWidget == l)); '
                'var msg = pl.ComponentGui.m_message; var small = string.Join(" / ", pl.ComponentGui.m_messageWidget.m_messages.Select(m => m.LabelWidget.Text).Where(t => t != null && t.Contains("C4"))); '
                'return (w == null ? "-" : w.Text) + " || " + (msg == null ? "" : msg.LargeText + " / " + msg.SmallText + " ") + small;')
def kill(eid): return MAIN + body_of(eid) + 'if (b == null) return "gone"; var time = project.FindSubsystem<Game.SubsystemTime>(true); Game.ScSurvivalBalance.Attack(b, pl, b.BoundingBox.Center(), Engine.Vector3.UnitZ, 1e6f, time.GameTime + 1000, false, false, false, null, null); return h.Health.ToString(System.Globalization.CultureInfo.InvariantCulture);'
def load(directory): return m0.Call(
    'Game.WorldsManager.UpdateWorldsList(); var w = Game.WorldsManager.WorldInfos.FirstOrDefault(x => x.DirectoryName == $s0); '
    'if (w == null) return "no world " + $s0; Game.ScreensManager.SwitchScreen("GameLoading", w, null); return "loading";', directory)


def main(label, packages, sections="sxkli"):
    pkgs = [Path(p) for p in packages.split("+")]
    case_dir = RUNS / f"round9-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; (case_dir / "pictures").mkdir(parents=True)
    R = {"case": case_dir.name, "packages": {p.name: sha(p) for p in pkgs}, "checks": [], "pictures": [], "data": {}}
    T0 = time.time(); g = None; h = None; ctx = {}
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": None if ok is None else bool(ok), "detail": detail})
        print(("NOT RUN " if ok is None else "PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:700], flush=True)
    def step(name, value=None): print(f"[{time.time() - T0:.0f}] {name}: {str(value)[:700]}", flush=True); return value
    def snap(name):
        nonlocal h
        try:
            h = h or window_of(g.proc.pid); im = picture(h); path = case_dir / "pictures" / f"{name}.jpg"; im.save(path, quality=88); R["pictures"].append(str(path)); return str(path)
        except Exception as e: R["pictures"].append(f"{name}: {e}"); return None
    def section(name, fn):
        if name[0] not in sections: return
        try: fn()
        except Exception as e: check(f"{name} (section completed)", False, f"{type(e).__name__}: {e}")
    def F(code, timeout=60): return g.func(code, timeout)
    def center(): return [int(v) // 2 for v in F(MAIN + 'return Engine.Window.Size.X + " " + Engine.Window.Size.Y;').split()]
    def click(down=.06):
        cx, cy = center(); g.cmd(f"MOUSEMOVE {cx} {cy}"); time.sleep(.03); g.cmd(f"MOUSEDOWN Left {cx} {cy}"); time.sleep(down); g.cmd(f"MOUSEUP Left {cx} {cy}")
    def face_point(px, py, pz):
        eye = [float(v) for v in F(MAIN + 'var e = pl.ComponentCreatureModel.EyePosition; return System.FormattableString.Invariant($"{e.X} {e.Y} {e.Z}");').split()]
        d = [px - eye[0], py - eye[1], pz - eye[2]]; n = math.sqrt(sum(v * v for v in d)) or 1; d = [v / n for v in d]
        F(look(math.atan2(-d[0], -d[2]), math.asin(max(-1, min(1, d[1])))))
    def start_world():
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        poll(g, SURVIVAL, lambda v: v == "Survival", 20)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240); F(KEEP_ACTIVE); F(MUTE); F(DURABLE); F(STARTER_NONE); F(CLOSE_DIALOGS)
    def reenter(directory, save=True):
        m = g.mark()
        g.cmd('EXEC ' + ('GameManager.SaveProject(true, true); ' if save else '') + 'GameManager.DisposeProject(); ScreensManager.SwitchScreen("MainMenu");'); g.wait('Entered screen "MainMenu"', 300, m)
        time.sleep(1); m = g.mark(); F(load(directory)); g.wait('Entered screen "Game"', 600, m)
        poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240); F(KEEP_ACTIVE); F(MUTE); F(DURABLE); time.sleep(1.5); step("dialogs after loading", F(CLOSE_DIALOGS))
    try:
        g = m0.game("sp", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g); F(MUTE)
        R["mods"] = step("mods", F('return string.Join("; ", ModsManager.ModList.Where(m => m.modInfo != null).Select(m => m.modInfo.PackageName + " " + m.modInfo.Version));'))
        start_world()
        # 1.6.0: the conspicuous C4 alert is a mod setting, off by default; this run checks it on (sp_bombalert covers both)
        step("C4 alert switch on", F('Game.ModSettingsManager.Set(new[] { "zh667.ScCsgoKnives", "Alerts", "BombAlert" }, true); return "on";'))
        t0 = time.time(); r = F(STRIP)
        while r == "wait" and time.time() - t0 < 120: time.sleep(1); r = F(STRIP)
        x, y, z = [int(v) for v in r.split()]; step("strip", (x, y, z)); ctx["directory"] = F(MAIN + 'return project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName;')
        F(EMPTY); F(teleport(x, y, z)); F(STAND); time.sleep(1.2); F(FPP); h = window_of(g.proc.pid)

        def squads():
            data = {}
            # three-man squad: no escort, no bomber
            got = F(PROJECT + f'var d = project.FindSubsystem<Game.SubsystemTacticalEnemies>(true); int n = d.SpawnManual(new Engine.Point3({x}, {y - 1}, {z - 30}), 3); return n + " " + d.ManualFailure;')
            three = step("three-man squad", F(squad_state())); data["three"] = three
            members = [m.split(",") for m in three.split("|")[1].split(";") if m]
            check("s: a three-man squad has no shield escort and no bomber", len(members) == 3 and all(m[2] == "False" and m[3] == "False" for m in members), three)
            F(EMPTY)
            # five-man squad 34 blocks ahead
            got = step("five-man squad", F(PROJECT + f'var d = project.FindSubsystem<Game.SubsystemTacticalEnemies>(true); int n = d.SpawnManual(new Engine.Point3({x}, {y - 1}, {z - 34}), 5); return n + " " + d.ManualFailure;'))
            s0 = step("composition", F(squad_state())); squad = s0.split("|")[0]; data["five"] = s0
            members = [m.split(",") for m in s0.split("|")[1].split(";") if m]
            escort = next((m for m in members if m[3] == "True"), None); bomber = next((m for m in members if m[2] == "True"), None)
            check("s: five-man squad: the demolition T has the shield, the rifleman is a pistol bomber carrying the C4, sizes unchanged",
                  len(members) == 5 and escort is not None and escort[1] == "Demolition" and bomber is not None and bomber[1] == "Rifle" and bomber[4] == "True"
                  and sum(m[3] == "True" for m in members) == 1 and sum(m[4] == "True" for m in members) == 1, s0)
            # escort positions while the squad comes in
            track = []; t0 = time.time(); pz = z + .5
            while time.time() - t0 < 14:
                st = F(squad_state(squad)); ms = {m.split(",")[0]: m.split(",") for m in st.split("|")[1].split(";") if m}
                if escort[0] in ms and bomber[0] in ms:
                    e, b = ms[escort[0]], ms[bomber[0]]
                    ex, ez, bx, bz = float(e[6]), float(e[7]), float(b[6]), float(b[7])
                    # the escort's place relative to the bomber, along the line to the player (positive: in front)
                    vx, vz = x + .5 - bx, pz - bz; n = math.hypot(vx, vz) or 1
                    ahead = ((ex - bx) * vx + (ez - bz) * vz) / n; side = abs((ex - bx) * vz - (ez - bz) * vx) / n
                    track.append({"t": round(time.time() - t0, 1), "escortAhead": round(ahead, 2), "escortSide": round(side, 2), "bomberToPlayer": round(n, 1)})
                if len(track) == 12: F(camera("Fly")); bpos = (bx, y, bz); F(fly_to(bx + 7, y + 9, bz + 6, bx, y, bz)); time.sleep(1); snap("escort-from-above"); F(camera("Fpp"))
                time.sleep(.5)
            data["track"] = track; step("escort track", track[::3])
            # the squad may stand in its spawn formation for a few seconds before it advances; judge the escort from 1.5 s
            # after the bomber has started moving towards the player
            moving = next((p["t"] for p in track if p["bomberToPlayer"] < track[0]["bomberToPlayer"] - .5), None)
            settled = [p for p in track if moving is not None and p["t"] >= moving + 1.5]
            data["advanceStartedAt"] = moving
            check("s: while the squad advances the escort stays in front of the bomber towards the player (ahead > 0.8, side < 1.6 in 80 % of the samples from 1.5 s after the advance starts)",
                  settled and sum(p["escortAhead"] > .8 and p["escortSide"] < 1.6 for p in settled) >= .8 * len(settled), {"advanceStartedAt": moving, "samples": settled[:10]})
            # shoot at the bomber from the front: the shield in between takes it
            F(MAIN + 'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "ak47"); var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); '
              'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, Game.GunSpec.FreshFull)), 1); inv.ActiveSlotIndex = 0; return "ak";')
            time.sleep(1.3)
            st = F(squad_state(squad)); ms = {m.split(",")[0]: m.split(",") for m in st.split("|")[1].split(";") if m}
            b = ms.get(bomber[0]); e = ms.get(escort[0])
            if b and e:
                before = (float(b[8]), int(e[9])); bursts = []
                for k in range(4):   # four short bursts aimed at the bomber, re-aimed each time as the pair moves
                    st = F(squad_state(squad)); ms = {m.split(",")[0]: m.split(",") for m in st.split("|")[1].split(";") if m}
                    if bomber[0] not in ms: break
                    b = ms[bomber[0]]; e = ms.get(escort[0]); face_point(float(b[6]), y + 1.1, float(b[7])); click(.2); time.sleep(.7)
                    bursts.append({"escortSide": round(abs(float(e[6]) - float(b[6])), 2) if e else None})
                st2 = F(squad_state(squad)); ms2 = {m.split(",")[0]: m.split(",") for m in st2.split("|")[1].split(";") if m}
                after = (float(ms2[bomber[0]][8]) if bomber[0] in ms2 else 0, int(ms2[escort[0]][9]) if escort[0] in ms2 else -1)
                data["coverShots"] = {"before": before, "after": after, "bursts": bursts}; step("shots at the bomber through the escort", data["coverShots"])
                check("s: shots aimed at the bomber from the front are caught by the escort's shield (its wear rises)", after[1] > before[1], data["coverShots"])
            F(CLEAR_INV)
            # the bomber plants; the alert
            planted = None; t0 = time.time(); cues = []
            while time.time() - t0 < 50:
                bs = F(BOMBS)
                if not bs.startswith("0"): planted = bs; break
                time.sleep(.5)
            step("planted", planted)
            if planted:
                time.sleep(.6); alert = step("alert", F(ALERT)); snap("bomb-alert-pc"); data["alert"] = alert
                for i in range(12): cues.append(F(BOMBS)); time.sleep(.5)
                data["beeps"] = cues
                F(camera("Fly")); bx_, bz_ = [float(v) for v in planted.split()[1].split(",")[:2]]
                F(fly_to(bx_ + 10, y + 4, bz_ + 10, bx_, y, bz_)); F(MAIN + f't = 0; return "ok";' if False else 'return "ok";')
                # a wall between the camera and the bomb: the marker shows through it
                F(PROJECT + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dx = -3; dx <= 3; dx++) for (int dy = 0; dy <= 5; dy++) t.ChangeCell((int){bx_} + 5 + dx, {y} + dy, (int){bz_} + 5, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)); return "wall";')
                time.sleep(1); snap("bomb-marker-through-wall"); F(camera("Fpp"))
                size = F('var s = Engine.Window.Size; return s.X + " " + s.Y;')
                try:
                    F('Engine.Window.Size = new Engine.Point2(540, 960); return "portrait";'); time.sleep(1.5); snap("bomb-alert-portrait")
                finally:
                    w, hh = size.split(); F(f'Engine.Window.Size = new Engine.Point2({w}, {hh}); return "restored";'); time.sleep(1)
                st = F(squad_state(squad)); ms = {m.split(",")[0]: m.split(",") for m in st.split("|")[1].split(";") if m}
                check("s: the bomber planted and no longer carries the C4; the escort did not plant", bomber[0] in ms and ms[bomber[0]][4] == "False" and planted.startswith("1"), {"planted": planted, "bomber": ms.get(bomber[0])})
                check("s: the plant alert is on screen with direction, distance and time", "C4" in alert and "格" in alert and "秒" in alert and "敌方安放了 C4" in alert, alert)
                beeps = [float(c.split()[1].split(",")[3]) for c in cues if not c.startswith("0")]
                check("s: the beeps are scheduled and the gap shrinks as the fuse runs", beeps and max(beeps) <= 1.0, cues[:4])
                # killed after planting: no C4; escort killed: none
                c4_0 = int(F(C4_ITEMS)); F(kill(bomber[0])); F(kill(escort[0])); time.sleep(1.5); c4_1 = int(F(C4_ITEMS))
                check("s: a bomber killed after planting and the shield escort drop no C4", c4_1 == c4_0, (c4_0, c4_1))
                F(PROJECT + 'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); foreach (var q in b.Bombs.ToArray()) b.Bombs.Remove(q); return "removed";')
            else: check("s: the bomber planted", False, "no bomb in 50 s")
            # a bomber killed before planting drops one C4; the player picks it up and plants it
            F(EMPTY); F(CLEAR_DROPS); time.sleep(.5)
            F(PROJECT + f'var d = project.FindSubsystem<Game.SubsystemTacticalEnemies>(true); d.SpawnManual(new Engine.Point3({x}, {y - 1}, {z - 50}), 5); return d.ManualFailure;')
            st = F(squad_state()); members = [m.split(",") for m in st.split("|")[1].split(";") if m]
            bomber2 = next((m for m in members if m[2] == "True"), None)
            F(kill(bomber2[0])); time.sleep(1.5); c4s = int(F(C4_ITEMS)); step("C4 items after the bomber's death", c4s)
            check("s: a bomber killed before planting drops exactly one C4", c4s == 1, c4s)
            reenter(ctx["directory"]); c4r = int(F(C4_ITEMS)); check("s: after a save and reload the dead bomber has not dropped another", c4r <= 1, c4r)
            F(EMPTY); pos = F(PROJECT + 'int c4 = Game.ScC4Block.Value; var p = project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.FirstOrDefault(q => q.Value == c4 && !q.ToRemove); return p == null ? "none" : System.FormattableString.Invariant($"{p.Position.X} {p.Position.Y} {p.Position.Z}");')
            if pos != "none":
                px, py, pz2 = [float(v) for v in pos.split()]; F(CLEAR_INV)
                F(MAIN + f'pl.ComponentBody.Position = new Engine.Vector3({px}f, {py}f + .2f, {pz2}f); pl.ComponentBody.Velocity = Engine.Vector3.Zero; return "at the C4";'); time.sleep(3.5)
                have = F(MAIN + 'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, Game.ScC4Block.Value).ToString();')
                slot = F(MAIN + 'var inv = pl.ComponentMiner.Inventory; int s = System.Linq.Enumerable.Range(0, inv.SlotsCount).FirstOrDefault(i => inv.GetSlotValue(i) == Game.ScC4Block.Value && inv.GetSlotCount(i) > 0, -1); if (s >= 0 && s < 7) inv.ActiveSlotIndex = s; return s.ToString();')
                F(MAIN + 'pl.ComponentLocomotion.LookAngles = new Engine.Vector2(0, -1.2f); return "down";'); time.sleep(1)
                F(MAIN + 'project.FindSubsystem<Game.SubsystemScC4>(true).SetPlantButton(pl, false); return "up";'); time.sleep(.5)
                step("plant preconditions", F(MAIN + 'var b = pl.ComponentBody; return "available " + Game.ScGunBindings.Available(pl) + " context " + Game.ScGunBindings.ContextAvailable(pl) + " active " + Game.Engine_Window_IsActive() + " c4 " + Game.ScC4Block.IsValue(pl.ComponentMiner.ActiveBlockValue) + " standing " + b.StandingOnValue.HasValue + " vel " + b.Velocity + " dialogs " + string.Join(",", Game.DialogsManager.Dialogs.Select(d => d.GetType().Name)) + " control " + Game.ScGunBindings.ControlAllowed(pl) + " gate " + Game.ScWeaponActionGate.Blocks(pl) + " cam " + pl.GameWidget.ActiveCamera.GetType().Name;'.replace("Game.Engine_Window_IsActive()", "Engine.Window.IsActive")))
                samples = [F(MAIN + 'var fp = pl.Entity.FindComponent<Game.ComponentFirstPersonModel>(); return "busy " + Game.KnifeAnimationController.IsBusy(fp);')]
                # the plant key (default E; the touch button is fed every frame from the on-screen panel)
                key = F('return Game.ScGunBindings.Get(Game.ScGunFunctions.Plant);'); g.cmd(f"KEYDOWN {key}")
                for k in range(8):
                    time.sleep(.75); samples.append(F(MAIN + 'var c = project.FindSubsystem<Game.SubsystemScC4>(true); return c.IsPlanting(pl) + " " + c.Charges.Count + " " + Game.ScC4Block.IsValue(pl.ComponentMiner.ActiveBlockValue);'))
                g.cmd(f"KEYUP {key}"); step("plant samples", samples)
                F(MAIN + 'project.FindSubsystem<Game.SubsystemScC4>(true).SetPlantButton(pl, false); return "released";')
                charges = F(PROJECT + 'return project.FindSubsystem<Game.SubsystemScC4>(true).Charges.Count.ToString();')
                data["pickUse"] = {"inventory": have, "slot": slot, "charges": charges}; step("pick up and use", data["pickUse"])
                check("s: the player picks the dropped C4 up and plants it", have == "1" and int(charges) >= 1, data["pickUse"])
                F(PROJECT + 'var c = project.FindSubsystem<Game.SubsystemScC4>(true); foreach (var q in c.Charges.ToArray()) c.Disarm(q); return "disarmed";')
            R["data"]["squads"] = data
        section("s squads", squads)

        def blast():
            F(EMPTY); F(CLEAR_DROPS); F(teleport(x, y, z)); F(STAND)
            r = F(MAIN + (
                f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); int bx = {x}, by = {y}, bz = {z} - 20; int air = 0, g = Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index); '
                # a lower step: 2 blocks down, 3..6 blocks east; an open hole 3 deep 4 blocks west; a wall shelter 5 north
                'for (int dx = 3; dx <= 6; dx++) for (int dz = -1; dz <= 1; dz++) { t.ChangeCell(bx + dx, by - 1, bz + dz, air); t.ChangeCell(bx + dx, by - 2, bz + dz, air); t.ChangeCell(bx + dx, by - 3, bz + dz, g); } '
                'for (int dy = 1; dy <= 3; dy++) t.ChangeCell(bx - 4, by - dy, bz, air); t.ChangeCell(bx - 4, by - 4, bz, g); '
                'for (int dx = -2; dx <= 2; dx++) for (int dy = 0; dy <= 3; dy++) t.ChangeCell(bx + dx, by + dy, bz - 4, g); '
                'var pk = project.FindSubsystem<Game.SubsystemPickables>(true); int iron = Game.Terrain.MakeBlockValue(Game.IronIngotBlock.Index), coal = Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index), cu = Game.Terrain.MakeBlockValue(Game.CopperIngotBlock.Index), salt = Game.Terrain.MakeBlockValue(Game.SaltpeterChunkBlock.Index); '
                'pk.AddPickable(iron, 3, new Engine.Vector3(bx + 5.5f, by - 2 + .3f, bz + .5f), Engine.Vector3.Zero, null, null); '
                'pk.AddPickable(coal, 3, new Engine.Vector3(bx - 3.5f, by - 3 + .3f, bz + .5f), Engine.Vector3.Zero, null, null); '
                'pk.AddPickable(cu, 3, new Engine.Vector3(bx + .5f, by + .3f, bz - 6.5f), Engine.Vector3.Zero, null, null); '
                'pk.AddPickable(salt, 3, new Engine.Vector3(bx + 2.5f, by + .3f, bz + 2.5f), Engine.Vector3.Zero, null, null); '
                'var bombs = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); bool ok = bombs.PlantFrom(new Engine.Vector3(bx + .5f, by, bz + .5f), 0f, false); '
                'if (ok) bombs.Bombs[bombs.Bombs.Count - 1].Charge.Remaining = 1.2f; return ok.ToString();'))
            step("blast setup", r); time.sleep(4)
            left = F(PROJECT + 'return string.Join(";", project.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Where(p => !p.ToRemove).Select(p => Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(p.Value)].GetType().Name + "x" + p.Count));')
            step("left after the blast", left); R["data"]["blast3d"] = left
            check("s/x: drops on a lower step and down an open hole are destroyed; behind a wall kept; level drop destroyed",
                  "IronIngotBlock" not in left and "CoalChunkBlock" not in left and "CopperIngotBlockx3" in left and "SaltpeterChunkBlock" not in left, left)
        section("x blast", blast)

        def cases():
            F(EMPTY); F(CLEAR_DROPS); F(teleport(x, y, z)); F(STAND); F(set_level(1)); time.sleep(.6); F(CLEAR_INV)
            F(MAIN + f'var inv = pl.ComponentMiner.Inventory; inv.AddSlotItems(0, {CASE}, 16); inv.AddSlotItems(1, {CASE}, 16); inv.ActiveSlotIndex = 0; return "cases";')
            OPEN = MAIN + (f'var inv0 = pl.ComponentMiner.Inventory; int cv = {CASE}; int cs = System.Linq.Enumerable.Range(0, inv0.SlotsCount).FirstOrDefault(i => inv0.GetSlotCount(i) > 0 && inv0.GetSlotValue(i) == cv, -1); if (cs < 0) return "no case"; inv0.ActiveSlotIndex = cs; '
                'var c = project.FindSubsystem<Game.SubsystemScCaseBlockBehavior>(true); c.OnUse(new Engine.Ray3(pl.ComponentCreatureModel.EyePosition, pl.ComponentBody.Matrix.Forward), pl.ComponentMiner); '
                'var d = Game.DialogsManager.Dialogs.OfType<Game.CaseOpeningDialog>().LastOrDefault(); if (d == null) return "no dialog"; '
                'var roll = (Game.CaseRoll)typeof(Game.CaseOpeningDialog).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).First(f => f.FieldType == typeof(Game.CaseRoll)).GetValue(d); '
                'var blk = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(roll.Reward.Value)]; return roll.Reward.Rarity + " " + roll.Reward.Block + " " + blk.GetType().Assembly.GetName().Name + " lvl" + roll.Reward.MinPlayerLevel;')
            CLAIM = MAIN + ('var d = Game.DialogsManager.Dialogs.OfType<Game.CaseOpeningDialog>().LastOrDefault(); if (d == null) return "none"; var t = typeof(Game.CaseOpeningDialog); '
                't.GetMethod("FinishAnimation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(d, null); t.GetMethod("ClaimAndDismiss", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(d, null); return "claimed";')
            first = F(OPEN); time.sleep(1.2); snap("case-keyless-rolling"); time.sleep(5.5); snap("case-keyless-result"); F(CLAIM); results = [first]
            for i in range(24): results.append(F(OPEN)); time.sleep(.2); F(CLAIM)
            left = F(MAIN + f'return Game.ScInventoryTransaction.Count(pl.ComponentMiner.Inventory, {CASE}).ToString();')
            tiers = {}
            for r_ in results: tiers[r_.split()[0]] = tiers.get(r_.split()[0], 0) + 1
            asm = sorted({r_.split()[2] for r_ in results if len(r_.split()) > 2})
            R["data"]["cases"] = {"results": results, "tiers": tiers, "left": left}; step("25 openings", R["data"]["cases"])
            check("s/k: 25 cases open without any key, one case each", int(left) == 32 - 25 and all(not r_.startswith("no ") for r_ in results), {"left": left, "tiers": tiers})
            check("k: rewards only from the game and the CS packages", all(a in ("Survivalcraft", "ScCsgoKnives", "ScCsgoTactical") for a in asm), asm)
            check("k: no gun above level 1 at level 1", all(" lvl0" in r_ or " lvl1" in r_ for r_ in results), [r_ for r_ in results if " lvl0" not in r_ and " lvl1" not in r_])
            key = F(PROJECT + 'var kb = Game.BlocksManager.GetBlockIndex<Game.ScCaseKeyBlock>(true); return Game.BlocksManager.Blocks[kb].GetCreativeValues().Count() + " recipes " + Game.CraftingRecipesManager.Recipes.Count(r => Game.Terrain.ExtractContents(r.ResultValue) == kb);')
            check("k: the key has no creative entry and no recipe", key == "0 recipes 0", key)
            pool = F(PROJECT + 'var d = Game.CaseRegistry.GetByData(0); return string.Join(",", d.Rewards.Select(r => Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(r.Value)].GetType().Assembly.GetName().Name).Distinct()) + " | " + d.Rewards.Count;')
            check("k: the whole survivor pool holds only game and CS items", all(a in ("Survivalcraft", "ScCsgoKnives", "ScCsgoTactical") for a in pool.split(" | ")[0].split(",")), pool)
            size = F('var s = Engine.Window.Size; return s.X + " " + s.Y;')
            try:
                F('Engine.Window.Size = new Engine.Point2(540, 960); return "portrait";'); time.sleep(1.5); F(OPEN); time.sleep(1.2); snap("case-keyless-portrait-rolling"); time.sleep(5.5); snap("case-keyless-portrait-result"); F(CLAIM)
            finally:
                w, hh = size.split(); F(f'Engine.Window.Size = new Engine.Point2({w}, {hh}); return "restored";'); time.sleep(1)
        section("k cases", cases)

        def levels():
            F(EMPTY); F(CLEAR_DROPS); F(teleport(x, y, z)); F(STAND); F(CLEAR_INV); time.sleep(.3)
            def total(): c = F(CASES).split(); return int(c[0]) + int(c[1])
            rows = []
            def at(level, wait=1.5): F(set_level(level)); time.sleep(wait); n = total(); rows.append((level, n, F(LEDGER))); return n
            base = at(1)                       # new world: recorded 1
            a = at(2)                           # +1
            b = at(5)                           # 3,4,5: +3
            c = at(12)                          # 6..10 +5, 11, 12 +4
            # death: does the game take levels away? (vanilla keeps them; then a level is taken away by hand)
            F(MAIN + 'pl.ComponentHealth.AttackResilience = 1f; pl.ComponentHealth.Health = 0f; return "dead";'); time.sleep(4)
            lvl_after_death = F(MAIN + 'return pl.PlayerData.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);')
            alive = poll(g, MAIN + 'return (pl.ComponentHealth.Health > 0).ToString();', lambda v: v == "True", 60)
            F(teleport(x, y, z)); F(STAND); F(DURABLE)
            d1 = at(9); d2 = at(12)              # lost to 9 then back to 12: nothing
            e1 = at(13)                          # +2
            reenter(ctx["directory"]); f1 = at(13); f2 = at(14)   # after the main menu: nothing for 13, +2 for 14
            # leaving without saving: the 15 reward and the record go together
            g1 = at(15); reenter(ctx["directory"], save=False); g2 = total(); g3 = at(15)
            # a full inventory: the cases drop at the feet
            F(MAIN + 'var inv = pl.ComponentMiner.Inventory; int dirt = Game.Terrain.MakeBlockValue(Game.DirtBlock.Index); for (int s = 0; s < inv.SlotsCount; s++) if (inv.GetSlotCount(s) == 0 && inv.GetSlotCapacity(s, dirt) > 0) inv.AddSlotItems(s, dirt, inv.GetSlotCapacity(s, dirt)); return "full";')
            h0 = F(CASES); F(set_level(22)); time.sleep(1.5); h1 = F(CASES)
            R["data"]["levels"] = {"rows": rows, "levelAfterDeath": lvl_after_death, "aliveAgain": alive, "fullBefore": h0, "fullAfter": h1, "noSave": [g1, g2, g3]}
            step("levels", R["data"]["levels"])
            check("l: a new world records level 1 without a reward; level 2 gives 1", base == 0 and a == 1, rows[:2])
            check("l: 2 -> 5 gives 3; 5 -> 12 gives 5 + 2 x 2 = 9", b - a == 3 and c - b == 9, rows[:4])
            check("l: death in this game keeps the level (reported); 12 -> 9 -> 12 gives nothing; 13 gives 2", d1 == c and d2 == c and e1 - d2 == 2, {"after death": lvl_after_death, "rows": rows})
            check("l: after leaving to the main menu and loading: 13 again nothing, 14 gives 2", f1 == e1 and f2 - f1 == 2, rows)
            # (the last save was the trip to the main menu at level 13: cases f1, record 13)
            check("l: left without saving: the 14 and 15 rewards are undone with their record and granted again once", g1 - f2 == 2 and g2 == f1 and g3 - g2 == 4, {"before": [f1, f2, g1], "reloaded": g2, "again": g3})
            hb, ha = [int(v) for v in h0.split()], [int(v) for v in h1.split()]
            check("l: 15 -> 22 grants 2 x 5 + 3 x 2 = 16; what does not fit in the inventory drops at the feet", (ha[0] + ha[1]) - (hb[0] + hb[1]) == 16 and ha[1] > hb[1], (h0, h1))
        section("l levels", levels)

        def iron():
            r = F(PROJECT + 'return string.Join(" | ", Game.ScComponentCrafting.All.Take(2).Select(e => e.Name + ": " + string.Join(",", e.Ingredients.Select(i => i.Id + "x" + i.Count))));')
            step("component recipes", r); check("i: metal part 6 iron + 2 coal, mechanical parts 5 iron + 4 copper + 2 germanium", "ironingotx6,coalchunkx2" in r and "ironingotx5,copperingotx4,germaniumchunkx2" in r, r)
        section("i iron", iron)
        R["gameErrors"] = g.errors()[:60]
        check("no game errors", not R["gameErrors"], R["gameErrors"][:5])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
        if g is not None: R.setdefault("gameErrors", g.errors()[:40])
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c["check"] for c in R["checks"] if c["ok"] is False]
    out = RESULTS / f"round9-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed {failed}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
