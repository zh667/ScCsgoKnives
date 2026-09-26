"""Offline lossless numeric-layout/Brotli/Zstd experiments on exact split assets.
No production readers or delivered archives are modified. Blosc's byte-shuffle
idea is implemented here explicitly, not claimed to be the Blosc container.
"""
from pathlib import Path
import concurrent.futures,hashlib,json,struct,sys,time,zlib
ROOT=Path(__file__).resolve().parents[1];STAGE=ROOT/'.tmp/resource-codecs-20260926'
sys.path[:0]=[str(STAGE/'deps'),str(ROOT/'.tmp/optimization-deps')]
import numpy as np
import brotli
import zstandard as zstd

def sha(data):return hashlib.sha256(data).hexdigest()
class Reader:
    def __init__(self,raw):self.raw=raw;self.p=0;self.spans=[]
    def take(self,n):
        assert 0<=n<=len(self.raw)-self.p
        start=self.p;self.p+=n;return self.raw[start:self.p]
    def num(self,fmt):return struct.unpack('<'+fmt,self.take(struct.calcsize('<'+fmt)))[0]
    def text(self,kind='I'):
        if kind=='7':
            n=0;shift=0
            while True:
                b=self.num('B');n|=(b&127)<<shift
                if b<128:break
                shift+=7;assert shift<35
        else:n=self.num(kind)
        return self.take(n).decode('utf8')
    def span(self,count,stride):
        assert count>=0 and stride>0
        if count:self.spans.append((self.p,count,stride))
        self.take(count*stride)

def mesh_spans(raw,skin):
    r=Reader(raw);r.take(12)
    for _ in range(r.num('H')):r.text('H');r.take(64)
    r.span(r.num('i'),52 if skin else 32)
    for _ in range(r.num('H')):
        if not skin:r.take(2)
        r.text('H');r.span(r.num('i'),4)
    if not skin:
        r.span(r.num('i'),52)
        for _ in range(r.num('H')):r.text('H');r.span(r.num('i'),4)
    assert r.p==len(raw);return r.spans

def npc_spans(raw):
    r=Reader(raw);assert r.num('I')==0x314D574E
    r.text();r.take(2+64)
    for _ in range(r.num('i')):
        r.text();r.take(1);r.text();r.take(64);r.text();r.take(64)
        nv,ni=r.num('i'),r.num('i');r.span(nv,26);r.span(ni,4)
    assert r.p==len(raw);return r.spans

def actor_spans(raw):
    r=Reader(raw);assert r.take(8)==b'SCACT001'
    for _ in range(r.num('i')):r.text('7')
    clips=[]
    for _ in range(r.num('i')):
        name=r.text('7');duration=r.num('f');keys=r.num('i')
        time_offset=r.p;r.span(keys,4);channels=[]
        for _ in range(r.num('i')):
            bone=r.num('i');prop=r.num('B');width=4 if prop==1 else 3
            channels.append(dict(bone=bone,property=prop,offset=r.p,keys=keys,width=width))
            r.span(keys,width*4)
        clips.append(dict(name=name,duration=duration,keys=keys,timeOffset=time_offset,channels=channels))
    assert r.p==len(raw);return r.spans,clips

def shuffle(raw,spans,mode,inverse=False):
    output=bytearray(raw)
    for offset,count,stride in spans:
        data=np.frombuffer(raw,dtype=np.uint8,count=count*stride,offset=offset)
        if mode==3:
            data=data.reshape(count,stride);restored=data.copy()
            for start in range(0,count,256):
                size=min(256,count-start);aligned=size-size%8
                if not aligned:continue
                block=data[start:start+aligned]
                if inverse:
                    bits=np.unpackbits(block.reshape(stride*8,aligned//8),axis=1,bitorder='little').T
                    restored[start:start+aligned]=np.packbits(bits,axis=1,bitorder='little')
                else:
                    bits=np.unpackbits(block,axis=1,bitorder='little').T
                    restored[start:start+aligned]=np.packbits(bits,axis=1,bitorder='little').reshape(aligned,stride)
            output[offset:offset+count*stride]=restored.tobytes();continue
        if inverse:
            data=data.reshape(stride,count).T.copy()
            if mode==2:data=np.bitwise_xor.accumulate(data,axis=0)
        else:
            data=data.reshape(count,stride)
            if mode==2:
                delta=data.copy();delta[1:]=data[1:]^data[:-1];data=delta
            data=data.T.copy()
        output[offset:offset+count*stride]=data.tobytes()
    return bytes(output)

def encode(payload,spans,mode,codec):
    table=struct.pack('<I',len(spans))+b''.join(struct.pack('<III',*s) for s in spans)
    inner=table+payload
    if codec=='deflate9':
        compressor=zlib.compressobj(9,zlib.DEFLATED,-15);packed=compressor.compress(inner)+compressor.flush();assert zlib.decompress(packed,-15)==inner;tag=0
    elif codec=='brotli9':packed=brotli.compress(inner,quality=9);assert brotli.decompress(packed)==inner;tag=1
    else:packed=zstd.ZstdCompressor(level=19).compress(inner);assert zstd.ZstdDecompressor().decompress(packed)==inner;tag=2
    return b'RSCODE01'+struct.pack('<BBHII',mode,tag,0,len(payload),len(inner))+packed

def run(row):
    start=time.perf_counter();source=(STAGE/row['path']).read_bytes();assert sha(source)==row['sha256']
    raw=source;group=row['group']
    if group=='weapon-animation':raw=(STAGE/row['packed']['path']).read_bytes();spans=row['packed']['spans'];assert sha(raw)==row['packed']['sha256']
    elif group=='embedded-mesh':spans=mesh_spans(raw,row['name'].endswith('.skin'))
    elif group=='npc-mesh':spans=npc_spans(raw)
    elif group=='actor-animation':spans,clips=actor_spans(raw)
    else:raise ValueError(group)
    end=0
    for offset,count,stride in spans:assert offset>=end;end=offset+count*stride
    assert end<=len(raw)
    identifier=sha((row['owner']+'/'+row['name']).encode())[:20]
    modes={0:raw,1:shuffle(raw,spans,1),2:shuffle(raw,spans,2),3:shuffle(raw,spans,3)}
    for mode in [1,2,3]:assert shuffle(modes[mode],spans,mode,True)==raw
    variants=[];best={}
    folder=STAGE/'encoded'/group;folder.mkdir(parents=True,exist_ok=True)
    for mode,payload in modes.items():
        for codec in ['deflate9','brotli9','zstd19']:
            tick=time.perf_counter();blob=encode(payload,spans if mode else [],mode,codec)
            outer=zlib.compressobj(9,zlib.DEFLATED,-15);outer_data=outer.compress(blob)+outer.flush()
            result=dict(mode=mode,codec=codec,bytes=len(blob),outerZipBytes=min(len(blob),len(outer_data)),seconds=time.perf_counter()-tick)
            variants.append(result)
            if codec not in best or result['outerZipBytes']<best[codec]['outerZipBytes']:
                path=folder/(identifier+'.'+codec+'.rsc');path.write_bytes(blob)
                best[codec]=dict(**result,path=str(path.relative_to(STAGE)),sha256=sha(blob))
    controls={}
    if group=='weapon-animation':
        # Direct JSON compression is a separate control, not confused with the
        # binary-curve representation. Keep all production metadata unchanged.
        for codec in ['brotli9','zstd19']:
            blob=encode(source,[],0,codec);outer=zlib.compressobj(9,zlib.DEFLATED,-15);outer_data=outer.compress(blob)+outer.flush()
            path=folder/(identifier+'.original-json.'+codec+'.rsc');path.write_bytes(blob)
            controls[codec]=dict(mode=0,codec=codec,bytes=len(blob),outerZipBytes=min(len(blob),len(outer_data)),path=str(path.relative_to(STAGE)),sha256=sha(blob),normalizedPath=row['path'],normalizedSha256=row['sha256'])
            variants.append(dict(mode=-1,codec='json-'+codec,bytes=len(blob),outerZipBytes=controls[codec]['outerZipBytes'],seconds=None))
    comp=zlib.compressobj(9,zlib.DEFLATED,-15);baseline=len(comp.compress(source)+comp.flush())
    return dict(owner=row['owner'],name=row['name'],group=group,path=row['path'],sourceSha256=row['sha256'],
        sourceBytes=len(source),normalizedPath=row['packed']['path'] if row['packed'] else row['path'],
        normalizedBytes=len(raw),normalizedSha256=sha(raw),spans=len(spans),
        actualZipCompressedBytes=row['compressedBytes'],baselineDeflate9Bytes=baseline,
        variants=variants,best=best,controls=controls,exactRoundtrip=True,nativeFloat32Exact=row['packed']['nativeFloat32Exact'] if row['packed'] else None,
        seconds=time.perf_counter()-start)

def main():
    rows=json.loads((STAGE/'inventory.json').read_bytes())
    jobs=[r for r in rows if r['group'] in ['embedded-mesh','weapon-animation','npc-mesh','actor-animation']]
    jobs.sort(key=lambda r:r['bytes'],reverse=True);results=[];started=time.perf_counter()
    with (STAGE/'lossless-progress.jsonl').open('w',encoding='utf8') as log:
        with concurrent.futures.ProcessPoolExecutor(max_workers=4) as pool:
            for row in pool.map(run,jobs,chunksize=1):
                results.append(row);log.write(json.dumps(row)+'\n');log.flush()
                if len(results)%10==0 or row['sourceBytes']>9_000_000:print(row['group'],row['name'],len(results),'/',len(jobs),{c:r['bytes'] for c,r in row['best'].items()},flush=True)
    totals={}
    for group in sorted({r['group'] for r in results}):
        part=[r for r in results if r['group']==group]
        totals[group]=dict(files=len(part),sourceBytes=sum(r['sourceBytes'] for r in part),
            normalizedBytes=sum(r['normalizedBytes'] for r in part),baselineDeflate9=sum(r['baselineDeflate9Bytes'] for r in part),
            actualZipBytes=sum(r['actualZipCompressedBytes'] or 0 for r in part),
            **{c:sum(r['best'][c]['outerZipBytes'] for r in part) for c in ['deflate9','brotli9','zstd19']})
    report=dict(totals=totals,results=results,seconds=time.perf_counter()-started,
        versions=dict(numpy=np.__version__,brotli=brotli.__version__,zstandard=zstd.__version__,zstd=zstd.ZSTD_VERSION),
        scope='Offline resource encoding; exact binary/float32 roundtrips, not a loadable scmod or Android benchmark.')
    (STAGE/'lossless-report.json').write_text(json.dumps(report,indent=2),'utf8');print(json.dumps(totals,indent=2),flush=True)
if __name__=='__main__':main()
