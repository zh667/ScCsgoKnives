using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

// universal-item-travel-analysis-20261002 (OpenSpec subworld-travel T1-T4). A gun item names its record by a number that
// is local to one world's table. A mod that moves a player between worlds and carries only the item values (slot, value,
// count, block type: Ancient World 0.41.16's TravelerSnapshot, actual DLL examined) takes the number along and leaves
// the record behind: in the other world the number names nothing (a paper icon), another model (refused), or - the
// case nobody notices - another gun of the same model, whose rounds, wear and finish the traveller's gun then shows.
//
// Two things are kept apart here:
//   the item state transfer (this file): what a carried gun is (its stable identity, its record), how it is written
//       into another world's table without guessing, and how a repeat of the same transfer changes nothing;
//   the travel life cycle (the provider's): when a player leaves, what is saved between the worlds, when the inventory
//       is restored. A provider calls ScGunTravelApi at three points and carries one opaque text; it does not learn
//       gun numbering. A provider that restores the player from the world's own saved XML already goes through
//       ScGunTravel (ProjectXmlSaved / ProjectXmlLoad); both paths map identities with the same rule, Resolve below.
//
// What this cannot do: repair a transfer whose provider dropped the text. With only the bare number arriving, a gun of
// the same model under that number in the destination is indistinguishable from the traveller's own; nothing here
// scans or shares tables between worlds. (subworld-travel-generic-20261003: a provider that drops the text but saved the
// world it left still left the same envelope in that world's Project.xml - ScTravelArrival reads it from there, only
// between worlds of one tree, and only when the arriving inventory holds exactly what was carried.)

public enum ScTravelCode {
    Ok, Repeat, SameWorld, NothingCarried,
    BadEnvelope, UnsupportedVersion, UnsupportedData, Corrupt, TransferReused,
    RegistryUnavailable, Busy, PendingObligations, Stacked, DuplicateCarried, MissingRecord,
    GrowthConflict, HeldElsewhere, ModelConflict, RegistryFull, SlotMismatch
}

/// <summary>One carried gun: its identity across worlds, the number it had in the world it left, and its whole record.</summary>
public sealed record ScTravelGun(string Identity, int Id, int Variant, string Row);
/// <summary>One carried slot holding a CS gun item (an instance, or a fresh gun: Identity empty).</summary>
public sealed record ScTravelSlot(int Slot, int Value, int Count, string Identity);

/// <summary>What a provider carries for one traveller on one trip: opaque to it, versioned, self-checking.</summary>
public sealed class ScTravelEnvelope {
    public const int CurrentVersion = 1;
    public const string Namespace = "zh667.ScCsgoKnives/guns";
    public int V { get; set; } = CurrentVersion;
    public string Ns { get; set; } = Namespace;
    /// <summary>The id of this transfer, made at export (one traveller, one departure). The same id again is the same
    /// transfer; a provider never has to invent or reuse one.</summary>
    public string Transfer { get; set; } = "";
    /// <summary>The stable identity of the world left (not its folder name).</summary>
    public string World { get; set; } = "";
    public string Traveller { get; set; } = "";
    public int Schema { get; set; }
    public int Layout { get; set; }
    public string Growth { get; set; } = "Unset";
    /// <summary>The gun block's index in the world left; the destination's may differ.</summary>
    public int Block { get; set; }
    public List<ScTravelGun> Guns { get; set; } = [];
    public List<ScTravelSlot> Slots { get; set; } = [];
    public string Digest { get; set; } = "";

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public string ComputeDigest() {
        var text = new StringBuilder();
        text.Append(V).Append('\n').Append(Ns).Append('\n').Append(Transfer).Append('\n').Append(World).Append('\n').Append(Traveller).Append('\n')
            .Append(Schema).Append('\n').Append(Layout).Append('\n').Append(Growth).Append('\n').Append(Block).Append('\n');
        foreach (var g in Guns.OrderBy(g => g.Identity, StringComparer.Ordinal)) text.Append(g.Identity).Append('|').Append(g.Id).Append('|').Append(g.Variant).Append('|').Append(g.Row).Append('\n');
        foreach (var s in Slots.OrderBy(s => s.Slot)) text.Append(s.Slot).Append('|').Append(s.Value).Append('|').Append(s.Count).Append('|').Append(s.Identity).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
    public string Encode() { Digest = ComputeDigest(); return JsonSerializer.Serialize(this, Json); }
    /// <summary>Reads an envelope. A text of a later version, of another namespace, or one that does not match its own
    /// digest is refused whole: nothing of it is applied and the provider keeps it as it is.</summary>
    public static ScTravelEnvelope Decode(string text, out ScTravelCode code, out string detail) {
        code = ScTravelCode.BadEnvelope; detail = "";
        if (string.IsNullOrWhiteSpace(text)) { detail = "没有携带枪械迁移数据"; return null; }
        ScTravelEnvelope e;
        try {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("v", out var version) || !version.TryGetInt32(out int v)) { detail = "迁移数据缺少版本"; return null; }
            if (v != CurrentVersion) { code = ScTravelCode.UnsupportedVersion; detail = $"迁移数据版本 {v}，本版只认识 {CurrentVersion}；数据原样保留，未应用"; return null; }
            e = JsonSerializer.Deserialize<ScTravelEnvelope>(text, Json);
        }
        catch (JsonException x) { detail = "迁移数据无法解析：" + x.Message; return null; }
        if (e is null || e.Ns != Namespace) { detail = "迁移数据不属于 CS 武器"; return null; }
        if (string.IsNullOrWhiteSpace(e.Transfer) || string.IsNullOrWhiteSpace(e.World) || e.Guns is null || e.Slots is null) { detail = "迁移数据缺少旅程或来源世界身份"; return null; }
        if (!string.Equals(e.Digest, e.ComputeDigest(), StringComparison.Ordinal)) { code = ScTravelCode.Corrupt; detail = "迁移数据与其校验不符（被截断或改动），未应用"; return null; }
        code = ScTravelCode.Ok; return e;
    }
}

/// <summary>What one world remembers about transfers: its own identity, the identity of every gun record that has one
/// (records that came from another world keep theirs), and the transfers it has committed.</summary>
public sealed class ScTravelLedger {
    public const string ReceiptsKey = "GunTravelReceipts";
    public const int ReceiptsKept = 64;
    public string WorldIdentity = Guid.NewGuid().ToString("N");
    /// <summary>Local record number -> identity (the saved GunTravelIdentities table, unchanged in form).</summary>
    public readonly Dictionary<int, string> Identities = [];
    readonly Dictionary<string, object> m_otherIdentityKeys = new(StringComparer.Ordinal);   // anything in that table this build does not read: written back as it was
    public sealed record Receipt(string Transfer, string Digest, string World, bool Completed, IReadOnlyList<(string Identity, int Id)> Map);
    readonly List<Receipt> m_receipts = [];
    public IReadOnlyList<Receipt> Receipts => m_receipts;

    public static string Token(string seed, int id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + id)));
    /// <summary>The identity of a local record: the one it came with, or (first time) one derived from this world's own
    /// identity and the number, the same value ScGunTravel.Capture writes on save.</summary>
    public string IdentityOf(int id) {
        if (!Identities.TryGetValue(id, out string identity)) Identities[id] = identity = Token(Guid.TryParse(WorldIdentity, out var uuid) ? uuid.ToString("N") : WorldIdentity, id);
        return identity;
    }
    /// <summary>Identity -> local number for every record of this world: the ones the table lists, and for a record it
    /// does not list (a world last saved by a build that keeps no such table, or never saved since the record was made)
    /// the identity that record gets here, as the saved-XML path gives every row one when the world is saved. A gun that
    /// was made in this world therefore finds its number again on return whether or not the table kept it.</summary>
    public Dictionary<string, int> Known(IEnumerable<int> records) {
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, token) in Identities) known.TryAdd(token, id);
        string seed = Guid.TryParse(WorldIdentity, out var uuid) ? uuid.ToString("N") : WorldIdentity;
        foreach (int id in records) if (!Identities.ContainsKey(id)) known.TryAdd(Token(seed, id), id);
        return known;
    }
    public Receipt Find(string transfer) => m_receipts.LastOrDefault(r => r.Transfer == transfer);
    internal void Commit(Receipt receipt) {
        m_receipts.RemoveAll(r => r.Transfer == receipt.Transfer); m_receipts.Add(receipt);
        while (m_receipts.Count > ReceiptsKept) m_receipts.RemoveAt(0);
    }

    public void LoadIdentities(ValuesDictionary saved) {
        Identities.Clear(); m_otherIdentityKeys.Clear();
        if (saved is null) return;
        foreach (var pair in saved) {
            if (int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && pair.Value is string token) Identities[id] = token;
            else m_otherIdentityKeys[pair.Key] = pair.Value;
        }
    }
    public ValuesDictionary SaveIdentities() {
        if (Identities.Count == 0 && m_otherIdentityKeys.Count == 0) return null;
        var d = new ValuesDictionary();
        foreach (var (id, token) in Identities) d.SetValue(id.ToString(CultureInfo.InvariantCulture), token);
        foreach (var (key, value) in m_otherIdentityKeys) d.SetValue(key, value);
        return d;
    }
    /// <summary>The committed transfers, newest last. A key of its own in the gun subsystem's saved values: a build that
    /// does not know it neither reads nor keeps it (it only loses the repeat protection of a trip in flight); the record
    /// table, its schema and the item layout are untouched.</summary>
    public ValuesDictionary SaveReceipts() {
        if (m_receipts.Count == 0) return null;
        var d = new ValuesDictionary();
        for (int i = 0; i < m_receipts.Count; i++) {
            var r = m_receipts[i];
            d.SetValue(i.ToString("000", CultureInfo.InvariantCulture), string.Join("|", r.Transfer.Replace("|", ""), r.Digest, r.World.Replace("|", ""), r.Completed ? "1" : "0",
                string.Join(",", r.Map.Select(m => m.Identity + ":" + m.Id.ToString(CultureInfo.InvariantCulture)))));
        }
        return d;
    }
    public void LoadReceipts(ValuesDictionary saved) {
        m_receipts.Clear();
        if (saved is null) return;
        foreach (var pair in saved.OrderBy(p => p.Key, StringComparer.Ordinal)) {
            string[] parts = (pair.Value as string ?? "").Split('|');
            if (parts.Length != 5 || parts[0].Length == 0) continue;   // unreadable: not a receipt (the transfer would be planned afresh, never guessed)
            var map = new List<(string, int)>(); bool ok = true;
            foreach (string item in parts[4].Split(',', StringSplitOptions.RemoveEmptyEntries)) {
                string[] kv = item.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) map.Add((kv[0], id)); else ok = false;
            }
            if (ok) m_receipts.Add(new Receipt(parts[0], parts[1], parts[2], parts[3] == "1", map));
        }
    }
}

/// <summary>The outcome of an export or an import.</summary>
public sealed record ScTravelResult(ScTravelCode Code, string Message, string Envelope = null, IReadOnlyDictionary<int, int> Values = null, int Guns = 0) {
    public bool Ok => Code is ScTravelCode.Ok or ScTravelCode.Repeat or ScTravelCode.SameWorld or ScTravelCode.NothingCarried;
}

public static class ScItemTravel {
    public sealed class Refusal(ScTravelCode code, string message) : InvalidOperationException(message) { public ScTravelCode Code { get; } = code; }

    /// <summary>The one rule that maps a carried gun to a number in the destination table, used by the saved-XML importer
    /// (ScGunTravel.Prepare) and by the live importer below. A gun whose identity the destination already knows goes back
    /// to that number (a return trip): only when nobody else holds that record there and it is the same model. Otherwise
    /// it takes the next free number. It never takes a number because the number or the model happens to match.</summary>
    /// <param name="known">identity -> local number of the destination (updated for a new number)</param>
    /// <param name="heldElsewhere">a holder other than the traveller's own slots names this record</param>
    /// <param name="variantOf">the model of a destination record, or null when there is none</param>
    /// <param name="allocate">the next free number, or -1 when the table is full</param>
    public static int Resolve(string identity, int variant, IDictionary<string, int> known, Func<int, bool> heldElsewhere, Func<int, int?> variantOf, Func<int> allocate, out bool allocated) {
        allocated = false;
        if (identity is null || identity.Length != 64 || !identity.All(Uri.IsHexDigit)) throw new Refusal(ScTravelCode.Corrupt, "跨世界枪械身份重复或快照不匹配");
        if (known.TryGetValue(identity, out int id)) {
            if (heldElsewhere(id)) throw new Refusal(ScTravelCode.HeldElsewhere, "目标世界同身份枪仍被其他容器持有，拒绝覆盖");
            if (variantOf(id) is not { } existing || existing != variant) throw new Refusal(ScTravelCode.ModelConflict, "目标世界同身份枪的记录已不在或型号不同，拒绝覆盖");
            return id;
        }
        id = allocate();
        if (id < 0) throw new Refusal(ScTravelCode.RegistryFull, "目标枪械记录表已满，迁移未执行");
        known.Add(identity, id); allocated = true;
        return id;
    }

    /// <summary>The growth rule of the destination after taking guns from a world with <paramref name="incoming"/>:
    /// an unset destination adopts it; two different explicit rules are refused (a gun's bonuses would change silently).</summary>
    public static ScGunGrowthMode GrowthAfter(ScGunGrowthMode destination, ScGunGrowthMode incoming) {
        if (destination == ScGunGrowthMode.Unset) return incoming;
        if (incoming != ScGunGrowthMode.Unset && incoming != destination) throw new Refusal(ScTravelCode.GrowthConflict, "两个世界计数成长规则不同，拒绝静默改变枪械加成");
        return destination;
    }

    internal static int GunBlock => BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock), out int index) ? index : -1;
    static double Now(Project project) => project?.FindSubsystem<SubsystemTime>(false)?.GameTime ?? 0;
    static ScTravelResult Refused(ScTravelCode code, string message) => new(code, message);

    // ------------------------------------------------------------------------------------------------ source world
    /// <summary>Source world, at the moment the provider fixes what the traveller carries: the envelope for the guns in
    /// <paramref name="inventory"/>. Nothing in this world is changed (the records stay; the provider's own flow decides
    /// what becomes of the player left behind). Refused, with the reason, when a carried gun cannot be carried as it is:
    /// its record is missing, it is stacked, the same gun is carried twice, or something of it is still unsettled.</summary>
    public static ScTravelResult Export(Project project, IInventory inventory, string traveller, string transfer = null) {
        if (string.IsNullOrWhiteSpace(transfer)) transfer = Guid.NewGuid().ToString("N");
        // (A multiplayer client only mirrors the server's table: what travels is the server's to say.)
        if (!ScNet.IsAuthority) return Refused(ScTravelCode.RegistryUnavailable, "联机客户端的枪械表只是房主的镜像，枪械迁移由房主一侧执行");
        // A world set up for a mode keeps its weapons (deathmatch-addon): they belong to the mode's own stock, also while
        // the package that runs the mode is absent.
        if (ScWorldModes.Dedicated(project)) return Refused(ScTravelCode.Busy, "本世界是模式专用世界，其中的枪械不随行到其他世界");
        var registry = ScGunRegistry.Current; int block = GunBlock;
        if (registry is null || registry.Disabled || registry.QuarantinedCount > 0 || block < 0 || registry.Travel is not { } ledger) return Refused(ScTravelCode.RegistryUnavailable, "本世界的枪械记录表不可用或有异常记录，枪械未随行");
        if (ScGunMutation.IsCommitting) return Refused(ScTravelCode.Busy, "枪械操作进行中，请稍后再传送");
        inventory = ScInventoryIdentity.Inventory(inventory);
        if (inventory is null) return Refused(ScTravelCode.RegistryUnavailable, "无法确认要携带的库存");
        double now = Now(project);
        var envelope = new ScTravelEnvelope { Transfer = transfer, World = ledger.WorldIdentity, Traveller = traveller ?? "", Schema = ScGunRegistry.Schema, Layout = GunSpec.DataLayout, Growth = registry.GrowthMode.ToString(), Block = block };
        var carried = new HashSet<int>();
        int slots = inventory is ComponentCreativeInventory creative ? Math.Min(creative.OpenSlotsCount, inventory.SlotsCount) : inventory.SlotsCount;
        for (int slot = 0; slot < slots; slot++) {
            int value = inventory.GetSlotValue(slot), count = inventory.GetSlotCount(slot);
            if (count <= 0 || Terrain.ExtractContents(value) != block) continue;
            int data = Terrain.ExtractData(value);
            // (A creative slot is an endless source in the engine; a traveller carries one gun out of it.)
            if (inventory is ComponentCreativeInventory) count = 1;
            if (GunSpec.IsForeign(data)) return Refused(ScTravelCode.UnsupportedData, $"第 {slot + 1} 格的枪是旧版本数据，无法随行");
            if (GunSpec.IsFresh(data)) { envelope.Slots.Add(new(slot, value, count, "")); continue; }
            int id = GunSpec.GetId(data), variant = GunSpec.GetVariant(data);
            if (count != 1) return Refused(ScTravelCode.Stacked, $"第 {slot + 1} 格叠放了多把同一记录的枪，请先分开再传送");
            if (!registry.TryGetSnapshot(id, out var state) || state.Variant != variant) return Refused(ScTravelCode.MissingRecord, $"第 {slot + 1} 格的枪 #{id} 记录缺失或型号不符，请先恢复原记录；不会在另一个世界重建新枪");
            if (!carried.Add(id)) return Refused(ScTravelCode.DuplicateCarried, $"携带了同一把枪 #{id} 的两个副本，请先让它们各自成为独立的枪（使用一次）再传送");
            if (registry.Kills.Pending.Any(k => k.RecordId == id)) return Refused(ScTravelCode.PendingObligations, "有待结算击杀，请结算后再传送");
            string identity = ledger.IdentityOf(id);
            envelope.Guns.Add(new(identity, id, variant, registry.NetworkRow(id, now)));
            envelope.Slots.Add(new(slot, value, 1, identity));
        }
        if (envelope.Guns.Count > 0 && registry.RecoveryOwner?.Invoke(inventory) is { } owner && registry.Recovery.HasPending(owner)) return Refused(ScTravelCode.PendingObligations, "有待归还的物品补偿，请结算后再传送");
        if (envelope.Slots.Count == 0) return new(ScTravelCode.NothingCarried, "没有携带 CS 枪械");
        return new(ScTravelCode.Ok, "", envelope.Encode(), Guns: envelope.Guns.Count);
    }

    // ------------------------------------------------------------------------------------------- destination world
    /// <summary>Destination world, before the provider restores the traveller's inventory. Everything that can fail is
    /// checked first; then the carried records are written into this world's table under local numbers and the transfer
    /// is remembered. The result maps every carried gun item value to the value to restore here (this world's block
    /// index, the local number). The provider restores those values, then calls <see cref="Complete"/>.
    /// The same transfer again (a retry, a restart between commit and the provider's own completion) returns the same
    /// mapping and writes nothing: no second number, and nothing the gun did here since is put back.
    /// <paramref name="inventory"/> is the inventory about to be replaced (the traveller's own): what it still holds
    /// from before the trip does not count as "someone else holds this gun".</summary>
    public static ScTravelResult Import(Project project, IInventory inventory, string text, bool commit = true) {
        var envelope = ScTravelEnvelope.Decode(text, out var code, out string detail);
        if (envelope is null) return Refused(code, detail);
        if (!ScNet.IsAuthority) return Refused(ScTravelCode.RegistryUnavailable, "联机客户端的枪械表只是房主的镜像，枪械迁移由房主一侧执行");
        var registry = ScGunRegistry.Current; int block = GunBlock;
        if (registry is null || registry.Disabled || registry.QuarantinedCount > 0 || block < 0 || registry.Travel is not { } ledger) return Refused(ScTravelCode.RegistryUnavailable, "目标枪械表异常，拒绝跨世界迁移");
        if (ScGunMutation.IsCommitting) return Refused(ScTravelCode.Busy, "枪械操作进行中，请稍后再恢复物品");
        if (envelope.Schema != ScGunRegistry.Schema || envelope.Layout != GunSpec.DataLayout) return Refused(ScTravelCode.UnsupportedData, $"迁移数据的记录格式 {envelope.Schema}/布局 {envelope.Layout} 不是本版的 {ScGunRegistry.Schema}/{GunSpec.DataLayout}；原样保留，未应用");
        if (!Enum.TryParse(envelope.Growth, out ScGunGrowthMode incomingMode) || !Enum.IsDefined(incomingMode)) return Refused(ScTravelCode.Corrupt, "跨世界成长规则无效");
        double now = Now(project);
        object own = inventory is null ? null : ScInventoryIdentity.Storage(inventory);
        IReadOnlyDictionary<int, int> Values(Func<ScTravelGun, int> local) {
            var values = new Dictionary<int, int>();
            foreach (var slot in envelope.Slots) {
                int data = Terrain.ExtractData(slot.Value);
                var gun = slot.Identity.Length == 0 ? null : envelope.Guns.FirstOrDefault(g => g.Identity == slot.Identity);
                values[slot.Value] = Terrain.MakeBlockValue(block, 0, gun is null ? data : GunSpec.WithId(gun.Variant, local(gun)));
            }
            return values;
        }
        // ---- checks on the envelope itself
        if (envelope.Guns.Select(g => g.Identity).Distinct().Count() != envelope.Guns.Count || envelope.Guns.Select(g => g.Id).Distinct().Count() != envelope.Guns.Count
            || envelope.Slots.Any(s => s.Identity.Length > 0 && envelope.Guns.All(g => g.Identity != s.Identity)) || envelope.Guns.Any(g => envelope.Slots.Count(s => s.Identity == g.Identity) != 1)
            || envelope.Slots.Any(s => Terrain.ExtractContents(s.Value) != envelope.Block || s.Count < 1 || s.Identity.Length > 0 && s.Count != 1))
            return Refused(ScTravelCode.Corrupt, "携带物品与快照数量或身份不符，未导入");
        // ---- the same transfer again: the committed mapping, nothing written
        if (ledger.Find(envelope.Transfer) is { } done) {
            if (done.Digest != envelope.Digest) return Refused(ScTravelCode.TransferReused, "同一旅程编号对应了另一份迁移数据，未应用");
            var map = done.Map.ToDictionary(m => m.Identity, m => m.Id);
            if (envelope.Guns.Any(g => !map.ContainsKey(g.Identity))) return Refused(ScTravelCode.Corrupt, "已提交旅程的身份映射不完整，未应用");
            return new(ScTravelCode.Repeat, "该旅程已提交，返回同一映射", Values: Values(g => map[g.Identity]), Guns: envelope.Guns.Count);
        }
        // ---- the world the traveller left is this world (it never left, or a same-world move): nothing to map
        if (envelope.World == ledger.WorldIdentity) {
            foreach (var g in envelope.Guns) if (!registry.TryGetSnapshot(g.Id, out var here) || here.Variant != g.Variant) return Refused(ScTravelCode.MissingRecord, $"枪 #{g.Id} 在本世界的记录缺失或型号不符");
            return new(ScTravelCode.SameWorld, "来源就是本世界，编号不变", Values: Values(g => g.Id), Guns: envelope.Guns.Count);
        }
        // ---- every record must read under this build's rules before anything is written
        var parsed = new List<(ScTravelGun Gun, ScGunRecord Record)>();
        foreach (var g in envelope.Guns) {
            if (!ScGunRegistry.TryReadRow(g.Row, now, incomingMode, out var record) || record.Variant != g.Variant) return Refused(ScTravelCode.Corrupt, "携带枪械快照损坏");
            parsed.Add((g, record));
        }
        // ---- the plan: identity -> local number, by the shared rule; nothing is written while it can still fail
        ScGunGrowthMode mode;
        var known = envelope.Guns.Count == 0 ? [] : ledger.Known(registry.Ids);
        var plan = new List<(ScTravelGun Gun, ScGunRecord Record, int Id, bool New)>();
        try {
            mode = envelope.Guns.Count == 0 ? registry.GrowthMode : GrowthAfter(registry.GrowthMode, incomingMode);
            var holders = envelope.Guns.Count == 0 ? [] : ScGunHolders.Scan(project, block).Where(h => h.Inventory is null || !ReferenceEquals(ScInventoryIdentity.Storage(h.Inventory), own)).Select(h => h.Id).ToHashSet();
            int next = registry.Next;
            foreach (var (gun, record) in parsed) {
                int id = Resolve(gun.Identity, gun.Variant, known, holders.Contains, n => registry.TryGetSnapshot(n, out var s) ? s.Variant : null,
                    () => { if (next > GunSpec.LastId) return -1; int n = next; next = ScGunEncoding.NextId(next + 1); return n; }, out bool allocated);
                plan.Add((gun, record, id, allocated));
            }
        }
        catch (Refusal r) { return Refused(r.Code, r.Message); }
        if (!commit) { var planned = plan.ToDictionary(p => p.Gun.Identity, p => p.Id); return new(ScTravelCode.Ok, "", Values: Values(g => planned[g.Identity]), Guns: plan.Count); }
        // ---- commit: records, identities, growth rule, receipt. (In memory, on the game thread; the world's next save
        // writes them together with the inventory the provider restores now.)
        var written = new List<int>(); var replaced = new List<(int Id, ScGunRecord Before)>();
        try {
            foreach (var (gun, record, id, isNew) in plan) {
                if (isNew) { int got = registry.ImportNew(record); if (got != id) throw new InvalidOperationException($"allocated {got} instead of {id}"); written.Add(got); }
                else { replaced.Add((id, registry.Get(id))); if (!registry.ImportOver(id, record)) throw new InvalidOperationException($"record {id} is gone"); }
                ledger.Identities[id] = gun.Identity;
            }
            registry.GrowthMode = mode;
            ledger.Commit(new(envelope.Transfer, envelope.Digest, envelope.World, false, plan.Select(p => (p.Gun.Identity, p.Id)).ToList()));
        }
        catch (Exception e) {
            foreach (int id in written) { registry.Abandon(id, "travel import failed"); ledger.Identities.Remove(id); }
            foreach (var (id, before) in replaced) registry.ImportUndo(id, before);
            KnifeLog.Error("[GUN_TRAVEL] import rolled back: " + e.Message);
            return Refused(ScTravelCode.RegistryUnavailable, "导入枪械记录失败，已撤回：" + e.Message);
        }
        var local = plan.ToDictionary(p => p.Gun.Identity, p => p.Id);
        KnifeLog.Information($"[GUN_TRAVEL] transfer {envelope.Transfer}: {plan.Count} carried gun(s) imported ({plan.Count(p => p.New)} new number(s), {plan.Count(p => !p.New)} returning); all carried state preserved");
        return new(ScTravelCode.Ok, "", Values: Values(g => local[g.Identity]), Guns: plan.Count);
    }

    /// <summary>Destination world, after the provider restored the inventory: checks that every carried gun now sits in
    /// <paramref name="inventory"/> exactly once under the number it was given here, and marks the transfer complete.
    /// When it does not (the provider restored other values, or lost a slot), nothing is marked and the reason is
    /// returned; the transfer stays repeatable and its records stay in the table, unreferenced and harmless.</summary>
    public static ScTravelResult Complete(Project project, IInventory inventory, string text) {
        var envelope = ScTravelEnvelope.Decode(text, out var code, out string detail);
        if (envelope is null) return Refused(code, detail);
        int block = GunBlock;
        if (ScGunRegistry.Current is not { Travel: { } ledger } registry || block < 0) return Refused(ScTravelCode.RegistryUnavailable, "目标枪械表异常，拒绝跨世界迁移");
        Dictionary<string, int> map;
        if (envelope.World == ledger.WorldIdentity) map = envelope.Guns.ToDictionary(g => g.Identity, g => g.Id);
        else if (ledger.Find(envelope.Transfer) is { } receipt && receipt.Digest == envelope.Digest) map = receipt.Map.ToDictionary(m => m.Identity, m => m.Id);
        else return Refused(ScTravelCode.MissingRecord, "该旅程尚未在本世界提交枪械记录");
        inventory = ScInventoryIdentity.Inventory(inventory);
        if (inventory is null) return Refused(ScTravelCode.RegistryUnavailable, "无法确认要核对的库存");
        var held = new Dictionary<int, int>();
        int slots = inventory is ComponentCreativeInventory creative ? Math.Min(creative.OpenSlotsCount, inventory.SlotsCount) : inventory.SlotsCount;
        for (int slot = 0; slot < slots; slot++) {
            int value = inventory.GetSlotValue(slot);
            if (inventory.GetSlotCount(slot) <= 0 || Terrain.ExtractContents(value) != block) continue;
            int data = Terrain.ExtractData(value);
            if (GunSpec.IsForeign(data) || GunSpec.IsFresh(data)) continue;
            int id = GunSpec.GetId(data);
            held[id] = held.GetValueOrDefault(id) + (inventory is ComponentCreativeInventory ? 1 : inventory.GetSlotCount(slot));
        }
        foreach (var gun in envelope.Guns) {
            if (!map.TryGetValue(gun.Identity, out int id) || !registry.TryGetSnapshot(id, out var state) || state.Variant != gun.Variant)
                return Refused(ScTravelCode.MissingRecord, $"携带枪械在本世界的记录 #{(map.TryGetValue(gun.Identity, out int n) ? n : -1)} 缺失或型号不符");
            if (held.GetValueOrDefault(id) != 1) return Refused(ScTravelCode.SlotMismatch, $"恢复后的库存里枪 #{id} 有 {held.GetValueOrDefault(id)} 把（应为 1）：物品与记录未一致恢复，旅程未标记完成");
        }
        if (ledger.Find(envelope.Transfer) is { } done) ledger.Commit(done with { Completed = true });
        return new(ScTravelCode.Ok, "", Guns: envelope.Guns.Count);
    }
}

/// <summary>The entry points for a travel provider. Only engine and base-library types cross this boundary, so a provider
/// can find and call it by reflection without referencing this assembly (as optional bridges usually do):
/// <c>Type.GetType("Game.ScGunTravelApi, ScCsgoKnives")</c>. See docs/tasks/universal-item-travel-analysis-20261002.md and
/// the provider notes for where a provider calls each and what it has to keep.
/// A provider that does not call these (Ancient World 0.41.16 does not) is not made compatible by their existence.</summary>
public static class ScGunTravelApi {
    public const int Version = ScTravelEnvelope.CurrentVersion;
    public const string Namespace = ScTravelEnvelope.Namespace;
    /// <summary>Source world, when the provider fixes what the traveller carries: the text to keep with the trip
    /// (null: no CS gun is carried, or refused - then <paramref name="refusal"/> says why and the trip should not start
    /// with those guns).</summary>
    public static string Export(IInventory inventory, string traveller, out string refusal) {
        var result = ScItemTravel.Export(GameManager.Project, inventory, traveller);
        refusal = result.Ok ? null : result.Code + ": " + result.Message;
        return result.Envelope;
    }
    /// <summary>Source world, before the provider commits to the trip: whether the guns carried in
    /// <paramref name="inventory"/> can travel now (the checks of <see cref="Export"/>, nothing produced).</summary>
    public static bool CanExport(IInventory inventory, out string refusal) {
        var result = ScItemTravel.Export(GameManager.Project, inventory, "", "preflight");
        refusal = result.Ok ? null : result.Code + ": " + result.Message;
        return result.Ok;
    }
    /// <summary>Destination world, before anything of the traveller's inventory is replaced: whether
    /// <see cref="Import"/> would succeed now. Nothing is written.</summary>
    public static bool CanImport(IInventory inventory, string envelope, out string refusal) {
        var result = ScItemTravel.Import(GameManager.Project, inventory, envelope, commit: false);
        refusal = result.Ok ? null : result.Code + ": " + result.Message;
        return result.Ok;
    }
    /// <summary>Destination world, before the inventory is restored: writes the carried records and returns, for every
    /// carried gun item value (as it was in the world left), the value to restore here. False (nothing written) with
    /// the reason when it cannot: the carried gun items must then not be restored under their old values.
    /// Calling it again with the same text returns the same values and writes nothing.</summary>
    public static bool Import(IInventory inventory, string envelope, out Dictionary<int, int> values, out string refusal) {
        var result = ScItemTravel.Import(GameManager.Project, inventory, envelope);
        values = result.Ok && result.Values is not null ? new Dictionary<int, int>(result.Values) : null;
        refusal = result.Ok ? null : result.Code + ": " + result.Message;
        return result.Ok;
    }
    /// <summary>Destination world, after the inventory was restored: confirms items and records agree and marks the
    /// transfer complete. False with the reason when they do not.</summary>
    public static bool Complete(IInventory inventory, string envelope, out string problem) {
        var result = ScItemTravel.Complete(GameManager.Project, inventory, envelope);
        problem = result.Ok ? null : result.Code + ": " + result.Message;
        return result.Ok;
    }
}
