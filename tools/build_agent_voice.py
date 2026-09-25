"""Curated SAS/Phoenix bilingual voice addon; input ZIPs are never modified."""
from pathlib import Path
import zipfile,re,json,hashlib,subprocess,concurrent.futures,wave,io
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'src/ScCsgoVoice/Assets/Audio/ScCsgoVoice'
EVENTS=[('affirmative','回应','收到'),('negative','回应','否定'),('thanks','回应','谢谢'),
 ('radio.followme','战术','跟我来'),('radio.enemyspotted','战术','发现敌人'),('radio.needbackup','战术','请求支援'),
 ('inposition','战术','已就位'),('radio.letsgo','战术','出发'),('followingfriend','战术','跟随'),('waitinghere','战术','等待'),
 ('enemydown','情绪','敌人已倒下'),('radio.takingfire','战术','遭到攻击'),
 ('grenade','投掷','投掷手雷'),('flashbang','投掷','投掷闪光弹'),('smoke','投掷','投掷烟雾弹'),('molotov','投掷','投掷燃烧物'),('decoy','投掷','投掷诱饵')]
def main():
    OUT.mkdir(parents=True,exist_ok=True);stage=ROOT/'.tmp/agent-voice';stage.mkdir(parents=True,exist_ok=True)
    sources=[('zh',Path(r'D:\下载\中文语音包(2).zip')),('en',Path(r'D:\下载\CSGO AGENT VOICE.zip'))]
    jobs=[];metadata=[];ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
    for lang,p in sources:
        with zipfile.ZipFile(p) as z:
            files={i.filename.split('/vo/',1)[-1] if '/vo/' in i.filename else i.filename[3:]:i for i in z.infolist() if i.filename.endswith('.wav') and ('/vo/' in i.filename or i.filename.startswith('vo/'))}
            for role,speaker in [('ct','sas'),('t','phoenix')]:
                for event,category,label in EVENTS:
                    prefix=role+'_'+event if category=='投掷' else event
                    if role=='t':prefix=prefix.replace('radio.','radio_')
                    candidates=sorted(n for n in files if re.fullmatch(re.escape(speaker+'/'+prefix)+r'\d+\.wav',n))[:3]
                    if len(candidates)!=3:raise ValueError((role,event,candidates))
                    for idx,n in enumerate(candidates):
                        data=z.read(files[n]);source=stage/f'{lang}-{role}-{event}-{idx}.wav';source.write_bytes(data)
                        with wave.open(io.BytesIO(data)) as w:duration=w.getnframes()/w.getframerate()
                        clip=f'{role}/{event}-{idx+1}';dest=OUT/lang/(clip+'.ogg');dest.parent.mkdir(parents=True,exist_ok=True)
                        record=dict(Id=clip,Role=role,Language=lang,Event=event,Category=category,Label=label+f' · {idx+1}',
                                    Resource='Audio/ScCsgoVoice/'+lang+'/'+clip,Duration=duration,Source=n,SourceSha256=hashlib.sha256(data).hexdigest())
                        jobs.append((source,dest,record));metadata.append(record)
    def convert(job):
        source,dest,record=job
        subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-y','-i',str(source),'-vn','-af','loudnorm=I=-18:TP=-1:LRA=11','-ac','1','-ar','32000','-c:a','libvorbis','-b:a','48k',str(dest)],check=True)
        record['Sha256']=hashlib.sha256(dest.read_bytes()).hexdigest();record['Bytes']=dest.stat().st_size
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:list(pool.map(convert,jobs))
    assert len(metadata)==204
    assert sum(r['Duration']*32000*2 for r in metadata)<32*1024*1024
    target=ROOT/'src/ScCsgoVoice/Assets/ScAgentVoices.json';target.write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n','utf8')
    summary=dict(clips=len(metadata),audio_bytes=sum(r['Bytes'] for r in metadata),seconds=sum(r['Duration'] for r in metadata),decoded_pcm_bytes=int(sum(r['Duration']*32000*2 for r in metadata)),format='Ogg Vorbis mono 32000Hz, target 48kbps, loudnorm -18LUFS/-1dBTP',
                 source_archives=[dict(file=p.name,sha256=hashlib.file_digest(p.open('rb'),'sha256').hexdigest()) for _,p in sources])
    (stage/'report.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),'utf8');print(json.dumps(summary,ensure_ascii=False))
if __name__=='__main__':main()
