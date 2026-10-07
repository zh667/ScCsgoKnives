"""deathmatch-addon round 2 (2026-10-03, user: "在Windows上你实机测试一下 ... 房主点开始没反应 然后点什么重生点，圈定范围什么的也不
明显"): the deathmatch package played through its own UI on the isolated 1.9.3.1 copy (M0_ENGINE=131), as the host of a
creative world would: F6 opens the menu, every menu entry is pressed with real mouse clicks at the button's place on
screen (the button is found by its text; a button outside the visible part of a scrolled panel is recorded as such and
scrolled to, as a player would), the buy wheel is opened with B and used with clicks, Tab shows the board. After each
step: the package's state, the menu's message, and a frame (looked at afterwards; frames are evidence, not checks).

Usage: sp_deathmatch.py <label> <core package | output-lite | output-full> <deathmatch package | output-dm>
The player's own Mods folder and worlds are never used (isolated copy, new world).
"""
import json, shutil, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE, build_arena, teleport
from sp_smoke_view import snap

DM_OUT = m0.DM
STAGES = m0.ROOT / ".tmp/completion-140-20260929"
STAGE_LABELS = {"lite": "轻量", "full": "全量", "agents": "探员", "dm": "死亡竞赛"}   # keys of the stage's packages.json
def package(spec):
    """A package by spec: output-lite / output-full / output-dm (the delivered packages), stage-<tag>-<lite|full|agents|dm>
    (a release candidate in its stage, before delivery), or a path."""
    if spec in ("output-lite", "output-full", "output-dm"): return {"output-lite": m0.LITE, "output-full": m0.FULL, "output-dm": DM_OUT}[spec]
    if spec == "output-full-150": return m0.OUT / m0.pkgname("全量", "1.5.0")   # the 1.5.0 Full (2026-10-06); the job arguments must stay ASCII
    if spec.startswith("stage-"):
        tag, kind = spec[len("stage-"):].rsplit("-", 1)
        pk = json.loads((STAGES / tag / "packages.json").read_text("utf8")); p = STAGES / tag / "candidate" / pk[STAGE_LABELS[kind]]["file"]
        if not p.exists(): raise FileNotFoundError(p)
        return p
    return Path(spec)
CREATIVE = 'if (ScreensManager.CurrentScreen is not NewWorldScreen s || s.m_worldSettings == null) return "not-new-world"; s.m_worldSettings.GameMode = GameMode.Creative; return s.m_worldSettings.GameMode.ToString();'
DMV = MAIN + 'var dm = project.FindSubsystem<Game.SubsystemScDeathmatch>(false); if (dm == null) return "no deathmatch subsystem"; '
STATE = DMV + (
    'var m = dm.Match; var me = dm.Enabled ? dm.StateOf(pl) : null; var inv = pl.ComponentMiner.Inventory; var modal = pl.ComponentGui.ModalPanelWidget; '
    'string message = null; if (modal != null) { var f = modal.GetType().GetField("m_message", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); if (f?.GetValue(modal) is Game.LabelWidget l) message = l.Text; } '
    'return System.Text.Json.JsonSerializer.Serialize(new { enabled = dm.Enabled, frozen = dm.Frozen, phase = m == null ? "-" : m.Phase.ToString(), view = dm.View.Phase.ToString(), match = m == null ? -1 : m.MatchId, '
    '  region = dm.Arena.HasRegion, lobby = dm.Arena.HasLobby, spawns = dm.Arena.Spawns.Count, '
    '  me = me == null ? "-" : me.Phase + " entered=" + me.Entered + " life=" + me.LifeId + " hp=" + me.Health + " ap=" + me.Armour + " desired=" + me.Desired.Encode(), '
    '  slots = string.Join(",", Enumerable.Range(0, 10).Select(i => inv.GetSlotCount(i) > 0 ? inv.GetSlotValue(i).ToString() : "-")), active = inv.ActiveSlotIndex, '
    '  modal = modal?.GetType().Name, screen = Game.ScreensManager.CurrentScreen?.GetType().Name, camera = pl.GameWidget.ActiveCamera.GetType().Name, message, '
    '  pos = System.FormattableString.Invariant($"{pl.ComponentBody.Position.X:0.0} {pl.ComponentBody.Position.Y:0.0} {pl.ComponentBody.Position.Z:0.0}"), health = pl.ComponentHealth.Health, '
    '  fly = pl.ComponentLocomotion.IsCreativeFlyEnabled, issues = dm.Enabled ? string.Join(" / ", dm.ArenaIssues().Select(i => i.Code + "#" + i.SpawnId)) : "" });')
# The visible buttons of the modal panel; the one whose text (or its labels) starts with $s0: its centre on screen, and
# whether that centre lies inside every clipping ancestor (a scrolled panel shows only part of its content).
FIND = MAIN + (
    'var root = pl.ComponentGui.ModalPanelWidget as Game.ContainerWidget; if (root == null) return "no modal"; '
    'bool Shown(Game.Widget w) { for (var x = w; x != null; x = x.ParentWidget) if (!x.IsVisible) return false; return true; } '
    'string Words(Game.Widget w) { var t = w is Game.ButtonWidget bw ? bw.Text : null; if (!string.IsNullOrEmpty(t)) return t; return w is Game.ContainerWidget c ? string.Join(" ", c.AllChildren.OfType<Game.LabelWidget>().Where(l => !string.IsNullOrEmpty(l.Text)).Select(l => l.Text)) : ""; } '
    'var buttons = root.AllChildren.OfType<Game.ButtonWidget>().Where(Shown).ToList(); var b = buttons.FirstOrDefault(x => Words(x).StartsWith($s0)); '
    'if (b == null) return "none: " + string.Join(" | ", buttons.Select(Words)); '
    'var bb = b.GlobalBounds; var c = (bb.Min + bb.Max) * 0.5f; bool inside = true; '
    'for (var x = b.ParentWidget; x != null; x = x.ParentWidget) if (x is Game.ScrollPanelWidget || x.ClampToBounds) { var g = x.GlobalBounds; if (c.X < g.Min.X || c.X > g.Max.X || c.Y < g.Min.Y || c.Y > g.Max.Y) inside = false; } '
    'return System.FormattableString.Invariant($"{c.X:0} {c.Y:0} {inside} {Engine.Window.Size.X}x{Engine.Window.Size.Y}");')
# Scrolls the modal panel's scroll area so the button lies in view (what a player does by dragging).
SCROLL_TO = MAIN + (
    'var root = pl.ComponentGui.ModalPanelWidget as Game.ContainerWidget; var sp = root?.AllChildren.OfType<Game.ScrollPanelWidget>().FirstOrDefault(); if (sp == null) return "no scroll panel"; '
    'string Words(Game.Widget w) => w is Game.ButtonWidget bw ? bw.Text ?? "" : ""; var b = root.AllChildren.OfType<Game.ButtonWidget>().FirstOrDefault(x => x.IsVisible && Words(x).StartsWith($s0)); if (b == null) return "no button"; '
    'float view = sp.GlobalBounds.Max.Y - sp.GlobalBounds.Min.Y; float y = b.GlobalBounds.Min.Y - sp.GlobalBounds.Min.Y + sp.ScrollPosition; sp.ScrollPosition = System.Math.Max(0f, y - view * 0.4f); return "scrolled " + sp.ScrollPosition;')
WHEEL = MAIN + (
    'var root = pl.ComponentGui.ModalPanelWidget as Game.ContainerWidget; var w = root?.AllChildren.OfType<Game.DmWheelWidget>().FirstOrDefault(); if (w == null) return "no wheel"; '
    'var p = w.WidgetToScreen(new Engine.Vector2(Game.DmWheelWidget.Radius + 10f) + Game.DmWheelWidget.SectorCentre($i0, w.Sectors.Count)); '
    'return System.FormattableString.Invariant($"{p.X:0} {p.Y:0} ") + string.Join("|", w.Sectors.Select(s => s.Label));')
WHEEL_CENTRE = MAIN + (
    'var root = pl.ComponentGui.ModalPanelWidget as Game.ContainerWidget; var w = root?.AllChildren.OfType<Game.DmWheelWidget>().FirstOrDefault(); if (w == null) return "no wheel"; '
    'var p = w.WidgetToScreen(new Engine.Vector2(Game.DmWheelWidget.Radius + 10f)); return System.FormattableString.Invariant($"{p.X:0} {p.Y:0}");')
CLOSE_MODAL = MAIN + 'pl.ComponentGui.ModalPanelWidget = null; return "closed";'
def look_down(x, y, z): return MAIN + (
    f'var w = pl.GameWidget; if (w.FindCamera<Game.FlyCamera>(false) == null) w.AddCamera(new Game.FlyCamera(w), q => true); var f = w.FindCamera<Game.FlyCamera>(); w.ActiveCamera = f; '
    f'f.m_position = new Engine.Vector3({x}f, {y}f, {z}f); f.m_direction = Engine.Vector3.Normalize(new Engine.Vector3(0.0001f, -1f, 0.35f)); f.m_velocity = Engine.Vector3.Zero; '
    'f.SetupPerspectiveCamera(f.m_position, f.m_direction, Engine.Vector3.UnitZ); return "fly";')
FPP = MAIN + 'var w = pl.GameWidget; w.ActiveCamera = w.FindCamera<Game.FppCamera>(); return "fpp";'
STAND_HERE = MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; return "ok";'
# The engine's own screenshot path (ScreenCaptureManager) leaves the GUI out unless the player's setting says otherwise: these
# runs are about the GUI, so it is shown (and the logo left out) in the isolated copy's settings.
GUI_IN_SHOTS = 'Game.SettingsManager.ShowGuiInScreenshots = true; Game.SettingsManager.ShowLogoInScreenshots = false; return "gui in screenshots";'
HUD = MAIN + ('var gw = pl.GuiWidget as Game.ContainerWidget; var root = gw?.AllChildren.OfType<Game.DmElement>().ToList(); if (root == null) return "no gui"; '
    'return string.Join(", ", root.Select(e => e.Id + (e.IsVisible ? "+" : "-")));')

# The equipment rows' sprites (round-2 SP r1: the rows showed only their key numbers).
EQUIP = MAIN + ('var inv = pl.ComponentMiner.Inventory; return string.Join("; ", Enumerable.Range(0, 10).Where(i => inv.GetSlotCount(i) > 0).Select(i => { int v = inv.GetSlotValue(i); '
    'var b = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(v)]; return i + ":" + b.GetType().Name + " data=" + Game.Terrain.ExtractData(v) + " variant=" + (b is Game.ScGunBlock ? Game.ScGunBlock.GetVariant(v) : -9) '
    '+ " sprite=" + (Game.DmPx.ItemSprite(v) != null); })) + " | ak47 sprite=" + (Game.DmPx.Sprite("ak47") != null);')
# Round 3 (the user: health on the game's own bar, armour on the CS core's armour HUD): what those two show right now.
SHOWN = MAIN + ('var bar = pl.ComponentGui.HealthBarWidget; var hud = pl.GuiWidget.AllChildren.FirstOrDefault(w => w.Name == "ScArmorHud") as Game.ContainerWidget; '
    'var badge = hud?.AllChildren.OfType<Game.ScArmorBadge>().FirstOrDefault()?.Texture; var values = hud == null ? "" : string.Join("/", hud.AllChildren.OfType<Game.LabelWidget>().Where(l => l.IsVisible && l.Text.Length > 0).Select(l => l.Text)); '
    'return "bar " + (bar.IsVisibleGlobal ? "shown" : "hidden") + " " + bar.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " | armour " + (hud != null && hud.IsVisibleGlobal ? badge + " " + values : "hidden");')
# The layout editor's pixel sliders and toggle (round 2: no vanilla checkbox/slider left): where they are on screen.
LAYOUT = ('var screen = Game.ScreensManager.CurrentScreen as Game.ContainerWidget; if (screen == null) return "no screen"; '
    'var sliders = screen.AllChildren.OfType<Game.DmPixelSlider>().ToList(); '
    'return string.Join(" | ", sliders.Select(s => { var b = s.GlobalBounds; return System.FormattableString.Invariant($"{b.Min.X:0} {b.Max.X:0} {(b.Min.Y + b.Max.Y) / 2:0} {s.Value:0.00}"); }));')

# Round 5 (2026-10-05: CS2's spray patterns, hitboxes, running speeds, in deathmatch worlds only): what the running game has.
R5_HOOKS = MAIN + 'var caps = Game.ScModes.HitCapsules(pl.ComponentBody); return "recoil " + (Game.ScModes.Recoil(pl) != null) + " capsules " + (caps?.Length ?? -1);'
# Front rays at the body's own centre line, at CS2 heights (units over the feet), scaled the way DmHitboxes places the capsules
# on this body (expected from the capsule table: 67.5 head, 60.7 neck, 55 chest, 42 stomach, 30 nothing - the legs stand apart).
R5_PARTS = MAIN + ('var caps = Game.ScModes.HitCapsules(pl.ComponentBody); if (caps == null) return "none"; var b = pl.ComponentBody; '
    'var f = b.Rotation.GetForwardVector(); var feet = b.Position; float H = b.BoxSize.Y; float eye = pl.ComponentCreatureModel.EyePosition.Y - feet.Y; '
    'float s = 0.875f * H / Game.DmHitboxes.EyeStanding, k = eye / (0.875f * H); '
    'string Probe(float u) { var o = feet + f * 3f + Engine.Vector3.UnitY * (u * s * k); var r = Game.ScHitCapsule.Resolve(caps, o, -f, 6f); return System.FormattableString.Invariant($"{u}:{r.Part}"); } '
    'return string.Join(" ", new float[] { 67.5f, 60.7f, 55f, 42f, 30f }.Select(Probe)) + System.FormattableString.Invariant($" | eye {eye:0.000} H {H:0.000}");')
R5_SLOT = MAIN + 'var inv = pl.ComponentMiner.Inventory; if (inv.GetSlotCount($i0) <= 0) return "empty"; inv.ActiveSlotIndex = $i0; return "slot " + $i0;'
R5_SPEED = MAIN + ('var v = pl.ComponentMiner.ActiveBlockValue; var blk = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(v)]; '
    'string what = blk is Game.ScGunBlock ? Game.GunSpec.All[Game.ScGunBlock.GetVariant(v)].Name : blk.GetType().Name; '
    'return System.FormattableString.Invariant($"{what} walk {pl.ComponentLocomotion.WalkSpeed:0.000}");')
R5_RAY = MAIN + ('var eye = pl.ComponentCreatureModel.EyePosition; var d = pl.ComponentCreatureModel.EyeRotation.GetForwardVector(); '
    'Game.SubsystemScGunBlockBehavior.DebugShots = true; '
    'return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");')
R5_RECOIL = DMV + ('var st = dm.Recoil.StateOf(pl); var (bullet, view) = st.Angles(); var look = pl.ComponentLocomotion.LookAngles; '
    'return System.FormattableString.Invariant($"gun {st.Gun} index {st.Index:0.00} bullet {bullet.X:0.00},{bullet.Y:0.00} view {view.X:0.00},{view.Y:0.00} look {look.X * 57.29578f:0.00},{look.Y * 57.29578f:0.00} | ") + Game.SubsystemScGunBlockBehavior.LastShotDebug;')


def main(label, core, dm):
    pkgs = [package(core), package(dm)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"dm-sp-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"dm-sp-{label}", "packages": {p.name: sha(p) for p in pkgs}, "steps": [], "frames": []}
    T0 = time.time(); g = None

    def step(name, value=None):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def state(name):
        try: return step(name, json.loads(g.func(STATE)))
        except Exception as e: return step(name, f"state failed: {e}")
    def frame(name):
        try:
            shot = snap(g, f"dm-sp-{label}-{len(R['frames']):02d}-{name}", ((0, 0, 0), (1, 1, 1)))[0]; time.sleep(.6)
            for f in Path(shot).parent.glob(Path(shot).name + "*"):
                target = case_dir / "frames" / f.name; shutil.copy2(f, target); R["frames"].append(str(target)); step("frame " + name, str(target))
        except Exception as e: step("frame failed " + name, str(e))
    def key(k, hold=.12):
        g.cmd(f"KEYDOWN {k}"); time.sleep(hold); g.cmd(f"KEYUP {k}"); time.sleep(.5)
    def click_at(x, y, wait=.7):
        g.cmd(f"MOUSEMOVE {x} {y}"); time.sleep(.15); g.cmd(f"MOUSEDOWN Left {x} {y}"); time.sleep(.12); g.cmd(f"MOUSEUP Left {x} {y}"); time.sleep(wait)
    def click(text, wait=.8):
        where = g.func(m0.Call(FIND, text))
        for _ in range(4):   # a panel that was just opened lays its buttons out in its first frames
            if not where.startswith(("none: ", "no modal")) or where.startswith("none: ") and len(where) > 7: break
            time.sleep(.5); where = g.func(m0.Call(FIND, text))
        if where.startswith(("none", "no modal")): return step(f"button '{text}' not found", where)
        x, y, inside, size = where.split()
        if inside != "True":
            step(f"button '{text}' is outside the visible part of the panel", f"{x},{y} window {size}"); step("scroll", g.func(m0.Call(SCROLL_TO, text))); time.sleep(.4)
            x, y, inside, size = g.func(m0.Call(FIND, text)).split()
        click_at(x, y, wait); return step(f"clicked '{text}'", f"{x},{y} inside={inside} window {size}")
    def wheel(i, wait=.7):
        r = g.func(m0.Call(WHEEL, i))
        if r.startswith("no wheel"): return step("wheel missing", r)
        x, y = r.split()[:2]; click_at(x, y, wait); return step(f"wheel sector {i}", r)
    def go(x, y, z):
        for _ in range(6):
            r = g.func(teleport(x, y, z)); time.sleep(.5)
            if r and not r.startswith("no"): break
        return r
    def menu():
        if g.func(MAIN + 'return pl.ComponentGui.ModalPanelWidget?.GetType().Name ?? "none";') != "DmMenuPanel": key("F6"); time.sleep(1.2)
        return g.func(MAIN + 'return pl.ComponentGui.ModalPanelWidget?.GetType().Name ?? "none";')

    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        step("creative world", poll(g, CREATIVE, lambda v: v == "Creative", 20))
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240))
        g.func(KEEP_ACTIVE); step("settings", g.func(GUI_IN_SHOTS)); time.sleep(2)
        x, y, z = [int(v) for v in step("arena", build_arena(g)).split()]
        go(x, y, z); g.func(STAND_HERE); time.sleep(1.5)
        # the world's intro camera hides the player's whole GUI (its controls container) until it hands over: wait for it
        step("camera", poll(g, MAIN + 'return pl.GameWidget.ActiveCamera.GetType().Name;', lambda v: v == "FppCamera", 30)); g.func(FPP); time.sleep(.5)
        state("in the world"); step("hud", g.func(HUD))

        # ---- the host's menu, through the key and through clicks (round 2: three pages, the map tools on "地图")
        step("F6", menu()); state("menu opened"); frame("menu-new-world")
        click("把本世界启用为竞技世界"); state("after enable"); frame("after-enable"); step("hud", g.func(HUD))
        g.func(CLOSE_MODAL); go(x - 4, y, z - 4); g.func(STAND_HERE); time.sleep(.8); step("menu", menu()); click("地图"); click("角点 1"); state("corner 1")
        g.func(CLOSE_MODAL); go(x + 4, y, z + 8); g.func(STAND_HERE); time.sleep(.8); frame("corner-1-pending-box"); step("menu", menu()); click("地图"); click("角点 2"); state("corner 2")
        for i, (sx, sz) in enumerate([(-3, -3), (3, -3), (-3, 7), (3, 7)]):
            g.func(CLOSE_MODAL); go(x + sx, y, z + sz); g.func(STAND_HERE); time.sleep(.6); step("menu", menu()); click("地图"); click("添加复活点"); state(f"spawn {i + 1}")
        g.func(CLOSE_MODAL); go(x, y, z + 12); g.func(STAND_HERE); time.sleep(.6); step("menu", menu()); click("地图"); click("准备/观战点"); state("lobby")
        # round 3: a map action that succeeds closes the menu (the player walks on): the check and the start are a new opening
        step("menu", menu()); click("地图"); click("检查地图"); state("check map"); frame("check-map")
        click("比赛"); click("开始比赛", 1.0); state("start before anyone entered (refused)"); frame("start-refused")
        g.func(CLOSE_MODAL); step("camera", g.func(look_down(x + .5, y + 18, z - 4))); time.sleep(1.2); frame("editing-preview-from-above")
        g.func(FPP); go(x, y, z + 12); g.func(STAND_HERE); time.sleep(1); frame("editing-preview-first-person"); step("hud", g.func(HUD))

        # ---- open the lobby, buy, enter, start
        step("menu", menu()); click("比赛"); click("开放大厅"); state("after open lobby"); frame("lobby-menu")
        g.func(CLOSE_MODAL); time.sleep(.6)
        key("B"); time.sleep(.6); state("wheel opened"); frame("wheel-top")
        wheel(2); frame("wheel-rifles"); wheel(0); frame("wheel-ak-drawer")
        click("原厂外观"); state("ak chosen")
        cx, cy = g.func(WHEEL_CENTRE).split(); click_at(cx, cy); step("wheel centre (back)")
        wheel(0); frame("wheel-pistols"); wheel(0); click("原厂外观"); state("pistol chosen"); frame("wheel-loadout-ready")
        cx, cy = g.func(WHEEL_CENTRE).split(); click_at(cx, cy); wheel(4); frame("wheel-knives"); click_at(cx, cy)   # every knife with its CS2 icon (2026-10-06)
        click("确认"); state("confirmed"); click("准备", 1.5); state("entered")
        step("menu", menu()); click("比赛"); click("开始比赛", 1.2); state("after start"); frame("after-start-menu")
        g.func(CLOSE_MODAL); time.sleep(1); frame("countdown-hud"); step("hud", g.func(HUD))
        time.sleep(6.5); state("after countdown"); step("equip sprites", g.func(EQUIP)); step("health and armour shown", g.func(SHOWN)); frame("alive-hud"); step("hud", g.func(HUD))
        # the game's bar follows the deathmatch health, not the engine's (which stays full): a life at 37 shows 3.5 hearts
        step("health set to 37", g.func(DMV + 'var p = dm.StateOf(pl); p.Health = 37; return "set";')); time.sleep(1.2)
        step("health 37 shown", g.func(SHOWN)); frame("health-37"); g.func(DMV + 'dm.StateOf(pl).Health = 100; return "restored";'); time.sleep(.5)
        time.sleep(3.5); state("protection over"); frame("alive-hud-after-protection")

        # ---- round 5: the mode's recoil and hitboxes are in force on this player; each weapon's running speed; a spray
        step("r5 hooks", g.func(R5_HOOKS)); step("r5 parts", g.func(R5_PARTS))
        speeds = []
        for slot in range(10):
            if g.func(m0.Call(R5_SLOT, slot)) == "empty": continue
            time.sleep(.5); speeds.append(g.func(R5_SPEED))
        step("r5 speeds", speeds)
        g.func(m0.Call(R5_SLOT, 0)); time.sleep(1.2); g.func(R5_RAY); step("r5 before spray", g.func(R5_RECOIL))
        # a held trigger as a player holds it: the left button down in the middle of the game window (an automatic gun keeps
        # firing while it is down; the automation's DIG_RAY presses once, and SetFireButton is rewritten by the knife
        # subsystem every update)
        cx, cy = [int(v) // 2 for v in g.func(MAIN + 'return Engine.Window.Size.X + " " + Engine.Window.Size.Y;').split()]
        g.cmd(f"MOUSEMOVE {cx} {cy}"); time.sleep(.2); g.cmd(f"MOUSEDOWN Left {cx} {cy}"); time.sleep(.9)
        step("r5 during the spray (0.9 s held)", g.func(R5_RECOIL)); time.sleep(.4); g.cmd(f"MOUSEUP Left {cx} {cy}")
        step("r5 right after the spray", g.func(R5_RECOIL)); frame("r5-spray")
        time.sleep(1.6); step("r5 1.6 s later", g.func(R5_RECOIL))

        # ---- equipment held: drop, board, layout
        before = g.func(STATE); g.cmd("DROP_ONCE"); time.sleep(1); after = g.func(STATE)
        step("drop attempt", {"before": json.loads(before)["slots"], "after": json.loads(after)["slots"]})
        g.cmd("KEYDOWN Tab"); time.sleep(.8); frame("scoreboard-held"); g.cmd("KEYUP Tab"); time.sleep(.4)
        step("menu", menu()); click("比赛"); click("结束本局", 1.2); state("after stop"); g.func(CLOSE_MODAL); time.sleep(1); frame("results")
        step("r5 after the match: own speed back", g.func(R5_SPEED)); step("r5 hooks after the match", g.func(R5_HOOKS))
        step("menu", menu()); click("设置"); click("编辑 HUD 布局", 1.5); state("layout editor"); frame("layout-editor")
        # press the size slider near the middle of its track with a real press, and read the value back
        before = step("layout sliders", g.func(LAYOUT))
        try:
            x0, x1, yc = [float(v) for v in before.split(" | ")[0].split()[:3]]; tx = x0 + (x1 - x0) * .45   # on the track (the readout takes the right quarter)
            g.cmd(f"MOUSEMOVE {tx:.0f} {yc:.0f}"); time.sleep(.15); g.cmd(f"MOUSEDOWN Left {tx:.0f} {yc:.0f}"); time.sleep(.3); g.cmd(f"MOUSEUP Left {tx:.0f} {yc:.0f}"); time.sleep(.6)
            step("layout sliders after a press", g.func(LAYOUT)); frame("layout-editor-slider")
        except Exception as e: step("slider press failed", str(e))
        R["gameErrors"] = g.errors()[:40]
        R["dmLog"] = [l for l in g.lines if "CS_DM" in l or "Deathmatch" in l or "Dm" in l][:80]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"dm-sp-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"steps {len(R['steps'])}; frames {len(R['frames'])}; errors {len(R.get('gameErrors', []))}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2], sys.argv[3]))
