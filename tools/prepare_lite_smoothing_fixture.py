"""Prepare isolated native Lite test runtimes; never replace source Full resources.

Run after package_lite_smoothing.py. WebP copies are decoded to PNG for existing
PNG-only test fixtures; decoding preserves exactly the Lite texture pixels.
"""
import io
import shutil
import subprocess
import zipfile
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/lite-smooth-20260926'
FIXTURE=STAGE/'fixture'

def junction(path,target):
    path.parent.mkdir(parents=True,exist_ok=True)
    if path.exists():
        assert path.resolve()==target.resolve(),path
        return
    # Static PowerShell program, paths are separate arguments, never shell interpolation.
    script=STAGE/'junction.ps1'
    script.write_text('param([string]$Link,[string]$Target)\nNew-Item -ItemType Junction -Path $Link -Target $Target | Out-Null\n',encoding='utf-8')
    subprocess.run(['pwsh','-NoProfile','-File',str(script),str(path),str(target)],check=True)

with zipfile.ZipFile(STAGE/'candidate/[API1.9]CS武器1.3.0-轻量包.scmod') as z:
    for info in z.infolist():
        if not info.filename.startswith('Assets/'): continue
        dst=FIXTURE/'src/ScCsgoKnives'/info.filename
        assert dst.resolve().is_relative_to(FIXTURE.resolve())
        dst.parent.mkdir(parents=True,exist_ok=True)
        data=z.read(info);dst.write_bytes(data)
        if dst.suffix=='.webp':
            image=Image.open(io.BytesIO(data));image.load();image.save(dst.with_suffix('.png'),format='PNG')
            with Image.open(dst.with_suffix('.png')) as decoded:
                assert decoded.convert('RGBA').tobytes()==image.convert('RGBA').tobytes()
    for tool in ['ActorSamplingCheck','NpcWeaponCheck','AppearanceCheck','TacticalRenderCheck','ActorLoadCheck']:
        dest=STAGE/'runtime'/tool
        shutil.copytree(ROOT/f'tools/{tool}/bin/Release/net10.0',dest,dirs_exist_ok=True)
        for name in ['ScCsgoResources.dll','ScCsgoKnives.dll','ScCsgoTactical.dll']:
            (dest/name).write_bytes(z.read(name))
        if tool=='AppearanceCheck': (dest/'ScCsgoAppearance.dll').write_bytes(z.read('Integrations/ScCsgoAppearance.bin'))

for name in ['ScCsgoTactical','ScCsgoAppearance']:
    junction(FIXTURE/f'src/{name}/Assets',FIXTURE/'src/ScCsgoKnives/Assets')
junction(FIXTURE/'.tmp/nmm-player-appearance-audit-20260920',ROOT/'.tmp/nmm-player-appearance-audit-20260920')
junction(FIXTURE/'tools',ROOT/'tools')
print('Prepared isolated Lite fixture and runtimes.')
