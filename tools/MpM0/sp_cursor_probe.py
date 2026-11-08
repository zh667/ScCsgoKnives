"""Measures whether the isolated test game clips the system cursor to its window (the user works while tests run).
One short session per SC_TEST_FREE_CURSOR value (1, then 0): main menu, a new world, 25 s in the world; GetClipCursor
sampled every 0.1 s from this (per-monitor DPI-aware) process, each rectangle compared with the whole virtual screen.
A clip outlives the game that set it, so any clip is released before each session and checked after it closes
(m0.free_cursor, which Game.close now calls).
Usage: sp_cursor_probe.py <label>   (M0_ENGINE as for the other sp_ scripts)"""
import collections, ctypes, json, os, sys, threading, time
from ctypes import wintypes
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import sp_uninstall   # noqa: F401  (standalone harness adaptations)
import m0
from m0 import RESULTS, RUNS, to_menu
user32 = ctypes.windll.user32
try: ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception: user32.SetProcessDPIAware()
def screen():
    vx, vy, vw, vh = (user32.GetSystemMetrics(i) for i in (76, 77, 78, 79)); return (vx, vy, vx + vw, vy + vh)
def sampler(stop, out, phase):
    full = screen(); r = wintypes.RECT()
    while not stop.is_set():
        user32.GetClipCursor(ctypes.byref(r)); rect = (r.left, r.top, r.right, r.bottom)
        out.append((phase[0], rect != full, rect)); time.sleep(0.1)
def main(label):
    stamp = time.strftime("%Y%m%d-%H%M%S"); case = RUNS / f"cursor-probe-{label}-{stamp}"; case.mkdir(parents=True)
    R = {"screen": screen(), "modes": {}}
    try:
        for mode in ["1", "0"]:
            os.environ["SC_TEST_FREE_CURSOR"] = mode; user32.ClipCursor(None)
            stop = threading.Event(); samples = []; phase = ["start"]
            th = threading.Thread(target=sampler, args=(stop, samples, phase)); th.start()
            g = m0.game(f"probe{mode}", case, [])
            try:
                to_menu(g); phase[0] = "menu"; time.sleep(5)
                m0.enter_world(g); phase[0] = "world"; time.sleep(25)
            finally:
                phase[0] = "closing"; g.close(); time.sleep(0.5); stop.set(); th.join()
            by = {}
            r = wintypes.RECT(); user32.GetClipCursor(ctypes.byref(r)); by_after = (r.left, r.top, r.right, r.bottom)
            for ph in ["menu", "world", "closing"]:
                xs = [s for s in samples if s[0] == ph]
                by[ph] = {"samples": len(xs), "clipped": sum(1 for s in xs if s[1]),
                          "rects": {str(k): v for k, v in collections.Counter(s[2] for s in xs).most_common(4)}}
            by["afterClose"] = {"rect": by_after, "free": by_after == screen()}
            R["modes"][f"SC_TEST_FREE_CURSOR={mode}"] = by
            print(mode, json.dumps(by), flush=True)
    finally:
        m0.RUNTIME_OWNER.release(); m0.free_cursor()
    RESULTS.mkdir(parents=True, exist_ok=True); out = RESULTS / f"cursor-probe-{label}.json"
    out.write_text(json.dumps(R, indent=1), "utf-8"); print("screen", R["screen"], out, flush=True)
if __name__ == "__main__": main(sys.argv[1])
