"""Golden-baseline test: the round-5 CSMC 5.10 controlled sample of M9 firstperson_lookat01
(187 points) vs the SC production transform chain, layer by layer, to find the FIRST stage
that deviates. Uses the shipped C# (tools/ArmPreview golden -> CsmcKnifeRig.SampleRawBindings
and Sample().Bindings); this script only compares, it does not re-implement the rig math.

Stages compared:
  A raw attachment  : CSMC Ӝ.þ(name) == SC RightMatrix*absolute*LeftMatrix (pre-normalization)
  B normalized      : SC InverseNorm*sourcePose*Norm (SC-internal; no CSMC truth, reported)

Only the 5 present bones (weapon_hand_r, hand_r, arm_lower_r, hand_l, arm_lower_l) are checked,
located BY NAME. Every one of the 187 points and all 16 named components m00..m33 are compared
(not just endpoints), plus translation error and rotation-angle error. Reports the max error's
sampleIndex/time/bone/stage.

    python3 tools/golden_m9.py [controlled-m9-lookat01.jsonl] [clip=inspect] [fps=30]

Stage-A pass threshold: 1e-4 (round-5 measured ~5e-6, from runtime-JSON decimal truncation).
"""
import sys, os, json, math, subprocess
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEFAULT_JSONL = "/home/dev/workspaces/CSMCReverse/runs/20260903-140032/controlled-m9-lookat01.jsonl"
BONES = ["weapon_hand_r", "hand_r", "arm_lower_r", "hand_l", "arm_lower_l"]
M = ["m00","m01","m02","m03","m10","m11","m12","m13",
     "m20","m21","m22","m23","m30","m31","m32","m33"]
STAGE_A_THRESHOLD = 1e-4


def load_truth(path):
    """(sampleIndex, bone) -> 4x4 numpy (row-major of the 16 named components)."""
    truth = {}
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        if not line:
            continue
        d = json.loads(line)
        if d.get("type") == "summary" or not d.get("present"):
            continue
        truth[(int(d["sampleIndex"]), d["bone"])] = np.array([d[k] for k in M], float).reshape(4, 4)
    return truth


def run_golden(knife, clip, fps):
    env = dict(os.environ, DOTNET_ROLL_FORWARD="Major")
    proj = os.path.join(HERE, "ArmPreview", "ArmPreview.csproj")
    dll = os.path.join(HERE, "ArmPreview", "bin", "Release", "net10.0", "ArmPreview.dll")
    rig = os.path.join(ROOT, "src", "ScCsgoKnives", "Animation", "CsmcKnifeRig.cs")
    prog = os.path.join(HERE, "ArmPreview", "Program.cs")
    newest = max(os.path.getmtime(rig), os.path.getmtime(prog))
    if not os.path.exists(dll) or os.path.getmtime(dll) < newest:
        subprocess.run(["dotnet", "build", proj, "-c", "Release", "-nologo", "-v", "q"],
                       check=True, env=env, cwd=ROOT)
    out = os.path.join(ROOT, ".tmp-fist", f"golden_{knife}_{clip}.jsonl")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    r = subprocess.run(["dotnet", dll, "golden", knife, clip, str(fps), out],
                       capture_output=True, text=True, env=env, cwd=ROOT)
    if r.returncode != 0:
        print(r.stderr); raise SystemExit(r.returncode)
    print(r.stderr.strip())
    raws, norms = {}, {}
    for line in open(out, encoding="utf-8"):
        d = json.loads(line)
        i = int(d["sampleIndex"])
        for b, m in d["rawBindings"].items():
            raws[(i, b)] = np.array(m, float).reshape(4, 4)
        for b, m in d["normBindings"].items():
            norms[(i, b)] = np.array(m, float).reshape(4, 4)
    return raws, norms


def rot_angle_err(a, b):
    def orth(m):
        r = m[:3, :3].copy()
        for k in range(3):
            n = np.linalg.norm(r[k])
            if n > 1e-9:
                r[k] /= n
        return r
    rel = orth(a) @ orth(b).T
    c = max(-1.0, min(1.0, (float(np.trace(rel)) - 1.0) * 0.5))
    return math.degrees(math.acos(c))


def main():
    jsonl = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_JSONL
    clip = sys.argv[2] if len(sys.argv) > 2 else "inspect"
    fps = int(sys.argv[3]) if len(sys.argv) > 3 else 30
    truth = load_truth(jsonl)
    samples = sorted({k[0] for k in truth})
    print(f"truth: {len(truth)} matrices, {len(samples)} sample points, bones={sorted({k[1] for k in truth})}")

    raws, norms = run_golden("m9", clip, fps)

    print("\n[stage A] raw attachment  CSMC Ӝ.þ(name)  vs  SC RightMatrix*absolute*LeftMatrix")
    worst = (0.0, None); per_bone = {b: (0.0, 0.0, 0.0) for b in BONES}
    missing = 0
    for (i, b), T in truth.items():
        S = raws.get((i, b))
        if S is None:
            missing += 1; continue
        elem = float(np.max(np.abs(S - T)))
        tr = float(np.linalg.norm(S[3, :3] - T[3, :3]))
        ang = rot_angle_err(S, T)
        pe, pt, pa = per_bone[b]
        per_bone[b] = (max(pe, elem), max(pt, tr), max(pa, ang))
        if elem > worst[0]:
            worst = (elem, (i, i / fps, b))
    print(f"  {'bone':16}{'maxElem':>12}{'maxTransl':>12}{'maxRotDeg':>12}")
    for b in BONES:
        pe, pt, pa = per_bone[b]
        print(f"  {b:16}{pe:12.2e}{pt:12.2e}{pa:12.3f}")
    print(f"  missing SC bones: {missing}")
    print(f"  MAX element error = {worst[0]:.3e} @ sample/time/bone={worst[1]}")
    verdict = "PASS" if worst[0] < STAGE_A_THRESHOLD else "FAIL"
    print(f"  stage A verdict (threshold {STAGE_A_THRESHOLD:.0e}): {verdict}")

    # stage B is SC-internal (no CSMC truth): report the normalization delta magnitude so a
    # later stage's deviation can be traced, without asserting it against the client.
    print("\n[stage B] normalization delta  ||normBinding - rawBinding||max  (SC-internal, informational)")
    for b in BONES:
        d = 0.0
        for i in samples:
            r, n = raws.get((i, b)), norms.get((i, b))
            if r is not None and n is not None:
                d = max(d, float(np.max(np.abs(n - r))))
        print(f"  {b:16} max|norm-raw|={d:.3e}")

    print("\nVERDICT:", "stage A (animation + Binding) reproduces the CSMC client truth"
          if verdict == "PASS" else "stage A DEVIATES -- first deviation is in the rig sampling/Binding math")
    return 0 if verdict == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
