"""Read 35 CS2 firearm exports and generate a non-runtime handling proposal.

Never edits gun code, packaged data, user tuning or worlds. Angles are the existing
atan(spread + inaccuracy) single-cone approximation, not Valve's bullet algorithm.
"""
import argparse, hashlib, json, math, re, random
from pathlib import Path
from decimal import Decimal, ROUND_HALF_UP
import cs2_weapons as source

ROOT=Path(__file__).resolve().parents[1]
PLAN=ROOT/'docs/gun-counter-growth-attributes-touch-plan-2026-09-08.md'
def q(v): return float(Decimal(str(v)).quantize(Decimal('.01'),rounding=ROUND_HALF_UP))
def clamp(v,lo,hi): return max(lo,min(hi,v))
def deg(v): return math.degrees(math.atan(v))
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
# Proposal only. Caps are extra angles, never overwrite the first-shot cone.
RULES={
 'Pistol':dict(hipScale=.85,hipMin=.25,moveCap=.9,jumpCap=1.2,bloomAddCap=.2,bloomCap=1.2,range=28,near=8,floor=.6,recoilCap=1.6),
 'HeavyPistol':dict(hipScale=.85,hipMin=.18,moveCap=1.2,jumpCap=1.8,bloomAddCap=.4,bloomCap=1.6,range=36,near=12,floor=.65,recoilCap=2.2),
 'Smg':dict(hipScale=.85,hipMin=.35,moveCap=.45,jumpCap=.9,bloomAddCap=.1,bloomCap=.65,range=32,near=10,floor=.5,recoilCap=.9),
 'Rifle':dict(hipScale=.85,hipMin=.25,moveCap=1.1,jumpCap=1.5,bloomAddCap=.15,bloomCap=1.3,range=48,near=18,floor=.7,recoilCap=1.25),
 'ScopedRifle':dict(hipScale=.85,hipMin=.25,moveCap=1.2,jumpCap=1.5,bloomAddCap=.15,bloomCap=1.3,range=56,near=20,floor=.75,recoilCap=1.25),
 'BoltSniper':dict(hipScale=.8,hipMin=1.2,moveCap=1.6,jumpCap=2.0,bloomAddCap=.35,bloomCap=.6,range=64,near=36,floor=.85,recoilCap=2.8),
 'AutoSniper':dict(hipScale=.8,hipMin=1.0,moveCap=1.3,jumpCap=1.8,bloomAddCap=.3,bloomCap=.9,range=64,near=30,floor=.8,recoilCap=1.4),
 'Shotgun':dict(hipScale=.9,hipMin=2,moveCap=.5,jumpCap=1,bloomAddCap=.1,bloomCap=.4,range=24,near=5,floor=.15,recoilCap=3),
 'MachineGun':dict(hipScale=.85,hipMin=.45,moveCap=1.4,jumpCap=2,bloomAddCap=.18,bloomCap=1.6,range=56,near=18,floor=.65,recoilCap=1.2),
 'Taser':dict(hipScale=0,hipMin=0,moveCap=0,jumpCap=0,bloomAddCap=0,bloomCap=0,range=3.05,near=3.05,floor=1,recoilCap=0),
}
ZH={'Pistol':'普通手枪','HeavyPistol':'重型手枪','Smg':'冲锋枪','Rifle':'普通步枪','ScopedRifle':'瞄准镜步枪','BoltSniper':'栓狙','AutoSniper':'连狙','Shotgun':'霰弹枪','MachineGun':'机枪','Taser':'电击枪'}
def category(g):
    n=g['Name']
    if n in ('deagle','revolver'):return 'HeavyPistol'
    if n in ('aug','sg556'):return 'ScopedRifle'
    return g['Class']

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--export-root',type=Path,default=ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons');args=ap.parse_args()
    source.VDATA=args.export_root/'01_weapon_data/firearm_blocks'
    source.PAIRS += ['m_flInaccuracyLand','m_flInaccuracyLadder']
    source.SCALAR += ['m_flInaccuracyJumpInitial','m_flInaccuracyJumpApex','m_flRecoveryTimeCrouchFinal']
    package=json.loads((ROOT/'docs/weapon-stats-0386.json').read_text('utf-8'))
    embedded=json.loads((ROOT/'src/ScCsgoKnives/AnimationData/cs2_weapons.json').read_text('utf-8'))
    rows=[]
    for g in package['guns']:
        n=g['Name']; stem=source.GUNS[n]; path=source.VDATA/(stem+'.vdata');raw=source.read(stem)
        kind=category(g);r=RULES[kind]; taser=kind=='Taser'
        if not taser:
            for k in ('m_flSpread','m_flInaccuracyStand','m_flInaccuracyCrouch','m_flInaccuracyMove','m_flInaccuracyJump','m_flInaccuracyFire','m_flRecoilMagnitude'):
                assert k in raw and len(raw[k])==2,(n,k)
        for k in ('m_nDamage','m_iMaxClip1','m_flCycleTime','m_flRange','m_flRangeModifier'):assert k in raw,(n,k)
        assert raw['m_iMaxClip1']==g['Magazine'],n
        assert abs(raw['m_flCycleTime']-g['CycleSeconds'])<.00001,n
        assert int(raw.get('m_nNumBullets') or 1)==g['Pellets'],n
        def pair(k,i):return raw.get(k,[0,0])[i]
        modes={}
        for i in (0,1):
            spread=pair('m_flSpread',i)
            cs=deg(spread+pair('m_flInaccuracyStand',i))
            move=deg(spread+pair('m_flInaccuracyMove',i)); crouch=deg(spread+pair('m_flInaccuracyCrouch',i))
            jump=deg(spread+pair('m_flInaccuracyJump',i)); fire=deg(pair('m_flInaccuracyFire',i))
            scope=bool(g['ZoomLevels']) and i==1
            scale=.85 if scope else (.65 if n=='awp' else r['hipScale'])
            base=0 if taser else q(max(.1 if scope else r['hipMin'],cs*scale))
            # R8 fanning is a real supported mode, not a fictitious ADS state.
            moveExtra=q(min(1.2 if scope else r['moveCap'],max(0,move-cs)*.12))
            jumpExtra=0 if taser else q(clamp(max(0,jump-cs)*.25,.2,r['jumpCap']))
            add=0 if taser else q(clamp(fire*.12,.03,r['bloomAddCap']))
            rec=raw.get('m_flRecoveryTimeStand',0)
            crouchFactor=1 if taser else clamp(crouch/max(cs,1e-9),.8,1)
            modes[str(i)]={'csStandingCone':q(cs),'csMovingCone':q(move),'csCrouchingCone':q(crouch),'csJumpCone':q(jump),'csFireInaccuracyAngle':q(fire),
                'baseCone':base,'movingExtra':moveExtra,'fullMoveCone':q(base+moveExtra),'crouchingCone':q(base*crouchFactor),
                'jumpExtra':jumpExtra,'bloomPerShot':add,'bloomMax':r['bloomCap'],'bloomRecoverySeconds':0 if taser else q(clamp(.65*rec,.1,.35)),
                'kickPitch':q(min(r['recoilCap'],pair('m_flRecoilMagnitude',i)*(1.6/30)*.7)),
                'kickYaw':q(pair('m_flRecoilMagnitude',i)*(1.6/30)*math.sin(math.radians(pair('m_flRecoilAngleVariance',i)/2))*.35),
                'cameraRecoveryT90':0 if taser else q(clamp(.6*rec,.12,.4))}
            assert abs(deg(spread+pair('m_flInaccuracyStand',i))-embedded['Guns'][n]['SpreadDegreesAlternate' if i else 'SpreadDegrees'])<.00002,n
        alt = '开镜' if g['ZoomLevels'] else '消音' if g['HasSilencer'] else '三连发' if g['HasBurstMode'] else '扇射' if n=='revolver' else None
        maxrange={'nova':24,'xm1014':22,'mag7':20,'sawedoff':18}.get(n,r['range'])
        near={'nova':6,'xm1014':5,'mag7':5,'sawedoff':4}.get(n,r['near'])
        assert maxrange<=g['RangeBlocks']
        base=re.search(r'_base\s*=\s*"([^"]+)"',path.read_text('utf-8'))
        rows.append({'name':n,'label':package['labels']['Blocks'][f"ScGunBlock:{g['Variant']}"]['DisplayName'],
            'category':kind,'sourceFile':str(path.relative_to(args.export_root)),'sourceSha256':digest(path),'prefab':base.group(1) if base else None,
            'rawCs2':raw,'current':{'hipCone':g['SpreadDegrees'],'range':g['RangeBlocks'],'power':g['Power'],'cycle':g['CycleSeconds'],'magazine':g['Magazine']},
            'proposal':{'maxRange':maxrange,'falloffStart':near,'endpointMultiplier':r['floor'],'shotgunMidDistance':q((near+maxrange)/2) if kind=='Shotgun' else None,
              'shotgunMidMultiplier':.4 if kind=='Shotgun' else None,'supportedAlternate':alt,'modes':modes}})
    assert len(rows)==35 and len({r['name'] for r in rows})==35
    smoke_tests=[]
    rng=random.Random(20260908)
    for row in rows:
        p=row['proposal']
        assert 0<p['maxRange'] and 0<=p['falloffStart']<=p['maxRange']
        assert 0<p['endpointMultiplier']<=1
        for mode,m in p['modes'].items():
            assert all(math.isfinite(x) and x>=0 for x in m.values()),(row['name'],mode)
            assert m['fullMoveCone']>=m['baseCone']>=m['crouchingCone']
            if row['category']!='Taser':assert m['bloomRecoverySeconds']>=.1
            # Model-only samples: catches degree/radian errors and verifies the
            # proposed cone, not live game's target collision or random generator.
            angle=math.radians(m['baseCone']);hits=0;largest=0
            for _ in range(1000):
                a=angle*math.sqrt(rng.random());phi=2*math.pi*rng.random()
                radius=30*math.tan(a);largest=max(largest,radius)
                if abs(radius*math.cos(phi))<=.3 and abs(radius*math.sin(phi))<=.9:hits+=1
            assert largest<=30*math.tan(angle)+1e-10
            smoke_tests.append({'gun':row['name'],'mode':mode,'samples':1000,'distance':30,
               'hypotheticalTarget':'0.6 by 1.8 block flat rectangle centered on aim; no body tolerance, recoil, walls or falloff',
               'insideTargetFraction':hits/1000,'maxSampleRadius':q(largest),'maxTheoreticalRadius':q(30*math.tan(angle))})
    report={'status':'design proposal only; no game/runtime data changed','source':'local CS2 vdata exports; not a claim of latest worldwide patch',
        'parentPrefabSourceSha256':digest(source.VDATA.parent/'source/weapons.vdata'),'currentPackageSha256':package['packageSha256'],
        'rawAngleInterpretation':'degrees(atan(spread+state inaccuracy)); single-cone estimate, not official CS2 distribution',
        'classRules':RULES,'count':len(rows),'guns':rows,
        'validation':{'sourceCoverage':'35/35','modesMathChecked':len(smoke_tests),'runtimeAcceptance':False,'coneSanityChecks':smoke_tests}}
    (ROOT/'docs/gun-handling-35-source-audit-2026-09-08.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
    text=['### 2.2 全35枪逐项对照与轻量手感建议（本轮新增）','',
          '本表来自35份本机CS2枪械数据及继承默认，均核对容量、射击间隔、主副模式、原始散布／不精度、恢复、后坐力、射程和衰减。当前包内站立散布换算与重新读取原始文件一致。不是只检查狙击或按大类猜全部枪。原始参数、文件哈希、每模式建议值及覆盖检查保存在 [35枪来源审计](gun-handling-35-source-audit-2026-09-08.json)。该文件仅用于设计审计，游戏不加载它。重建命令：`python tools/audit_cs2_handling_plan.py`。', '',
          '数学检查覆盖35枪的两组资源模式，验证有限值、站立／移动／蹲姿边界及射程；每组1000个简化圆锥样本只用于排除单位和边界错误，不是实机命中率验收，不为未开放的副模式增加功能。', '',
          '**方案状态：待实施、待实机校准的明确初值。当前0.38.6没有改变。** 原始CS2列只是来源，不是已经实装的完整弹道算法。伤害、基础射速、容量、耐久、弹药成本继续保留现行生存平衡；本节调整基础手感和距离定位。', '',
          '#### A. 原始资源与当前默认对照','',
          '下表都是主模式、站立首发。无量纲原始量已由同一公式换算为角度，移动列是资源移动不精度加基础spread的比较端点，不直接当作新版游戏惩罚。', '',
          '| 枪械 | 当前散布° | CS2站立换算° | CS2移动换算° | CS2 Range单位 | 项目换算格 | CS2衰减参数 |','|---|---:|---:|---:|---:|---:|---:|']
    for row in rows:
        c=row['current'];m=row['proposal']['modes']['0'];raw=row['rawCs2']
        text.append(f"| {row['label']} | {c['hipCone']:.4f} | {m['csStandingCone']:.2f} | {m['csMovingCone']:.2f} | {raw['m_flRange']:g} | {raw['m_flRange']*.0254:.2f} | {raw['m_flRangeModifier']:g} |")
    text += ['', '#### B. 建议采用值：每一把枪，不用区间代替','',
             '静止／移动均指不开镜主模式、没有跳跃和连射累积。移动端点为水平速度≥4.5格/秒，不是任意轻推摇杆即吃满惩罚。副模式列只列真实支持的模式，普通枪资源的第二项不自动生成一个瞄准功能。表中角度保留两位作为设计值。', '',
             '| 枪械 | 静止首发° | 满移动首发° | 支持副模式静止° | 最大射程格 | 满伤害至格 | 射程末端倍率 |','|---|---:|---:|---|---:|---:|---:|']
    for row in rows:
        p=row['proposal'];m=p['modes']['0']; alt=f"{p['supportedAlternate']} {p['modes']['1']['baseCone']:.2f}" if p['supportedAlternate'] else '不适用'
        text.append(f"| {row['label']} | {m['baseCone']:.2f} | {m['fullMoveCone']:.2f} | {alt} | {p['maxRange']:g} | {p['falloffStart']:g} | {p['endpointMultiplier']*100:g}% |")
    text += ['', '#### C. 连射与后坐力的具体初值','',
             '每次成功开火之后增加散布，第一发不预扣惩罚；霰弹按一次扳机算一次、三连发按三次算。下表是主模式，副模式完整值见审计JSON。恢复时间指超过停火等待0.12秒后，从累积上限回到0的时间；少量累积恢复更快。镜头恢复T90是偏移衰减90%的用时，与散布恢复分开。', '',
             '| 枪械 | 每发新增散布° | 连射额外上限° | 满累积恢复秒 | 上跳基准° | 横摆±° | 镜头恢复T90秒 |','|---|---:|---:|---:|---:|---:|---:|']
    for row in rows:
        m=row['proposal']['modes']['0']
        text.append('| '+row['label']+' | '+' | '.join(f"{m[k]:.2f}" for k in ('bloomPerShot','bloomMax','bloomRecoverySeconds','kickPitch','kickYaw','cameraRecoveryT90'))+' |')
    text += ['', '<!-- END GENERATED HANDLING AUDIT -->','']
    doc=PLAN.read_text('utf-8');start='### 2.2 全35枪逐项对照与轻量手感建议（本轮新增）'
    if start in doc:
        a=doc.index(start);b=doc.index('<!-- END GENERATED HANDLING AUDIT -->',a)+len('<!-- END GENERATED HANDLING AUDIT -->')
        doc=doc[:a]+'\n'.join(text).rstrip()+doc[b:]
    else:doc=doc.replace('## 3. CS2 StatTrak 资源复用','\n'.join(text)+'\n## 3. CS2 StatTrak 资源复用',1)
    PLAN.write_text(doc,'utf-8')
    print('35/35 sources verified; wrote proposal audit and tables only.')
    for row in rows:
        p=row['proposal']; m=p['modes']['0'];print(row['name'],m['baseCone'],m['fullMoveCone'],p['maxRange'])
if __name__=='__main__':main()
