"""Shorten OBJ decimals without changing any decoded float32 or face indices."""
from pathlib import Path
import json,hashlib,zlib
import numpy as np
R=Path(__file__).resolve().parents[1];S=R/'.tmp/split-lite-130-20260926'
record=json.loads((S/'asset-derivation.json').read_bytes());rows=[]
for name,row in record['entries'].items():
    if not name.endswith('.obj'):continue
    p=S/row['owner']/'assets'/row['target'];raw=p.read_bytes();lines=[]
    for line in raw.decode('utf8').splitlines():
        fields=line.split()
        if fields and fields[0] in ['v','vn','vt']:
            values=[]
            for token in fields[1:]:
                v=np.float32(token);a=np.format_float_positional(v,unique=True,trim='-');b=np.format_float_scientific(v,unique=True,trim='-')
                result=min([a,b],key=len);assert np.float32(result).tobytes()==v.tobytes();values.append(result)
            line=fields[0]+' '+' '.join(values)
        lines.append(line)
    data=('\n'.join(lines)+'\n').encode();p.write_bytes(data);row['sha256']=hashlib.sha256(data).hexdigest();row['operation']='float32-exact OBJ decimal compaction'
    rows.append(dict(path=name,before=len(zlib.compress(raw,9)),after=len(zlib.compress(data,9))))
(S/'asset-derivation.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf8')
(S/'obj-compaction.json').write_text(json.dumps(rows,indent=2),encoding='utf8')
print('OBJ compressed savings',sum(r['before']-r['after'] for r in rows),'across',len(rows),'meshes',flush=True)
