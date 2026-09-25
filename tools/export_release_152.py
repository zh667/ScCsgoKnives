"""Build the current cadence table and compact verified release evidence."""
from pathlib import Path
import hashlib,json,re
ROOT=Path(__file__).resolve().parents[1]
def read(p):return json.loads((ROOT/p).read_text('utf-8-sig'))
def sha(p):return hashlib.sha256((ROOT/p).read_bytes()).hexdigest()
def tiers(l):return [max(0,min(10,l-i*10)) for i in range(5)]
def multiplier(n,l):
    t=tiers(l)
    if n in ('awp','ssg08'):return 1+.65*(.05*t[1]+.05*t[2]+.075*t[3]+.075*t[4])
    if n=='taser':
        factor=1-.05*t[0]-.02*t[1]-.01*t[2]-.005*t[3]-.005*t[4]
        return 1+.65*(1/factor-1)
    return 1+(.004 if n in ('scar20','g3sg1') else .0065)*l
def main():
    data=read('src/ScCsgoKnives/AnimationData/cs2_weapons.json')['Guns']
    names=dict(re.findall(r'"(\w+)"=>"([^"]+)"',(ROOT/'src/ScCsgoKnives/World/ScGunNames.cs').read_text('utf8')))
    source=(ROOT/'src/ScCsgoKnives/Rendering/GunSpec.cs').read_text('utf8')
    order=re.findall(r'"(\w+)"',re.search(r'FrozenOrder = \[(.*?)\]',source).group(1))
    levels=[0,10,20,30,40,50]
    text=['# 全部枪械射速表（1.5.2）','','单位：发/分钟，普通射击模式的理论持续射速；不含换弹。全部35款初级恢复CS2基准，分级成长仅增加相对初级的加成。Zeus按充能限制单列说明。','','| 枪械 | Lv0 | Lv10 | Lv20 | Lv30 | Lv40 | Lv50 |','| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
    evidence=[]
    for n in order:
        base=2 if n=='taser' else 60/data[n]['CycleSeconds']
        rpm=[round(base*multiplier(n,l),3) for l in levels]
        text.append('| '+names[n]+' | '+' | '.join(f'{v:.2f}'.rstrip('0').rstrip('.') for v in rpm)+' |')
        evidence.append(dict(name=n,base_cycle_seconds=data[n]['CycleSeconds'],levels=levels,rpm=rpm))
    text+=['','Zeus 的触发间隔仍为0.15秒，但每发需等待充能，因此上表初级按30秒一次（2发/分）计。分级充能秒数依次为：'+ '、'.join(f'{30/multiplier("taser",l):.2f}' for l in levels)+'。',
           '', '三连发初级：Glock整轮0.5秒/轮内0.05秒，FAMAS整轮0.55秒/轮内0.075秒；升级按所属普通枪倍率缩短间隔。R8主射击0.5秒、副射击0.4秒，均按普通枪倍率成长。',
           '', '数据来源、旧枪状态处理与测试边界见 [1.5.2发布说明](release-single-1.5.2.md)。原始9月24日证据保留，旧表的减速值不再是当前实现。']
    (ROOT/'docs/gun-rates-2026-09-25.md').write_text('\n'.join(text)+'\n','utf8')
    report=read('.tmp/balance-check-152.json');assert report['failed']==0
    reports={'balance':{k:report[k] for k in ['coreSha256','count','passed','failed']}}
    packages={}
    for edition in ('Full','Lite'):
        package=read(f'output/release-single-1.5.2/{edition}.json');assert sha('output/'+package['path'])==package['sha256']
        packages[edition]={k:package[k] for k in ['path','bytes','sha256','gameplayDlls']}
        assert package['gameplayDlls']['ScCsgoKnives.dll']==report['coreSha256']
        for kind in ('core','tactical','gate'):
            file=f'output/release-single-1.5.2/{kind}-{edition.lower()}'+('-check.json' if kind!='gate' else '.json')
            r=read(file);assert r['failed']==0,(file,r['failed'])
            reports[kind+'-'+edition]=dict(count=len(r['checks']),failed=0,report_sha256=sha(file))
    for name in ['ScCsgoKnives.dll','ScCsgoTactical.dll','Integrations/ScCsgoAppearance.bin','ScCsgoBundle.dll']:
        assert packages['Full']['gameplayDlls'][name]==packages['Lite']['gameplayDlls'][name]
    result=dict(date='2026-09-25',base_commit='928280d',layout=5,schema=6,rules=7,packages=packages,reports=reports,rates=evidence,
                actual_world_copy_sha256=sha('.tmp/migration-20260925/World7-Project.bak'),
                actual_world_missing_record_ids=[7,8,12,3,4],recovered_missing_records=False,
                limits=['Read-only player world audit; no installation or player-world modification','Missing original records still require a verified pre-loss backup','Offline DLL, packaged regressions and native hook checks, not full-game/Android acceptance'])
    (ROOT/'docs/release-single-1.5.2-evidence.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps(reports,ensure_ascii=False,indent=2))
if __name__=='__main__':main()
