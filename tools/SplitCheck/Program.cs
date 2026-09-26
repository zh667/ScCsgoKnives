using System.Reflection;
using System.Xml.Linq;
using System.Text.Json;
using Engine;
using Game;
using TemplatesDatabase;

if(args.Length!=5)throw new ArgumentException("SplitCheck <core> <agents> <Content.zip> <core|agents> <report>");
Dispatcher.Initialize();bool enabled=args[3]=="agents";var checks=new List<string>();int failed=0;
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);}
try {
    ModEntity Package(string path){var m=new HeadlessMod{ModArchive=Game.ZipArchive.Open(File.OpenRead(path))};m.InitResources();ModsManager.ModList.Add(m);return m;}
    var core=Package(args[0]);var mods=new List<ModEntity>{core};if(enabled)mods.Add(Package(args[1]));
    AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>ModsManager.Dlls.GetValueOrDefault(e.Name);
    var assemblies=mods.ToDictionary(m=>m,m=>m.GetAssemblies());
    foreach(var aa in assemblies.Values)foreach(var a in aa)ModsManager.Dlls[a.FullName]=a;
    foreach(var m in mods)foreach(var a in assemblies[m])m.HandleAssembly(a);
    var dll=assemblies[core].Single(a=>a.GetName().Name=="ScCsgoKnives");
    Type T(string n)=>dll.GetType("Game."+n,true);
    Check("split capability agrees with installed package",(bool)T("ScOptionalAgents").GetProperty("Available").GetValue(null)==enabled);
    Check("full catalogue mode",!(bool)T("ScMinimalEdition").GetField("Enabled").GetRawConstantValue()&&(bool)T("ScMinimalEdition").GetField("InspectEnabled").GetRawConstantValue());
    Check("no bundle adapter",!core.ModFiles.ContainsKey("ScCsgoBundle.dll"));
    var blockTypes=mods.SelectMany(m=>m.BlockTypes).ToArray();
    Check("unique native block type names",blockTypes.Select(t=>t.FullName).Distinct().Count()==blockTypes.Length);
    foreach(string n in new[]{"ScTacticalShieldBlock","ScTacticalBeaconBlock","ScTacticalDefuserBlock","ScTacticalSquadBlock","ScChickenEggBlock"}){
        var t=T(n);Check(n+" native registration in core",core.BlockTypes.Contains(t));
        var b=(Block)Activator.CreateInstance(t);int index=700+BlocksManager.BlockTypeToIndex.Count;b.BlockIndex=index;BlocksManager.BlockTypeToIndex[t]=index;BlocksManager.Blocks[index]=b;
        Check(n+" optional creative visibility",b.GetCreativeValues().Any()==enabled);
        if(!enabled){b.Initialize();Check(n+" initialization needs no addon resources",true);Check(n+" no missing behavior",string.IsNullOrEmpty(b.Behaviors));foreach(int data in new[]{0,1,3,1999,2000,6000}){
            int value=Terrain.MakeBlockValue(index,0,data);Check(n+" exact inactive item data "+data,b.SetDamage(value,99)==value&&b.GetDamageDestructionValue(value)==value);
        }}
    }
    if(enabled){
        using var addon=System.IO.Compression.ZipFile.OpenRead(args[1]);
        foreach(var entry in addon.Entries.Where(e=>e.FullName.StartsWith("Assets/Audio/"))){using var input=entry.Open();using var data=new MemoryStream();input.CopyTo(data);data.Position=0;var sound=Engine.Media.SoundData.Load(data);Check("native addon audio "+entry.FullName,sound.Data.Length>0&&sound.SamplingFrequency>=8000&&sound.ChannelsCount>=1);}
        var tactical=assemblies[mods[1]].Single(a=>a.GetName().Name=="ScCsgoTactical");
        foreach(var n in new[]{"ScTacticalShieldBlock","ScTacticalBeaconBlock","ScTacticalDefuserBlock","ScTacticalSquadBlock","TacticalItemMesh"})Check(n+" forwarded identity",tactical.GetType("Game."+n,true)==T(n));
        Check("matching dependency",mods[1].modInfo.DependencyRanges.ContainsKey("zh667.ScCsgoKnives"));
        mods[1].IsDisabled=true;Check("stale assembly cannot activate disabled addon",!(bool)T("ScOptionalAgents").GetProperty("Available").GetValue(null));mods[1].IsDisabled=false;
        ModsManager.Dlls.Remove(dll.FullName);bool refused=false;
        try{tactical.GetType("Game.ScSplitAgentMarker").GetMethod("ValidateCore").Invoke(null,null);}catch(TargetInvocationException e){refused=e.InnerException is InvalidOperationException;}finally{ModsManager.Dlls[dll.FullName]=dll;}
        Check("wrong core gets explicit package requirement",refused);
    }
    T("ScWorkbenchExtension").GetMethod("RegisterBaseRecipes").Invoke(null,null);
    var recipes=(System.Collections.IEnumerable)T("ScWorkbenchExtension").GetProperty("All").GetValue(null);
    Check("chicken recipe follows addon",recipes.Cast<object>().Any(r=>(string)r.GetType().GetProperty("Key").GetValue(r)=="chicken-egg")==enabled);
    if(!enabled){T("SubsystemScChicken").GetMethod("RegisterSpawn").Invoke(null,[null]);Check("absent addon skips natural spawn",true);Check("absent addon skips follow input",!(bool)T("SubsystemScChicken").GetMethod("HandleFollow").Invoke(null,[null,null]));}
    var loader=core.Loaders.Single(l=>l.GetType().Name=="ScCompatibilityModLoader");var actions=new List<Action>();loader.OnLoadingFinished(actions);foreach(var action in actions)action();foreach(var action in actions)action();
    Check("one persistent tactical identity",ModsManager.ModList.Count(m=>m.modInfo.PackageName=="zh667.ScCsgoTactical")==1);
    var identity=ModsManager.ModList.Single(m=>m.modInfo.PackageName=="zh667.ScCsgoTactical");
    Check("inactive alias never supplies gameplay",enabled||identity.ModArchive==null&&identity.BlockTypes.Count==0&&identity.Loaders.Count==0);
    Check("public identity version stays 1.3.0",identity.modInfo.Version=="1.3.0");
    var used=new SubsystemUsedMods();var saved=new ValuesDictionary();used.Save(saved);
    for(int round=0;round<2;round++){var x=new XElement("Values");saved.Save(x);saved=new ValuesDictionary();saved.ApplyOverrides(XElement.Parse(x.ToString()));Check("native UsedMods survives XML "+round,saved.GetValue<int>("ModsCount")==2);}
    using var content=System.IO.Compression.ZipFile.OpenRead(args[2]);using var stream=content.Entries.Single(e=>e.FullName.EndsWith("Database.xml")).Open();var database=XElement.Load(stream);
    foreach(var m in mods)m.LoadXdb(ref database);DatabaseManager.LoadDataBaseFromXml(database);
    foreach(string name in new[]{"ScTacticalCT","ScTacticalT","ScTacticalEnemy","ScTacticalHostage","ScCsgoChicken"})Check(name+" native entity availability",(DatabaseManager.FindEntityValuesDictionary(name,false)!=null)==enabled);
    Check("dormant native template always available",DatabaseManager.FindEntityValuesDictionary("ScCompatibilityDormant",true)!=null);
    var compat=T("ScCompatibility");var manifest=(XElement)compat.GetField("Manifest").GetValue(null);
    Check("actual compatibility manifest loaded",manifest.Elements("Entity").Count()==6);
    var project=new XElement("Project",new XElement("Subsystems"),new XElement("Entities",new XAttribute("NextID",100)));
    int id=10;foreach(var e in manifest.Elements("Entity").Where(e=>(string)e.Attribute("Name")!="ScCompatibilityDormant"))
        project.Element("Entities").Add(new XElement("Entity",new XAttribute("Id",id++),new XAttribute("Name",(string)e.Attribute("Name")),new XAttribute("Guid",(string)e.Attribute("Guid")),new XElement("Values",new XAttribute("Name","Future"),new XElement("Value",new XAttribute("Name","Opaque"),new XAttribute("Type","string"),new XAttribute("Value","retain original companion/chicken state")))));
    var original=new XElement(project);var prepare=compat.GetMethod("Prepare");
    Func<Guid,bool> actual=g=>DatabaseManager.GameDatabase.Database.FindDatabaseObject(g,DatabaseManager.GameDatabase.EntityTemplateType,false)!=null;
    object plan=prepare.Invoke(null,[project,"split",manifest,actual]);XElement Document(object p)=>(XElement)p.GetType().GetProperty("Document").GetValue(p);
    int Dormant(object p)=>(int)p.GetType().GetProperty("Dormant").GetValue(p);
    Check("all optional entities follow native availability",Dormant(plan)==(enabled?0:5));
    for(int round=0;round<2;round++){var doc=Document(plan);compat.GetMethod("PreserveOpaque").Invoke(null,[doc]);plan=prepare.Invoke(null,[XElement.Parse(doc.ToString()),"split",manifest,actual]);}
    var restore=prepare.Invoke(null,[Document(plan),"with-agents",manifest,(Func<Guid,bool>)(_=>true)]);
    Check("addon readdition restores exact entity IDs and payloads",XNode.DeepEquals(Document(restore).Element("Entities"),original.Element("Entities")));
    Check("detached conversion leaves source unchanged",XNode.DeepEquals(original,project));
}catch(Exception e){failed=1;Console.Error.WriteLine(e);checks.Add(e.ToString());}
File.WriteAllText(args[4],JsonSerializer.Serialize(new{mode=args[3],failed,checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"split {args[3]}: {checks.Count} checks, failed={failed}");return failed;
sealed class HeadlessMod:ModEntity {public override void LoadIcon(Stream stream) {}}
