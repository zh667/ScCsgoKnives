namespace Game;

public enum ScGunResult { Success, Foreign, MissingRecord, StateChanged, InsufficientMaterials, RegistryFull, InventoryRejected, Invalid, DuplicateUnresolved, Busy }

/// <summary>The one way a gun's state changes (M4 plan §5): a read-only Prepare that snapshots the inventory slot and
/// the record, then a Commit on the game thread that re-verifies everything, pays ammo/materials, replaces the item
/// value when a record id has to be published (as a block value), applies the change and bumps the revision - or
/// undoes every step it took and says why. Fresh guns (ids 0/1023) get their record here. A record held anywhere else
/// at commit time is never modified in place: the acting copy gets its own record first (so the original, the copy,
/// and a copy used after a reload all stay apart). Commits never nest: a second Commit while one runs is Busy, so a
/// hostile inventory callback cannot publish an id between the peek and the publish.</summary>
public sealed class ScGunMutation {
    /// <summary>Installed by the gun subsystem: the other holder keys currently holding record <c>id</c>, apart from the given one.</summary>
    public static Func<int, string, IEnumerable<string>> HolderLocator;
    /// <summary>Called after a commit changed which item sits where (a published id), so holder caches can drop.</summary>
    public static Action HoldersChanged;
    static bool s_committing;
    /// <summary>Items a failed rollback could not put back (inventory, slot, value, count): reported, never silently dropped.</summary>
    public static readonly List<(IInventory Inventory, int Slot, int Value, int Count)> PendingRestore = [];
    public IInventory Inventory { get; }
    public int Slot { get; }
    public int Expected { get; private set; }
    public int Variant { get; }
    public int Id { get; private set; }
    public bool Fresh { get; }
    /// <summary>Another holder of this record was seen at Prepare; Commit checks again on its own.</summary>
    public bool NeedsClone { get; private set; }
    public long InventoryRevision { get; }
    public int RecordRevision { get; }
    public string Holder { get; }
    public ScGunSnapshot Before { get; }
    /// <summary>What went wrong, for logs.</summary>
    public string Detail { get; private set; } = "";

    ScGunMutation(IInventory inventory, int slot, int expected, ScGunSnapshot before, bool fresh, bool needsClone, string holder) {
        Inventory = inventory; Slot = slot; Expected = expected; Before = before; Variant = before.Variant; Id = before.Id; Fresh = fresh; NeedsClone = needsClone;
        InventoryRevision = ScInventoryTransaction.Revision(inventory); RecordRevision = before.Revision; Holder = holder;
    }
    static bool HeldElsewhere(int id, string holder) => HolderLocator is not null && HolderLocator(id, holder).Any();

    /// <summary>Read-only. Null with the reason when the slot does not hold a usable gun.</summary>
    public static ScGunMutation Prepare(IInventory inventory, int slot, string holder, out ScGunResult why) {
        var registry = ScGunRegistry.Current;
        why = ScGunResult.Invalid;
        if (registry is null || inventory is null || !ScInventoryTransaction.IsWeaponSlot(inventory, slot)) return null;
        int value = inventory.GetSlotValue(slot), data = Terrain.ExtractData(value);
        if (registry.Disabled || GunSpec.IsForeign(data)) { why = ScGunResult.Foreign; return null; }
        int variant = GunSpec.GetVariant(data);
        if (variant < 0 || variant >= GunSpec.All.Length) return null;
        bool fresh = GunSpec.IsFresh(data);
        ScGunSnapshot before;
        if (fresh) before = ScGunSnapshot.ForFresh(variant, GunSpec.GetId(data) == GunSpec.FreshFull);
        else if (!registry.TryGetSnapshot(GunSpec.GetId(data), out before) || before.Variant != variant) { why = ScGunResult.MissingRecord; return null; }
        why = ScGunResult.Success;
        return new ScGunMutation(inventory, slot, value, before, fresh, !fresh && HeldElsewhere(before.Id, holder), holder);
    }

    static bool Valid(ScGunRecord r) => r.Variant >= 0 && r.Variant < GunSpec.All.Length && r.Rounds >= 0 && r.Rounds <= Math.Max(GunSpec.All[r.Variant].Magazine, 0)
        && r.MaxDurability >= 1 && r.Durability >= 0 && r.Durability <= r.MaxDurability && (r.RechargeReadyAt < 0 || double.IsFinite(r.RechargeReadyAt));

    /// <summary>Applies <paramref name="change"/> to a draft of the record, paying <paramref name="cost"/> of <paramref name="ammo"/>
    /// and the <paramref name="materials"/> (item value → count), all-or-nothing.</summary>
    public ScGunResult Commit(Action<ScGunRecord> change, int ammo = 0, int cost = 0, IReadOnlyDictionary<int, int> materials = null) {
        var registry = ScGunRegistry.Current;
        if (registry is null || registry.Disabled) return Fail(ScGunResult.Foreign, "registry missing or disabled");
        if (s_committing) return Fail(ScGunResult.Busy, "another gun commit is in progress");
        if (Inventory.GetSlotValue(Slot) != Expected || ScInventoryTransaction.Revision(Inventory) != InventoryRevision || !ScInventoryTransaction.IsWeaponSlot(Inventory, Slot)) return Fail(ScGunResult.StateChanged, "slot or inventory changed since Prepare");
        ScGunRecord record = null;
        if (!Fresh) {
            record = registry.Get(Id);
            if (record is null) return Fail(ScGunResult.MissingRecord, $"record {Id} is gone");
            if (record.Revision != RecordRevision) return Fail(ScGunResult.StateChanged, $"record {Id} moved from revision {RecordRevision} to {record.Revision}");
            NeedsClone = HeldElsewhere(Id, Holder); // decided now, not at Prepare: the world may have changed in between
        }
        bool creative = Inventory is ComponentCreativeInventory;
        if (creative && cost != 0) return Fail(ScGunResult.InventoryRejected, "a creative slot cannot pay ammo");
        if (!creative && cost > 0 && ScInventoryTransaction.Count(Inventory, ammo) < cost) return Fail(ScGunResult.InsufficientMaterials, "not enough ammunition");
        if (materials is not null) foreach (var m in materials) if (m.Value < 0 || ScInventoryTransaction.Count(Inventory, m.Key) < m.Value) return Fail(ScGunResult.InsufficientMaterials, $"not enough of item {m.Key}");
        bool needsId = Fresh || NeedsClone;
        int candidate = needsId ? registry.PeekNextId() : -1;
        if (needsId && candidate < 0) return Fail(NeedsClone ? ScGunResult.DuplicateUnresolved : ScGunResult.RegistryFull, "registry full");
        var draft = Fresh
            ? new ScGunRecord { Variant = Variant, Rounds = Before.Rounds, SilencerOff = Before.SilencerOff, Durability = Before.Durability, MaxDurability = Before.MaxDurability }
            : record.Copy();
        draft.Holder = null;
        s_committing = true; // no other gun commit, on any record, until this one has published or rolled back
        var undo = new List<(int Slot, int Value, int Count)>(); // what to put back, newest last
        try {
            change(draft);
            if (!Valid(draft)) return Fail(ScGunResult.Invalid, "the change left the record out of range");
            int replacement = needsId ? Terrain.ReplaceData(Expected, GunSpec.WithId(Variant, candidate)) : Expected;
            try {
                if (materials is not null) {
                    foreach (var m in materials) {
                        int needed = m.Value;
                        for (int i = 0; i < Inventory.SlotsCount && needed > 0; i++) {
                            if (i == Slot || Inventory.GetSlotValue(i) != m.Key || Inventory.GetSlotCount(i) == 0) continue;
                            int want = Math.Min(needed, Inventory.GetSlotCount(i)), got = Inventory.RemoveSlotItems(i, want);
                            if (got > 0) undo.Add((i, m.Key, got));
                            needed -= got;
                            if (got != want) break;
                        }
                        if (needed > 0) { Rollback(undo); return Fail(ScGunResult.InsufficientMaterials, $"item {m.Key} ran short while deducting"); }
                    }
                }
                int ammoBefore = cost > 0 ? ScInventoryTransaction.Count(Inventory, ammo) : 0;
                if (!ScInventoryTransaction.ReplaceWithCost(Inventory, Slot, Expected, replacement, ammo, cost)) { Rollback(undo); return Fail(ScGunResult.InventoryRejected, "the inventory refused the replacement"); }
                if (cost > 0) { int paid = ammoBefore - ScInventoryTransaction.Count(Inventory, ammo); if (paid > 0) undo.Add((-1, ammo, paid)); }
                if (Inventory.GetSlotValue(Slot) != replacement || (!creative && Inventory.GetSlotCount(Slot) != 1)) {
                    // The engine reported the replacement but the slot does not show it: undo the gun and everything paid.
                    if (Inventory.GetSlotCount(Slot) == 0) undo.Add((Slot, Expected, 1));
                    Rollback(undo); return Fail(ScGunResult.InventoryRejected, $"slot {Slot} holds {Inventory.GetSlotValue(Slot)} x{Inventory.GetSlotCount(Slot)} after the replacement");
                }
                if (needsId) {
                    int published = registry.Publish(draft);
                    if (published != candidate) {
                        if (published > 0) registry.Abandon(published, "published under an unexpected id");
                        undo.Add((Slot, Expected, 1)); Inventory.RemoveSlotItems(Slot, 1);
                        Rollback(undo); return Fail(ScGunResult.InventoryRejected, $"registry published {published}, the item was written for {candidate}");
                    }
                    Id = published;
                }
                else {
                    record.Variant = draft.Variant; record.Rounds = draft.Rounds; record.SilencerOff = draft.SilencerOff; record.Durability = draft.Durability;
                    record.MaxDurability = draft.MaxDurability; record.RechargeReadyAt = draft.RechargeReadyAt;
                }
                var committed = needsId ? draft : record;
                committed.Revision = RecordRevision + 1; committed.Holder = Holder;
                Expected = replacement;
                ScInventoryTransaction.Changed(Inventory);
                if (needsId) HoldersChanged?.Invoke();
                return ScGunResult.Success;
            }
            catch (Exception e) {
                KnifeLog.Error($"gun mutation threw during the inventory step: {e}");
                Rollback(undo);
                return Fail(ScGunResult.InventoryRejected, "the inventory threw: " + e.Message);
            }
        }
        finally { s_committing = false; }
    }
    ScGunResult Fail(ScGunResult result, string detail) { Detail = detail; return result; }
    /// <summary>Puts back what this commit took, newest first, and verifies each item landed; anything that did not is kept in PendingRestore and logged.</summary>
    void Rollback(List<(int Slot, int Value, int Count)> undo) {
        for (int k = undo.Count - 1; k >= 0; k--) {
            var (slot, value, count) = undo[k];
            bool restored = false;
            try {
                if (slot >= 0) {
                    int before = Inventory.GetSlotCount(slot);
                    if (Inventory.GetSlotCount(slot) == 0 || Inventory.GetSlotValue(slot) == value) { Inventory.AddSlotItems(slot, value, count); restored = Inventory.GetSlotValue(slot) == value && Inventory.GetSlotCount(slot) >= before + count; }
                }
                if (!restored) { // any slot that can take it (ammo refunds have no fixed slot)
                    for (int i = 0; i < Inventory.SlotsCount && !restored; i++) {
                        if (i == Slot || !(Inventory.GetSlotCount(i) == 0 || Inventory.GetSlotValue(i) == value) || Inventory.GetSlotCapacity(i, value) < count) continue;
                        int before = Inventory.GetSlotCount(i); Inventory.AddSlotItems(i, value, count);
                        restored = Inventory.GetSlotValue(i) == value && Inventory.GetSlotCount(i) >= before + count;
                    }
                }
            }
            catch (Exception e) { KnifeLog.Error($"gun mutation rollback threw putting back {count} x {value}: {e.Message}"); }
            if (!restored) { PendingRestore.Add((Inventory, slot, value, count)); KnifeLog.Error($"gun mutation could not put back {count} x item {value} into slot {slot}; kept in PendingRestore"); }
        }
        undo.Clear();
        ScInventoryTransaction.Changed(Inventory);
    }

    public static string Explain(ScGunResult result) => result switch {
        ScGunResult.Success => "完成",
        ScGunResult.Foreign => "这把枪的数据来自旧版本或本世界已停用枪械",
        ScGunResult.MissingRecord => "这把枪的状态记录不存在",
        ScGunResult.StateChanged => "枪械或背包状态已变化，请重试",
        ScGunResult.InsufficientMaterials => "材料或弹药不足",
        ScGunResult.RegistryFull => $"枪械状态表已满（{GunSpec.LastId} 把），新枪无法登记；已有的枪不受影响",
        ScGunResult.InventoryRejected => PendingRestore.Count > 0 ? "背包操作失败，且有物品未能放回，请查看日志" : "背包拒绝了这次修改，未扣除任何物品",
        ScGunResult.DuplicateUnresolved => "这把枪与另一把共用记录，且状态表已满，无法分离",
        ScGunResult.Busy => "正在处理另一次枪械操作",
        _ => "无效操作"
    };
}
