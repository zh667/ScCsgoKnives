"""Select supported knife/paint pairs from the installed CS2, with source hashes."""
import hashlib, json, re, subprocess, sys
from pathlib import Path
import audit_requested_cs2_finishes as audit
from install_knives import KNIVES

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons/knife_finishes_20260913'
CLI = Path('E:/CSMCReverse-Tools/ValveResourceFormat/CLI/bin/Release/Source2Viewer-CLI.exe')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    index=audit.vpk.read_vpk_index(); lower={p.lower():p for p in index}
    STAGE.mkdir(exist_ok=True)
    items=STAGE/'items_game.txt'
    audit.vpk.extract_entry('scripts/items/items_game.txt',index['scripts/items/items_game.txt'],items)
    text=items.read_text('utf-8-sig')
    sections=audit.kv(text)[0][1]
    paints={dict(v)['name']:dict(v,id=k) for section,values in sections if section=='paint_kits' for k,v in values}
    associations=set(re.findall(r'"\[([^\]]+)\](weapon_[a-z0-9_]+)"',text))
    paths=set(); rows=[]
    for variant,(_,asset,weapon) in enumerate(KNIVES):
        candidates=[]
        patterns = (r'am_ruby_marbleized$',) if asset == 'bayonet' else (
            r'am_gamma_doppler_phase[1-4]$', r'aa_fade$', r'am_slaughter$')
        for pattern in patterns:
            # Rare-special knives are not enumerated in the ordinary loot-list
            # [paint]weapon associations. Their generated econ renders provide
            # the explicit supported weapon/paint pairs in this VPK.
            candidates=sorted(key for key in paints if re.match(pattern,key) and
                f'panorama/images/econ/default_generated/{weapon}_{key}_light_png.vtex_c'.lower() in lower)
            if candidates: break
        if not candidates:
            if asset == 'bayonet': raise ValueError('Required official bayonet Ruby pair missing')
            rows.append(dict(variant=variant,asset=asset,weapon=weapon,finish=None,reason='No official Gamma/Fade/Slaughter pair'))
            print(asset,'NO OFFICIAL FINISH');continue
        key=candidates[(variant*17+3)%len(candidates)]; paint=paints[key]
        icon=f'panorama/images/econ/default_generated/{weapon}_{key}_light_png.vtex_c'
        if icon.lower() not in lower: raise ValueError('Missing official icon: '+icon)
        icon=lower[icon.lower()]
        recipes=[p for p in index if p.lower().endswith('/'+key.lower()+'.vcompmat_c')]
        vmats=[p for p in index if p.lower().endswith('/'+key.lower()+'.vmat_c')]
        if not recipes or not vmats: raise ValueError('Missing paint recipe: '+key)
        paths.update([icon,*recipes,*vmats])
        rows.append(dict(variant=variant,asset=asset,weapon=weapon,finish=key,paintId=int(paint['id']),fields=paint,
                         icon=icon,recipes=recipes,vmats=vmats))
        print(asset,key,paint['id'])
    # Resolve all compiled dependencies referenced by decoded recipes/materials.
    done=set(); records=[]
    for iteration in range(6):
        pending=paths-done
        if not pending: break
        for name in sorted(pending):
            dest=STAGE/'raw'/name
            audit.vpk.extract_entry(name,index[name],dest)
            records.append(dict(path=name,sha256=sha(dest),bytes=dest.stat().st_size))
        done.update(pending)
        with (STAGE/'decode.log').open('w',encoding='utf-8') as log:
            subprocess.run([str(CLI),'-i',str(STAGE/'raw'),'-o',str(STAGE/'decoded'),'--recursive','-d','--threads','2'],stdout=log,stderr=subprocess.STDOUT,check=True)
        for f in (STAGE/'decoded').rglob('*'):
            if f.suffix not in ('.vmat','.vcompmat'):continue
            for ref in re.findall(r'"([^"\n]+\.(?:vmat|vcompmat|vtex))"',f.read_text('utf-8')):
                hit=lower.get((ref+'_c').lower())
                if hit:paths.add(hit)
    if paths-done:raise ValueError('Unresolved dependencies')
    manifest=dict(sourceVpk=str(audit.vpk.VPK),vpkIndexSha256=sha(audit.vpk.VPK),itemsSha256=sha(items),
                  rows=rows,files=records,note='Icons are official light renders. Paint composition for this engine is a separate port, not Valve pixel-exact rendering.')
    (STAGE/'source-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n','utf-8')
    print('Staged',len(records),'files to',STAGE)

if __name__=='__main__':main()
