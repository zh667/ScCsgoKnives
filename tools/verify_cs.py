"""Check the shipped C# against the CS:MC photos, using the C# files as the source of truth.

The model lives in Python and the game runs C#, so the two can drift. This parses
the constants out of KnifeTuning.cs and the grip and fist tables out of
CsmcFirstPersonRenderer.cs, rebuilds each knife's placement and its two arm boxes
exactly as the renderer does at idle, and scores them against the four reference
photos the way the fits were scored:

  * the knife's silhouette against the photo's knife pixels (symmetric Chamfer,
    tools/fistsolve.py) -- must be within a few pixels of what the solver reached;
  * the fist boxes against the photo's arm pixels (IoU, tools/fistfit.py) -- must
    be within a few percent of what the fit reached.

Then, for all twenty-two knives, that the idle left hand lands on its target and
that the right grip sits inside its own fist box.

The rig underneath is tools/rigprobe.py, which reproduces the game's own logged
bone positions to three decimals. Tolerances are the fits' own residuals plus the
scatter between the four photos, not numbers picked to make the run go green.
"""
import sys, os, re, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import rigprobe as R

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'src/ScCsgoKnives/Rendering')

def cs_constants():
    t = open(os.path.join(SRC, 'KnifeTuning.cs')).read()
    c = {}
    for m in re.finditer(r'public static float (\w+)\s*=\s*(-?[\d.]+)f;', t):
        c[m.group(1)] = float(m.group(2))
    for k in ('LeftHandTargetScreenX', 'LeftHandTargetScreenY', 'ArmLeanFromBone', 'ArmPalmOvershoot',
              'KnifePitchDegrees', 'KnifeYawDegrees', 'ArmScreenWidth', 'LeftArmScreenWidth'):
        if k not in c: raise SystemExit(f'{k} not found in KnifeTuning.cs')
    r = open(os.path.join(SRC, 'CsmcFirstPersonRenderer.cs')).read()
    m = re.search(r'static float s_projX = ([\d.]+)f, s_projY = ([\d.]+)f;', r)
    c['projX'], c['projY'] = float(m.group(1)), float(m.group(2))
    for k in ('HandBoxLength', 'HandBoxWidth', 'MinUsableForearm', 'HandModelScale'):
        c[k] = float(re.search(r'const float ' + k + r' = ([\d.]+)f', r).group(1))
    c['ReferenceSourceScale'] = float(re.search(r'const float ReferenceSourceScale = ([\d.]+)f', r).group(1))
    return c

def cs_grips():
    r = open(os.path.join(SRC, 'CsmcFirstPersonRenderer.cs')).read()
    tbl = r[r.index('static readonly Vector3[] s_gripOffsets = ['):]
    tbl = tbl[:tbl.index('];')]
    grips = {n: (float(x), float(y), float(z)) for x, y, z, n in
             re.findall(r'new\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f\),\s*// (\w+)', tbl)}
    m = re.search(r'ShadowDaggerLeftGrip = new\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f\)', r)
    left = {'push': tuple(float(v) for v in m.groups())}
    return grips, left

def cs_fists():
    """Per-knife FistSpec: (anchorX, anchorY, lean, overshoot, scale, leftX, leftY, leftLean, leftNear, leftWidth, near, width); NaN = shared."""
    r = open(os.path.join(SRC, 'CsmcFirstPersonRenderer.cs')).read()
    def spec(args):
        vals = [float('nan') if 'NaN' in a else float(a.rstrip('f')) for a in args.split(',')]
        while len(vals) < 12: vals.append(float('nan'))
        return vals
    shared = spec(re.search(r's_sharedFist = new\(([^)]*)\)', r).group(1))
    claw = spec(re.search(r's_clawFist = new\(([^)]*)\)', r).group(1))
    fists = {}
    for name, args in re.findall(r'\["(\w+)"\] = new\(([^)]*)\)', r):
        fists[name] = spec(args)
    for name in re.findall(r'\["(\w+)"\] = s_clawFist', r):
        fists[name] = claw
    return {n: fists.get(n, shared) for n in R.NAMES}

C = cs_constants()
GRIPS, LEFT_GRIPS = cs_grips()
FISTS = cs_fists()
PX, PY = C['projX'], C['projY']
W, H = 1920, 1080

def orv(v, fallback): return fallback if math.isnan(v) else v

def screen(p):
    d = -p[2]
    return None if d <= 1e-4 else np.array([p[0]*PX/d*0.5+0.5, 0.5-p[1]*PY/d*0.5])

def to_view(sx, sy, d):
    return np.array([(sx-0.5)*2*d/PX, (0.5-sy)*2*d/PY, -d])

def anchor_for(name):
    f = FISTS[name]
    return to_view(orv(f[0], C['AnchorScreenX']), orv(f[1], C['AnchorScreenY']), C['AnchorDepth'])

def lean_for(name, left):
    f = FISTS[name]
    return orv(f[7], C['LeftArmLean']) if left else orv(f[2], C['RightArmLean'])

def overshoot_for(name, left):
    return C['ArmPalmOvershoot'] if left else orv(FISTS[name][3], C['ArmPalmOvershoot'])

def left_target_for(name):
    f = FISTS[name]
    return orv(f[5], C['LeftHandTargetScreenX']), orv(f[6], C['LeftHandTargetScreenY'])

def near_for(name, left):
    return orv(FISTS[name][8], C['LeftArmNear']) if left else orv(FISTS[name][10], C['RightArmNear'])

def screen_width_for(name, left):
    return orv(FISTS[name][9], C['LeftArmScreenWidth']) if left else orv(FISTS[name][11], C['ArmScreenWidth'])

def rig_scale(name, rig):
    return C['KnifeScale'] * FISTS[name][4] * rig.ref_scale / C['ReferenceSourceScale']

def project_down_arm(grip, lean_degrees, near):   # ProjectDownArm, transcribed
    depth = -grip[2]
    if not depth > 0.01: return grip + np.array([0.0, -1.0, 0.0])
    sx, sy = grip[0]*PX/depth*0.5+0.5, 0.5-grip[1]*PY/depth*0.5
    aspect = PY/PX
    lean = math.radians(lean_degrees)
    stepx, stepy = math.sin(lean)/aspect, math.cos(lean)
    run = min(4.0, max(0.05, (C['ArmExitY']-sy)/max(stepy, 0.05)))
    return to_view(sx+stepx*run, sy+stepy*run, depth/max(near, 0.1))

def project_plane(v, n):
    p = v - n*float(np.dot(v, n))
    l = np.linalg.norm(p)
    if l < 1e-5:
        p = np.cross(n, [0.0, 1.0, 0.0]); l = np.linalg.norm(p)
        if l < 1e-5: return np.array([1.0, 0.0, 0.0])
    return p/l

def solve_arm(name, grip, idle_grip, left):        # SolveArm, transcribed (ArmLeanFromBone = 0)
    lean = lean_for(name, left)
    elbow = project_down_arm(idle_grip, lean, near_for(name, left))
    span = elbow - grip
    reach = float(np.linalg.norm(span))
    if not math.isfinite(reach) or reach < 1e-4: return None
    axis = span/reach
    side = project_plane(grip/np.linalg.norm(grip), axis)
    up = np.cross(side, axis); up /= np.linalg.norm(up)
    depth = max(-grip[2], 0.01)
    face = max(-1.0, min(1.0, C.get('FistGripFace', 0.0)))
    per_depth = screen_width_for(name, left)*2/max(PX, 1e-4)
    view_width = per_depth*depth/max(1.0 - 0.5*face*per_depth*side[2], 0.2)
    overshoot = view_width*overshoot_for(name, left)
    return dict(grip=grip, elbow=elbow, axis=axis, side=side, up=up, lean=lean,
                width=view_width, overshoot=overshoot, reach=reach,
                seat=grip - side*(face*0.5*view_width) - axis*overshoot)

def box_corners(arm):                              # DrawArm's box, centred on the grip
    cs = []
    L = arm['reach'] + arm['overshoot']
    for a in (0.0, L):
        for su in (-0.5, 0.5):
            for ss in (-0.5, 0.5):
                cs.append(arm['seat'] + arm['axis']*a + arm['up']*(su*arm['width']) + arm['side']*(ss*arm['width']))
    return np.array(cs)

def compose(name):
    """Idle placement plus both arms, exactly as the C# builds them."""
    rig = R.rig(name)
    a = rig.absolute('idle', 0.0)
    s = rig_scale(name, rig)
    orientation = (R.scale([s]*3) @ R.rot_z(270) @ R.rot_y(180) @ R.rot_x(90)
                   @ R.rot_x(C['KnifePitchDegrees']) @ R.rot_y(C['KnifeYawDegrees']))
    hr = R.binding_matrix(rig, a, 'hand_r')
    anchor = anchor_for(name)
    idle_grip = R.xform(GRIPS[name], hr @ orientation)
    place = orientation @ R.translation(anchor - idle_grip)
    out = dict(rig=rig, place=place, scale=s, anchor=anchor)
    grip_r = R.xform(GRIPS[name], R.binding_matrix(rig, a, 'hand_r') @ place)
    out['r'] = solve_arm(name, grip_r, anchor, False)
    hl = R.binding_matrix(rig, a, 'hand_l') @ place
    fl = (R.binding_matrix(rig, a, 'arm_lower_l') @ place)[3, :3]
    usable = np.linalg.norm(hl[3, :3] - fl) >= C['MinUsableForearm']*s
    grip_l = R.xform(LEFT_GRIPS.get(name, (0, 0, 0)), hl)
    out['left_usable'] = bool(usable)
    if usable:
        tx, ty = left_target_for(name)
        target = to_view(tx, ty, C['LeftHandDepth']) if -grip_l[2] > 0.01 else grip_l
        out['l'] = solve_arm(name, target, target, True)
        out['left_target'] = (tx, ty)
    return out

def main():
    import fistsolve as FS, fistfit as FF, mccs_masks as M
    # The solver outputs the shipped tables were derived from. The photos they
    # were fitted against live in photo/ (kept out of the repository).
    kf = json.load(open(os.path.join(ROOT, 'tools/reference/knifefit.json')))
    # the refit with the renderer's own construction: square section, box centred
    # on the grip (FistGripFace 0, what the CS:MC recordings show), left grip pinned
    # at LeftHandDepth. fistfit_face.json is the same fit with the grip on the far face.
    ff = json.load(open(os.path.join(ROOT, 'tools/reference/fistfit_sq.json' if abs(C.get('FistGripFace', 0.0)) < 0.5 else 'tools/reference/fistfit_face.json')))
    print(f"constants read from KnifeTuning.cs: knifeScale {C['KnifeScale']} pitch/yaw {C['KnifePitchDegrees']}/{C['KnifeYawDegrees']}"
          f" anchor ({C['AnchorScreenX']},{C['AnchorScreenY']})@{C['AnchorDepth']} armWidth R {C['ArmScreenWidth']} L {C['LeftArmScreenWidth']}"
          f" overshoot {C['ArmPalmOvershoot']}w lean R {C['RightArmLean']} L {C['LeftArmLean']}\n")
    ok = True
    print(f"{'':10}{'chamfer':>9}{'fit':>7}{'  right IoU':>12}{'fit':>7}{'  left IoU':>11}{'fit':>7}")
    for name in ('m9', 'karambit', 'butterfly', 'tactical'):
        comp = compose(name)
        # knife silhouette, scored with the solver's own machinery but the C#'s placement
        fit = FS.Fit(name)
        fit.anchor = comp['anchor']
        kk = kf['knives'][name]
        pts = (np.c_[fit.P, np.ones(len(fit.P))] @ comp['place'])[:, :3]
        chamfer = chamfer_of(fit, pts)
        # arm boxes
        im = M.load(name); sky, arm_mask, hud, knife = M.masks(im)
        right = M.right_arm_region(arm_mask)[::2, ::2]
        r = FS.REF[name]
        region = np.zeros((H//2, W//2), bool); region[(r['F'][1]-40)//2:, 450:] = True
        mr = FF.hull_mask(box_corners(comp['r']))
        iou_r = FF.iou(mr, right, region) if mr is not None else 0.0
        left = (arm_mask & (np.arange(W)[None, :] < 900))[::2, ::2]
        rows = np.nonzero(left[350:].any(1))[0] + 350
        regionL = np.zeros((H//2, W//2), bool); regionL[rows.min()-15:, :450] = True
        ml = FF.hull_mask(box_corners(comp['l'])) if comp['left_usable'] else None
        iou_l = FF.iou(ml, left, regionL) if ml is not None else 0.0
        fit_l = ff[name]['left_cs']['iou']; fit_r = ff[name]['right_iou']
        # The fits had per-photo width, taper and (left) hand target; the shipped
        # arms share one of each across the straight knives, which is worth a few
        # percent of overlap -- the left box loses 0.07 to a target 0.01 of the frame
        # off its own photo's. A transcription error costs far more: the one-sided
        # box alone was 0.15.
        line_ok = chamfer <= kk['cost'] + 4.0 and iou_r >= fit_r - 0.06 and iou_l >= fit_l - 0.08
        ok &= line_ok
        print(f"{name:<10}{chamfer:9.1f}{kk['cost']:7.1f}{iou_r:12.3f}{fit_r:7.3f}{iou_l:11.3f}{fit_l:7.3f}   {'ok' if line_ok else 'FAIL'}")
    # every knife: left hand on target, right grip inside its fist
    print()
    worst = 0.0
    for name in R.NAMES:
        comp = compose(name)
        arm = comp['r']
        rel = arm['grip'] - arm['seat']
        along = float(np.dot(rel, arm['axis'])); across = math.hypot(float(np.dot(rel, arm['side'])), float(np.dot(rel, arm['up'])))
        inside = 0.0 <= along <= arm['reach'] + arm['overshoot'] and across <= arm['width']*0.5 + 1e-4
        if not inside:
            ok = False; print(f"  {name}: grip is not inside its fist box (along {along:.3f}, across {across:.3f})")
        if comp['left_usable']:
            s = screen(comp['l']['grip']); tx, ty = comp['left_target']
            err = math.hypot(s[0]-tx, s[1]-ty); worst = max(worst, err)
    print(f"all 22: right grip inside its fist box; idle left hand on target (worst error {worst:.4f} of the frame)")
    if worst > 0.002: ok = False
    print('\nPASS: the shipped C# reproduces the CS:MC photo fits' if ok else '\nFAIL: the shipped C# does not reproduce the fits')
    return 0 if ok else 1

def chamfer_of(fit, pts):
    """The solver's symmetric Chamfer for already-placed view-space points."""
    d = -pts[:, 2]; okm = d > 1e-3
    sx = (pts[:, 0]*PX/np.where(okm, d, 1)*0.5+0.5)*W
    sy = (0.5-pts[:, 1]*PY/np.where(okm, d, 1)*0.5)*H
    S = np.c_[sx, sy][okm]
    ix = np.clip(S[:, 0].astype(int), 0, W-1); iy = np.clip(S[:, 1].astype(int), 0, H-1)
    onscreen = (S[:, 0] >= 0) & (S[:, 0] < W) & (S[:, 1] >= 0) & (S[:, 1] < H)
    hidden = fit.t.arm[iy, ix] | ~onscreen
    Sv = S[~hidden]
    D = np.sqrt(((Sv[:, None, :] - fit.t.mask_pts[None, :, :])**2).sum(-1))
    Dall = np.sqrt(((S[:, None, :] - fit.t.mask_pts[None, :, :])**2).sum(-1))
    return float(D.min(1).mean() + Dall.min(0).mean())

if __name__ == '__main__':
    sys.exit(main())
