using System.Reflection;
using System.Text.Json;
using System.IO.Compression;

/// <summary>Read actual packaged gameplay methods, not the unused CS reference damage field.</summary>
static class WeaponStatsExport {
    public static void Write(Assembly mod, string package, string output, string digest) {
        object Call(string type,string method,params object[] args) {
            var m=mod.GetType("Game."+type).GetMethods(BindingFlags.Public|BindingFlags.Static)
                .Single(m=>m.Name==method && m.GetParameters().Length==args.Length && m.GetParameters().Zip(args).All(p=>p.Second is null || p.First.ParameterType.IsInstanceOfType(p.Second)));
            return m.Invoke(null,args);
        }
        object Field(string type,string name)=>mod.GetType("Game."+type).GetField(name).GetValue(null);
        Dictionary<string,object> Properties(object obj)=>obj.GetType().GetProperties().Where(p=>p.Name!="Value").ToDictionary(p=>p.Name,p=>p.GetValue(obj));
        var recipes=((Array)Field("ScWeaponCrafting","All")).Cast<object>().ToArray();
        var guns=new List<object>();
        var all=((Array)Field("GunSpec","All")).Cast<object>().ToArray();
        for(int i=0;i<all.Length;i++) {
            var gun=all[i]; var type=gun.GetType();
            var row=type.GetFields(BindingFlags.Instance|BindingFlags.Public).ToDictionary(f=>f.Name,f=>f.GetValue(gun));
            string name=(string)row["Name"]; int variant=(int)Call("ScGunBlock","AssetIndex",i);
            var recipe=recipes.Single(r=>(string)r.GetType().GetProperty("Name").GetValue(r)==name);
            row["Variant"]=i; row["Power"]=Call("ScSurvivalBalance","Power",name);
            row["PelletPower"]=Call("ScSurvivalBalance","PelletPower",gun,0f);
            row["HeadMultiplier"]=Call("ScHeadshot","MultiplierFor",gun);
            row["Durability"]=Call("ScGunDurability","Full",name);
            row["Class"]=Call("ScGunDurability","ClassOf",name).ToString();
            row["Recipe"]=Properties(recipe);
            row["RepairFullCost"]=Call("ScWeaponRepair","FullCost",recipe);
            row["ReloadCost"]=Call("ScReloadTransaction","Required",gun);
            row["AmmoKind"]=Call("ScReloadTransaction","AmmoKind",gun);
            row["Tube"]=Call("ScReloadTransaction","IsTube",name);
            row["DeploySeconds"]=Call("CsmcKnifeRig","GetProfileDuration",variant,"deploy");
            row["ReloadSeconds"]=Call("KnifeAnimationController","ReloadSeconds",variant,false,(int)row["Magazine"]);
            row["EmptyReloadSeconds"]=Call("KnifeAnimationController","ReloadSeconds",variant,true,(int)row["Magazine"]);
            row["SpreadStanding"]=Call("Cs2Weapons","SpreadDegrees",name,false,0f,(float)row["SpreadDegrees"]);
            var cs=Call("Cs2Weapons","Get",name); row["OptionalCs2Numbers"]=Properties(cs);
            guns.Add(row);
        }
        using var zip=ZipFile.OpenRead(package);
        using var lang=zip.GetEntry("Assets/Lang/zh-CN.json").Open();
        var labels=JsonDocument.Parse(lang).RootElement.Clone();
        var skins=((Array)Field("ScGunSkinCatalog","All")).Cast<object>().Select(Properties).ToArray();
        var report=new { packageSha256=digest, gunNumbersDefault=Field("KnifeTuning","GunNumbers"), guns,
            recipes=recipes.Select(Properties).ToArray(),skins,labels,
            knife=new {light=Call("ScKnifeStrike","Power",false),heavy=Call("ScKnifeStrike","Power",true),
                lightRange=Call("ScKnifeStrike","Range",false),heavyRange=Call("ScKnifeStrike","Range",true),
                lightInterval=Call("ScKnifeStrike","Interval",false),heavyInterval=Call("ScKnifeStrike","Interval",true)} };
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
        Console.WriteLine($"Wrote {guns.Count} guns and {recipes.Length} assembly recipes from packaged DLL to {output}");
    }
}
