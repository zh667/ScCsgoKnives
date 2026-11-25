using System.Text.Json;
using System.Text.Json.Serialization;
namespace Game;

public enum TacticalRole { Sniper, Rifle, Close, Machine, Demolition }
/// <summary>NPC equipment never enters the player gun registry; enemies drop supplies instead of guns.</summary>
public sealed class TacticalEnemyState {
    public int Schema {get;set;}=1;
    public string Squad {get;set;}="";
    public TacticalRole Role {get;set;}
    public int Variant {get;set;}
    public int Rounds {get;set;}
    public int Reserve {get;set;}
    public int Grenade {get;set;}
    public int Grenades {get;set;}
    public bool Bomb {get;set;}
    public bool LootDone {get;set;}
    public float Health {get;set;}=1;
    public float ReloadLeft {get;set;}
    public float ShotLeft {get;set;}
    public float GrenadeLeft {get;set;}=12;
    /// <summary>Seconds a newly summoned challenge squad holds fire and does not start aiming, including when attacked.
    /// Absent in older saves (0).</summary>
    public float Warmup {get;set;}
    /// <summary>Recorded when the squad is created outside creative mode; only such a squad's bomb can pay a defuse
    /// reward. Absent in older saves (false), so no reward is guessed.</summary>
    public bool Rewardable {get;set;}
    /// <summary>"natural" or "manual", recorded at creation. Null in older saves: counted as natural (conservative).</summary>
    public string Source {get;set;}
    /// <summary>1.6.0 (feedback-1124-170 D7): wear of the shield escort's riot shield, 0..ScTacticalShieldBlock.Life (Life:
    /// no shield). An older reader drops the field, so a switch back and forth only loses the wear (R2).</summary>
    public int ShieldWear {get;set;}
    /// <summary>1.6.0 (user decisions 2026-11-25): in a squad with a demolition T, that T escorts with a riot shield and
    /// never plants; one rifleman is the squad's bomber instead: he carries the C4 (<see cref="Carry"/>) and fights with a
    /// pistol (<see cref="Sidearm"/>), the way the 1.5.0 demolition T did. The new fields are unknown to older readers: there
    /// the bomber is an ordinary rifleman (his rifle is <see cref="Variant"/>; his rounds fit its magazine) and the escort an
    /// ordinary pistol T, and nobody plants.</summary>
    public bool Bomber {get;set;}
    /// <summary>The bomber still carries his C4 (cleared when it is planted).</summary>
    public bool Carry {get;set;}
    /// <summary>The bomber's pistol, a GunSpec index from the pistol pool; his rounds and reserve belong to it.</summary>
    public int Sidearm {get;set;}=-1;
    /// <summary>The gun this T actually fires and shows: the bomber's pistol, otherwise <see cref="Variant"/>.</summary>
    [JsonIgnore] public int Weapon=>Bomber&&Sidearm>=0?Sidearm:Variant;
    /// <summary>Carries a bomb to plant: the bomber, or a demolition T from a 1.5.0 save that still has its bomb.</summary>
    [JsonIgnore] public bool CarriesBomb=>Bomb||Carry;
    /// <summary>The demolition T's riot shield is up while it holds; the escort never plants (a demolition T of a 1.5.0 save
    /// that still carries its bomb plants as it used to, without a shield).</summary>
    [JsonIgnore] public bool ShieldUp=>Role==TacticalRole.Demolition&&!Bomb&&ShieldWear<ScTacticalShieldBlock.Life;
    /// <summary>The bomber's pistols: the pistol pool without the dual Berettas, whose 30 rounds would not fit every rifle's
    /// magazine (an older reader checks the rounds against the rifle in <see cref="Variant"/>).</summary>
    public static readonly string[] Sidearms=["glock18","usp_silencer","hkp2000","p250","fiveseven","tec9","cz75a","deagle","revolver"];
    /// <summary>Makes a rifleman of a new squad its bomber (CreateSquad).</summary>
    public void MakeBomber(Engine.Random random){
        Bomber=true;Carry=true;Sidearm=Array.FindIndex(GunSpec.All,g=>g.Name==Sidearms[random.Int(0,Sidearms.Length-1)]);
        Rounds=GunSpec.All[Sidearm].Magazine;Reserve=Rounds*3;
    }
    public static readonly string[][] Pools=[
        ["awp","ssg08","scar20","g3sg1"],
        ["ak47","m4a1s","m4a4","famas","galilar","aug","sg556"],
        ["mac10","mp9","mp7","mp5sd","ump45","p90","bizon","nova","xm1014","mag7","sawedoff"],
        ["m249","negev"],
        ["glock18","usp_silencer","hkp2000","p250","fiveseven","tec9","cz75a","elite","deagle","revolver"]
    ];
    // Distinct names: regressions bind Create by name.
    public static TacticalEnemyState Create(TacticalRole role,string squad,Engine.Random random)=>CreateWarm(role,squad,random,0);
    public static TacticalEnemyState CreateWarm(TacticalRole role,string squad,Engine.Random random,float warmup) {
        string[] pool=Pools[(int)role];
        int index=role==TacticalRole.Close?(random.Bool()?random.Int(0,6):random.Int(7,10)):random.Int(0,pool.Length-1);
        int variant=Array.FindIndex(GunSpec.All,g=>g.Name==pool[index]);
        return new(){Squad=squad,Role=role,Variant=variant,Rounds=GunSpec.All[variant].Magazine,Reserve=GunSpec.All[variant].Magazine*3,
            Grenade=random.Int(0,9) switch {0 or 1=>1,2 or 3=>2,4=>3,5=>4,_=>0},Grenades=random.Int(0,2),Warmup=warmup};
    }
    public string Encode()=>JsonSerializer.Serialize(this);
    public static TacticalEnemyState Decode(string json) {
        var s=JsonSerializer.Deserialize<TacticalEnemyState>(json)??throw new InvalidOperationException("敌方装备数据为空。");
        if(s.Schema!=1||!Enum.IsDefined(s.Role)||string.IsNullOrWhiteSpace(s.Squad)||s.Squad.Length>80||s.Variant<0||s.Variant>=GunSpec.All.Length
            ||!Pools[(int)s.Role].Contains(GunSpec.All[s.Variant].Name)||s.Rounds<0||s.Rounds>GunSpec.All[s.Variant].Magazine||s.Reserve<0||s.Reserve>1000
            ||s.Grenade<0||s.Grenade>4||s.Grenades<0||s.Grenades>2||!float.IsFinite(s.Health+s.ReloadLeft+s.ShotLeft+s.GrenadeLeft)
            ||s.Source is not (null or "natural" or "manual")||!float.IsFinite(s.Warmup)||s.Warmup<0||s.Warmup>10||s.Health<0||s.Health>1||s.ReloadLeft<0||s.ReloadLeft>10||s.ShotLeft<0||s.ShotLeft>10||s.GrenadeLeft<0||s.GrenadeLeft>60
            ||s.Bomb&&s.Role!=TacticalRole.Demolition||s.ShieldWear<0||s.ShieldWear>ScTacticalShieldBlock.Life||s.ShieldWear>0&&s.Role!=TacticalRole.Demolition
            ||s.Carry&&!s.Bomber||s.Bomber&&(s.Role==TacticalRole.Demolition||s.Sidearm<0||s.Sidearm>=GunSpec.All.Length||!Sidearms.Contains(GunSpec.All[s.Sidearm].Name)||s.Rounds>GunSpec.All[s.Sidearm].Magazine))throw new InvalidOperationException("敌方装备存档版本或数值不受支持，拒绝重置。");
        // A demolition T of a 1.5.0 save (or of an earlier 1.6.0 candidate) that still carries its bomb plants it as it used
        // to and never raises a shield, before or after the plant.
        if(s.Role==TacticalRole.Demolition&&s.Bomb)s.ShieldWear=ScTacticalShieldBlock.Life;
        return s;
    }
    [JsonIgnore] public int DisplayValue=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true),0,GunSpec.MakeData(Weapon,0,false));
}

public enum DefuseResult { Active, Cancelled, Defused, Exploded }
public sealed class TacticalDefusePress {
    bool held;
    public void Cancel()=>held=true;
    public bool Step(bool down,bool eligible){bool start=down&&!held&&eligible;held=down;return start;}
}
public sealed class TacticalDefuseClock {
    public float Elapsed {get;private set;}
    public float Duration {get;}
    public TacticalDefuseClock(bool kit)=>Duration=kit?5:10;
    public float Needed=>Math.Max(0,Duration-Elapsed);
    public bool Enough(float fuse)=>fuse>Needed;
    /// <summary>A multiplayer client's copy of the server's clock.</summary>
    public void MirrorElapsed(float elapsed)=>Elapsed=Math.Clamp(elapsed,0,Duration);
    public DefuseResult Advance(float fuseBefore,float dt,bool valid) {
        if(!float.IsFinite(fuseBefore+dt)||dt<0)throw new ArgumentOutOfRangeException(nameof(dt));
        if(!valid)return fuseBefore<=dt?DefuseResult.Exploded:DefuseResult.Cancelled;
        if(Needed<=dt&&Needed<fuseBefore){Elapsed=Duration;return DefuseResult.Defused;}
        if(fuseBefore<=dt)return DefuseResult.Exploded;
        Elapsed+=dt;return DefuseResult.Active;
    }
}
