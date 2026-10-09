namespace Game;

/// <summary>Optional deathmatch entry supplied by the separate addon. The core owns the settings-page slot without
/// taking a compile-time dependency on the addon.</summary>
public static class ScDeathmatchSettings {
    public static Action<ContainerWidget> Open;
    public static void ResetProvider() => Open = null;
}
