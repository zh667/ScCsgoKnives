"""Independent binary integrity audit for derived meshes; no GPU or gameplay claims."""
from pathlib import Path
import struct,hashlib,json,argparse
ROOT=Path(__file__).resolve().parents[1]
def read(path):
    b=path.read_bytes();o=12;skin=b[:8]==b'SCK2SKIN'
    def n(f):
        nonlocal o
        v=struct.unpack_from('<'+f,b,o)[0];o+=struct.calcsize('<'+f);return v
    def s():
        nonlocal o
        size=n('H');v=b[o:o+size];o+=size;return v
    for _ in range(n('H')):s();o+=64
    header=b[:o]
    def section(skinned,rigid):
        nonlocal o
        count=n('i');stride=52 if skinned else 32;records=[b[o+i*stride:o+(i+1)*stride] for i in range(count)];o+=count*stride
        groups=[]
        for _ in range(n('H')):
            joint=n('H') if rigid else None;material=s();count=n('i');indices=struct.unpack_from('<'+'I'*count,b,o);o+=count*4
            assert count%3==0 and all(i<len(records) for i in indices)
            groups.append((joint,material,indices))
        return records,groups
    sections=[section(skin,not skin)]
    if not skin:sections.append(section(True,False))
    assert o==len(b)
    return header,sections
parser=argparse.ArgumentParser()
parser.add_argument('--stage',type=Path,default=ROOT/'.tmp/optimized-resources')
parser.add_argument('--report',type=Path,default=ROOT/'output/optimization-106/model-integrity.json')
args=parser.parse_args()
checks=[]
for source in sorted((ROOT/'src/ScCsgoKnives/AnimationData').iterdir()):
    target=args.stage/'AnimationData'/source.name
    if source.name.endswith('.cs2.animation.json'):
        assert source.read_bytes()==target.read_bytes();checks.append(dict(name=source.name,kind='animation',exact=True));continue
    if source.suffix not in ('.skin','.parts'):continue
    h,a=read(source);j,b=read(target);assert h==j and len(a)==len(b)
    for (old,og),(new,ng) in zip(a,b):
        assert len(og)==len(ng)
        for (oj,om,oi),(nj,nm,ni) in zip(og,ng):
            assert (oj,om)==(nj,nm) and len(ni)<=len(oi)
            allowed={old[i] for i in oi};assert all(new[i] in allowed for i in ni)
            # Every retained vertex has its exact original UV, normal, position, joint indices and weights.
    checks.append(dict(name=source.name,kind='mesh',headersExact=True,vertexAttributesExact=True,materialsAndBonesExact=True))
report=dict(checks=checks,count=len(checks),failed=0)
args.report.parent.mkdir(parents=True,exist_ok=True)
args.report.write_text(json.dumps(report,indent=2))
print('Validated',len(checks),'animations/meshes; zero changed surviving vertex attributes, bone headers or material identities')
