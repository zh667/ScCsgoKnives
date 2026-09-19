namespace Game;

/// <summary>Optional modules register recipes after blocks load. The base mod has no DLC dependency.</summary>
public sealed record ScWorkbenchRecipe(string Key,string Name,string Category,Func<int> Output,Func<Dictionary<int,int>> Cost,int Level=1) {
    public int Value=>Output();
    public Dictionary<int,int> Materials()=>Cost();
}
public static class ScWorkbenchExtension {
    public const int ApiVersion=1;
    static readonly Dictionary<string,ScWorkbenchRecipe> Recipes=new(StringComparer.Ordinal);
    public static IEnumerable<ScWorkbenchRecipe> All=>Recipes.Values;
    public static void Register(ScWorkbenchRecipe recipe)=>Recipes[recipe.Key]=recipe;
}
