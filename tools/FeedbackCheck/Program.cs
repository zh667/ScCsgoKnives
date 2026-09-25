using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using Game;
Engine.Dispatcher.Initialize();
var cases=new List<object>();int failed=0;
void T(string name,Func<bool> test){try{bool ok=test();cases.Add(new{name,ok});if(!ok){failed++;Console.WriteLine("FAIL "+name);}}catch(Exception e){failed++;cases.Add(new{name,ok=false,detail=e.ToString()});Console.WriteLine("FAIL "+name+": "+e.Message);}}
var groups=new Dictionary<int,string[]>{[2]=["glock18","hkp2000","p250","usp_silencer"],[3]=["nova","sawedoff","elite"],[4]=["mac10","ump45"],[5]=["fiveseven","tec9","cz75a","deagle","revolver"],[6]=["mp9","mp7","mp5sd","bizon","mag7"],[8]=["p90","xm1014"],[12]=["galilar","famas"],[14]=["ak47","m4a4","m4a1s"],[16]=["aug","sg556","ssg08"],[18]=["awp","m249","negev"],[20]=["scar20","g3sg1","taser"]};
T("all-35-guns-once",()=>groups.Values.SelectMany(v=>v).Distinct().Count()==35);
foreach(var g in groups)foreach(string name in g.Value)T("craft/"+name,()=>ScWeaponCrafting.All.Single(e=>!e.Knife&&e.Name==name).Level==g.Key);
foreach(string gun in new[]{"nova","sawedoff","mac10","ump45"})T("early-cost/"+gun,()=>{var e=ScWeaponCrafting.All.Single(e=>e.Name==gun);return e.B==5&&e.H==1&&e.M==(gun is "nova" or "sawedoff"?1:2);});
foreach(int fps in new[]{30,60,120})foreach(int kind in Enumerable.Range(0,6))foreach(bool low in new[]{false,true})foreach(bool draw in new[]{false,true})foreach(bool hold in new[]{false,true}){
    T($"quick/{fps}/{kind}/{low}/{draw}/{hold}",()=>{
        string asset=ScGrenadeBlock.Assets[kind],alias=low?"throwLow":"throwHigh";
        float pull=Cs2Rig.Duration(asset,"pullpin"),release=Cs2Rig.GrenadeReleaseTime(asset,alias),duration=Cs2Rig.Duration(asset,alias);
        var timeline=ScGrenadePreparation.Create(0,pull,release,duration,true,kind==3,draw?.15:0);
        double released=-1;int commits=0;bool committed=false;
        for(int i=0;i<fps*2;i++){double now=(double)i/fps;timeline.Step(now,hold&&now<.75);
            if(!committed&&now>=timeline.ReleaseAt){released=now;committed=true;commits++;}
            if(Math.Abs(now-timeline.ReleaseAt)<1e-8&&Math.Abs(timeline.ClipElapsed(now,pull,release,duration)-release)>.001)return false;
        }
        double limit=hold?1.5+release:pull+release+2d/fps;
        return commits==1&&released<=limit&&released>0&&timeline.EndAt-timeline.ThrowStartedAt>=duration-.001;
    });
}
T("legacy-preparation-retains-wait",()=>{var p=ScGrenadePreparation.Create(0,.9666f,.06667f,.7666f,false,false);p.Step(.5,false);if(p.Throwing)return false;p.Step(1,false);return Math.Abs(p.ReleaseAt-1.06667)<.0001;});
T("voice-default-binding-no-camera-conflict",()=>ScGunBindings.Default(ScGunFunctions.Voice)=="Z"&&ScGunBindings.Conflict(ScGunFunctions.Voice,ScGunFunctions.C4Timer));
var clips=JsonSerializer.Deserialize<AgentVoiceClip[]>(File.ReadAllText("src/ScCsgoVoice/Assets/ScAgentVoices.json"));
T("voice-count-language-pairs",()=>clips.Length==204&&clips.GroupBy(c=>c.Id).All(g=>g.Count()==2&&g.Select(c=>c.Language).Order().SequenceEqual(new[]{"en","zh"})));
T("voice-cache-budget",()=>clips.Sum(c=>c.Duration*32000*2)<32*1024*1024);
foreach(var clip in clips)T("voice-decode/"+clip.Language+"/"+clip.Id,()=>{
    string path="src/ScCsgoVoice/Assets/"+clip.Resource+".ogg";
    using var stream=File.OpenRead(path);var sound=Engine.Media.SoundData.Load(stream);return sound.ChannelsCount==1&&sound.SamplingFrequency==32000&&sound.Data.Length>0;
});
foreach(string mesh in new[]{"ct_default","glove_sporty","glove_specialist","glove_slick"})T("ct-bodygroup/"+mesh,()=>{
    var old=TacticalArms.Mesh(mesh);var covered=TacticalArms.Mesh("ct_covered_"+mesh);
    return old.Primitives.Single(p=>p.Material=="bare_arm_133").Indices.Length/3==1216&&covered.Primitives.All(p=>p.Material!="bare_arm_133")
        &&old.Joints.SequenceEqual(covered.Joints)&&old.InverseBind.SequenceEqual(covered.InverseBind)
        &&covered.Primitives.All(p=>old.Primitives.Single(o=>o.Material==p.Material).Indices.SequenceEqual(p.Indices));
});
var report=new{count=cases.Count,failed,passed=cases.Count-failed,dlls=new[]{typeof(GunSpec).Assembly,typeof(TacticalArms).Assembly,typeof(AgentVoiceOptions).Assembly}.ToDictionary(a=>a.GetName().Name,a=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))).ToLowerInvariant()),cases};
File.WriteAllText(args.Length>0?args[0]:".tmp/feedback-check.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"FeedbackCheck {report.passed}/{report.count}, failed={failed}");return failed==0?0:1;
