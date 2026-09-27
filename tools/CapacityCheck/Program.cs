using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

Engine.Dispatcher.Initialize();
if(args.Length>0&&args[0]=="--matrix")return CapacityMatrix.Run(args.Skip(1).ToArray());
if(args.Length>0&&args[0]=="--recover-worlds")return WorldRecovery.Run(args.Skip(1).ToArray());
if(args.Length<4)throw new ArgumentException("CapacityCheck <core.dll> <donor.scworld> <Mods directory or -> <report.json>");
var mod=new Module(args[0]);var checks=new List<object>();int failed=0;
void Require(bool value,string detail="assertion failed"){if(!value)throw new InvalidOperationException(detail);}
void Check(string name,Action action){try{action();checks.Add(new{name,ok=true});}catch(Exception e){failed++;checks.Add(new{name,ok=false,error=e.GetBaseException().ToString()});Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
XElement Group(XElement p,string n)=>p?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")==n);
string Text(XElement p,string n)=>(string)p?.Elements("Value").SingleOrDefault(e=>(string)e.Attribute("Name")==n)?.Attribute("Value");
ValuesDictionary Read(XElement x){var d=new ValuesDictionary();d.ApplyOverrides(new XElement(x));return d;}
XElement Xml(ValuesDictionary d){var x=new XElement("Values");d.Save(x);return x;}
object Load(XElement x)=>mod.Call("ScGunRegistry",null,"Load",Read(x),0d);
XElement Save(object reg)=>Xml((ValuesDictionary)mod.Call("ScGunRegistry",reg,"Save",0d));
int Allocate(object r,int variant=0,int rounds=7)=>(int)mod.Call("ScGunRegistry",r,"Allocate",variant,rounds,false,700,1500,0);
void Current(object r)=>mod.Type("ScGunRegistry").GetField("Current").SetValue(null,r);
int Enc(int variant,int id)=>(int)mod.Call("GunSpec",null,"WithId",variant,id);
byte[] original=File.ReadAllBytes(args[1]);string worldHash=Convert.ToHexString(SHA256.HashData(original));
using var zip=new System.IO.Compression.ZipArchive(new MemoryStream(original));
using var projectStream=zip.GetEntry("Project.xml").Open();var world=XElement.Load(projectStream);
var guns=Group(world.Element("Subsystems"),"ScGunBlockBehavior");var table=Group(guns,"GunRegistry");
var oldRows=Group(table,"Records").Elements().ToDictionary(e=>(string)e.Attribute("Name"),e=>(string)e.Attribute("Value"));
int maximum=(int)mod.Type("GunSpec").GetField("LastId").GetRawConstantValue();
Check("all-v5-valid-encodings-byte-identical",()=>{for(int v=0;v<35;v++)for(int id=0;id<=1023;id++)Require(Enc(v,id)==(v|(id<<6)));});
Check("donor-1022-records-exact-and-first-new-id-1024",()=>{
    var r=Load(table);Current(r);Require((int)mod.Get(r,"Count")==1022&&(int)mod.Get(r,"Next")==1024);
    Require(Allocate(r)==1024);var rows=Group(Save(r),"Records");foreach(var row in oldRows)Require(Text(rows,row.Key)==row.Value,"old row changed: "+row.Key);
    int data=Enc(0,1024);Require((int)mod.Call("GunSpec",null,"GetId",data)==1024&&(int)mod.Call("GunSpec",null,"GetVariant",data)==0);
    Require((bool)mod.Call("GunSpec",null,"IsUsable",data));
});
Check("every-extended-code-engine-roundtrip-no-template-collision",()=>{
    var r=mod.New("ScGunRegistry");Current(r);int count=0;
    while(!(bool)mod.Get(r,"IsFull")){
        int variant=count%35;int id=Allocate(r,variant,0);Require(id>0&&id!=1023);int data=Enc(variant,id);
        Require(data>=0&&data<131072);foreach(int block in new[]{1,512,1023}){int value=Terrain.MakeBlockValue(block,15,data);Require(Terrain.ExtractData(value)==data&&Terrain.ExtractContents(value)==block);}
        Require((int)mod.Call("GunSpec",null,"GetId",data)==id&&(int)mod.Call("GunSpec",null,"GetVariant",data)==variant);count++;
    }
    Require(count==66558&&(int)mod.Get(r,"Next")==66560&&Allocate(r)==-1);
    Require((bool)mod.Call("GunSpec",null,"IsFresh",Enc(0,1023)));Current(null);
});
Check("extended-transaction-and-two-consecutive-xml-roundtrips",()=>{
    var r=Load(table);Current(r);int id=Allocate(r);int data=Enc(0,id);
    var inv=new Inventory();inv.values[0]=Terrain.MakeBlockValue(512,0,data);inv.counts[0]=1;
    object[] prepare={inv,0,"capacity-test",null};var tx=mod.Type("ScGunMutation").GetMethod("Prepare").Invoke(null,prepare);Require(tx!=null);
    var action=typeof(Mutations).GetMethod("Shot").MakeGenericMethod(mod.Type("ScGunRecord")).Invoke(null,null);
    Require(mod.Call("ScGunMutation",tx,"Commit",action,0,0,null).ToString()=="Success");
    var record=mod.Call("ScGunRegistry",r,"Get",id);Require((int)mod.Get(record,"Rounds")==6&&(int)mod.Get(record,"Durability")==699);
    var saved=Save(r);string before=saved.ToString();
    for(int n=0;n<2;n++){r=Load(XElement.Parse(saved.ToString()));Current(r);saved=Save(r);Require(saved.ToString()==before);Require((int)mod.Call("GunSpec",null,"GetId",data)==id);}
});
Check("old-bit16-not-decoded-with-v5-proof-registry",()=>{
    var r=Load(table);Current(r);Require((bool)mod.Call("GunSpec",null,"IsForeign",65536));
    object[] decode={65536,r,5,0,0};Require(!(bool)mod.Type("ScGunEncoding").GetMethod("Decode").Invoke(null,decode));
});
Check("malformed-mixed-schema-and-layout-refused",()=>{
    var d=Read(guns);d.SetValue("GunDataLayout",5);var reg=Load(table);d.SetValue("GunRegistry",(ValuesDictionary)mod.Call("ScGunRegistry",reg,"Save",0d));
    bool refused=false;try{mod.Call("ScGunSaveGuard",null,"Validate",d);}catch{refused=true;}Require(refused);
});
Check("detached-extended-travel-does-not-use-current-world",()=>{
    var w=new XElement(world);var g=Group(w.Element("Subsystems"),"ScGunBlockBehavior");var r=Load(table);int id=Allocate(r,22,7);
    var saved=Save(r);saved.SetAttributeValue("Name","GunRegistry");Group(g,"GunRegistry").ReplaceWith(saved);
    g.Elements("Value").Single(e=>(string)e.Attribute("Name")=="GunDataLayout").SetAttributeValue("Value",6);
    var player=w.Element("Entities").Elements("Entity").Single(e=>(string)e.Attribute("Name")=="FemalePlayer");
    var slot=Group(Group(Group(player,"Inventory"),"Slots"),"Slot9");int old=int.Parse(Text(slot,"Contents"));
    slot.Elements("Value").Single(e=>(string)e.Attribute("Name")=="Contents").SetAttributeValue("Value",Terrain.ReplaceData(old,Enc(22,id)));
    Current(mod.New("ScGunRegistry"));mod.Call("ScGunTravel",null,"Capture",w,"data:/capacity-source");mod.Call("ScGunTravel",null,"ValidateCaptured",w,"data:/capacity-source");
    var packet=Group(player,"ScGunTravel");Require(Text(packet,"Error")==null&&Text(Group(packet,"Records"),id.ToString())!=null);
    // Isolated target contains the copied player only, as in a player-only cross-world import.
    var target=new XElement(w);Group(target.Element("Subsystems"),"ScGunBlockBehavior").Remove();
    Group(target.Element("Subsystems"),"SushiSyncBox")?.Remove();target.Element("Entities").Elements().Where(e=>(string)e.Attribute("Name")!="FemalePlayer").Remove();
    var plan=mod.Call("ScGunTravel",null,"Prepare",target,"data:/capacity-target");Require(plan!=null);
    var doc=(XElement)mod.Get(plan,"Document");var inspection=mod.Call("ScGunLoadIntegrity",null,"Inspect",doc);Require(((Array)mod.Get(inspection,"Issues")).Length==0);
});
if(args[2]!="-")Check("actual-sushi-regressions",()=>{var results=SushiInventoryRegression.Run(mod.Assembly,args[2]);foreach(var result in results)Require(result.Ok,result.Name+": "+result.Detail);});
foreach(string path in args.Skip(4))Check("unupdated-reader-refuses/"+Path.GetFileName(Path.GetDirectoryName(path)),()=>{
    var old=new Module(path);var d=Read(guns);d.SetValue("GunDataLayout",6);d.SetValue("GunRegistry",(ValuesDictionary)mod.Call("ScGunRegistry",Load(table),"Save",0d));
    string before=Xml(d).ToString();bool refused=false;try{old.Call("ScGunSaveGuard",null,"Validate",d);}catch(Exception e){refused=e.GetBaseException() is InvalidOperationException;}
    Require(refused&&Xml(d).ToString()==before,"old build accepted or changed new format");
});
Check("player-source-archive-unchanged",()=>Require(worldHash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))));
var report=new{failed,count=checks.Count,core=mod.Path,coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mod.Path))),sourceWorldSha256=worldHash,maximumRecords=66558,checks};
File.WriteAllText(args[3],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"CapacityCheck {checks.Count-failed}/{checks.Count}, failed={failed}");return failed==0?0:1;

sealed class Module {
    public string Path;public Assembly Assembly;
    public Module(string path){Path=System.IO.Path.GetFullPath(path);Assembly=new Context(Path).LoadFromAssemblyPath(Path);}
    public Type Type(string name)=>Assembly.GetType("Game."+name,true);
    public object New(string name)=>Activator.CreateInstance(Type(name));
    public object Get(object obj,string name)=>obj.GetType().GetField(name)?.GetValue(obj)??obj.GetType().GetProperty(name).GetValue(obj);
    public object Call(string type,object obj,string name,params object[] args)=>Type(type).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(b=>b)).Invoke(obj,args);
    sealed class Context(string path):AssemblyLoadContext(Guid.NewGuid().ToString()) {
        protected override Assembly Load(AssemblyName name){if(name.Name is "ScCsgoResources" or "ScCsgoResourceCodec")return LoadFromAssemblyPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),name.Name+".dll"));return null;}
    }
}
public static class Mutations {public static Action<T> Shot<T>()=>r=>{var t=r.GetType();t.GetField("Rounds").SetValue(r,(int)t.GetField("Rounds").GetValue(r)-1);t.GetField("Durability").SetValue(r,(int)t.GetField("Durability").GetValue(r)-1);};}
sealed class Inventory:IInventory {
    public int[] values=new int[4],counts=new int[4];public Project Project=>null;public int SlotsCount=>4;public int VisibleSlotsCount{get;set;}=4;public int ActiveSlotIndex{get;set;}
    public int GetSlotValue(int i)=>values[i];public int GetSlotCount(int i)=>counts[i];public int GetSlotCapacity(int i,int v)=>1;public int GetSlotProcessCapacity(int i,int v)=>0;
    public void AddSlotItems(int i,int v,int n){values[i]=v;counts[i]+=n;}public int RemoveSlotItems(int i,int n){n=Math.Min(n,counts[i]);counts[i]-=n;return n;}
    public void ProcessSlotItems(int i,int v,int c,int p,out int result,out int count){result=v;count=0;}public void DropAllItems(Engine.Vector3 p){}
}
