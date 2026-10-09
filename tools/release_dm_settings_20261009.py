"""Build task worktree sources using the established distinct Full/Lite release pipeline."""
import json,sys,time,traceback
from pathlib import Path

SOURCE=Path(__file__).resolve().parents[1]
ROOT=Path('E:/projects/ScCsgoKnives')
S=ROOT/'.tmp/dev-temp/dm-settings-release-20261009'
import deathmatch_140 as dm
cp=dm.cp;base=dm.base
base.REL=ROOT/'.tmp/release-140-20260928';base.BASELINE_DIR=ROOT/'output'
cp.ROOT=ROOT;cp.MP_REFS=ROOT/'.tmp/mp-m0-20260929/refs/mp'
dm.ROOT=ROOT;dm.SOURCE=SOURCE/'src/ScCsgoDeathmatch'
for attr in ['ASSETS_RECORD','PIXEL_RECORD','HUD_RECORD','FIXTURE']:
    setattr(dm,attr,ROOT/getattr(dm,attr).relative_to(SOURCE))
base.TACTICAL_ASSET_RECORDS=[ROOT/p.relative_to(SOURCE) for p in base.TACTICAL_ASSET_RECORDS]
cp.CORE_RECORDS=[ROOT/p.relative_to(SOURCE) for p in cp.CORE_RECORDS]

def main():
    for step in sys.argv[1].split(','):
        base.ROOT=SOURCE if step=='prepare' else ROOT
        begin=time.monotonic();print('START',step,flush=True);error=None
        try:ok=dm.STEPS[step](S) is not False
        except (Exception,SystemExit):ok=False;error=traceback.format_exc();print(error,flush=True)
        p=S/'execution.json';records=json.loads(p.read_text('utf8')) if p.exists() else []
        records.append(dict(step=step,passed=ok,seconds=round(time.monotonic()-begin,2),error=error));base.dump(p,records)
        print('END',step,ok,records[-1]['seconds'],flush=True)
        if not ok:return 1
    return 0
if __name__=='__main__':raise SystemExit(main())
