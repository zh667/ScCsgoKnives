"""Author the shared supply/radio atlas only; no CS2 imports or external source inputs.

Cell numbers are the material constants in ScSupplyGeometry.cs. All marks are drawn
at fixed coordinates with Pillow primitives (no system-font or random dependency).
"""
from pathlib import Path
import argparse, hashlib, json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/survival_surface.png'
COLORS = [(134,148,156),(53,64,71),(180,139,69),(153,49,37),(42,49,50),(28,84,111),
          (39,76,70),(60,82,85),(182,195,200),(83,94,98),(29,37,40),(53,104,98),
          (40,110,153),(159,84,48),(127,44,36),(127,44,36),(209,200,168),(40,111,124),
          (141,101,58),(184,178,146),(187,148,64),(47,55,55),(75,87,93),(23,30,33),(72,83,84)]

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--out', type=Path, default=DEST)
    ap.add_argument('--report',type=Path);args=ap.parse_args()
    atlas=Image.new('RGBA',(512,256),(49,61,66,255))
    for i,base in enumerate(COLORS):
        im=Image.new('RGB',(64,64),base);d=ImageDraw.Draw(im)
        # Very small tonal changes, broad enough to avoid noisy inventory silhouettes.
        if i in (0,1,2,3,7,8,9,18,22):
            for y in range(3,61):
                delta=(2 if y%4==0 else 0)+int(5*(1-y/64))
                d.line((3,y,60,y),fill=tuple(min(255,c+delta) for c in base))
            d.line((5,5,58,5),fill=tuple(min(255,c+20) for c in base),width=2)
            d.line((5,59,58,59),fill=tuple(max(0,c-15) for c in base),width=2)
        if i==4:
            for y in range(8,58,10):d.line((5,y,59,y),fill=(33,41,42),width=2)
        if i==5:
            for r in range(29,0,-1):
                d.ellipse((32-r,32-r,32+r,32+r),fill=(19+int(r*.55),50+int(r*2),72+int(r*2.3)))
            d.arc((10,8,51,51),195,280,fill=(128,205,210),width=3)
            d.arc((16,14,47,47),200,265,fill=(70,156,172),width=2)
        if i==6:
            for x in range(5,60,9):
                d.line((x,4,x,59),fill=(54,92,84));d.line((4,x,59,x),fill=(54,92,84))
            d.rectangle((8,8,55,55),outline=(78,113,99),width=2)
        if i==9:
            d.ellipse((12,12,51,51),fill=(126,139,142),outline=(32,43,48),width=3)
            d.line((21,43,43,21),fill=(35,47,51),width=5)
        if i==10:
            for y in range(9,58,8):
                d.rounded_rectangle((6,y,58,y+4),radius=2,fill=(11,20,24))
                d.line((9,y+5,55,y+5),fill=(82,96,98))
        if i==11:
            d.rectangle((5,5,58,58),fill=(72,121,105),outline=(113,162,131),width=2)
            for j in range(4):d.rectangle((10+j*7,21-j*4,14+j*7,28),fill=(23,52,46))
            d.rectangle((42,12,52,21),outline=(23,52,46),width=2)
            d.rectangle((10,39,51,43),fill=(23,52,46));d.rectangle((10,48,37,51),fill=(23,52,46))
        if i in (12,13,14,15,24):
            d.rectangle((5,5,58,58),outline=tuple(min(255,c+48) for c in base),width=2)
            ink=(235,224,187)
            if i==12: # CT shield and cross, legible without tiny lettering.
                d.polygon([(23,10),(41,10),(40,37),(32,51),(24,37)],fill=ink)
                d.rectangle((30,18,34,40),fill=base);d.rectangle((27,25,37,31),fill=base)
            elif i==13:
                d.polygon([(32,10),(44,49),(20,49)],fill=ink)
                d.rectangle((30,24,34,38),fill=base);d.rectangle((30,42,34,46),fill=base)
            elif i in (14,15):
                # Hand-authored seven-segment 3/5. Only upper/lower-left differ.
                for box in [(25,8,39,14),(25,24,39,30),(25,40,39,46),(35,28,39,43)]:d.rectangle(box,fill=ink)
                d.rectangle((35,12,39,27) if i==14 else (25,12,29,27),fill=ink)
                n=3 if i==14 else 5
                for k in range(n):d.rectangle((12+k*8,51,16+k*8,55),fill=ink)
            else:
                d.line((17,17,47,47),fill=ink,width=5);d.line((47,17,17,47),fill=ink,width=5)
        if i==16:
            d.rectangle((3,4,60,16),fill=(34,96,110));d.rectangle((3,48,60,60),fill=(34,96,110))
            d.polygon([(31,20),(27,35),(28,42),(31,46),(34,42),(35,35)],fill=(44,110,121))
            d.line((8,23,15,23),fill=(69,75,69),width=2);d.line((48,39,56,39),fill=(69,75,69),width=2)
        if i==17:
            d.arc((8,8,56,56),180,300,fill=(84,159,164),width=3)
        if i==18:
            for x in (12,24,43,52):d.line((x,6,x-3,58),fill=(110,79,48),width=2)
        if i==19:
            for x in range(5,60,5):d.line((x,3,x,60),fill=(108,108,91),width=2)
            d.rectangle((3,48,60,60),fill=(37,110,121))
        if i==20:
            for x in range(-50,100,23):d.polygon([(x,3),(x+10,3),(x+68,61),(x+58,61)],fill=(49,58,58))
        if i==21:
            for y in range(8,60,8):
                for x in range(8,60,8):d.line((x-2,y+2,x+2,y-2),fill=(73,82,79),width=2)
        if i==22:
            for x in range(9,60,10):
                d.line((x,5,x,59),fill=(32,44,51),width=3);d.line((x+3,5,x+3,59),fill=(113,125,127))
        # Three pixels of edge extrusion; runtime UV stays inside this padded cell.
        core=im.crop((3,3,61,61))
        im.paste(core,(3,3));im.paste(core.crop((0,0,1,58)).resize((3,58)),(0,3))
        im.paste(core.crop((57,0,58,58)).resize((3,58)),(61,3))
        im.paste(im.crop((0,3,64,4)).resize((64,3)),(0,0));im.paste(im.crop((0,60,64,61)).resize((64,3)),(0,61))
        atlas.paste(im,((i%8)*64,(i//8)*64))
    args.out.parent.mkdir(parents=True,exist_ok=True);atlas.save(args.out)
    if args.report:
        args.report.parent.mkdir(parents=True,exist_ok=True)
        args.report.write_text(json.dumps({'source':'authored Pillow primitives; no imported art','size':list(atlas.size),'cells':len(COLORS),'sha256':hashlib.sha256(args.out.read_bytes()).hexdigest()},indent=2)+'\n',encoding='utf-8')
    print(f'Authored {len(COLORS)} padded material/marking cells: {args.out}')

if __name__=='__main__':main()
