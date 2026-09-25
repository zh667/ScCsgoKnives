using Game;
using GameEntitySystem;
using TemplatesDatabase;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Linq;
using System.IO.Compression;

static class ProtectedLoadCheck {
    static XElement G(XElement p,string n)=>p?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")==n);
    static XElement Field(string n,object v)=>new("Value",new XAttribute("Name",n),new XAttribute("Type",v is int?"int":"string"),new XAttribute("Value",v));
    static XElement Gun(XElement p)=>G(p.Element("Subsystems"),"ScGunBlockBehavior");
    static ValuesDictionary Read(XElement x){var v=new ValuesDictionary();v.ApplyOverrides(new XElement(x));return v;}
    static XElement Serialize(ValuesDictionary v,string n){var x=new XElement("Values",new XAttribute("Name",n));v.Save(x);return x;}
    static void Require(bool ok,string message="assertion failed"){if(!ok)throw new Exception(message);}
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static XElement Fixture()=>XElement.Load("tools/fixtures/migration-120-20260925/world7-guns.xml");
    static XElement Slots(XElement p)=>p.Element("Entities").Descendants("Values").Single(e=>(string)e.Attribute("Name")=="Slots");
    public static void Run(string legacyRoot,Action<string,bool,string> check){
        void T(string n,Action test){var old=ScGunRegistry.Current;var locator=ScGunMutation.HolderLocator;try{ScGunMutation.HolderLocator=null;test();check("protected-load/"+n,true,n);}catch(Exception e){check("protected-load/"+n,false,e.ToString());}finally{ScGunRegistry.Current=old;ScGunMutation.HolderLocator=locator;}}
        T("actual-120-dll-confirms-two-preexisting-conflicts",()=>{
            var old=new AssemblyLoadContext("actual120-integrity",true).LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(legacyRoot,"0/ScCsgoKnives.dll")));
            var doc=Fixture();var type=old.GetType("Game.ScGunRegistry");var spec=old.GetType("Game.GunSpec");
            var registry=type.GetMethod("Load").Invoke(null,[Read(G(Gun(doc),"GunRegistry")),0d]);type.GetField("Current").SetValue(null,registry);
            foreach(var slot in Slots(doc).Elements()){
                int value=(int)slot.Element("Value").Attribute("Value");bool expected=(string)slot.Attribute("Name") is "Slot0" or "Slot1";
                bool actual=(bool)spec.GetMethod("IsUsable").Invoke(null,[Terrain.ExtractData(value)]);Require(actual==expected,(string)slot.Attribute("Name"));
            }
            Require(ScGunLoadIntegrity.Inspect(doc).Issues.Length==2);
        });
        T("whole-world-copy-with-local-conflict-is-not-a-player-import",()=>Require(ScGunTravel.Prepare(Fixture(),"data:/Worlds/Imported") is null));
        T("backup-failure-is-atomic",()=>{
            var doc=Fixture();string before=doc.ToString();bool refused=false;
            try{ScGunLoadIntegrity.ApplyProtected(doc,()=>throw new IOException("injected backup failure"),_=>false);}catch(IOException){refused=true;}
            Require(refused&&doc.ToString()==before);
        });
        T("native-subsystem-save-two-reloads-preserve-conflict-and-good-state",()=>{
            var doc=Fixture();string originalRows=G(G(Gun(doc),"GunRegistry"),"Records").ToString(),originalSlots=Slots(doc).ToString();
            var dir=Directory.CreateTempSubdirectory("actual120-upgrade-");string path=Path.Combine(dir.FullName,"Project.xml");doc.Save(path);byte[] original=File.ReadAllBytes(path);
            Directory.CreateDirectory(Path.Combine(dir.FullName,"OtherMod"));File.WriteAllText(Path.Combine(dir.FullName,"OtherMod/state"),"preserve");File.WriteAllBytes(Path.Combine(dir.FullName,"terrain.dat"),[1,2,3]);
            string backup=ScGunLoadIntegrity.BeforeLoad(doc,newWorld(dir.FullName),null);using(var zip=ZipFile.OpenRead(backup))Require(zip.GetEntry("OtherMod/state")!=null&&zip.GetEntry("terrain.dat")!=null);
            Require(File.ReadAllBytes(path).SequenceEqual(original)&&Slots(doc).ToString()==originalSlots&&G(G(Gun(doc),"GunRegistry"),"Records").ToString()==originalRows);
            Require(ScGunSchemaUpgrade.BeforeReleaseLoad(doc,newWorld(dir.FullName),backup)==backup);
            for(int round=0;round<2;round++){
                var data=Read(Gun(doc));var registry=ScGunRegistry.Current=ScGunRegistry.Load(data.GetValue<ValuesDictionary>("GunRegistry"),0);
                var subsystem=new SubsystemScGunBlockBehavior();void Set(string n,object v)=>typeof(SubsystemScGunBlockBehavior).GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(subsystem,v);
                Set("m_saveReady",true);Set("m_worldLayout",5);Set("m_registry",registry);Set("m_time",new SubsystemTime());
                Set("m_integrityProtection",data.GetValue<ValuesDictionary>(ScGunLoadIntegrity.ProtectionKey));Set("m_releaseBackup",data.GetValue<ValuesDictionary>(ScGunSchemaUpgrade.ReleaseMarker));
                Set("m_travelSource",data.GetValue<string>(ScGunTravel.SourcePath));Set("m_travelWorldIdentity",data.GetValue<string>(ScGunTravel.WorldIdentity));Set("m_travelIdentities",data.GetValue<ValuesDictionary>(ScGunTravel.Identities));
                var saved=new ValuesDictionary();subsystem.Save(saved);Gun(doc).ReplaceWith(Serialize(saved,"ScGunBlockBehavior"));
                doc=XElement.Parse(doc.ToString());
                Require(Slots(doc).ToString()==originalSlots&&G(G(Gun(doc),"GunRegistry"),"Records").ToString()==originalRows);
                Require(ScGunLoadIntegrity.Inspect(doc).Issues.Length==2&&saved.ContainsKey(ScGunLoadIntegrity.ProtectionKey));
                ScGunLoadIntegrity.ApplyProtected(doc,()=>throw new Exception("unexpected second backup"),File.Exists);
                Require(ScGunSchemaUpgrade.BeforeReleaseLoad(doc,newWorld(dir.FullName)) is null);
                foreach(var slot in Slots(doc).Elements()){
                    int value=(int)slot.Element("Value").Attribute("Value");var inv=new ComponentCreativeInventory{OpenSlotsCount=1};inv.m_slots.Add(value);
                    var tx=ScGunMutation.Prepare(inv,0,"test",out var why);
                    bool good=(string)slot.Attribute("Name") is "Slot0" or "Slot1";
                    Require(good?tx!=null:tx==null&&why==ScGunResult.ModelMismatch);
                    Require(inv.GetSlotValue(0)==value);
                }
            }
        });
        T("orphan-watermark-survives-saving-and-new-guns",()=>{
            var doc=Fixture();Slots(doc).Add(new XElement("Values",new XAttribute("Name","Slot20"),Field("Contents",Terrain.MakeBlockValue(317,0,GunSpec.WithId(0,900)))));
            var prepared=ScGunLoadIntegrity.Prepare(doc);Require(ScGunLoadIntegrity.Inspect(prepared).Next==901);
            var registry=ScGunRegistry.Load(Read(G(Gun(prepared),"GunRegistry")),0);
            for(int round=0;round<2;round++){registry=ScGunRegistry.Load(registry.Save(0),0);Require(registry.Next==901&&!registry.TryGetSnapshot(900,out _));}
            int id=registry.Allocate(0,29,false,1499);Require(id==901&&!registry.TryGetSnapshot(900,out _));
        });
        T("unknown-schema-and-foreign-bits-still-refused",()=>{
            foreach(bool future in new[]{false,true}){
                var doc=Fixture();if(future)G(Gun(doc),"GunRegistry").Elements("Value").Single(v=>(string)v.Attribute("Name")=="Schema").SetAttributeValue("Value",99);
                else Slots(doc).Elements().First().Element("Value").SetAttributeValue("Value",Terrain.MakeBlockValue(317,0,1<<16));
                bool refused=false;try{ScGunLoadIntegrity.Prepare(doc);}catch(InvalidOperationException){refused=true;}Require(refused);
            }
        });
        T("nontext-record-cannot-enter-local-preservation",()=>{
            var doc=Fixture();var row=G(G(Gun(doc),"GunRegistry"),"Records").Elements().First();row.SetAttributeValue("Type","int");row.SetAttributeValue("Value",123);
            bool refused=false;try{ScGunLoadIntegrity.Prepare(doc);}catch(InvalidOperationException){refused=true;}Require(refused);
        });
        T("duplicate-xml-record-cannot-be-silently-merged",()=>{
            var doc=Fixture();var rows=G(G(Gun(doc),"GunRegistry"),"Records");rows.Add(new XElement(rows.Elements().First()));
            string before=doc.ToString();bool refused=false;try{ScGunLoadIntegrity.Prepare(doc);}catch(InvalidOperationException){refused=true;}
            Require(refused&&doc.ToString()==before);
        });
        T("changed-conflict-needs-a-new-backup",()=>{
            var doc=Fixture();ScGunLoadIntegrity.ApplyProtected(doc,()=>"verified-first",_=>true);
            Slots(doc).Elements().Last().Element("Value").SetAttributeValue("Value",Terrain.MakeBlockValue(317,0,GunSpec.WithId(1,900)));
            int calls=0;ScGunLoadIntegrity.ApplyProtected(doc,()=>{calls++;return "verified-second";},_=>true);Require(calls==1);
        });
        T("raw-quarantined-row-and-item-survive-two-reloads",()=>{
            var doc=Fixture();string raw="v=0,unknown=original-corrupt-text";G(G(Gun(doc),"GunRegistry"),"Records").Add(Field("900",raw));
            Slots(doc).Add(new XElement("Values",new XAttribute("Name","Slot20"),Field("Contents",Terrain.MakeBlockValue(317,0,GunSpec.WithId(0,900)))));
            var prepared=ScGunLoadIntegrity.Prepare(doc);var registry=ScGunRegistry.Load(Read(G(Gun(prepared),"GunRegistry")),0);
            for(int round=0;round<2;round++){
                var saved=registry.Save(0);Require(saved.GetValue<ValuesDictionary>("Records").GetValue<string>("900")==raw);
                registry=ScGunRegistry.Load(saved,0);Require(registry.Next==901&&registry.QuarantinedCount==1&&!registry.TryGetSnapshot(900,out _));
            }
            Require(XNode.DeepEquals(Slots(doc),Slots(prepared)));
        });
        T("mismatch-cannot-witness-duplicate-and-strip-good-gun-growth",()=>{
            var registry=ScGunRegistry.Current=new ScGunRegistry();int id=registry.Allocate(0,13,false,900);
            var project=Blank<Project>();project.m_subsystems=[];project.m_entities=[];
            var inventory=new ComponentCreativeInventory{OpenSlotsCount=2};inventory.m_slots.Add(Terrain.MakeBlockValue(317,0,GunSpec.WithId(0,id)));inventory.m_slots.Add(Terrain.MakeBlockValue(317,0,GunSpec.WithId(30,id)));
            var entity=Blank<Entity>();entity.m_project=project;entity.m_components=[inventory];inventory.m_entity=entity;project.m_entities[entity]=true;
            var holders=ScGunHolders.Scan(project,317).ToArray();Require(holders.Length==1&&holders[0].Slot==0);
            ScGunMutation.HolderLocator=(record,except)=>ScGunHolders.Scan(project,317).Where(h=>h.Id==record&&h.Key!=except).Select(h=>h.Key);
            var tx=ScGunMutation.Prepare(inventory,0,ScGunHolders.Key(inventory,0),out _);Require(tx.Commit(r=>r.Rounds--)==ScGunResult.Success);
            Require(!tx.NeedsClone&&registry.Count==1&&registry.TryGetSnapshot(id,out var s)&&s.Rounds==12&&s.Durability==900);
        });
        T("travel-capture-does-not-export-AK-as-M249",()=>{
            var doc=Fixture();var player=doc.Element("Entities").Element("Entity");string originalRows=G(G(player,ScGunTravel.Packet),"Records").ToString(),slots=Slots(doc).ToString();
            ScGunTravel.Capture(doc,"app:/doc/Worlds/World7");Require(G(player,ScGunTravel.Packet).Elements("Value").Any(v=>(string)v.Attribute("Name")=="Error"));
            Require(G(G(player,ScGunTravel.Packet),"Records").ToString()==originalRows&&Slots(doc).ToString()==slots);
            bool refused=false;try{ScGunTravel.ValidateCaptured(doc,"app:/doc/Worlds/World7");}catch(InvalidOperationException){refused=true;}Require(refused);
        });
        T("failed-capture-does-not-keep-a-stale-destination-source",()=>{
            var doc=Fixture();var player=doc.Element("Entities").Element("Entity");var packet=G(player,ScGunTravel.Packet);
            packet.Elements("Value").Single(v=>(string)v.Attribute("Name")=="Source").SetAttributeValue("Value","data:/OldDestination");
            ScGunTravel.Capture(doc,"app:/doc/Worlds/World7");packet=G(player,ScGunTravel.Packet);
            Require((string)packet.Elements("Value").Single(v=>(string)v.Attribute("Name")=="Source").Attribute("Value")=="app:/doc/Worlds/World7");
            Require((string)packet.Elements("Value").Single(v=>(string)v.Attribute("Name")=="RecoverySource").Attribute("Value")=="data:/OldDestination");
            var target=new XElement(doc);Gun(target).Remove();bool refused=false;
            try{ScGunTravel.Prepare(target,"data:/OldDestination");}catch(InvalidOperationException){refused=true;}Require(refused);
        });
    }
    static WorldInfo newWorld(string dir){var w=Blank<WorldInfo>();w.DirectoryName=dir;return w;}
}
