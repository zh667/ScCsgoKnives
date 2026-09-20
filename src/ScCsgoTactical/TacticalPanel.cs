using Engine;
namespace Game;

public sealed class TacticalPanel : CanvasWidget {
    readonly ComponentPlayer player;
    readonly ComponentTacticalCompanion companion;
    public ComponentTacticalCompanion Companion=>companion;
    readonly BevelledButtonWidget follow,guard,cover,cease,dismiss,close,inspect;
    readonly LabelWidget status;
    readonly GridPanelWidget inventoryGrid,commandGrid;
    readonly int playerSlots;
    readonly BevelledButtonWidget[] commandButtons;
    public TacticalPanel(ComponentPlayer p,ComponentTacticalCompanion c){
        player=p;companion=c;c.PanelOpen=true;Size=new Vector2(620,520);HorizontalAlignment=WidgetAlignment.Center;VerticalAlignment=WidgetAlignment.Center;
        Children.Add(ScGunUi.Frame());var scroll=new TacticalInventoryScroll{Direction=LayoutDirection.Vertical,ScrollPosition=0,ScrollSpeed=0};Children.Add(scroll);
        var body=new StackPanelWidget{Direction=LayoutDirection.Vertical,Margin=new Vector2(12,8)};scroll.Children.Add(body);
        body.Children.Add(ScGunUi.Heading(c.Creature.DisplayName));close=ScGunUi.Button("关闭",160);body.Children.Add(close);status=ScGunUi.Label("",.8f);body.Children.Add(status);
        body.Children.Add(ScGunUi.Note("左格：枪械或盾牌；右侧四格：通用弹匣／霰弹。拖动装备，取回时状态保留。"));
        body.Children.Add(Grid(c.Inventory,0,5,5));
        commandGrid=new GridPanelWidget{ColumnsCount=4,RowsCount=1,HorizontalAlignment=WidgetAlignment.Center};body.Children.Add(commandGrid);
        follow=ScGunUi.Button("跟随",110);guard=ScGunUi.Button("守在这里",110);cover=ScGunUi.Button("前方掩护",110);cease=ScGunUi.Button("停火",110);
        commandButtons=[follow,guard,cover,cease];for(int i=0;i<4;i++){commandGrid.Children.Add(commandButtons[i]);commandGrid.SetWidgetCell(commandButtons[i],new Point2(i,0));}
        body.Children.Add(ScGunUi.Note("主动攻击附近敌对生物，并协助攻击主人击中的目标。空手近战；持枪需提供弹药。举盾时不开火，脚部和背后不受保护。"));
        inspect=ScGunUi.Button("检视武器",240);body.Children.Add(inspect);
        body.Children.Add(ScGunUi.Heading("我的物品（含全部快捷栏）"));
        var inventory=p.ComponentMiner.Inventory;int count=inventory is ComponentCreativeInventory?10:inventory.SlotsCount;
        playerSlots=count;inventoryGrid=Grid(inventory,0,count,8);body.Children.Add(inventoryGrid);
        dismiss=ScGunUi.Button("收空装备后解散",240);body.Children.Add(dismiss);
    }
    public override void MeasureOverride(Vector2 available){
        float width=Math.Max(340,Math.Min(620,available.X));Size=new Vector2(width,Math.Min(520,available.Y));
        int columns=Math.Clamp((int)((width-24)/64),5,8);inventoryGrid.ColumnsCount=columns;inventoryGrid.RowsCount=(playerSlots+columns-1)/columns;
        for(int i=0;i<playerSlots;i++)inventoryGrid.SetWidgetCell(inventoryGrid.Children[i],new Point2(i%columns,i/columns));
        int buttons=width<480?2:4;commandGrid.ColumnsCount=buttons;commandGrid.RowsCount=4/buttons;
        for(int i=0;i<4;i++)commandGrid.SetWidgetCell(commandButtons[i],new Point2(i%buttons,i/buttons));
        base.MeasureOverride(available);
    }
    GridPanelWidget Grid(IInventory inv,int first,int count,int columns){var g=new GridPanelWidget{ColumnsCount=columns,RowsCount=(count+columns-1)/columns,HorizontalAlignment=WidgetAlignment.Center};for(int i=0;i<count;i++){var slot=new TacticalInventorySlot(player){Size=new Vector2(64,64)};slot.AssignInventorySlot(inv,first+i);g.Children.Add(slot);g.SetWidgetCell(slot,new Point2(i%columns,i/columns));}return g;}
    public override void Update(){
        if(!companion.IsAddedToProject||companion.DeathHandled||!companion.OwnedBy(player)||player.ComponentHealth.Health<=0||Vector3.DistanceSquared(player.ComponentBody.Position,companion.Creature.ComponentBody.Position)>36||close.IsClicked){Exit();return;}
        companion.PanelOpen=true;status.Text=$"生命 {companion.Creature.ComponentHealth.Health*100:0}%   {companion.Order switch {TacticalOrder.Guard=>"守在这里",TacticalOrder.Cover=>"前方掩护",_=>"跟随"}}   {(companion.CeaseFire?"停火":"主动攻击")}";
        if(follow.IsClicked)companion.Command(TacticalOrder.Follow);if(guard.IsClicked)companion.Command(TacticalOrder.Guard);if(cover.IsClicked)companion.Command(TacticalOrder.Cover);
        if(cease.IsClicked)companion.CeaseFire=!companion.CeaseFire;cease.Text=companion.CeaseFire?"允许攻击":"停火";
        if(inspect.IsClicked&&companion.InspectWeapon()){Exit();return;}
        dismiss.IsEnabled=Enumerable.Range(0,companion.Inventory.SlotsCount).All(i=>companion.Inventory.GetSlotCount(i)==0);
        if(dismiss.IsClicked&&dismiss.IsEnabled){Exit();companion.Project.RemoveEntity(companion.Entity,true);}
    }
    void Exit(){companion.PanelOpen=false;if(player.ComponentGui.ModalPanelWidget==this)player.ComponentGui.ModalPanelWidget=null;}
}

// A gesture starting on an item belongs to native inventory drag/drop, not to scrolling.
// Keep the decision until release, even after the pointer leaves the starting slot.
public sealed class TacticalInventoryScroll : ScrollPanelWidget {
    bool itemGesture;
    public override void Update(){
        if(Input.Tap is Vector2 tap)itemGesture=HitTestGlobal(tap,w=>w is InventorySlotWidget) is InventorySlotWidget;
        if(Input.Drag is Vector2 drag&&HitTestGlobal(drag,w=>w is InventorySlotWidget) is InventorySlotWidget slot&&slot.DragHostWidget?.IsDragInProgress==true)itemGesture=true;
        if(itemGesture){m_lastDragPosition=null;ScrollSpeed=0;if(!Input.Press.HasValue&&!Input.Drag.HasValue)itemGesture=false;return;}
        base.Update();
    }
}
public sealed class TacticalInventorySlot : InventorySlotWidget {
    readonly ComponentPlayer viewer;
    public TacticalInventorySlot(ComponentPlayer player){viewer=player;foreach(var child in AllChildren)child.IsHitTestVisible=false;}
    public override GameWidget GameWidget=>viewer.GameWidget;
    public override ComponentPlayer GetViewPlayer()=>viewer;
}
