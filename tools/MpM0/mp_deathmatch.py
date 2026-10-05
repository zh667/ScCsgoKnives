"""deathmatch-addon round 2 (2026-10-03, user: "在Windows上你实机测试一下 ... 房主点开始没反应"): the deathmatch package in
multiplayer on the 1.9.3.2_MP copies - one host and one client, each its own process - played through the package's own
UI as two people would: the host builds the arena with F6 and real clicks, the client joins over the network, both buy
with the B wheel and press "入场", the host presses "开始比赛", the client shoots the host with a real trigger (the game's
own dig input held along the client's line of sight), the host dies and respawns, Tab shows the board, the host ends
the match. After each step: each side's view of the match (the client reads only what the server sent it) and frames
from both windows (looked at afterwards; frames are evidence, not checks).

Usage: mp_deathmatch.py <label> <core package | output-lite | output-full> <deathmatch package | output-dm>
Isolated MP role folders only; the player's own Mods folder and worlds are never used.
"""
import json, shutil, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import m0
from m0 import Game, MODS, TA_BUILT, RESULTS, RUNS, sha, to_menu, join, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE, FORCE_ACTIVE, HANDSHAKE, BRIDGE, build_arena, teleport, EYE
from sp_deathmatch import DM_OUT, package, CREATIVE, STATE, FIND, SCROLL_TO, WHEEL, WHEEL_CENTRE, CLOSE_MODAL, FPP, STAND_HERE, GUI_IN_SHOTS, HUD, EQUIP
from sp_smoke_view import snap

DMV = MAIN + 'var dm = project.FindSubsystem<Game.SubsystemScDeathmatch>(false); if (dm == null) return "no deathmatch subsystem"; '
# What this process was told: the replicated view (on a client nothing else exists), its own self, the rows and the feed.
VIEW = DMV + (
    'var v = dm.View; var s = v.Of(pl); var inv = pl.ComponentMiner.Inventory; '
    'return System.Text.Json.JsonSerializer.Serialize(new { index = pl.PlayerData.PlayerIndex, enabled = v.Enabled, phase = v.Phase.ToString(), match = v.MatchId, '
    '  me = s.Phase + " entered=" + s.Entered + " life=" + s.LifeId + " hp=" + s.Health + " ap=" + s.Armour, '
    '  rows = string.Join(" | ", v.Rows.Select(r => r.PlayerIndex + ":" + r.Name + " " + r.Phase + " K" + r.Kills + " D" + r.Deaths + " A" + r.Assists + (r.Playing ? "" : " spectating") + (r.Connected ? "" : " offline"))), '
    '  feed = string.Join(" | ", v.Feed.Select(f => f.Kill.KillerName + " > " + f.Kill.VictimName + " " + f.Kill.Weapon)), result = v.Result == null ? "-" : "result", '
    '  slots = string.Join(",", Enumerable.Range(0, 10).Select(i => inv.GetSlotCount(i) > 0 ? inv.GetSlotValue(i).ToString() : "-")), active = inv.ActiveSlotIndex, '
    '  modal = pl.ComponentGui.ModalPanelWidget?.GetType().Name, camera = pl.GameWidget.ActiveCamera.GetType().Name, health = pl.ComponentHealth.Health, '
    '  pos = System.FormattableString.Invariant($"{pl.ComponentBody.Position.X:0.0} {pl.ComponentBody.Position.Y:0.0} {pl.ComponentBody.Position.Z:0.0}") });')
def aim_at_player(index): return MAIN + (
    f'var t = players.ComponentPlayers.FirstOrDefault(c => c.PlayerData.PlayerIndex == {index})?.ComponentBody; if (t == null) return "no target"; '
    'var eye = pl.ComponentCreatureModel.EyePosition; var bb = t.BoundingBox; var c = (bb.Min + bb.Max) * 0.5f + new Engine.Vector3(0, 0.15f, 0); var d = Engine.Vector3.Normalize(c - eye); '
    'return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");')
POS = MAIN + 'var p = pl.ComponentBody.Position; return System.FormattableString.Invariant($"{p.X} {p.Y} {p.Z}");'
ACTIVE_SLOT0 = MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 0; return "slot 0";'
# round 6: a wall of planks from x0 to x1 at z, three blocks high from y (server side, before a match: the platform sends it on)
def planks_wall(x0, x1, y, z): return PROJECT + (
    f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); int planks = Game.Terrain.MakeBlockValue(Game.PlanksBlock.Index); '
    f'for (int wx = {x0}; wx <= {x1}; wx++) for (int dy = 0; dy <= 2; dy++) t.ChangeCell(wx, {y} + dy, {z}, planks); '
    f'return Game.Terrain.ExtractContents(t.Terrain.GetCellValue({x0}, {y} + 1, {z})) == Game.PlanksBlock.Index ? "planks at {x0}..{x1},{y}..{y}+2,{z}" : "not placed";')
def wall_between(ax, ay, az, bx, by, bz): return PROJECT + (
    f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); float mx = ({ax}f + {bx}f) / 2, mz = ({az}f + {bz}f) / 2; int y0 = (int)System.MathF.Floor(System.MathF.Min({ay}f, {by}f)); '
    f'bool alongX = System.MathF.Abs({bx}f - {ax}f) >= System.MathF.Abs({bz}f - {az}f); int cx = (int)System.MathF.Floor(mx), cz = (int)System.MathF.Floor(mz); '
    'int planks = Game.Terrain.MakeBlockValue(Game.PlanksBlock.Index); '
    'for (int i = -1; i <= 1; i++) for (int dy = 0; dy <= 2; dy++) t.ChangeCell(alongX ? cx : cx + i, y0 + dy, alongX ? cz + i : cz, planks); '
    'return cx + "," + y0 + "," + cz + (alongX ? " across x" : " across z");')
# the first thing the server's own trace meets along an aim "x y z dx dy dz": "first planks at <d>" / "first <block> at <d>" / "nothing"
def first_on(aim): return PROJECT + (
    f'var a = "{aim}".Split(\' \').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); var s = new Engine.Vector3(a[0], a[1], a[2]); var d = new Engine.Vector3(a[3], a[4], a[5]); '
    'var r = Game.ScGunRange.TraceBullet(project.FindSubsystem<Game.SubsystemTerrain>(true), s, d, 64f); if (r == null) return "nothing"; '
    'var b = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(r.Value.Value)]; return "first " + (b is Game.PlanksBlock ? "planks" : b.GetType().Name) + " at " + r.Value.Distance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);')
HOST_HP = MAIN + 'var dm = project.FindSubsystem<Game.SubsystemScDeathmatch>(false); var p = dm?.StateOf(pl); var k = dm?.View.Feed.LastOrDefault().Kill; return p == null ? "-" : "hp " + p.Health + " ap " + p.Armour + " life " + p.LifeId + " lastkill-penetration " + (k?.Penetration.ToString() ?? "-");'


class Side:
    """One process driven as a player: steps, frames and real input on its own window."""
    def __init__(self, g, label, case_dir, R, T0):
        self.g, self.label, self.case_dir, self.R, self.T0 = g, label, case_dir, R, T0
    def step(self, name, value=None):
        t = round(time.time() - self.T0, 1); self.R["steps"].append({"t": t, "side": self.g.name, "step": name, "value": value}); print(f"[{t}] {self.g.name} {name}: {value}", flush=True); return value
    def view(self, name):
        try: return self.step(name, json.loads(self.g.func(VIEW)))
        except Exception as e: return self.step(name, f"view failed: {e}")
    def frame(self, name):
        try:
            shot = snap(self.g, f"dm-mp-{self.label}-{len(self.R['frames']):02d}-{self.g.name}-{name}", ((0, 0, 0), (1, 1, 1)))[0]; time.sleep(.6)
            for f in Path(shot).parent.glob(Path(shot).name + "*"):
                target = self.case_dir / "frames" / f.name; shutil.copy2(f, target); self.R["frames"].append(str(target)); self.step("frame " + name, str(target))
        except Exception as e: self.step("frame failed " + name, str(e))
    def key(self, k, hold=.12):
        self.g.func(FORCE_ACTIVE); self.g.cmd(f"KEYDOWN {k}"); time.sleep(hold); self.g.cmd(f"KEYUP {k}"); time.sleep(.5)
    def click_at(self, x, y, wait=.7):
        self.g.func(FORCE_ACTIVE); g = self.g
        g.cmd(f"MOUSEMOVE {x} {y}"); time.sleep(.15); g.cmd(f"MOUSEDOWN Left {x} {y}"); time.sleep(.12); g.cmd(f"MOUSEUP Left {x} {y}"); time.sleep(wait)
    def click(self, text, wait=.8):
        g = self.g; where = g.func(m0.Call(FIND, text))
        for _ in range(4):
            if not where.startswith(("none: ", "no modal")) or where.startswith("none: ") and len(where) > 7: break
            time.sleep(.5); where = g.func(m0.Call(FIND, text))
        if where.startswith(("none", "no modal")): return self.step(f"button '{text}' not found", where)
        x, y, inside, size = where.split()
        if inside != "True":
            self.step(f"button '{text}' is outside the visible part of the panel", f"{x},{y} window {size}"); self.step("scroll", g.func(m0.Call(SCROLL_TO, text))); time.sleep(.4)
            x, y, inside, size = g.func(m0.Call(FIND, text)).split()
        self.click_at(x, y, wait); return self.step(f"clicked '{text}'", f"{x},{y} inside={inside} window {size}")
    def wheel(self, i, wait=.7):
        r = self.g.func(m0.Call(WHEEL, i))
        if r.startswith("no wheel"): return self.step("wheel missing", r)
        x, y = r.split()[:2]; self.click_at(x, y, wait); return self.step(f"wheel sector {i}", r)
    def go(self, x, y, z):
        r = None
        for _ in range(6):
            r = self.g.func(teleport(x, y, z)); time.sleep(.5)
            if r and not r.startswith("no"): break
        self.g.func(STAND_HERE); return r
    def modal(self): return self.g.func(MAIN + 'return pl.ComponentGui.ModalPanelWidget?.GetType().Name ?? "none";')
    def menu(self):
        if self.modal() != "DmMenuPanel": self.key("F6"); time.sleep(1.2)
        return self.step("menu", self.modal())
    def close(self): self.g.func(CLOSE_MODAL); time.sleep(.5)
    def buy(self, group, item, finish="原厂外观"):
        """B, a category, a weapon, its finish, confirm, enter - all with clicks."""
        if self.modal() != "DmWheelPanel": self.key("B"); time.sleep(.8)
        self.wheel(group); self.wheel(item); self.click(finish); self.click("确认"); return self.click("入场", 1.5)


def main(label, core, dm):
    # core: the CS packages of both processes, or "<host>/<client>" for mixed editions as the user plays (a phone hosting
    # Lite + agents, a computer joining with Full); "+" joins several packages of one side
    host_core, client_core = (core.split("/", 1) if "/" in core else (core, core))
    ta = MODS / TA_BUILT.name; dm_pkg = package(dm)
    host_pkgs, client_pkgs = [ta, *map(package, host_core.split("+")), dm_pkg], [ta, *map(package, client_core.split("+")), dm_pkg]
    pkgs = list(dict.fromkeys(host_pkgs + client_pkgs))
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"dm-mp-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"dm-mp-{label}", "engine": "1.9.3.2_MP", "packages": {p.name: sha(p) for p in pkgs}, "host": [p.name for p in host_pkgs], "client": [p.name for p in client_pkgs], "mpBuild": sha(m0.MPBIN / "Survivalcraft.dll"), "steps": [], "checks": [], "frames": []}
    T0 = time.time(); server = client = None
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True); return ok
    def jview(side):
        try: return json.loads(side.g.func(VIEW))
        except Exception: return {}

    try:
        server = Game("server", case_dir, host_pkgs); to_menu(server); H = Side(server, label, case_dir, R, T0)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        H.step("creative world", poll(server, CREATIVE, lambda v: v == "Creative", 20))
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        server.func(KEEP_ACTIVE); H.step("settings", server.func(GUI_IN_SHOTS))
        x, y, z = [int(v) for v in H.step("arena", build_arena(server)).split()]
        # round 6: a 3 x 3 wall of planks in one corner of the arena, built before any match (the arena's terrain is protected
        # under a match, design §4.2): x+2..x+4 at z+3, off every line between the spawn points
        H.step("planks wall (before the match)", server.func(planks_wall(x + 2, x + 4, y, z + 3)))
        H.go(x, y, z); time.sleep(1)
        H.step("camera", poll(server, MAIN + 'return pl.GameWidget.ActiveCamera.GetType().Name;', lambda v: v == "FppCamera", 30)); server.func(FPP)

        # ---- the host builds the arena (the same clicks as the single-player walkthrough)
        H.menu(); H.click("把本世界启用为竞技世界"); H.view("after enable")
        H.close(); H.go(x - 4, y, z - 4); H.menu(); H.click("地图"); H.click("角点 1")
        H.close(); H.go(x + 4, y, z + 8); H.menu(); H.click("地图"); H.click("角点 2")
        for i, (sx, sz) in enumerate([(-3, -3), (3, -3), (-3, 7), (3, 7)]):
            H.close(); H.go(x + sx, y, z + sz); H.menu(); H.click("地图"); H.click("添加复活点")
        H.close(); H.go(x, y, z + 12); H.menu(); H.click("地图"); H.click("准备/观战点")
        H.menu(); H.click("地图"); H.click("检查地图")   # round 3: a map action that succeeds closes the menu
        host = json.loads(server.func(STATE)); H.step("host state", host)
        check("arena built through the host's menu", host.get("region") and host.get("spawns") == 4 and host.get("lobby"), host)
        H.close()

        # ---- a client joins over the network
        client = Game("client1", case_dir, client_pkgs); to_menu(client); join(client, server); C = Side(client, label, case_dir, R, T0)
        C.step("handshake", poll(client, HANDSHAKE, lambda v: v not in ("Pending", "NotApplicable"), 40))
        check("client accepted by the CS layer", client.func(HANDSHAKE) == "Accepted", client.func(BRIDGE))
        C.step("settings", client.func(GUI_IN_SHOTS))
        C.step("camera", poll(client, MAIN + 'return pl.GameWidget.ActiveCamera.GetType().Name;', lambda v: v == "FppCamera", 40)); client.func(FPP)
        cv = C.view("client joined"); check("client sees the arena world", isinstance(cv, dict) and cv.get("enabled"), cv)
        C.frame("joined"); H.frame("host-editing-after-join")
        C.menu(); C.frame("client-menu-editing"); C.close()

        # ---- host: start before anyone entered (must say why), then the lobby
        H.menu(); H.click("比赛"); H.click("开始比赛", 1.0); H.step("refusal", json.loads(server.func(STATE)).get("message")); H.click("开放大厅", 1.0); H.close()
        cv = poll(client, VIEW, lambda v: '"phase":"Lobby"' in v, 15); C.step("client after lobby", cv)
        check("client told the lobby is open", '"phase":"Lobby"' in (cv or ""), cv)
        time.sleep(1.5); C.frame("lobby"); C.view("client at lobby")

        # ---- both buy and enter; the host starts
        C.buy(2, 0); C.frame("client-entered"); C.view("client entered")
        H.buy(0, 0); H.view("host entered")
        hs = json.loads(server.func(STATE)); H.step("host state before start", hs)
        H.menu(); H.click("比赛"); H.click("开始比赛", 1.0); H.step("host message", json.loads(server.func(STATE)).get("message")); H.close()
        C.frame("countdown"); H.frame("countdown")
        cv = poll(client, VIEW, lambda v: '"phase":"Running"' in v, 15); C.step("client after countdown", cv)
        check("client sees the match running", '"phase":"Running"' in (cv or ""), cv)
        time.sleep(1.5)
        client.func(ACTIVE_SLOT0); server.func(ACTIVE_SLOT0); time.sleep(.5)
        cv, hv = C.view("client alive"), H.view("host alive")
        # the client bought a rifle (primary slot), the host only a pistol (secondary slot)
        check("client alive with the bought rifle", isinstance(cv, dict) and cv.get("me", "").startswith(("Alive", "SpawnProtected")) and cv.get("slots", "-").split(",")[0] != "-", cv)
        check("host alive with the bought pistol", isinstance(hv, dict) and hv.get("me", "").startswith(("Alive", "SpawnProtected")) and hv.get("slots", "-,-").split(",")[1] != "-", hv)
        C.step("equip sprites", client.func(EQUIP)); C.frame("alive-hud"); H.frame("alive-hud")

        # ---- the client shoots the host (protection ends after its seconds or when the protected player attacks)
        time.sleep(4)
        hx, hy, hz = [float(v) for v in server.func(POS).split()]
        hi = int(server.func(MAIN + 'return pl.PlayerData.PlayerIndex.ToString();'))
        C.go(int(hx) - 1 if hx > x else int(hx) + 1, int(hy), int(hz) + 3 if hz < z + 4 else int(hz) - 3); time.sleep(1.2)
        before = jview(H); H.step("host before the shots", before)
        killed = False
        for burst in range(6):
            aim = client.func(aim_at_player(hi)); C.step(f"aim {burst}", aim)
            if aim.startswith("no"): break
            client.func(FORCE_ACTIVE); client.cmd(f"DIG_RAY {aim} 40"); time.sleep(40 / 30 + 1.2)
            hv = jview(H); H.step(f"host after burst {burst}", hv)
            if hv.get("me", "").startswith(("DeathView", "SpawnPending")) or "D1" in hv.get("rows", ""): killed = True; break
        check("the client's shots killed the host (server)", killed, hv)
        time.sleep(.8); C.frame("kill-feed"); H.frame("death-panel")
        cv = poll(client, VIEW, lambda v: json.loads(v).get("feed", "") != "", 10); C.step("client view after the kill", cv)
        check("client's feed shows the kill", json.loads(cv or "{}").get("feed", "") != "", cv)
        client.func(FORCE_ACTIVE); client.cmd("KEYDOWN Tab"); time.sleep(.9); C.frame("scoreboard"); client.cmd("KEYUP Tab"); time.sleep(.4)
        hv = poll(server, VIEW, lambda v: '"me":"Alive' in v or '"me":"SpawnProtected' in v, 20); H.step("host after respawn", hv)
        check("host respawned", '"me":"Alive' in (hv or "") or '"me":"SpawnProtected' in (hv or ""), hv)
        H.frame("respawned")

        # ---- round 6: a wooden wall between them; the client's rounds cross it (CS2's penetration, wood easy) and hurt the host
        time.sleep(4.5)
        # the two on either side of the wall built before the match: the client 2.5 blocks in front, the host 3.5 behind
        C.go(x + 3, y, z); H.go(x + 3, y, z + 6); time.sleep(1.5)
        aim = client.func(aim_at_player(hi)); C.step("aim through the wall", aim)
        first = H.step("what the aim meets first (server)", server.func(first_on(aim)))
        before = server.func(HOST_HP); H.step("host before the wallbang", before)
        hurt = False
        for burst in range(4):
            client.func(FORCE_ACTIVE); client.cmd(f"DIG_RAY {aim} 40"); time.sleep(40 / 30 + 1.2)
            after = server.func(HOST_HP); H.step(f"host after wallbang burst {burst}", after)
            if after != before: hurt = True; break
            aim = client.func(aim_at_player(hi))
        check("rounds crossed a wooden wall and hurt the host (round 6 penetration)", hurt and str(first).startswith("first planks"), {"first": first, "before": before, "after": after})
        C.frame("wallbang"); H.frame("wallbang")

        # ---- the host ends the match: the client is shown the results
        H.menu(); H.click("比赛"); H.click("结束本局", 1.2); H.close()
        cv = poll(client, VIEW, lambda v: '"phase":"Results"' in v, 15); C.step("client after stop", cv)
        check("client shown the results", '"phase":"Results"' in (cv or ""), cv)
        time.sleep(1); C.frame("results"); H.frame("results")
        R["gameErrors"] = {"server": server.errors()[:40], "client": client.errors()[:40]}
        R["dmLog"] = {"server": [l for l in server.lines if "CS_DM" in l][:120], "client": [l for l in client.lines if "CS_DM" in l][:120]}
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        for g in (client, server):
            if g is not None:
                try: g.close()
                except Exception as e: print("close failed", g.name, e, flush=True)
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"dm-mp-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    print(f"steps {len(R['steps'])}; checks {len(R['checks']) - len(failed)}/{len(R['checks'])}; frames {len(R['frames'])}; failure {R.get('failure')}; failed {failed}; {out}", flush=True)
    return 1 if R.get("failure") or failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2], sys.argv[3]))
