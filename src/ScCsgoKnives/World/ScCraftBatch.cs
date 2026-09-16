namespace Game;

/// <summary>Plan the entire batch against the post-deduction inventory before spending anything.</summary>
public static class ScCraftBatch {
    public const int Maximum = 100;
    sealed record Change(int Slot, int Value, int Count);
    sealed class Plan {
        public readonly List<Change> Remove = [], Add = [];
        public int[] Values, Counts;
        public object Storage;
        public bool Creative;
    }
    public static Dictionary<int,int> Cost(IReadOnlyDictionary<int,int> unit, int quantity) {
        if (quantity < 1 || quantity > Maximum || unit is null) throw new ArgumentOutOfRangeException(nameof(quantity));
        return unit.ToDictionary(p=>p.Key,p=>p.Value>0?checked(p.Value*quantity):throw new ArgumentOutOfRangeException(nameof(unit)));
    }
    static Plan Prepare(IInventory inventory,int result,IReadOnlyDictionary<int,int> unit,int quantity,out string reason) {
        reason="";
        if(inventory is null || quantity<1 || quantity>Maximum || result==0 || unit is null) {reason="数量须为 1～100。";return null;}
        var plan=new Plan { Creative=inventory is ComponentCreativeInventory, Storage=ScInventoryIdentity.Storage(inventory) };
        int slots=plan.Creative?Math.Min(10,inventory.SlotsCount):inventory.SlotsCount;
        plan.Values=Enumerable.Range(0,slots).Select(inventory.GetSlotValue).ToArray();
        plan.Counts=Enumerable.Range(0,slots).Select(inventory.GetSlotCount).ToArray();
        int[] counts=(int[])plan.Counts.Clone();
        foreach(var p in Cost(unit,quantity)) {
            int left=p.Value;
            for(int i=0;i<slots && left>0;i++) if(plan.Values[i]==p.Key && counts[i]>0) {
                int take=Math.Min(left,counts[i]);plan.Remove.Add(new(i,p.Key,take));counts[i]-=take;left-=take;
            }
            if(left>0){reason="材料不足。";return null;}
        }
        // Creative writable slots are infinite sources, not stacks: one chosen output per empty slot.
        int remaining=quantity;
        for(int pass=0;pass<2;pass++) for(int i=0;i<slots && remaining>0;i++) {
            bool same=counts[i]>0 && plan.Values[i]==result;
            if(pass==0 ? !same || plan.Creative : counts[i]!=0)continue;
            int capacity=plan.Creative?Math.Min(1,inventory.GetSlotCapacity(i,result)):inventory.GetSlotCapacity(i,result);
            int amount=Math.Min(remaining,Math.Max(0,capacity-counts[i]));
            if(amount<=0)continue;
            plan.Add.Add(new(i,result,amount));counts[i]+=amount;remaining-=amount;
        }
        if(remaining>0){reason=plan.Creative?"快捷栏空位不足（创造物品每格一个无限来源）。":"成品空间不足。";return null;}
        return plan;
    }
    public static string Unavailable(IInventory inventory,int result,IReadOnlyDictionary<int,int> unit,int quantity) {
        try {Prepare(inventory,result,unit,quantity,out string reason);return reason;}
        catch {return "材料或库存暂不可用。";}
    }
    public static bool TryCraft(IInventory inventory,int result,IReadOnlyDictionary<int,int> unit,int quantity) {
        if(!ScGunMutation.TryEnter())return false;
        var registry=ScGunRegistry.Current;string owner=null;
        object originalStorage=null;
        ScGunInventoryJournal journal=null;
        try {
            if(inventory is null || registry is null || registry.Disabled)return false;
            originalStorage=ScInventoryIdentity.Storage(inventory);
            // Pin known forwarding inventories whenever the backing storage is itself an inventory.
            if(originalStorage is IInventory real)inventory=real;
            journal=new ScGunInventoryJournal(inventory);
            owner=ScWeaponCrafting.RecoveryOwnerOverride?.Invoke(inventory)??registry.RecoveryOwner?.Invoke(inventory);
            if(string.IsNullOrWhiteSpace(owner)||registry.Recovery.HasPending(owner))return false;
            var plan=Prepare(inventory,result,unit,quantity,out _);if(plan is null)return false;
            void Stable() {
                if(!ReferenceEquals(plan.Storage,ScInventoryIdentity.Storage(inventory)) || !ReferenceEquals(registry,ScGunRegistry.Current))
                    throw new InvalidOperationException("Inventory storage changed during crafting");
            }
            Stable();
            for(int i=0;i<plan.Values.Length;i++)if(inventory.GetSlotValue(i)!=plan.Values[i]||inventory.GetSlotCount(i)!=plan.Counts[i])return false;
            foreach(var c in plan.Remove){Stable();journal.RemoveExact(c.Slot,c.Value,c.Count);}
            foreach(var c in plan.Add){Stable();if(plan.Creative)journal.ReplaceCreative(c.Slot,plan.Values[c.Slot],c.Value);else journal.AddExact(c.Slot,c.Value,c.Count);}
            Stable();ScInventoryTransaction.Changed(inventory);return true;
        } catch(Exception e) {
            if(registry is not null && owner is not null && journal is not null) {
                if(ReferenceEquals(originalStorage,ScInventoryIdentity.Storage(inventory)))journal.Rollback(registry.Recovery,owner);
                else journal.DeferRollback(registry.Recovery,owner);
            }
            KnifeLog.Warning("[GUN_CRAFT] batch refused/rolled back: "+e.Message);return false;
        } finally {ScGunMutation.Exit();}
    }
}
