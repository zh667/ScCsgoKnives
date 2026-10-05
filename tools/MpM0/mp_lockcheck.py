"""Runner self-check (starts no game): the fixed MP test runtime is exclusive.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_lockcheck.py
A helper process holds the runtime lock; a second runner with a short wait must report "busy" and must not touch the
runtime; after the holder exits, the lock is free again. Prints what it sees; exit 1 on a failed expectation.
"""
import json, os, subprocess, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0

HOLD = r'''
import msvcrt, sys, time
f = open(sys.argv[1], "a+b"); f.seek(0); msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1)
print("held", flush=True); time.sleep(float(sys.argv[2])); print("released", flush=True)
'''


def main():
    m0.RUNTIME.mkdir(parents=True, exist_ok=True)
    lock = m0.RUNTIME / ".runtime.lock"
    out = {}
    holder = subprocess.Popen([sys.executable, "-c", HOLD, str(lock), "20"], stdout=subprocess.PIPE, text=True)
    out["holder"] = holder.stdout.readline().strip()
    os.environ["SC_MP_LOCK_WAIT"] = "6"
    t0 = time.time()
    r = subprocess.run([sys.executable, str(Path(m0.__file__)), "preflight"], capture_output=True, text=True, env=dict(os.environ, SC_MP_LOCK_WAIT="6"))
    out["second runner while held"] = {"exit": r.returncode, "seconds": round(time.time() - t0, 1), "said": (r.stdout + r.stderr).strip().splitlines()[-1:]}
    holder.wait(30); out["holder end"] = holder.stdout.read().strip()
    r2 = subprocess.run([sys.executable, str(Path(m0.__file__)), "preflight"], capture_output=True, text=True, env=dict(os.environ, SC_MP_LOCK_WAIT="6"))
    out["runner after release"] = {"exit": r2.returncode, "said": (r2.stdout + r2.stderr).strip().splitlines()[-1:]}
    print(json.dumps(out, ensure_ascii=False, indent=1))
    busy = "busy" in " ".join(out["second runner while held"]["said"]).lower() and out["second runner while held"]["exit"] != 0
    free = "busy" not in " ".join(out["runner after release"]["said"]).lower()
    print("lock exclusive:", busy, "/ free after release:", free)
    return 0 if busy and free else 1


if __name__ == "__main__":
    sys.exit(main())
