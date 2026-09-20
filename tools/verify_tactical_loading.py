"""Fresh-process native optional-loading checks, using the actual shipped package bytes."""
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
game=Path(sys.argv[1])
version=json.loads((ROOT/'src/ScCsgoTactical/modinfo.json').read_text('utf8'))['Version']
package=ROOT/f'output/[API1.9]CS战术同伴拓展{version}-作者ZH667.scmod'
build=ROOT/'tools/TacticalLoadCheck/bin/Release/net10.0'
stage=ROOT/'.tmp/tactical-loading-installed'
stage.mkdir(exist_ok=True)
for p in build.iterdir():
    if p.is_file():shutil.copy2(p,stage/p.name)
for p in game.glob('*.dll'):shutil.copy2(p,stage/p.name)
out=ROOT/f'output/tactical-{version}'
results=[]
for engine,folder in [('nuget',build),('installed',stage)]:
    for mode in ('none','nmm-only','neo-only','disabled','outdated','both','reversed','legacy','reload-disabled'):
        report=out/f'loading-{engine}-{mode}.json'
        command=['dotnet',str(folder/'TacticalLoadCheck.dll'),str(ROOT),str(game/'Content.zip'),str(package),mode,str(report)]
        with (out/f'loading-{engine}-{mode}.log').open('w',encoding='utf8') as log:
            result=subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
        if result.returncode:
            print((out/f'loading-{engine}-{mode}.log').read_text('utf8',errors='replace'))
            raise SystemExit(result.returncode)
        checked=json.loads(report.read_text('utf8'))
        assert checked['failed']==0
        results.append({'engine':engine,**checked})
        print(f'{engine}/{mode}: {len(checked["checks"])} passed',flush=True)
summary={'tacticalSha256':hashlib.sha256(package.read_bytes()).hexdigest(),'failed':0,'runs':results}
(out/'loading-checks.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n','utf8')
