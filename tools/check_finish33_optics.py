"""Texture/geometry regressions for the user-reported scope failures; no GPU required."""
import json
from pathlib import Path
import numpy as np
from PIL import Image
from build_finish33 import ROOT,TEX,SOURCE,material_maps,Glb

sources=json.loads((SOURCE/'source-manifest.json').read_text('utf-8'))['finishes']
s=next(s for s in sources if s['gun']=='g3sg1');g=Glb(s['sourceGlb'])
base,_,_=material_maps(g,'weapon_snip_g3sg1',1024)
paint=np.asarray(Image.open(TEX/'g3sg1_hd__so_green.png').convert('RGB'))/255
results=[]
for name,(u,v) in {'rear-lens':(.744,.078),'front-lens':(.872,.152)}.items():
    y,x=int(v*1024),int(u*1024)
    error=float(np.max(abs(paint[y-4:y+5,x-4:x+5]-base[y-4:y+5,x-4:x+5])))
    results.append({'name':'g3sg1-'+name,'ok':error<.005,'maxColorDifference':error})
native=json.loads((ROOT/'src/ScCsgoKnives/AnimationData/gun_native_meshes.json').read_text('utf-8'))
for asset in ('awp','ssg08','scar20','aug'):
    results.append({'name':asset+'-scope-material','ok':any(p['Material']=='cs2_legacy_scope' for p in native[asset])})
for asset in ('aug','sg556'):
    results.append({'name':asset+'-lens-material','ok':any(p['Material']=='cs2_scope_lens' for p in native[asset])})
print(json.dumps(results,indent=2))
assert all(r['ok'] for r in results), 'optical material regression'
