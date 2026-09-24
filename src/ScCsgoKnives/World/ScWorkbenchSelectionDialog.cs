using System.Collections;
using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Native responsive workshop: category tabs, list, preview, materials and fixed footer.
/// Recipes use an explicit batch craft button; other operations open their quote on double click/tap.</summary>
public sealed class ScWorkbenchSelectionDialog : Dialog {
    readonly object[] m_items;
    readonly Func<object, string> m_label;
    readonly Action<object> m_choose;
    readonly IInventory m_inventory;
    readonly bool m_creative;
    readonly ListPanelWidget m_list = new() { ItemSize = 52, Direction = LayoutDirection.Vertical };
    readonly CanvasWidget m_listHost = new(), m_previewHost = new(), m_detailHost = new();
    readonly BlockIconWidget m_icon = new() { Size = new Vector2(160), HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center };
    readonly RectangleWidget m_picture = new() { FillColor=Color.White,OutlineThickness=0,HorizontalAlignment=WidgetAlignment.Center,VerticalAlignment=WidgetAlignment.Center,IsVisible=false };
    readonly ButtonWidget m_apply=ScGunUi.Button("应用手套",146);
    readonly LabelWidget m_title, m_name;
    readonly StackPanelWidget m_details = new() { Direction = LayoutDirection.Vertical, Margin = new Vector2(10,6) };
    readonly ScrollPanelWidget m_detailScroll = new() { Direction = LayoutDirection.Vertical };
    readonly ScrollPanelWidget m_tabsScroll = new() { Direction = LayoutDirection.Horizontal };
    readonly StackPanelWidget m_tabs = new() { Direction = LayoutDirection.Horizontal };
    readonly ButtonWidget m_cancel = ScGunUi.Button("关闭",110);
    readonly LabelWidget m_hint = ScGunUi.Note("单击预览 · 双击 / 双点进入");
    readonly List<(string Category, ButtonWidget Button)> m_categories = [];
    string m_category = "全部";
    object m_selected;
    object m_lastClicked, m_pendingChoice;
    double m_lastClickTime = double.NegativeInfinity;
    float m_lastClickScroll;
    bool m_done;
    readonly ButtonWidget m_quantity=ScGunUi.Button("数量 1",108),m_craft=ScGunUi.Button("制作 1 件",150);
    readonly List<(int Count,ButtonWidget Button)> m_quick=[];
    readonly List<(int Value,int Need,LabelWidget Label)> m_materialLabels=[];
    int m_count=1;
    double m_refreshAt,m_craftAfter;
    public Func<object,string> CraftPermission {get;set;}
    Func<object,Dictionary<int,int>> m_materialQuote;
    public void SetMaterialQuote(Func<object,Dictionary<int,int>> quote){m_materialQuote=quote;if(m_selected is not null)Select(m_selected);}
    bool Craftable => m_selected is ScComponentCrafting.Entry or ScWeaponCrafting.Entry or ScWorkbenchRecipe;
    int ResultCount() => !m_creative && m_selected is ScWorkbenchRecipe r ? Math.Max(1, r.ResultCount) : 1;
    Dictionary<int,int> UnitCost() => m_selected switch {ScWorkbenchRecipe r=>r.Materials(),ScComponentCrafting.Entry c=>c.Materials(),ScWeaponCrafting.Entry e=>e.Materials(),_=>[]};
    string CraftReason() => !m_creative && m_selected is ScWorkbenchRecipe {CreativeOnly:true} ? "仅创造模式领取，无生存制作配方。" : CraftPermission?.Invoke(m_selected) is {Length:>0} reason?reason:
        ScCraftBatch.UnavailableBatch(m_inventory,ValueOf(m_selected),m_creative?new Dictionary<int,int>():UnitCost(),m_count,ResultCount());
    void RefreshQuote() {
        m_quantity.Text=$"{(ResultCount()>1?"批数":"数量")} {m_count}";m_craft.Text=ResultCount()>1?$"制作 {m_count} 批":$"制作 {m_count} 件";
        m_quantity.IsVisible=m_craft.IsVisible=Craftable;
        foreach(var p in m_quick)p.Button.IsVisible=Craftable && ActualSize.X>=620;
        if(!Craftable)return;
        foreach(var p in m_materialLabels) {
            int owned=ScInventoryTransaction.Count(m_inventory,p.Value);
            p.Label.Text=$"{BlocksManager.Blocks[Terrain.ExtractContents(p.Value)].GetDisplayName(null,p.Value)}\n{p.Need} / {owned}";
            p.Label.Color=owned>=p.Need||m_creative?ScGunUi.Text:new Color(240,150,110);
        }
        string reason=CraftReason();m_craft.IsEnabled=reason.Length==0 && Time.RealTime>=m_craftAfter;
        if(reason.Length>0)m_hint.Text=reason;
    }
    public sealed record Navigation(string Category, object Selected, float Scroll);
    public Navigation CaptureNavigation() => new(m_category,m_selected,m_list.ScrollPosition);
    public void RestoreNavigation(Navigation state) {
        if(state is null)return;
        Filter(m_categories.Any(c=>c.Category==state.Category)?state.Category:"全部");
        object match=m_list.Items.FirstOrDefault(i=>Equals(i,state.Selected));
        if(match is not null)Select(match);
        m_list.ScrollPosition=Math.Max(0,state.Scroll);
    }
    public Action BackAction {get;set;}

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
            if(item is ScWorkbenchAppearance appearance)row.Children.Add(new RectangleWidget { Subtexture=Picture(appearance),Size=new Vector2(64,48),FillColor=Color.White,OutlineThickness=0,VerticalAlignment=WidgetAlignment.Center });
            if(item is int level) {
                var text=new StackPanelWidget{Direction=LayoutDirection.Vertical,VerticalAlignment=WidgetAlignment.Center};
                text.Children.Add(new LabelWidget{Text=$"Lv{level}",FontScale=.9f,Color=ScGunUi.Accent});
                string label=m_label(item);int start=label.IndexOf('（');
                text.Children.Add(new LabelWidget{Text=start>=0?label[start..]:label,FontScale=.58f,Color=ScGunUi.Dim});
                row.Children.Add(text);
            } else row.Children.Add(new LabelWidget { Text=m_label(item),FontScale=.66f,WordWrap=true,MaxLines=2,Ellipsis=true,VerticalAlignment=WidgetAlignment.Center });
            return row;
        };
        m_list.ItemClicked=item=>ClickItem(item, Time.RealTime);
        m_previewHost.Children.Add(ScGunUi.Frame()); m_previewHost.Children.Add(m_icon);m_previewHost.Children.Add(m_picture); Children.Add(m_previewHost);
        m_detailHost.Children.Add(ScGunUi.Frame()); m_detailScroll.Children.Add(m_details); m_detailHost.Children.Add(m_detailScroll); Children.Add(m_detailHost);
        m_name=ScGunUi.Label("",.9f,ScGunUi.Accent); m_name.WordWrap=true;
        m_hint.WordWrap=true; Children.Add(m_hint); Children.Add(m_cancel);
        Children.Add(m_quantity);Children.Add(m_craft);
        Children.Add(m_apply);
        foreach(int n in new[]{1,10,100}){var b=ScGunUi.Button(n.ToString(),52);m_quick.Add((n,b));Children.Add(b);}
        Filter("全部");
    }
    static Subtexture Picture(ScWorkbenchAppearance item)=>new(ContentManager.Get<Texture2D>(item.Preview),Vector2.Zero,Vector2.One);
    static string CategoryOf(object item) => item is ScWorkbenchAppearance ? "手套" : item is ScWorkbenchAction action ? action.Category : item is ScWorkbenchRecipe r ? r.Category : item is ScComponentCrafting.Entry ? "配件制作" : item is ScWeaponCrafting.Entry e ? e.Knife ? "刀具" : ScGunDurability.ClassOf(e.Name) switch {
        ScGunDurability.Class.Pistol=>"手枪",ScGunDurability.Class.Smg=>"冲锋枪",ScGunDurability.Class.Rifle=>"步枪",
        ScGunDurability.Class.Shotgun=>"霰弹枪",ScGunDurability.Class.BoltSniper or ScGunDurability.Class.AutoSniper=>"狙击枪",
        ScGunDurability.Class.MachineGun=>"机枪",_=>"电击枪"
    } : item is ScGunSkin or ScKnifeSkinning.Finish ? "涂装" : item is ScKnifeSkinning.Candidate ? "背包刀具" : item is ScWeaponRepair.Candidate or ScWeaponSkinning.Candidate or ScGunCounter.Candidate ? "背包枪械" : "功能";
    static int ValueOf(object item) => item switch {
        ScWorkbenchRecipe r=>r.Value,
        ScKnifeSkinning.Candidate c=>c.Value, ScKnifeSkinning.Finish f=>f.Value,
        ScComponentCrafting.Entry c=>c.Value, ScWeaponCrafting.Entry e=>e.Value, ScWeaponRepair.Candidate c=>c.Value,ScWeaponSkinning.Candidate c=>c.Value,ScGunCounter.Candidate c=>c.Value,
        ScGunSkin s=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunSkinTemplateBlock>(true),0,s.PaintId),_=>0
    };
    void Filter(string category) {
        m_lastClicked=null; m_pendingChoice=null; m_lastClickTime=double.NegativeInfinity;
        m_category=category; m_list.ClearItems();
        foreach(var item in m_items.Where(i=>category=="全部"||CategoryOf(i)==category)) m_list.AddItem(item);
        m_list.ScrollPosition=0; Select(m_list.Items.Count>0?m_list.Items[0]:null);
        foreach(var pair in m_categories) pair.Button.Color=pair.Category==category?ScGunUi.Accent:ScGunUi.Text;
    }
    void Select(object item) {
        if(!Equals(m_selected,item))m_count=1;
        m_selected=item; m_list.SelectedItem=item;
        m_materialLabels.Clear();
        m_hint.Text=Craftable?"选择数量后制作 · 材料自动堆叠":"单击预览 · 双击 / 双点进入";
        m_details.Children.Clear(); m_name.Text=item is null?"没有可用项目":m_label(item); m_details.Children.Add(m_name);
        int value=ValueOf(item); m_icon.IsVisible=value!=0; if(value!=0)m_icon.Value=value;
        m_picture.IsVisible=m_apply.IsVisible=item is ScWorkbenchAppearance;
        if(item is ScWorkbenchAppearance appearance){
            m_picture.Subtexture=Picture(appearance);m_details.Children.Add(ScGunUi.Note(appearance.Description));
            m_hint.Text="单击预览 · 点击应用手套，或双击 / 双点应用";
            m_detailScroll.ScrollPosition=0;RefreshQuote();return;
        }
        var materials = m_materialQuote?.Invoke(item) ?? (item switch {
            ScWorkbenchRecipe r=>r.Materials(),
            ScComponentCrafting.Entry c=>c.Materials(),
            ScWeaponCrafting.Entry e=>e.Materials(),
            ScGunSkin s=>ScGunSkinCatalog.CostOf(s,ScWeaponMaterialBlock.Value),
            ScKnifeSkinning.Finish f=>ScKnifeSkinning.Cost(f.Skin),
            _=>new Dictionary<int,int>()
        });
        if(Craftable)materials=ScCraftBatch.Cost(materials,m_count);
        if(item is ScWeaponCrafting.Entry entry) {
            m_details.Children.Add(ScGunUi.Note($"制作等级 {entry.Level} · 产出 {m_count} 件"));
            m_details.Children.Add(ScGunUi.Note(entry.Knife?"选择数量后点击制作。":"空枪交付，弹药另行制作。"));
        }
        else if(item is ScWorkbenchRecipe recipe)
            m_details.Children.Add(ScGunUi.Note(recipe.CreativeOnly?"仅创造模式领取 · 无生存配方":$"制作等级 {recipe.Level} · 每批 {recipe.ResultCount} 件 · 本次产出 {m_count * ResultCount()} 件"));
        else if(item is not ScComponentCrafting.Entry && value!=0 && EffectiveGunStats.TrySnapshotValue(value,out var s))
            m_details.Children.Add(ScGunUi.Note($"弹量 {s.Rounds} · {(s.CounterInstalled?$"计数 {s.KillCount} / Lv{s.Level}":"未安装计数器")}\n双击 / 双点此项查看本次操作的精确报价。"));
        else m_details.Children.Add(ScGunUi.Note(Craftable?$"产出 {m_count * ResultCount()} 件 · 点击下方制作":"双击 / 双点此项进入。单击仅预览，不会执行操作。"));
        if(m_creative)m_details.Children.Add(ScGunUi.Note("创造模式：不消耗材料，每件占一个快捷栏无限来源格。"));
        if(materials.Count>0) {
            m_details.Children.Add(ScGunUi.Heading("材料 · 需要 / 持有"));
            var materialScroll=new ScrollPanelWidget {Direction=LayoutDirection.Horizontal,DesiredSize=new Vector2(float.PositiveInfinity,118)};
            var columns=new StackPanelWidget {Direction=LayoutDirection.Horizontal};materialScroll.Children.Add(columns);m_details.Children.Add(materialScroll);
            StackPanelWidget column=null;int index=0;
            foreach(var p in materials) {
                int owned=ScInventoryTransaction.Count(m_inventory,p.Key);
                var row=new StackPanelWidget { Direction=LayoutDirection.Horizontal, Margin=new Vector2(0,3) };
                row.Children.Add(new BlockIconWidget { Value=p.Key,Size=new Vector2(36) });
                var label=new LabelWidget { Text=$"{BlocksManager.Blocks[Terrain.ExtractContents(p.Key)].GetDisplayName(null,p.Key)}\n{p.Value} / {owned}",FontScale=.65f,WordWrap=true,Size=new Vector2(135,48),
                    Color=owned>=p.Value||m_creative?ScGunUi.Text:new Color(240,150,110),VerticalAlignment=WidgetAlignment.Center };
                row.Children.Add(label);m_materialLabels.Add((p.Key,p.Value,label));
                if(index++%2==0){column=new StackPanelWidget{Direction=LayoutDirection.Vertical,Margin=new Vector2(6,0)};columns.Children.Add(column);}column.Children.Add(row);
            }
        }
        m_detailScroll.ScrollPosition=0;
        RefreshQuote();
    }
    void ClickItem(object item, double time) {
        if(m_done || m_pendingChoice is not null || item is null || !m_list.Items.Contains(item))return;
        bool activate=ReferenceEquals(item,m_lastClicked) && time>=m_lastClickTime && time-m_lastClickTime<=.45
            && Math.Abs(m_list.ScrollPosition-m_lastClickScroll)<2;
        Select(item);
        m_lastClicked=item; m_lastClickTime=time; m_lastClickScroll=m_list.ScrollPosition;
        if(activate && !Craftable)m_pendingChoice=item;
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
        m_hint.IsVisible=h>=360;
        float bodyH=Math.Max(20,h-(h<360?172:230));
        m_previewHost.IsVisible=h>=480;
        if(h<480){float lw=Math.Min(220,w*.42f);Place(m_listHost,12,110,lw,bodyH);Place(m_detailHost,20+lw,110,w-lw-32,bodyH);}
        else if(w>=900){Place(m_previewHost,12,110,210,bodyH);Place(m_listHost,230,110,280,bodyH);Place(m_detailHost,518,110,w-530,bodyH);}
        else if(w>=620){Place(m_listHost,12,110,210,bodyH);Place(m_previewHost,230,110,w-242,100);Place(m_detailHost,230,218,w-242,bodyH-108);}
        else{Place(m_listHost,12,110,Math.Max(130,w*.42f),bodyH);float x=20+Math.Max(130,w*.42f);Place(m_previewHost,x,110,w-x-12,80);Place(m_detailHost,x,198,w-x-12,bodyH-88);}
        m_icon.Size=new Vector2(w>=900?160:76);
        float pictureWidth=Math.Min(m_previewHost.Size.X-16,(m_previewHost.Size.Y-16)*4/3);
        m_picture.Size=new Vector2(Math.Max(1,pictureWidth),Math.Max(1,pictureWidth)*.75f);
        Place(m_hint,12,h-116,w-24,48);Place(m_cancel,12,h-56,80,48);
        Place(m_quantity,w-274,h-56,108,48);Place(m_craft,w-158,h-56,146,48);
        Place(m_apply,w-158,h-56,146,48);
        if(w<380){Place(m_cancel,12,h-56,64,48);Place(m_quantity,80,h-56,84,48);Place(m_craft,168,h-56,w-180,48);}
        int k=0;foreach(var p in m_quick){p.Button.IsVisible=Craftable&&w>=620;Place(p.Button,104+56*k++,h-56,52,48);}
        base.MeasureOverride(available);
    }
    public override void Update() {
        if(m_done)return;
        if(Time.RealTime>=m_refreshAt){m_refreshAt=Time.RealTime+.25;RefreshQuote();}
        if(Craftable) {
            foreach(var p in m_quick)if(p.Button.IsClicked){m_count=p.Count;Select(m_selected);return;}
            if(m_quantity.IsClicked){DialogsManager.ShowDialog(ParentWidget,new TextBoxDialog(ResultCount()>1?"制作批数（1～100）":"制作数量（1～100）",m_count.ToString(),3,text=>{
                if(m_done||text is null)return;
                if(int.TryParse(text,out int n)&&n>=1&&n<=ScCraftBatch.Maximum){m_count=n;Select(m_selected);}
                else m_hint.Text="请输入 1～100 的整数。";
            }));return;}
            if(m_craft.IsClicked && Time.RealTime>=m_craftAfter) {
                m_craftAfter=Time.RealTime+.35;
                string reason=CraftReason();
                bool made=reason.Length==0&&ScCraftBatch.TryCraftBatch(m_inventory,ValueOf(m_selected),m_creative?new Dictionary<int,int>():UnitCost(),m_count,ResultCount());
                RefreshQuote();m_hint.Text=made?$"已制作 {m_count * ResultCount()} 件":reason.Length>0?reason:"制作未完成；已尝试退款，未退回部分将自动重试。";
                return;
            }
        }
        foreach(var p in m_categories)if(p.Button.IsClicked){Filter(p.Category);return;}
        if(m_list.SelectedItem is {} selected && !ReferenceEquals(selected,m_selected))Select(selected);
        if(Input.Cancel||Input.Back||m_cancel.IsClicked){m_done=true;DialogsManager.HideDialog(this);BackAction?.Invoke();return;}
        if(m_apply.IsClicked&&m_selected is ScWorkbenchAppearance)m_pendingChoice=m_selected;
        // Defer navigation out of ListPanelWidget.ItemClicked, and dispatch at most once.
        if(m_pendingChoice is {} choice){m_done=true;DialogsManager.HideDialog(this);m_choose(choice);}
    }
}
