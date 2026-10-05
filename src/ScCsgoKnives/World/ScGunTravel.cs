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
    static ScGunRegistry Registry(XElement table) {
        var values=new ValuesDictionary();if(table is not null)values.ApplyOverrides(new XElement(table));
        return ScGunRegistry.Load(table is null?null:values,0);
    }
    static int Layout(XElement gun) => int.TryParse(Text(gun,"GunDataLayout"),out int layout)?layout:ScGunEncoding.PreviousLayout;
    static IEnumerable<(XElement Field,int Id,int Variant)> Items(XElement entity,int block,ScGunRegistry registry,int layout) {
        foreach(var inventory in entity.Elements("Values")) {
            if((string)inventory.Attribute("Name")==Packet)continue;
            var slots=Group(inventory,"Slots");if(slots is null)continue;
            foreach(var slot in slots.Elements("Values")) {
                if(!int.TryParse(Text(slot,"Contents"),out int value))continue;
                int data=Terrain.ExtractData(value);if(Terrain.ExtractContents(value)!=block)continue;
                string countText=Text(slot,"Count");int count;
                // Native ComponentCreativeInventory.Save writes Contents ONLY for each open slot.
                // Do not infer this for a malformed survival/custom inventory.
                if(countText is null && (string)inventory.Attribute("Name")=="CreativeInventory")count=1;
                else if(!int.TryParse(countText,out count))throw new InvalidOperationException("携带枪械的库存数量格式不明确，未迁移");
                if(count<=0)continue;
                if(!ScGunEncoding.Decode(data,registry,layout,out int id,out int variant))throw new InvalidOperationException("跨世界枪械编码/记录无法确认，未迁移");
                if(id is GunSpec.FreshFull or GunSpec.FreshEmpty)continue;
                if(count!=1)throw new InvalidOperationException("跨世界枪械存在堆叠实例，未迁移");
                yield return(slot.Elements("Value").Single(e=>(string)e.Attribute("Name")=="Contents"),id,variant);
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
        var gun=Guns(project);if(Layout(gun) is not (ScGunEncoding.PreviousLayout or GunSpec.DataLayout))return;
        var registry=Group(gun,"GunRegistry");
        if(!int.TryParse(Text(registry,"Schema"),out int schema)||schema is not (ScGunRegistry.SchemaTenLevels or ScGunRegistry.SchemaThirtyLevels or ScGunRegistry.SchemaStagedKills or ScGunRegistry.SchemaV5 or ScGunRegistry.Schema))return;
        var rows=Map(Group(registry,"Records"));var identities=Map(Group(gun,Identities));int block=Index(project);
        if(block<0)return;
        var proofValues=new ValuesDictionary();proofValues.ApplyOverrides(new XElement(registry));
        var proofRegistry=ScGunRegistry.Load(proofValues,0);
        string worldId=Text(gun,WorldIdentity);
        // Old identities are preserved verbatim. New identities use a persisted world UUID, not a reusable folder name.
        string seed=Guid.TryParse(worldId,out var uuid)?uuid.ToString("N"):world;
        foreach(string id in rows.Keys)if(int.TryParse(id,out int number)&&!identities.ContainsKey(id))identities[id]=Token(seed,number);
        var allIds=Make(Identities);foreach(var kv in identities)allIds.Add(Field(kv.Key,kv.Value));Replace(gun,allIds);
        foreach(var player in project.Element("Entities")?.Elements().Where(Player)??[]) {
            // v1 packets are the immutable old schema-3 protocol. Version 2 explicitly identifies schema 4,
            // so an old build refuses instead of quarantining Lv11+ guns as if their records were corrupt.
            var packet=Make(Packet);packet.Add(Field("Version",schema==ScGunRegistry.SchemaTenLevels?1:3),Field("Source",world),Field("GrowthMode",Text(registry,"GrowthMode")??"Unset"));
            if(schema!=ScGunRegistry.SchemaTenLevels)packet.Add(Field("Schema",schema),Field("Layout",Layout(gun)));
            var carried=Make("Records");var keys=Make("Identities");
            try {
                var items=Items(player,block,proofRegistry,Layout(gun)).ToArray();
                // Validate every item before deduplicating IDs; a conflicting second model is not a copy.
                foreach(var item in items)
                    if(!proofRegistry.TryGetSnapshot(item.Id,out var state)||state.Variant!=item.Variant)
                        throw new InvalidOperationException($"携带枪械 #{item.Id} 的记录缺失、已隔离或型号冲突，请先恢复原记录；禁止跨世界重建新枪");
                foreach(var item in items.DistinctBy(i=>i.Id)) {
                    string id=item.Id.ToString(CultureInfo.InvariantCulture);
                    if(!rows.TryGetValue(id,out var row))throw new InvalidOperationException("枪械记录缺失，禁止跨世界重建新枪");
                    carried.Add(Field(id,row));keys.Add(Field(id,identities.GetValueOrDefault(id)??Token(world,item.Id)));
                }
                // Pending credit/refunds belong to the source world and cannot silently be left behind.
                bool pending=Group(Group(registry,"PendingKills"),"Entries")?.HasElements==true
                    || Group(Group(registry,"Recovery"),"Batches")?.HasElements==true;
                if(carried.HasElements && pending)throw new InvalidOperationException("有待结算击杀或物品补偿，请结算后再传送");
            }catch(Exception e){
                // Retain any previous recovery evidence, but mark it unusable for transport.
                if(Group(player,Packet) is {} previous) {
                    var preserved=new XElement(previous);
                    preserved.Elements("Value").Where(v=>(string)v.Attribute("Name")=="Error").Remove();
                    // The error belongs to THIS save. Leaving an older destination as Source could
                    // make a later failed transfer look local and bypass the incoming-error check.
                    string oldSource=Text(preserved,"Source");
                    if(oldSource!=world && Text(preserved,"RecoverySource") is null && oldSource is not null)
                        preserved.Add(Field("RecoverySource",oldSource));
                    preserved.Elements("Value").Where(v=>(string)v.Attribute("Name")=="Source").Remove();
                    preserved.Add(Field("Source",world));
                    preserved.Add(Field("Error",e.Message));Replace(player,preserved);continue;
                }
                packet.Add(Field("Error",e.Message));
            }
            packet.Add(carried,keys);Replace(player,packet);
        }
    }
    /// <summary>subworld-travel-generic-20261003: what each player of a saved world carried when it was saved (the packet
    /// <see cref="Capture"/> wrote), as live travel envelopes - one per inventory of the player, with the slot of every
    /// carried gun. A provider that carries only item values and restores them after the destination has loaded (Ancient
    /// World) leaves exactly this behind in the world it left; <see cref="ScTravelArrival"/> takes it from there. A packet
    /// that says the guns could not travel comes back as its error. Nothing is written.</summary>
    internal static List<(string Inventory, ScTravelEnvelope Envelope, string Error)> Departures(XElement project) {
        var found = new List<(string, ScTravelEnvelope, string)>();
        var gun = Guns(project); int block = Index(project);
        string world = Text(gun, WorldIdentity);
        if (gun is null || block < 0 || !Guid.TryParse(world, out _)) return found;
        string seed = Text(Group(project.Element("Subsystems"), "GameInfo"), "TotalElapsedGameTime") ?? "";
        foreach (var player in project.Element("Entities")?.Elements().Where(Player) ?? []) {
            var packet = Group(player, Packet); if (packet is null) continue;
            if (Text(packet, "Error") is { } error) { found.Add(("", null, error)); continue; }
            if (Text(packet, "Version") != "3" || !int.TryParse(Text(packet, "Schema"), out int schema) || !int.TryParse(Text(packet, "Layout"), out int layout)) continue;
            string growth = Text(packet, "GrowthMode") ?? "Unset";
            if (!Enum.TryParse(growth, out ScGunGrowthMode mode)) continue;
            var rows = Map(Group(packet, "Records")); var keys = Map(Group(packet, "Identities"));
            foreach (var inventory in player.Elements("Values")) {
                string name = (string)inventory.Attribute("Name");
                if (name == Packet || Group(inventory, "Slots") is not { } slots) continue;
                var envelope = new ScTravelEnvelope { World = world, Traveller = Text(Group(player, "Player"), "PlayerIndex") ?? "", Schema = schema, Layout = layout, Growth = growth, Block = block };
                string problem = null;
                foreach (var slot in slots.Elements("Values")) {
                    string slotName = (string)slot.Attribute("Name") ?? "";
                    if (!slotName.StartsWith("Slot", StringComparison.Ordinal) || !int.TryParse(slotName[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) continue;
                    if (!int.TryParse(Text(slot, "Contents"), out int value) || Terrain.ExtractContents(value) != block) continue;
                    int count = name == "CreativeInventory" ? 1 : int.TryParse(Text(slot, "Count"), out int c) ? c : 0;
                    if (count <= 0) continue;
                    int data = Terrain.ExtractData(value), id = ScGunEncoding.Id(data);
                    if (id is GunSpec.FreshFull or GunSpec.FreshEmpty) { envelope.Slots.Add(new(index, value, count, "")); continue; }
                    string key = id.ToString(CultureInfo.InvariantCulture);
                    if (!rows.TryGetValue(key, out string row) || !keys.TryGetValue(key, out string identity) || !ScGunRegistry.TryReadRow(row, 0, mode, out var record)
                        || !ScGunEncoding.Extended(data) && (data & 63) != record.Variant || count != 1 || envelope.Guns.Any(g => g.Id == id)) { problem = $"携带包里第 {index + 1} 格的枪 #{id} 与记录不一致"; break; }
                    envelope.Guns.Add(new(identity, id, record.Variant, row));
                    envelope.Slots.Add(new(index, value, 1, identity));
                }
                if (problem is not null) { found.Add((name, null, problem)); continue; }
                if (envelope.Guns.Count == 0) continue;
                // One departure, one transfer: the world left, what was carried, and when that world was saved.
                var content = new StringBuilder(seed);
                foreach (var g in envelope.Guns.OrderBy(g => g.Identity, StringComparer.Ordinal)) content.Append('|').Append(g.Identity).Append(':').Append(g.Row);
                foreach (var sl in envelope.Slots.OrderBy(sl => sl.Slot)) content.Append('|').Append(sl.Slot).Append(':').Append(sl.Value);
                envelope.Transfer = "saved-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world + "|" + content)))[..32];
                found.Add((name, envelope, null));
            }
        }
        return found;
    }
    public static void ValidateCaptured(XElement project,string world) {
        int block=Index(project);if(block<0)throw new InvalidOperationException("枪械方块映射缺失");
        foreach(var player in project.Element("Entities")?.Elements().Where(Player)??[]) {
            var packet=Group(player,Packet);var items=Items(player,block,Registry(Group(Guns(project),"GunRegistry")),Layout(Guns(project))).ToArray();
            var rows=Map(Group(packet,"Records"));var identities=Map(Group(packet,"Identities"));
            if(Text(packet,"Version")!="3"||Text(packet,"Schema")!=Text(Group(Guns(project),"GunRegistry"),"Schema")||!SupportedPacket(packet)
                ||Canonical(Text(packet,"Source"))!=Canonical(world)||Text(packet,"Error") is not null
                || rows.Count!=items.Length||identities.Count!=rows.Count||items.Select(i=>i.Id).Distinct().Count()!=items.Length
                ||items.Any(i=>!rows.ContainsKey(i.Id.ToString(CultureInfo.InvariantCulture))||!identities.ContainsKey(i.Id.ToString(CultureInfo.InvariantCulture))))
                throw new InvalidOperationException("携带枪械与快照数量或身份不符，禁止以 0 把枪继续穿越");
        }
    }
    public sealed record Plan(XElement Document,int Guns);
    static int PacketSchema(XElement packet) => Text(packet,"Version") switch {
        "1" => ScGunRegistry.SchemaTenLevels, "2" => ScGunRegistry.SchemaThirtyLevels,
        "3" => int.TryParse(Text(packet,"Schema"),out int schema) ? schema : -1, _ => -1
    };
    static bool SupportedPacket(XElement packet) => PacketSchema(packet) is ScGunRegistry.SchemaTenLevels or ScGunRegistry.SchemaThirtyLevels or ScGunRegistry.SchemaStagedKills or ScGunRegistry.SchemaV5 or ScGunRegistry.Schema
        && (Text(packet,"Version")!="3" || Text(packet,"Layout")== (PacketSchema(packet)==ScGunRegistry.Schema?GunSpec.DataLayout:ScGunEncoding.PreviousLayout).ToString());
    static bool CompleteWorldCopy(XElement doc,XElement player) {
        // A restored/copied WORLD may keep its old path in both the player packet and the
        // frozen world metadata. Its registry is already local; never treat that as a foreign
        // player-only import. Legacy Ghoul WorldPath supplies the pre-0.41.8 provenance.
        var gun=Guns(doc);var packet=Group(player,Packet);
        string version=Text(packet,"Version");
        if(!SupportedPacket(packet))return false;
        string stored=Text(gun,SourcePath)??Text(Group(doc.Element("Subsystems"),"Tartareosity"),"WorldPath");
        if(string.IsNullOrWhiteSpace(stored)||Canonical(stored)!=Canonical(Text(packet,"Source")))return false;
        int block=Index(doc);if(block<0)return false;
        var fields=new ValuesDictionary();var table=Group(gun,"GunRegistry");if(table is null)return false;
        fields.ApplyOverrides(table);var registry=ScGunRegistry.Load(fields,0);
        if(registry.Disabled)return false;
        var rows=Map(Group(table,"Records"));var packetRows=Map(Group(packet,"Records"));
        var ids=Map(Group(gun,Identities));var packetIds=Map(Group(packet,"Identities"));
        _=Items(player,block,registry,Layout(gun)).ToArray(); // unknown encodings/counts still refuse
        // Proven whole-world copies keep their local damaged references too. The integrity guard
        // handles those after this step; treating them as player-only imports would overwrite/renumber them.
        return packetRows.All(p=>rows.GetValueOrDefault(p.Key)==p.Value)
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
            foreach(var item in Items(entity,block,registry,Layout(gun)))used.Add(item.Id);
        // Terrain/container/pickable encodings are not all inventories: conservatively protect any
        // matching encoded value outside incoming player nodes, including unknown mod containers.
        var outside=new XElement(doc);
        var outsideEntities=outside.Element("Entities")?.Elements().ToArray()??[];
        var originalEntities=doc.Element("Entities")?.Elements().ToArray()??[];
        for(int i=0;i<originalEntities.Length;i++)if(incoming.Contains(originalEntities[i]))outsideEntities[i].Remove();
        foreach(var f in outside.Descendants("Value"))if(int.TryParse((string)f.Attribute("Value"),out int v)&&Terrain.ExtractContents(v)==block
            &&ScGunEncoding.Decode(Terrain.ExtractData(v),registry,Layout(gun),out int existingId,out _)&&ScGunEncoding.IsRecordId(existingId))used.Add(existingId);
        var seen=new HashSet<string>();int count=0;
        foreach(var player in incoming) {
            var packet=Group(player,Packet);string origin=Text(packet,"Source");
            string version=Text(packet,"Version");
            if(version is not ("1" or "2" or "3") || string.IsNullOrWhiteSpace(origin)
                || version!="3"&&!Related(origin,target) || Text(packet,"Error") is not null)
                throw new InvalidOperationException("跨世界携带快照不兼容或未就绪："+Text(packet,"Error"));
            if(!SupportedPacket(packet))
                throw new InvalidOperationException("跨世界快照的数据版本不受支持");
            var rows=Map(Group(packet,"Records"));var keys=Map(Group(packet,"Identities"));
            if(rows.Count!=keys.Count)throw new InvalidOperationException("跨世界记录与身份数量不一致");
            if(!Enum.TryParse<ScGunGrowthMode>(Text(packet,"GrowthMode"),out var incomingMode)||!Enum.IsDefined(incomingMode))throw new InvalidOperationException("跨世界成长规则无效");
            var proof=new ValuesDictionary();proof.SetValue("Schema",PacketSchema(packet));proof.SetValue("Next",1023);proof.SetValue("GrowthMode",incomingMode.ToString());
            var proofRows=new ValuesDictionary();foreach(var row in rows)proofRows.SetValue(row.Key,row.Value);proof.SetValue("Records",proofRows);
            var parsed=ScGunRegistry.Load(proof,0);if(parsed.QuarantinedCount>0||parsed.Count!=rows.Count||parsed.Disabled)throw new InvalidOperationException("携带枪械快照损坏");
            int packetLayout=PacketSchema(packet)==ScGunRegistry.Schema?GunSpec.DataLayout:ScGunEncoding.PreviousLayout;
            var carriedItems=Items(player,block,parsed,packetLayout).ToArray();
            if(rows.Count!=carriedItems.Length)throw new InvalidOperationException("携带物品与快照数量不一致，未导入");
            foreach(var item in carriedItems) {
                string old=item.Id.ToString(CultureInfo.InvariantCulture);
                if(!rows.TryGetValue(old,out var row)||!keys.TryGetValue(old,out var identity)||identity.Length!=64||!identity.All(Uri.IsHexDigit)||!seen.Add(identity)||!parsed.TryGetSnapshot(item.Id,out var snap)||snap.Variant!=item.Variant)
                    throw new InvalidOperationException("跨世界枪械身份重复或快照不匹配");
                // The identity -> local number rule is ScItemTravel's, shared with the live importer: back to the number
                // this identity already has here (never over a record someone else holds, never over another model),
                // otherwise the next free number.
                int id=ScItemTravel.Resolve(identity,item.Variant,reverse,used.Contains,n=>registry.TryGetSnapshot(n,out var existing)?existing.Variant:(int?)null,
                    ()=>{if(next>GunSpec.LastId)return -1;int n=next;next=ScGunEncoding.NextId(next+1);return n;},out bool allocated);
                if(allocated)identities[id.ToString(CultureInfo.InvariantCulture)]=identity;
                // Old packet rows must be upgraded exactly once before joining the current table.
                records.SetValue(id.ToString(CultureInfo.InvariantCulture),parsed.Save(0).GetValue<ValuesDictionary>("Records").GetValue<string>(old));
                int value=int.Parse((string)item.Field.Attribute("Value"),CultureInfo.InvariantCulture);
                item.Field.SetAttributeValue("Value",Terrain.ReplaceData(value,GunSpec.WithId(item.Variant,id)).ToString(CultureInfo.InvariantCulture));count++;
            }
            packet.Remove();
            if(carriedItems.Length>0) {effectiveMode=ScItemTravel.GrowthAfter(effectiveMode,incomingMode);saved.SetValue("GrowthMode",effectiveMode.ToString());}
        }
        saved.SetValue("Next",next);var verified=ScGunRegistry.Load(saved,0);if(verified.Disabled||verified.QuarantinedCount>0)throw new InvalidOperationException("合并后枪械记录校验失败");
        var table=Make("GunRegistry");saved.Save(table);Replace(gun,table);
        var ids=Make(Identities);foreach(var kv in identities)ids.Add(Field(kv.Key,kv.Value));Replace(gun,ids);
        gun.Elements("Value").Where(v=>(string)v.Attribute("Name")=="GunDataLayout").Remove();gun.Add(Field("GunDataLayout",GunSpec.DataLayout));
        return new Plan(doc,count);
    }
    /// <summary>Guns the saved-XML path took in while the world now loading was read (0: none). A world whose player XML
    /// arrived with the carried guns needs no arrival check after it has loaded (ScTravelArrival).</summary>
    public static int ImportedOnLoad { get; private set; }
    public static void BeforeLoad(XElement source,WorldInfo world) {
        ImportedOnLoad=0;
        var plan=Prepare(source,world.DirectoryName);if(plan is null)return;
        ImportedOnLoad=Math.Max(1,plan.Guns);
        source.ReplaceNodes(plan.Document.Nodes());KnifeLog.Information($"[GUN_TRAVEL] imported {plan.Guns} carried guns; layout {GunSpec.DataLayout}, schema {ScGunRegistry.Schema}, all carried state preserved");
    }
}
