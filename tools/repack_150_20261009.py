"""Rebuild the accepted 1.5.0 family in one task-local stage; no automatic delivery/deletion."""
import hashlib
import importlib.util
import json
import subprocess
import sys
import time
import traceback
from pathlib import Path

ROOT=Path('E:/projects/ScCsgoKnives')
STAGE=ROOT/'.tmp/dev-temp/repack-150-20261009'
MAIN='d3a9568a90c6d9e856e5ca1b653c17a6766625e9'
sys.path.insert(0,str(ROOT/'tools'))
import deathmatch_140 as dm
cp=dm.cp
base=dm.base
spec=importlib.util.spec_from_file_location('repack_completion',Path(__file__).with_name('completion_140.py'))
fixed=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixed);fixed.ROOT=ROOT
# Pin this run's original inputs even after the reusable pipeline advances to the new delivery.
base.BASELINES={'全量':'fb513611e132017c6dc8c94cc42182067c829bc8d968caf0ac3374a91f55b3e4',
    '轻量':'dff871b1b027c4b0e12899958478b215dbeccaaefbe0093a676883c9cf85b007',
    '探员':'20fa0a05bfa5d68dfe1375811e4d57d11627dd46b8563ce58d835a60e9d38482'}
dm.STEPS['compat']=fixed.compat

def main():
    steps=sys.argv[1].split(',')
    if 'prepare' in steps:
        subprocess.run(['git','diff','--exit-code',MAIN,'--','src','tools','Directory.Build.targets','nuget.config'],cwd=ROOT,check=True)
    for step in steps:
        started=time.time()
        print('START',step,flush=True)
        error=None
        try:ok=dm.STEPS[step](STAGE) is not False
        except Exception:
            ok=False;error=traceback.format_exc();print(error,flush=True)
        report=STAGE/'execution.json'
        entries=json.loads(report.read_text('utf8')) if report.exists() else []
        entries.append(dict(step=step,passed=ok,seconds=round(time.time()-started,2),sourceCommit=MAIN,error=error))
        base.dump(report,entries)
        print('END',step,ok,entries[-1]['seconds'],flush=True)
        if not ok:return 1
        if step=='prepare':
            base.dump(STAGE/'source-provenance.json',dict(commit=MAIN,codeDiffFromMain='',scriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                sourceWorktree=str(ROOT),recordWorktree=str(Path(__file__).resolve().parents[1]),resourcePolicy='Preserve baseline resources; no clips/mesh/texture rebuild'))
    return 0

if __name__=='__main__':raise SystemExit(main())
