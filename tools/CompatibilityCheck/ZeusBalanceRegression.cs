using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Game;

// Compare released binaries: the original 1.2.0 for Zeus, the previous latest for every other gun.
static class ZeusBalanceRegression {
    public static int Run(string previousPath,string historicalPath,string currentPath,string reportPath) {
        var previous=new Module(previousPath,"previous");
        var historical=new Module(historicalPath,"original-1.2.0");
        var current=new Module(currentPath,"current");
        var failures=new List<string>();int count=0;
        var options=new JsonSerializerOptions{IncludeFields=true};
        string Snapshot(object value)=>JsonSerializer.Serialize(value,value.GetType(),options);
        void Check(string name,bool ok){count++;if(!ok)failures.Add(name);}
        Array Guns(Module m)=>(Array)m.Type("GunSpec").GetField("All").GetValue(null);
        var oldGuns=Guns(previous);var originalGuns=Guns(historical);var newGuns=Guns(current);
        Check("35-stable-weapon-ids",oldGuns.Length==35&&newGuns.Length==35&&originalGuns.Length==35);
        var milestones=new List<object>();
        for(int v=0;v<newGuns.Length;v++){
            object gun=newGuns.GetValue(v);string name=(string)current.Get(gun,"Name");
            var reference=name=="taser"?historical:previous;
            object oldGun=(name=="taser"?originalGuns:oldGuns).GetValue(v);
            Check(name+"/base-spec",Snapshot(gun)==Snapshot(oldGun));
            for(int level=0;level<=50;level++){
                int value=Terrain.MakeBlockValue(512,0,v);
                foreach(bool enabled in new[]{false,true})foreach(bool alternate in new[]{false,true}){
                    foreach(var m in new[]{reference,current})m.Type("ScGunplaySettings").GetField("Enabled").SetValue(null,enabled);
                    var actual=current.Call("EffectiveGunStats",null,"ResolveLevel",gun,value,alternate,level);
                    var expected=reference.Call("EffectiveGunStats",null,"ResolveLevel",oldGun,value,alternate,level);
                    Check($"{name}/Lv{level}/handling-{enabled}/alternate-{alternate}",Snapshot(actual)==Snapshot(expected));
                }
                Check($"{name}/Lv{level}/growth-rate",Equals(current.Call("ScGunGrowth",null,"FireRateMultiplier",v,level),reference.Call("ScGunGrowth",null,"FireRateMultiplier",v,level)));
                if(name=="taser"&&level%10==0){
                    var stats=current.Call("EffectiveGunStats",null,"ResolveLevel",gun,value,false,level);
                    milestones.Add(new{level,power=current.Get(stats,"Power"),seconds=current.Get(stats,"RechargeSeconds"),rpm=current.Call("ScGunAttributes",null,"EffectiveRpm",stats)});
                }
            }
        }
        var modules=new[]{previous,historical,current}.Select(m=>new{m.Name,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(m.Path))).ToLowerInvariant()});
        var report=new{count,failed=failures.Count,passed=count-failures.Count,modules,milestones,failures};
        File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Zeus isolation: {report.passed}/{count}, failed={report.failed}");
        foreach(string failure in failures.Take(20))Console.WriteLine("FAIL "+failure);
        return failures.Count==0?0:1;
    }
}
