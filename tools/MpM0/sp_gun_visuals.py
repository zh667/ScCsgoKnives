"""deathmatch round 6 (2026-10-05, the user: "你还可以看所有枪的枪线，曳光弹，以及枪烟，我感觉这些还是不够还原"): every gun fired in
this game the way the CS2 reference frames were taken (docs/tasks/deathmatch-addon-round6-20261005.md): a wall about nine
metres ahead, a held trigger for an automatic gun (the real left button) or single presses, first person and then a side view
of the character (the game's orbit camera beside it); the game window's own picture (PrintWindow) every ~50 ms while it fires and for
a second and a half after, so the flash, the tracers and whatever smoke follows are in the frames. Frames only: what they
show is compared by looking, against CS2's frames of the same guns.

Usage: sp_gun_visuals.py <label> <core package | output-full | stage-<tag>-full> [gun,gun,...]
Isolated 1.9.3.1 copy (M0_ENGINE=131), new creative world; the player's own Mods and worlds are never used.
"""
import ctypes, json, sys, time
from ctypes import wintypes
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE, build_arena, teleport
from sp_deathmatch import package, CREATIVE, STAND_HERE, FPP
from sp_camera_fire import give, look, camera, button

AUTO = {"ak47", "m4a4", "m4a1s", "galilar", "famas", "aug", "sg556", "mp9", "mac10", "mp7", "mp5sd", "ump45", "p90", "bizon", "m249", "negev", "g3sg1", "scar20", "cz75a", "xm1014"}
WALL = MAIN + ('var t = project.FindSubsystem<Game.SubsystemTerrain>(true); var p = pl.ComponentBody.Position; int x = (int)System.MathF.Floor(p.X), y = (int)System.MathF.Floor(p.Y), z = (int)System.MathF.Floor(p.Z); '
    'for (int dx = -6; dx <= 6; dx++) for (int dy = 0; dy <= 5; dy++) t.ChangeCell(x + dx, y + dy, z - 9, Game.Terrain.MakeBlockValue(Game.SandstoneBlock.Index)); return x + " " + y + " " + z;')
# the side view: the game's orbit camera beside the character (+X of it, a little above, 2.6 m away), which keeps the
# character under control - a free camera takes no fire input
ORBIT_SIDE = MAIN + ('var w = pl.GameWidget; var o = w.FindCamera<Game.OrbitCamera>(); w.ActiveCamera = o; '
    'o.m_angles = new Engine.Vector2(0f, Engine.MathUtils.DegToRad(8f)); o.m_distance = 2.6f; '
    'o.m_position = pl.ComponentBody.Position + 0.9f * pl.ComponentBody.BoxSize.Y * Engine.Vector3.UnitY + Engine.Vector3.Transform(new Engine.Vector3(2.6f, 0f, 0f), Engine.Matrix.CreateFromYawPitchRoll(0f, 0f, Engine.MathUtils.DegToRad(8f))); '
    'return w.ActiveCamera.GetType().Name + " " + w.ActiveCamera.IsEntityControlEnabled;')
POS = MAIN + 'var p = pl.ComponentBody.Position; return System.FormattableString.Invariant($"{p.X} {p.Y} {p.Z}");'

u32 = ctypes.windll.user32; gdi = ctypes.windll.gdi32
try: ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception: pass
class BIH(ctypes.Structure):
    _fields_ = [("biSize", wintypes.DWORD), ("biWidth", wintypes.LONG), ("biHeight", wintypes.LONG), ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD),
                ("biCompression", wintypes.DWORD), ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", wintypes.LONG), ("biYPelsPerMeter", wintypes.LONG),
                ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD)]
def window_of(pid):
    found = []
    @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
    def cb(h, _):
        p = wintypes.DWORD(); u32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and u32.IsWindowVisible(h) and u32.GetWindowTextLengthW(h) > 0: found.append(h)
        return True
    u32.EnumWindows(cb, 0); return found[0] if found else None
def picture(h):
    """the window's own picture, whatever covers it on the desktop (PW_CLIENTONLY | PW_RENDERFULLCONTENT)"""
    from PIL import Image
    r = wintypes.RECT(); u32.GetClientRect(h, ctypes.byref(r)); w, hh = r.right, r.bottom
    dc = u32.GetDC(h); mem = gdi.CreateCompatibleDC(dc); bmp = gdi.CreateCompatibleBitmap(dc, w, hh); gdi.SelectObject(mem, bmp)
    u32.PrintWindow(h, mem, 3)
    bi = BIH(); bi.biSize = ctypes.sizeof(BIH); bi.biWidth = w; bi.biHeight = -hh; bi.biPlanes = 1; bi.biBitCount = 32
    buf = ctypes.create_string_buffer(w * hh * 4); gdi.GetDIBits(mem, bmp, 0, hh, buf, ctypes.byref(bi), 0)
    gdi.DeleteObject(bmp); gdi.DeleteDC(mem); u32.ReleaseDC(h, dc)
    return Image.frombuffer("RGBA", (w, hh), buf.raw, "raw", "BGRA", 0, 1).convert("RGB")


# the game slowed to a quarter, as CS2's frames were taken (host_timescale 0.25): the gun's cycle, the tracers and the flash
# all run on the game clock
def time_factor(f): return MAIN + f'var st = project.FindSubsystem<Game.SubsystemTime>(true); st.BasicGameTimeFactor = {f}f; st.GameTimeFactor = {f}f; return "factor " + project.FindSubsystem<Game.SubsystemTime>(true).GameTimeFactor;'


def main(label, core, guns=None, factor="1"):
    pkgs = [package(core)]
    names = guns.replace("+", ",").split(",") if guns else None   # m0submit splits its arguments at commas: "+" also separates
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"gun-visuals-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"gun-visuals-{label}", "packages": {p.name: sha(p) for p in pkgs}, "guns": {}, "steps": []}
    g = None
    def step(name, value=None):
        R["steps"].append({"step": name, "value": value}); print(f"{name}: {value}", flush=True); return value
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        step("creative world", poll(g, CREATIVE, lambda v: v == "Creative", 20))
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)); g.func(KEEP_ACTIVE)
        if not names: names = json.loads(g.func('return System.Text.Json.JsonSerializer.Serialize(Game.GunSpec.All.Select(s => s.Name));'))
        x, y, z = [int(v) for v in step("arena", build_arena(g)).split()]
        g.func(teleport(x, y, z + 12)); g.func(STAND_HERE); time.sleep(1.5)
        step("wall", g.func(WALL)); g.func(FPP); time.sleep(.6)
        h = window_of(g.proc.pid); step("window", bool(h))
        cx, cy = [int(v) // 2 for v in g.func(MAIN + 'return Engine.Window.Size.X + " " + Engine.Window.Size.Y;').split()]
        px, py, pz = [float(v) for v in g.func(POS).split()]
        for name in names:
            d = case_dir / "frames" / name; d.mkdir(exist_ok=True)
            step(f"{name} given", g.func(give(name))); time.sleep(1.2)
            entry = {}
            g.func(time_factor(float(factor)))
            for view in ["fp", "side"]:
                if view == "fp": g.func(FPP); g.func(look(0.0, 0.03))
                else: entry["side camera"] = g.func(ORBIT_SIDE)
                time.sleep(.8)
                saved = []; t0 = time.time(); taps = 0
                # first person: the real left button; the side view holds the on-screen fire button (SetFireButton, as the
                # camera-fire runs do)
                def press(): g.cmd(f"MOUSEDOWN Left {cx} {cy}") if view == "fp" else g.func(button(True))
                def release(): g.cmd(f"MOUSEUP Left {cx} {cy}") if view == "fp" else g.func(button(False))
                g.cmd(f"MOUSEMOVE {cx} {cy}"); time.sleep(.1)
                if name in AUTO: press()
                slow = 1 / max(.05, float(factor)); held = name in AUTO
                while time.time() - t0 < 3.4 * slow:
                    if name not in AUTO and taps < 4 and time.time() - t0 > taps * .45 * slow:
                        # the R8 fires only after its hammer is pulled for about 0.4 s of game time
                        press(); time.sleep(max(.25, (.6 if name == "revolver" else .05) * slow)); release()
                        taps += 1
                    if held and time.time() - t0 > 1.1 * slow: release(); held = False
                    im = picture(h); im = im.resize((960, int(960 * im.height / im.width))); p = d / f"{view}_{len(saved):03d}.jpg"; im.save(p, quality=86); saved.append(p.name)
                    time.sleep(.03)
                release()
                entry[view] = {"frames": len(saved)}
            g.func(time_factor(1)); g.func(FPP); R["guns"][name] = entry; step(f"{name} frames", entry)
        R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"gun-visuals-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"guns {len(R['guns'])}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
