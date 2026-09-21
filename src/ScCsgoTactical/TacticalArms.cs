using System.IO;
using System.Text.Json;
using Engine;
using Engine.Graphics;
namespace Game;

public static class TacticalArms {
    public sealed record Glove(string Key,string Name,string Mesh,int PaintId);
    public static readonly Glove[] Gloves=[
        new("sporty_green","运动手套｜树篱迷宫","sporty",10038),
        new("sporty_purple","运动手套｜潘多拉之盒","sporty",10037),
        new("specialist_kimono_diamonds_red","专业手套｜深红和服","specialist",10033),
        new("sporty_blue_pink","运动手套｜迈阿密风云","sporty",10048),
        new("slick_red","驾驶手套｜深红织物","slick",10016)];
    static readonly Dictionary<string,Cs2SkinnedMesh> meshes=[];
    static readonly Dictionary<string,IReadOnlyList<ScFirstPersonArmPart>> sets=[];
    static Dictionary<string,string> materials;
    static Stream Resource(string name)=>typeof(TacticalArms).Assembly.GetManifestResourceStream("Game.ArmData."+name)
        ?? throw new InvalidOperationException("Missing tactical arms resource: "+name);
    public static Cs2SkinnedMesh Mesh(string name){
        if(!meshes.TryGetValue(name,out var mesh)){using var s=Resource(name+".skin");meshes[name]=mesh=Cs2SkinnedMesh.ReadMesh(s);}return mesh;
    }
    public static string Role(ComponentFirstPersonModel first)=>first?.Entity?.Components.OfType<IScFirstPersonAppearance>().FirstOrDefault()?.FirstPersonRole;
    public static bool ValidGlove(string key)=>key==""||Gloves.Any(g=>g.Key==key);
    public static IReadOnlyList<ScFirstPersonArmPart> Resolve(ComponentFirstPersonModel first){
        var player=first?.Entity?.FindComponent<ComponentPlayer>();if(player==null)return null;
        string role=Role(first);
        string glove=first.Project.FindSubsystem<SubsystemScTactical>(false)?.GloveFor(player.PlayerData.PlayerIndex)??"";
        if(role is not ("ct" or "t") && glove=="")return null;
        try{return ResolveSet(role,glove);}catch(Exception e){KnifeDiagnostics.WarnOnce("tactical-arms-"+role+glove,e.Message);return null;}
    }
    public static IReadOnlyList<ScFirstPersonArmPart> ResolveSet(string role,string key){
        role=role is "ct" or "t"?role:"";
        var glove=Gloves.FirstOrDefault(g=>g.Key==key);
        if(role==""&&glove==null)return null;
        string cache=role+":"+(glove?.Key??"");if(sets.TryGetValue(cache,out var result))return result;
        if(materials==null){using var s=Resource("materials.json");materials=JsonSerializer.Deserialize<Dictionary<string,string>>(s);}
        var layers=new List<ScFirstPersonArmPart>();
        void Add(string name){
            var mesh=Mesh(name);var names=new string[mesh.Primitives.Length];var textures=new Texture2D[names.Length];
            for(int i=0;i<names.Length;i++){
                string mat=mesh.Primitives[i].Material;
                names[i]=mat=="bare_arm_133"?"cs2_arm":glove!=null&&mat.StartsWith("glove_"+glove.Mesh+"_")
                    ?"tactical_arm_"+glove.Key+(mat.EndsWith("left")?"_left":"_right"):materials[mat];
                textures[i]=ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+names[i]);
            }
            layers.Add(new(mesh,names,textures));
        }
        Add(glove!=null?"glove_"+glove.Mesh:role+"_default");
        if(role!="")Add(role+"_sleeves");
        return sets[cache]=layers;
    }
    public static void Clear(){sets.Clear();meshes.Clear();materials=null;}
    public static void Register(){
        ScFirstPersonAppearance.Resolve=Resolve;
        ScWorkbenchExtension.RegisterAction(new("tactical-gloves","人物外观／更换手套","功能",Open));
    }
    static void Open(ComponentPlayer player,Action back){
        var subsystem=player.Project.FindSubsystem<SubsystemScTactical>(true);
        string selected=subsystem.GloveFor(player.PlayerData.PlayerIndex);
        string role=Role(player.Entity.FindComponent<ComponentFirstPersonModel>());
        string scope=role is "ct" or "t"?"第一人称与当前 CT／T 的第三人称同步更换。":"第一人称生效；选择 CT／T 角色后，第三人称也会同步。";
        ScWorkbenchAppearance[] items=[new("","跟随角色默认手套","恢复角色原装手套。未选 CT／T 时恢复原第一人称手臂。\n免费外观，不消耗材料。","Textures/ScCsgoTactical/Gloves/default_"+(role is "ct" or "t"?role:"arms")),
            ..Gloves.Select(g=>new ScWorkbenchAppearance(g.Key,g.Name,"崭新出厂 · 磨损 0.06\n"+scope+"\n袖子跟随角色；按玩家独立保存。\n免费外观，不消耗材料。","Textures/ScCsgoTactical/Gloves/"+g.Key))];
        var dialog=new ScWorkbenchSelectionDialog("人物外观 · 更换手套",items,64,
            item=>(((ScWorkbenchAppearance)item).Key==selected?"✓ 已装备 · ":"")+((ScWorkbenchAppearance)item).Name,
            item=>{if(player.ComponentHealth.Health>0){subsystem.SetGlove(player.PlayerData.PlayerIndex,((ScWorkbenchAppearance)item).Key);
                player.ComponentGui.DisplaySmallMessage("手套已更换。CT／T 的第一、第三人称将同步显示。",Color.White,false,false);}back();},player.ComponentMiner.Inventory,false){BackAction=back};
        dialog.RestoreNavigation(new("全部",items.FirstOrDefault(i=>i.Key==selected)??items[0],0));
        DialogsManager.ShowDialog(player.GuiWidget,dialog);
    }
}
