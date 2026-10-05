"""Diagnostic (test tooling only): a 1.9.3.2_MP session with no CS package at all (the engine's bundled mods and
TestAutomation only). Does a client's own player update run, can it walk, does its slot change reach the server?

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_vanilla.py
Prints what it sees; no pass/fail.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, RESULTS, RUNS, TA_BUILT, to_menu, join, poll
m0.PLATFORM_AS_DESIGNED = False  # the platform exactly as shipped
from mp_m1 import KEEP_ACTIVE, PROJECT, MAIN, SURVIVAL, player

CLIENT = MAIN + ('var p = pl.ComponentBody.Position; var t = project.FindSubsystem<Game.SubsystemTime>(true).GameTime; '
    'return System.FormattableString.Invariant($"time {t:0.00} last {pl.LastActiveSlot} slot {pl.ComponentMiner.Inventory.ActiveSlotIndex} pos {p.X:0.00} {p.Y:0.00} {p.Z:0.00} dig {pl.ComponentInput.PlayerInput.Dig.HasValue} move {pl.ComponentInput.PlayerInput.Move}");')
def server_view(i): return player(i) + 'var p = pl.ComponentBody.Position; return System.FormattableString.Invariant($"slot {pl.ComponentMiner.Inventory.ActiveSlotIndex} pos {p.X:0.00} {p.Y:0.00} {p.Z:0.00}");'
SKIPS = MAIN + ('var names = new System.Collections.Generic.List<string>(); foreach (var a in Game.CompatNetAdapterRegistry.Adapters) { bool s = false; try { s = a.ShouldSkipClientUpdate(pl); } catch { } if (s) names.Add(a.GetType().Name); } '
    'return names.Count == 0 ? "none" : string.Join(", ", names);')


def main():
    ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"vanilla-{stamp}"; case_dir.mkdir(parents=True)
    out = []
    def say(name, value): out.append((name, value)); print(f"{name}: {value}", flush=True); return value
    server = c1 = None
    try:
        server = Game("server", case_dir, [ta]); to_menu(server)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        poll(server, SURVIVAL, lambda v: v == "Survival", 20)
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        say("server mods", server.func('return string.Join(", ", ModsManager.ModList.Where(m => m.modInfo != null).Select(m => m.modInfo.PackageName + " " + m.modInfo.Version));'))
        c1 = Game("client1", case_dir, [ta]); to_menu(c1); join(c1, server)
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        say("keep active", c1.func(KEEP_ACTIVE))
        def sample(label, n=4):
            for k in range(n): say(f"{label} #{k}", f"client [{c1.func(CLIENT)}] server [{server.func(server_view(i1))}] skip [{c1.func(SKIPS)}]"); time.sleep(0.5)
        sample("idle")
        c1.func(MAIN + 'pl.LastActiveSlot = 200; return "ok";'); sample("after a LastActiveSlot sentinel")
        c1.func(MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 3; return "ok";'); sample("after slot 3 by script")
        c1.cmd("MOVE_INPUT 0 0 1 120"); sample("walking forward (move input 120 frames)", 6)
        c1.cmd("KEYDOWN W"); sample("holding W", 4); c1.cmd("KEYUP W")
        # The same session with only the CommandCompatNet broadcast timer held on the server (m0.HOLD_COMMAND_SNAPSHOTS).
        say("hold command snapshots (server)", server.func(m0.HOLD_COMMAND_SNAPSHOTS)); time.sleep(2.5)
        sample("held: idle")
        c1.func(MAIN + 'pl.ComponentMiner.Inventory.ActiveSlotIndex = 5; return "ok";'); sample("held: slot 5 by script")
        c1.cmd("KEYDOWN W"); sample("held: holding W", 4); c1.cmd("KEYUP W")
    finally:
        for g in [c1, server]:
            if g is not None: g.close()
        import shutil
        for g in ["server", "client1"]: shutil.rmtree(case_dir / g, ignore_errors=True)  # the game copies; logs stay
        RESULTS.mkdir(parents=True, exist_ok=True)
        (RESULTS / "vanilla-mp.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), "utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
