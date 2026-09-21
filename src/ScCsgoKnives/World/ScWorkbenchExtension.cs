namespace Game;

/// <summary>Optional modules register recipes after blocks load. The base mod has no DLC dependency.</summary>
public sealed record ScWorkbenchRecipe(string Key,string Name,string Category,Func<int> Output,Func<Dictionary<int,int>> Cost,int Level=1) {
    public bool CreativeOnly { get; init; }
    public int ResultCount { get; init; } = 1;
    public Func<int,bool> Matches { get; init; }
    public int Value=>Output();
    public Dictionary<int,int> Materials()=>Cost();
}
public sealed record ScWorkbenchAction(string Key,string Name,string Category,Action<ComponentPlayer,Action> Open);
public static class ScWorkbenchExtension {
    public const int ApiVersion=1;
    static readonly Dictionary<string,ScWorkbenchRecipe> Recipes=new(StringComparer.Ordinal);
    static readonly Dictionary<string,ScWorkbenchAction> MenuActions=new(StringComparer.Ordinal);
    public static IEnumerable<ScWorkbenchAction> Actions=>MenuActions.Values;
    public static void RegisterAction(ScWorkbenchAction action)=>MenuActions[action.Key]=action;
    public static IEnumerable<ScWorkbenchRecipe> All=>Recipes.Values;
    public static void Register(ScWorkbenchRecipe recipe)=>Recipes[recipe.Key]=recipe;
    public static ScWorkbenchRecipe Find(int value) => Recipes.Values.FirstOrDefault(r => r.Matches?.Invoke(value) ?? Terrain.ReplaceLight(r.Value,0) == Terrain.ReplaceLight(value,0));
    public static bool IsRecipe(int value) => Find(value) is not null;

    /// <summary>
    /// Recipes for CS items that used to be exposed by the vanilla nine-cell
    /// crafting grid. Keeping them here gives them the same material preview,
    /// quantity selector and help page as guns and knives. The old item values
    /// are deliberately unchanged; only the crafting route moves.
    /// </summary>
    public static void RegisterBaseRecipes() {
        static Dictionary<int,int> Cost(params (string id,int count)[] parts) =>
            parts.GroupBy(p => p.id, StringComparer.Ordinal).ToDictionary(g => ScComponentCrafting.Resolve(g.Key), g => g.Sum(p => p.count));
        static void Add(string key,string name,string category,Func<int> value, int level, (string id,int count)[] cost, int resultCount=1, bool creativeOnly=false) =>
            Register(new ScWorkbenchRecipe(key,name,category,value,()=>Cost(cost),level) { CreativeOnly=creativeOnly, ResultCount=resultCount });

        Add("ammo-magazine", "通用弹匣 ×2", "弹药", () => ScAmmoBlock.Value(ScAmmoBlock.Magazine), 1,
            [("ironingot",1),("copperingot",2),("gunpowder",3)], 2);
        Add("ammo-shell", "霰弹 ×8", "弹药", () => ScAmmoBlock.Value(ScAmmoBlock.Shell), 1,
            [("ironingot",1),("copperingot",1),("gunpowder",2),("canvas",1)], 8);
        Add("c4", "C4", "装备", () => ScC4Block.Value, 4,
            [("gunpowder",24),("copperingot",4),("sccsgomaterial:0",2),("sccsgomaterial:1",1)]);

        string[] names = ScGrenadeBlock.Names;
        string[][] ingredients = [
            ["ironingot","ironingot","gunpowder","gunpowder","gunpowder","gunpowder"],
            ["ironingot","glass","glass","gunpowder"],
            ["ironingot","coalchunk","coalchunk","canvas","gunpowder"],
            ["glass","glass","coalchunk","coalchunk","canvas","gunpowder"],
            ["ironingot","ironingot","copperingot","gunpowder","gunpowder","gunpowder","coalchunk","coalchunk"],
            ["ironingot","copperingot","gunpowder"]
        ];
        for (int i = 0; i < names.Length; i++) {
            int kind = i;
            var parts = ingredients[i].GroupBy(x => x, StringComparer.Ordinal).Select(g => (g.Key, g.Count())).ToArray();
            Add("grenade-" + i, names[i], "投掷物", () => ScGrenadeBlock.Value(kind), i is 0 or 2 or 4 ? 3 : 2, parts);
        }
        // A creature spawner has no survival crafting route. Natural CS chickens
        // remain available; do not mistake vanilla creative eggs for laid eggs.
        Add("chicken-egg", "CS 小鸡生成蛋", "生物", () => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScChickenEggBlock>(true)), 2,
            [], creativeOnly:true);
    }
}
