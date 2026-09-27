"""Promote validated local artifacts without replacing any previous delivery or player file."""
import json,shutil
from prepare_capacity_fix import ROOT,STAGE,sha

def read(name):return json.loads((STAGE/name).read_text('utf8'))
def copy_new(source,target):
    target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists():assert sha(target.read_bytes())==sha(source.read_bytes()),f'Refusing to replace {target}'
    else:shutil.copyfile(source,target)
    assert sha(target.read_bytes())==sha(source.read_bytes())

def main():
    packages=read('packages.json');baseline=read('baselines.json');results={}
    names=['family','extended-matrix','inventory','full-check','lite-check','1.0.0-check','1.2.0-check',
           'package-full','package-lite','native-hooks','ai-full','ai-lite','split-core','split-agents']
    names += ['native-'+k for k in packages]
    names += [f'integration-{role}-{variant}' for role in ['full','lite'] for variant in ['both','reversed','none']]
    for name in names:
        report=read(name+'.json');assert report['failed']==0,name
        results[name]={'failed':0,'count':report.get('count',len(report.get('checks',[])))}
    for key,meta in baseline.items():assert sha((ROOT/'output'/key).read_bytes())==meta['sha256'],'Previous delivery changed'
    for key,p in packages.items():
        source=STAGE/'candidate'/p['file'];assert sha(source.read_bytes())==p['sha256']
        report=read((key if key in ['full','lite'] else key.split('-')[0])+'-check.json')
        assert report['coreSha256'].lower()==p['coreSha256']
        if key in ['full','lite']:
            r=read('package-'+key+'.json');assert r['packageSha256'].lower()==p['sha256'] and r['dllSha256'].lower()==p['coreSha256']
        copy_new(source,ROOT/'output'/p['file'])
    recovery=read('recovered-worlds/world-recovery.json')
    assert recovery['originalsUnchanged'] and recovery['coreSha256'].lower()==packages['full']['coreSha256']
    private=ROOT/'output/玲兰扩容恢复-20260927'
    for w in recovery['worlds']:
        assert sha((ROOT/'.tmp/player-world-audit-20260927'/w['source']).read_bytes())==w['sourceSha256'].lower()
        source=STAGE/'recovered-worlds'/w['file'];assert sha(source.read_bytes())==w['sha256'].lower()
        copy_new(source,private/w['file'])
    copy_new(STAGE/'recovered-worlds/world-recovery.json',private/'world-recovery.json')
    copy_new(ROOT/'docs/capacity-player-instructions-2026-09-27.md',private/'使用说明.md')
    summary={'revision':'capacity-20260927','layout':6,'schema':7,'maximumRecords':66558,
             'packages':{k:{n:v for n,v in p.items() if n!='entries'} for k,p in packages.items()},
             'unchangedAgents':baseline['[API1.9]CS武器1.3.0-探员包.scmod'],'checks':results,
             'androidAcceptance':False,'previousDeliveriesUnchanged':True,'installedModsOrWorldsChanged':False}
    report=ROOT/'output/release-capacity-20260927/manifest.json';report.parent.mkdir(parents=True,exist_ok=True)
    data=json.dumps(summary,ensure_ascii=False,indent=2)+'\n'
    if report.exists():assert report.read_text('utf8')==data,'Delivery manifest changed'
    else:report.write_text(data,'utf8')
    copy_new(ROOT/'docs/release-capacity-2026-09-27.md',report.parent/'发布说明.md')
    print(json.dumps({'packages':len(packages),'worlds':len(recovery['worlds']),'checks':results,'output':str(report.parent)},ensure_ascii=False,indent=2))

if __name__=='__main__':main()
