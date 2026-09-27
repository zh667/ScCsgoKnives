"""Separate appended phone launches/worlds before comparing performance evidence."""
import argparse,json,re,hashlib
from pathlib import Path
from analyze_mobile_perf_log import analyze,values

def main():
    p=argparse.ArgumentParser();p.add_argument("log",type=Path);p.add_argument("output",type=Path);a=p.parse_args()
    raw=a.log.read_bytes();lines=raw.decode("utf-8-sig").splitlines();data=analyze(a.log)
    launch=0;world=0;by_line={};resources=[];long_frames=[];errors=[]
    for i,line in enumerate(lines,1):
        if "Survivalcraft starting up" in line:launch+=1;world=0
        if "[CS_PERF] build=" in line:world+=1
        by_line[i]=(launch,world)
        item=dict(line=i,time=line.split()[0],launch=launch,world=world)
        if "[CS_RESOURCE]" in line:resources.append(dict(**item,**values(line)))
        if "[CS_PERF] long-frame" in line:long_frames.append(dict(**item,text=line,**values(line.split(" | ")[0])))
        if "ERROR:" in line:
            stack=[];k=i
            while k<len(lines) and lines[k].startswith("   at "):stack.append(lines[k]);k+=1
            errors.append(dict(**item,text=line,stack=stack))
    for key in ["windows","spawns","warmups"]:
        for row in data[key]:
            row["launch"],row["world"]=by_line[row["line"]]
            if key=="windows":
                b=row["stages"].get("Bones",{})
                row["boneMsPerCall"]=b.get("totalMs",0)/max(1,b.get("n",0))
                if "renderBones" in row:
                    actual,total=map(int,row["renderBones"].split("/"))
                    row["renderBoneFraction"]=actual/total if total else None
                row["uncoveredWallMs"]=row["windowMs"]-row["frames"]*row["engineAvgMs"]
    data["resources"]=resources;data["longFrames"]=long_frames;data["errorsWithStacks"]=errors
    data["capture"]=dict(method="adb pull, USB-authorized read-only copy",remote="/sdcard/Survivalcraft2.4_API1.9/Bugs/Game.log",
                         cutoff=lines[-1].split()[0],sha256=hashlib.sha256(raw).hexdigest(),
                         note="Live append log; fixed snapshot, not continuous monitoring. launch/world IDs separate restarts and reentries.")
    data["launches"]=[]
    for n in sorted({r["launch"] for r in data["windows"]}):
        windows=[r for r in data["windows"] if r["launch"]==n];spawns=[r for r in data["spawns"] if r["launch"]==n]
        textures=[r["ms"] for r in resources if r["launch"]==n and r.get("stage")=="texture"]
        bones=[r["boneMsPerCall"] for r in windows if r["stages"].get("Bones",{}).get("n",0)>1000]
        data["launches"].append(dict(launch=n,worlds=sorted({r["world"] for r in windows}),windows=len(windows),
            frames=sum(r["frames"] for r in windows),spawnDetails=len(spawns),spawnFailed=sum(not r["success"] for r in spawns),
            overThresholds=[sum(r["overThresholdCounts"][i] for r in windows) for i in range(3)],
            boneMsPerCallRange=[min(bones),max(bones)] if bones else None,
            textureLoggedCount=len(textures),textureLoggedMs=sum(textures),
            textureLoggedRange=[min(textures),max(textures)] if textures else None,
            managedMiBRange=[min(r["managedMB"] for r in windows),max(r["managedMB"] for r in windows)]))
    a.output.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n","utf8")
    print(json.dumps(data["launches"],ensure_ascii=False,indent=2))
    for r in data["windows"]:
        if r["launch"]>1:print(r["time"],"NPC",r["enemies"],"fps",round(r["approxFpsFromMeanFrame"],1),"bones/call",round(r["boneMsPerCall"],4),"ratio",round(r.get("renderBoneFraction",0),3))

if __name__=="__main__":main()
