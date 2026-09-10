"""Stage the 33 requested source finishes without installing unverified artwork into the mod."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import audit_requested_cs2_finishes as audit

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    parser=argparse.ArgumentParser()
    parser.add_argument('--audit',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--cli',type=Path)
    args=parser.parse_args()
    report=json.loads(args.audit.read_text('utf-8-sig'))
    assert len(report['rows'])==33 and all(len(row['matches'])==1 for row in report['rows'])
    assert report['vpkIndexSha256']==sha(audit.vpk.VPK),'VPK changed; rerun audit'
    assert next(r for r in report['rows'] if r['weapon']=='glock')['matches'][0]['id']=='1119'
    index=audit.vpk.read_vpk_index();lower={p.lower():p for p in index}
    paths=set();planned=[]
    for row in report['rows']:
        paint=row['matches'][0]
        for p in paint['recipes']+paint['materials']+paint['icons']:paths.add(p)
        for dependency in paint['dependencies']:paths.add(lower[(dependency['path']+'_c').lower()])
        planned.append({'gun':row['weapon'],'name':row['requested'],'paintId':int(paint['id']),
            'key':paint['key'],'wear':paint['wearMin'],'seed':0,'wearName':'久经沙场' if paint['wearMin']>=.15 else '崭新出厂',
            'body':'legacy' if paint['fields'].get('use_legacy_model')=='1' else 'hd',
            'model':row['model'],'sourceGlb':row['localGlb'],'recipes':paint['recipes'],
            'state':'source-only; final wear composition and matching inventory icon NOT verified'})
    root=args.output.resolve()
    if root.exists():raise SystemExit('Refusing to overwrite an existing staging directory: '+str(root))
    root.mkdir(parents=True)
    files=[]
    for name in sorted(paths):
        if '..' in Path(name).parts or Path(name).is_absolute():raise ValueError(name)
        dest=root/'raw'/name
        audit.vpk.extract_entry(name,index[name],dest)
        files.append({'path':name,'bytes':dest.stat().st_size,'sha256':sha(dest)})
    manifest={'sourceVpk':str(audit.vpk.VPK),'vpkIndexSha256':report['vpkIndexSha256'],
        'note':'Official source assets, NOT a playable release or a claim of completed Factory New rendering.',
        'finishes':planned,'files':files}
    (root/'source-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'Staged {len(planned)} finishes / {len(files)} original resources, {sum(f["bytes"] for f in files)/1e6:.2f} MB: {root}',flush=True)
    if args.cli:
        with (root/'decode.log').open('w',encoding='utf-8') as log:
            subprocess.run([str(args.cli),'-i',str(root/'raw'),'-o',str(root/'decoded'),'--recursive','-d','--threads','2'],stdout=log,stderr=subprocess.STDOUT,check=True)
        print('Decoding completed; see decode.log. Source hashes are recorded independently of decoded outputs.')

if __name__=='__main__':main()
