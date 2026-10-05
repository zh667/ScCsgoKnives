"""Unattended-runtime acceptance (docs/tasks/mp-unattended-network-20260930.md §6): one short round of the fixed runtime.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/mp_rounds.py <dev tag> <round label>
Server + client1 from the fixed role folders, the dev Lite package and the loopback-only TestAutomation. Checked:
  the executables are the fixed configured paths (hashes recorded in runtime.json); the previous run's role state was
  archived and this run starts with no world but its own; TestAutomation's command port is bound to 127.0.0.1 only and a
  packet from the machine's LAN address gets no answer; the client joins over loopback and its CS handshake is accepted;
  no Windows security prompt is open at any sample and no firewall rule other than the configured group names a runtime
  executable; everything finishes within bounded waits (no interactive wait).
"""
import json, socket, subprocess, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, PKG, RESULTS, RUNS, TA_BUILT, sha, to_menu, join, poll, powershell
from mp_m1 import PROJECT, MAIN, SURVIVAL, HANDSHAKE

WORLDS = 'return WorldsManager.WorldInfos.Count + " " + string.Join(",", WorldsManager.WorldInfos.Select(w => w.WorldSettings.Name));'
PROMPTS = ("Get-Process | Where-Object { $_.MainWindowTitle -match 'Windows (安全|Security)|防火墙|Firewall' } | "
           "ForEach-Object { $_.ProcessName + ': ' + $_.MainWindowTitle }")
OTHER_RULES = ("$root = '" + str(m0.RUNTIME).replace("'", "''") + "'; @(Get-NetFirewallApplicationFilter | Where-Object { $_.Program -and $_.Program.StartsWith($root, "
               "[StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $r = $_ | Get-NetFirewallRule; if ($r.Group -ne '" + m0.FW_GROUP + "') { $r.Name } }).Count")


def main(tag, label):
    pkg = PKG / f"dev-{tag}-lite.scmod"; ta = MODS / TA_BUILT.name
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"rounds-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"rounds-{label}", "package": {pkg.name: sha(pkg), ta.name: sha(ta)}, "steps": [], "checks": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True)
    prompts = []
    def watch(): prompts.extend(l for l in powershell(PROMPTS)[1].splitlines() if l.strip())
    server = c1 = None
    try:
        server = Game("server", case_dir, [ta, pkg])
        rt = m0.RUNTIME_OWNER.record
        check("firewall preflight passed", not rt["firewall"]["problems"], rt["firewall"])
        step("previous run's role state archived", rt.get("archived"))
        check("no stale runtime processes were left", not rt.get("staleProcesses"), rt.get("staleProcesses"))
        to_menu(server); watch()
        check("server runs from its fixed path", str(server.dir).lower() == str(m0.RUNTIME / "server").lower(), str(server.dir))
        bound = powershell(f"@(Get-NetUDPEndpoint -LocalPort {server.cmd_port} -ErrorAction SilentlyContinue | ForEach-Object {{ $_.LocalAddress }}) -join ','")[1]
        check("TestAutomation command port bound to loopback only", bound == "127.0.0.1", bound)
        lan = [a for a in socket.gethostbyname_ex(socket.gethostname())[2] if not a.startswith("127.")]
        if lan:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.bind((lan[0], 0)); s.settimeout(2)
            s.sendto(b"FUNC return 1;", (lan[0], server.cmd_port))
            try: answer = s.recvfrom(4096)[0][:40]
            except socket.timeout: answer = None
            except ConnectionResetError: answer = None; step("the LAN address has no listener on that port", "ICMP port unreachable (WinError 10054)")
            s.close()
            check("a command from the LAN address gets no answer", answer is None, f"{lan[0]} -> {answer}")
        else: step("no LAN address on this machine", "skipped")
        server.cmd('EXEC SettingsManager.ShowPlayWithFriendsDialog = false; SettingsManager.PlayWithFriendsEnabled = false; SettingsManager.SaveSettings();')
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Play"', 120, m)
        worlds = step("worlds before creating this run's", server.func(WORLDS))
        check("the run starts with no world of an earlier run", worlds.split()[0] == "0", worlds)
        m = server.mark(); server.cmd("CLICK_WIDGET NewWorld"); server.wait('Entered screen "NewWorld"', 120, m)
        poll(server, SURVIVAL, lambda v: v == "Survival", 20)
        m = server.mark(); server.cmd("CLICK_WIDGET Play"); server.wait('Entered screen "Player"', 600, m)
        m = server.mark(); server.cmd("CLICK_WIDGET PlayButton"); server.wait("Player into playing.", 600, m); watch()
        c1 = Game("client1", case_dir, [ta, pkg]); to_menu(c1)
        check("client1 runs from its fixed path", str(c1.dir).lower() == str(m0.RUNTIME / "client1").lower(), str(c1.dir))
        join(c1, server); watch()
        check("client1 joined over loopback and was accepted", poll(c1, HANDSHAKE, lambda v: v == "Accepted", 30) == "Accepted")
        watch(); time.sleep(3); watch()
        check("no Windows security prompt at any sample", not prompts, prompts)
        others = powershell(OTHER_RULES)[1]
        check("no firewall rule outside the configured group names a runtime executable", others.strip() == "0", others)
        step("role hashes", m0.RUNTIME_OWNER.record.get("roles"))
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        for g in [c1, server]:
            if g is None: continue
            R[g.name] = {"errors": g.errors(), "logLines": g.mark()}
            g.close()
        m0.RUNTIME_OWNER.release()
        R["runtime"] = m0.RUNTIME_OWNER.record
        check("every runtime process exited before the lock was released", not (R["runtime"] or {}).get("releasedWithProcesses"), (R["runtime"] or {}).get("releasedWithProcesses"))
        check("seconds within bound", time.time() - T0 < 600, round(time.time() - T0, 1))
        RESULTS.mkdir(parents=True, exist_ok=True)
        (RESULTS / f"rounds-{label}.json").write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else ""))
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
