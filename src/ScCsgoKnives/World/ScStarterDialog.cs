using Engine;
namespace Game;

public sealed class ScStarterDialog : Dialog {
    readonly ButtonWidget choice,confirm;
    readonly Func<ScStarterPlan,bool> submit;
    ScStarterPlan plan;
    public ScStarterDialog(ScStarterPlan initial,Func<ScStarterPlan,bool> submit){
        plan=Enum.IsDefined(initial)?initial:ScStarterPlan.None;this.submit=submit;
        Children.Add(ScGunUi.Frame());
        var panel=new StackPanelWidget{Direction=LayoutDirection.Vertical,Margin=new Vector2(16),HorizontalAlignment=WidgetAlignment.Center};
        panel.Children.Add(ScGunUi.Heading("开局装备"));
        choice=ScGunUi.Button(ScStarterLoadout.Label(plan),330);panel.Children.Add(choice);
        confirm=ScGunUi.Button("确定",150);confirm.HorizontalAlignment=WidgetAlignment.Center;panel.Children.Add(confirm);
        Children.Add(panel);
    }
    public override void Update(){
        if(choice.IsClicked){plan=(ScStarterPlan)(((int)plan+1)%4);choice.Text=ScStarterLoadout.Label(plan);}
        if(confirm.IsClicked&&submit(plan))DialogsManager.HideDialog(this);
    }
}
