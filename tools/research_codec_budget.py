"""Rebuild resource-only research DLLs and estimate whole-package codec budgets.
These DLLs have new encodings without production readers: NEVER ship them.
No scmod is generated or overwritten. Include the measured ZstdSharp DLL cost.
"""
import concurrent.futures,json,subprocess,zipfile,zlib
from research_resource_codecs import ROOT,STAGE,sha,encode

def zipped_size(data):
    c=zlib.compressobj(9,zlib.DEFLATED,-15);return min(len(data),len(c.compress(data)+c.flush()))

def profile(codec):
    inventory=json.loads((STAGE/'inventory.json').read_bytes());report=json.loads((STAGE/'lossless-report.json').read_bytes());lookup={r['name']:r for r in report['results']}
    folder=STAGE/'budgets'/codec;assets=folder/'AnimationData';assets.mkdir(parents=True,exist_ok=True);selected=[]
    for row in inventory:
        if row['group']=='glb':continue
        data=(STAGE/row['path']).read_bytes();assert sha(data)==row['sha256']
        if row['group']=='embedded-other':blob=data
        else:
            probe=lookup[row['name']]
            if row['group']=='weapon-animation':blob=(STAGE/probe['controls'][codec]['path']).read_bytes()
            elif probe['best'][codec]['mode']==0:blob=(STAGE/probe['best'][codec]['path']).read_bytes()
            else:blob=encode(data,[],0,codec)
            path=folder/'encoded'/row['path'];path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(blob)
            selected.append(dict(name=row['name'],group=row['group'],path=str(path.relative_to(STAGE)),mode=0,
                codec=codec,bytes=len(blob),outerZipBytes=zipped_size(blob),sha256=sha(blob),normalizedPath=row['path'],normalizedSha256=row['sha256']))
        if row['owner']=='core':(assets/row['name']).write_bytes(blob)
    project=folder/'ScCsgoResources.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoResources</AssemblyName><Version>1.10.4</Version><AssemblyVersion>1.10.4.0</AssemblyVersion><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion><GenerateDependencyFile>false</GenerateDependencyFile><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><EmbeddedResource Include="AnimationData/*"><LogicalName>Game.AnimationData.%(Filename)%(Extension)</LogicalName></EmbeddedResource></ItemGroup></Project>','utf8')
    with (folder/'build.log').open('w',encoding='utf8') as log:subprocess.run(['dotnet','build',str(project),'-c','Release','--nologo'],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,check=True)
    assembly=folder/'bin/Release/net10.0/ScCsgoResources.dll';dll=assembly.read_bytes();zip_path=folder/'resource-size-only.zip'
    if zip_path.exists():zip_path.unlink()
    with (folder/'7zip.log').open('w',encoding='utf8') as log:subprocess.run(['C:/Program Files/7-Zip/7z.exe','a','-tzip','-mm=Deflate','-mx=9','-mfb=258','-mpass=15','-mmt=2',str(zip_path),assembly.name],cwd=assembly.parent,stdout=log,stderr=subprocess.STDOUT,check=True)
    with zipfile.ZipFile(zip_path) as z:
        assert z.read(assembly.name)==dll
        size=min(zipped_size(dll),z.getinfo(assembly.name).compress_size)
    packages={}
    for owner,label,expected in [('core','轻量','2d4cfc231c7a4e419eb3e7332fbf6b89d1fdcc0601e2c174d0c60b2543c43ea3'),('agents','探员','9c430eecdbfd44ee501cd75352f3086c26ad98e6cda210bccc120ffac6c5d2db')]:
        original=ROOT/f'output/[API1.9]CS武器1.3.0-{label}包.scmod';assert sha(original.read_bytes())==expected
        with zipfile.ZipFile(original) as z:
            estimate=original.stat().st_size;old=0;new=0;changes=[];decoder_cost=0
            if owner=='core':
                old=z.getinfo('ScCsgoResources.dll').compress_size;new=size;estimate+=new-old
                if codec=='zstd19':
                    library=ROOT/'tools/ResourceCodecCheck/bin/Release/net10.0/ZstdSharp.dll';decoder_cost=zipped_size(library.read_bytes())+30+46+2*len('ZstdSharp.dll');estimate+=decoder_cost
            else:
                for row in selected:
                    if row['group'] not in ['actor-animation','npc-mesh']:continue
                    before=z.getinfo(row['name']).compress_size;after=min(before,row['outerZipBytes']);old+=before;new+=after;estimate+=after-before
                    changes.append(dict(name=row['name'],before=before,after=after,wrap=after<before))
            packages[owner]=dict(baselineBytes=original.stat().st_size,baselineSha256=expected,
                encodedPayloadEstimate=estimate,estimatedSavedBytes=original.stat().st_size-estimate,
                affectedOldCompressedBytes=old,affectedNewCompressedBytes=new,decoderZipCost=decoder_cost,changes=changes)
    result=dict(codec=codec,layout='Original resource bytes, no curve conversion or byte/bit shuffle',packages=packages,
        resourceDllBytes=len(dll),resourceDllZipBytes=size,resourceDllSha256=sha(dll),
        selected=selected,scope='Size-only rebuild and arithmetic package estimate. Includes ZstdSharp DLL+ZIP-header cost once in core; excludes new reader/manifest/licensing metadata changes. Not a loadable release.')
    (folder/'report.json').write_text(json.dumps(result,indent=2),'utf8');print(codec,json.dumps({k:{n:v for n,v in p.items() if n!='changes'} for k,p in packages.items()},ensure_ascii=False),flush=True);return result

if __name__=='__main__':
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:results=list(pool.map(profile,['brotli9','zstd19']))
    (STAGE/'budget-report.json').write_text(json.dumps(results,indent=2),'utf8')
