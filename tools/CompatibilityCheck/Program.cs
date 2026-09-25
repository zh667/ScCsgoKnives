using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using Game;
using GameEntitySystem;
using TemplatesDatabase;
using System.Collections;
using System.Security.Cryptography;

Engine.Dispatcher.Initialize();
if(args.Length!=4)throw new ArgumentException("CompatibilityCheck <1.0-compat.dll> <1.2-compat.dll> <latest.dll> <report.json>");
var checks=new List<object>();int failed=0;
void T(string name,Action test){try{test();checks.Add(new{name,ok=true});}catch(Exception e){failed++;checks.Add(new{name,ok=false,detail=e.GetBaseException().ToString()});Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
void Require(bool b,string m="assertion failed"){if(!b)throw new Exception(m);}
XElement Xml(ValuesDictionary v){var x=new XElement("Values");v.Save(x);return x;}
ValuesDictionary Read(XElement x){var v=new ValuesDictionary();v.ApplyOverrides(new XElement(x));return v;}
var modules=args.Take(3).Select((p,i)=>new Module(p,i.ToString())).ToArray();
var latest=modules[2];
foreach(var source in modules)foreach(var target in modules.Where(m=>m!=source)){
    string route=source.Name+"->"+target.Name;
    for(int variant=0;variant<35;variant++){
        int v=variant;
        T(route+"/state/"+v,()=>{
            object reg=source.New("ScGunRegistry");
            var records=new ValuesDictionary();int id=1;
            foreach(int level in new[]{0,1,10,20,30,40,50})foreach(int wear in new[]{0,1,2})foreach(int ammo in new[]{0,1,2}){
                int max=(int)source.Call("ScGunGrowth",null,"MaxDurability",v,level),cap=(int)source.Call("ScGunGrowth",null,"Capacity",v,level);
                int rid=(int)source.Call("ScGunRegistry",reg,"Allocate",v,0,false,max,max,0);
                object r=source.Call("ScGunRegistry",reg,"Get",rid);long kills=(long)source.Call("ScGunGrowth",null,"KillsFor",v,level);
                foreach(var pair in new Dictionary<string,object>{{"Rounds",ammo==0?0:ammo==1?Math.Max(1,cap/2):cap},{"Durability",wear==0?0:wear==1?max/2:max},
                    {"CounterInstalled",true},{"KillCount",kills},{"AppliedGrowthLevel",level},{"GrowthRulesVersion",7},{"GrowthKillCredit",37L},{"Revision",13},
                    {"RechargeReadyAt",v==34?107.25d:-1d},{"RechargeCycleSeconds",v==34?15.3846f:0f},{"ReserveOverflowRounds",3}})source.Set(r,pair.Key,pair.Value);
                id++;
            }
            var saved=(ValuesDictionary)source.Call("ScGunRegistry",reg,"Save",100d);string expected=Xml(saved).ToString();
            for(int pass=0;pass<2;pass++){
                var loaded=target.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),100d);
                Require((int)target.Get(loaded,"Count")==id-1&&(int)target.Get(loaded,"QuarantinedCount")==0,"lost record");
                saved=(ValuesDictionary)target.Call("ScGunRegistry",loaded,"Save",100d);Require(Xml(saved).ToString()==expected,"fields changed in no-op switch");
                loaded=source.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),100d);saved=(ValuesDictionary)source.Call("ScGunRegistry",loaded,"Save",100d);
                Require(Xml(saved).ToString()==expected,"return changed original state");
            }
        });
    }
    T(route+"/play-state-and-kill",()=>{
        var reg=source.New("ScGunRegistry");int id=(int)source.Call("ScGunRegistry",reg,"Allocate",0,7,false,900,1500,0);
        var r=source.Call("ScGunRegistry",reg,"Get",id);source.Set(r,"CounterInstalled",true);source.Set(r,"GrowthRulesVersion",7);
        var saved=(ValuesDictionary)source.Call("ScGunRegistry",reg,"Save",100d);
        reg=target.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),100d);target.Type("ScGunRegistry").GetField("Current").SetValue(null,reg);
        var inv=new Inventory();inv.values[0]=Terrain.MakeBlockValue(512,0,64);inv.counts[0]=1;
        object[] prepare=[inv,0,"matrix",null];var tx=target.Type("ScGunMutation").GetMethod("Prepare").Invoke(null,prepare);Require(tx!=null,"prepare failed");
        var action=typeof(Mutations).GetMethod("Shot").MakeGenericMethod(target.Type("ScGunRecord")).Invoke(null,null);
        object result=target.Call("ScGunMutation",tx,"Commit",action,0,0,null);Require(result.ToString()=="Success","shot failed");
        saved=(ValuesDictionary)target.Call("ScGunRegistry",reg,"Save",100d);
        reg=source.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),100d);r=source.Call("ScGunRegistry",reg,"Get",1);
        Require((int)source.Get(r,"Rounds")==6&&(int)source.Get(r,"Durability")==899&&(long)source.Get(r,"KillCount")==1,"play state not retained");
    });
    T(route+"/repair-skin-silencer-and-new-gun",()=>{
        var reg=target.New("ScGunRegistry");target.Type("ScGunRegistry").GetField("Current").SetValue(null,reg);
        int id=(int)target.Call("ScGunRegistry",reg,"Allocate",0,7,false,0,1500,0);
        var inv=new Inventory();inv.values[0]=Terrain.MakeBlockValue(512,0,id<<6);inv.counts[0]=1;
        object[] prepare=[inv,0,"matrix",null];var tx=target.Type("ScGunMutation").GetMethod("Prepare").Invoke(null,prepare);
        var action=typeof(Mutations).GetMethod("RepairSkin").MakeGenericMethod(target.Type("ScGunRecord")).Invoke(null,null);
        Require(target.Call("ScGunMutation",tx,"Commit",action,0,0,null).ToString()=="Success");
        int fresh=(int)target.Call("ScGunRegistry",reg,"Allocate",1,9,true,733,1500,0);
        var saved=(ValuesDictionary)target.Call("ScGunRegistry",reg,"Save",0d);
        reg=source.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),0d);var row=source.Call("ScGunRegistry",reg,"Get",id);
        Require((int)source.Get(row,"Rounds")==7&&(int)source.Get(row,"Durability")==1500&&(int)source.Get(row,"SkinId")==180&&(bool)source.Get(row,"SilencerOff"));
        row=source.Call("ScGunRegistry",reg,"Get",fresh);Require((int)source.Get(row,"Rounds")==9&&(int)source.Get(row,"Durability")==733);
    });
    T(route+"/growth-and-clone-do-not-inflate",()=>{
        var reg=target.New("ScGunRegistry");target.Type("ScGunRegistry").GetField("Current").SetValue(null,reg);
        reg.GetType().GetField("GrowthMode").SetValue(reg,Enum.Parse(reg.GetType().GetField("GrowthMode").FieldType,"CountAndGrow"));
        int id=(int)target.Call("ScGunRegistry",reg,"Allocate",0,7,false,750,1500,0);var r=target.Call("ScGunRegistry",reg,"Get",id);
        target.Set(r,"CounterInstalled",true);target.Set(r,"GrowthRulesVersion",7);target.Set(r,"KillCount",250L);target.Set(r,"PendingGrowthLevel",10);
        var inv=new Inventory();inv.values[0]=Terrain.MakeBlockValue(512,0,64);inv.counts[0]=1;
        object[] grow=[inv,0,"matrix",0d,0,0];var result=target.Type("ScGunGrowthService").GetMethod("ApplyPending").Invoke(null,grow);
        Require(result.ToString()=="Success");int clone=(int)target.Call("ScGunRegistry",reg,"Clone",id);
        var saved=(ValuesDictionary)target.Call("ScGunRegistry",reg,"Save",0d);var normal=Xml(saved).ToString();
        for(int pass=0;pass<2;pass++){
            reg=source.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),0d);r=source.Call("ScGunRegistry",reg,"Get",id);
            Require((int)source.Get(r,"AppliedGrowthLevel")==10&&(int)source.Get(r,"Durability")==1125&&(int)source.Get(r,"Rounds")==7);
            var c=source.Call("ScGunRegistry",reg,"Get",clone);Require((int)source.Get(c,"AppliedGrowthLevel")==0&&(long)source.Get(c,"KillCount")==0);
            saved=(ValuesDictionary)source.Call("ScGunRegistry",reg,"Save",0d);Require(Xml(saved).ToString()==normal);
        }
    });
    T(route+"/pending-kills-quarantine-and-full-watermark",()=>{
        var data=XElement.Parse("<Values><Value Name='Schema' Type='int' Value='6'/><Value Name='Next' Type='int' Value='1023'/><Values Name='Records'><Value Name='900' Type='string' Value='corrupt-preserve-exactly'/></Values><Values Name='PendingKills'><Value Name='Next' Type='string' Value='1'/><Values Name='Entries'/></Values></Values>");
        object reg=source.Call("ScGunRegistry",null,"Load",Read(data),0d);
        var queue=source.Get(reg,"Kills");queue.GetType().GetMethod("Enqueue").Invoke(queue,[900,0]);
        var saved=(ValuesDictionary)source.Call("ScGunRegistry",reg,"Save",0d);string before=Xml(saved).ToString();
        for(int n=0;n<2;n++){
            reg=target.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),0d);Require((int)target.Get(reg,"Next")==1023&&(int)target.Get(reg,"QuarantinedCount")==1);
            Require((int)target.Call("ScGunRegistry",reg,"Allocate",0,0,false,1500,1500,0)==-1);
            saved=(ValuesDictionary)target.Call("ScGunRegistry",reg,"Save",0d);Require(Xml(saved).ToString()==before);
        }
    });
}
foreach(var mod in modules){
    T(mod.Name+"/manual-backup-all-runtime-migration-paths",()=>{
        var dir=Directory.CreateTempSubdirectory("no-auto-backup-");
        var info=(WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));
        info.DirectoryName=dir.FullName;
        string oldPath=Path.Combine(dir.FullName,"existing.snapshot");File.WriteAllText(oldPath,"user-owned");
        var fixture=XElement.Load("tools/fixtures/migration-120-20260925/world7-guns.xml");
        foreach(int schema in new[]{4,6}) {
            var doc=new XElement(fixture);
            doc.Descendants("Values").Single(e=>(string)e.Attribute("Name")=="GunRegistry").Elements("Value").Single(e=>(string)e.Attribute("Name")=="Schema").SetAttributeValue("Value",schema);
            for(int round=0;round<2;round++) {
                Require(mod.Call("ScGunSchemaUpgrade",null,"BeforeLoad",doc,info)==null);
                Require(mod.Call("ScGunSchemaUpgrade",null,"BeforeReleaseLoad",doc,info,null)==null);
            }
        }
        for(int round=0;round<2;round++)Require(mod.Call("ScGunLoadIntegrity",null,"BeforeLoad",fixture,info,null)==null);
        var old=(XElement)mod.Call("ScGun0282MigrationSelfTest",null,"Fixture");
        mod.Call("ScGun0282Migration",null,"BeforeLoad",old,info);
        Require(old.Descendants("Values").Any(e=>(string)e.Attribute("Name")=="Official0282Migration"));
        var travel=(XElement)mod.Call("ScGun0282MigrationSelfTest",null,"Fixture");
        // Use a valid schema6 carried gun for an actual player-only cross-world import.
        var source=new XElement(fixture);var slots=source.Descendants("Values").Single(e=>(string)e.Attribute("Name")=="Slots");
        slots.Elements().Where(e=>(string)e.Attribute("Name") is "Slot10" or "Slot11").Remove();
        mod.Call("ScGunTravel",null,"Capture",source,"data:/Source");
        var target=new XElement(source);target.Element("Subsystems").Elements().Single(e=>(string)e.Attribute("Name")=="ScGunBlockBehavior").Remove();
        mod.Call("ScGunTravel",null,"BeforeLoad",target,info);
        Require(target.Descendants("Values").Any(e=>(string)e.Attribute("Name")=="GunRegistry"));
        Require(Directory.GetFiles(dir.FullName,"*",SearchOption.AllDirectories).Length==1&&File.ReadAllText(oldPath)=="user-owned","unexpected file creation/deletion");
    });
    T(mod.Name+"/preserves-all-later-item-type-identities",()=>{
        foreach(string name in new[]{"ScC4Block","ScChickenEggBlock","ScTacticalShieldBlock","ScTacticalBeaconBlock","ScTacticalDefuserBlock","ScTacticalSquadBlock"}){
            if(mod==latest&&name.StartsWith("ScTactical"))continue; // supplied by the bundled tactical assembly
            var type=mod.Type(name);Require(typeof(Block).IsAssignableFrom(type));
            var block=(Block)Activator.CreateInstance(type);
            if(type.BaseType.Name=="ScCompatibilityItemBlock")Require(!block.IsPlaceable&&block.GetDamage(1234)==0&&block.SetDamage(1234,99)==1234&&!block.GetCreativeValues().Any());
        }
    });
    T(mod.Name+"/opaque-entity-roundtrip",()=>{
        Guid guid=Guid.Parse("99b9cc00-c3c0-5483-8e8b-362afd6603a3");
        var manifest=new XElement("Compatibility",new XAttribute("Protocol",1),new XElement("Entity",new XAttribute("Name","ScTacticalCT"),new XAttribute("Guid",guid)),new XElement("Subsystem",new XAttribute("Name","ScTactical")));
        var entity=XElement.Parse("<Entity Id='42' Name='ScTacticalCT' Guid='99b9cc00-c3c0-5483-8e8b-362afd6603a3'><Values Name='TacticalInventory'><Values Name='Slots'><Values Name='Slot0'><Value Name='Contents' Type='int' Value='1049088'/><Value Name='Count' Type='int' Value='1'/></Values></Values></Values></Entity>");
        var world=new XElement("Project",new XElement("Subsystems",new XElement("Values",new XAttribute("Name","ScTactical"),new XElement("Value",new XAttribute("Name","FutureFlag"),new XAttribute("Type","string"),new XAttribute("Value","keep")))),new XElement("Entities",new XAttribute("NextID",43),entity));
        var plan=mod.Call("ScCompatibility",null,"Prepare",world,mod.Name,manifest,(Func<Guid,bool>)(_=>false));
        var doc=(XElement)plan.GetType().GetProperty("Document").GetValue(plan);Require((int)plan.GetType().GetProperty("Dormant").GetValue(plan)==1);
        var archived=doc.Element("Entities").Element("Entity");Require((string)archived.Attribute("Id")=="42"&&(int)doc.Element("Entities").Attribute("NextID")==43);
        var component=(Component)mod.New("ComponentScCompatibilityArchive");component.Load(Read(archived.Element("Values")),null);var values=new ValuesDictionary();component.Save(values,null);
        var componentXml=Xml(values);componentXml.SetAttributeValue("Name","ScCompatibilityArchive");archived.ReplaceNodes(componentXml);
        Require(((IInventory)component).GetSlotValue(0)==1049088&&((IInventory)component).GetSlotCapacity(0,1)==0);
        var state=doc.Element("Subsystems").Elements().Single(e=>(string)e.Attribute("Name")=="ScCompatibility");
        var subsystem=(Subsystem)mod.New("SubsystemScCompatibility");subsystem.Load(Read(state));var persisted=new ValuesDictionary();subsystem.Save(persisted);
        var stateXml=Xml(persisted);stateXml.SetAttributeValue("Name","ScCompatibility");state.ReplaceWith(stateXml);
        doc.Element("Subsystems").Elements().Where(e=>(string)e.Attribute("Name")=="ScTactical").Remove();mod.Call("ScCompatibility",null,"PreserveOpaque",doc);
        Require(doc.Element("Subsystems").Elements().Any(e=>(string)e.Attribute("Name")=="ScTactical"));
        plan=latest.Call("ScCompatibility",null,"Prepare",XElement.Parse(doc.ToString()),"latest",manifest,(Func<Guid,bool>)(_=>true));
        var restored=(XElement)plan.GetType().GetProperty("Document").GetValue(plan);Require(XNode.DeepEquals(restored.Element("Entities").Element("Entity"),entity),"dormant payload changed");
    });
}
var report=new{failed,count=checks.Count,passed=checks.Count-failed,modules=modules.Select(m=>new{m.Name,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(m.Path))).ToLowerInvariant()}),checks};
File.WriteAllText(args[3],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"CompatibilityCheck {report.passed}/{report.count}, failed={failed}");return failed==0?0:1;

sealed class Module {
    public readonly string Name,Path;readonly Assembly assembly;
    public Module(string path,string name){Path=System.IO.Path.GetFullPath(path);Name=name;assembly=new Context(Path).LoadFromAssemblyPath(Path);}
    public Type Type(string n)=>assembly.GetType("Game."+n,true);
    public object New(string n)=>Activator.CreateInstance(Type(n));
    public object Call(string n,object obj,string method,params object[] args)=>Type(n).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).Single(m=>m.Name==method&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(b=>b)).Invoke(obj,args);
    public void Set(object r,string n,object v)=>r.GetType().GetField(n).SetValue(r,v);
    public object Get(object r,string n)=>r.GetType().GetField(n)?.GetValue(r)??r.GetType().GetProperty(n).GetValue(r);
    sealed class Context(string path):AssemblyLoadContext(Guid.NewGuid().ToString()){
        protected override Assembly Load(AssemblyName name){if(name.Name=="ScCsgoResources")return LoadFromAssemblyPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"ScCsgoResources.dll"));return null;}
    }
}
public static class Mutations {
    public static Action<T> Shot<T>()=>r=>{var t=r.GetType();t.GetField("Rounds").SetValue(r,(int)t.GetField("Rounds").GetValue(r)-1);t.GetField("Durability").SetValue(r,(int)t.GetField("Durability").GetValue(r)-1);t.GetField("KillCount").SetValue(r,(long)t.GetField("KillCount").GetValue(r)+1);};
    public static Action<T> RepairSkin<T>()=>r=>{var t=r.GetType();t.GetField("Durability").SetValue(r,t.GetField("MaxDurability").GetValue(r));t.GetField("SkinId").SetValue(r,180);t.GetField("SilencerOff").SetValue(r,true);};
}
sealed class Inventory:IInventory {
    public int[] values=new int[4],counts=new int[4];public Project Project=>null;public int SlotsCount=>4;public int VisibleSlotsCount{get;set;}=4;public int ActiveSlotIndex{get;set;}
    public int GetSlotValue(int i)=>values[i];public int GetSlotCount(int i)=>counts[i];public int GetSlotCapacity(int i,int v)=>1;public int GetSlotProcessCapacity(int i,int v)=>0;
    public void AddSlotItems(int i,int v,int n){values[i]=v;counts[i]+=n;}public int RemoveSlotItems(int i,int n){n=Math.Min(n,counts[i]);counts[i]-=n;return n;}
    public void ProcessSlotItems(int i,int v,int c,int p,out int result,out int count){result=v;count=0;}public void DropAllItems(Engine.Vector3 p){}
}
