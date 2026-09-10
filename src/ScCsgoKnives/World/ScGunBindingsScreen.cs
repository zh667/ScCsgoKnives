using Engine;
using Engine.Input;
namespace Game;

/// <summary>Scrollable key picker also usable without a physical keyboard; save/cancel is transactional.</summary>
public sealed class ScGunBindingsScreen : Screen {
    public const string ScreenName = "ScCsgoGunBindings";
    readonly CanvasWidget m_root = new();
    readonly ScrollPanelWidget m_scroll = new() { Direction = LayoutDirection.Vertical };
    readonly StackPanelWidget m_list = new() { Direction = LayoutDirection.Vertical };
    readonly LabelWidget m_title = ScGunUi.Label("CS 武器 · 键盘绑定");
    readonly LabelWidget m_status = ScGunUi.Note("");
    readonly ButtonWidget m_save = ScGunUi.Button("保存", 120), m_cancel = ScGunUi.Button("取消", 120), m_reset = ScGunUi.Button("恢复默认", 140);
    readonly Dictionary<string, ButtonWidget> m_buttons = [];
    Dictionary<string, string> m_working;
    Screen m_back;
    public ScGunBindingsScreen() {
        Children.Add(new ScGunWorldBackground()); Children.Add(ScGunUi.Frame()); Children.Add(m_root);
        m_root.Children.Add(m_title); m_root.Children.Add(m_scroll); m_scroll.Children.Add(m_list);
        m_list.Children.Add(ScGunUi.Note("键名与玲兰触控 2.1 的按键配置页一致，选择同名键即可。手机、电脑都生效，不依赖触屏按钮总开关；原版鼠标和原版按键仍保留。"));
        m_list.Children.Add(ScGunUi.Note("列表同时显示原版已有操作和额外键盘键。点击只修改额外键盘键，不取消鼠标操作；原版操作以游戏当前绑定为准，默认左键开火／轻刀／强投，右键开镜／消音器／连发／速射／重刀／轻投。"));
        m_list.Children.Add(ScGunUi.Note("触屏点击场景不等于鼠标右键；手机可映射额外键盘键。选择原版已用键可能同时触发移动、背包等功能，请避开冲突。"));
        foreach (var id in ScGunFunctions.All) {
            var b = ScGunUi.Button("", 280); b.Margin = new Vector2(0, 4);
            ((BevelledButtonWidget)b).m_labelWidget.WordWrap=true;
            m_buttons[id] = b; m_list.Children.Add(b);
        }
        foreach (Widget w in new Widget[] { m_status, m_reset, m_cancel, m_save }) m_root.Children.Add(w);
    }
    public override void Enter(object[] parameters) {
        m_back = ScreensManager.PreviousScreen; m_working = new(ScGunBindings.Keys);
        foreach (var id in ScGunFunctions.All) m_working.TryAdd(id, ScGunBindings.Default(id));
        ScWeaponTouchPanel.SuppressAll(true); m_status.Text = ""; Refresh();
    }
    void Refresh() { foreach (var (id,b) in m_buttons) b.Text = ScGunBindings.BindingSummary(id,m_working[id]); }
    public override void MeasureOverride(Vector2 available) {
        float w = Math.Max(280, available.X), h = Math.Max(220, available.Y), bw = Math.Min(140, (w-32)/3);
        m_title.Size = new Vector2(w-24,36); m_root.SetWidgetPosition(m_title,new Vector2(12,8));
        m_scroll.DesiredSize = new Vector2(w-24, Math.Max(50,h-152)); m_root.SetWidgetPosition(m_scroll,new Vector2(12,50));
        foreach(var b in m_buttons.Values) { ((CanvasWidget)b).Size = new Vector2(Math.Min(540,w-48),64);((BevelledButtonWidget)b).FontScale=.85f; }
        m_status.Size = new Vector2(w-24,40); m_root.SetWidgetPosition(m_status,new Vector2(12,h-98));
        int i=0; foreach(var b in new[]{m_reset,m_cancel,m_save}) { ((CanvasWidget)b).Size=new Vector2(bw,48);m_root.SetWidgetPosition(b,new Vector2(8+i++*(bw+8),h-54)); }
        base.MeasureOverride(available);
    }
    public override void Update() {
        foreach (var (id,b) in m_buttons) if(b.IsClicked) {
            var options = new[]{""}.Concat(Enum.GetValues<Key>().Select(k=>k.ToString()).Where(k=>ScGunBindings.Valid(k))).ToArray();
            DialogsManager.ShowDialog(this,new ListSelectionDialog("额外键盘键："+ScGunBindings.Label(id),options,52,
                x=>(string)x==""?"移除额外键盘键（保留原版操作）":ScGunBindings.KeyLabel((string)x),x=> {
                    string key=(string)x;
                    var conflict=m_working.FirstOrDefault(p=>p.Key!=id && key.Length>0 && p.Value==key && ScGunBindings.Conflict(id,p.Key));
                    if(conflict.Key!=null) {m_status.Text="未修改：此键已绑定「"+ScGunBindings.Label(conflict.Key)+"」。";return;}
                    m_working[id]=key;m_status.Text="尚未保存";Refresh();
                })); return;
        }
        if(m_reset.IsClicked){foreach(var id in ScGunFunctions.All)m_working[id]=ScGunBindings.Default(id);Refresh();m_status.Text="尚未保存";}
        if(m_cancel.IsClicked || Input.Back || Input.Cancel){ScreensManager.SwitchScreen(m_back);return;}
        if(m_save.IsClicked) {
            var before=new Dictionary<string,string>(ScGunBindings.Keys);
            ScGunBindings.Keys.Clear();foreach(var p in m_working)ScGunBindings.Keys[p.Key]=p.Value;
            if(ScUiSettings.Save()){ScreensManager.SwitchScreen(m_back);return;}
            ScGunBindings.Keys.Clear();foreach(var p in before)ScGunBindings.Keys[p.Key]=p.Value;
            m_status.Text="保存失败，原配置保留。";
        }
    }
}
