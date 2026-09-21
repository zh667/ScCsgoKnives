using Engine.Graphics;
namespace Game;

/// <summary>Optional appearance components report only their own entity's selected role.</summary>
public interface IScFirstPersonAppearance { string FirstPersonRole { get; } }
public sealed record ScFirstPersonArmPart(Cs2SkinnedMesh Mesh, string[] Materials, Texture2D[] Textures);
public static class ScFirstPersonAppearance {
    // Resolver receives the actual drawing player, never a global active-player selection.
    // Null/empty means the core's original arms, including when the DLC is absent.
    public static Func<ComponentFirstPersonModel, IReadOnlyList<ScFirstPersonArmPart>> Resolve { get; set; }
}
