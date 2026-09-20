using System.Text.Json;
using System.Text.Json.Serialization;
namespace Game;

public enum TacticalRole { Sniper, Rifle, Close, Machine, Demolition }
/// <summary>NPC equipment never enters the player gun registry until a gun actually drops.</summary>
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
    public static readonly string[][] Pools=[
        ["awp","ssg08","scar20","g3sg1"],
        ["ak47","m4a1s","m4a4","famas","galilar","aug","sg556"],
        ["mac10","mp9","mp7","mp5sd","ump45","p90","bizon","nova","xm1014","mag7","sawedoff"],
        ["m249","negev"],
        ["glock18","usp_silencer","hkp2000","p250","fiveseven","tec9","cz75a","elite","deagle","revolver"]
    ];
    public static TacticalEnemyState Create(TacticalRole role,string squad,Engine.Random random) {
        string[] pool=Pools[(int)role];
        int index=role==TacticalRole.Close?(random.Bool()?random.Int(0,6):random.Int(7,10)):random.Int(0,pool.Length-1);
        int variant=Array.FindIndex(GunSpec.All,g=>g.Name==pool[index]);
        return new(){Squad=squad,Role=role,Variant=variant,Rounds=GunSpec.All[variant].Magazine,Reserve=GunSpec.All[variant].Magazine*3,
            Grenade=random.Int(0,9) switch {0 or 1=>1,2 or 3=>2,4=>3,5=>4,_=>0},Grenades=random.Int(0,2),Bomb=role==TacticalRole.Demolition};
    }
    public string Encode()=>JsonSerializer.Serialize(this);
    public static TacticalEnemyState Decode(string json) {
        var s=JsonSerializer.Deserialize<TacticalEnemyState>(json)??throw new InvalidOperationException("敌方装备数据为空。");
        if(s.Schema!=1||!Enum.IsDefined(s.Role)||string.IsNullOrWhiteSpace(s.Squad)||s.Squad.Length>80||s.Variant<0||s.Variant>=GunSpec.All.Length
            ||!Pools[(int)s.Role].Contains(GunSpec.All[s.Variant].Name)||s.Rounds<0||s.Rounds>GunSpec.All[s.Variant].Magazine||s.Reserve<0||s.Reserve>1000
            ||s.Grenade<0||s.Grenade>4||s.Grenades<0||s.Grenades>2||!float.IsFinite(s.Health+s.ReloadLeft+s.ShotLeft+s.GrenadeLeft)
            ||s.Health<0||s.Health>1||s.ReloadLeft<0||s.ReloadLeft>10||s.ShotLeft<0||s.ShotLeft>10||s.GrenadeLeft<0||s.GrenadeLeft>60
            ||s.Bomb&&s.Role!=TacticalRole.Demolition)throw new InvalidOperationException("敌方装备存档版本或数值不受支持，拒绝重置。");
        return s;
    }
    [JsonIgnore] public int DisplayValue=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true),0,GunSpec.MakeData(Variant,0,false));
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
    public DefuseResult Advance(float fuseBefore,float dt,bool valid) {
        if(!float.IsFinite(fuseBefore+dt)||dt<0)throw new ArgumentOutOfRangeException(nameof(dt));
        if(!valid)return fuseBefore<=dt?DefuseResult.Exploded:DefuseResult.Cancelled;
        if(Needed<=dt&&Needed<fuseBefore){Elapsed=Duration;return DefuseResult.Defused;}
        if(fuseBefore<=dt)return DefuseResult.Exploded;
        Elapsed+=dt;return DefuseResult.Active;
    }
}
