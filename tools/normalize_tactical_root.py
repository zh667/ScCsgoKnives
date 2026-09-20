"""Upgrade the existing derived hostage GLB without rebaking its preserved skin/clips."""
import json,struct,hashlib
from pathlib import Path
root=Path(__file__).resolve().parents[1]
path=root/'src/ScCsgoTactical/Assets/Models/ScCsgoTactical/hostage.glb'
b=path.read_bytes();length=struct.unpack_from('<I',b,12)[0];doc=json.loads(b[20:20+length]);tail=b[20+length:]
roots=doc['scenes'][0]['nodes']
if len(roots)==1 and doc['nodes'][roots[0]].get('name')=='hostage_axis':
    doc['nodes'].append({'name':'Root','children':roots});doc['scenes'][0]['nodes']=[len(doc['nodes'])-1]
    j=json.dumps(doc,separators=(',',':')).encode();j+=b' '*(-len(j)%4)
    path.write_bytes(struct.pack('<4sII',b'glTF',2,20+len(j)+len(tail))+struct.pack('<II',len(j),0x4e4f534a)+j+tail)
report=root/'docs/tactical-derived-assets.json';rows=json.loads(report.read_text('utf8'))
for row in rows:
    if row['name']=='hostage':row.update(bytes=path.stat().st_size,nodes=len(doc['nodes']),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),rootFix='Identity entity root above hostage_axis; original source export unchanged')
report.write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n','utf8')
print(rows[-1]['sha256'])
