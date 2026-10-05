using Engine;
using Engine.Graphics;
namespace Game;

public sealed class ScStarterDialog : Dialog {
    readonly List<(ScStarterPlan Plan,BevelledButtonWidget Button)> choices=[];
    readonly Func<ScStarterPlan,bool> submit;
    readonly StackPanelWidget panel=new(){Direction=LayoutDirection.Vertical,Margin=new Vector2(12),HorizontalAlignment=WidgetAlignment.Stretch};
    readonly ScrollPanelWidget scroll=new(){Direction=LayoutDirection.Vertical,HorizontalAlignment=WidgetAlignment.Stretch,VerticalAlignment=WidgetAlignment.Stretch};
    bool submitted;
    public ScStarterDialog(ScStarterPlan initial,Func<ScStarterPlan,bool> submit){
        HorizontalAlignment=WidgetAlignment.Center;VerticalAlignment=WidgetAlignment.Center;this.submit=submit;
        Children.Add(ScGunUi.Frame());
        panel.Children.Add(ScGunUi.Heading("开局装备"));
        foreach(var plan in Enum.GetValues<ScStarterPlan>()){
            var button=ScGunUi.Button(ScStarterLoadout.Label(plan),430,64);
            button.HorizontalAlignment=WidgetAlignment.Stretch;button.Margin=new Vector2(0,3);
            button.m_labelWidget.WordWrap=true;button.m_labelWidget.TextAnchor=TextAnchor.HorizontalCenter|TextAnchor.VerticalCenter;
            if(plan==initial)button.Color=ScGunUi.Accent;
            choices.Add((plan,button));panel.Children.Add(button);
        }
        panel.Children.Add(ScGunUi.Note("装备方案均附送 5 个通用弹匣"));
        scroll.Children.Add(panel);Children.Add(scroll);
    }
    public override void MeasureOverride(Vector2 available){
        Size=new Vector2(Math.Max(0,Math.Min(470,available.X-16)),Math.Max(0,Math.Min(390,available.Y-16)));
        foreach(var (_,button) in choices){button.Size=new Vector2(Math.Max(0,Size.X-24),available.Y<380?52:64);button.FontScale=Size.X<340?.72f:.82f;}
        base.MeasureOverride(available);
    }
    public override void Update(){
        if(submitted)return;
        foreach(var (plan,button) in choices)if(button.IsClicked){
            submitted=submit(plan);
            if(submitted)DialogsManager.HideDialog(this);
            return;
        }
    }
}
