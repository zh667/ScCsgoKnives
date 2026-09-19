using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

/// <summary>Ordinary component inventory: visible to the base gun holder scanner and saved with the entity.</summary>
public sealed class ComponentTacticalInventory : ComponentInventoryBase {
    public override int ActiveSlotIndex {get=>0;set{}}
    public override int GetSlotCapacity(int slot,int value) {
        if(slot<0||slot>=SlotsCount)return 0;
        if(slot==0)return ScTacticalShieldBlock.IsShield(value)||Terrain.ExtractContents(value)==BlocksManager.GetBlockIndex<ScGunBlock>(true)||ScGunSkinTemplateBlock.IsTemplate(value)||ScGunCounterTemplateBlock.IsTemplate(value)?1:0;
        return Terrain.ExtractContents(value)==BlocksManager.GetBlockIndex<ScAmmoBlock>(true)?40:0;
    }
    public override void AddSlotItems(int slot,int value,int count){base.AddSlotItems(slot,value,count);ScInventoryTransaction.Changed(this);}
    public override int RemoveSlotItems(int slot,int count){int n=base.RemoveSlotItems(slot,count);if(n>0)ScInventoryTransaction.Changed(this);return n;}
    public override void Load(ValuesDictionary values,IdToEntityMap map){base.Load(values,map);if(SlotsCount!=5)throw new InvalidOperationException("战术同伴装备栏格式不受支持。");}
}
