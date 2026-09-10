using System.Reflection;
namespace Game;

public static class ScAnimationResources {
    // Real assembly reference lets the game's existing mod assembly resolver load the required resource pack.
    // Gameplay metadata/shaders remain in core so stat and shader fixes do not churn the resource pack.
    public static Assembly Assembly => typeof(ScCsgoResources.ResourceMarker).Assembly;
}
