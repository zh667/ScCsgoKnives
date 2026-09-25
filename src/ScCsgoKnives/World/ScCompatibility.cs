using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>Stable compatibility family 1. The capsule contains opaque XML, not guessed defaults.
/// Future releases must preserve this reader contract and pass all previous family writers.</summary>
public static class ScCompatibility {
    public const int Protocol = 1;
    public const string Key = "ScCompatibility", DormantComponent = "ScCompatibilityArchive";
    public const string DormantGuid = "7807c7ed-ce6a-4fdf-aad7-782480bf5e83";
    public static string ActiveBuild = "1.6.0";
    public static string VerifiedBackup;
    public static XElement Manifest = new("Compatibility", new XAttribute("Protocol", Protocol));
    internal static XElement Group(XElement p,string name)=>p?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")==name);
    internal static string Text(XElement p,string name)=>(string)p?.Elements("Value").SingleOrDefault(e=>(string)e.Attribute("Name")==name)?.Attribute("Value");
    internal static XElement Field(string name,object value)=>new("Value",new XAttribute("Name",name),new XAttribute("Type",value is int?"int":"string"),new XAttribute("Value",value));
    static void Set(XElement p,string name,object value){p.Elements("Value").Where(e=>(string)e.Attribute("Name")==name).Remove();p.Add(Field(name,value));}
    public sealed record Plan(XElement Document,int Dormant,int Restored,bool Switching);
    static XElement Capsule(XElement project) {
        string raw=Text(Group(project.Element("Subsystems"),Key),"Capsule");
        var value=raw is null?new XElement("Capsule",new XAttribute("Protocol",Protocol)):XElement.Parse(raw);
        if((int?)value.Attribute("Protocol")!=Protocol)throw new InvalidOperationException("不支持的双向兼容协议，原数据未改动");
        return value;
    }
    static HashSet<string> Owned(XElement manifest,string kind)=>manifest.Elements(kind).Select(e=>(string)e.Attribute("Name")).Where(n=>!string.IsNullOrEmpty(n)).ToHashSet(StringComparer.Ordinal);
    static XElement MergeManifest(XElement a,XElement b){
        var merged=new XElement("Compatibility",new XAttribute("Protocol",Protocol));
        foreach(var e in a.Elements().Concat(b.Elements()).DistinctBy(e=>e.Name+":"+(string)e.Attribute("Name")))merged.Add(new XElement(e));
        return merged;
    }
    /// <summary>Pure staging. A dormant entity retains its engine ID so old worlds cannot reuse it.</summary>
    public static Plan Prepare(XElement original,string build,XElement definitions,Func<Guid,bool> available) {
        var doc=new XElement(original);var subs=doc.Element("Subsystems")??throw new InvalidOperationException("世界缺少Subsystems");
        var previous=Group(subs,Key);var capsule=Capsule(doc);
        var manifest=MergeManifest(capsule.Element("Compatibility")??new XElement("Compatibility"),definitions);
        capsule.Element("Compatibility")?.Remove();capsule.Add(manifest);
        var ownedSubs=Owned(manifest,"Subsystem");var ownedComponents=Owned(manifest,"Component");
        var entityIds=manifest.Elements("Entity").Select(e=>(string)e.Attribute("Guid")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Preserve only absent owned groups, never reinsert completed native transactions or deleted records.
        var opaque=new XElement("Opaque");
        if((bool?)definitions.Attribute("AppearanceAvailable")!=true) {
            // NMM falls back when our role descriptor is unavailable. Preserve only our selected keys,
            // not unrelated third-party appearance data or player choices in other fields.
            foreach(var field in doc.Descendants("Value").Where(e=>((string)e.Attribute("Value"))?.StartsWith("zh667.cs.",StringComparison.Ordinal)==true)) {
                var entity=field.Ancestors("Entity").FirstOrDefault();
                var path=field.Ancestors("Values").Reverse().Select(e=>(string)e.Attribute("Name")).ToArray();
                if(path.Length==0)continue;
                opaque.Add(new XElement("Appearance",new XAttribute("Entity",(string)entity?.Attribute("Id")??""),
                    path.Select(n=>new XElement("Part",new XAttribute("Name",n))),new XElement(field)));
            }
        }
        foreach(var group in subs.Elements("Values").Where(e=>ownedSubs.Contains((string)e.Attribute("Name"))&&(string)e.Attribute("Name")!=Key))
            opaque.Add(new XElement("Subsystem",new XElement(group)));
        int dormant=0,restored=0;
        var entities=doc.Element("Entities");
        foreach(var entity in entities?.Elements("Entity").ToArray()??[]) {
            var archive=Group(entity,DormantComponent);
            if(archive is not null) {
                var payload=XElement.Parse(Text(archive,"Payload")??throw new InvalidOperationException("休眠实体缺少原始记录"));
                if((string)payload.Attribute("Id")!=(string)entity.Attribute("Id"))throw new InvalidOperationException("休眠实体身份不匹配");
                if(Guid.TryParse((string)payload.Attribute("Guid"),out var restoredId)&&available(restoredId)) {
                    entity.ReplaceWith(payload);restored++;
                } else dormant++;
                continue;
            }
            string guid=(string)entity.Attribute("Guid");
            if(entityIds.Contains(guid)&&Guid.TryParse(guid,out var id)&&!available(id)) {
                var proxy=new XElement("Entity",new XAttribute("Id",(string)entity.Attribute("Id")??throw new InvalidOperationException("实体缺少ID")),
                    new XAttribute("Guid",DormantGuid),new XAttribute("Name","ScCompatibilityDormant"),
                    new XElement("Values",new XAttribute("Name",DormantComponent),Field("Payload",entity.ToString(SaveOptions.DisableFormatting))));
                entity.ReplaceWith(proxy);dormant++;continue;
            }
            foreach(var component in entity.Elements("Values").Where(e=>ownedComponents.Contains((string)e.Attribute("Name"))))
                opaque.Add(new XElement("Component",new XAttribute("Entity",(string)entity.Attribute("Id")??""),new XElement(component)));
        }
        capsule.Element("Opaque")?.Remove();capsule.Add(opaque);
        var state=new XElement("Values",new XAttribute("Name",Key),Field("Protocol",Protocol),Field("Build",build),Field("Capsule",capsule.ToString(SaveOptions.DisableFormatting)),Field("Dormant",dormant));
        if(Text(previous,"Backup") is {} backup)state.Add(Field("Backup",backup));
        bool switching=Text(previous,"Build")!=build;
        previous?.Remove();subs.Add(state);
        return new(doc,dormant,restored,switching);
    }
    /// <summary>Post-save hook uses only the frozen XML, never global/live project state.</summary>
    public static void PreserveOpaque(XElement saved) {
        var capsule=Capsule(saved);
        foreach(var entry in capsule.Element("Opaque")?.Elements()??[]) {
            if(entry.Name=="Appearance") {
                string entity=(string)entry.Attribute("Entity");
                XElement parent=string.IsNullOrEmpty(entity)?saved.Element("Subsystems"):saved.Element("Entities")?.Elements("Entity").SingleOrDefault(e=>(string)e.Attribute("Id")==entity);
                if(parent is null)continue;
                foreach(var part in entry.Elements("Part")) {
                    string partName=(string)part.Attribute("Name");var child=Group(parent,partName);
                    if(child is null){child=new XElement("Values",new XAttribute("Name",partName));parent.Add(child);}parent=child;
                }
                var field=entry.Element("Value");string key=(string)field.Attribute("Name");
                parent.Elements("Value").Where(v=>(string)v.Attribute("Name")==key).Remove();parent.Add(new XElement(field));continue;
            }
            var value=entry.Element("Values");if(value is null)throw new InvalidOperationException("兼容保留节点缺少原数据");
            XElement target=entry.Name=="Subsystem"?saved.Element("Subsystems"):
                saved.Element("Entities")?.Elements("Entity").SingleOrDefault(e=>(string)e.Attribute("Id")==(string)entry.Attribute("Entity"));
            if(target is null)continue; // a real player/entity removal must not resurrect it
            string name=(string)value.Attribute("Name");
            if(Group(target,name) is null)target.Add(new XElement(value));
        }
    }
    internal static void BeforeLoad(XElement source,WorldInfo world) {
        VerifiedBackup=null;
        var definitions=new XElement(Manifest);
        definitions.SetAttributeValue("AppearanceAvailable",ModsManager.Dlls.Values.Any(a=>a.GetType("Game.ComponentCsPlayerAppearance") is not null));
        var staged=Prepare(source,ActiveBuild,definitions,id=>DatabaseManager.GameDatabase.Database.FindDatabaseObject(id,DatabaseManager.GameDatabase.EntityTemplateType,false) is not null);
        // User policy: backups are manual. Switching never creates files or requires an old backup path.
        // Reserve references in the FULL entity XML before any unavailable inventories become opaque strings.
        if(ScGunSchemaUpgrade.SavedSchema(source)>0) {
            var protectedDoc=new XElement(source);
            // Full data of already dormant inventories must participate in reservation too.
            foreach(var entity in protectedDoc.Element("Entities")?.Elements("Entity").ToArray()??[])
                if(Group(entity,DormantComponent) is {} archive)entity.ReplaceWith(XElement.Parse(Text(archive,"Payload")));
            ScGunLoadIntegrity.BeforeLoad(protectedDoc,world,null);
            var gun=Group(staged.Document.Element("Subsystems"),"ScGunBlockBehavior");
            gun?.ReplaceWith(new XElement(Group(protectedDoc.Element("Subsystems"),"ScGunBlockBehavior")));
        }
        source.ReplaceNodes(staged.Document.Nodes());
        if(staged.Switching||staged.Dormant>0||staged.Restored>0)
            KnifeLog.Information($"[CS_COMPAT] build={ActiveBuild}; dormant={staged.Dormant}; restored={staged.Restored}; backups=manual");
    }
}

/// <summary>Serializable carrier for opaque subsystem/component data and the active compatibility build.</summary>
public sealed class SubsystemScCompatibility : Subsystem {
    ValuesDictionary saved;
    public override void Load(ValuesDictionary values){
        int version=values.GetValue<int>("Protocol",ScCompatibility.Protocol);
        if(version!=ScCompatibility.Protocol)throw new InvalidOperationException("不支持的双向兼容协议");
        saved=new ValuesDictionary();foreach(var pair in values)if(pair.Key!="Class")saved.SetValue(pair.Key,pair.Value);
    }
    public override void Save(ValuesDictionary values){
        foreach(var pair in saved)values.SetValue(pair.Key,pair.Value);
        values.SetValue("Protocol",ScCompatibility.Protocol);values.SetValue("Build",ScCompatibility.ActiveBuild);
    }
}
public sealed class ComponentScCompatibilityArchive : Component, IInventory {
    string payload;
    readonly List<(int Value,int Count)> items=[];
    public override void Load(ValuesDictionary values,IdToEntityMap map){
        payload=values.GetValue<string>("Payload");var xml=XElement.Parse(payload);
        foreach(var slots in xml.Descendants("Values").Where(e=>(string)e.Attribute("Name")=="Slots"))
            foreach(var slot in slots.Elements("Values")) {
                if(!int.TryParse(ScCompatibility.Text(slot,"Contents"),out int value))continue;
                string count=ScCompatibility.Text(slot,"Count");
                if(count is null&&(string)slots.Parent?.Attribute("Name")=="CreativeInventory")items.Add((value,1));
                else if(int.TryParse(count,out int n)&&n>0)items.Add((value,n));
            }
    }
    public override void Save(ValuesDictionary values,EntityToIdMap map)=>values.SetValue("Payload",payload);
    Project IInventory.Project=>Project;
    public int SlotsCount=>items.Count;
    public int VisibleSlotsCount{get;set;}
    public int ActiveSlotIndex{get;set;}
    public int GetSlotValue(int i)=>i>=0&&i<items.Count?items[i].Value:0;
    public int GetSlotCount(int i)=>i>=0&&i<items.Count?items[i].Count:0;
    public int GetSlotCapacity(int i,int v)=>0;
    public int GetSlotProcessCapacity(int i,int v)=>0;
    public void AddSlotItems(int i,int v,int n)=>throw new InvalidOperationException("新版实体库存已保留，当前旧版不可修改");
    public int RemoveSlotItems(int i,int n)=>0;
    public void ProcessSlotItems(int i,int v,int c,int p,out int result,out int count){result=v;count=0;}
    public void DropAllItems(Vector3 p){}
}

/// <summary>Unavailable newer items keep their exact type/data and can be moved, but never used/placed/worn.</summary>
public abstract class ScCompatibilityItemBlock : ScNoDurabilityBlock {
    protected ScCompatibilityItemBlock(){IsPlaceable=false;IsCollidable=false;MaxStacking=1;DefaultCategory="CS武器";DefaultTextureSlot=15;DefaultMeleePower=0;DefaultProjectilePower=0;}
    public override IEnumerable<int> GetCreativeValues()=>[];
    public override string GetDisplayName(SubsystemTerrain terrain,int value)=>(DefaultDisplayName??"新版物品")+" · 当前旧版暂不可用";
    public override string GetDescription(int value)=>"原物品类型和状态已保留；换回支持该物品的兼容版后可继续使用。";
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int v,int x,int y,int z){}
    public override void DrawBlock(PrimitivesRenderer3D renderer,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env)=>
        BlocksManager.DrawCubeBlock(renderer,value,new Vector3(size*.35f),ref matrix,color*new Color(130,155,175),color,env);
}

public sealed class ScCompatibilityModLoader : ModLoader {
    ModEntity tacticalAlias;
    public override int Priority=>900;
    public override void __ModInitialize(){
        ScCompatibility.ActiveBuild=Entity.modInfo.Version;
        Entity.GetFile("Assets/ScCompatibilityManifest.xml",s=>ScCompatibility.Manifest=XElement.Load(s));
        if((bool?)ScCompatibility.Manifest.Attribute("Legacy")==true&&ModsManager.ModList.Any(m=>!m.IsDisabled&&m.modInfo.PackageName=="zh667.ScCsgoTactical"))
            throw new InvalidOperationException("旧版兼容包只保留战术数据，不能同时启用依赖新版接口的独立战术拓展；请停用独立拓展或换回最新兼容总包。");
        ModsManager.RegisterHook("ProjectXmlLoad",this,-1000);
        ModsManager.RegisterHook("OnProjectXmlSaved",this,1000);
    }
    public override void ProjectXmlLoad(XElement project,WorldInfo world,ContainerWidget widget){
        try{ScCompatibility.BeforeLoad(project,world);}
        catch(Exception e){
            var subs=project.Element("Subsystems");if(subs is not null&&ScCompatibility.Group(subs,"ScGunBlockBehavior") is null)subs.Add(new XElement("Values",new XAttribute("Name","ScGunBlockBehavior")));
            ScGunSaveGuard.Refuse(project,"双向兼容检查失败，未启用转换："+e.Message);
        }
    }
    public override void OnProjectXmlSaved(XElement project)=>ScCompatibility.PreserveOpaque(project);
    public override void OnLoadingFinished(List<Action> actions){
        if((bool?)ScCompatibility.Manifest.Attribute("Legacy")!=true)return;
        actions.Add(()=>{
            if(ModsManager.ModList.Any(m=>m.modInfo.PackageName=="zh667.ScCsgoTactical"))return;
            // Identity denotes preserved data, not active tactical gameplay. The archive has no loaders/resources.
            tacticalAlias=new ModEntity{modInfo=ModsManager.DeserializeJson("{\"Name\":\"CS战术数据保留（旧版休眠）\",\"Version\":\"1.4.0\",\"ApiVersion\":\"1.9.3.1\",\"PackageName\":\"zh667.ScCsgoTactical\",\"NonPersistentMod\":false}"),IsDependencyChecked=true};
            ModsManager.ModList.Add(tacticalAlias);ModsManager.PackageNameToModEntity["zh667.ScCsgoTactical"]=tacticalAlias;
        });
    }
}
