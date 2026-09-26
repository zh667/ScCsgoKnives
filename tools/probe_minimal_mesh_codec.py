"""Offline meshoptimizer codec experiment: no simplification, no shipping decoder changes."""
from pathlib import Path
import sys,struct,json,zlib,hashlib,brotli,numpy as np
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root/'.tmp/optimization-deps'))
import meshoptimizer as mo
stage=root/'.tmp/minimal-inspect40-130-20260926/resources/AnimationData';out=root/'.tmp/minimal-mesh-codec';out.mkdir(exist_ok=True)
rows=[]
for p in sorted(stage.iterdir()):
    if p.suffix not in ['.parts','.skin']:continue
    raw=p.read_bytes();offset=12;spans=[];skin=p.suffix=='.skin'
    def number(fmt):
        global offset
        n=struct.unpack_from('<'+fmt,raw,offset)[0];offset+=struct.calcsize('<'+fmt);return n
    def string():
        global offset
        n=number('H');offset+=n
    for _ in range(number('H')):string();offset+=64
    def vertices(stride):
        global offset
        count=number('i');spans.append((offset,count,stride,'vertex'));offset+=count*stride
    def indices():
        global offset
        count=number('i');spans.append((offset,count,4,'index'));offset+=count*4
    vertices(52 if skin else 32)
    for _ in range(number('H')):
        if not skin:offset+=2
        string();indices()
    if not skin:
        vertices(52)
        for _ in range(number('H')):string();indices()
    assert offset==len(raw)
    encoded=bytearray(b'EXPMOPT0');last=0
    for start,count,stride,kind in spans:
        metadata=raw[last:start];source=raw[start:start+count*stride]
        encoded+=struct.pack('<i',len(metadata))+metadata+struct.pack('<ii',count,stride)
        if count:
            if kind=='vertex':
                packed=mo.encode_vertex_buffer(np.frombuffer(source,dtype=np.uint8).reshape(count,stride),count,stride)
                decoded=mo.decode_vertex_buffer(count,stride,packed).tobytes()
            else:
                packed=mo.encode_index_sequence(np.frombuffer(source,dtype='<u4'))
                decoded=mo.decode_index_sequence(count,4,packed).tobytes()
            assert decoded==source,(p.name,kind)
        else:packed=b''
        encoded+=struct.pack('<i',len(packed))+packed;last=start+count*stride
    encoded+=raw[last:];compressed=brotli.compress(bytes(encoded),quality=9)
    (out/(p.name+'.experiment.br')).write_bytes(compressed)
    rows.append(dict(name=p.name,sourceSha256=hashlib.sha256(raw).hexdigest(),raw=len(raw),zlib9=len(zlib.compress(raw,9)),meshoptBrotli9=len(compressed),decodedBuffersExact=True))
report=dict(rows=rows,totals={key:sum(r[key] for r in rows) for key in ['raw','zlib9','meshoptBrotli9']},scope='Offline codec estimate, not package savings or a loadable resource format. Sequence codec preserves exact index order. No quantization or simplification. Native decoder integration/Android testing not performed.')
(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps(report['totals'],indent=2))
