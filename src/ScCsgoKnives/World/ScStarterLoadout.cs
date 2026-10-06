namespace Game;

public enum ScStarterPlan { None, Knife, KnifeAndPistol, Full }
public static class ScStarterLoadout {
    public const int MagazineCount=5;
    public static string Label(ScStarterPlan plan)=>plan switch {
        ScStarterPlan.Knife=>"随机原皮刀",
        ScStarterPlan.KnifeAndPistol=>"随机原皮刀＋随机原皮手枪",
        ScStarterPlan.Full=>"随机主武器＋随机手枪＋随机刀",
        _=>"不赠送装备"
    };
    public static bool Pistol(string name)=>name is "glock18" or "hkp2000" or "p250" or "usp_silencer" or "fiveseven" or "tec9" or "cz75a" or "elite" or "deagle" or "revolver";
    public static (int Value,int Count)[] Items(ScStarterPlan plan,Engine.Random random){
        if(!Enum.IsDefined(plan))throw new ArgumentOutOfRangeException(nameof(plan));
        if(plan==ScStarterPlan.None)return [];
        var items=new List<(int,int)>();
        int Pick(int[] pool)=>pool[random.Int(0,pool.Length-1)];
        int knife=Pick(Enumerable.Range(0,CsmcKnifeRig.KnifeCount).Where(ScMinimalEdition.KnifeAvailable).ToArray());
        items.Add((Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScKnifeBlock>(true),0,knife),1));
        void Gun(bool pistol){
            int variant=Pick(Enumerable.Range(0,GunSpec.All.Length).Where(v=>GunSpec.All[v].Name!="taser"&&Pistol(GunSpec.All[v].Name)==pistol).ToArray());
            items.Add((Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true),0,GunSpec.MakeData(variant,GunSpec.All[variant].Magazine)),1));
        }
        if(plan>=ScStarterPlan.KnifeAndPistol)Gun(true);
        if(plan==ScStarterPlan.Full)Gun(false);
        items.Add((ScAmmoBlock.Value(ScAmmoBlock.Magazine),MagazineCount));
        return items.ToArray();
    }
}
