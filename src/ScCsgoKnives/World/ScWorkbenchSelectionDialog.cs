using System.Collections;
using Engine;
namespace Game;

/// <summary>Native responsive workshop: category tabs, list, preview, materials and fixed footer.
/// Browsing is read-only; the existing quote/commit callback only runs after explicit Continue.</summary>
public sealed class ScWorkbenchSelectionDialog : Dialog {
    readonly object[] m_items;
    readonly Func<object, string> m_label;
    readonly Action<object> m_choose;
    readonly IInventory m_inventory;
    readonly bool m_creative;
    readonly ListPanelWidget m_list = new() { ItemSize = 52, Direction = LayoutDirection.Vertical };
    readonly CanvasWidget m_listHost = new(), m_previewHost = new(), m_detailHost = new();
    readonly BlockIconWidget m_icon = new() { Size = new Vector2(160), HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center };
    readonly LabelWidget m_title, m_name;
    readonly StackPanelWidget m_details = new() { Direction = LayoutDirection.Vertical, Margin = new Vector2(10,6) };
    readonly ScrollPanelWidget m_detailScroll = new() { Direction = LayoutDirection.Vertical };
    readonly ScrollPanelWidget m_tabsScroll = new() { Direction = LayoutDirection.Horizontal };
    readonly StackPanelWidget m_tabs = new() { Direction = LayoutDirection.Horizontal };
    readonly ButtonWidget m_confirm = ScGunUi.Button("下一步",130), m_cancel = ScGunUi.Button("关闭",110);
    readonly List<(string Category, ButtonWidget Button)> m_categories = [];
    string m_category = "全部";
    object m_selected;
    bool m_done;

    public ScWorkbenchSelectionDialog(string title, IEnumerable items, float itemHeight, Func<object,string> label,
        Action<object> choose, IInventory inventory, bool creative) {
        m_items = items.Cast<object>().ToArray(); m_label = label; m_choose = choose; m_inventory = inventory; m_creative = creative;
        HorizontalAlignment = VerticalAlignment = WidgetAlignment.Center;
        Children.Add(ScGunUi.Frame());
        m_title = ScGunUi.Label(title, .95f); m_title.Ellipsis = true; m_title.MaxLines=1; Children.Add(m_title);
        m_tabsScroll.Children.Add(m_tabs); Children.Add(m_tabsScroll);
        foreach (var category in new[] { "全部" }.Concat(m_items.Select(CategoryOf).Distinct())) {
            var b=ScGunUi.Button(category,92); b.Margin=new Vector2(2,0); m_tabs.Children.Add(b); m_categories.Add((category,b));
        }
        m_listHost.Children.Add(ScGunUi.Frame()); m_listHost.Children.Add(m_list); Children.Add(m_listHost);
        m_list.ItemWidgetFactory = item => {
            var row = new StackPanelWidget { Direction=LayoutDirection.Horizontal, Margin=new Vector2(4,2) };
            int value=ValueOf(item);
            if(value!=0) row.Children.Add(new BlockIconWidget { Value=value,Size=new Vector2(40) });
            row.Children.Add(new LabelWidget { Text=m_label(item),FontScale=.66f,WordWrap=true,MaxLines=2,Ellipsis=true,VerticalAlignment=WidgetAlignment.Center });
            return row;
        };
        m_list.ItemClicked=item=>Select(item);
        m_previewHost.Children.Add(ScGunUi.Frame()); m_previewHost.Children.Add(m_icon); Children.Add(m_previewHost);
        m_detailHost.Children.Add(ScGunUi.Frame()); m_detailScroll.Children.Add(m_details); m_detailHost.Children.Add(m_detailScroll); Children.Add(m_detailHost);
        m_name=ScGunUi.Label("",.9f,ScGunUi.Accent); m_name.WordWrap=true;
        Children.Add(m_confirm); Children.Add(m_cancel);
        Filter("全部");
    }
    static string CategoryOf(object item) => item is ScWeaponCrafting.Entry e ? e.Knife ? "刀具" : ScGunDurability.ClassOf(e.Name) switch {
        ScGunDurability.Class.Pistol=>"手枪",ScGunDurability.Class.Smg=>"冲锋枪",ScGunDurability.Class.Rifle=>"步枪",
        ScGunDurability.Class.Shotgun=>"霰弹枪",ScGunDurability.Class.BoltSniper or ScGunDurability.Class.AutoSniper=>"狙击枪",
        ScGunDurability.Class.MachineGun=>"机枪",_=>"电击枪"
    } : item is ScGunSkin ? "涂装" : item is ScWeaponRepair.Candidate or ScWeaponSkinning.Candidate or ScGunCounter.Candidate ? "背包枪械" : "功能";
    static int ValueOf(object item) => item switch {
        ScWeaponCrafting.Entry e=>e.Value, ScWeaponRepair.Candidate c=>c.Value,ScWeaponSkinning.Candidate c=>c.Value,ScGunCounter.Candidate c=>c.Value,
        ScGunSkin s=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunSkinTemplateBlock>(true),0,s.PaintId),_=>0
    };
    void Filter(string category) {
        m_category=category; m_list.ClearItems();
        foreach(var item in m_items.Where(i=>category=="全部"||CategoryOf(i)==category)) m_list.AddItem(item);
        m_list.ScrollPosition=0; Select(m_list.Items.Count>0?m_list.Items[0]:null);
        foreach(var pair in m_categories) pair.Button.Color=pair.Category==category?ScGunUi.Accent:ScGunUi.Text;
    }
    void Select(object item) {
        m_selected=item; m_list.SelectedItem=item; m_confirm.IsEnabled=item is not null;
        m_details.Children.Clear(); m_name.Text=item is null?"没有可用项目":m_label(item); m_details.Children.Add(m_name);
        int value=ValueOf(item); m_icon.IsVisible=value!=0; if(value!=0)m_icon.Value=value;
        var materials = item switch {
            ScWeaponCrafting.Entry e=>e.Materials(),
            ScGunSkin s=>ScGunSkinCatalog.CostOf(s,ScWeaponMaterialBlock.Value),
            _=>new Dictionary<int,int>()
        };
        if(item is ScWeaponCrafting.Entry entry) {
            m_details.Children.Add(ScGunUi.Note($"制作等级 {entry.Level} · 产出 1 件"));
            m_details.Children.Add(ScGunUi.Note(entry.Knife?"组装刀具；下一步确认后才扣料。":"空枪交付，弹药另行制作；下一步确认后才扣料。"));
        }
        else if(value!=0 && EffectiveGunStats.TrySnapshotValue(value,out var s))
            m_details.Children.Add(ScGunUi.Note($"弹量 {s.Rounds} · {(s.CounterInstalled?$"计数 {s.KillCount} / Lv{s.Level}":"未安装计数器")}\n选择下一步查看本次操作的精确报价。"));
        else m_details.Children.Add(ScGunUi.Note("选择下一步进入此功能。操作不会在浏览列表时执行。"));
        if(m_creative)m_details.Children.Add(ScGunUi.Note("创造模式：不消耗材料。"));
        if(materials.Count>0) {
            m_details.Children.Add(ScGunUi.Heading("材料 · 需要 / 持有"));
            foreach(var p in materials) {
                int owned=ScInventoryTransaction.Count(m_inventory,p.Key);
                var row=new StackPanelWidget { Direction=LayoutDirection.Horizontal, Margin=new Vector2(0,3) };
                row.Children.Add(new BlockIconWidget { Value=p.Key,Size=new Vector2(36) });
                row.Children.Add(new LabelWidget { Text=$"{BlocksManager.Blocks[Terrain.ExtractContents(p.Key)].GetDisplayName(null,p.Key)}\n{p.Value} / {owned}",FontScale=.65f,WordWrap=true,
                    Color=owned>=p.Value||m_creative?ScGunUi.Text:new Color(240,150,110),VerticalAlignment=WidgetAlignment.Center });
                m_details.Children.Add(row);
            }
        }
        m_detailScroll.ScrollPosition=0;
    }
    void Place(Widget widget,float x,float y,float width,float height) {
        SetWidgetPosition(widget,new Vector2(x,y));
        if(widget is CanvasWidget c)c.Size=new Vector2(width,height);
        else if(widget is LabelWidget l)l.Size=new Vector2(width,height);
        else widget.DesiredSize=new Vector2(width,height);
    }
    public override void MeasureOverride(Vector2 available) {
        float w=Math.Min(1100,Math.Max(280,available.X-24)),h=Math.Min(650,Math.Max(220,available.Y-24));
        Size=new Vector2(w,h); Place(m_title,12,8,w-24,32); Place(m_tabsScroll,12,48,w-24,52);
        float bodyH=h-172;
        m_previewHost.IsVisible=h>=380;
        if(h<380){float lw=Math.Min(220,w*.42f);Place(m_listHost,12,110,lw,bodyH);Place(m_detailHost,20+lw,110,w-lw-32,bodyH);}
        else if(w>=900){Place(m_previewHost,12,110,210,bodyH);Place(m_listHost,230,110,280,bodyH);Place(m_detailHost,518,110,w-530,bodyH);}
        else if(w>=620){Place(m_listHost,12,110,210,bodyH);Place(m_previewHost,230,110,w-242,100);Place(m_detailHost,230,218,w-242,bodyH-108);}
        else{Place(m_listHost,12,110,Math.Max(130,w*.42f),bodyH);float x=20+Math.Max(130,w*.42f);Place(m_previewHost,x,110,w-x-12,80);Place(m_detailHost,x,198,w-x-12,bodyH-88);}
        m_icon.Size=new Vector2(w>=900?160:76);
        Place(m_confirm,w-154,h-56,130,48);Place(m_cancel,12,h-56,110,48);
        base.MeasureOverride(available);
    }
    public override void Update() {
        if(m_done)return;
        foreach(var p in m_categories)if(p.Button.IsClicked){Filter(p.Category);return;}
        if(m_list.SelectedItem is {} selected && !ReferenceEquals(selected,m_selected))Select(selected);
        if(Input.Cancel||Input.Back||m_cancel.IsClicked){m_done=true;DialogsManager.HideDialog(this);return;}
        if(m_confirm.IsClicked&&m_selected is not null){m_done=true;DialogsManager.HideDialog(this);m_choose(m_selected);}
    }
}
