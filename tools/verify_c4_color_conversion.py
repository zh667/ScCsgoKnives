"""Test opaque C4 RGB against the original, including texels with zero material alpha."""
import io
import json
from pathlib import Path
import zipfile
import numpy as np
from PIL import Image, ImageDraw
from optimize_weapon_resources import texture, rows

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/release-1.1.0/c4-fix'
NAME='Assets/Textures/ScCsgoKnives/c4_cs2.png'


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    source=(ROOT/'src/ScCsgoKnives'/NAME).read_bytes()
    original=Image.open(io.BytesIO(source))
    expected=original.convert('RGB').resize((512,512),Image.Resampling.LANCZOS)
    result=texture(NAME,source)
    actual=Image.open(io.BytesIO(result)).convert('RGBA')
    delta=np.abs(np.asarray(expected,dtype=float)-np.asarray(actual,dtype=float)[:,:,:3])
    hidden=np.asarray(original.getchannel('A').resize((512,512),Image.Resampling.NEAREST))==0
    mean=float(delta.mean());hidden_mean=float(delta[hidden].mean())
    assert mean<10 and hidden_mean<10,(mean,hidden_mean)
    assert actual.getchannel('A').getextrema()==(255,255)
    # The old RGBA path destroys hidden RGB even before encoding. This input
    # must detect that failure, not just confirm that WebP decoding succeeds.
    old=original.resize((512,512),Image.Resampling.LANCZOS)
    b=io.BytesIO();old.save(b,format='WEBP',quality=85,method=4,exact=True)
    old=Image.open(io.BytesIO(b.getvalue())).convert('RGB')
    old_error=float(np.abs(np.asarray(expected,dtype=float)-np.asarray(old,dtype=float)).mean())
    assert old_error>mean*5,(old_error,mean)
    # Real transparency remains supported for inventory/effect artwork.
    icon=(ROOT/'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/c4_slot.png').read_bytes()
    icon_before=Image.open(io.BytesIO(icon)).convert('RGBA')
    icon_after=Image.open(io.BytesIO(texture('Assets/Textures/ScCsgoKnives/c4_slot.png',icon))).convert('RGBA')
    assert np.array_equal(np.asarray(icon_before)[:,:,3],np.asarray(icon_after)[:,:,3])
    canvas=Image.new('RGB',(1536,542),(40,40,40));draw=ImageDraw.Draw(canvas)
    for i,(label,im) in enumerate([('Reference: RGB resize',expected),('Before: broken RGBA conversion',old),('Fixed: opaque RGB WebP',actual.convert('RGB'))]):
        canvas.paste(im,(i*512,30));draw.text((i*512+8,8),label,fill='white')
    canvas.save(OUT/'c4-color-comparison.jpg',quality=94)
    (OUT/'c4-color-regression.json').write_text(json.dumps(dict(
        rgbMeanAbsoluteError=mean,originalZeroAlphaRgbError=hidden_mean,
        oldConversionError=old_error,outputOpaque=True,inventoryAlphaPreserved=True),indent=2)+'\n','utf-8')
    print('C4 RGB error:',mean,'zero-alpha source RGB error:',hidden_mean,'old:',old_error)


if __name__=='__main__':main()
