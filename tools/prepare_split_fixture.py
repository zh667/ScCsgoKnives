"""Isolated native fixture using the exact two candidate payloads."""
import io,shutil,subprocess,zipfile,json
from pathlib import Path
from PIL import Image
R=Path(__file__).resolve().parents[1];S=R/'.tmp/split-lite-130-20260926';F=S/'fixture'
def junction(path,target):
    path.parent.mkdir(parents=True,exist_ok=True)
    if path.exists():assert path.resolve()==target.resolve();return
    script=S/'junction.ps1';script.write_text('param([string]$Link,[string]$Target)\nNew-Item -ItemType Junction -Path $Link -Target $Target | Out-Null\n',encoding='utf8')
    subprocess.run(['pwsh','-NoProfile','-File',str(script),str(path),str(target)],check=True)
entries={}
for label in ['轻量','探员']:
    with zipfile.ZipFile(S/f'candidate/[API1.9]CS武器1.3.0-{label}包.scmod') as z:
        for n in z.namelist():
            b=z.read(n)
            if n in entries and entries[n]!=b:assert not n.startswith('Assets/'),n
            entries[n]=b
for n,b in entries.items():
    if not n.startswith('Assets/'):continue
    p=F/'src/ScCsgoKnives'/n;assert p.resolve().is_relative_to(F.resolve());p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
    if p.suffix=='.webp':
        image=Image.open(io.BytesIO(b));image.load();image.save(p.with_suffix('.png'),format='PNG')
for tool in ['NpcWeaponCheck','AppearanceCheck','ActorLoadCheck']:
    dest=S/'runtime'/tool;shutil.copytree(R/'.tmp/lite-smooth-20260926/runtime'/tool,dest,dirs_exist_ok=True)
    for n in ['ScCsgoResources.dll','ScCsgoKnives.dll','ScCsgoTactical.dll']:(dest/n).write_bytes(entries[n])
    if tool=='AppearanceCheck':(dest/'ScCsgoAppearance.dll').write_bytes(entries['Integrations/ScCsgoAppearance.bin'])
for project in ['ScCsgoTactical','ScCsgoAppearance']:junction(F/f'src/{project}/Assets',F/'src/ScCsgoKnives/Assets')
junction(F/'.tmp/nmm-player-appearance-audit-20260920',R/'.tmp/nmm-player-appearance-audit-20260920')
junction(F/'tools',R/'tools')
# Test-only union supplies native NPC cache comparisons, never a delivered bundle.
with zipfile.ZipFile(S/'fixture-union.scmod','w',zipfile.ZIP_STORED) as z:
    for n,b in entries.items():z.writestr(n,b)
for key,label in [('full','全量'),('lite','轻量'),('mini','极简'),('split','轻量')]:
    path=(S/'candidate' if key=='split' else R/'output')/f'[API1.9]CS武器1.3.0-{label}包.scmod'
    if key=='lite' and (S/'original-lite.scmod').exists():path=S/'original-lite.scmod'
    dest=S/'compat-baselines'/key;dest.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(path) as z:
        for n in ['ScCsgoKnives.dll','ScCsgoResources.dll','ScCsgoTactical.dll']:
            if n in z.namelist():(dest/n).write_bytes(z.read(n))
print('Exact union fixture and isolated runtimes prepared')
