"""Export approved planning values to the runtime catalog; source audit remains intact."""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
audit=json.loads((ROOT/'docs/gun-handling-35-source-audit-2026-09-08.json').read_text('utf-8'))
guns={}
for g in audit['guns']:
    p=g['proposal']
    guns[g['name']]={'Range':p['maxRange'],'FalloffStart':p['falloffStart'],'FalloffFloor':p['endpointMultiplier'],
                     'MidDistance':p['shotgunMidDistance'],'MidMultiplier':p['shotgunMidMultiplier'],
                     'Alternate':p['supportedAlternate'], 'Modes':p['modes']}
assert len(guns)==35
(ROOT/'src/ScCsgoKnives/AnimationData/gun_handling.json').write_text(json.dumps({'Version':1,'Guns':guns},ensure_ascii=False,indent=1)+'\n','utf-8')
print('35 approved gun handling records exported; no stored gun state changed')
