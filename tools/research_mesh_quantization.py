"""Research meshoptimizer + bounded vertex quantization, never a shipped codec."""
from pathlib import Path
import concurrent.futures,json,struct,sys,time,zlib
from research_resource_codecs import ROOT,STAGE,sha,mesh_spans
import numpy as np
import brotli,zstandard as zstd,meshoptimizer as mo

def quantize(source,count,stride,position_bits,normal_bits):
    a=np.frombuffer(source,np.uint8).reshape(count,stride)
    f=a[:,:32].copy().view('<f4').reshape(count,8).astype(np.float64)
    assert np.isfinite(f).all()
    pmin=f[:,:3].min(axis=0);pmax=f[:,:3].max(axis=0);umin=f[:,6:8].min(axis=0);umax=f[:,6:8].max(axis=0)
    def encode(values,low,high,bits):
        extent=high-low;safe=np.where(extent==0,1,extent);n=(1<<bits)-1
        q=np.rint((values-low)/safe*n).clip(0,n).astype('<u4')
        restored=(low+q.astype(np.float64)/n*extent).astype('<f4')
        return q,restored
    qp,p=encode(f[:,:3],pmin,pmax,position_bits);quv,uv=encode(f[:,6:8],umin,umax,16)
    assert np.abs(f[:,3:6]).max()<=1.00001
    scale=(1<<(normal_bits-1))-1;qn=np.rint(np.clip(f[:,3:6],-1,1)*scale).astype('<i2');norm=(qn.astype(np.float64)/scale).astype('<f4')
    prefix=16 if position_bits==16 else 24;newstride=prefix+stride-32
    packed=np.zeros((count,newstride),np.uint8);posbytes=6 if position_bits==16 else 12
    packed[:,:posbytes]=qp.astype('<u2' if position_bits==16 else '<u4').view(np.uint8).reshape(count,posbytes)
    packed[:,posbytes:posbytes+6]=qn.view(np.uint8).reshape(count,6)
    packed[:,posbytes+6:posbytes+10]=quv.astype('<u2').view(np.uint8).reshape(count,4)
    packed[:,prefix:]=a[:,32:]
    # Decode the integer layout, not just compare a floating calculation with itself.
    qp2=packed[:,:posbytes].copy().view('<u2' if position_bits==16 else '<u4').reshape(count,3).astype(np.float64)
    qn2=packed[:,posbytes:posbytes+6].copy().view('<i2').reshape(count,3).astype(np.float64)
    quv2=packed[:,posbytes+6:posbytes+10].copy().view('<u2').reshape(count,2).astype(np.float64)
    restored=a.copy();floats=np.concatenate([(pmin+qp2/((1<<position_bits)-1)*(pmax-pmin)).astype('<f4'),(qn2/scale).astype('<f4'),(umin+quv2/65535*(umax-umin)).astype('<f4')],axis=1)
    restored[:,:32]=floats.view(np.uint8).reshape(count,32)
    assert np.array_equal(restored[:,32:],a[:,32:])
    diff=floats.astype(np.float64)-f
    poserr=np.linalg.norm(diff[:,:3],axis=1);uverr=np.linalg.norm(diff[:,6:8],axis=1)
    n0=f[:,3:6];n1=floats[:,3:6].astype(np.float64);lens=np.linalg.norm(n0,axis=1)*np.linalg.norm(n1,axis=1);valid=lens>1e-12
    angles=np.degrees(np.arccos(np.clip(np.sum(n0[valid]*n1[valid],axis=1)/lens[valid],-1,1)))
    metadata=np.concatenate([pmin,pmax,umin,umax]).astype('<f4').tobytes()
    stats=dict(vertices=count,maxPositionError=float(poserr.max()),maxUvError=float(uverr.max()),maxNormalDegrees=float(angles.max()) if len(angles) else 0,zeroNormals=int((~valid).sum()),maxComponentError=float(np.abs(diff).max()))
    return packed.tobytes(),newstride,metadata,restored.tobytes(),stats

def run(row):
    raw=(STAGE/row['path']).read_bytes();assert sha(raw)==row['sha256'];spans=mesh_spans(raw,row['name'].endswith('.skin'));results=[]
    for label,pbits,nbits in [('lossless',0,0),('p18-n16-uv16',18,16),('p16-n12-uv16',16,12)]:
        encoded=bytearray(b'RSMOPT01'+struct.pack('<II',len(raw),len(spans)));last=0;rebuilt=bytearray(raw);errors=[];vertices=0;indices=0;started=time.perf_counter()
        for offset,count,stride in spans:
            gap=raw[last:offset];source=raw[offset:offset+count*stride];kind=0 if stride==4 else 1
            encoded+=struct.pack('<I',len(gap))+gap;metadata=b'';newstride=stride
            if kind:
                vertices+=count;buffer=source
                if pbits:
                    buffer,newstride,metadata,restored,stats=quantize(source,count,stride,pbits,nbits);errors.append(stats);rebuilt[offset:offset+count*stride]=restored
                data=np.frombuffer(buffer,np.uint8).reshape(count,newstride);packed=mo.encode_vertex_buffer(data,count,newstride)
                assert mo.decode_vertex_buffer(count,newstride,packed).tobytes()==buffer
            else:
                indices+=count;packed=mo.encode_index_sequence(np.frombuffer(source,dtype='<u4'));assert mo.decode_index_sequence(count,4,packed).tobytes()==source
            encoded+=struct.pack('<BIIIIII',kind,count,stride,newstride,pbits,nbits,len(metadata))+metadata+struct.pack('<I',len(packed))+packed
            last=offset+count*stride
        encoded+=raw[last:]
        if not pbits:assert bytes(rebuilt)==raw
        compressed={'brotli9':brotli.compress(bytes(encoded),quality=9),'zstd19':zstd.ZstdCompressor(level=19).compress(bytes(encoded))}
        assert brotli.decompress(compressed['brotli9'])==bytes(encoded) and zstd.ZstdDecompressor().decompress(compressed['zstd19'])==bytes(encoded)
        folder=STAGE/'meshoptimizer'/label;folder.mkdir(parents=True,exist_ok=True)
        best=min(compressed,key=lambda k:len(compressed[k]));p=folder/(row['name']+'.'+best);p.write_bytes(compressed[best])
        aggregate={k:max((e[k] for e in errors),default=0) for k in ['maxPositionError','maxUvError','maxNormalDegrees','maxComponentError']}
        results.append(dict(setting=label,vertexCount=vertices,indexCount=indices,rawContainerBytes=len(encoded),
            **{c:len(b) for c,b in compressed.items()},errors=aggregate,
            indicesWeightsJointsMetadataUnchanged=True,codecRoundtripExact=True,decodedVertexSha256=sha(bytes(rebuilt)),seconds=time.perf_counter()-started))
    return dict(name=row['name'],sourceSha256=row['sha256'],bytes=len(raw),settings=results)

if __name__=='__main__':
    rows=[r for r in json.loads((STAGE/'inventory.json').read_bytes()) if r['group']=='embedded-mesh'];results=[]
    with concurrent.futures.ProcessPoolExecutor(max_workers=3) as pool:
        for row in pool.map(run,rows):
            results.append(row)
            if len(results)%10==0:print('meshoptimizer',len(results),'/',len(rows),flush=True)
    totals={}
    for setting in ['lossless','p18-n16-uv16','p16-n12-uv16']:
        values=[next(v for v in r['settings'] if v['setting']==setting) for r in results]
        totals[setting]=dict(files=len(values),**{c:sum(v[c] for v in values) for c in ['brotli9','zstd19','vertexCount','indexCount']},
            errors={k:max(v['errors'][k] for v in values) for k in ['maxPositionError','maxUvError','maxNormalDegrees','maxComponentError']})
    (STAGE/'meshoptimizer-report.json').write_text(json.dumps(dict(results=results,totals=totals,scope='Offline codecs and grid quantization only; no mesh simplification, rendering acceptance or shipped runtime.'),indent=2),'utf8');print(json.dumps(totals,indent=2))
