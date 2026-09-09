using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Engine;
using Game;
using TemplatesDatabase;

static class Growth30BoundaryRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod,string oldPackage,string ghoulDll) {
        var results=new List<Result>();
        void Check(string name,bool ok,string detail="")=>results.Add(new("growth30-boundary/"+name,ok,detail));
        XElement V(string n,object v)=>new("Value",new XAttribute("Name",n),new XAttribute("Type",v is int?"int":"string"),new XAttribute("Value",v));
        XElement G(string n,params object[] x)=>new("Values",new XAttribute("Name",n),x);
        XElement Group(XElement e,string n)=>e.Elements("Values").Single(x=>(string)x.Attribute("Name")==n);
        var registry=mod.GetType("Game.ScGunRegistry");var travel=mod.GetType("Game.ScGunTravel");
        var bridge=mod.GetType("Game.ScGhoulTestBridge");
        try {
            var gateType=mod.GetType("Game.ScTestEntryGate");var gate=Activator.CreateInstance(gateType);
            bool Open()=>(bool)gateType.GetProperty("Unlocked").GetValue(gate);
            Check("test-entry-default-hidden",!Open());
            for(int i=0;i<6;i++)gateType.GetMethod("Tap").Invoke(gate,[(double)i]);
            Check("six-taps-not-enough",!Open());gateType.GetMethod("Tap").Invoke(gate,[6d]);Check("seven-taps-unlock",Open());
            gateType.GetMethod("Reset").Invoke(gate,null);Check("exit-rehides",!Open());
            for(int i=0;i<8;i++)gateType.GetMethod("Tap").Invoke(gate,[(double)i*4]);Check("slow-random-taps-do-not-unlock",!Open());
            Check("no-arbitrary-reflection-target",bridge.GetMethod("TransferMethod").Invoke(null,[typeof(object)]) is null);
            if(ghoulDll is not null) {
                var ghoul=new PackageContext("ghoul-test-api").LoadFromAssemblyPath(Path.GetFullPath(ghoulDll));
                var method=(MethodInfo)bridge.GetMethod("TransferMethod").Invoke(null,[ghoul.GetType("SAGhoul.Tartareosity.SubsystemTartareosity",true)]);
                Check("real-ghoul-method-resolves-without-execution",method?.Name=="TransPortal"&&method.GetParameters().Length==0&&method.DeclaringType.FullName=="SAGhoul.Tartareosity.SubsystemWorld");
            }
            // Saved XML is the only source: this hook can run on the save worker after gameplay has changed.
            var row="v=0,r=180,s=0,d=1875,m=3750,n=8,c=-1,p=180,ct=1,k=3000,gl=30,gp=-1,gv=2,rc=0,ov=0";
            var xml=new XElement("Project",new XElement("Subsystems",G("BlocksManager",V("302","ScGunBlock")),
                G("ScGunBlockBehavior",V("GunDataLayout",5),V("GunTravelSource","data:/Worlds/Test"),
                    G("GunRegistry",V("Schema",4),V("Next",2),V("GrowthMode","CountAndGrow"),G("Records",V("1",row))))),
                new XElement("Entities",new XElement("Entity",new XAttribute("Name","MalePlayer"),G("Inventory",G("Slots",G("0",V("Contents",Terrain.MakeBlockValue(302,0,64)),V("Count",1)))))));
            var loader=Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
            loader.GetType().GetMethod("ProjectXmlSave").Invoke(loader,[new XElement("Project")]);
            loader.GetType().GetMethod("OnProjectXmlSaved").Invoke(loader,[xml]);
            Check("actual-after-save-hook-creates-v3-packet",xml.Descendants("Values").Any(e=>(string)e.Attribute("Name")=="ScGunTravel"
                &&e.Elements("Value").Any(v=>(string)v.Attribute("Name")=="Version"&&(string)v.Attribute("Value")=="3")));
            var saved=new ValuesDictionary();saved.ApplyOverrides(Group(Group(xml.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry"));
            bridge.GetMethod("VerifyCheckpoint").Invoke(null,[xml,saved,"data:/Worlds/Test"]);Check("checkpoint-matches",true);
            var imported=travel.GetMethod("Prepare").Invoke(null,[xml,"data:/Worlds/Test/Tartareosity"]);
            var doc=imported is null ? new XElement(xml) : (XElement)imported.GetType().GetProperty("Document").GetValue(imported);
            Check("level30-transfer-preserves-record",doc.Descendants("Value").Any(v=>(string)v.Attribute("Value")==row));
            var corrupt=new XElement(xml);corrupt.Descendants("Value").First(v=>(string)v.Attribute("Value")==row).SetAttributeValue("Value",row.Replace("r=180","r=179"));
            bool refused=false;try{bridge.GetMethod("VerifyCheckpoint").Invoke(null,[corrupt,saved,"data:/Worlds/Test"]);}catch(TargetInvocationException){refused=true;}Check("mismatched-checkpoint-refused",refused);
            // A pending queue in the frozen save refuses travel even if the live registry is different/null.
            var pending=new XElement(xml);Group(Group(pending.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry")
                .Add(G("PendingKills",V("Next","2"),G("Entries",V("1","1,0"))));
            loader.GetType().GetMethod("OnProjectXmlSaved").Invoke(loader,[pending]);
            Check("pending-credit-from-frozen-save",pending.Descendants("Values").Where(v=>(string)v.Attribute("Name")=="ScGunTravel").Any(v=>v.Elements("Value").Any(e=>(string)e.Attribute("Name")=="Error")));
            if(oldPackage is null)return results;
            using var zip=ZipFile.OpenRead(oldPackage);using var stream=zip.Entries.Single(e=>e.Name=="ScCsgoKnives.dll").Open();
            using var bytes=new MemoryStream();stream.CopyTo(bytes);bytes.Position=0;
            var old=new PackageContext("pre-growth30").LoadFromStream(bytes);var oldRegistry=old.GetType("Game.ScGunRegistry");
            Check("old-package-is-schema3",(int)oldRegistry.GetField("Schema").GetRawConstantValue()==3);
            var source=Activator.CreateInstance(oldRegistry);oldRegistry.GetMethod("Allocate").Invoke(source,[0,7,true,1125,2250,180]);
            var record=oldRegistry.GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,[1]);
            foreach(var field in new Dictionary<string,object>{{"CounterInstalled",true},{"KillCount",3500L},{"AppliedGrowthLevel",10},{"PendingGrowthLevel",10},{"GrowthRulesVersion",1}})
                record.GetType().GetField(field.Key).SetValue(record,field.Value);
            var oldSave=(ValuesDictionary)oldRegistry.GetMethod("Save").Invoke(source,[0d]);
            string original=oldSave.GetValue<ValuesDictionary>("Records").GetValue<string>("1");
            var loaded=registry.GetMethod("Load").Invoke(null,[oldSave,0d]);
            var newSave=(ValuesDictionary)registry.GetMethod("Save").Invoke(loaded,[0d]);
            for(int i=0;i<2;i++) {
                var tree=new XElement("Values");newSave.Save(tree);var data=new ValuesDictionary();data.ApplyOverrides(XElement.Parse(tree.ToString()));
                loaded=registry.GetMethod("Load").Invoke(null,[data,0d]);newSave=(ValuesDictionary)registry.GetMethod("Save").Invoke(loaded,[0d]);
                Check("actual-schema3-state-preserved/"+i,newSave.GetValue<int>("Schema")==4&&newSave.GetValue<ValuesDictionary>("Records").GetValue<string>("1")==original);
            }
            var oldLoaded=oldRegistry.GetMethod("Load").Invoke(null,[newSave,0d]);
            Check("actual-old-dll-refuses-schema4",(bool)oldRegistry.GetProperty("UnknownSchema").GetValue(oldLoaded));
            var guardValues=new ValuesDictionary();guardValues.SetValue("GunDataLayout",5);guardValues.SetValue("GunRegistry",newSave);
            refused=false;try{old.GetType("Game.ScGunSaveGuard").GetMethod("Validate").Invoke(null,[guardValues]);}catch(TargetInvocationException){refused=true;}Check("old-preload-guard-refuses",refused);
            // Real full-world snapshot of generated files only; never opens or changes a player's world.
            string temp=Path.Combine(Path.GetTempPath(),"sc-growth30-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            var registryNode=G("GunRegistry");oldSave.Save(registryNode);
            var world=new XElement("Project",new XElement("Subsystems",G("ScGunBlockBehavior",V("GunDataLayout",5),registryNode)));
            world.Save(Path.Combine(temp,"Project.xml"));File.WriteAllBytes(Path.Combine(temp,"Chunks.dat"),[1,2,3,4,5]);
            WorldInfo Info(string directory) {var info=(WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));info.DirectoryName=directory;return info;}
            var upgrade=mod.GetType("Game.ScGunSchemaUpgrade");string backup=(string)upgrade.GetMethod("BeforeLoad").Invoke(null,[world,Info(temp)]);
            using(var snapshot=ZipFile.OpenRead(backup))Check("schema3-full-world-backup",snapshot.GetEntry("Project.xml") is not null&&snapshot.GetEntry("Chunks.dat") is not null);
            string before=world.ToString();refused=false;
            try{upgrade.GetMethod("BeforeLoad").Invoke(null,[world,Info(Path.Combine(temp,"missing-world"))]);}catch(TargetInvocationException){refused=true;}
            Check("backup-failure-refuses-without-xml-change",refused&&world.ToString()==before);
        }catch(Exception e){Check("failure",false,e.ToString());}
        return results;
    }
}
