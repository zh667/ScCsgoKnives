"""Package tactical gameplay plus the lazily loaded player appearance integration."""
import hashlib,json,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'src/ScCsgoTactical'
BUILD=SOURCE/'bin/Release/net10.0'
meta=json.loads((SOURCE/'modinfo.json').read_text('utf8'))
dll=BUILD/'ScCsgoTactical.dll'
sources=list(SOURCE.glob('*.cs'))+[SOURCE/'ScCsgoTactical.csproj']
assert dll.stat().st_mtime>=max(p.stat().st_mtime for p in sources),'Rebuild DLC first'
assert (BUILD/'modinfo.json').read_bytes()==(SOURCE/'modinfo.json').read_bytes(),'Stale metadata'
entries={'ScCsgoTactical.dll':dll.read_bytes(),'modinfo.json':(SOURCE/'modinfo.json').read_bytes(),'LICENSE':(ROOT/'LICENSE').read_bytes()}
entries['ASSET_SOURCES.md']=(SOURCE/'ASSET_SOURCES.md').read_bytes()
for p in sorted((SOURCE/'Assets').rglob('*')):
    if p.is_file():entries[p.relative_to(SOURCE).as_posix()]=p.read_bytes()
appearance=ROOT/'src/ScCsgoAppearance'
bridge=appearance/'bin/Release/net10.0/ScCsgoAppearance.dll'
bridge_sources=list(appearance.glob('*.cs'))+[appearance/'ScCsgoAppearance.csproj',dll]
assert bridge.stat().st_mtime>=max(p.stat().st_mtime for p in bridge_sources),'Rebuild appearance integration after tactical'
# A .dll outside Assets is eagerly scanned by SCAPI, even without NMM/Neorxna.
entries['Integrations/ScCsgoAppearance.bin']=bridge.read_bytes()
entries['Integrations/ASSET_SOURCES.md']=(appearance/'ASSET_SOURCES.md').read_bytes()
for p in sorted((appearance/'Assets').rglob('*')):
    if p.is_file():
        name=p.relative_to(appearance).as_posix()
        assert name not in entries,'Duplicate resource '+name
        entries[name]=p.read_bytes()
path=ROOT/f'output/[API1.9]CS战术同伴拓展{meta["Version"]}-作者ZH667.scmod'
with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for name,data in entries.items():z.writestr(name,data)
report={'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size,'entries':{n:hashlib.sha256(b).hexdigest() for n,b in entries.items()}}
report_dir=ROOT/f'output/tactical-{meta["Version"]}'
report_dir.mkdir(exist_ok=True)
(report_dir/'tactical-package.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({k:v for k,v in report.items() if k!='entries'},ensure_ascii=False))
