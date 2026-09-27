"""Reproducible isolated build/check runner; logs stay on the project drive."""
import argparse,concurrent.futures,json,subprocess,os
from pathlib import Path
from prepare_capacity_fix import ROOT,STAGE
DLLS={name:STAGE/path/'bin/Release/net10.0/ScCsgoKnives.dll' for name,path in [('full','full/source'),('lite','lite/source'),('1.0.0','1.0.0/src/ScCsgoKnives'),('1.2.0','1.2.0/src/ScCsgoKnives')]}
def run(label,arguments):
    logs=STAGE/'logs';logs.mkdir(exist_ok=True)
    with (logs/(label+'.log')).open('w',encoding='utf8') as log:
        env=os.environ.copy();env['SC_NMM_CHECK_PACKAGE']=r'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods\[API1.9]NekoMeko Model-v1.1.scmod'
        result=subprocess.run(['dotnet',*map(str,arguments)],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,env=env)
    print(label, 'PASS' if result.returncode==0 else 'FAIL',flush=True)
    if result.returncode:print('See '+str(logs/(label+'.log')),flush=True)
    return result.returncode==0
def main():
    mode=argparse.ArgumentParser();mode.add_argument('mode',choices=['build','checks','inventory','package','package-regression','integration']);a=mode.parse_args()
    jobs=[]
    if a.mode=='build':
        builds=json.loads((STAGE/'builds.json').read_text('utf8'))
        jobs=[('build-'+k,['build',p,'-c','Release','-p:SkipScmodPackaging=true','--nologo','-v:q']) for k,p in builds.items()]
        jobs += [('build-'+t,['build',ROOT/'tools'/t/(t+'.csproj'),'-c','Release','--nologo','-v:q']) for t in ['CapacityCheck','CompatibilityCheck','InventoryCheck','PackageCheck','TacticalLoadCheck']]
    elif a.mode=='checks':
        check=ROOT/'tools/CapacityCheck/bin/Release/net10.0/CapacityCheck.dll';donor=ROOT/'.tmp/player-world-audit-20260927/安(3).scworld'
        old=[STAGE/n/'ScCsgoKnives.dll' for n in ['old100','old120','old130']]
        jobs=[('capacity-'+k,[check,p,donor,r'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods',STAGE/(k+'-check.json'),*old]) for k,p in DLLS.items()]
        jobs += [('capacity-matrix',[check,'--matrix',donor,STAGE/'extended-matrix.json',DLLS['1.0.0'],DLLS['1.2.0'],DLLS['full'],DLLS['lite']])]
        jobs += [('family',[ROOT/'tools/CompatibilityCheck/bin/Release/net10.0/CompatibilityCheck.dll',DLLS['1.0.0'],DLLS['1.2.0'],DLLS['full'],STAGE/'family.json'])]
    elif a.mode=='inventory':
        jobs=[('inventory',[ROOT/'tools/InventoryCheck/bin/Release/net10.0/InventoryCheck.dll',DLLS['full'],r'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods',STAGE/'inventory.json'])]
    else:
        content=r'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Content.zip'
        packages=json.loads((STAGE/'packages.json').read_text('utf8'))
        for role in (['full','lite'] if a.mode!='integration' else []):
            p=STAGE/'candidate'/packages[role]['file']
            jobs.append(('package-'+role,[ROOT/'tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll','--scmod',p,'--sha256',packages[role]['sha256'],'--vanilla-content',content,'--json',STAGE/('package-'+role+'.json')]))
        for role in (['full','lite','1.0.0-full','1.0.0-lite','1.2.0-full','1.2.0-lite'] if a.mode=='package' else []):
            jobs.append(('native-'+role,[ROOT/'tools/TacticalLoadCheck/bin/Release/net10.0/TacticalLoadCheck.dll','--world-resource-gate',STAGE/'candidate'/packages[role]['file'],STAGE/('native-'+role+'.json')]))
        if a.mode=='package':jobs.append(('native-hooks',[ROOT/'tools/TacticalLoadCheck/bin/Release/net10.0/TacticalLoadCheck.dll','--compat-native',STAGE/'candidate'/packages['full']['file'],content,STAGE/'native-hooks.json']))
        if a.mode=='integration':
            for role in ['full','lite']:
                core=STAGE/'candidate'/packages[role]['file']
                addon=core if role=='full' else STAGE/'baseline'/'[API1.9]CS武器1.3.0-探员包.scmod'
                for variant in ['both','reversed','none']:
                    label='integration-'+role+'-'+variant
                    jobs.append((label,[ROOT/'tools/TacticalLoadCheck/bin/Release/net10.0/TacticalLoadCheck.dll',ROOT,content,addon,variant,STAGE/(label+'.json'),core]))
                label='ai-'+role
                jobs.append((label,[ROOT/'tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll','--scmod',core,'--tactical-package',addon,'--tactical-ai-only','--vanilla-content',content,'--json',STAGE/(label+'.json')]))
                if role=='lite':
                    for variant in ['core','agents']:
                        label='split-'+variant
                        jobs.append((label,[ROOT/'tools/SplitCheck/bin/Release/net10.0/SplitCheck.dll',core,addon,content,variant,STAGE/(label+'.json')]))
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:results=list(pool.map(lambda job:run(*job),jobs))
    raise SystemExit(0 if all(results) else 1)
if __name__=='__main__':main()
