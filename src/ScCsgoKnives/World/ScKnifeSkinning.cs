namespace Game;

/// <summary>Knife finishes retain the existing item bits; no gun record is allocated.</summary>
public static class ScKnifeSkinning {
    public sealed record Candidate(int Slot, int Value) {
        public int Variant => ScKnifeBlock.GetVariant(Value);
        public int Skin => ScKnifeBlock.SkinOf(Value);
    }
    public sealed record Finish(int Variant, int Skin, int Value);
    public sealed record Quote(IInventory Inventory, object Storage, int Slot, int Value, int Replacement,
        bool Free, IReadOnlyDictionary<int,int> Cost);
    public static IEnumerable<Candidate> Candidates(IInventory inventory) {
        inventory = ScInventoryIdentity.Inventory(inventory);
        if(inventory is null)yield break;
        int slots=inventory is ComponentCreativeInventory?Math.Min(10,inventory.SlotsCount):inventory.SlotsCount;
        for(int i=0;i<slots;i++) {
            int value=inventory.GetSlotValue(i);
            if(ScInventoryTransaction.IsWeaponSlot(inventory,i) && ScKnifeBlock.IsKnown(value)
                && Terrain.ExtractContents(value)==BlocksManager.GetBlockIndex<ScKnifeBlock>(true)
                && ScKnifeSkinCatalog.Finish(ScKnifeBlock.GetVariant(value)) is not null)yield return new(i,value);
        }
    }
    public static Dictionary<int,int> Cost(int skin) {
        var cost=new Dictionary<int,int>{{ScWeaponMaterialBlock.Value(ScWeaponMaterialBlock.Paint),skin==0?2:8}};
        if(skin!=0){cost[ScWeaponMaterialBlock.Value(ScWeaponMaterialBlock.Blank)]=2;cost[ScComponentCrafting.Resolve("diamond")]=2;}
        return cost;
    }
    public static Quote Prepare(IInventory inventory,Candidate knife,int skin,bool free) {
        if(inventory is null || knife is null || !ScInventoryTransaction.IsWeaponSlot(inventory,knife.Slot)
            || inventory.GetSlotValue(knife.Slot)!=knife.Value || !Candidates(inventory).Contains(knife)
            || skin==knife.Skin || (skin!=0 && skin!=ScKnifeSkinCatalog.ForVariant(knife.Variant)))return null;
        int data=Terrain.ExtractData(knife.Value);
        int result=Terrain.ReplaceData(knife.Value,(data & ~(3<<5)) | (skin<<5));
        return new(inventory,ScInventoryIdentity.Storage(inventory),knife.Slot,knife.Value,result,free,free?new Dictionary<int,int>():Cost(skin));
    }
    public static bool Apply(IInventory inventory,Quote quote,bool free) {
        if(quote is null || !ReferenceEquals(inventory,quote.Inventory) || quote.Free!=free || !ScGunMutation.TryEnter())return false;
        var registry=ScGunRegistry.Current;ScGunInventoryJournal journal=null;string owner=null;
        try {
            owner=registry?.RecoveryOwner?.Invoke(inventory);
            if(registry is null || registry.Disabled || string.IsNullOrWhiteSpace(owner) || registry.Recovery.HasPending(owner))return false;
            if(!ReferenceEquals(quote.Storage,ScInventoryIdentity.Storage(inventory)))return false;
            if(quote.Storage is IInventory real)inventory=real;
            if(!ScInventoryTransaction.IsWeaponSlot(inventory,quote.Slot) || inventory.GetSlotValue(quote.Slot)!=quote.Value
                || inventory.GetSlotCapacity(quote.Slot,quote.Replacement)<1)return false;
            foreach(var p in quote.Cost)if(ScInventoryTransaction.Count(inventory,p.Key)<p.Value)return false;
            journal=new(inventory);
            void Stable(){if(!ReferenceEquals(quote.Storage,ScInventoryIdentity.Storage(inventory)) || !ReferenceEquals(quote.Storage,ScInventoryIdentity.Storage(quote.Inventory)) || !ReferenceEquals(registry,ScGunRegistry.Current) || registry.RecoveryOwner?.Invoke(quote.Inventory)!=owner)throw new InvalidOperationException("Knife inventory changed");}
            foreach(var p in quote.Cost) {
                int left=p.Value;
                for(int i=0;i<inventory.SlotsCount && left>0;i++)if(i!=quote.Slot && inventory.GetSlotValue(i)==p.Key){
                    int take=Math.Min(left,inventory.GetSlotCount(i));if(take<=0)continue;Stable();journal.RemoveExact(i,p.Key,take);left-=take;
                }
                if(left!=0)throw new InvalidOperationException("Knife finish materials changed");
            }
            Stable();
            if(inventory is ComponentCreativeInventory)journal.ReplaceCreative(quote.Slot,quote.Value,quote.Replacement);
            else {journal.RemoveExact(quote.Slot,quote.Value,1);Stable();journal.AddExact(quote.Slot,quote.Replacement,1);}
            Stable();ScInventoryTransaction.Changed(inventory);return true;
        } catch(Exception e) {
            if(journal is not null){if(ReferenceEquals(quote.Storage,ScInventoryIdentity.Storage(inventory)) && registry.RecoveryOwner?.Invoke(inventory)==owner)journal.Rollback(registry.Recovery,owner);else journal.DeferRollback(registry.Recovery,owner);}
            KnifeLog.Warning("[KNIFE_SKIN] rolled back: "+e.Message);return false;
        } finally {ScGunMutation.Exit();}
    }
}
