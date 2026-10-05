"""Diagnostic (test tooling only): the state of a 1.9.3.2_MP test client while it is driven by TestAutomation.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_diag.py <dev tag> [lite|full]
Host + one client. Samples on the client: window activity, game clock, its player's active slot and position, and the
server's copy, around: a forced window activation, a slot change by script, the engine's own slot hook called directly,
a slot key, and position writes. Prints what it sees; no pass/fail.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, to_menu, join, poll
from mp_m1 import FORCE_ACTIVE, KEEP_ACTIVE, PROJECT, MAIN, SURVIVAL, ARENA, build_arena, HANDSHAKE, player, teleport, EYE, near, move_to

CLIENT = MAIN + ('var p = pl.ComponentBody.Position; var t = project.FindSubsystem<Game.SubsystemTime>(true).GameTime; '
    'return System.FormattableString.Invariant($"active {Engine.Window.IsActive} time {t:0.00} slot {pl.ComponentMiner.Inventory.ActiveSlotIndex} pos {p.X:0.00} {p.Y:0.00} {p.Z:0.00} ready {pl.PlayerData.IsReadyForPlaying} main {pl.PlayerData.IsMainPlayer}");')
def server_view(i): return player(i) + ('var p = pl.ComponentBody.Position; '
    'return System.FormattableString.Invariant($"slot {pl.ComponentMiner.Inventory.ActiveSlotIndex} pos {p.X:0.00} {p.Y:0.00} {p.Z:0.00}");')
HOOK = MAIN + 'ModsManager.HookAction("OnNetworkActiveSlotChanged", loader => { loader.OnNetworkActiveSlotChanged(pl); return false; }); return "hook called";'


def main(tag, edition):
    pkg = PKG / f"dev-{tag}-{edition}.scmod"; ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"diag-{tag}-{stamp}"; case_dir.mkdir(parents=True)
    out = []
    def say(name, value): out.append((name, value)); print(f"{name}: {value}", flush=True); return value
    server = c1 = None
    try:
        server = Game("server", case_dir, [ta, pkg]); to_menu(server)
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        poll(server, SURVIVAL, lambda v: v == "Survival", 20)
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m)
        x, y, z = say("arena", build_arena(server)).split()
        c1 = Game("client1", case_dir, [ta, pkg]); to_menu(c1); join(c1, server)
        say("accepted", poll(c1, HANDSHAKE, lambda v: v == "Accepted", 30))
        i1 = int(c1.func(MAIN + 'return pl.PlayerData.PlayerIndex;'))
        def sample(label, n=4):
            for k in range(n): say(f"{label} #{k}", f"client [{c1.func(CLIENT)}] server [{server.func(server_view(i1))}]"); time.sleep(0.5)
        sample("idle", 2)
        say("keep active", c1.func(KEEP_ACTIVE))
        PROBE = MAIN + ('var t = project.FindSubsystem<Game.SubsystemTime>(true).GameTime; '
            'return System.FormattableString.Invariant($"last {pl.LastActiveSlot} slot {pl.ComponentMiner.Inventory.ActiveSlotIndex} time {t:0.00} lastAction {pl.m_lastActionTime:0.00} input {pl.ComponentInput.PlayerInput.Dig.HasValue}/{pl.ComponentInput.PlayerInput.Interact.HasValue}");')
        say("probe", c1.func(PROBE))
        say("sentinel", c1.func(MAIN + 'pl.LastActiveSlot = 200; return "set";'))
        for k in range(4): say(f"after sentinel #{k}", c1.func(PROBE)); time.sleep(0.4)
        # Does the player update loop see an injected dig at all?
        c1.cmd("DIG_RAY 0 80 0 0 -1 0 90")
        for k in range(4): say(f"during a held dig #{k}", c1.func(PROBE)); time.sleep(0.3)
        # Which updateables does the client's SubsystemUpdate hold for the player entity?
        say("updateables", c1.func(MAIN + 'var u = project.FindSubsystem<Game.SubsystemUpdate>(true); var f = typeof(Game.SubsystemUpdate).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public); '
            'return string.Join("; ", f.Select(x => x.Name + ":" + x.FieldType.Name));'))
        say("player registered for updates", c1.func(MAIN + 'var u = project.FindSubsystem<Game.SubsystemUpdate>(true); var d = (System.Collections.IDictionary)typeof(Game.SubsystemUpdate).GetField("m_updateables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).GetValue(u); '
            'var l = (System.Collections.IList)typeof(Game.SubsystemUpdate).GetField("m_sortedUpdateables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).GetValue(u); '
            'return "dict " + d.Contains(pl) + " sorted " + l.Contains(pl) + " count " + l.Count;'))
        say("adapters that skip the player", c1.func(MAIN + 'var names = new System.Collections.Generic.List<string>(); '
            'foreach (var a in Game.CompatNetAdapterRegistry.Adapters) { bool s = false; try { s = a.ShouldSkipClientUpdate(pl); } catch (System.Exception e) { names.Add(a.GetType().Name + " threw " + e.GetType().Name); continue; } names.Add(a.GetType().Name + "=" + s); } '
            'return string.Join(", ", names);'))
        say("hook loaders", c1.func('var f = typeof(ModsManager).GetField("HookInterface", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public); '
            'if (f == null) return "no HookInterface"; var d = (System.Collections.IDictionary)f.GetValue(null); if (!d.Contains("OnIUpdateableUpdate")) return "none"; '
            'var list = (System.Collections.IEnumerable)d["OnIUpdateableUpdate"]; return string.Join(",", list.Cast<object>().Select(o => o.GetType().FullName));'))
        say("player components", c1.func(MAIN + 'return string.Join(",", pl.Entity.Components.Select(c => c.GetType().Name + (c is Game.IUpdateable ? "*" : "")));'))
    finally:
        for g in [c1, server]:
            if g is not None: g.close()
        import shutil
        for g in ["server", "client1"]: shutil.rmtree(case_dir / g, ignore_errors=True)  # the game copies; logs stay
        RESULTS.mkdir(parents=True, exist_ok=True)
        (RESULTS / f"diag-{tag}.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), "utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "lite"))
