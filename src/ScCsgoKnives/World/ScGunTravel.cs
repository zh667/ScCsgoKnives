using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>Multi-world mods can copy player XML but omit our world registry. Transport only records
/// referenced by carried inventory slots. Never merge two worlds' whole registries or infer missing data.</summary>
public static class ScGunTravel {
    public const string SourcePath="GunTravelSource";
    public const string WorldIdentity="GunTravelWorldIdentity";
    public const string Packet="ScGunTravel", Identities="GunTravelIdentities", Backup="GunTravelBackup";
    static XElement Group(XElement p,string n)=>p?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")==n);
    static string Text(XElement p,string n)=>(string)p?.Elements("Value").SingleOrDefault(e=>(string)e.Attribute("Name")==n)?.Attribute("Value");
    static XElement Field(string n,object v)=>new("Value",new XAttribute("Name",n),new XAttribute("Type",v is int?"int":"string"),new XAttribute("Value",Convert.ToString(v,CultureInfo.InvariantCulture)));
    static XElement Make(string n)=>new("Values",new XAttribute("Name",n));
    static XElement Guns(XElement p)=>Group(p.Element("Subsystems"),"ScGunBlockBehavior");
    static bool Player(XElement e)=>Group(e,"Player") is not null || Group(e,Packet) is not null || (string)e.Attribute("Name") is "MalePlayer" or "FemalePlayer";
    static string Canonical(string path)=>(path??"").Replace('\\','/').TrimEnd('/');
    static int Index(XElement p) {
        var entries=Group(p.Element("Subsystems"),"BlocksManager")?.Elements("Value").Where(e=>(string)e.Attribute("Value")=="ScGunBlock").ToArray();
        return entries?.Length==1?int.Parse((string)entries[0].Attribute("Name"),CultureInfo.InvariantCulture):-1;
    }
    static IEnumerable<(XElement Field,int Id,int Variant)> Items(XElement entity,int block) {
        foreach(var inventory in entity.Elements("Values")) {
            if((string)inventory.Attribute("Name")==Packet)continue;
            var slots=Group(inventory,"Slots");if(slots is null)continue;
            foreach(var slot in slots.Elements("Values")) {
                if(!int.TryParse(Text(slot,"Contents"),out int value))continue;
                int data=Terrain.ExtractData(value);if(Terrain.ExtractContents(value)!=block||GunSpec.IsFresh(data))continue;
                string countText=Text(slot,"Count");int count;
                // Native ComponentCreativeInventory.Save writes Contents ONLY for each open slot.
                // Do not infer this for a malformed survival/custom inventory.
                if(countText is null && (string)inventory.Attribute("Name")=="CreativeInventory")count=1;
                else if(!int.TryParse(countText,out count))throw new InvalidOperationException("携带枪械的库存数量格式不明确，未迁移");
                if(count<=0)continue;
                if(GunSpec.IsForeign(data)||count!=1)throw new InvalidOperationException("跨世界枪械存在未知编码或堆叠实例，未迁移");
                yield return(slot.Elements("Value").Single(e=>(string)e.Attribute("Name")=="Contents"),GunSpec.GetId(data),GunSpec.GetVariant(data));
            }
        }
    }
    static string Token(string world,int id)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world+"|"+id)));
    static Dictionary<string,string> Map(XElement g)=>g?.Elements("Value").ToDictionary(e=>(string)e.Attribute("Name"),e=>(string)e.Attribute("Value"))??[];
    static void Replace(XElement parent,XElement value){Group(parent,(string)value.Attribute("Name"))?.Remove();parent.Add(value);}
    static bool Related(string a,string b) {
        a=a.Replace('\\','/').TrimEnd('/');b=b.Replace('\\','/').TrimEnd('/');
        return a==b+"/Tartareosity"||b==a+"/Tartareosity";
    }
    public static void CaptureSaved(XElement project) {
        string world=Text(Guns(project),SourcePath);
        if(!string.IsNullOrWhiteSpace(world))Capture(project,world);
    }
    // Adds transport metadata to the engine's detached save XML only. No inventory/record is mutated.
    public static void Capture(XElement project,string world) {
        world=Canonical(world);
        var gun=Guns(project);if(Text(gun,"GunDataLayout")!="5")return;
        var registry=Group(gun,"GunRegistry");
        if(!int.TryParse(Text(registry,"Schema"),out int schema)||schema is not (ScGunRegistry.SchemaTenLevels or ScGunRegistry.Schema))return;
        var rows=Map(Group(registry,"Records"));var identities=Map(Group(gun,Identities));int block=Index(project);
        if(block<0)return;
        string worldId=Text(gun,WorldIdentity);
        // Old identities are preserved verbatim. New identities use a persisted world UUID, not a reusable folder name.
        string seed=Guid.TryParse(worldId,out var uuid)?uuid.ToString("N"):world;
        foreach(string id in rows.Keys)if(int.TryParse(id,out int number)&&!identities.ContainsKey(id))identities[id]=Token(seed,number);
        var allIds=Make(Identities);foreach(var kv in identities)allIds.Add(Field(kv.Key,kv.Value));Replace(gun,allIds);
        foreach(var player in project.Element("Entities")?.Elements().Where(Player)??[]) {
            // v1 packets are the immutable old schema-3 protocol. Version 2 explicitly identifies schema 4,
            // so an old build refuses instead of quarantining Lv11+ guns as if their records were corrupt.
            var packet=Make(Packet);packet.Add(Field("Version",schema==ScGunRegistry.SchemaTenLevels?1:3),Field("Source",world),Field("GrowthMode",Text(registry,"GrowthMode")??"Unset"));
            if(schema==ScGunRegistry.Schema)packet.Add(Field("Schema",schema),Field("Layout",GunSpec.DataLayout));
            var carried=Make("Records");var keys=Make("Identities");
            try {
                foreach(var item in Items(player,block).DistinctBy(i=>i.Id)) {
                    string id=item.Id.ToString(CultureInfo.InvariantCulture);
                    if(!rows.TryGetValue(id,out var row))throw new InvalidOperationException("枪械记录缺失，禁止跨世界重建新枪");
                    carried.Add(Field(id,row));keys.Add(Field(id,identities.GetValueOrDefault(id)??Token(world,item.Id)));
                }
                // Pending credit/refunds belong to the source world and cannot silently be left behind.
                bool pending=Group(Group(registry,"PendingKills"),"Entries")?.HasElements==true
                    || Group(Group(registry,"Recovery"),"Batches")?.HasElements==true;
                if(carried.HasElements && pending)throw new InvalidOperationException("有待结算击杀或物品补偿，请结算后再传送");
            }catch(Exception e){packet.Add(Field("Error",e.Message));}
            packet.Add(carried,keys);Replace(player,packet);
        }
        KnifeLog.Information($"[GUN_TRAVEL_04110] saved world={world}; carried records="+project.Element("Entities")?.Elements().Where(Player).Sum(p=>Group(Group(p,Packet),"Records")?.Elements().Count()??0));
    }
    public static void ValidateCaptured(XElement project,string world) {
        int block=Index(project);if(block<0)throw new InvalidOperationException("枪械方块映射缺失");
        foreach(var player in project.Element("Entities")?.Elements().Where(Player)??[]) {
            var packet=Group(player,Packet);var items=Items(player,block).ToArray();
            var rows=Map(Group(packet,"Records"));var identities=Map(Group(packet,"Identities"));
            if(Text(packet,"Version")!="3"||Text(packet,"Schema")!=ScGunRegistry.Schema.ToString()||Text(packet,"Layout")!=GunSpec.DataLayout.ToString()
                ||Canonical(Text(packet,"Source"))!=Canonical(world)||Text(packet,"Error") is not null
                || rows.Count!=items.Length||identities.Count!=rows.Count||items.Select(i=>i.Id).Distinct().Count()!=items.Length
                ||items.Any(i=>!rows.ContainsKey(i.Id.ToString(CultureInfo.InvariantCulture))||!identities.ContainsKey(i.Id.ToString(CultureInfo.InvariantCulture))))
                throw new InvalidOperationException("携带枪械与快照数量或身份不符，禁止以 0 把枪继续穿越");
        }
    }
    public sealed record Plan(XElement Document,int Guns);
    static bool CompleteWorldCopy(XElement doc,XElement player) {
        // A restored/copied WORLD may keep its old path in both the player packet and the
        // frozen world metadata. Its registry is already local; never treat that as a foreign
        // player-only import. Legacy Ghoul WorldPath supplies the pre-0.41.8 provenance.
        var gun=Guns(doc);var packet=Group(player,Packet);
        string version=Text(packet,"Version");
        if(version is not ("1" or "2" or "3") || version=="3"&&(Text(packet,"Schema")!=ScGunRegistry.Schema.ToString()||Text(packet,"Layout")!=GunSpec.DataLayout.ToString()))return false;
        string stored=Text(gun,SourcePath)??Text(Group(doc.Element("Subsystems"),"Tartareosity"),"WorldPath");
        if(string.IsNullOrWhiteSpace(stored)||Canonical(stored)!=Canonical(Text(packet,"Source")))return false;
        int block=Index(doc);if(block<0)return false;
        var fields=new ValuesDictionary();var table=Group(gun,"GunRegistry");if(table is null)return false;
        fields.ApplyOverrides(table);var registry=ScGunRegistry.Load(fields,0);
        if(registry.Disabled||registry.QuarantinedCount>0)return false;
        var rows=Map(Group(table,"Records"));var packetRows=Map(Group(packet,"Records"));
        var ids=Map(Group(gun,Identities));var packetIds=Map(Group(packet,"Identities"));
        return Items(player,block).All(i=>registry.TryGetSnapshot(i.Id,out var s)&&s.Variant==i.Variant)
            &&packetRows.All(p=>rows.GetValueOrDefault(p.Key)==p.Value)
            &&packetIds.All(p=>ids.GetValueOrDefault(p.Key)==p.Value);
    }
    public static Plan Prepare(XElement source,string target) {
        var doc=new XElement(source);var players=doc.Element("Entities")?.Elements().Where(Player).ToArray()??[];
        var incoming=players.Where(p=>Group(p,Packet) is {} packet && Canonical(Text(packet,"Source"))!=Canonical(target)&&!CompleteWorldCopy(doc,p)).ToArray();
        if(incoming.Length==0)return null;
        int block=Index(doc);if(block<0)throw new InvalidOperationException("跨世界枪械方块映射不明确");
        var gun=Guns(doc);if(gun is null){gun=Make("ScGunBlockBehavior");doc.Element("Subsystems").Add(gun);}
        var values=new ValuesDictionary();values.ApplyOverrides(gun);ScGunSaveGuard.Validate(values);
        var registry=ScGunRegistry.Load(values.GetValue<ValuesDictionary>("GunRegistry",null),0);
        if(registry.Disabled||registry.QuarantinedCount>0)throw new InvalidOperationException("目标枪械表异常，拒绝跨世界迁移");
        var saved=registry.Save(0);var records=saved.GetValue<ValuesDictionary>("Records");int next=registry.Next;
        var effectiveMode=registry.GrowthMode;
        var identities=Map(Group(gun,Identities));var reverse=identities.ToDictionary(p=>p.Value,p=>int.Parse(p.Key,CultureInfo.InvariantCulture));
        var used=new HashSet<int>();
        // Any target-world holder outside the copied players protects its record against replacement.
        foreach(var entity in doc.Element("Entities")?.Elements().Where(e=>!incoming.Contains(e))??[])
            foreach(var item in Items(entity,block))used.Add(item.Id);
        // Terrain/container/pickable encodings are not all inventories: conservatively protect any
        // matching encoded value outside incoming player nodes, including unknown mod containers.
        var outside=new XElement(doc);
        var outsideEntities=outside.Element("Entities")?.Elements().ToArray()??[];
        var originalEntities=doc.Element("Entities")?.Elements().ToArray()??[];
        for(int i=0;i<originalEntities.Length;i++)if(incoming.Contains(originalEntities[i]))outsideEntities[i].Remove();
        foreach(var f in outside.Descendants("Value"))if(int.TryParse((string)f.Attribute("Value"),out int v)&&Terrain.ExtractContents(v)==block&&!GunSpec.IsFresh(Terrain.ExtractData(v)))used.Add(GunSpec.GetId(Terrain.ExtractData(v)));
        var seen=new HashSet<string>();int count=0;
        foreach(var player in incoming) {
            var packet=Group(player,Packet);string origin=Text(packet,"Source");
            string version=Text(packet,"Version");
            if(version is not ("1" or "2" or "3") || string.IsNullOrWhiteSpace(origin)
                || version!="3"&&!Related(origin,target) || Text(packet,"Error") is not null)
                throw new InvalidOperationException("跨世界携带快照不兼容或未就绪："+Text(packet,"Error"));
            if(version=="3"&&(Text(packet,"Schema")!=ScGunRegistry.Schema.ToString()||Text(packet,"Layout")!=GunSpec.DataLayout.ToString()))
                throw new InvalidOperationException("跨世界快照的数据版本不受支持");
            var rows=Map(Group(packet,"Records"));var keys=Map(Group(packet,"Identities"));
            if(rows.Count!=keys.Count)throw new InvalidOperationException("跨世界记录与身份数量不一致");
            var carriedItems=Items(player,block).ToArray();
            if(rows.Count!=carriedItems.Length)throw new InvalidOperationException("携带物品与快照数量不一致，未导入");
            if(!Enum.TryParse<ScGunGrowthMode>(Text(packet,"GrowthMode"),out var incomingMode)||!Enum.IsDefined(incomingMode))throw new InvalidOperationException("跨世界成长规则无效");
            var proof=new ValuesDictionary();proof.SetValue("Schema",version=="1"?ScGunRegistry.SchemaTenLevels:ScGunRegistry.Schema);proof.SetValue("Next",1023);
            var proofRows=new ValuesDictionary();foreach(var row in rows)proofRows.SetValue(row.Key,row.Value);proof.SetValue("Records",proofRows);
            var parsed=ScGunRegistry.Load(proof,0);if(parsed.QuarantinedCount>0||parsed.Count!=rows.Count||parsed.Disabled)throw new InvalidOperationException("携带枪械快照损坏");
            foreach(var item in carriedItems) {
                string old=item.Id.ToString(CultureInfo.InvariantCulture);
                if(!rows.TryGetValue(old,out var row)||!keys.TryGetValue(old,out var identity)||identity.Length!=64||!identity.All(Uri.IsHexDigit)||!seen.Add(identity)||!parsed.TryGetSnapshot(item.Id,out var snap)||snap.Variant!=item.Variant)
                    throw new InvalidOperationException("跨世界枪械身份重复或快照不匹配");
                int id;
                if(reverse.TryGetValue(identity,out id)) {
                    if(used.Contains(id)||!registry.TryGetSnapshot(id,out var existing)||existing.Variant!=item.Variant)throw new InvalidOperationException("目标世界同身份枪仍被其他容器持有，拒绝覆盖");
                } else {if(next>GunSpec.LastId)throw new InvalidOperationException("目标枪械记录表已满，迁移未执行");id=next++;reverse.Add(identity,id);identities[id.ToString(CultureInfo.InvariantCulture)]=identity;}
                records.SetValue(id.ToString(CultureInfo.InvariantCulture),row);
                int value=int.Parse((string)item.Field.Attribute("Value"),CultureInfo.InvariantCulture);
                item.Field.SetAttributeValue("Value",Terrain.ReplaceData(value,GunSpec.WithId(item.Variant,id)).ToString(CultureInfo.InvariantCulture));count++;
            }
            packet.Remove();
            if(carriedItems.Length>0 && effectiveMode==ScGunGrowthMode.Unset) {effectiveMode=incomingMode;saved.SetValue("GrowthMode",effectiveMode.ToString());}
            else if(carriedItems.Length>0 && incomingMode!=ScGunGrowthMode.Unset && incomingMode!=effectiveMode)
                throw new InvalidOperationException("两个世界计数成长规则不同，拒绝静默改变枪械加成");
        }
        saved.SetValue("Next",next);var verified=ScGunRegistry.Load(saved,0);if(verified.Disabled||verified.QuarantinedCount>0)throw new InvalidOperationException("合并后枪械记录校验失败");
        var table=Make("GunRegistry");saved.Save(table);Replace(gun,table);
        var ids=Make(Identities);foreach(var kv in identities)ids.Add(Field(kv.Key,kv.Value));Replace(gun,ids);
        gun.Elements("Value").Where(v=>(string)v.Attribute("Name")=="GunDataLayout").Remove();gun.Add(Field("GunDataLayout",5));
        return new Plan(doc,count);
    }
    public static void BeforeLoad(XElement source,WorldInfo world) {
        var plan=Prepare(source,world.DirectoryName);if(plan is null)return;
        var gun=Guns(plan.Document);
        if(Group(gun,Backup) is null) {
            string snapshot=ScGunSchemaUpgrade.Snapshot(world.DirectoryName,"ScCsgoKnives-before-travel-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
            var backup=Make(Backup);backup.Add(Field("Path",snapshot));gun.Add(backup);
        }
        source.ReplaceNodes(plan.Document.Nodes());KnifeLog.Information($"[GUN_TRAVEL] imported {plan.Guns} carried guns; layout 5, schema {ScGunRegistry.Schema}, all carried state preserved");
    }
}
