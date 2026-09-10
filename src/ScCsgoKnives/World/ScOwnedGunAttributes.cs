namespace Game;

/// <summary>Read-only queries over actual writable player slots, not the infinite creative catalogue.</summary>
public static class ScOwnedGunAttributes {
    public sealed record Candidate(int Slot, int Value, ScGunSnapshot Snapshot) {
        public string Name => ScGunNames.Item(Snapshot) + $" · 第 {Slot + 1} 格";
    }
    public static bool TryRead(IInventory inventory, int slot, out Candidate gun) {
        gun = null;
        if (inventory is null || slot < 0 || slot >= inventory.SlotsCount || inventory.GetSlotCount(slot)<=0
            || inventory is ComponentCreativeInventory creative && slot >= creative.OpenSlotsCount) return false;
        int value=inventory.GetSlotValue(slot);
        bool ordinary=BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock),out int index)
            && Terrain.ExtractContents(value)==index && ScGunBlock.IsKnown(value);
        if (!ordinary && !ScGunSkinTemplateBlock.IsTemplate(value) && !ScGunCounterTemplateBlock.IsTemplate(value)) return false;
        if (!EffectiveGunStats.TrySnapshotValue(value,out var snapshot)) return false;
        gun=new(slot,value,snapshot);return true;
    }
    public static Candidate[] Candidates(IInventory inventory) {
        if(inventory is null)return [];
        int count=inventory is ComponentCreativeInventory c ? Math.Min(c.OpenSlotsCount,c.SlotsCount) : inventory.SlotsCount;
        var guns=new List<Candidate>();
        for(int slot=0;slot<count;slot++)if(TryRead(inventory,slot,out var gun))guns.Add(gun);
        return guns.ToArray();
    }
    /// <summary>Revision changes refresh the same gun. Replacement/move/removal invalidates the selection.</summary>
    public static bool TryResolve(IInventory inventory, Candidate selected, ScGunRegistry registry,
        out Candidate current, out EffectiveGunStats stats) {
        stats=default;current=null;
        if(selected is null || !ReferenceEquals(registry,ScGunRegistry.Current)
            || !TryRead(inventory,selected.Slot,out current) || current.Value!=selected.Value
            || current.Snapshot.Id!=selected.Snapshot.Id || current.Snapshot.Variant!=selected.Snapshot.Variant) return false;
        var spec=GunSpec.All[current.Snapshot.Variant];
        // This is a static reference condition, not the player's transient scope/stance/bloom.
        // M4/USP alternate selects the ACTUAL silencer state, exactly as firing does.
        bool alternate=spec.HasSilencer && !current.Snapshot.SilencerOff;
        stats=EffectiveGunStats.Resolve(spec,current.Value,alternate);
        return true;
    }
}
