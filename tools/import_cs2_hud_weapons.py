"""Import original CS2 equipment silhouettes without changing weapon IDs."""
from pathlib import Path
import re,json,hashlib,io
import resvg_py
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parent.parent
SOURCE=ROOT/'.tmp/cs2-hud-weapons-20260919/panorama/images/icons/equipment'
ASSETS=ROOT/'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
code=(ROOT/'src/ScCsgoKnives/Rendering/GunSpec.cs').read_text('utf-8')
names=re.findall(r'"([a-z0-9_]+)"',re.search(r'FrozenOrder = \[(.*?)\]',code).group(1))
mapping={'m4a1s':'m4a1_silencer','m4a4':'m4a1','glock18':'glock'}
rows=[]
for name in names:
    source=SOURCE/(mapping.get(name,name)+'.svg')
    # Fit each SVG to a shared canvas without distorting the original aspect ratio.
    raster=Image.open(io.BytesIO(resvg_py.svg_to_bytes(svg_path=str(source),width=448,skip_system_fonts=True))).convert('RGBA')
    box=raster.getbbox()
    if box:raster=raster.crop(box)
    raster.thumbnail((432,128),Image.Resampling.LANCZOS)
    target=Image.new('RGBA',(448,144));target.alpha_composite(raster,((448-raster.width)//2,(144-raster.height)//2))
    path=ASSETS/f'hud_weapon_{name}.png';target.save(path)
    rows.append({'gun':name,'source':'panorama/images/icons/equipment/'+source.name,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'target':path.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
(ROOT/'docs/hud-weapon-sources-20260919.json').write_text(json.dumps({'vpk':'E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo/pak01_dir.vpk','rows':rows},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Imported',len(rows),'original CS2 weapon silhouettes')
