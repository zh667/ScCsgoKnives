"""Builds a small survival test world for a phone run (TEST-ONLY): a flat granite strip, the player at its south end with
16 weapon cases in the first hotbar slot, and a five-man enemy squad (shield escort + pistol bomber) 34 blocks north that
has seen the player. Saved, then the world folder is copied to <out>. Usage: M0_ENGINE=131 python sp_phoneworld.py
<label> <core package>[+<package>...] <out folder> [squad|bomb]
squad (default): the squad 34 blocks north. bomb: no squad; an enemy C4 planted 9 blocks north with its full 40 s fuse,
so a phone run sees the alert (or its absence) right after loading and can open a case before it goes off."""
import shutil, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RUNS, to_menu, poll
from mp_m1 import MAIN, PROJECT, KEEP_ACTIVE, SURVIVAL, teleport, STAND
from sp_engine_compat import STARTER_NONE, DURABLE, CLOSE_DIALOGS
from sp_round9 import STRIP, MUTE, CASE

m0.firewall_preflight = lambda: {"skipped": "standalone sp run", "problems": []}
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)


def main(label, packages, out, mode="squad"):
    pkgs = [Path(p) for p in packages.split("+")]
    case_dir = RUNS / f"phoneworld-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; case_dir.mkdir(parents=True)
    g = m0.game("pw", case_dir, pkgs)
    def F(code, timeout=60): return g.func(code, timeout)
    try:
        to_menu(g); F(MUTE)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        poll(g, SURVIVAL, lambda v: v == "Survival", 20)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240); F(KEEP_ACTIVE); F(MUTE); F(DURABLE); F(STARTER_NONE); F(CLOSE_DIALOGS)
        t0 = time.time(); r = F(STRIP)
        while r == "wait" and time.time() - t0 < 120: time.sleep(1); r = F(STRIP)
        x, y, z = [int(v) for v in r.split()]
        F(teleport(x, y, z)); F(STAND)
        print("strip", x, y, z, flush=True)
        print(F(MAIN + f'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); inv.AddSlotItems(0, {CASE}, 16); inv.ActiveSlotIndex = 0; '
                'var info = project.FindSubsystem<Game.SubsystemGameInfo>(true); info.WorldSettings.Name = "CS phone test"; '
                'players.GlobalSpawnPosition = pl.ComponentBody.Position; pl.PlayerData.SpawnPosition = pl.ComponentBody.Position; return info.DirectoryName;'), flush=True)
        if mode == "bomb":
            print(F(PROJECT + f'var b = project.FindSubsystem<Game.SubsystemTacticalBombs>(true); bool ok = b.PlantFrom(new Engine.Vector3({x} + .5f, {y}, {z} - 9 + .5f), 0f, false); return "bomb " + ok + " " + b.Bombs.Count;'), flush=True)
            F(MAIN + 'var info = project.FindSubsystem<Game.SubsystemGameInfo>(true); info.WorldSettings.Name = "CS phone bomb"; return "named";')
        else:
            print(F(PROJECT + f'var d = project.FindSubsystem<Game.SubsystemTacticalEnemies>(true); int n = d.SpawnManual(new Engine.Point3({x}, {y - 1}, {z - 34}), 5); return n + " " + d.ManualFailure;'), flush=True)
            F(MAIN + 'var info = project.FindSubsystem<Game.SubsystemGameInfo>(true); info.WorldSettings.Name = "CS phone squad"; return "named";')
        directory = F(MAIN + 'return project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName;')
        m = g.mark(); g.cmd('EXEC GameManager.SaveProject(true, true); GameManager.DisposeProject(); ScreensManager.SwitchScreen("MainMenu");'); g.wait('Entered screen "MainMenu"', 300, m)
        src = Path(directory if Path(directory).is_absolute() else g.dir / directory)
        if not src.exists():
            hits = list(g.dir.rglob(Path(directory).name)); src = hits[0] if hits else src
        dst = Path(out); shutil.rmtree(dst, ignore_errors=True); shutil.copytree(src, dst)
        print("copied", src, "->", dst, sum(f.stat().st_size for f in dst.rglob("*") if f.is_file()), flush=True)
        return 0
    finally:
        g.close(); m0.RUNTIME_OWNER.release()


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
