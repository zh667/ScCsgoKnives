using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Engine.Serialization;
using Game;
using TemplatesDatabase;

if(args.Length==5&&args[0]=="--voice-native"){
    Dispatcher.Initialize();int voiceFailures=0;var voiceChecks=new List<object>();
    void RequireVoice(string name,bool ok){voiceChecks.Add(new{name,ok});if(!ok)throw new Exception(name);}
    try{
        ModEntity Load(string path){var m=new HeadlessMod{ModArchive=Game.ZipArchive.Open(File.OpenRead(path))};m.InitResources();ModsManager.ModList.Add(m);var aa=m.GetAssemblies();foreach(var a in aa)ModsManager.Dlls[a.FullName]=a;foreach(var a in aa)m.HandleAssembly(a);return m;}
        AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>ModsManager.Dlls.GetValueOrDefault(e.Name);
        var core=Load(args[1]);var voice=Load(args[2]);
        var coreAssembly=ModsManager.Dlls.Values.Single(a=>a.GetName().Name=="ScCsgoKnives");
        var voiceAssembly=ModsManager.Dlls.Values.Single(a=>a.GetName().Name=="ScCsgoVoice");
        bool expected=coreAssembly.GetType("Game.ScAgentVoice")!=null;
        var actions=new List<Action>();foreach(var l in voice.Loaders)l.OnLoadingFinished(actions);foreach(var action in actions)action();
        RequireVoice("optional-interface-detection",(bool)voiceAssembly.GetType("Game.AgentVoiceModLoader").GetField("Supported").GetValue(null)==expected);
        RequireVoice("optional-pack-is-nonpersistent",voice.modInfo.NonPersistentMod);
        using var voiceContent=System.IO.Compression.ZipFile.OpenRead(args[3]);using var stream=voiceContent.Entries.Single(e=>e.FullName.EndsWith("Database.xml")).Open();var db=XElement.Load(stream);
        core.LoadXdb(ref db);voice.LoadXdb(ref db);DatabaseManager.LoadDataBaseFromXml(db);
        RequireVoice("voice-database-loads-with-old-and-new-core",db.Descendants("MemberSubsystemTemplate").Any(e=>(string)e.Attribute("Name")=="ScAgentVoice"));
        var sub=(GameEntitySystem.Subsystem)Activator.CreateInstance(voiceAssembly.GetType("Game.SubsystemScAgentVoice"));
        var project=(GameEntitySystem.Project)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameEntitySystem.Project));project.m_subsystems=[new SubsystemTime(),new SubsystemPlayers(),new SubsystemAudio()];sub.m_project=project;
        sub.Load(new ValuesDictionary());var saved=new ValuesDictionary();sub.Save(saved);sub.Dispose();
        RequireVoice("load-dispose-no-world-payload",saved.Count==0);
        using var zip=System.IO.Compression.ZipFile.OpenRead(args[2]);int decoded=0;
        foreach(var entry in zip.Entries.Where(e=>e.FullName.EndsWith(".ogg"))){using var input=entry.Open();using var audio=new MemoryStream();input.CopyTo(audio);audio.Position=0;var data=Engine.Media.SoundData.Load(audio);if(data.ChannelsCount!=1||data.SamplingFrequency!=32000)throw new Exception("bad audio format");decoded++;}
        RequireVoice("native-decodes-all-204-clips",decoded==204);
    }catch(Exception e){voiceFailures=1;voiceChecks.Add(new{error=e.ToString()});Console.Error.WriteLine(e);}
    File.WriteAllText(args[4],JsonSerializer.Serialize(new{failed=voiceFailures,checks=voiceChecks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"voice-native {voiceChecks.Count}, failed={voiceFailures}");return voiceFailures;
}

// deathmatch-addon task 1.4 (design §14): a world that carries the deathmatch package's save group and mode marker, taken
// through the game's own load and save hooks in three separate processes - with the package, WITHOUT it (the released
// core alone), and with it again. Each process loads the packages natively (archive, assemblies, loaders, database) and
// has no compile-time reference to any of them. What is asserted: the load hooks never refuse the world, the engine's
// own project parser accepts the unknown group, the group and the marker come out of every save exactly as they went
// in, and without the package the world says which mode it belongs to. What it is NOT: a game loading that world on
// screen (the user's test).
// Usage: --dm-payload <core.scmod> <Content.zip> <deathmatch.scmod | -> <world in.xml> <group.xml | -> <world out.xml> <report.json>
if(args.Length==8&&args[0]=="--dm-payload") {
    Dispatcher.Initialize();var cases=new List<object>();int failure=0;bool withDm=args[3]!="-";
    void Require(string name,bool ok,string detail=""){cases.Add(new{name,ok,detail});if(!ok)throw new Exception(name+" "+detail);}
    try {
        var mods=new List<ModEntity>();
        foreach(string path in withDm?new[]{args[1],args[3]}:new[]{args[1]}){var m=new HeadlessMod{ModArchive=Game.ZipArchive.Open(File.OpenRead(path))};m.InitResources();ModsManager.ModList.Add(m);mods.Add(m);}
        var loaded=mods.ToDictionary(m=>m,m=>m.GetAssemblies());
        foreach(var a in loaded.Values.SelectMany(a=>a))ModsManager.Dlls[a.FullName]=a;
        AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>ModsManager.Dlls.GetValueOrDefault(e.Name);
        foreach(var m in mods)foreach(var a in loaded[m])m.HandleAssembly(a);
        using var vanilla=System.IO.Compression.ZipFile.OpenRead(args[2]);
        using var databaseStream=vanilla.Entries.Single(e=>e.FullName.EndsWith("Database.xml")).Open();var database=XElement.Load(databaseStream);
        foreach(var m in mods)m.LoadXdb(ref database);DatabaseManager.LoadDataBaseFromXml(database);
        var core=ModsManager.Dlls.Values.Single(a=>a.GetName().Name=="ScCsgoKnives");
        bool template=database.Descendants("MemberSubsystemTemplate").Any(e=>(string)e.Attribute("Name")=="ScDeathmatch");
        Require(withDm?"with the package: the database has the ScDeathmatch subsystem and its class resolves to the package's assembly":"without the package: the database has no ScDeathmatch subsystem and no deathmatch assembly is loaded",
            withDm?template&&TypeCache.FindType("Game.SubsystemScDeathmatch",true,true).Assembly.GetName().Name=="ScCsgoDeathmatch":!template&&ModsManager.Dlls.Values.All(a=>a.GetName().Name!="ScCsgoDeathmatch"));
        ContentManager.AddContentReader(new Game.IContentReader.XmlReader());
        mods[0].GetFile("Assets/ScCsgoResources.xml",s=>{var copy=new MemoryStream();s.CopyTo(copy);copy.Position=0;var contentInfo=new ContentInfo("ScCsgoResources.xml");contentInfo.SetContentStream(copy);ContentManager.Add(contentInfo);});
        XElement Group(XElement parent,string name)=>parent.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")==name);
        string Field(XElement group,string name)=>(string)group?.Elements("Value").SingleOrDefault(e=>(string)e.Attribute("Name")==name)?.Attribute("Value");
        var doc=XElement.Load(args[4]);var subs=doc.Element("Subsystems");
        XElement payload=args[5]!="-"?XElement.Load(args[5]):null;
        if(payload!=null){Group(subs,"ScDeathmatch")?.Remove();subs.Add(new XElement(payload));}     // the group as the package's own Save wrote it (dmloop)
        var original=new XElement(Group(subs,"ScDeathmatch")??throw new Exception("the world has no ScDeathmatch group"));
        var dir=Directory.CreateTempSubdirectory("dm-payload-");
        var info=(WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));info.DirectoryName=dir.FullName;
        var hooks=ModsManager.m_tempModHooks["ProjectXmlLoad"].UnorderedItems.OrderBy(i=>i.Priority).Select(i=>i.Element).ToArray();
        foreach(var hook in hooks)hook.ProjectXmlLoad(doc,info,null);
        Require("the game's load hooks accept the world (no refusal, no file written)",Group(doc.Element("Subsystems"),"ScGunBlockBehavior")!=null&&Directory.GetFiles(dir.FullName).Length==0);
        subs=doc.Element("Subsystems");var state=Group(subs,"ScCompatibility");var capsule=XElement.Parse(Field(state,"Capsule"));
        Require("the world's capsule lists ScDeathmatch as a save group that belongs to a package (the name travels with the world)",capsule.Element("Compatibility").Elements("Subsystem").Any(e=>(string)e.Attribute("Name")=="ScDeathmatch"));
        var kept=capsule.Element("Opaque").Elements("Subsystem").Select(e=>e.Element("Values")).SingleOrDefault(e=>(string)e.Attribute("Name")=="ScDeathmatch");
        Require("the capsule holds the group exactly as it was saved",kept!=null&&XNode.DeepEquals(kept,original));
        // the engine's own parser of a saved world: a group without a subsystem template is carried as plain values and no
        // subsystem is made of it (GameEntitySystem.Project instantiates only template-backed groups)
        // (the fixture world is a trimmed Project.xml without the root's template reference; a real one names the game's project template)
        var forParser=new XElement(doc);var projectTemplate=database.Descendants("ProjectTemplate").First();
        if(forParser.Attribute("Guid")==null&&forParser.Attribute("Name")==null)forParser.SetAttributeValue("Guid",(string)projectTemplate.Attribute("Guid"));
        var parsed=new GameEntitySystem.ProjectData(DatabaseManager.GameDatabase,forParser,null,true);
        var parsedGroup=parsed.ValuesDictionary.GetValue<ValuesDictionary>("ScDeathmatch",null);
        Require(withDm?"the engine's project parser binds the group to the package's subsystem":"the engine's project parser accepts the unknown group and binds no subsystem to it",
            parsedGroup!=null&&(parsedGroup.DatabaseObject!=null)==withDm&&parsedGroup.GetValue<int>("Schema")==int.Parse(Field(original,"Schema")));
        var modesType=core.GetType("Game.ScWorldModes",true);var compatType=core.GetType("Game.SubsystemScCompatibility",true);
        System.Collections.IList Modes(XElement c)=>(System.Collections.IList)System.Linq.Enumerable.ToList((IEnumerable<object>)modesType.GetMethod("Read").Invoke(null,[c]));
        // ---- what a save writes: the compatibility subsystem's own Save; the deathmatch group only when its subsystem exists
        var compat=(GameEntitySystem.Subsystem)Activator.CreateInstance(compatType);var compatValues=new ValuesDictionary();compatValues.ApplyOverrides(state);compat.Load(compatValues);
        if(withDm&&payload!=null) {
            // the package marks the world when the host enables the arena (ScWorldModes.Mark writes this element into the capsule)
            var marked=(XElement)compatType.GetMethod("ReadCapsule").Invoke(compat,null);marked.Element("Modes")?.Remove();
            marked.Add(new XElement("Modes",new XElement("Mode",new XAttribute("Id","zh667.ScCsgoDeathmatch/deathmatch"),new XAttribute("Name","死亡竞赛"),new XAttribute("Required",true))));
            compatType.GetMethod("WriteCapsule").Invoke(compat,[marked]);
        }
        var writtenValues=new ValuesDictionary();compat.Save(writtenValues);var written=new XElement("Values",new XAttribute("Name","ScCompatibility"));writtenValues.Save(written);
        var saved=new XElement(doc);var savedSubs=saved.Element("Subsystems");Group(savedSubs,"ScCompatibility").ReplaceWith(written);
        if(!withDm)Group(savedSubs,"ScDeathmatch").Remove();                    // no subsystem, so the engine writes no such group
        foreach(var hook in ModsManager.m_tempModHooks["OnProjectXmlSaved"].UnorderedItems.OrderBy(i=>i.Priority).Select(i=>i.Element))hook.OnProjectXmlSaved(saved);
        var after=Group(saved.Element("Subsystems"),"ScDeathmatch");
        Require(withDm?"with the package the saved world carries the group":"without the package the save puts the group back exactly as it was: nothing lost, nothing reset",after!=null&&XNode.DeepEquals(after,original));
        var savedCapsule=XElement.Parse(Field(Group(saved.Element("Subsystems"),"ScCompatibility"),"Capsule"));var modes=Modes(savedCapsule);
        Require("the saved world carries the mode marker: a dedicated deathmatch world",modes.Count==1&&(string)modes[0].GetType().GetProperty("Id").GetValue(modes[0])=="zh667.ScCsgoDeathmatch/deathmatch"&&(bool)modes[0].GetType().GetProperty("Required").GetValue(modes[0]));
        if(!withDm) {
            string notice=(string)modesType.GetMethod("DormantNotice").Invoke(null,[modes[0]]);
            Require("without the package the world names its mode and says the data is kept and the mode is dormant",notice.Contains("死亡竞赛")&&notice.Contains("保留"),notice);
        }
        // (the core's own save hook adds travel metadata beside the registry; the registry itself - every gun record - is what must not move)
        var gunsBefore=Group(Group(doc.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry");var gunsAfter=Group(Group(saved.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry");
        Require("the gun registry (every gun record) is untouched by the round trip",gunsBefore!=null&&XNode.DeepEquals(gunsBefore,gunsAfter));
        saved.Save(args[6]);
        cases.Add(new{name="identity",ok=true,detail=$"core {core.ManifestModule.ModuleVersionId}; group sha256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original.ToString(SaveOptions.DisableFormatting)))).ToLowerInvariant()}"});
    }catch(Exception e){failure=1;cases.Add(new{error=e.ToString()});Console.Error.WriteLine(e);}
    File.WriteAllText(args[7],JsonSerializer.Serialize(new{mode=withDm?"with the deathmatch package":"without the deathmatch package",failed=failure,checks=cases},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
    Console.WriteLine($"dm-payload ({(withDm?"with":"without")} the package) {cases.Count} cases, failed={failure}");return failure;
}

if(args.Length==4&&args[0]=="--compat-native") {
    Dispatcher.Initialize();var cases=new List<object>();int failure=0;
    void CheckCompat(string name,bool ok){cases.Add(new{name,ok});if(!ok)throw new Exception(name);}
    try {
        var coreMod=new HeadlessMod{ModArchive=Game.ZipArchive.Open(File.OpenRead(args[1]))};coreMod.InitResources();
        ModsManager.ModList.Add(coreMod);var assemblies=coreMod.GetAssemblies();
        foreach(var a in assemblies)ModsManager.Dlls[a.FullName]=a;
        AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>ModsManager.Dlls.GetValueOrDefault(e.Name);
        foreach(var a in assemblies)coreMod.HandleAssembly(a);
        using var vanilla=System.IO.Compression.ZipFile.OpenRead(args[2]);
        using var databaseStream=vanilla.Entries.Single(e=>e.FullName.EndsWith("Database.xml")).Open();var database=XElement.Load(databaseStream);
        coreMod.LoadXdb(ref database);DatabaseManager.LoadDataBaseFromXml(database);
        CheckCompat("native-database-resolves-dormant-entity",DatabaseManager.FindEntityValuesDictionary("ScCompatibilityDormant",true)!=null);
        var coreAssembly=assemblies.Single(a=>a.GetName().Name=="ScCsgoKnives");
        var hooks=ModsManager.m_tempModHooks["ProjectXmlLoad"].UnorderedItems.OrderBy(i=>i.Priority).Select(i=>i.Element).ToArray();
        CheckCompat("compatibility-hook-precedes-core",hooks[0].GetType().Name=="ScCompatibilityModLoader");
        ContentManager.AddContentReader(new Game.IContentReader.XmlReader());
        coreMod.GetFile("Assets/ScCsgoResources.xml",s=>{
            var copy=new MemoryStream();s.CopyTo(copy);copy.Position=0;
            var contentInfo=new ContentInfo("ScCsgoResources.xml");contentInfo.SetContentStream(copy);ContentManager.Add(contentInfo);
        });
        var dir=Directory.CreateTempSubdirectory("compat-native-");
        var doc=XElement.Load("tools/fixtures/migration-120-20260925/world7-guns.xml");
        string path=Path.Combine(dir.FullName,"Project.xml");doc.Save(path);byte[] before=File.ReadAllBytes(path);
        var info=(WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));info.DirectoryName=dir.FullName;
        foreach(var hook in hooks)hook.ProjectXmlLoad(doc,info,null);
        var gun=doc.Element("Subsystems").Elements().Single(e=>(string)e.Attribute("Name")=="ScGunBlockBehavior");var values=new ValuesDictionary();values.ApplyOverrides(gun);
        coreAssembly.GetType("Game.ScGunSaveGuard").GetMethod("Validate").Invoke(null,[values]);
        CheckCompat("real-source-120-hook-no-automatic-backup",Directory.GetFiles(dir.FullName,"*.snapshot").Length==0&&Directory.GetFiles(dir.FullName).Length==1&&before.SequenceEqual(File.ReadAllBytes(path)));
        // A missing old backup is not a load prerequisite; reloading/switching writes no files.
        info.DirectoryName=Path.Combine(dir.FullName,"nonexistent-world-directory");
        foreach(var hook in hooks)hook.ProjectXmlLoad(doc,info,null);
        var reloadedValues=new ValuesDictionary();reloadedValues.ApplyOverrides(doc.Element("Subsystems").Elements().Single(e=>(string)e.Attribute("Name")=="ScGunBlockBehavior"));
        coreAssembly.GetType("Game.ScGunSaveGuard").GetMethod("Validate").Invoke(null,[reloadedValues]);
        CheckCompat("second-load-without-backup-directory",!Directory.Exists(info.DirectoryName));
        var proxy=XElement.Parse("<Entity Id='42' Guid='7807c7ed-ce6a-4fdf-aad7-782480bf5e83' Name='ScCompatibilityDormant'><Values Name='ScCompatibilityArchive'><Value Name='Payload' Type='string' Value='&lt;Entity Id=&quot;42&quot; Name=&quot;Preserved&quot; /&gt;'/></Values></Entity>");
        var data=new GameEntitySystem.EntityData(DatabaseManager.GameDatabase,proxy);var round=new XElement("Entity");data.Save(round);
        CheckCompat("native-entity-serialization-keeps-id-and-payload",(string)round.Attribute("Id")=="42"&&round.Descendants("Value").Any(e=>(string)e.Attribute("Name")=="Payload"));
        var activities=new List<Action>();foreach(var loader in coreMod.Loaders)if(loader.GetType().Name is "ScCompatibilityModLoader" or "BundleModLoader")loader.OnLoadingFinished(activities);
        foreach(var action in activities)action();
        CheckCompat("usedmods-keeps-tactical-identity",ModsManager.GetModEntity("zh667.ScCsgoTactical",out _));
    }catch(Exception e){failure=1;cases.Add(new{error=e.ToString()});Console.Error.WriteLine(e);}
    File.WriteAllText(args[3],JsonSerializer.Serialize(new{failed=failure,checks=cases},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"compat-native {cases.Count} cases, failed={failure}");return failure;
}

// Exercise the native archive scanner, loader registration and database type resolution.
// Each mode runs in its own process, without compile-time references to any tested mod.
if(args.Length==3&&args[0]=="--world-resource-gate"){
    Dispatcher.Initialize();
    var gateChecks=new List<object>();int gateFailed=0;
    try{
        using var archive=Game.ZipArchive.Open(File.OpenRead(args[1]));
        var entries=archive.ReadCentralDir();
        Assembly core=null;
        // Load only the two assemblies needed by the world's resource gate.
        foreach(string name in new[]{"ScCsgoResources.dll","ScCsgoKnives.dll"}){
            using var data=new MemoryStream();archive.ExtractFile(entries.Single(e=>e.FilenameInZip==name),data);
            var assembly=Assembly.Load(data.ToArray());ModsManager.Dlls[assembly.FullName]=assembly;
            if(name=="ScCsgoKnives.dll")core=assembly;
        }
        AppDomain.CurrentDomain.AssemblyResolve+=(_,a)=>ModsManager.Dlls.GetValueOrDefault(a.Name);
        ContentManager.AddContentReader(new Game.IContentReader.XmlReader());
        var marker=new MemoryStream();archive.ExtractFile(entries.Single(e=>e.FilenameInZip=="Assets/ScCsgoResources.xml"),marker);
        var gateContent=new ContentInfo("ScCsgoResources.xml");gateContent.SetContentStream(marker);ContentManager.Add(gateContent);
        // Reproduce the cold ContentManager -> native XML reader path used by
        // ProjectXmlLoad, then run the save guard on a detached valid subsystem.
        var project=XElement.Parse("<Project><Subsystems><Values Name='ScGunBlockBehavior'><Value Name='GunDataLayout' Type='int' Value='5'/><Values Name='GunRegistry'><Value Name='Schema' Type='int' Value='6'/></Values></Values></Subsystems></Project>");
        var original=new XElement(project);
        core.GetType("Game.ScRequiredResources").GetMethod("Validate").Invoke(null,null);
        var values=new ValuesDictionary();values.ApplyOverrides(project.Element("Subsystems").Element("Values"));
        core.GetType("Game.ScGunSaveGuard").GetMethod("Validate").Invoke(null,[values]);
        gateChecks.Add(new{name="cold-native-resource-and-world-save-guard",ok=true});
        if(!XNode.DeepEquals(project,original))throw new Exception("Fixture XML changed unexpectedly");
        gateChecks.Add(new{name="world-fixture-unchanged",ok=true});
        // The actual XML hook must propagate a missing-record refusal into Subsystem.Load's guard.
        // Use an uninitialized WorldInfo: its normal ctor needs unrelated game UI settings.
        var broken=XElement.Parse("<Project><Subsystems><Values Name='BlocksManager'><Value Name='315' Type='string' Value='ScGunBlock'/></Values></Subsystems><Entities><Entity Name='MalePlayer'><Values Name='CreativeInventory'><Values Name='Slots'><Values Name='Slot0'><Value Name='Contents' Type='int' Value='7340347'/></Values></Values></Values></Entity></Entities></Project>");
        broken.Descendants("Value").Single(v=>(string)v.Attribute("Name")=="Contents").SetAttributeValue("Value",Terrain.MakeBlockValue(315,0,448));
        var brokenBefore=new XElement(broken);
        var world=(WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));world.DirectoryName="data:/Worlds/integrity-probe";
        var loader=Activator.CreateInstance(core.GetType("Game.ScCsgoKnivesModLoader"));
        core.GetType("Game.ScCsgoKnivesModLoader").GetMethod("ProjectXmlLoad",[typeof(XElement),typeof(WorldInfo),typeof(ContainerWidget)]).Invoke(loader,[broken,world,null]);
        var blockedGroup=broken.Element("Subsystems").Elements("Values").Single(v=>(string)v.Attribute("Name")=="ScGunBlockBehavior");
        var blocked=new ValuesDictionary();blocked.ApplyOverrides(blockedGroup);
        bool refused=false;try{core.GetType("Game.ScGunSaveGuard").GetMethod("Validate").Invoke(null,[blocked]);}catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException){refused=true;}
        if(!refused)throw new Exception("Actual XML hook failed to block missing gun registry");
        blockedGroup.Remove();if(!XNode.DeepEquals(broken,brokenBefore))throw new Exception("Guard changed source item fields");
        gateChecks.Add(new{name="actual-project-xml-hook-refuses-missing-record-without-source-edits",ok=true});
        // Actual 1.2.0 gun subtree: two good carried guns and two model-conflicting old items.
        var legacyFixture=XElement.Load("tools/fixtures/migration-120-20260925/world7-guns.xml");
        var localCopy=new XElement(legacyFixture);
        var auditDir=Directory.CreateTempSubdirectory("packaged-120-upgrade-");
        string projectPath=Path.Combine(auditDir.FullName,"Project.xml");localCopy.Save(projectPath);
        File.WriteAllText(Path.Combine(auditDir.FullName,"other-mod-state"),"preserved");
        byte[] originalDisk=File.ReadAllBytes(projectPath);
        world.DirectoryName=auditDir.FullName;
        var hook=core.GetType("Game.ScCsgoKnivesModLoader").GetMethod("ProjectXmlLoad",[typeof(XElement),typeof(WorldInfo),typeof(ContainerWidget)]);
        for(int round=0;round<2;round++){
            hook.Invoke(loader,[localCopy,world,null]);
            var gun=localCopy.Element("Subsystems").Elements("Values").Single(v=>(string)v.Attribute("Name")=="ScGunBlockBehavior");
            var loadedValues=new ValuesDictionary();loadedValues.ApplyOverrides(gun);
            core.GetType("Game.ScGunSaveGuard").GetMethod("Validate").Invoke(null,[loadedValues]);
            var notice=loadedValues.GetValue<ValuesDictionary>("GunIntegrityProtection");
            if(notice.GetValue<int>("Count")!=2)throw new Exception("Local conflicts were not preserved");
            if(!XNode.DeepEquals(legacyFixture.Element("Entities"),localCopy.Element("Entities")))throw new Exception("Player gun XML was changed by the compatibility hook");
            localCopy=XElement.Parse(localCopy.ToString());
        }
        if(!originalDisk.SequenceEqual(File.ReadAllBytes(projectPath)))throw new Exception("Source world was overwritten by load hook");
        if(Directory.GetFiles(auditDir.FullName,"*.snapshot").Length!=0||Directory.GetFiles(auditDir.FullName).Length!=2)throw new Exception("Load created unexpected files");
        if(File.ReadAllText(Path.Combine(auditDir.FullName,"other-mod-state"))!="preserved")throw new Exception("Other mod state changed");
        gateChecks.Add(new{name="actual-120-native-hook-two-loads-preserve-items-without-automatic-backups",ok=true});
        foreach(var entry in entries.Where(e=>e.FilenameInZip.StartsWith("Assets/")&&(e.FilenameInZip.EndsWith(".xml")||e.FilenameInZip.EndsWith(".xdb")))){
            using var data=new MemoryStream();archive.ExtractFile(entry,data);data.Position=0;
            XElement.Load(data);gateChecks.Add(new{name="raw-xml/"+entry.FilenameInZip,ok=true});
        }
    }catch(Exception e){gateFailed=1;gateChecks.Add(new{error=e.ToString()});Console.Error.WriteLine(e.Message);}
    File.WriteAllText(args[2],JsonSerializer.Serialize(new{failed=gateFailed,checks=gateChecks},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"World resource gate: {gateChecks.Count} gateChecks, {gateFailed} failures");return gateFailed;
}
if(args.Length==4&&args[0]=="--archive-identical"){
    // Verify every recompressed member using the game's own ZIP implementation,
    // against the previously accepted package, not merely a second desktop unzip.
    using var original=System.IO.Compression.ZipFile.OpenRead(args[1]);
    using var native=Game.ZipArchive.Open(File.OpenRead(args[2]));
    var files=native.ReadCentralDir();
    if(!files.Select(f=>f.FilenameInZip).Order().SequenceEqual(original.Entries.Select(e=>e.FullName).Order()))
        throw new Exception("Archive member names differ");
    foreach(var file in files){
        using var data=new MemoryStream();native.ExtractFile(file,data);
        using var reference=original.GetEntry(file.FilenameInZip).Open();
        data.Position=0;
        if(!System.Security.Cryptography.SHA256.HashData(data).SequenceEqual(System.Security.Cryptography.SHA256.HashData(reference)))
            throw new Exception("Native extracted bytes differ: "+file.FilenameInZip);
    }
    File.WriteAllText(args[3],JsonSerializer.Serialize(new{count=files.Count,failed=0,nativeReader="Game.ZipArchive"}));
    Console.WriteLine($"Native ZIP: {files.Count} identical members");return 0;
}
if (args.Length is not (5 or 6)) throw new ArgumentException("TacticalLoadCheck <repo> <Content.zip> <tactical.scmod> <mode> <report> [core.scmod]");
string root=Path.GetFullPath(args[0]), mode=args[3];
Dispatcher.Initialize();
AssemblyLoadContext.Default.Resolving += (context,name) => {
    string path=Path.Combine(AppContext.BaseDirectory,name.Name+".dll");
    return File.Exists(path)?context.LoadFromAssemblyPath(path):null;
};
AppDomain.CurrentDomain.AssemblyResolve += (_,a) => ModsManager.Dlls.GetValueOrDefault(a.Name);
var checks=new List<object>();
void Check(string name,bool ok){checks.Add(new{name,ok});if(!ok)throw new Exception(name);}
ModEntity Package(string path){var mod=new HeadlessMod{ModArchive=Game.ZipArchive.Open(File.OpenRead(path))};mod.InitResources();return mod;}
string Output(string name)=>Path.Combine(root,"output",name);
using var content=System.IO.Compression.ZipFile.OpenRead(args[1]);
int failed=0;
try {
    string coreVersion=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"src/ScCsgoKnives/modinfo.json"))).RootElement.GetProperty("Version").GetString();
    var core=Package(args.Length==6?args[5]:Output($"[API1.9]CS武器{coreVersion}-作者ZH667-全量版.scmod"));
    bool merged=core.ModFiles.ContainsKey("ScCsgoBundle.dll");
    var tactical=merged?core:Package(args[2]);
    var mods=merged?new List<ModEntity>{core}:new List<ModEntity>{core,tactical};
    bool complete=mode is "both" or "reversed" or "legacy" or "reload-disabled";
    bool nmmPresent=complete || mode is "nmm-only" or "disabled" or "outdated";
    bool neoPresent=complete || mode is "neo-only" or "disabled" or "outdated";
    ModEntity nmm=null,neo=null,legacy=null;
    if(nmmPresent){nmm=Package(Environment.GetEnvironmentVariable("SC_NMM_CHECK_PACKAGE")??Output("[API1.9]NekoMekoModel1.1-源码构建.scmod"));mods.Add(nmm);}
    if(neoPresent){
        string source=Path.Combine(root,".tmp/creature-audit-20260917/10");
        neo=new ModEntity{modInfo=ModsManager.DeserializeJson(File.ReadAllText(Path.Combine(source,"modinfo.json")))};
        var assembly=Assembly.Load(File.ReadAllBytes(Path.Combine(source,"neorxna.dll")));
        ModsManager.Dlls[assembly.FullName]=assembly;mods.Add(neo);
    }
    if(mode=="disabled")nmm.IsDisabled=true;
    if(mode=="outdated")neo.modInfo.Version="1.3";
    if(mode=="legacy"){
        legacy=Package(Path.Combine(root,"tools/fixtures/appearance-1.1.0.scmod"));mods.Add(legacy);
    }
    foreach(var mod in mods.Where(m=>!m.IsDisabled))ModsManager.ModList.Add(mod);
    var assemblies=new Dictionary<ModEntity,Assembly[]>();
    foreach(var mod in mods.Where(m=>!m.IsDisabled)){
        assemblies[mod]=mod.GetAssemblies();
        foreach(var a in assemblies[mod])ModsManager.Dlls[a.FullName]=a;
    }
    var tacticalAssembly=assemblies[tactical].Single(a=>a.GetName().Name=="ScCsgoTactical");
    var expectedRootAssemblies=new List<string>{"ScCsgoBundle","ScCsgoKnives","ScCsgoResources","ScCsgoTactical"};
    if(core.ModFiles.ContainsKey("ScCsgoVoice.dll"))expectedRootAssemblies.Add("ScCsgoVoice");
    var expectedTacticalAssemblies=new List<string>{"ScCsgoTactical"};
    if(tactical.ModFiles.ContainsKey("ScCsgoVoice.dll"))expectedTacticalAssemblies.Add("ScCsgoVoice");
    Check("native scanner sees expected root DLLs",merged?
        assemblies[core].Select(a=>a.GetName().Name).Order().SequenceEqual(expectedRootAssemblies.Order()):assemblies[tactical].Select(a=>a.GetName().Name).Order().SequenceEqual(expectedTacticalAssemblies.Order()));
    Check("tactical has no framework references",tacticalAssembly.GetReferencedAssemblies().All(a=>a.Name is not ("sc-nekomekomodel" or "neorxna" or "ScCsgoAppearance")));
    Check("native dependency declaration",merged?core.modInfo.DependencyRanges.Count==0:tactical.modInfo.DependencyRanges.Count==1&&tactical.modInfo.DependencyRanges.ContainsKey(core.modInfo.PackageName));
    foreach(var mod in mods.Where(m=>!m.IsDisabled))mod.CombineContent();
    if(complete&&mode!="reversed")foreach(var a in assemblies[nmm])nmm.HandleAssembly(a);
    if(merged)foreach(var a in assemblies[core])core.HandleAssembly(a);
    else {foreach(var a in assemblies[core])core.HandleAssembly(a);foreach(var a in assemblies[tactical])tactical.HandleAssembly(a);}
    if(complete&&mode=="reversed")foreach(var a in assemblies[nmm])nmm.HandleAssembly(a);
    if(legacy!=null)foreach(var a in assemblies[legacy])legacy.HandleAssembly(a);
    var appearance=ModsManager.ModLoaders.Where(l=>l.GetType().FullName=="Game.AppearanceModLoader").ToArray();
    Check("appearance loader count",appearance.Length==(complete?1:0));
    Check("tactical gameplay loader survives",tactical.Loaders.Count(l=>l.GetType().Name=="TacticalModLoader")==1&&new[]{core,tactical}.Distinct().SelectMany(m=>m.BlockTypes).Count(t=>t.Name.StartsWith("ScTactical"))>=4);
    if(merged){
        var loader=core.Loaders.Single(l=>l.GetType().Name=="BundleModLoader");
        Check("alias absent until native assembly loop completes",!ModsManager.GetModEntity("zh667.ScCsgoTactical",out _));
        var actions=new List<Action>();loader.OnLoadingFinished(actions);foreach(var action in actions)action();foreach(var action in actions)action();
        Check("core remains primary loader",core.Loader.GetType().Name=="ScCsgoKnivesModLoader");
        Check("legacy tactical identity resolves once",ModsManager.GetModEntity("zh667.ScCsgoTactical",out var identity)&&ModsManager.ModList.Count(m=>m.modInfo.PackageName=="zh667.ScCsgoTactical")==1);
        Check("identity never duplicates gameplay or resources",identity.ModArchive==null&&identity.Loaders.Count==0&&identity.BlockTypes.Count==0&&identity.ModFiles.Count==0);
        var used=new SubsystemUsedMods();var saved=new ValuesDictionary();used.Save(saved);
        for(int round=0;round<2;round++){
            // In deliberately incomplete framework modes, the host has scanned an
            // NMM DLL whose dependency is absent (the real engine disables it).
            // Do not make the global converter scan this invalid fixture assembly.
            bool xmlRoundtrip=complete||mode=="none";
            if(xmlRoundtrip){var xml=new XElement("Values");saved.Save(xml);saved=new ValuesDictionary();saved.ApplyOverrides(XElement.Parse(xml.ToString()));}
            else used.Save(saved);
            var entries=saved.GetValue<ValuesDictionary>("Mods");var identities=Enumerable.Range(0,saved.GetValue<int>("ModsCount")).Select(i=>entries.GetValue<ValuesDictionary>(i.ToString())).ToArray();
            Check("native UsedMods "+(xmlRoundtrip?"XML roundtrip ":"identity save ")+round,identities.Count(v=>v.GetValue<string>("PackageName")=="zh667.ScCsgoKnives")==1&&identities.Count(v=>v.GetValue<string>("PackageName")=="zh667.ScCsgoTactical"&&v.GetValue<string>("Version")==identity.modInfo.Version)==1);
        }
        ModsManager.ModList.Remove(identity);var duplicate=new ModEntity{modInfo=identity.modInfo};ModsManager.ModList.Add(duplicate);
        bool refused=false;try{loader.__ModInitialize();}catch(InvalidOperationException){refused=true;}finally{ModsManager.ModList.Remove(duplicate);ModsManager.ModList.Add(identity);}
        Check("duplicate tactical identity refuses loading",refused);
    }
    var initialize=tacticalAssembly.GetType("Game.TacticalAppearanceIntegration").GetMethod("Initialize");
    initialize.Invoke(null,[tactical]);
    Check("initialization does not duplicate hooks",ModsManager.ModLoaders.Count(l=>l.GetType().FullName=="Game.AppearanceModLoader")==appearance.Length);
    if(complete){
        Check("appearance belongs to correct package",ReferenceEquals(appearance[0].Entity,legacy??tactical));
        Check("preview registered exactly once",ModsManager.m_tempModHooks["OnPlayerModelWidgetMeasureOverride"].UnorderedItems.Count(i=>i.Element.GetType().FullName=="Game.AppearanceModLoader")==1);
        var keys=ContentManager.List("NekoMekoRes/NekoResModel").Select(c=>{
            using var stream=c.Duplicate();using var json=JsonDocument.Parse(stream);return json.RootElement.GetProperty("Key").GetString();
        }).Where(k=>k.StartsWith("zh667.cs.")).ToArray();
        Check("merged resources keep unique saved model keys",keys.Order().SequenceEqual(new[]{"zh667.cs.ct","zh667.cs.t"}));
        using var stream=content.Entries.Single(e=>e.FullName.EndsWith("Database.xml")).Open();
        var db=XElement.Load(stream);
        // Match native default load ordering; the merged core now also owns appearance.
        foreach(var mod in new[]{core,nmm,tactical,legacy}.Where(m=>m!=null).Distinct().OrderBy(m=>m.modInfo.LoadOrder))mod.LoadXdb(ref db);
        DatabaseManager.LoadDataBaseFromXml(db);
        var player=DatabaseManager.FindEntityValuesDictionary("Player",true);
        foreach(var pair in new[]{("NekoMekoModel","Game.ComponentCsPlayerAppearance"),("NekoHUD","Game.ComponentCsAppearanceHud")}){
            string className=player.GetValue<ValuesDictionary>(pair.Item1).GetValue<string>("Class");
            Check("native database replacement "+pair.Item1,className==pair.Item2);
            Check("native type cache resolves "+pair.Item1,TypeCache.FindType(className,true,true).Assembly==appearance[0].GetType().Assembly);
        }
        if(mode=="reload-disabled"){
            nmm.IsDisabled=true;ModsManager.ModList.Remove(nmm);
            var next=Package(args[2]);next.HandleAssembly(tacticalAssembly);
            Check("stale assemblies do not enable disabled integration",next.Loaders.All(l=>l.GetType().FullName!="Game.AppearanceModLoader"));
        }
    }else Check("optional payload not loaded",ModsManager.Dlls.Values.All(a=>a.GetName().Name!="ScCsgoAppearance"));
}catch(Exception e){failed=1;checks.Add(new{error=e.ToString()});Console.Error.WriteLine(e);}
File.WriteAllText(args[4],JsonSerializer.Serialize(new{mode,failed,checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{mode}: {checks.Count} checks, {failed} failures");return failed;

sealed class HeadlessMod:ModEntity {
    // Icon upload requires a GPU; every archive/assembly/database operation remains native.
    public override void LoadIcon(Stream stream) { }
}
