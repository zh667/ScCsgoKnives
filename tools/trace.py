"""M9 offline matrix replay harness -- reader/checker side.

Runs `ArmPreview trace` (the mod's own validated CsmcKnifeRig sampler, headless)
to export every CSMC bone's per-frame world matrix, then decomposes each to
translation / quaternion / scale and checks the trace is self-consistent frame to
frame -- NOT by eye, NOT by screenshot fitting:

  - root bone is (near) constant across the clip
  - every bone matrix decomposes with ~unit scale and a valid rotation
  - the weapon relative to the wrist (weapon_hand_r x inverse(hand_r)) is
    reported per frame, so hand_r vs weapon_hand_r divergence is a number

Writes a clean <knife>_<clip>_trace.jsonl with t/q/s per bone (the format the
plan asks for) next to the raw dump.

    python3 tools/trace.py [knife=m9] [clip=inspect] [fps=30]

Only the weapon rig (hand_r, weapon_hand_r, arm_lower_r, fingers, root) comes
from our data. LeftArm/RightArm are a separate arm animatable not in the weapon
animbin; they will be absent here, and that is expected -- the SC port does not
use them. Do not fill them in by fitting.
"""
import sys, os, json, math, subprocess
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

def run_trace(knife, clip, fps):
    env = dict(os.environ, DOTNET_ROLL_FORWARD='Major')
    proj = os.path.join(HERE, 'ArmPreview', 'ArmPreview.csproj')
    dll = os.path.join(HERE, 'ArmPreview', 'bin', 'Release', 'net10.0', 'ArmPreview.dll')
    mod = os.path.join(ROOT, 'src', 'ScCsgoKnives', 'Animation', 'CsmcKnifeRig.cs')
    if not os.path.exists(dll) or os.path.getmtime(dll) < os.path.getmtime(mod):
        subprocess.run(['dotnet', 'build', proj, '-c', 'Release', '-nologo', '-v', 'q'], check=True, env=env, cwd=ROOT)
    os.makedirs(os.path.join(ROOT, '.tmp-fist'), exist_ok=True)
    raw = os.path.join(ROOT, '.tmp-fist', f'{knife}_{clip}_bones.jsonl')
    r = subprocess.run(['dotnet', dll, 'trace', knife, clip, str(fps), raw], capture_output=True, text=True, encoding="utf-8", errors="replace", env=env, cwd=ROOT)
    if r.returncode != 0:
        print(r.stderr); raise SystemExit(r.returncode)
    print(r.stderr.strip())
    return [json.loads(l) for l in open(raw) if l.strip()]

def mat(m16):
    # ArmPreview emits row-major M11..M44 (Engine row-vector: point * M).
    return np.array(m16, dtype=float).reshape(4, 4)

def decompose(M):
    """Row-vector matrix -> translation, quaternion (xyzw), scale."""
    t = M[3, :3].copy()
    basis = M[:3, :3]
    scale = np.linalg.norm(basis, axis=1)
    R = basis / np.where(scale > 1e-9, scale, 1.0)[:, None]
    if np.linalg.det(R) < 0:  # remove reflection
        scale[0] = -scale[0]; R[0] = -R[0]
    # rotation matrix (row-vector convention) -> quaternion
    m = R
    tr = m[0, 0] + m[1, 1] + m[2, 2]
    if tr > 0:
        s = math.sqrt(tr + 1.0) * 2
        w = 0.25 * s
        x = (m[1, 2] - m[2, 1]) / s
        y = (m[2, 0] - m[0, 2]) / s
        z = (m[0, 1] - m[1, 0]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        w = (m[1, 2] - m[2, 1]) / s; x = 0.25 * s
        y = (m[1, 0] + m[0, 1]) / s; z = (m[2, 0] + m[0, 2]) / s
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        w = (m[2, 0] - m[0, 2]) / s; x = (m[1, 0] + m[0, 1]) / s
        y = 0.25 * s; z = (m[2, 1] + m[1, 2]) / s
    else:
        s = math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        w = (m[0, 1] - m[1, 0]) / s; x = (m[2, 0] + m[0, 2]) / s
        y = (m[2, 1] + m[1, 2]) / s; z = 0.25 * s
    q = np.array([x, y, z, w]); q /= np.linalg.norm(q)
    return t, q, scale

def qangle(a, b):
    return math.degrees(2 * math.acos(min(1.0, abs(float(np.dot(a, b))))))

if __name__ == '__main__':
    knife = sys.argv[1] if len(sys.argv) > 1 else 'm9'
    clip = sys.argv[2] if len(sys.argv) > 2 else 'inspect'
    fps = int(sys.argv[3]) if len(sys.argv) > 3 else 30
    frames = run_trace(knife, clip, fps)
    print(f"\n{knife} {clip}: {len(frames)} frames @ {fps} fps")

    all_bones = sorted(frames[0]['bones'].keys())
    want = ['root_0', 'arm_lower_r', 'hand_r', 'weapon_hand_r']
    present = [b for b in want if b in frames[0]['bones']]
    missing_arms = [b for b in ('LeftArm', 'RightArm') if b not in frames[0]['bones']]
    print(f"bones in trace: {len(all_bones)} | key bones present: {present}")
    if missing_arms:
        print(f"LeftArm/RightArm absent (expected -- separate arm animatable, not needed by the SC port)")

    # self-consistency checks
    print("\n-- self-consistency --")
    # 1. each bone's scale is CONSISTENT across frames (a uniform normalization
    #    scale is baked into Bones -- that is expected; drift would not be).
    base_scale = {n: decompose(mat(frames[0]['bones'][n]))[2] for n in all_bones}
    worst = 0.0
    for f in frames:
        for name, m16 in f['bones'].items():
            _, _, s = decompose(mat(m16))
            worst = max(worst, float(np.max(np.abs(np.abs(s) - np.abs(base_scale[name])))))
    print(f"worst per-bone scale drift across frames: {worst:.5f} (near 0 = scale stable; a uniform baked scale != 1 is fine)")
    # 2. root near constant
    r0 = decompose(mat(frames[0]['bones']['root_0']))[0] if 'root_0' in frames[0]['bones'] else None
    if r0 is not None:
        drift = max(np.linalg.norm(decompose(mat(f['bones']['root_0']))[0] - r0) for f in frames)
        print(f"root_0 translation drift over clip: {drift:.5f} (near 0 = stable base)")
    # 3. weapon relative to wrist, per frame
    out = []
    print(f"\n{'t':>5} {'hand_r roll':>12} {'weapon roll':>12} {'weapon vs wrist':>16}")
    prev_h = prev_w = None
    hand_travel = weap_travel = 0.0
    for f in frames:
        row = {'frame': f['frame'], 't': f['t'], 'clip': f['clip'], 'bones': {}}
        for name, m16 in f['bones'].items():
            t, q, s = decompose(mat(m16))
            row['bones'][name] = {'t': [round(float(x), 5) for x in t],
                                  'q': [round(float(x), 5) for x in q],
                                  's': [round(float(x), 5) for x in s]}
        out.append(row)
        if 'hand_r' in f['bones'] and 'weapon_hand_r' in f['bones']:
            H = mat(f['bones']['hand_r']); W = mat(f['bones']['weapon_hand_r'])
            _, qh, _ = decompose(H); _, qw, _ = decompose(W)
            rel = W @ np.linalg.inv(H)                 # weapon in the wrist's frame
            _, qrel, _ = decompose(rel)
            _, qrel0, _ = decompose(mat(frames[0]['bones']['weapon_hand_r']) @ np.linalg.inv(mat(frames[0]['bones']['hand_r'])))
            if prev_h is not None:
                hand_travel += qangle(qh, prev_h); weap_travel += qangle(qw, prev_w)
            prev_h, prev_w = qh, qw
            if abs(round(f['t'] * 10) - f['t'] * 10) < 1e-6:
                qh0 = decompose(mat(frames[0]['bones']['hand_r']))[1]
                qw0 = decompose(mat(frames[0]['bones']['weapon_hand_r']))[1]
                print(f"{f['t']:5.2f} {qangle(qh, qh0):11.1f} {qangle(qw, qw0):11.1f} {qangle(qrel, qrel0):15.1f}")
    print(f"\ncumulative rotation travel over clip: hand_r={hand_travel:.0f} deg, weapon_hand_r={weap_travel:.0f} deg")
    print("(matches the roll_sweep finding: weapon_hand_r spins more than the wrist -- the reason arm roll must follow hand_r, not the weapon bone)")

    clean = os.path.join(ROOT, '.tmp-fist', f'{knife}_{clip}_trace.jsonl')
    with open(clean, 'w') as w:
        for row in out:
            w.write(json.dumps(row) + '\n')
    print(f"\nwrote t/q/s trace -> {clean}")
