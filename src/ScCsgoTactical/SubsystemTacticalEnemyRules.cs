using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>Natural enemy-squad rules of this world (switch, whole in-game days to wait, density). Its own saved group,
/// listed in the compatibility manifest, so older compatible packages and the core-only edition keep it untouched.
/// A world without rules takes the device defaults once (an old "off" preference stays off), then keeps its own.</summary>
public sealed class SubsystemTacticalEnemyRules : Subsystem {
    public const int Schema=1;
    public ScEnemyRules Rules {get;private set;}=ScEnemyRules.Default;
    public bool FromDefaults {get;private set;}
    public override void Load(ValuesDictionary values){
        base.Load(values);
        if(values.GetValue("Schema",Schema)!=Schema)throw new InvalidOperationException("敌对小队世界规则版本不受支持，请使用相应版本探员包。");
        if(values.GetValue("Initialized",false)){
            int days=values.GetValue("GraceDays",ScEnemySpawnPolicy.GraceDays),density=values.GetValue("Density",(int)ScEnemyDensity.Standard);
            var read=new ScEnemyRules(values.GetValue("Natural",true),days,(ScEnemyDensity)density);
            Rules=read.Normalize();
            if(Rules!=read)Log.Warning($"[CS_SPAWN] world rules out of range (days={days}, density={density}); using {Rules.GraceDays}/{Rules.Density}.");
        }else{Rules=ScUiSettings.EnemyDefaults;FromDefaults=true;}
        ScEnemyRulesBridge.Read=p=>p?.FindSubsystem<SubsystemTacticalEnemyRules>(false)?.Rules;
        ScEnemyRulesBridge.Write=(p,r)=>p?.FindSubsystem<SubsystemTacticalEnemyRules>(false) is {} s&&s.Set(r);
        ScEnemyRulesBridge.Progress=Describe;
        Log.Information($"[CS_SPAWN] world rules natural={Rules.Natural} graceDays={Rules.GraceDays} density={Rules.Density} source={(FromDefaults?"device-defaults":"world")}");
    }
    public override void Save(ValuesDictionary values){
        base.Save(values);values.SetValue("Schema",Schema);values.SetValue("Initialized",true);
        values.SetValue("Natural",Rules.Natural);values.SetValue("GraceDays",Rules.GraceDays);values.SetValue("Density",(int)Rules.Density);
    }
    public bool Set(ScEnemyRules rules){Rules=rules.Normalize();FromDefaults=false;Log.Information($"[CS_SPAWN] world rules changed natural={Rules.Natural} graceDays={Rules.GraceDays} density={Rules.Density}");return true;}
    /// <summary>Progress line of the settings page for the rules being edited. Passing the waiting period does not
    /// mean a squad can appear: the first real blocker is named (video-feedback-20260929 S0). The global creature
    /// limit is reported as found; who changed it is unknown here, so no other mod is named.</summary>
    static string Describe(GameEntitySystem.Project project,ScEnemyRules rules){
        var info=project.FindSubsystem<SubsystemGameInfo>(false);var day=project.FindSubsystem<SubsystemTimeOfDay>(false)?.DayDuration??0;
        if(info is null||!(day>0))return "";
        double elapsed=info.TotalElapsedGameTime,left=ScEnemySpawnPolicy.RemainingSeconds(elapsed,day,rules.GraceDays);
        bool mode=info.WorldSettings.EnvironmentBehaviorMode==EnvironmentBehaviorMode.Living&&info.WorldSettings.GameMode>=GameMode.Survival;
        string state=!rules.Natural?"已关闭":!mode?"当前世界模式不自然刷新":left>0?$"还需约 {left/day:0.0} 个游戏日":Ready(project.FindSubsystem<SubsystemTacticalEnemies>(false),rules);
        return $"本世界已进行 {elapsed/day:0.0} 个游戏日；{state}。";
    }
    public static string Ready(SubsystemTacticalEnemies director,ScEnemyRules rules){
        if(director is null)return "等待期已结束";
        var budget=director.Budget;var limits=ScEnemySpawnPolicy.Limits(rules.Density);
        string limit=budget.Limit==SubsystemTacticalEnemies.NaturalBudget.VanillaLimit?$"{budget.Limit}":$"{budget.Limit}（原版默认 {SubsystemTacticalEnemies.NaturalBudget.VanillaLimit}，{LimitSource()}）";
        switch(director.BlockerFor(rules)){
            case SubsystemTacticalEnemies.NaturalBlocker.BudgetImpossible:
                return $"等待期已结束，但自然刷新无法进行：游戏当前的普通生物总上限为 {limit}，容纳不下 {budget.Squad} 人小队。本模组不会修改该上限或拆散小队；召唤信标不受此限制";
            case SubsystemTacticalEnemies.NaturalBlocker.BudgetFull:
                return $"等待期已结束；当前已有普通生物 {budget.Creatures} 只，总上限 {limit}，暂时容纳不下 {budget.Squad} 人小队";
            case SubsystemTacticalEnemies.NaturalBlocker.SquadCap:
                return $"等待期已结束；自然敌人已有 {director.NaturalCount} 名，再来一队会超过“{ScEnemySpawnPolicy.Label(rules.Density)}”的 {limits.Cap} 名上限";
            case SubsystemTacticalEnemies.NaturalBlocker.Cooldown:
                return $"等待期已结束；距上一队的最短间隔还剩约 {director.CooldownLeft:0} 秒";
        }
        string text="等待期已结束，规则允许生成。是否刷出由游戏的自然刷新机制随机决定（与其他生物竞争），不是定时必刷；刷出时敌队站在距玩家约 32～44 格的露天可站地面，优先视线外，出现在视野内时至少 32 格、渐显并有 3 秒警告，然后朝玩家当时所在方向巡逻一段";
        var refusals=director.RecentRefusals.Where(p=>p.Key.StartsWith("placement:",StringComparison.Ordinal)||p.Key.StartsWith("squad-placement:",StringComparison.Ordinal)).ToArray();
        if(refusals.Length>0)text+=$"；本次进入世界以来有 {refusals.Sum(p=>p.Value)} 个候选位置因地形或视线被拒（{string.Join("、",refusals.GroupBy(p=>Reason(p.Key)).OrderByDescending(g=>g.Sum(p=>p.Value)).Take(3).Select(g=>g.Key+g.Sum(p=>p.Value)+"次"))}）";
        var met=director.Encounters;
        if(met.Count>0)text+=$"；最近 {met.Count} 队自然敌队中 {met.Count(e=>e.Seen>=0||e.Engaged>=0)} 队已与玩家遭遇，{met.Count(e=>e.Slept>=0)} 队未遇到玩家就因距离过远而休眠";
        return text;
    }
    /// <summary>Known mods that lower the game's creature limits, by their own modinfo (r2-c4-completion-20260929: the
    /// installed Slower Creature Spawns 1.0.0 at its 0.1x setting was verified to lower 26 to 2). Named only when it is
    /// actually loaded; otherwise the text says the limit was changed without guessing by whom.</summary>
    public static readonly string[] KnownLimiters=["Slower Creature Spawns"];
    public static string LimitSource(){
        var loaded=ModsManager.ModList.Where(m=>m is {IsDisabled:false}&&m.modInfo?.Name is {} n&&KnownLimiters.Contains(n)).Select(m=>$"“{m.modInfo.Name} {m.modInfo.Version}”").ToArray();
        return loaded.Length>0?$"已加载的模组 {string.Join("、",loaded)} 会降低该上限":"已被其他设置或模组调整";
    }
    static string Reason(string key)=>key[(key.IndexOf(':')+1)..] switch{
        "fluid"=>"脚下或身体处有液体","hazard"=>"危险方块",
        "unloaded"=>"地形未加载","height"=>"高度越界","under-cover"=>"上方有屋顶或悬垂遮挡","headroom"=>"头顶空间不足","soft-ground"=>"地面不可站立",
        "near-player"=>"离玩家太近","in-view"=>"在玩家视野内且不足 32 格","occupied"=>"位置被占","uneven"=>"队员落点高差过大",_=>"其他"};
}
