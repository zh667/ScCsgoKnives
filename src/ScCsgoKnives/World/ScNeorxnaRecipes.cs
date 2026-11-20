using System.Reflection;
namespace Game;

/// <summary>feedback-1120-160 #1 (player: "枪械1.5和neo1.4会冲突，导致通用弹匣配方看不到"). Neorxna 1.4.x shows recipes on its
/// item hover card and in its recipe dialog without asking the block for its recipe page: it asks RecipaediaEX when that is
/// installed, otherwise every function in its public <c>RecipaediaRecipesNeo.RecipeWidgetProviders</c> list, whose only
/// entry walks the nine-cell crafting table. Everything CS makes at the weapon workbench is not in that table, so the card
/// said there was no recipe. This adds one provider that answers for workbench items with the same recipe the assembly
/// page shows (ScAssemblyRecipeCard). No hard dependency: without Neorxna nothing happens; a list of another shape is left
/// alone with one warning. Usages (the provider's second argument) are not answered.</summary>
public static class ScNeorxnaRecipes {
    public const string TypeName = "Neorxna.NUI.RecipaediaRecipesNeo", FieldName = "RecipeWidgetProviders";
    static readonly Func<int, bool, IEnumerable<Widget>> s_provider = Provide;
    /// <summary>"not run", "absent", "registered", or why the contract was not used (logs and offline checks).</summary>
    public static string Status { get; private set; } = "not run";

    public static void Register() => RegisterWith(AppDomain.CurrentDomain.GetAssemblies());

    /// <summary><see cref="Register"/> over a given set of assemblies (offline checks pass the real neorxna.dll).</summary>
    public static string RegisterWith(IEnumerable<Assembly> assemblies) {
        try {
            Type type = assemblies.Select(TypeIn).FirstOrDefault(t => t is not null);
            if (type is null) return Status = "absent";
            if (type.GetField(FieldName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is not List<Func<int, bool, IEnumerable<Widget>>> list) {
                Status = "contract differs: no public static List<Func<int,bool,IEnumerable<Widget>>> " + FieldName;
                KnifeDiagnostics.WarnOnce("neorxna-recipes-contract", "[CS_NEORXNA] " + Status + "; workbench recipes not offered to Neorxna.");
                return Status;
            }
            if (!list.Contains(s_provider)) list.Add(s_provider);
            Status = "registered";
            KnifeLog.Information("[CS_NEORXNA] workbench recipes offered to Neorxna's recipe cards (" + type.Assembly.GetName().Version + ").");
        } catch (Exception e) {
            Status = "failed: " + e.GetBaseException().Message;
            KnifeDiagnostics.WarnOnce("neorxna-recipes-failed", "[CS_NEORXNA] " + Status);
        }
        return Status;
    }

    static Type TypeIn(Assembly assembly) {
        try { return assembly.GetType(TypeName, false); } catch { return null; }
    }

    /// <summary>The provider: one card for an item the workbench makes; nothing for anything else or for usages.</summary>
    public static IEnumerable<Widget> Provide(int value, bool asIngredient) {
        if (asIngredient) return [];
        try { return ScAssemblyRecipeCard.Find(value) is { } recipe ? [ScAssemblyRecipeCard.Card(recipe)] : []; }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("neorxna-recipe-card", "[CS_NEORXNA] recipe card failed: " + e.GetBaseException().Message);
            return [];
        }
    }
}
