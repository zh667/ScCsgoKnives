"""Read-only planning evidence: audio archive inventory, WAV headers, and video frame samples."""
from pathlib import Path
from collections import Counter
import json,re,zipfile,wave,cv2
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/player-feedback-20260925';OUT.mkdir(exist_ok=True)
def inspect(path):
    result=dict(file=path.name,archive_bytes=path.stat().st_size)
    with zipfile.ZipFile(path) as z:
        infos=z.infolist();audio=[i for i in infos if i.filename.lower().endswith('.wav')]
        result.update(entries=len(infos),wav_count=len(audio),unpacked_bytes=sum(i.file_size for i in infos))
        rows=[];headers=Counter();errors=[]
        for i in audio:
            name=i.filename.replace('\\','/');key=name.split('/vo/',1)[-1] if '/vo/' in name else name[3:] if name.startswith('vo/') else name
            parts=key.split('/');speaker=parts[0];event=re.sub(r'\d+$','',Path(parts[-1]).stem)
            row=dict(key=key,speaker=speaker,event=event,bytes=i.file_size)
            try:
                with z.open(i) as f, wave.open(f) as w:
                    row.update(seconds=w.getnframes()/w.getframerate(),rate=w.getframerate(),channels=w.getnchannels(),bits=w.getsampwidth()*8)
                    headers[f'{w.getframerate()}Hz/{w.getnchannels()}ch/{w.getsampwidth()*8}bit']+=1
            except Exception as e:errors.append(dict(key=key,error=str(e)))
            rows.append(row)
        result.update(speakers=dict(Counter(r['speaker'] for r in rows)),headers=dict(headers),header_errors=errors[:12],header_error_count=len(errors),
                      total_seconds=sum(r.get('seconds',0) for r in rows),events=dict(Counter(r['event'] for r in rows).most_common(70)))
        return result,rows
def main():
    zh,zrows=inspect(Path(r'D:\下载\中文语音包(2).zip'));en,erows=inspect(Path(r'D:\下载\CSGO AGENT VOICE.zip'))
    common=set(r['key'] for r in zrows)&set(r['key'] for r in erows)
    result=dict(chinese=zh,english=en,paired_relative_paths=len(common),
                notes='Languages named by input archives; header/path audit is not a full listening/translation check.',
                preferred_speakers={speaker:{lang:[r for r in rows if r['speaker']==speaker][:8] for lang,rows in [('zh',zrows),('en',erows)]} for speaker in ['sas','phoenix','leet','fbi','swat']})
    video=Path(r'D:\下载\QQ20260925-162738-HD.mp4');cap=cv2.VideoCapture(str(video));fps=cap.get(cv2.CAP_PROP_FPS);frames=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));duration=frames/fps
    times=[0,.5,1,1.5,2,2.5,3,3.5,4,4.5,5,5.5]
    sheet=Image.new('RGB',(768*3,450*4),(22,24,28));draw=ImageDraw.Draw(sheet)
    for index,t in enumerate(times):
        cap.set(cv2.CAP_PROP_POS_MSEC,t*1000);ok,frame=cap.read()
        if not ok:continue
        img=Image.fromarray(cv2.cvtColor(frame,cv2.COLOR_BGR2RGB));img.thumbnail((768,427));x=index%3*768;y=index//3*450
        sheet.paste(img,(x,y+22));draw.text((x+8,y+5),f'{t:.1f}s',fill='white')
    cap.release();sheet.save(OUT/'video-contact.jpg',quality=88)
    result['video']=dict(file=video.name,bytes=video.stat().st_size,fps=fps,frames=frames,duration_seconds=duration,frame_samples=times)
    catalog=json.loads((ROOT/'src/ScCsgoKnives/AnimationData/cs2_catalog.json').read_text('utf8'))
    result['grenade_timings']={n:[dict(alias=c.get('Alias'),duration=c['Duration'],throw_events=[dict(name=e.get('Name'),at=e.get('At')) for e in c.get('Events',[]) if e.get('Name','').lower().endswith('.throw')])
        for c in asset['Clips'].values() if c.get('Alias') in ('deploy','pullpin','throwHigh','throwLow')]
        for n,asset in catalog.items() if n.startswith('grenade_')}
    result['sas_phoenix']={lang:dict(count=sum(r['speaker'] in ('sas','phoenix') for r in rows),seconds=sum(r.get('seconds',0) for r in rows if r['speaker'] in ('sas','phoenix')))
        for lang,rows in [('zh',zrows),('en',erows)]}
    screenshot=Path(r'C:\Users\张衡\AppData\Local\Temp\codex-clipboard-fc6f8c66-a9da-4124-91ba-90e81233077b.png')
    if screenshot.exists():Image.open(screenshot).convert('RGB').crop((500,465,935,850)).save(OUT/'ct-heavy-arm-crop.jpg',quality=92)
    (OUT/'audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf8')
    # Inventory remains local; the compact report is suitable for the planning document.
    temp=ROOT/'.tmp/feedback-20260925';temp.mkdir(exist_ok=True)
    (temp/'voice-inventory.json').write_text(json.dumps(dict(zh=zrows,en=erows),ensure_ascii=False),'utf8')
    for title,data in [('zh',zh),('en',en)]:print(title,json.dumps({k:v for k,v in data.items() if k not in ('events','header_errors')},ensure_ascii=False))
    print('paired paths',len(common),'duration',duration)
if __name__=='__main__':main()
