namespace Game;

/// <summary>Package capability, independent of the archive-free saved-mod identity alias.</summary>
public static class ScOptionalAgents {
    /// <summary>Core contract for the agents package: dormant companion equipment is visible to gun holder scans.</summary>
    public const int CompanionLedgerProtocol = 1;
    /// <summary>World enemy rules, damage direction and trajectory contracts used by the agents package.</summary>
    public const int FollowupProtocol = 1;
#if SC_SPLIT
    public const bool Split = true;
    public static bool Available => ModsManager.ModList.Any(m => !m.IsDisabled && m.ModArchive != null && m.modInfo.PackageName == "zh667.ScCsgoTactical")
        && ModsManager.Dlls.Values.Any(a => a.GetType("Game.ScSplitAgentMarker") != null);
#else
    public const bool Split = false;
    public static bool Available => !ScMinimalEdition.Enabled;
#endif
}
