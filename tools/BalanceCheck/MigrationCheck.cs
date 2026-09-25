using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using System.IO.Compression;
using Game;
using TemplatesDatabase;

static class MigrationCheck {
    static readonly CultureInfo CI=CultureInfo.InvariantCulture;
    static object Call(Type t,object instance,string name,params object[] args) => t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance)
        .Single(m=>m.Name==name&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>p.ParameterType.IsInstanceOfType(args[i])).All(x=>x)).Invoke(instance,args);
    static Dictionary<string,string> Fields(string row)=>row.Split(',').Select(p=>p.Split('=')).ToDictionary(p=>p[0],p=>p[1]);
    static ValuesDictionary Read(XElement xml) {var d=new ValuesDictionary();d.ApplyOverrides(XElement.Parse(xml.ToString()));return d;}
    static XElement Xml(ValuesDictionary d) {var xml=new XElement("Values");d.Save(xml);return xml;}
    static XElement Group(string n, params object[] children)=>new("Values",new XAttribute("Name",n),children);
    static XElement Value(string n, int v)=>new("Value",new XAttribute("Name",n),new XAttribute("Type","int"),new XAttribute("Value",v));
    public static void Run(string legacyRoot, Action<string,bool,string> check) {
        foreach(int package in new[]{0,1}) {
            string label=package==0?"1.2.0":"1.0.0";
            try {
                var old=new LegacyContext(label).LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(legacyRoot,package.ToString(),"ScCsgoKnives.dll")));
                var reg=old.GetType("Game.ScGunRegistry");var growth=old.GetType("Game.ScGunGrowth");var spec=old.GetType("Game.GunSpec");
                int schema=(int)reg.GetField("Schema").GetRawConstantValue(), rules=(int)growth.GetField("RulesVersion").GetRawConstantValue();
                var guns=(Array)spec.GetField("All").GetValue(null);var skins=(Array)old.GetType("Game.ScGunSkinCatalog").GetField("All").GetValue(null);
                check(label+"/actual-schema",schema==(package==0?6:4)&&rules==(package==0?7:2)&&guns.Length==GunSpec.All.Length,$"schema={schema}, rules={rules}");
                var fixture=new XElement("LegacyFixture",new XAttribute("Version",label));
                for(int variant=0;variant<guns.Length;variant++) {
                    var gun=guns.GetValue(variant);string asset=(string)gun.GetType().GetField("Name").GetValue(gun);
                    check(label+"/id/"+variant,asset==GunSpec.All[variant].Name,asset);
                    int[] paints=[0,..skins.Cast<object>().Where(s=>(string)s.GetType().GetProperty("Gun").GetValue(s)==asset).Select(s=>(int)s.GetType().GetProperty("PaintId").GetValue(s))];
                    var source=Activator.CreateInstance(reg);
                    reg.GetField("GrowthMode").SetValue(source,Enum.Parse(reg.GetField("GrowthMode").FieldType,"CountAndGrow"));
                    foreach(int level in package==0?new[]{0,1,9,10,19,20,29,30,40,50}:new[]{0,1,9,10,19,20,29,30})
                    foreach(int wear in new[]{0,1,2}) foreach(int ammo in new[]{0,1,2}) foreach(bool silencer in new[]{false,true}) {
                        int cap=(int)Call(growth,null,"Capacity",variant,level), max=(int)Call(growth,null,"MaxDurability",variant,level);
                        // Capacity has a GunSpec overload with the same arity in some published versions.
                        int id=(int)Call(reg,source,"Allocate",variant,0,silencer,max,max,paints[(level+wear+ammo)%paints.Length]);
                        object r=Call(reg,source,"Get",id);float cycle=(float)Call(growth,null,"RechargeSeconds",gun,level);
                        long kills=(long)(package==0?growth.GetMethod("KillsFor",[typeof(int),typeof(int)]).Invoke(null,[variant,level]):growth.GetMethod("KillsFor",[typeof(int)]).Invoke(null,[level]));
                        foreach(var pair in new Dictionary<string,object>{{"Rounds",ammo==0?0:ammo==1?Math.Max(1,cap/2):cap},{"Durability",wear==0?0:wear==1?Math.Max(1,max/2):max},
                            {"Revision",17},{"CounterInstalled",true},{"KillCount",kills},{"AppliedGrowthLevel",level},{"PendingGrowthLevel",-1},{"GrowthRulesVersion",rules},
                            {"RechargeReadyAt",cycle>0?10d+cycle*.6:-1d},{"RechargeCycleSeconds",cycle},{"ReserveOverflowRounds",7}}) r.GetType().GetField(pair.Key).SetValue(r,pair.Value);
                    }
                    var data=(ValuesDictionary)Call(reg,source,"Save",10d);var xml=Xml(data);string original=xml.ToString();fixture.Add(new XElement("Gun",new XAttribute("Name",asset),xml));
                    var loaded=ScGunRegistry.Load(Read(xml),100);var before=data.GetValue<ValuesDictionary>("Records");
                    string first=null;
                    for(int round=0;round<2;round++) {
                        var saved=loaded.Save(100);var rows=saved.GetValue<ValuesDictionary>("Records");
                        foreach(var pair in before) {
                            var a=Fields((string)pair.Value);var b=Fields(rows.GetValue<string>(pair.Key,""));
                            bool same=new[]{"v","s","p","ct","k","n","c","rc"}.All(k=>a[k]==b[k]);
                            if(package==0) same&=(string)pair.Value==rows.GetValue<string>(pair.Key);
                            else {
                                int l=int.Parse(b["gl"]), oldLevel=int.Parse(a["gl"]);long kills=long.Parse(a["k"]);
                                double oldPower=1+.10*Math.Min(oldLevel,10)+.30*Math.Clamp(oldLevel-10,0,10)+.50*Math.Clamp(oldLevel-20,0,10);
                                int equivalent=Enumerable.Range(0,51).First(x=>ScGunGrowth.DamageMultiplier(x)+1e-5>=oldPower);
                                int expected=Math.Max(equivalent,ScGunGrowth.LevelFor(variant,kills));
                                int max=ScGunGrowth.MaxDurability(variant,expected);
                                same&=l==expected&&b["gp"]=="-1"&&b["gv"]=="7"&&int.Parse(b["m"])==max
                                    &&int.Parse(b["d"])==ScGunGrowth.ScaleDurability(int.Parse(a["d"]),int.Parse(a["m"]),max)
                                    &&int.Parse(a["r"])+int.Parse(a["ov"])==int.Parse(b["r"])+int.Parse(b["ov"])
                                    &&long.Parse(b["kc"])==Math.Max(0,ScGunGrowth.KillsFor(variant,l)-kills);
                            }
                            check($"{label}/state/{asset}/{pair.Key}/{round}",same,$"before={pair.Value}; after={rows.GetValue<string>(pair.Key,"")}");
                        }
                        check($"{label}/watermark/{asset}/{round}",rows.Count==before.Count&&loaded.QuarantinedCount==0&&saved.GetValue<int>("Next")==data.GetValue<int>("Next"),"count, allocation watermark, no quarantine");
                        string stable=Xml(saved).ToString(); if(first!=null)check($"{label}/idempotent/{asset}",stable==first,"second save is identical");first=stable;
                        loaded=ScGunRegistry.Load(Read(Xml(saved)),100);
                    }
                    var expectedCurrent=loaded.Save(100).GetValue<ValuesDictionary>("Records");
                    foreach(bool creative in new[]{false,true}) {
                        string[] carried=["1",(before.Count/2).ToString(),before.Count.ToString()];
                        var table=new XElement(xml);table.SetAttributeValue("Name","GunRegistry");var slots=Group("Slots");
                        for(int slot=0;slot<carried.Length;slot++) {
                            int value=Terrain.MakeBlockValue(302,0,(int)Call(spec,null,"WithId",variant,int.Parse(carried[slot])));
                            var item=Group(slot.ToString(),Value("Contents",value));if(!creative)item.Add(Value("Count",1));slots.Add(item);
                        }
                        var origin=new XElement("Project",new XElement("Subsystems",Group("BlocksManager",new XElement("Value",new XAttribute("Name","302"),new XAttribute("Type","string"),new XAttribute("Value","ScGunBlock"))),Group("ScGunBlockBehavior",Value("GunDataLayout",5),table)),
                            new XElement("Entities",new XElement("Entity",new XAttribute("Name","MalePlayer"),Group(creative?"CreativeInventory":"Inventory",slots))));
                        Call(old.GetType("Game.ScGunTravel"),null,"Capture",origin,"data:/Worlds/OldBalance");
                        var destination=new XElement("Project",new XElement("Subsystems",new XElement(origin.Element("Subsystems").Elements().First()),Group("ScGunBlockBehavior",Value("GunDataLayout",5),
                            Group("GunRegistry",Value("Schema",6),Value("Next",1),Group("Records")))),new XElement(origin.Element("Entities")));
                        string unchanged=destination.ToString();var transfer=ScGunTravel.Prepare(destination,"data:/Worlds/OldBalance/Other");
                        var imported=transfer.Document.Descendants("Values").Single(e=>(string)e.Attribute("Name")=="GunRegistry");var rows=Read(imported).GetValue<ValuesDictionary>("Records");
                        bool ok=transfer.Guns==3&&rows.Count==3&&unchanged==destination.ToString();
                        var importedSlots=transfer.Document.Element("Entities").Descendants("Values").Single(e=>(string)e.Attribute("Name")=="Slots").Elements().ToArray();
                        for(int i=0;i<3;i++){int value=int.Parse((string)importedSlots[i].Elements("Value").Single(e=>(string)e.Attribute("Name")=="Contents").Attribute("Value"));
                            ok&=rows.GetValue<string>(GunSpec.GetId(Terrain.ExtractData(value)).ToString())==expectedCurrent.GetValue<string>(carried[i]);}
                        check($"{label}/actual-travel/{asset}/{creative}",ok,"old DLL travel capture -> new detached import; remapped references, complete rows, no source edits");
                    }
                    check(label+"/source-unchanged/"+asset,original==Xml(data).ToString(),"old serializer XML never mutated");
                }
                // CountOnly must not retroactively turn counted kills into earned levels or full durability.
                var counting=Activator.CreateInstance(reg);reg.GetField("GrowthMode").SetValue(counting,Enum.Parse(reg.GetField("GrowthMode").FieldType,"CountOnly"));
                Call(reg,counting,"Allocate",0,7,true,750,1500,0);var cr=Call(reg,counting,"Get",1);
                cr.GetType().GetField("CounterInstalled").SetValue(cr,true);cr.GetType().GetField("KillCount").SetValue(cr,3000L);
                var countData=(ValuesDictionary)Call(reg,counting,"Save",0d);fixture.Add(new XElement("CountOnly",Xml(countData)));
                for(int n=0;n<2;n++) {countData=ScGunRegistry.Load(Read(Xml(countData)),0).Save(0);var f=Fields(countData.GetValue<ValuesDictionary>("Records").GetValue<string>("1"));
                    check($"{label}/count-only/{n}",f["k"]=="3000"&&f["gl"]=="0"&&f["d"]=="750"&&f["m"]=="1500"&&f["r"]=="7","count, wear and ammo preserved");}
                // Small immutable fixtures contain actual old serialized rows, no proprietary DLL/assets.
                string fp=Path.Combine("tools","fixtures","balance-20260924",label+".xml.gz");Directory.CreateDirectory(Path.GetDirectoryName(fp));
                if(File.Exists(fp)) {using var f=File.OpenRead(fp);using var z=new GZipStream(f,CompressionMode.Decompress);check(label+"/frozen-fixture",XNode.DeepEquals(XElement.Load(z),fixture),"regenerated with actual historical DLL");}
                else {using var f=File.Create(fp);using var z=new GZipStream(f,CompressionLevel.SmallestSize);fixture.Save(z);}
                BackupCheck(label,schema,fixture.Element("Gun").Element("Values"),check);
            } catch(Exception e) {check(label+"/exception",false,e.GetBaseException().ToString());}
        }
    }
    static void BackupCheck(string label,int schema,XElement saved,Action<string,bool,string> check) {
        string dir=Path.Combine(Path.GetTempPath(),"balance-world-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var table=new XElement(saved);table.SetAttributeValue("Name","GunRegistry");var project=new XElement("Project",new XElement("Subsystems",Group("ScGunBlockBehavior",Value("GunDataLayout",5),table)));
        project.Save(Path.Combine(dir,"Project.xml"));File.WriteAllBytes(Path.Combine(dir,"Chunks.dat"),[1,2,3,4,5]);
        Directory.CreateDirectory(Path.Combine(dir,"ThirdParty"));File.WriteAllText(Path.Combine(dir,"ThirdParty","state.txt"),"preserve me");
        byte[] original=File.ReadAllBytes(Path.Combine(dir,"Project.xml"));
        var info=(WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));info.DirectoryName=dir;
        string backup=ScGunSchemaUpgrade.BeforeLoad(project,info);
        check(label+"/schema-upgrade-no-automatic-backup",backup==null&&Directory.GetFiles(dir,"*.snapshot").Length==0,"manual backup policy; old schema still accepted");
        info.DirectoryName=Path.Combine(dir,"missing");
        check(label+"/no-backup-directory-required",ScGunSchemaUpgrade.BeforeLoad(project,info)==null&&!Directory.Exists(info.DirectoryName),"no filesystem writes or backup prerequisite");
        check(label+"/disk-source-untouched",original.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"Project.xml"))),"generated world only; never player worlds");
    }
    sealed class LegacyContext(string name):AssemblyLoadContext(name) {protected override Assembly Load(AssemblyName name)=>null;}
}
