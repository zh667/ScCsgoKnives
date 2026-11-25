"""Frame pacing at the main menu (TEST-ONLY): frame times and the engine's pacing knobs (Time.FrameDurationLimit,
Window.PresentationInterval, SettingsManager.PresentationInterval, CPU frame time). Usage:
M0_ENGINE=131|1951 python sp_menufps.py <label> <none | core package[+package...]>"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RUNS, RESULTS, to_menu
from sp_perf1124 import SAMPLER, START, STOP

m0.firewall_preflight = lambda: {"skipped": "standalone sp run", "problems": []}
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)
KNOBS = ('return System.FormattableString.Invariant($"limit={Engine.Time.FrameDurationLimit} window.interval={Engine.Window.PresentationInterval} settings.interval={Game.SettingsManager.PresentationInterval} '
         'avgFrame={Engine.Time.AverageFrameDuration * 1000:0.00}ms avgCpu={Engine.Time.AverageCpuFrameDuration * 1000:0.00}ms refresh={Engine.Window.ScreenRefreshRate}");')


def main(label, packages):
    pkgs = [] if packages == "none" else [Path(p) for p in packages.split("+")]
    case_dir = RUNS / f"menufps-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; case_dir.mkdir(parents=True)
    g = m0.game("mf", case_dir, pkgs); out = {}
    try:
        to_menu(g); g.func('Game.SettingsManager.SoundsVolume = 0f; Game.SettingsManager.MusicVolume = 0f; return "muted";'); g.func(SAMPLER)
        for t in (5, 20):
            time.sleep(t if t == 5 else 10); g.func(START); time.sleep(5); out[f"frames@{t}s"] = json.loads(g.func(STOP)); out[f"knobs@{t}s"] = g.func(KNOBS)
            print(label, t, out[f"frames@{t}s"], out[f"knobs@{t}s"], flush=True)
    finally:
        g.close(); m0.RUNTIME_OWNER.release()
    (RESULTS / f"menufps-{label}.json").write_text(json.dumps(out, indent=1), "utf-8")


if __name__ == "__main__":
    main(*sys.argv[1:])
