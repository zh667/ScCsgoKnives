using Engine;
namespace Game;

/// <summary>Owned-item inspection stays inside the workbench dialog stack. No recipe-screen history loop.</summary>
public sealed class ScOwnedGunAttributesDialog : Dialog {
    readonly IInventory m_inventory;
    readonly ScGunRegistry m_registry;
    readonly Func<bool> m_available;
    readonly Action m_back;
    readonly ListPanelWidget m_list=new() { ItemSize=64, Direction=LayoutDirection.Vertical };
    readonly CanvasWidget m_listHost=new(), m_detailHost=new();
    readonly ScrollPanelWidget m_scroll=new() { Direction=LayoutDirection.Vertical };
    readonly StackPanelWidget m_content=new() { Direction=LayoutDirection.Vertical, Margin=new Vector2(10,6) };
    readonly LabelWidget m_title=ScGunUi.Label("当前武器属性",1f,ScGunUi.Accent);
    readonly LabelWidget m_hint=ScGunUi.Note("选择背包／快捷栏中的一把枪 · 显示当前实际数值，不是等级预览");
    readonly ButtonWidget m_backButton=ScGunUi.Button("返回枪械台",160);
    ScOwnedGunAttributes.Candidate[] m_candidates=[];
    ScOwnedGunAttributes.Candidate m_selected;
    EffectiveGunStats m_lastStats;
    bool m_wide,m_built,m_closed;
    public ScOwnedGunAttributesDialog(IInventory inventory, Func<bool> available, Action back) {
        m_inventory=inventory;m_registry=ScGunRegistry.Current;m_available=available;m_back=back;
        HorizontalAlignment=VerticalAlignment=WidgetAlignment.Center;
        Children.Add(ScGunUi.Frame());Children.Add(m_title);Children.Add(m_hint);
        m_listHost.Children.Add(ScGunUi.Frame());m_listHost.Children.Add(m_list);Children.Add(m_listHost);
        m_detailHost.Children.Add(ScGunUi.Frame());m_scroll.Children.Add(m_content);m_detailHost.Children.Add(m_scroll);Children.Add(m_detailHost);
        Children.Add(m_backButton);
        m_list.ItemWidgetFactory=item=> {
            var gun=(ScOwnedGunAttributes.Candidate)item;
            var row=new StackPanelWidget { Direction=LayoutDirection.Horizontal, Margin=new Vector2(4,2) };
            row.Children.Add(new BlockIconWidget { Value=gun.Value,Size=new Vector2(42) });
            row.Children.Add(new LabelWidget { Text=gun.Name,FontScale=.65f,WordWrap=true,MaxLines=3,Ellipsis=true });
            return row;
        };
        m_list.ItemClicked=item=>Select((ScOwnedGunAttributes.Candidate)item);
        ReloadList(true);
    }
    void ReloadList(bool initial) {
        m_candidates=ScOwnedGunAttributes.Candidates(m_inventory);
        float scroll=m_list.ScrollPosition;m_list.ClearItems();foreach(var gun in m_candidates)m_list.AddItem(gun);
        m_list.ScrollPosition=scroll;
        if(initial)Select(m_candidates.FirstOrDefault(g=>g.Slot==m_inventory?.ActiveSlotIndex)??m_candidates.FirstOrDefault());
    }
    void Select(ScOwnedGunAttributes.Candidate gun) {
        m_selected=gun;m_list.SelectedItem=gun;m_scroll.ScrollPosition=0;Refresh();
    }
    void Refresh() {
        m_content.Children.Clear();m_built=true;
        if(!ScOwnedGunAttributes.TryResolve(m_inventory,m_selected,m_registry,out var gun,out var stats)) {
            m_content.Children.Add(ScGunUi.Heading(m_candidates.Length==0 ? "背包中没有可查看的枪械" : "请选择一把现有枪械"));
            m_content.Children.Add(ScGunUi.Note("只读取背包与快捷栏中的 CS 枪械；箱子里的枪请先取出。物品移走、替换或记录不可读时不会改用图鉴模板冒充当前武器。"));
            return;
        }
        m_selected=gun;m_lastStats=stats;
        var identity=new StackPanelWidget { Direction=LayoutDirection.Horizontal };
        identity.Children.Add(new BlockIconWidget { Value=gun.Value,Size=new Vector2(76) });
        var labels=new StackPanelWidget { Direction=LayoutDirection.Vertical };
        var name=ScGunUi.Label(ScGunNames.Item(gun.Snapshot),.9f,ScGunUi.Accent);name.WordWrap=true;labels.Children.Add(name);
        labels.Children.Add(ScGunUi.Note($"第 {gun.Slot+1} 格 · 当前已生效 Lv{stats.Level}"));
        labels.Children.Add(ScGunUi.Note(gun.Snapshot.CounterInstalled?$"有效击杀 {gun.Snapshot.KillCount} 次":"未安装计数器，未解锁等级成长；安装前的击杀不补计。"));
        identity.Children.Add(labels);m_content.Children.Add(identity);
        var spec=GunSpec.All[gun.Snapshot.Variant];
        m_content.Children.Add(ScGunUi.Note("当前实际属性 · 静止、不开镜、首发基准；伤害按近距离计算。"+
            (spec.HasSilencer?(gun.Snapshot.SilencerOff?" 消音器已拆。":" 消音器在位。"):"")));
        if(gun.Snapshot.PendingGrowthLevel>gun.Snapshot.AppliedGrowthLevel)
            m_content.Children.Add(ScGunUi.Note($"待应用 Lv{gun.Snapshot.PendingGrowthLevel}：本页仍显示当前已生效等级，不提前增加数值。"));
        var rows=ScGunAttributes.RowsFromEffective(spec,stats);
        for(int i=0;i<rows.Count;i+=m_wide?2:1) {
            var pair=new StackPanelWidget { Direction=LayoutDirection.Horizontal };
            for(int j=i;j<Math.Min(rows.Count,i+(m_wide?2:1));j++) {
                var cell=new CanvasWidget { Size=new Vector2(float.PositiveInfinity,-1),Margin=new Vector2(3) };
                cell.Children.Add(StatRow(rows[j]));pair.Children.Add(cell);
            }
            m_content.Children.Add(pair);
        }
    }
    static Widget StatRow(ScGunAttributes.Row row) {
        var stack=new StackPanelWidget { Direction=LayoutDirection.Vertical, Margin=new Vector2(3,5) };
        var label=ScGunUi.Label(row.Label,.72f);label.WordWrap=true;stack.Children.Add(label);
        var number=ScGunUi.Label(row.Text+" "+row.Unit,.84f,ScGunUi.Accent);number.WordWrap=true;stack.Children.Add(number);
        var bar=new ValueBarWidget { BarsCount=10,Value=row.Fraction,BarSize=new Vector2(11,8),Spacing=2,LayoutDirection=LayoutDirection.Horizontal,LitBarColor=ScGunUi.Accent };
        stack.Children.Add(bar);
        if(!string.IsNullOrEmpty(row.Detail))stack.Children.Add(ScGunUi.Note(row.Detail));
        return stack;
    }
    void Place(Widget w,float x,float y,float width,float height) {
        SetWidgetPosition(w,new Vector2(x,y));
        if(w is CanvasWidget c)c.Size=new Vector2(width,height);
        else if(w is LabelWidget l)l.Size=new Vector2(width,height);
        else w.DesiredSize=new Vector2(width,height);
    }
    public override void MeasureOverride(Vector2 available) {
        float w=Math.Max(260,Math.Min(1120,available.X-24)),h=Math.Max(240,Math.Min(720,available.Y-24));
        Size=new Vector2(w,h);bool wide=w>=840;
        if(!m_built||wide!=m_wide){m_wide=wide;Refresh();}
        Place(m_title,12,8,w-24,32);Place(m_hint,12,42,w-24,44);Place(m_backButton,12,h-58,160,48);
        if(w>=620) { float left=Math.Min(260,w*.32f);Place(m_listHost,12,92,left,h-162);Place(m_detailHost,left+22,92,w-left-34,h-162); }
        else { float listHeight=Math.Min(130,(h-172)*.3f);Place(m_listHost,12,92,w-24,listHeight);Place(m_detailHost,12,102+listHeight,w-24,h-172-listHeight); }
        base.MeasureOverride(available);
    }
    public override void Update() {
        if(m_closed)return;
        if(Input.Back||Input.Cancel||m_backButton.IsClicked||m_available?.Invoke()==false||!ReferenceEquals(m_registry,ScGunRegistry.Current)) {
            m_closed=true;DialogsManager.HideDialog(this);m_back?.Invoke();return;
        }
        var now=ScOwnedGunAttributes.Candidates(m_inventory);
        if(!now.SequenceEqual(m_candidates)) {
            ReloadList(false);
            if(!ScOwnedGunAttributes.TryResolve(m_inventory,m_selected,m_registry,out _,out _))m_selected=null;
            Refresh();
        }
        else if(ScOwnedGunAttributes.TryResolve(m_inventory,m_selected,m_registry,out _,out var stats)&&stats!=m_lastStats)Refresh();
    }
}
