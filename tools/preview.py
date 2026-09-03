"""Draws a clip frame by frame from the shipped C# arm maths. tools/ArmPreview runs
the mod's own SolveArm/ResolveRoll headless (no Python replica of the renderer);
this only draws its output and prints the sync summary: frames where the knife
has stopped but the fist still turns, the fist/knife rate ratio while squaring,
and how far the hold squares.

    python3 tools/preview.py [knife=m9] [clip=inspect] [fps=30] [every=3]
"""
import sys, os, json, math, subprocess, numpy as np
HERE=os.path.dirname(os.path.abspath(__file__)); ROOT=os.path.dirname(HERE)
sys.path.insert(0, HERE)
from PIL import Image, ImageDraw
import verify_cs as V, gripsolve as G, fleet_qa as F

OVERRIDES=[a for a in sys.argv[1:] if '=' in a]   # key=value tunable overrides, passed to ArmPreview
sys.argv=[a for a in sys.argv if '=' not in a]

def run_tool(knife, clip, fps):
    env=dict(os.environ, DOTNET_ROLL_FORWARD='Major')
    proj=os.path.join(HERE,'ArmPreview','ArmPreview.csproj')
    dll=os.path.join(HERE,'ArmPreview','bin','Release','net10.0','ArmPreview.dll')
    src=os.path.join(ROOT,'src','ScCsgoKnives')
    newest=max(os.path.getmtime(os.path.join(d,f)) for d,_,fs in os.walk(src) for f in fs if f.endswith('.cs') and 'bin' not in d and 'obj' not in d)
    if not os.path.exists(dll) or os.path.getmtime(dll) < newest:
        subprocess.run(['dotnet','build',proj,'-c','Release','-nologo','-v','q'],check=True,env=env,cwd=ROOT)
    os.makedirs(os.path.join(ROOT,'.tmp-fist'),exist_ok=True)
    path=os.path.join(ROOT,'.tmp-fist',f'armpreview_{knife}_{clip}.json')
    out=subprocess.run(['dotnet',dll,knife,clip,str(fps),path]+OVERRIDES,capture_output=True,text=True,env=env,cwd=ROOT)
    if out.returncode!=0: print(out.stderr); raise SystemExit(out.returncode)
    return json.load(open(path))

def corners(s):
    seat=np.array(s['seat']); axis=np.array(s['axis']); side=np.array(s['side']); up=np.array(s['up'])
    w=s['width']; L=s['reach']+s['overshoot']
    return np.array([seat+axis*a+up*(su*w)+side*(ss*w) for a in (0.0,L) for su in (-0.5,0.5) for ss in (-0.5,0.5)])

def draw_frame(knife, fr, canvas):
    d=ImageDraw.Draw(canvas)
    if fr['right']: F.draw_box(d, corners(fr['right']), (255,255,0))
    if fr['left']: F.draw_box(d, corners(fr['left']), (255,160,0))
    for part,m in fr['parts'].items():
        Vm=G.mesh_part(knife,part)[::6]; M=np.array(m).reshape(4,4)
        for p in (np.c_[Vm,np.ones(len(Vm))]@M)[:,:3]:
            s=V.screen(p)
            if s is None: continue
            x,y=s[0]*F.W,s[1]*F.H
            if 0<=x<F.W and 0<=y<F.H: d.point((x,y),fill=(0,255,0))
    if fr['right']:
        g=V.screen(np.array(fr['right']['grip']))
        if g is not None: d.ellipse([g[0]*F.W-4,g[1]*F.H-4,g[0]*F.W+4,g[1]*F.H+4],outline=(255,0,255),width=2)

if __name__=='__main__':
    knife=sys.argv[1] if len(sys.argv)>1 else 'm9'; clip=sys.argv[2] if len(sys.argv)>2 else 'inspect'
    fps=int(sys.argv[3]) if len(sys.argv)>3 else 30; every=int(sys.argv[4]) if len(sys.argv)>4 else 3
    doc=run_tool(knife,clip,fps); frames=doc['frames']
    print(f"{knife} {clip}: {len(frames)} frames @ {fps} fps from the shipped C# (proj {doc['projX']:.4f}/{doc['projY']:.4f})")
    rows=[(f['t'], f['right']['rigidDeg'] if f['right'] else 0.0, f['right']['resolvedDeg'] if f['right'] else 0.0, f['right']['holdDeg'] if f['right'] else 0.0) for f in frames]
    hold=max((h for *_,h in rows), default=0.0)
    print(f"measured hold (C# MeasureHolds) = {hold:.1f} deg")
    print(f"{'t':>5} {'rigid':>7} {'fist':>7} {'fist-rigid':>10} {'rate':>6}")
    prev=None
    stopped_turning=0; ratios=[]
    for i,(t,r,f_,h) in enumerate(rows):
        rate=0.0 if prev is None else abs(r-prev[1])*fps
        if abs(round(t*10)-t*10)<1e-6: print(f"{t:5.2f} {r:7.1f} {f_:7.1f} {f_-r:10.1f} {rate:6.0f}")
        if prev is not None:
            if rate<10 and abs(f_-prev[2])*fps>30: stopped_turning+=1
            if abs(r-prev[1])>0.5 and abs(prev[1])>45+1 and abs(r)>45+1 and abs(f_-r)>0.5: ratios.append((abs(f_)-abs(prev[2]))/(abs(r)-abs(prev[1])))
        prev=(t,r,f_)
    print(f"\nknife-stopped-but-fist-still-turning frames: {stopped_turning}")
    if ratios: print(f"fist/knife rate ratio while squaring: min {min(ratios):.2f} max {max(ratios):.2f}")
    holds=[abs(f_) for (t,r,f_,h),(t2,r2,f2,h2) in zip(rows,rows[1:]) if abs(r2-r)*fps<10 and abs(r)>100]
    if holds: print(f"at the hold the fist squares to {min(holds):.1f}..{max(holds):.1f} deg (target 180)")
    sel=frames[::every]; tw,th=480,270; cols=8; rws=(len(sel)+cols-1)//cols
    sheet=Image.new('RGB',(cols*tw,rws*th),(120,160,240)); dr=ImageDraw.Draw(sheet)
    for i,fr in enumerate(sel):
        c=Image.new('RGB',(F.W,F.H),(120,160,240)); draw_frame(knife,fr,c)
        x,y=(i%cols)*tw,(i//cols)*th; sheet.paste(c.resize((tw,th)),(x,y))
        lab=f"t={fr['t']:.2f} rigid={fr['right']['rigidDeg']:.0f} fist={fr['right']['resolvedDeg']:.0f}" if fr['right'] else f"t={fr['t']:.2f}"
        dr.rectangle([x,y,x+230,y+13],fill=(0,0,0)); dr.text((x+2,y+1),lab,fill=(255,255,0))
    os.makedirs(os.path.join(ROOT,'.tmp-fist'),exist_ok=True)
    out=os.path.join(ROOT,'.tmp-fist',f'preview_{knife}_{clip}.png'); sheet.save(out); print('wrote',out)
