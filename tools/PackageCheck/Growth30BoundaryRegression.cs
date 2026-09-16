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
            int oldSchema=(int)oldRegistry.GetField("Schema").GetRawConstantValue();
            Check("actual-old-package-supported-schema",oldSchema is 3 or 4 or 5,$"schema={oldSchema}; package={oldPackage}; SHA256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(oldPackage)))}");
            // Build records with the supplied historical DLL, including its own capacity and charge rules.
            // New loading is then checked against the source XML fields, not against newly generated records.
            var oldSpec=old.GetType("Game.GunSpec"); var oldGrowth=old.GetType("Game.ScGunGrowth");
            var oldGuns=(Array)oldSpec.GetField("All").GetValue(null);
            var newGuns=(Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null);
            var oldSkins=(Array)old.GetType("Game.ScGunSkinCatalog").GetField("All").GetValue(null);
            Dictionary<string,string> Fields(string raw)=>raw.Split(',').Select(x=>x.Split('=')).ToDictionary(x=>x[0],x=>x[1]);
            var newGrowth=mod.GetType("Game.ScGunGrowth");
            int currentRules=(int)newGrowth.GetField("RulesVersion").GetRawConstantValue();
            var levelForVariant=newGrowth.GetMethods().Single(m=>m.Name=="LevelFor"&&m.GetParameters().Length==2);
            var killsForVariant=newGrowth.GetMethods().Single(m=>m.Name=="KillsFor"&&m.GetParameters().Length==2);
            var maxDurability=newGrowth.GetMethod("MaxDurability");
            var scaleDurability=newGrowth.GetMethod("ScaleDurability");
            var capacityAt=newGrowth.GetMethod("Capacity",[typeof(int),typeof(int)]);
            // Reproduce the reported 3000-kill/Lv0 count-only migration with the actual published serializer.
            var counting=Activator.CreateInstance(oldRegistry);
            oldRegistry.GetField("GrowthMode").SetValue(counting,Enum.Parse(oldRegistry.GetField("GrowthMode").FieldType,"CountOnly"));
            oldRegistry.GetMethod("Allocate").Invoke(counting,[0,7,true,750,1500,0]);
            var counted=oldRegistry.GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(counting,[1]);
            counted.GetType().GetField("CounterInstalled").SetValue(counted,true);counted.GetType().GetField("KillCount").SetValue(counted,3000L);
            var countingData=(ValuesDictionary)oldRegistry.GetMethod("Save").Invoke(counting,[0d]);
            for(int round=0;round<2;round++) {
                var countingXml=new XElement("Values");countingData.Save(countingXml);var countingRead=new ValuesDictionary();countingRead.ApplyOverrides(XElement.Parse(countingXml.ToString()));
                var loadedCounting=registry.GetMethod("Load").Invoke(null,[countingRead,0d]);countingData=(ValuesDictionary)registry.GetMethod("Save").Invoke(loadedCounting,[0d]);
                var f=Fields(countingData.GetValue<ValuesDictionary>("Records").GetValue<string>("1"));
                Check("supplied-dll-count-only/"+round,f["k"]=="3000"&&f["gl"]=="0"&&f["d"]=="750"&&f["m"]=="1500"&&f["r"]=="7"&&f["s"]=="1",string.Join(",",f.Select(p=>$"{p.Key}={p.Value}")));
            }
            bool Adapted(Dictionary<string,string> before,Dictionary<string,string> after,int variant) {
                if(before["v"]!=after["v"]||before["s"]!=after["s"]||before["p"]!=after["p"]||before["ct"]!=after["ct"]||before["k"]!=after["k"]||before["n"]!=after["n"]) return false;
                int oldLevel=int.Parse(before["gl"]);int newLevel=int.Parse(after["gl"]);long kills=long.Parse(before["k"]);
                int fromKills=(int)levelForVariant.Invoke(null,[variant,kills]);
                if(newLevel<oldLevel||newLevel<fromKills||newLevel>50||after["gp"]!="-1"||after["gv"]!=currentRules.ToString()) return false;
                int newMax=(int)maxDurability.Invoke(null,[variant,newLevel]);
                int newDur=(int)scaleDurability.Invoke(null,[int.Parse(before["d"]),int.Parse(before["m"]),newMax]);
                if(after["m"]!=newMax.ToString()||after["d"]!=newDur.ToString()) return false;
                int cap=(int)capacityAt.Invoke(null,[variant,newLevel]);
                int oldOv=before.ContainsKey("ov")?int.Parse(before["ov"]):0;
                int newR=int.Parse(after["r"]), newOv=after.ContainsKey("ov")?int.Parse(after["ov"]):0;
                if(int.Parse(before["r"])+oldOv!=newR+newOv||newR>cap) return false;
                long needed=(long)killsForVariant.Invoke(null,[variant,newLevel]);
                return after.GetValueOrDefault("kc","0")==Math.Max(0,needed-kills).ToString();
            }
            for(int variant=0;variant<oldGuns.Length;variant++) {
                var oldGun=oldGuns.GetValue(variant);string asset=(string)oldGun.GetType().GetField("Name").GetValue(oldGun);
                Check("supplied-dll-frozen-model-id/"+variant,asset==(string)newGuns.GetValue(variant).GetType().GetField("Name").GetValue(newGuns.GetValue(variant)));
                var matrixSource=Activator.CreateInstance(oldRegistry);
                oldRegistry.GetField("GrowthMode").SetValue(matrixSource,Enum.Parse(oldRegistry.GetField("GrowthMode").FieldType,"CountAndGrow"));
                int[] paints=[0,..oldSkins.Cast<object>().Where(s=>(string)s.GetType().GetProperty("Gun").GetValue(s)==asset).Select(s=>(int)s.GetType().GetProperty("PaintId").GetValue(s))];
                foreach(int level in oldSchema==3?new[]{0,1,9,10}:new[]{0,1,9,10,19,20,29,30})
                foreach(int wear in new[]{0,1,2}) foreach(int ammo in new[]{0,1,2}) foreach(bool silencer in new[]{false,true}) {
                    int capacity=(int)oldGrowth.GetMethod("Capacity",[typeof(int),typeof(int)]).Invoke(null,[variant,level]);
                    int maximum=(int)oldGrowth.GetMethod("MaxDurability").Invoke(null,[variant,level]);
                    long kills=(long)oldGrowth.GetMethod("KillsFor").Invoke(null,[level]);
                    int paint=paints[(level+wear+ammo)%paints.Length];
                    int id=(int)oldRegistry.GetMethod("Allocate").Invoke(matrixSource,[variant,0,silencer,maximum,maximum,paint]);
                    var r=oldRegistry.GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(matrixSource,[id]);
                    float cycle=(float)oldGrowth.GetMethod("RechargeSeconds").Invoke(null,[oldGun,level]);
                    foreach(var pair in new Dictionary<string,object>{{"Rounds",ammo==0?0:ammo==1?Math.Max(1,capacity/2):capacity},
                        {"Durability",wear==0?0:wear==1?Math.Max(1,maximum/2):maximum},{"Revision",17},{"CounterInstalled",true},
                        {"KillCount",kills},{"AppliedGrowthLevel",level},{"PendingGrowthLevel",-1},
                        {"GrowthRulesVersion",(int)oldGrowth.GetField("RulesVersion").GetRawConstantValue()},
                        {"RechargeReadyAt",cycle>0?10d+cycle*.6:-1d},{"RechargeCycleSeconds",cycle},{"ReserveOverflowRounds",7}})
                        r.GetType().GetField(pair.Key).SetValue(r,pair.Value);
                }
                var priorCurrent=registry.GetField("Current").GetValue(null);
                var oldMatrix=(ValuesDictionary)oldRegistry.GetMethod("Save").Invoke(matrixSource,[10d]);
                var originalXml=new XElement("Values");oldMatrix.Save(originalXml);string unchanged=originalXml.ToString();
                var matrixLoaded=registry.GetMethod("Load").Invoke(null,[oldMatrix,100d]);
                var expectedRows=oldMatrix.GetValue<ValuesDictionary>("Records");
                for(int round=0;round<2;round++) {
                    var written=(ValuesDictionary)registry.GetMethod("Save").Invoke(matrixLoaded,[100d]);
                    var xmlRound=new XElement("Values");written.Save(xmlRound);var read=new ValuesDictionary();read.ApplyOverrides(XElement.Parse(xmlRound.ToString()));
                    matrixLoaded=registry.GetMethod("Load").Invoke(null,[read,100d]);
                    registry.GetField("Current").SetValue(null,matrixLoaded);
                    var rows=((ValuesDictionary)registry.GetMethod("Save").Invoke(matrixLoaded,[100d])).GetValue<ValuesDictionary>("Records");
                    foreach(var pair in expectedRows) {
                        var beforeFields=Fields((string)pair.Value);var afterFields=Fields(rows.GetValue<string>(pair.Key,""));
                        int itemData=(int)oldSpec.GetMethod("WithId").Invoke(null,[variant,int.Parse(pair.Key)]);
                        bool usable=(bool)mod.GetType("Game.ScGunBlock").GetMethod("IsKnown").Invoke(null,[Terrain.MakeBlockValue(302,0,itemData)]);
                        bool same=usable&&Adapted(beforeFields,afterFields,variant);
                        double oldRemaining=double.Parse(beforeFields["c"],System.Globalization.CultureInfo.InvariantCulture);
                        double newRemaining=double.Parse(afterFields["c"],System.Globalization.CultureInfo.InvariantCulture);
                        double oldCycle=double.Parse(beforeFields["rc"],System.Globalization.CultureInfo.InvariantCulture);
                        double newCycle=double.Parse(afterFields["rc"],System.Globalization.CultureInfo.InvariantCulture);
                        bool charge=oldRemaining<0?newRemaining==-1:oldSchema==5?Math.Abs(newRemaining-oldRemaining)<1e-5:
                            oldCycle>0&&newCycle>0&&Math.Abs(newRemaining/newCycle-oldRemaining/oldCycle)<1e-5;
                        Check($"supplied-dll-state/{asset}/{pair.Key}/{round}",same&&charge,$"paint={beforeFields["p"]}; level={beforeFields["gl"]}; rounds={beforeFields["r"]}; wear={beforeFields["d"]}/{beforeFields["m"]}; charge={newRemaining}");
                    }
                    Check($"supplied-dll-count-watermark/{asset}/{round}",rows.Count==expectedRows.Count&&written.GetValue<int>("Next")==oldMatrix.GetValue<int>("Next")
                        &&(int)registry.GetProperty("QuarantinedCount").GetValue(matrixLoaded)==0);
                }
                var expectedCurrent=((ValuesDictionary)registry.GetMethod("Save").Invoke(matrixLoaded,[100d])).GetValue<ValuesDictionary>("Records");
                foreach(bool creative in new[]{false,true}) {
                    string[] carried=["1",(expectedRows.Count/2).ToString(),expectedRows.Count.ToString()];
                    var sourceTable=G("GunRegistry");oldMatrix.Save(sourceTable);
                    var slots=G("Slots");
                    for(int slot=0;slot<carried.Length;slot++) {
                        int itemData=(int)oldSpec.GetMethod("WithId").Invoke(null,[variant,int.Parse(carried[slot])]);
                        var item=G(slot.ToString(),V("Contents",Terrain.MakeBlockValue(302,0,itemData)));
                        if(!creative)item.Add(V("Count",1));slots.Add(item);
                    }
                    var origin=new XElement("Project",new XElement("Subsystems",G("BlocksManager",V("302","ScGunBlock")),
                        G("ScGunBlockBehavior",V("GunDataLayout",5),sourceTable)),new XElement("Entities",
                        new XElement("Entity",new XAttribute("Name","MalePlayer"),G(creative?"CreativeInventory":"Inventory",slots))));
                    old.GetType("Game.ScGunTravel").GetMethod("Capture").Invoke(null,[origin,"data:/Worlds/Feedback"]);
                    var destination=new XElement("Project",new XElement("Subsystems",G("BlocksManager",V("302","ScGunBlock")),
                        G("ScGunBlockBehavior",V("GunDataLayout",5),G("GunRegistry",V("Schema",6),V("Next",1),V("GrowthMode","Unset"),G("Records")))),new XElement(origin.Element("Entities")));
                    string beforeDestination=destination.ToString();
                    var transfer=travel.GetMethod("Prepare").Invoke(null,[destination,"data:/Worlds/Feedback/Other"]);
                    var transferred=(XElement)transfer.GetType().GetProperty("Document").GetValue(transfer);
                    var importedTable=Group(Group(transferred.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry");
                    var importedData=new ValuesDictionary();importedData.ApplyOverrides(importedTable);
                    bool portable=importedData.GetValue<ValuesDictionary>("Records").Count==3&&destination.ToString()==beforeDestination;
                    for(int slot=0;slot<carried.Length;slot++) {
                        int value=int.Parse((string)Group(transferred.Element("Entities").Element("Entity"),creative?"CreativeInventory":"Inventory")
                            .Descendants("Values").Single(e=>(string)e.Attribute("Name")==slot.ToString()).Elements("Value").Single(v=>(string)v.Attribute("Name")=="Contents").Attribute("Value"));
                        int newId=(Terrain.ExtractData(value)>>6)&1023;
                        portable&=importedData.GetValue<ValuesDictionary>("Records").GetValue<string>(newId.ToString())==expectedCurrent.GetValue<string>(carried[slot]);
                    }
                    Check($"supplied-dll-cross-world/{asset}/{creative}",portable,"actual old DLL capture; new destination remaps references and preserves complete rows; source unchanged");
                }
                registry.GetField("Current").SetValue(null,priorCurrent);
                var afterSource=new XElement("Values");oldMatrix.Save(afterSource);Check("supplied-dll-source-untouched/"+asset,afterSource.ToString()==unchanged);
            }
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
                var fields=newSave.GetValue<ValuesDictionary>("Records").GetValue<string>("1").Split(',').Select(x=>x.Split('=')).ToDictionary(x=>x[0],x=>x[1]);
                int fromKills=(int)levelForVariant.Invoke(null,[0,3500L]);
                int newLevel=int.Parse(fields["gl"]);
                int newMax=(int)maxDurability.Invoke(null,[0,newLevel]);
                int newDur=(int)scaleDurability.Invoke(null,[1125,2250,newMax]);
                long needed=(long)killsForVariant.Invoke(null,[0,newLevel]);
                Check("actual-old-dll-state-preserved/"+i,newSave.GetValue<int>("Schema")==6&&fields["v"]=="0"&&fields["r"]=="7"&&fields["s"]=="1"
                    &&fields["k"]=="3500"&&newLevel==fromKills&&newLevel>=10&&fields["gl"]==newLevel.ToString()
                    &&fields["gp"]=="-1"&&fields["gv"]==currentRules.ToString()
                    &&fields["kc"]==Math.Max(0,needed-3500).ToString()&&fields["d"]==newDur.ToString()&&fields["m"]==newMax.ToString()
                    &&oldSave.GetValue<ValuesDictionary>("Records").GetValue<string>("1")==original);
            }
            var oldLoaded=oldRegistry.GetMethod("Load").Invoke(null,[newSave,0d]);
            Check("actual-old-dll-refuses-schema6",(bool)oldRegistry.GetProperty("UnknownSchema").GetValue(oldLoaded));
            var guardValues=new ValuesDictionary();guardValues.SetValue("GunDataLayout",5);guardValues.SetValue("GunRegistry",newSave);
            refused=false;try{old.GetType("Game.ScGunSaveGuard").GetMethod("Validate").Invoke(null,[guardValues]);}catch(TargetInvocationException){refused=true;}Check("old-preload-guard-refuses",refused);
            // Real full-world snapshot of generated files only; never opens or changes a player's world.
            string temp=Path.Combine(Path.GetTempPath(),"sc-growth30-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            var registryNode=G("GunRegistry");oldSave.Save(registryNode);
            var world=new XElement("Project",new XElement("Subsystems",G("ScGunBlockBehavior",V("GunDataLayout",5),registryNode)));
            world.Save(Path.Combine(temp,"Project.xml"));File.WriteAllBytes(Path.Combine(temp,"Chunks.dat"),[1,2,3,4,5]);
            WorldInfo Info(string directory) {var info=(WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));info.DirectoryName=directory;return info;}
            var upgrade=mod.GetType("Game.ScGunSchemaUpgrade");string backup=(string)upgrade.GetMethod("BeforeLoad").Invoke(null,[world,Info(temp)]);
            using(var snapshot=ZipFile.OpenRead(backup))Check("actual-old-schema-full-world-backup",snapshot.GetEntry("Project.xml") is not null&&snapshot.GetEntry("Chunks.dat") is not null);
            string before=world.ToString();refused=false;
            try{upgrade.GetMethod("BeforeLoad").Invoke(null,[world,Info(Path.Combine(temp,"missing-world"))]);}catch(TargetInvocationException){refused=true;}
            Check("backup-failure-refuses-without-xml-change",refused&&world.ToString()==before);
        }catch(Exception e){Check("failure",false,e.ToString());}
        return results;
    }
}
