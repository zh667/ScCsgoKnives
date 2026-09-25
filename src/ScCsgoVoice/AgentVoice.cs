using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

public sealed class AgentVoiceOptions {
    public int Version {get;set;}=1;
    public string Language {get;set;}="zh";
    public float Volume {get;set;}=.7f;
    public bool PlayerEnabled {get;set;}=true;
    public bool NpcEnabled {get;set;}=true;
    public bool Captions {get;set;}=true;
    public List<string> Favorites {get;set;}=[];
    [JsonExtensionData] public Dictionary<string,JsonElement> Unknown {get;set;}
    public static AgentVoiceOptions Current=new();
    public static int Revision;
    static bool writable=true;
    static string Path=>ScLocalSettings.PathFor("ScCsgoAgentVoice.json");
    public static void Load(){try{if(!Storage.FileExists(Path))return;using var s=Storage.OpenFile(Path,OpenFileMode.Read);var read=JsonSerializer.Deserialize<AgentVoiceOptions>(s);if(read.Version!=1||read.Language is not("zh" or "en")||!float.IsFinite(read.Volume))throw new InvalidDataException();read.Volume=Math.Clamp(read.Volume,0,1);read.Favorites??=[];Current=read;}catch{writable=false;}}
    public static bool Change(Action<AgentVoiceOptions> change){
        if(!writable)return false;var next=JsonSerializer.Deserialize<AgentVoiceOptions>(JsonSerializer.Serialize(Current));change(next);
        try{string path=Storage.GetSystemPath(Path),temp=path+".pending";var bytes=JsonSerializer.SerializeToUtf8Bytes(next,new JsonSerializerOptions{WriteIndented=true});
            using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}if(!File.ReadAllBytes(temp).SequenceEqual(bytes))throw new IOException("settings verification failed");File.Move(temp,path,true);Current=next;Revision++;return true;}catch{return false;}
    }
}
public sealed class AgentVoiceClip {
    public string Id {get;set;} public string Role {get;set;} public string Language {get;set;}
    public string Event {get;set;} public string Category {get;set;} public string Label {get;set;}
    public string Resource {get;set;} public float Duration {get;set;}
}
public sealed class AgentVoiceModLoader:ModLoader {
    public static bool Supported;
    public static AgentVoiceClip[] Clips=[];
    public override void __ModInitialize(){
        ModsManager.RegisterHook("OnLoadingFinished",this);
        Entity.GetFile("Assets/ScAgentVoices.json",s=>Clips=JsonSerializer.Deserialize<AgentVoiceClip[]>(s));
    }
    public override void OnLoadingFinished(List<Action> actions)=>actions.Add(()=>{
        var core=ModsManager.Dlls.Values.FirstOrDefault(a=>a.GetName().Name=="ScCsgoKnives");
        Supported=core?.GetType("Game.ScAgentVoice")?.GetField("ApiVersion")?.GetRawConstantValue() is int version&&version==1;
        if(Supported)Connect();else Log.Information("[CS Voice] 当前主包没有语音接口，语音附属已保持停用，不修改世界。");
    });
    [MethodImpl(MethodImplOptions.NoInlining)] static void Connect(){
        AgentVoiceOptions.Load();ScAgentVoice.OpenSettings=AgentVoiceMenus.Settings;
        // The in-world voice key is handled by the subsystem HUD. Keeping the callback
        // here avoids opening a modal list dialog from a gameplay input edge.
        ScAgentVoice.OpenMenu=p=>p?.Project?.FindSubsystem<SubsystemScAgentVoice>(false)?.OpenMenu(p);
        ScAgentVoice.FilterInput=input=>input?.m_componentPlayer?.Project?.FindSubsystem<SubsystemScAgentVoice>(false)?.FilterInput(input);
        ScWorkbenchExtension.RegisterAction(new("agent-voice","探员语音设置","功能",(p,back)=>AgentVoiceMenus.Settings(p.GuiWidget)));
    }
}
sealed class VoiceHud : IDisposable {
    public readonly CanvasWidget Root = new() {
        Size = new Vector2(440, 116), HorizontalAlignment = WidgetAlignment.Near,
        VerticalAlignment = WidgetAlignment.Far, MarginLeft = 12, MarginBottom = 126,
        IsVisible = false, IsHitTestVisible = false
    };
    readonly LabelWidget title = ScGunUi.Label("语音菜单", .68f, ScGunUi.Accent);
    readonly LabelWidget hint = ScGunUi.Label("按 1–5 选择；再次按 Z 关闭", .52f, ScGunUi.Dim);
    readonly BevelledButtonWidget[] options = Enumerable.Range(1, 5).Select(i => ScGunUi.Button(i.ToString(), 78, 44)).ToArray();
    readonly CanvasWidget content = new() { Size = new Vector2(420, 104), MarginLeft = 10, MarginTop = 6 };
    readonly StackPanelWidget row = new() { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Near };
    public bool Visible => Root.IsVisible;
    public VoiceHud() {
        var frame = ScGunUi.Frame(); frame.IsHitTestVisible = false; Root.Children.Add(frame);
        foreach(var option in options){option.FontScale=.56f;option.Margin=new Vector2(2,0);}
        var stack = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
        stack.Children.Add(title); stack.Children.Add(hint); row.Children.Add(options[0]); row.Children.Add(options[1]);
        row.Children.Add(options[2]); row.Children.Add(options[3]); row.Children.Add(options[4]); stack.Children.Add(row);
        content.Children.Add(stack); Root.Children.Add(content);
    }
    public void Attach(ContainerWidget host) { if (!ReferenceEquals(Root.ParentWidget, host)) { Root.ParentWidget?.Children.Remove(Root); host?.Children.Add(Root); } }
    public void Show(string role, string language, IReadOnlyList<string> labels) {
        title.Text = (role == "ct" ? "CT · SAS" : "T · Phoenix") + " · " + (language == "zh" ? "中文" : "English");
        hint.Text = "按数字键或点击；每项从对应语音变体中随机选择";
        for (int i = 0; i < options.Length; i++) options[i].Text = $"{i + 1}  {labels[i]}";
        Root.IsHitTestVisible = true; Root.IsVisible = true;
    }
    public int Clicked() { for (int i = 0; i < options.Length; i++) if (options[i].IsClicked) return i + 1; return 0; }
    public void Hide() { Root.IsVisible = false; Root.IsHitTestVisible = false; }
    public void Dispose() { Root.ParentWidget?.Children.Remove(Root); }
}
/// <summary>One scheduler per world; no voice queue or random clocks are written to saves.</summary>
public sealed class SubsystemScAgentVoice:Subsystem,IUpdateable {
    sealed class Speaker {public double Next,Ambient;public float Health;public readonly Queue<string> Recent=[];public readonly Dictionary<string,double> Last=[];}
    sealed record Pending(Entity Entity,string Role,string Action,double At,double Expires,AgentVoiceClip Exact=null,bool Manual=false);
    readonly Dictionary<Entity,Speaker> speakers=new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<ComponentPlayer,VoiceHud> huds=[];
    static readonly (string Label, string[] Events)[] Commands = [
        ("跟我来", ["radio.followme"]),
        ("发现敌人", ["radio.enemyspotted"]),
        ("需要支援", ["radio.needbackup"]),
        ("投掷物", ["grenade", "flashbang", "smoke", "molotov", "decoy"]),
        ("回应", ["affirmative", "negative", "thanks", "inposition", "waitinghere"])
    ];
    readonly List<Pending> pending=[];
    readonly List<(Vector3 Position,double Until,bool Manual)> playing=[];
    readonly Engine.Random random=new();
    SubsystemTime time;SubsystemPlayers players;SubsystemAudio audio;
    double nextScan;int revision;bool enabled;
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    public override void Load(ValuesDictionary values){time=Project.FindSubsystem<SubsystemTime>(true);players=Project.FindSubsystem<SubsystemPlayers>(true);audio=Project.FindSubsystem<SubsystemAudio>(true);enabled=AgentVoiceModLoader.Supported;if(enabled)Subscribe();revision=AgentVoiceOptions.Revision;}
    [MethodImpl(MethodImplOptions.NoInlining)] void Subscribe()=>ScAgentVoice.Event+=Receive;
    Speaker State(Entity entity){if(!speakers.TryGetValue(entity,out var s))speakers[entity]=s=new(){Health=entity.FindComponent<ComponentHealth>()?.Health??1,Ambient=time.GameTime+random.Float(18,32)};return s;}
    static string Role(Entity e)=>e.ValuesDictionary?.DatabaseObject?.Name switch {"ScTacticalCT"=>"ct","ScTacticalT" or "ScTacticalEnemy"=>"t",_=>null};
    static bool Alive(Entity e)=>e.IsAddedToProject&&e.FindComponent<ComponentHealth>() is {Health:>0};
    static string Map(string action)=>action switch {"spawn"=>"radio.letsgo","spotted"=>"radio.enemyspotted","hurt"=>"radio.takingfire","kill"=>"enemydown","follow"=>"followingfriend","wait"=>"waitinghere","idle"=>"inposition","grenade_hegrenade"=>"grenade","grenade_flashbang"=>"flashbang","grenade_smokegrenade"=>"smoke","grenade_decoy"=>"decoy","grenade_molotov" or "grenade_incendiary"=>"molotov",_=>action};
    public static float Probability(string action)=>action switch{"spawn"=>.95f,"spotted"=>.9f,"hurt"=>.45f,"kill"=>.8f,"follow" or "wait"=>.95f,"idle"=>.55f,_=>.85f};
    void Receive(Entity entity,string role,string action){
        if(entity.Project!=Project||!AgentVoiceOptions.Current.NpcEnabled||role is not("ct" or "t"))return;
        if(pending.Count>=24||random.Float(0,1)>Probability(action))return;
        var s=State(entity);double now=time.GameTime;
        if(now<s.Next||pending.Any(p=>ReferenceEquals(p.Entity,entity)))return;
        string key=Map(action);if(s.Last.TryGetValue("event:"+key,out var prior)&&now-prior<(action=="spotted"?6:3))return;
        s.Last["event:"+key]=now;
        double delay=action=="spawn"?random.Float(.5f,1.5f):0;
        pending.Add(new(entity,role,key,now+delay,now+delay+2));
    }
    public bool Manual(ComponentPlayer player,AgentVoiceClip clip,out string reason){
        reason="";if(!enabled||!AgentVoiceOptions.Current.PlayerEnabled){reason="玩家语音已关闭";return false;}
        if(!Alive(player.Entity)||ScAgentVoice.PlayerRole(player)!=clip.Role){reason="请先选择对应的CT/T角色";return false;}
        var s=State(player.Entity);double now=time.GameTime;
        if(now<s.Next){reason="语音冷却中";return false;}
        if(s.Last.TryGetValue(clip.Id,out var last)&&now-last<5){reason="这句刚说过，请换一句";return false;}
        pending.RemoveAll(p=>ReferenceEquals(p.Entity,player.Entity));pending.Add(new(player.Entity,clip.Role,clip.Event,now,now+2,clip,true));return true;
    }
    public bool ManualRandom(ComponentPlayer player, int command, out string reason) {
        reason=""; string role=ScAgentVoice.PlayerRole(player), language=AgentVoiceOptions.Current.Language;
        if(command<0||command>=Commands.Length){reason="语音选项无效";return false;}
        var events=Commands[command].Events;
        var candidates=AgentVoiceModLoader.Clips.Where(c=>c.Role==role&&c.Language==language
            &&events.Contains(c.Event)).ToArray();
        if(candidates.Length==0){reason="这个语音选项暂无可用语音";return false;}
        return Manual(player,candidates[random.Int(0,candidates.Length-1)],out reason);
    }
    VoiceHud Hud(ComponentPlayer p) { if(!huds.TryGetValue(p,out var h)){h=new VoiceHud();h.Attach(p.ComponentGui.ControlsContainerWidget);huds[p]=h;}return h; }
    public void OpenMenu(ComponentPlayer player) {
        if(!enabled||player is null||!AgentVoiceOptions.Current.PlayerEnabled||ScAgentVoice.PlayerRole(player) is not("ct" or "t"))return;
        Hud(player).Show(ScAgentVoice.PlayerRole(player),AgentVoiceOptions.Current.Language,Commands.Select(c=>c.Label).ToArray());
    }
    /// <summary>While the HUD is open, consume numeric hotbar selection without consuming the key edge used by the HUD.</summary>
    public void FilterInput(ComponentInput input) {
        var player=input?.m_componentPlayer;if(player is null||!huds.TryGetValue(player,out var hud)||!hud.Visible)return;
        object controls=input.m_playerInput;var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        foreach(var field in controls.GetType().GetFields(flags)){
            string name=field.Name.ToLowerInvariant();if(!name.Contains("slot")&&!name.Contains("select")&&!name.Contains("hotbar"))continue;
            try{
                if(field.FieldType==typeof(bool))field.SetValue(controls,false);
                else if(Nullable.GetUnderlyingType(field.FieldType) is not null)field.SetValue(controls,null);
                else if(field.FieldType==typeof(int))field.SetValue(controls,-1);
            }catch{ }
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] void Unsubscribe()=>ScAgentVoice.Event-=Receive;
    public override void Dispose(){if(enabled)Unsubscribe();foreach(var h in huds.Values)h.Dispose();huds.Clear();pending.Clear();speakers.Clear();playing.Clear();base.Dispose();}
    public void Update(float dt){if(enabled)Tick();}
    [MethodImpl(MethodImplOptions.NoInlining)] void Tick(){
        double now=time.GameTime;var options=AgentVoiceOptions.Current;
        if(revision!=AgentVoiceOptions.Revision){revision=AgentVoiceOptions.Revision;pending.Clear();}
        playing.RemoveAll(p=>p.Until<=now);
        foreach(var player in players.ComponentPlayers){
            var hud=Hud(player);bool active=ScGunBindings.Available(player)&&ScAgentVoice.PlayerRole(player) is "ct" or "t"&&options.PlayerEnabled;
            if(!active){hud.Hide();continue;}
            bool voicePressed=ScGunBindings.Down(player,ScGunFunctions.Voice,true);
            if(voicePressed){if(hud.Visible)hud.Hide();else OpenMenu(player);}
            if(!hud.Visible)continue;
            int selected=hud.Clicked();
            if(selected==0)for(int n=1;n<=5;n++)if(ScGunBindings.NumberDown(player,n)){selected=n;break;}
            if(selected>0){ManualRandom(player,selected-1,out string reason);if(!string.IsNullOrEmpty(reason))player.ComponentGui.DisplaySmallMessage(reason,Color.White,false,false);hud.Hide();}
        }
        foreach(var p in huds.Keys.Where(p=>!players.ComponentPlayers.Contains(p)).ToArray()){huds[p].Dispose();huds.Remove(p);}
        if(now>=nextScan){nextScan=now+.5;
            foreach(var e in Project.Entities){string role=Role(e);if(role is null||!Alive(e))continue;var s=State(e);float health=e.FindComponent<ComponentHealth>().Health;
                if(health<s.Health-.0001f)Receive(e,role,"hurt");s.Health=health;
                bool combat=e.Components.Any(c=>c.GetType().Name=="ComponentTacticalEnemy"&&c.GetType().GetField("TargetBody")?.GetValue(c)!=null||c.GetType().Name=="ComponentTacticalCompanion"&&c.GetType().GetField("threat",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(c)!=null);
                if(now>=s.Ambient){s.Ambient=now+random.Float(18,32);if(!combat)Receive(e,role,"idle");}
            }
            foreach(var e in speakers.Keys.Where(e=>!e.IsAddedToProject).ToArray())speakers.Remove(e);
        }
        foreach(var p in pending.OrderByDescending(p=>p.Manual).ToArray()){
            if(now<p.At)continue;
            if(now>p.Expires||!Alive(p.Entity)||p.Manual&&!options.PlayerEnabled||!p.Manual&&!options.NpcEnabled){pending.Remove(p);continue;}
            if(p.Manual&&p.Entity.FindComponent<ComponentPlayer>() is {} owner&&!ScGunBindings.Available(owner))continue;
            var body=p.Entity.FindComponent<ComponentBody>();if(body is null){pending.Remove(p);continue;}var pos=body.Position+Vector3.UnitY*1.5f;float max=p.Manual?24:32;
            if(!players.ComponentPlayers.Any(x=>Vector3.DistanceSquared(x.ComponentBody.Position,pos)<=max*max)||options.Volume<=0){pending.Remove(p);continue;}
            if(playing.Count>=2||!p.Manual&&playing.Any(s=>Vector3.DistanceSquared(s.Position,pos)<30*30)){pending.Remove(p);continue;}
            var s=State(p.Entity);if(now<s.Next){pending.Remove(p);continue;}
            AgentVoiceClip[] candidates=AgentVoiceModLoader.Clips.Where(c=>c.Language==options.Language&&c.Role==p.Role&&c.Event==p.Action&&!s.Recent.Contains(c.Id)&&(!s.Last.TryGetValue(c.Id,out var last)||now-last>=15)).ToArray();
            if(candidates.Length==0&&s.Recent.Count>0)candidates=AgentVoiceModLoader.Clips.Where(c=>c.Language==options.Language&&c.Role==p.Role&&c.Event==p.Action&&c.Id!=s.Recent.Last()&&(!s.Last.TryGetValue(c.Id,out var last)||now-last>=15)).ToArray();
            var clip=p.Exact is { } exact&&exact.Language==options.Language?exact:candidates.Length>0?candidates[random.Int(0,candidates.Length-1)]:null;
            pending.Remove(p);if(clip is null)continue;
            try{audio.PlaySound(clip.Resource,options.Volume,0,pos,3,false);
                s.Next=now+(p.Manual?1.2:3);s.Last[clip.Id]=now;s.Recent.Enqueue(clip.Id);while(s.Recent.Count>3)s.Recent.Dequeue();playing.Add((pos,now+clip.Duration,p.Manual));
                if(options.Captions)foreach(var listener in players.ComponentPlayers.Where(x=>Vector3.DistanceSquared(x.ComponentBody.Position,pos)<max*max))listener.ComponentGui.DisplaySmallMessage((p.Role=="ct"?"CT":"T")+" · "+clip.Label.Split('·')[0].Trim(),Color.White,false,false);
            }catch(Exception ex){KnifeDiagnostics.WarnOnce("agent-voice-"+clip.Resource,ex.Message);}
        }
    }
}

public static class AgentVoiceMenus {
    static void Select(ContainerWidget parent,string title,IEnumerable<object> rows,Func<object,string> label,Action<object> selected)=>DialogsManager.ShowDialog(parent,new ListSelectionDialog(title,rows,56,label,selected));
    static void Notice(ContainerWidget parent,string text)=>DialogsManager.ShowDialog(parent,new MessageDialog("探员语音",text,"知道了",null,null));
    public static void Settings(ContainerWidget parent){
        var o=AgentVoiceOptions.Current;
        Select(parent,"探员语音 · 设置即时保存",new object[]{"language","volume","player","npc","captions","preview"},x=>(string)x switch{
            "language"=>"语言："+(o.Language=="zh"?"中文":"英文"),"volume"=>$"音量：{o.Volume:P0}","player"=>"玩家主动语音："+(o.PlayerEnabled?"开":"关"),"npc"=>"NPC自动语音："+(o.NpcEnabled?"开":"关"),"captions"=>"语义提示："+(o.Captions?"开":"关"),_=>"本地试听（不在世界中喊话）"},x=>{
            string key=(string)x;
            if(key=="preview"){Select(parent,"选择试听声线",new object[]{"ct","t"},x=>(string)x=="ct"?"CT · SAS":"T · Phoenix",r=>Categories(parent,(string)r,null,true));return;}
            bool saved=AgentVoiceOptions.Change(v=>{switch(key){case "language":v.Language=v.Language=="zh"?"en":"zh";break;case "volume":v.Volume=v.Volume>=.99f?0:Math.Min(1,v.Volume+.1f);break;case "player":v.PlayerEnabled=!v.PlayerEnabled;break;case "npc":v.NpcEnabled=!v.NpcEnabled;break;case "captions":v.Captions=!v.Captions;break;}});
            if(saved)Settings(parent);else Notice(parent,"设置无法保存，原配置已保留。");
        });
    }
    public static void Menu(ComponentPlayer player){string role=ScAgentVoice.PlayerRole(player);if(role is not("ct" or "t")){Notice(player.GuiWidget,"请先选择CT或T人物。");return;}Categories(player.GuiWidget,role,player,false);}
    static void Categories(ContainerWidget parent,string role,ComponentPlayer player,bool preview)=>Select(parent,(role=="ct"?"CT · SAS":"T · Phoenix")+" · "+(AgentVoiceOptions.Current.Language=="zh"?"中文":"英文"),
        new object[]{"收藏","战术","回应","投掷","情绪","设置"},x=>(string)x,x=>{
            string category=(string)x;if(category=="设置"){Settings(parent);return;}
            var clips=AgentVoiceModLoader.Clips.Where(c=>c.Role==role&&c.Language==AgentVoiceOptions.Current.Language&&(category=="收藏"?AgentVoiceOptions.Current.Favorites.Contains(c.Id):c.Category==category)).ToArray();
            if(clips.Length==0){Notice(parent,"还没有收藏；在具体语句页面选择收藏。");return;}
            Select(parent,category+" · 选择语义",clips.Select(c=>c.Event).Distinct().Cast<object>(),e=>clips.First(c=>c.Event==(string)e).Label.Split('·')[0],e=>
                Select(parent,"选择具体语句",clips.Where(c=>c.Event==(string)e).Cast<object>(),c=>((AgentVoiceClip)c).Label,c=>Actions(parent,(AgentVoiceClip)c,player,preview)));
        });
    static double nextPreview;
    static void Actions(ContainerWidget parent,AgentVoiceClip clip,ComponentPlayer player,bool preview)=>Select(parent,clip.Label,new object[]{preview?"试听":"说出","收藏／取消收藏","返回"},x=>(string)x,x=>{
        switch((string)x){
            case "试听":if(Time.RealTime>=nextPreview){AudioManager.PlaySound(clip.Resource,AgentVoiceOptions.Current.Volume,0,0);nextPreview=Time.RealTime+clip.Duration+.2;}break;
            case "说出":var voice=player.Project.FindSubsystem<SubsystemScAgentVoice>(false);if(voice==null){Notice(parent,"语音系统未就绪");break;}if(!voice.Manual(player,clip,out var reason))Notice(parent,reason);break;
            case "收藏／取消收藏":if(!AgentVoiceOptions.Change(o=>{if(!o.Favorites.Remove(clip.Id))o.Favorites.Add(clip.Id);}))Notice(parent,"收藏保存失败");else Actions(parent,clip,player,preview);break;
            default:Categories(parent,clip.Role,player,preview);break;
        }
    });
}
