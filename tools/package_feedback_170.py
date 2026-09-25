"""1.7.0 gameplay patch reuses verified family-1 resource streams; optional voices ship separately."""
import argparse,json,hashlib,zipfile,zlib
from pathlib import Path
from pack_single_scmods import raw_member,write_archive
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output';REPORT=OUT/'release-feedback-1.7.0'
def sha(b):return hashlib.sha256(b).hexdigest()
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--edition',choices=['Full','Lite','Voice'],required=True);a=ap.parse_args();REPORT.mkdir(exist_ok=True)
    test=json.loads((ROOT/'.tmp/feedback-check.json').read_text());assert test['failed']==0
    entries={};hashes={}
    def add(n,data):
        c=zlib.compressobj(9,zlib.DEFLATED,-15);entries[n]=(8,zlib.crc32(data),len(data),c.compress(data)+c.flush());hashes[n]=sha(data)
    def dll(name):
        data=(ROOT/f'src/{name}/bin/Release/net10.0/{name}.dll').read_bytes();assert sha(data)==test['dlls'][name],name;add(name+'.dll',data)
    if a.edition=='Voice':
        root=ROOT/'src/ScCsgoVoice';dll('ScCsgoVoice')
        add('modinfo.json',(root/'modinfo.json').read_bytes())
        for p in sorted((root/'Assets').rglob('*')):
            if p.is_file():add(str(p.relative_to(root)).replace('\\','/'),p.read_bytes())
        add('INSTALL.txt','CS探员语音1.0.0：独立可选附属包，主包1.7.0启用完整语音功能。全量/轻量共用。\n模组设置→探员语音设置选择中文或英文；CT/T玩家按Z或语音触屏键选句。可在武器按键/触控布局中调整。NPC自动语音跟随同一语言。\n未安装NMM/Neorxna及选中CT/T时，玩家角色语音不可用；NPC语音仍可用。旧兼容主包没有接口时语音停用，不阻止世界加载。\n附属包不生成世界记录或自动备份，不提供联网广播。语义提示不是逐字字幕。\n'.encode('utf-8-sig'))
        add('ASSET_SOURCES.md','Audio derived from user-supplied 中文语音包(2).zip and CSGO AGENT VOICE.zip. Original Counter-Strike characters/audio belong to their respective rights holders. Original archives untouched. Each source path/hash and derived hash is in Assets/ScAgentVoices.json.\n'.encode('utf8'))
        target=OUT/'[API1.9]CS探员语音1.0.0-中英精选-作者ZH667.scmod'
    else:
        sourceReport=json.loads((OUT/'release-compatibility-1'/f'latest-{a.edition}.json').read_text('utf8'));source=OUT/sourceReport['path'];assert sha(source.read_bytes())==sourceReport['sha256']
        with zipfile.ZipFile(source) as z:
            for i in z.infolist():entries[i.filename]=(i.compress_type,i.CRC,i.file_size,raw_member(z,i));hashes[i.filename]=sha(z.read(i))
            meta=json.loads(z.read('modinfo.json'));meta['Version']='1.7.0';meta['Description']='制作等级按前期手枪/霰弹/冲锋、后期步枪/狙击重排；快速投掷可切换完整模式；修复CT重刀内层手臂穿袖；支持独立CS探员中英语音附属。兼容系列1双向替换，不自动备份。'
            add('modinfo.json',json.dumps(meta,ensure_ascii=False,indent=2).encode('utf8'))
            core=json.loads(z.read('Integrations/ScCsgoKnives.modinfo.json'));core['Version']='1.7.0';add('Integrations/ScCsgoKnives.modinfo.json',json.dumps(core,ensure_ascii=False).encode('utf8'))
            tactical=json.loads((ROOT/'src/ScCsgoTactical/modinfo.json').read_text('utf8'));add('Integrations/ScCsgoTactical.modinfo.json',json.dumps(tactical,ensure_ascii=False).encode('utf8'))
            bundle=json.loads(z.read('Integrations/ScCsgoBundle.json'));bundle.update(version='1.7.0',core='1.7.0',tactical='1.5.0',coreSha256=test['dlls']['ScCsgoKnives']);add('Integrations/ScCsgoBundle.json',json.dumps(bundle,ensure_ascii=False).encode('utf8'))
            family=json.loads(z.read('Integrations/CompatibilityFamily.json'));family.update(version='1.7.0',core_sha256=test['dlls']['ScCsgoKnives'],build_revision='2026-09-25-feedback');add('Integrations/CompatibilityFamily.json',json.dumps(family,indent=2).encode('utf8'))
        dll('ScCsgoKnives');dll('ScCsgoTactical')
        add('INSTALL.txt','CS武器1.7.0总包（含战术1.5.0）：退出世界后替换CS主包，只启用一个全量/轻量总包，不另装独立战术包。\n兼容系列1，原枪状态保持，不自动备份，由玩家手动备份。\n制作门槛是人物等级；已有枪不锁使用。最新版新增0.15～0.20秒快速投掷，可在模组设置即时切回完整准备动作。\nCT长袖使用遮蔽派生网格，T露肤保持。\n语音为独立可选scmod，本总包不含音频，安装CS探员语音后，模组设置选中文/英文；CT/T玩家按Z或语音触屏键打开选句菜单。\n诊断为离线与原生加载/渲染，不等于所有设备实机验收。\n'.encode('utf-8-sig'))
        target=OUT/f'[API1.9]CS武器1.7.0-作者ZH667-{"512轻量" if a.edition=="Lite" else "全量"}总包.scmod'
    pending=target.with_suffix('.pending');write_archive(pending,entries)
    with zipfile.ZipFile(pending) as z:
        assert z.testzip() is None
        for n,h in hashes.items():assert sha(z.read(n))==h,n
    pending.replace(target);report=dict(path=target.name,bytes=target.stat().st_size,sha256=sha(target.read_bytes()),entries=len(entries),dlls={n:h for n,h in hashes.items() if n.endswith('.dll')})
    (REPORT/f'{a.edition}.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8');print(json.dumps(report,ensure_ascii=False))
if __name__=='__main__':main()
