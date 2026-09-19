using System.Runtime.CompilerServices;
using Engine;
namespace Game;

public static class ScInventoryWear {
    static readonly ConditionalWeakTable<InventorySlotWidget,LabelWidget> Labels=new();
    public static string Text(int value) {
        // Creative skin/counter templates are separate blocks, not registry IDs.
        if(ScGunSkinTemplateBlock.TrySnapshot(value,out _) || ScGunCounterTemplateBlock.TrySnapshot(value,out _))return "100%";
        return Terrain.ExtractContents(value)==BlocksManager.GetBlockIndex<ScGunBlock>(true)
            && ScGunBlock.IsKnown(value)?ScGunDurability.PercentText(Terrain.ExtractData(value)):null;
    }
    public static void Update(InventorySlotWidget slot) {
        string text=slot.m_inventory is {} inv && slot.m_slotIndex>=0 && slot.m_slotIndex<inv.SlotsCount
            && inv.GetSlotCount(slot.m_slotIndex)>0 && !slot.HideBlockIcon && slot.m_blockIconWidget.IsVisible
            ?Text(inv.GetSlotValue(slot.m_slotIndex)):null;
        if(text is null) {if(Labels.TryGetValue(slot,out var old))old.IsVisible=false;return;}
        if(!Labels.TryGetValue(slot,out var label)) {
            label=new LabelWidget{Name="ScGunWearPercent",FontScale=.36f,DropShadow=true,IsHitTestVisible=false,
                HorizontalAlignment=WidgetAlignment.Near,VerticalAlignment=WidgetAlignment.Far,Margin=new Vector2(3,2)};
            Labels.Add(slot,label);slot.Children.Add(label);
        }
        int data=Terrain.ExtractData(slot.m_inventory.GetSlotValue(slot.m_slotIndex));
        bool instance=Terrain.ExtractContents(slot.m_inventory.GetSlotValue(slot.m_slotIndex))==BlocksManager.GetBlockIndex<ScGunBlock>(true);
        label.Text=text;label.IsVisible=true;label.Color=instance&&(ScGunDurability.IsLow(data)||ScGunDurability.IsBroken(data))?new Color(255,115,90):Color.White;
    }
}
