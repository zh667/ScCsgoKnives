"""CS2's player hitboxes for the deathmatch package (deathmatch round 5, 2026-10-05; the user: "命中判定沿用核心的头、身体、
手臂、腿划分，没有和 CS2 对照测过" and chose "CS2 命中盒"), read-only on the CS2 install.

Run on Windows: ./tools/dev.ps1 python tools/cs2_dm_hitboxes.py
Source: the installed CS2 pak01 VPK through the Source2Viewer CLI the other imports use:
  agents/models/ctm_sas/ctm_sas.vmdl_c, agents/models/tm_phoenix/tm_phoenix.vmdl_c (a CT and a T agent)
  DATA block: the skeleton (m_boneName, m_nParent, m_bonePosParent, m_boneRotParent)
  MDAT block: m_hitboxsets "cstrike" - per hitbox the bone, the capsule's two centres in that bone's space
              (m_vMinBounds / m_vMaxBounds), m_flShapeRadius, m_nGroupId (CS hit group), m_nShapeType (2 = capsule)
What is AS READ: every capsule's bone, radius, group and its two centres in bone space; the bind pose.
What is MODELLED (the pose a capsule set is placed in; CS2 poses its hitboxes with the animation a player plays, which is
not in these files):
  standing  fitted to CS2 itself (1.41.8.8, 2026-10-04, aim_map: single nospread AK-47 shots stepped down a bot facing
            the shooter at 174 units, the server's log naming each hit group): pelvis and spine_0 (stomach) as in the
            bind pose, everything from spine_1 up (chest, neck, head, arms) UPPER_DROP lower, each leg turned out
            LEG_SPREAD degrees about its hip - 24 of the 26 body rows of that sweep agree, the two others sit on a band
            edge (<= 1 unit). The bot's head was off the centre line (its aiming pose) and is not modelled: the head
            stays centred as this game's models hold it. The arms are lowered from the bind pose's spread into a
            rifle-holding line (upper arm down and forward, forearm forward and in), lengths kept
  crouched  not a separate table: this game's crouch is not CS2's (Survivalcraft lowers the eye to 45% of standing,
            ComponentHumanModel; CS2 to 72%), and its hip would sit at the ankle - the game scales the standing set's
            heights by the eye's present height over the standing one (DmHitboxes), radii kept
Output: src/ScCsgoDeathmatch/Data/dm_cs2_hitboxes.json (CS2 units, body frame x forward, y left, z up, origin at the feet)
Record: docs/tasks/deathmatch-cs2-hitboxes-20261005-assets.json (VPK identity, model hashes, the output's hash)
"""
import hashlib, json, math, re, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
CS2 = Path(r"E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive")
VPK = CS2 / "game/csgo/pak01_dir.vpk"
CLI = next(p for p in [ROOT / ".tmp/vrf-cli/Source2Viewer-CLI.exe", Path(r"E:\CSMCReverse-Tools\ValveResourceFormat\CLI\bin\Release\Source2Viewer-CLI.exe")] if p.exists())
WORK = ROOT / ".tmp/dev-temp/cs2-hitbox-20261005"
OUT = ROOT / "src/ScCsgoDeathmatch/Data/dm_cs2_hitboxes.json"
RECORD = ROOT / "docs/tasks/deathmatch-cs2-hitboxes-20261005-assets.json"
MODELS = ["agents/models/ctm_sas/ctm_sas.vmdl_c", "agents/models/tm_phoenix/tm_phoenix.vmdl_c"]
EYE_STAND = 64.062561   # CS2's standing view offset (VEC_VIEW), units
UPPER_DROP, LEG_SPREAD = 4.5, 16.0   # the standing pose fitted to CS2's measured sweep (see above)
UPPER_GROUPS = {1, 2, 4, 5, 8}      # head, chest, arms, neck: what moves with spine_1 and above
GROUPS = {0: "generic", 1: "head", 2: "chest", 3: "stomach", 4: "left arm", 5: "right arm", 6: "left leg", 7: "right leg", 8: "neck"}
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()

# ---------------------------------------------------------------- reading
def export(member):
    target = WORK / Path(member).name
    if not target.exists():
        r = subprocess.run([str(CLI), "-i", str(VPK), "-o", str(target), "--vpk_filepath", member], capture_output=True, text=True, encoding="utf-8", errors="replace")
        assert r.returncode == 0 and target.is_file(), (member, r.stderr[-400:])
    blocks = {}
    for blk in ("DATA", "MDAT"):
        t = subprocess.run([str(CLI), "-i", str(target), "--block", blk], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
        (WORK / f"{target.stem}_{blk}.txt").write_text(t, "utf-8"); blocks[blk] = t
    return target, blocks
def array(text, key):
    i = text.index(key + " =")
    j = text.index("[", i); depth = 0
    for k in range(j, len(text)):
        if text[k] == "[": depth += 1
        elif text[k] == "]":
            depth -= 1
            if depth == 0: return text[j + 1:k]
    raise ValueError(key)
def numbers(s): return [float(x) for x in re.findall(r"-?\d+(?:\.\d+)?(?:[eE]-?\d+)?", s)]
def skeleton(data):
    names = re.findall(r'"([^"]+)"', array(data, "m_boneName"))
    parents = [int(x) for x in numbers(array(data, "m_nParent"))]
    pos = numbers(array(data, "m_bonePosParent")); rot = numbers(array(data, "m_boneRotParent"))
    assert len(parents) == len(names) and len(pos) == 3 * len(names) and len(rot) == 4 * len(names)
    return names, parents, [pos[3 * i:3 * i + 3] for i in range(len(names))], [rot[4 * i:4 * i + 4] for i in range(len(names))]
def hitboxes(mdat):
    i = mdat.index('key = "cstrike"'); body = array(mdat[i:], "m_HitBoxes")
    out = []
    for block in re.split(r"\n\t*\},\s*\n\t*\{", body):
        def field(name, kind=str):
            m = re.search(name + r' = ("([^"]*)"|\[([^\]]*)\]|(-?[\d.]+))', block)
            if not m: return None
            return m.group(2) if m.group(2) is not None else numbers(m.group(3)) if m.group(3) is not None else kind(m.group(4))
        if field("m_sBoneName") is None: continue
        out.append(dict(name=field("m_name"), bone=field("m_sBoneName"), a=field("m_vMinBounds"), b=field("m_vMaxBounds"),
                        r=field("m_flShapeRadius", float), group=int(field("m_nGroupId", float)), shape=int(field("m_nShapeType", float))))
    return out

# ---------------------------------------------------------------- math (quaternions x, y, z, w)
def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx, aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]
def qrot(q, v):
    x, y, z, w = q; vx, vy, vz = v
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return [vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx)]
def add(a, b): return [a[i] + b[i] for i in range(3)]
def sub(a, b): return [a[i] - b[i] for i in range(3)]
def mul(a, s): return [x * s for x in a]
def length(a): return math.sqrt(sum(x * x for x in a))
def unit(a): l = length(a); return [x / l for x in a]
def world(names, parents, pos, rot):
    gp, gr = [None] * len(names), [None] * len(names)
    for i in range(len(names)):
        p = parents[i]
        if p < 0: gp[i], gr[i] = pos[i][:], rot[i][:]
        else: gr[i] = qmul(gr[p], rot[i]); gp[i] = add(gp[p], qrot(gr[p], pos[i]))
    return gp, gr

def capsules(model):
    target, blocks = export(model)
    names, parents, pos, rot = skeleton(blocks["DATA"]); gp, gr = world(names, parents, pos, rot)
    index = {n: i for i, n in enumerate(names)}
    # the hitboxes name their bones in lower case ("leg_upper_l"); the skeleton keeps its own case ("leg_upper_L")
    lower = {n.lower(): i for i, n in enumerate(names)}
    caps = []
    for h in hitboxes(blocks["MDAT"]):
        assert h["shape"] == 2, ("not a capsule", h)
        b = lower[h["bone"].lower()]
        caps.append(dict(name=h["name"], bone=names[b], group=h["group"], r=h["r"], a=add(gp[b], qrot(gr[b], h["a"])), b=add(gp[b], qrot(gr[b], h["b"])),
                         local=(h["a"], h["b"])))
    joints = {n: gp[index[n]] for n in names}
    return dict(model=model, file=target, caps=caps, joints=joints, rot={n: gr[index[n]] for n in names})

# ---------------------------------------------------------------- the two poses
ARM_CHAIN = {"L": ["arm_upper_L", "arm_lower_L", "hand_L"], "R": ["arm_upper_R", "arm_lower_R", "hand_R"]}
LEG_CHAIN = {"L": ["leg_upper_L", "leg_lower_L", "ankle_L"], "R": ["leg_upper_R", "leg_lower_R", "ankle_R"]}
def side_of(bone):
    for s, chain in list(ARM_CHAIN.items()) + list(LEG_CHAIN.items()):
        if bone in chain: return s
    return None
def place_chain(joints, chain, root, d1, d2):
    """the chain's joints re-placed from its root along two directions, segment lengths kept: a bone's capsule follows its
    joint as a rigid piece (rotation from the old segment direction to the new one)"""
    L1 = length(sub(joints[chain[1]], joints[chain[0]])); L2 = length(sub(joints[chain[2]], joints[chain[1]]))
    new = {chain[0]: root, chain[1]: add(root, mul(unit(d1), L1))}; new[chain[2]] = add(new[chain[1]], mul(unit(d2), L2))
    return new
def rotation_between(u, v):
    u, v = unit(u), unit(v); c = sum(u[i] * v[i] for i in range(3)); ax = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
    s = length(ax)
    if s < 1e-9: return [0, 0, 0, 1] if c > 0 else [1, 0, 0, 0]
    ang = math.atan2(s, c); ax = mul(ax, 1 / s); return [ax[0] * math.sin(ang / 2), ax[1] * math.sin(ang / 2), ax[2] * math.sin(ang / 2), math.cos(ang / 2)]
def move_capsule(cap, old_from, old_to, new_from, new_to):
    q = rotation_between(sub(old_to, old_from), sub(new_to, new_from))
    f = lambda p: add(new_from, qrot(q, sub(p, old_from)))
    return dict(cap, a=f(cap["a"]), b=f(cap["b"]))
def turn_about(p, pivot, degrees):
    """a point turned about the forward axis through pivot (positive: toward +y)"""
    r = math.radians(degrees); dy, dz = p[1] - pivot[1], p[2] - pivot[2]
    return [p[0], pivot[1] + dy * math.cos(r) - dz * math.sin(r), pivot[2] + dy * math.sin(r) + dz * math.cos(r)]
def repose(src):
    J = dict(src["joints"]); caps = [dict(c) for c in src["caps"]]
    segments = {}   # bone -> (old from, old to, new from, new to)
    # arms: a rifle-holding line in front of the chest (modelled), from the shoulder where the fitted stance has it
    for s, chain in ARM_CHAIN.items():
        sign = 1 if s == "L" else -1                     # +y is left
        shoulder = add(J[chain[0]], [0, 0, -UPPER_DROP])
        new = place_chain(J, chain, shoulder, [.30, .22 * sign, -.93], [.88, -.38 * sign, .28])
        hand_dir = [.9, -.3 * sign, .1]; tip = add(new[chain[2]], mul(unit(hand_dir), 6.0))
        old_tip = add(J[chain[2]], mul(unit(sub(J[chain[2]], J[chain[1]])), 6.0))
        segments[chain[0]] = (J[chain[0]], J[chain[1]], new[chain[0]], new[chain[1]])
        segments[chain[1]] = (J[chain[1]], J[chain[2]], new[chain[1]], new[chain[2]])
        segments[chain[2]] = (J[chain[2]], old_tip, new[chain[2]], tip)
    out = []
    for c in caps:
        leg = side_of(c["bone"]) is not None and c["bone"] not in ARM_CHAIN["L"] + ARM_CHAIN["R"]
        if c["bone"] in segments: c = move_capsule(c, *segments[c["bone"]])
        elif c["group"] in UPPER_GROUPS: c = dict(c, a=add(c["a"], [0, 0, -UPPER_DROP]), b=add(c["b"], [0, 0, -UPPER_DROP]))
        if leg:
            hip = J[LEG_CHAIN["L" if c["bone"].endswith("_L") else "R"][0]]
            spread = LEG_SPREAD if c["bone"].endswith("_L") else -LEG_SPREAD
            c = dict(c, a=turn_about(c["a"], hip, spread), b=turn_about(c["b"], hip, spread))
        out.append(dict(name=c["name"], bone=c["bone"], group=c["group"], groupName=GROUPS.get(c["group"], str(c["group"])), r=round(c["r"], 4),
                        a=[round(x, 4) for x in c["a"]], b=[round(x, 4) for x in c["b"]]))
    return out

if __name__ == "__main__":
    WORK.mkdir(parents=True, exist_ok=True)
    print("cli", CLI, flush=True)
    sources = [capsules(m) for m in MODELS]
    # the agents must agree on the set (same bones, groups and radii): one table serves every appearance
    base = sources[0]
    for other in sources[1:]:
        a = [(c["bone"], c["group"], c["r"]) for c in base["caps"]]; b = [(c["bone"], c["group"], c["r"]) for c in other["caps"]]
        print(other["model"], "same set as", base["model"], a == b, flush=True)
        worst = max(max(length(sub(x["a"], y["a"])), length(sub(x["b"], y["b"]))) for x, y in zip(base["caps"], other["caps"])) if a == b else None
        print("  largest centre difference in the bind pose (units):", worst, flush=True)
    for c in base["caps"]: print(f"  {c['name']:16s} {GROUPS.get(c['group'])!s:9s} r {c['r']:4.1f} a {[round(x, 1) for x in c['a']]} b {[round(x, 1) for x in c['b']]}")
    stand = repose(base)
    result = {"Format": "ScCsgoDeathmatch.Cs2Hitboxes/1", "Units": "CS2 units (inch); body frame: x forward, y left, z up, origin at the feet",
              "Source": {"vpk": "game/csgo/pak01_dir.vpk", "vpkSha256": sha(VPK), "model": base["model"], "modelSha256": sha(base["file"]),
                         "steam": (CS2 / "game/csgo/steam.inf").read_text("utf-8", errors="replace").splitlines()[:3]},
              "Groups": {str(k): v for k, v in GROUPS.items()},
              "Pose": f"standing: fitted to CS2's measured sweep (spine_1 and up {UPPER_DROP} units below the bind pose, legs turned out {LEG_SPREAD} degrees), arms in a rifle-holding line (modelled); crouched: the standing heights scaled with the eye (in the game)",
              "EyeStanding": EYE_STAND, "Standing": stand}
    OUT.write_text(json.dumps(result, ensure_ascii=False, indent=1), "utf-8")
    RECORD.write_text(json.dumps({"output": OUT.relative_to(ROOT).as_posix(), "sha256": sha(OUT), "bytes": OUT.stat().st_size,
                                  "source": result["Source"], "models": {s["model"]: sha(s["file"]) for s in sources}}, ensure_ascii=False, indent=1), "utf-8")
    print("standing", [(c["name"], c["groupName"]) for c in stand]); print("->", OUT, sha(OUT)[:16])
