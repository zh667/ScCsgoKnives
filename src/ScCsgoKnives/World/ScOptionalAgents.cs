namespace Game;

/// <summary>Package capability, independent of the archive-free saved-mod identity alias.</summary>
public static class ScOptionalAgents {
#if SC_SPLIT
    public const bool Split = true;
    public static bool Available => ModsManager.ModList.Any(m => !m.IsDisabled && m.ModArchive != null && m.modInfo.PackageName == "zh667.ScCsgoTactical")
        && ModsManager.Dlls.Values.Any(a => a.GetType("Game.ScSplitAgentMarker") != null);
#else
    public const bool Split = false;
    public static bool Available => !ScMinimalEdition.Enabled;
#endif
}
