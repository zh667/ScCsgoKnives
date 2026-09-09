using Engine;
namespace Game;

/// <summary>Scrollable quote with always-visible confirmation. All commit validation stays at the caller.</summary>
public sealed class ScWorkbenchConfirmDialog : Dialog {
    readonly LabelWidget m_title, m_text;
    readonly ScrollPanelWidget m_scroll=new(){Direction=LayoutDirection.Vertical};
    readonly ButtonWidget m_yes,m_no;
    readonly Action<MessageDialogButton> m_handler;
    bool m_done;
    public ScWorkbenchConfirmDialog(string title,string detail,string yes,string no,Action<MessageDialogButton> handler) {
        m_handler=handler; HorizontalAlignment=VerticalAlignment=WidgetAlignment.Center;
        Children.Add(ScGunUi.Frame());
        m_title=ScGunUi.Label(title,.95f);m_title.WordWrap=true;m_title.MaxLines=2;m_title.Ellipsis=true;Children.Add(m_title);
        m_text=ScGunUi.Note(detail);m_text.FontScale=.8f;m_text.Margin=new Vector2(8,4);m_scroll.Children.Add(m_text);Children.Add(m_scroll);
        m_yes=ScGunUi.Button(yes,180);m_no=ScGunUi.Button(no,160);Children.Add(m_yes);Children.Add(m_no);
        m_no.IsVisible=!string.IsNullOrEmpty(no);
    }
    public override void MeasureOverride(Vector2 available) {
        float w=Math.Min(650,Math.Max(280,available.X-24)),h=Math.Min(570,Math.Max(220,available.Y-24));Size=new(w,h);
        SetWidgetPosition(m_title,new(12,10));m_title.Size=new(w-24,60);
        SetWidgetPosition(m_scroll,new(12,78));m_scroll.DesiredSize=new(w-24,h-150);
        float bw=Math.Min(180,(w-36)/2);m_yes.Size=new(bw,48);m_no.Size=new(bw,48);
        SetWidgetPosition(m_yes,new(m_no.IsVisible?w-12-bw:(w-bw)/2,h-60));SetWidgetPosition(m_no,new(12,h-60));
        base.MeasureOverride(available);
    }
    public override void Update() {
        if(m_done)return;
        if(Input.Cancel||Input.Back||m_no.IsClicked){Dismiss(MessageDialogButton.Button2);return;}
        if(m_yes.IsClicked)Dismiss(MessageDialogButton.Button1);
    }
    void Dismiss(MessageDialogButton result){m_done=true;DialogsManager.HideDialog(this);m_handler(result);}
}
