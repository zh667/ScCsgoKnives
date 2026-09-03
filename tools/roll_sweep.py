"""DEPRECATED for the arm's dynamics: this is a Python replica of the renderer and
it has drifted from the C# three times (bone changes, the inspect pullback). For
anything about how the fist moves, use tools/preview.py, which runs the shipped
C# itself headless. Kept for the idle photo-fit sheet only.

Sweeps one knife's clip at 30fps with the shipped roll construction (hand_r
carries the roll reference, the held part places the grip) and prints, per frame,
the rigid side angle (= the knife's own turn) and the fist angle under three rules:
old (stateless smoothstep 90..130), gated (0.11.14: square weighted by stillness),
and lin (shipped: a straight line from SquareFromDegrees to the clip's measured
hold angle). Read the summary: frames in motion where the fist diverges from the
rigid side, how far the hold squares, and whether the fist stops with the knife.

    python3 tools/roll_sweep.py [knife=m9] [clip=inspect]

hold_angle(name, comp, clip) is importable (fleet_qa uses it): the clip's extreme
rigid angle if the wrist dwells there >= 0.25s within 5 degrees, else 0 -- the same
test as the C# MeasureHolds.
"""
import sys, os, math, json, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rigprobe as R, verify_cs as V, gripsolve as G
ROOT=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
unit=lambda v: v/np.linalg.norm(v); proj=lambda v,n: v-n*np.dot(v,n)
sangle=lambda a,b,ax: math.atan2(np.dot(np.cross(a,b),ax), np.dot(a,b))
def turn(v,axis,ang): return unit(proj(v*math.cos(ang)+np.cross(axis,v)*math.sin(ang),axis))
FS,FE=0.06,0.28
FPS=30

def _setup(name, comp):
    rig=comp['rig']; place=comp['place']; idle=rig.absolute('idle',0.0)
    hr0=R.binding_matrix(rig,idle,'hand_r')@place
    ref_local=unit((np.r_[comp['r']['side'],0]@np.linalg.inv(hr0))[:3])
    held='weapon_hand_r' in G.MANIFEST[name]
    inPart=None
    if held:
        h0=R.binding_matrix(rig,idle,'hand_r'); p0=R.binding_matrix(rig,idle,'weapon_hand_r')
        inPart=(np.r_[V.GRIPS[name],1]@(h0@np.linalg.inv(p0)))[:3]
    return rig, place, ref_local, held, inPart

def frame(name, comp, setup, clip, t):
    """axis, faceOn, rigid side, signed rigid angle at time t."""
    rig, place, ref_local, held, inPart = setup
    a=rig.absolute(clip,t); hr=R.binding_matrix(rig,a,'hand_r')@place
    grip=(np.r_[inPart,1]@(R.binding_matrix(rig,a,'weapon_hand_r')@place))[:3] if held else R.xform(V.GRIPS[name],hr)
    arm=V.solve_arm(name,grip,comp['anchor'],False); axis,faceOn=arm['axis'],arm['side']
    carried=proj((np.r_[ref_local,0]@hr)[:3],axis); s=np.linalg.norm(carried); w=min(1,max(0,(s-FS)/(FE-FS)))
    rigid=unit(proj(faceOn*(1-w)+carried/max(s,1e-6)*w,axis))
    return axis, faceOn, rigid, sangle(faceOn,rigid,axis)

def clip_duration(name, clip):
    return json.load(open(f'{ROOT}/src/ScCsgoKnives/AnimationData/{name}.csmc.animation.json'))['Clips'][clip]['Duration']

_hold_cache={}
def hold_angle(name, comp, clip):
    """Radians; 0 if the clip does not rest at its extreme (same test as C# MeasureHolds)."""
    key=(name,clip)
    if key in _hold_cache: return _hold_cache[key]
    setup=_setup(name,comp); dur=clip_duration(name,clip); n=int(dur*FPS)
    sizes=[abs(frame(name,comp,setup,clip,i/FPS)[3]) for i in range(n+1)]
    best=max(sizes); dwell=sum(1 for s in sizes if s>=best-math.radians(5))
    h=best if (best>0.01 and dwell/FPS>=0.25) else 0.0
    _hold_cache[key]=h; return h

def square_lin(size, from_rad, hold_rad, weight=1.0):
    """The shipped rule: straight line from `from` to the hold -> straight-behind."""
    if hold_rad<=from_rad+0.01 or size<=from_rad: return size
    target=math.pi if size>=hold_rad else from_rad+(size-from_rad)*(math.pi-from_rad)/(hold_rad-from_rad)
    return size+(target-size)*weight

if __name__=='__main__':
    name=sys.argv[1] if len(sys.argv)>1 else 'm9'; clip=sys.argv[2] if len(sys.argv)>2 else 'inspect'
    comp=V.compose(name); setup=_setup(name,comp); dur=clip_duration(name,clip); n=int(dur*FPS)
    H=hold_angle(name,comp,clip); FROM=math.radians(V.C.get('SquareFromDegrees',45))
    OF,OFULL=math.radians(90),math.radians(130)
    print(f"{name} {clip} {dur}s | measured hold = {math.degrees(H):.1f} deg | SquareFrom = {math.degrees(FROM):.0f}")
    still=1.0; prev=None; rows=[]
    for i in range(n+1):
        t=i/FPS; axis,faceOn,rigid,ang=frame(name,comp,setup,clip,t); size=abs(ang)
        rate=0.0 if prev is None else math.degrees(abs(sangle(proj(prev,axis),rigid,axis)))*FPS
        if prev is not None:
            tgt=min(1,max(0,(90-rate)/60)); still+=(tgt-still)*(1-math.exp(-1/FPS/0.2))
        prev=rigid
        tt=min(1,max(0,(size-OF)/(OFULL-OF))); sm=tt*tt*(3-2*tt)
        old=size+(math.pi-size)*sm; gated=size+(math.pi-size)*sm*still; lin=square_lin(size,FROM,H)
        rows.append((t,math.degrees(size),math.degrees(old),math.degrees(gated),math.degrees(lin),rate))
    print(f"{'t':>5} {'rigid':>6} {'old':>6} {'gated':>6} {'lin':>6} | {'old-r':>6} {'gat-r':>6} {'lin-r':>6} {'rate':>5}")
    for t,r,o,g,l,rt in rows:
        if abs(round(t*10)-t*10)<1e-6: print(f"{t:5.2f} {r:6.1f} {o:6.1f} {g:6.1f} {l:6.1f} | {o-r:6.1f} {g-r:6.1f} {l-r:6.1f} {rt:5.0f}")
    def stops(col):
        # frames where the knife is still (<10 deg/s) but the fist is still turning (>30 deg/s)
        c=0
        for (t1,*a),(t2,*b) in zip(rows,rows[1:]):
            if a[4]<10 and abs(b[col]-a[col])*FPS>30: c+=1
        return c
    print(f"\nknife-stopped-but-fist-still-turning frames: old={stops(1)} gated={stops(2)} lin={stops(3)}")
    ratio=[(l2-l1)/(r2-r1) for (t1,r1,_,_,l1,_),(t2,r2,_,_,l2,_) in zip(rows,rows[1:]) if abs(r2-r1)>0.5 and l1>math.degrees(FROM)+1 and l2>math.degrees(FROM)+1]
    if ratio: print(f"lin fist/knife rate ratio while squaring: min {min(ratio):.2f} max {max(ratio):.2f} (constant = in step)")
    hold_rows=[l for t,r,o,g,l,rt in rows if rt<10 and r>100]
    if hold_rows: print(f"at the hold, lin squares to {min(hold_rows):.1f}..{max(hold_rows):.1f} deg (target 180)")
