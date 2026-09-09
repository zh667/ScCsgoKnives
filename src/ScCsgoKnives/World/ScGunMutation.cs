using System.Threading;
namespace Game;

public enum ScGunResult { Success, Foreign, MissingRecord, StateChanged, InsufficientMaterials, RegistryFull, InventoryRejected, Invalid, DuplicateUnresolved, Busy, RecoveryPending }

/// <summary>World-bound gun transaction with measured receipts, inverse rollback and durable compensation.</summary>
public sealed class ScGunMutation {
    public static Func<int, string, IEnumerable<string>> HolderLocator;
    static int s_committing;
    public static bool IsCommitting => Volatile.Read(ref s_committing) != 0;
    internal static bool TryEnter() => Interlocked.CompareExchange(ref s_committing, 1, 0) == 0;
    internal static void Exit() => Volatile.Write(ref s_committing, 0);
    readonly ScGunRegistry m_registry;
    readonly string m_owner;
    public IInventory Inventory { get; }
    public int Slot { get; }
    public int Expected { get; private set; }
    public int Variant { get; }
    public int Id { get; private set; }
    public bool Fresh { get; }
    public bool SkinTemplate { get; private set; }
    public bool CounterTemplate { get; private set; }
    public bool NeedsClone { get; private set; }
    public long InventoryRevision { get; }
    public int RecordRevision { get; }
    public string Holder { get; }
    public ScGunSnapshot Before { get; }
    public string Detail { get; private set; } = "";
    // Per-transaction fault seam for rollback tests, never set by gameplay.
    internal Action AfterRecordWrite;
    // Consumed only after every fallible inventory step, while the save guard is held.
    internal ScGunKillQueue.Entry KillToComplete;

    ScGunMutation(ScGunRegistry registry, IInventory inventory, int slot, int expected, ScGunSnapshot before, bool fresh, string holder, string owner) {
        m_registry = registry; m_owner = owner;
        Inventory = inventory; Slot = slot; Expected = expected; Before = before; Variant = before.Variant; Id = before.Id; Fresh = fresh;
        InventoryRevision = ScInventoryTransaction.Revision(inventory); RecordRevision = before.Revision; Holder = holder;
    }
    public static ScGunMutation Prepare(IInventory inventory, int slot, string holder, out ScGunResult why) {
        var registry = ScGunRegistry.Current;
        why = ScGunResult.Invalid;
        if (registry is null || inventory is null || slot < 0 || slot >= inventory.SlotsCount || !ScInventoryTransaction.IsWeaponSlot(inventory, slot)) return null;
        int value = inventory.GetSlotValue(slot), data = Terrain.ExtractData(value);
        if (ScGunSkinTemplateBlock.IsTemplate(value) || ScGunCounterTemplateBlock.IsTemplate(value)) {
            if (registry.Disabled) { why = ScGunResult.Foreign; return null; }
            bool counterTemplate = ScGunCounterTemplateBlock.IsTemplate(value);
            ScGunSnapshot template;
            if (counterTemplate ? !ScGunCounterTemplateBlock.TrySnapshot(value, out template) : !ScGunSkinTemplateBlock.TrySnapshot(value, out template)) return null;
            string templateOwner = registry.RecoveryOwner is null ? holder : registry.RecoveryOwner(inventory);
            if (string.IsNullOrWhiteSpace(templateOwner)) return null;
            if (registry.Recovery.HasPending(templateOwner)) { why = ScGunResult.RecoveryPending; return null; }
            why = ScGunResult.Success;
            return new ScGunMutation(registry, inventory, slot, value, template, true, holder, templateOwner) { SkinTemplate = !counterTemplate, CounterTemplate = counterTemplate };
        }
        if (registry.Disabled || GunSpec.IsForeign(data)) { why = ScGunResult.Foreign; return null; }
        int variant = GunSpec.GetVariant(data);
        if (variant < 0 || variant >= GunSpec.All.Length) return null;
        string owner = registry.RecoveryOwner is null ? holder : registry.RecoveryOwner(inventory);
        if (string.IsNullOrWhiteSpace(owner)) return null;
        if (registry.Recovery.HasPending(owner)) { why = ScGunResult.RecoveryPending; return null; }
        bool fresh = GunSpec.IsFresh(data);
        ScGunSnapshot before;
        if (fresh) before = ScGunSnapshot.ForFresh(variant, GunSpec.GetId(data) == GunSpec.FreshFull);
        else if (!registry.TryGetSnapshot(GunSpec.GetId(data), out before) || before.Variant != variant) { why = ScGunResult.MissingRecord; return null; }
        why = ScGunResult.Success;
        return new ScGunMutation(registry, inventory, slot, value, before, fresh, holder, owner);
    }
    static bool Valid(ScGunRecord r) => r.Variant >= 0 && r.Variant < GunSpec.All.Length && r.Rounds >= 0
        // Ammunition is bounded by this gun's own capacity at the level it carries, not by the base magazine:
        // a legitimately grown Negev holds 225 and must not be refused by the model's 150.
        && r.Rounds <= ScGunGrowth.Capacity(r.Variant, r.AppliedGrowthLevel)
        && r.MaxDurability >= 1 && r.Durability >= 0 && r.Durability <= r.MaxDurability && (r.RechargeReadyAt == -1 || (r.RechargeReadyAt >= 0 && double.IsFinite(r.RechargeReadyAt)))
        && float.IsFinite(r.RechargeCycleSeconds) && r.RechargeCycleSeconds >= 0 && r.RechargeCycleSeconds <= 1e6f
        // Counter and growth state: a gun with no counter carries no kills, no level and no rules version.
        && r.KillCount >= 0 && r.AppliedGrowthLevel >= 0 && r.AppliedGrowthLevel <= ScGunGrowth.MaxLevel
        && (r.PendingGrowthLevel == ScGunGrowth.NoPending || (r.PendingGrowthLevel >= 0 && r.PendingGrowthLevel <= ScGunGrowth.MaxLevel))
        && r.GrowthRulesVersion >= 0 && r.ReserveOverflowRounds >= 0 && r.ReserveOverflowRounds <= 1_000_000
        && (r.CounterInstalled || (r.KillCount == 0 && r.AppliedGrowthLevel == 0 && r.GrowthRulesVersion == 0 && r.PendingGrowthLevel == ScGunGrowth.NoPending))
        // A finish must exist in this build's catalogue and belong to this model; nothing else may be written.
        && ScGunSkinCatalog.IsKnown(r.SkinId) && (r.SkinId == ScGunSkinCatalog.None || ScGunSkinCatalog.Fits(ScGunSkinCatalog.Find(r.SkinId), r.Variant));
    bool SlotUnchanged() => Inventory.GetSlotValue(Slot) == Expected && ScInventoryTransaction.Revision(Inventory) == InventoryRevision && ScInventoryTransaction.IsWeaponSlot(Inventory, Slot);

    public ScGunResult Commit(Action<ScGunRecord> change, int ammo = 0, int cost = 0, IReadOnlyDictionary<int, int> materials = null) {
        if (!TryEnter()) return Fail(ScGunResult.Busy, "another gun commit/recovery is in progress");
        var journal = new ScGunInventoryJournal(Inventory);
        int published = -1;
        ScGunRecord original = null, updated = null;
        int originalId = Id, originalExpected = Expected;
        try {
            if (!ReferenceEquals(ScGunRegistry.Current, m_registry) || m_registry.Disabled) return Fail(ScGunResult.Foreign, "world changed or disabled");
            if (m_registry.Recovery.HasPending(m_owner)) return Fail(ScGunResult.RecoveryPending, "inventory has unfinished compensation");
            if (!SlotUnchanged()) return Fail(ScGunResult.StateChanged, "slot changed since Prepare");
            ScGunRecord record = null;
            if (!Fresh) {
                record = m_registry.Get(Id);
                if (record is null) return Fail(ScGunResult.MissingRecord, "record missing");
                if (record.Revision != RecordRevision || record.Variant != Variant || RecordRevision == int.MaxValue) return Fail(ScGunResult.StateChanged, "record changed");
                NeedsClone = HolderLocator is not null && HolderLocator(Id, Holder).Any(); // runtime locator always scans afresh
            }
            bool creative = Inventory is ComponentCreativeInventory;
            if (KillToComplete is { } credit && (Fresh || NeedsClone || credit.RecordId != Id || credit.Variant != Variant
                || !m_registry.Kills.Pending.Contains(credit))) return Fail(ScGunResult.StateChanged, "kill credential no longer matches this sole gun instance");
            if (cost < 0 || (creative && cost != 0) || change is null) return Fail(ScGunResult.Invalid, "invalid operation cost");
            var costs = new Dictionary<int, int>();
            if (cost > 0) costs[ammo] = cost;
            if (materials is not null) foreach (var item in materials) {
                if (item.Value < 0) return Fail(ScGunResult.Invalid, "negative material cost");
                costs[item.Key] = checked(costs.GetValueOrDefault(item.Key) + item.Value);
            }
            foreach (var item in costs) {
                long available = 0;
                for (int i = 0; i < Inventory.SlotsCount; i++) if (i != Slot && Inventory.GetSlotValue(i) == item.Key) available += Inventory.GetSlotCount(i);
                if (available < item.Value) return Fail(ScGunResult.InsufficientMaterials, "not enough ammo/materials");
            }
            bool needsId = Fresh || NeedsClone;
            int candidate = needsId ? m_registry.PeekNextId() : -1;
            if (needsId && candidate < 0) return Fail(NeedsClone ? ScGunResult.DuplicateUnresolved : ScGunResult.RegistryFull, "registry full");
            var draft = Fresh ? new ScGunRecord { Variant = Variant, Rounds = Before.Rounds, SilencerOff = Before.SilencerOff, Durability = Before.Durability, MaxDurability = Before.MaxDurability, SkinId = Before.SkinId } : record.Copy();
            // A record held in two places at once means the item was duplicated. The copy that acts here gets its
            // own record, and a duplicate never inherits the survival kills or the level they bought.
            if (NeedsClone) ScGunGrowth.StripGrowth(draft);
            change(draft);
            if (draft.Variant != Variant || !Valid(draft)) return Fail(ScGunResult.Invalid, "invalid draft state");
            if (!SlotUnchanged() || !ReferenceEquals(ScGunRegistry.Current, m_registry) || (!Fresh && record.Revision != RecordRevision)) return Fail(ScGunResult.StateChanged, "state changed in preparation");
            int replacement = needsId ? Terrain.ReplaceData(Expected, GunSpec.WithId(Variant, candidate)) : Expected;
            if (SkinTemplate || CounterTemplate) replacement = Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true), Terrain.ExtractLight(Expected), GunSpec.WithId(Variant, candidate));
            if (needsId && Inventory.GetSlotCapacity(Slot, replacement) < 1) return Fail(ScGunResult.InventoryRejected, "slot cannot hold new gun id");
            foreach (var item in costs) {
                int needed = item.Value;
                for (int i = 0; i < Inventory.SlotsCount && needed > 0; i++) {
                    if (i == Slot || Inventory.GetSlotValue(i) != item.Key || Inventory.GetSlotCount(i) <= 0) continue;
                    int amount = Math.Min(needed, Inventory.GetSlotCount(i));
                    journal.RemoveExact(i, item.Key, amount); needed -= amount;
                }
                if (needed != 0) throw new InvalidOperationException("Materials changed during deduction");
            }
            if (Inventory.GetSlotValue(Slot) != Expected || !ScInventoryTransaction.IsWeaponSlot(Inventory, Slot)) throw new InvalidOperationException("Gun moved during deduction");
            if (needsId) {
                if (creative) journal.ReplaceCreative(Slot, Expected, replacement);
                else { journal.RemoveExact(Slot, Expected, 1); journal.AddExact(Slot, replacement, 1); }
            }
            // Existing records update in place: no gratuitous removal and re-addition of the gun each shot.
            if (Inventory.GetSlotValue(Slot) != replacement || !ScInventoryTransaction.IsWeaponSlot(Inventory, Slot) || !ReferenceEquals(ScGunRegistry.Current, m_registry))
                throw new InvalidOperationException("Inventory/world changed during replacement");
            if (needsId) {
                published = m_registry.Publish(draft);
                if (published != candidate) throw new InvalidOperationException($"Published {published} instead of {candidate}");
            }
            else {
                original = record.Copy(); updated = record;
                record.CopyFrom(draft);
            }
            var committed = needsId ? draft : record;
            committed.Revision = RecordRevision + 1; committed.Holder = Holder;
            if (needsId) Id = published;
            Expected = replacement;
            AfterRecordWrite?.Invoke();
            ScInventoryTransaction.Changed(Inventory);
            if (KillToComplete is not null) m_registry.Kills.Complete(KillToComplete.EventId);
            return ScGunResult.Success;
        }
        catch (Exception e) {
            // Restore before inventory callbacks run during refund, so observers see the old record.
            if (updated is not null) updated.CopyFrom(original);
            Id = originalId; Expected = originalExpected;
            if (published > 0) m_registry.Abandon(published, "inventory commit failed");
            journal.Rollback(m_registry.Recovery, m_owner);
            KnifeLog.Error("gun mutation rejected: " + e.Message);
            return Fail(m_registry.Recovery.HasPending(m_owner) ? ScGunResult.RecoveryPending : ScGunResult.InventoryRejected, e.Message);
        }
        finally { Exit(); }
    }
    ScGunResult Fail(ScGunResult result, string detail) { Detail = detail; return result; }
    public static string Explain(ScGunResult result) => result switch {
        ScGunResult.Success => "完成",
        ScGunResult.Foreign => "这把枪的数据来自旧版本或本世界已停用枪械",
        ScGunResult.MissingRecord => "这把枪的状态记录不存在",
        ScGunResult.StateChanged => "枪械或背包状态已变化，请重试",
        ScGunResult.InsufficientMaterials => "材料或弹药不足",
        ScGunResult.RegistryFull => $"枪械状态表已满（{GunSpec.LastId} 把），新枪无法登记；已有的枪不受影响",
        ScGunResult.InventoryRejected => "背包拒绝了这次修改，物品已退回",
        ScGunResult.RecoveryPending => "有物品尚未归还，补偿已保留在本世界，库存可接收时会自动重试",
        ScGunResult.DuplicateUnresolved => "这把枪与另一把共用记录，且状态表已满，无法分离",
        ScGunResult.Busy => "正在处理另一次枪械操作",
        _ => "无效操作"
    };
}
