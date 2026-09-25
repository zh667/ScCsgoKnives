"""CT long sleeves use the glove bodygroup only; preserve the original mesh and all vertex/bind bytes."""
from pathlib import Path
import struct,hashlib,json
ROOT=Path(__file__).resolve().parents[1];DATA=ROOT/'src/ScCsgoTactical/ArmData'
def read_parts(b):
    assert b[:8]==b'SCK2SKIN';o=12;n=struct.unpack_from('<H',b,o)[0];o+=2
    for _ in range(n):l=struct.unpack_from('<H',b,o)[0];o+=2+l+64
    v=struct.unpack_from('<I',b,o)[0];o+=4+v*52;prefix=b[:o];n=struct.unpack_from('<H',b,o)[0];o+=2;parts=[]
    for _ in range(n):
        start=o;l=struct.unpack_from('<H',b,o)[0];o+=2;name=b[o:o+l].decode();o+=l;count=struct.unpack_from('<I',b,o)[0];o+=4+count*4
        parts.append((name,count,b[start:o]))
    assert o==len(b);return prefix,parts
def main():
    report=[]
    for name in ['ct_default','glove_sporty','glove_specialist','glove_slick']:
        src=DATA/(name+'.skin');b=src.read_bytes();prefix,parts=read_parts(b);removed=[p for p in parts if p[0]=='bare_arm_133'];kept=[p for p in parts if p[0]!='bare_arm_133']
        assert len(removed)==1 and removed[0][1]//3==1216 and kept
        out=prefix+struct.pack('<H',len(kept))+b''.join(p[2] for p in kept);path=DATA/('ct_covered_'+name+'.skin');path.write_bytes(out)
        report.append(dict(source=src.name,source_sha256=hashlib.sha256(b).hexdigest(),derived=path.name,sha256=hashlib.sha256(out).hexdigest(),removed_triangles=1216,kept_materials=[p[0] for p in kept],vertex_and_bind_bytes_unchanged=True))
    dest=ROOT/'docs/ct-covered-arms-2026-09-25.json';dest.write_text(json.dumps(report,indent=2)+'\n','utf8');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
