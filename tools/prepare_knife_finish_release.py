"""Version the corrected release and retain the rejected preview assets for audit."""
from pathlib import Path
import json
R=Path(__file__).resolve().parents[1]
for rel in ('src/ScCsgoKnives/modinfo.json','src/ScCsgoResources/modinfo.json'):
    p=R/rel;meta=json.loads(p.read_text('utf-8-sig'))
    if 'Knives/' in rel:
        meta['Version']='1.0.14-preview';meta['Dependencies']['zh667.ScCsgoResources']='[1.10.2]'
        meta['Description']=meta['Description'].replace('资源包1.4.0','资源包1.10.2').replace('资源包1.10.0','资源包1.10.2')
    else:meta['Version']='1.10.2'
    p.write_text(json.dumps(meta,ensure_ascii=False,indent=2)+'\n','utf-8')
p=R/'src/ScCsgoResources/ScCsgoResources.csproj'
s=p.read_text('utf-8-sig').replace('<Version>1.8.0</Version>','<Version>1.10.2</Version>').replace('<Version>1.10.0</Version>','<Version>1.10.2</Version>').replace('1.8.0+knife-finishes','1.10.2+cs2-knife-finish-sources').replace('1.10.0+cs2-knife-finish-sources','1.10.2+cs2-knife-finish-sources')
p.write_text(s,'utf-8')
p=R/'src/ScCsgoKnives/World/ScRequiredResources.cs';s=p.read_text('utf-8-sig')
s=s.replace('Version="1.8.0"','Version="1.10.2"').replace('Version="1.10.0"','Version="1.10.2"').replace('全量版 1.8.0 或优化版 1.9.0','全量版 1.10.2').replace('全量版 1.10.0','全量版 1.10.2')
s=s.replace('全量版 1.8.0 或优化版 1.9.0','全量版 1.10.2').replace('全量版 1.10.0','全量版 1.10.2')
p.write_text(s,'utf-8')
p=R/'tools/PackageCheck/ResourcePackInput.cs';s=p.read_text('utf-8-sig').replace('and not ("1.10.0","Full")','and not ("1.10.2","Full")')
p.write_text(s,'utf-8')
