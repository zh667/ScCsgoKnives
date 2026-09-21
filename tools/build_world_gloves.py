"""Convert CS2's actual lower-detail world gloves; original exports remain untouched."""
import json,hashlib
from pathlib import Path
from cs2_glb_to_skinned import convert
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons/08_first_person/glb/agents/models/shared/arms'
report={}
for kind in ['sporty','specialist','slick']:
    source=SOURCE/f'glove_{kind}/glove_{kind}.glb'
    data,joints,stats=convert(source,'worldmodel')
    dest=ROOT/f'src/ScCsgoTactical/ArmData/world_{kind}.skin'
    dest.write_bytes(data)
    report[kind]={'source':str(source),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'sha256':hashlib.sha256(data).hexdigest(),'joints':joints,'primitives':stats}
(ROOT/'docs/world-gloves-assets.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print('Converted 3 world glove meshes.')
