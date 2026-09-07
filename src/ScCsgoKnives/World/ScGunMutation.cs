namespace Game;

public enum ScGunResult { Success, Foreign, MissingRecord, StateChanged, InsufficientMaterials, RegistryFull, InventoryRejected, Invalid, DuplicateUnresolved, Busy }

/// <summary>The one way a gun's state changes (M4 plan §5): a read-only Prepare that snapshots the inventory slot and
/// the record, then a Commit on the game thread that re-verifies everything, pays ammo/materials, replaces the item
/// value when a record id has to be published, applies the change and bumps the revision - or rolls back and says why.
/// Fresh guns (ids 0/1023) get their record here; a copy that turns out to share a record with another holder gets its
/// own before it is used. Nothing is reserved between Prepare and Commit, so a rejected commit publishes no id.</summary>
public sealed class ScGunMutation {
    /// <summary>Installed by the gun subsystem: the other holders (holder keys) currently holding record <c>id</c>, apart from the given one.</summary>
    public static Func<int, string, IEnumerable<string>> HolderLocator;
    static readonly HashSet<int> s_committing = [];
    public IInventory Inventory { get; }
    public int Slot { get; }
    public int Expected { get; private set; }
    public int Variant { get; }
    public int Id { get; private set; }
    public bool Fresh { get; }
    public bool NeedsClone { get; }
    public long InventoryRevision { get; }
    public int RecordRevision { get; }
    public string Holder { get; }
    public ScGunSnapshot Before { get; }

    ScGunMutation(IInventory inventory, int slot, int expected, ScGunSnapshot before, bool fresh, bool needsClone, string holder) {
        Inventory = inventory; Slot = slot; Expected = expected; Before = before; Variant = before.Variant; Id = before.Id; Fresh = fresh; NeedsClone = needsClone;
        InventoryRevision = ScInventoryTransaction.Revision(inventory); RecordRevision = before.Revision; Holder = holder;
    }

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
        bool needsClone = false;
        if (fresh) before = ScGunSnapshot.ForFresh(variant, GunSpec.GetId(data) == GunSpec.FreshFull);
        else {
            if (!registry.TryGetSnapshot(GunSpec.GetId(data), out before)) { why = ScGunResult.MissingRecord; return null; }
            if (before.Variant != variant) { why = ScGunResult.MissingRecord; return null; }
            var record = registry.Get(before.Id);
            if (record.Holder is not null && record.Holder != holder && HolderLocator is not null && HolderLocator(before.Id, holder).Any()) needsClone = true;
        }
        why = ScGunResult.Success;
        return new ScGunMutation(inventory, slot, value, before, fresh, needsClone, holder);
    }

    static bool Valid(ScGunRecord r) => r.Variant >= 0 && r.Variant < GunSpec.All.Length && r.Rounds >= 0 && r.Rounds <= Math.Max(GunSpec.All[r.Variant].Magazine, 0)
        && r.MaxDurability >= 1 && r.Durability >= 0 && r.Durability <= r.MaxDurability && (r.RechargeReadyAt < 0 || double.IsFinite(r.RechargeReadyAt));

    /// <summary>Applies <paramref name="change"/> to a draft of the record, paying <paramref name="cost"/> of <paramref name="ammo"/>
    /// and the <paramref name="materials"/> (item value → count), all-or-nothing.</summary>
    public ScGunResult Commit(Action<ScGunRecord> change, int ammo = 0, int cost = 0, IReadOnlyDictionary<int, int> materials = null) {
        var registry = ScGunRegistry.Current;
        if (registry is null || registry.Disabled) return ScGunResult.Foreign;
        if (Inventory.GetSlotValue(Slot) != Expected || ScInventoryTransaction.Revision(Inventory) != InventoryRevision || !ScInventoryTransaction.IsWeaponSlot(Inventory, Slot)) return ScGunResult.StateChanged;
        ScGunRecord record = null;
        if (!Fresh) {
            record = registry.Get(Id);
            if (record is null) return ScGunResult.MissingRecord;
            if (record.Revision != RecordRevision) return ScGunResult.StateChanged;
            if (s_committing.Contains(Id)) return ScGunResult.Busy;
        }
        bool creative = Inventory is ComponentCreativeInventory;
        if (creative && cost != 0) return ScGunResult.InventoryRejected;
        if (!creative && cost > 0 && ScInventoryTransaction.Count(Inventory, ammo) < cost) return ScGunResult.InsufficientMaterials;
        if (materials is not null) foreach (var m in materials) if (m.Value < 0 || ScInventoryTransaction.Count(Inventory, m.Key) < m.Value) return ScGunResult.InsufficientMaterials;
        bool needsId = Fresh || NeedsClone;
        int candidate = -1;
        if (needsId) { candidate = registry.PeekNextId(); if (candidate < 0) return NeedsClone ? ScGunResult.DuplicateUnresolved : ScGunResult.RegistryFull; }
        var draft = Fresh
            ? new ScGunRecord { Variant = Variant, Rounds = Before.Rounds, SilencerOff = Before.SilencerOff, Durability = Before.Durability, MaxDurability = Before.MaxDurability }
            : record.Copy();
        draft.Holder = null;
        int guarded = Id; // the record being committed; a clone publishes a new Id, the guard still belongs to the source
        if (!Fresh) s_committing.Add(guarded); // from here on a re-entrant commit on this record is refused as Busy
        try {
            change(draft);
            if (!Valid(draft)) return ScGunResult.Invalid;
            int replacement = needsId ? Terrain.ReplaceData(Expected, GunSpec.WithId(Variant, candidate)) : Expected; // a block value, not bare data
            var taken = new List<(int Slot, int Value, int Count)>();
            if (materials is not null) {
                foreach (var m in materials) {
                    int needed = m.Value;
                    for (int i = 0; i < Inventory.SlotsCount && needed > 0; i++) {
                        if (i == Slot || Inventory.GetSlotValue(i) != m.Key || Inventory.GetSlotCount(i) == 0) continue;
                        int want = Math.Min(needed, Inventory.GetSlotCount(i)), got = Inventory.RemoveSlotItems(i, want);
                        taken.Add((i, m.Key, got)); needed -= got;
                        if (got != want) break;
                    }
                    if (needed > 0) { Restore(taken); return ScGunResult.InsufficientMaterials; }
                }
            }
            int ammoBefore = cost > 0 ? ScInventoryTransaction.Count(Inventory, ammo) : 0;
            if (!ScInventoryTransaction.ReplaceWithCost(Inventory, Slot, Expected, replacement, ammo, cost)) { Restore(taken); return ScGunResult.InventoryRejected; }
            if (Inventory.GetSlotValue(Slot) != replacement || (!creative && Inventory.GetSlotCount(Slot) != 1)) {
                // The engine reported the replacement but the slot does not show it: put the gun and the ammo back as far as possible.
                KnifeLog.Error($"gun mutation: slot {Slot} holds {Inventory.GetSlotValue(Slot)} x{Inventory.GetSlotCount(Slot)} after replacing {Expected} with {replacement}; restoring");
                if (Inventory.GetSlotCount(Slot) == 0) Inventory.AddSlotItems(Slot, Expected, 1);
                if (cost > 0) { int missing = ammoBefore - ScInventoryTransaction.Count(Inventory, ammo); for (int i = 0; i < Inventory.SlotsCount && missing > 0; i++) if (i != Slot && (Inventory.GetSlotCount(i) == 0 || Inventory.GetSlotValue(i) == ammo)) { Inventory.AddSlotItems(i, ammo, missing); missing = 0; } }
                Restore(taken); ScInventoryTransaction.Changed(Inventory); return ScGunResult.InventoryRejected;
            }
            if (needsId) {
                int published = registry.Publish(draft);
                if (published != candidate) { KnifeLog.Error($"gun registry published id {published} instead of the candidate {candidate}; item {replacement} left pointing at the candidate"); }
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
            return ScGunResult.Success;
        }
        finally { if (!Fresh) s_committing.Remove(guarded); }
    }
    void Restore(List<(int Slot, int Value, int Count)> taken) { foreach (var item in taken) Inventory.AddSlotItems(item.Slot, item.Value, item.Count); }

    public static string Explain(ScGunResult result) => result switch {
        ScGunResult.Success => "完成",
        ScGunResult.Foreign => "这把枪的数据来自旧版本或本世界已停用枪械",
        ScGunResult.MissingRecord => "这把枪的状态记录不存在",
        ScGunResult.StateChanged => "枪械或背包状态已变化，请重试",
        ScGunResult.InsufficientMaterials => "材料或弹药不足",
        ScGunResult.RegistryFull => $"枪械状态表已满（{GunSpec.LastId} 把），新枪无法登记；已有的枪不受影响",
        ScGunResult.InventoryRejected => "背包拒绝了这次修改，未扣除任何物品",
        ScGunResult.DuplicateUnresolved => "这把枪与另一把共用记录，且状态表已满，无法分离",
        ScGunResult.Busy => "正在处理这把枪的另一次操作",
        _ => "无效操作"
    };
}
