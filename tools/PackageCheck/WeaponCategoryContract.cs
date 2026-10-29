using System.Reflection;

// Current packages expose a stable key; historical compatibility fixtures predate that contract.
static class WeaponCategoryContract {
    public static string For(Assembly mod) =>
        mod.GetType("Game.ScWeaponText")?.GetField("Category")?.GetRawConstantValue() as string ?? "CS武器";
}
