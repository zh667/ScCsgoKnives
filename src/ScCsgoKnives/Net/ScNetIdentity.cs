using System.Reflection;
namespace Game;

/// <summary>Which builds may play together in multiplayer (2026-10-01, user: phone players use Lite, computer players Full,
/// and the two must connect). The release pipeline stamps every assembly it builds from one source snapshot — the Full and
/// split Lite cores and both agents builds — with the same gameplay identity (assembly metadata <see cref="MetadataKey"/>):
/// the editions differ in resources and in which assembly holds the agents' item types (the world maps those by class name),
/// not in anything the network carries or the server decides. An unstamped (development) build is identified by its exact
/// module, as before. The agents must be on both sides or on neither: a server's agents need the client's templates and
/// models, a client's agents need the server to run them.</summary>
public static class ScNetIdentity {
    public const string MetadataKey = "ScGameplayIdentity";
    const string ModulePrefix = "module:";

    /// <summary>The release stamp of <paramref name="assembly"/>, else its exact module.</summary>
    public static string Of(Assembly assembly) {
        if (assembly is null) return "";
        foreach (var a in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            if (a.Key == MetadataKey && !string.IsNullOrEmpty(a.Value)) return a.Value;
        return ModulePrefix + assembly.ManifestModule.ModuleVersionId;
    }
    /// <summary>This core's gameplay build.</summary>
    public static string Build => Of(typeof(ScNetIdentity).Assembly);
    /// <summary>The agents this process plays with: the build of the loaded agents assembly (bundled in Full, the separate
    /// agents package beside split Lite), or "" when they are not available.</summary>
    public static string Agents => ScOptionalAgents.Available && ModsManager.Dlls.Values.FirstOrDefault(a => a.GetName().Name == "ScCsgoTactical") is { } tactical
        ? Of(tactical) : "";
    /// <summary>For logs and notices only; never compared.</summary>
    public static string Edition => ScMinimalEdition.Enabled ? "mini" : ScOptionalAgents.Split ? "lite" : "full";

    /// <summary>Why a peer with this core build and agents cannot play on this server, or null.</summary>
    public static string Incompatibility(string peerBuild, string peerAgents, string serverBuild, string serverAgents) {
        peerAgents ??= ""; serverAgents ??= "";
        if (peerBuild != serverBuild) return $"CS 武器构建 {Short(peerBuild)} 与服务器 {Short(serverBuild)} 不是同一次发布（轻量包和全量包可以互联，但须是同一次发布的版本）";
        if (peerAgents == serverAgents) return null;
        if (serverAgents.Length == 0) return "服务器没有探员（轻量包未装探员包），本机有探员：双方须一致，请服务器装上同版本探员包";
        if (peerAgents.Length == 0) return "服务器有探员（全量包或轻量包+探员包），本机没有：请安装同版本探员包";
        return $"探员包 {Short(peerAgents)} 与服务器 {Short(serverAgents)} 不是同一次发布";
    }
    /// <summary>The mode the loaded world runs, as both ends must have it (deathmatch-addon, protocol 7): its id, the build
    /// of the package that runs it and that package's rules fingerprint; "" in a world without a mode. A client that has
    /// the package loads the same world data and so arrives at the same text; one without it arrives at "".</summary>
    public static string Mode(GameEntitySystem.Project project) => ScModes.Of(project) is { } mode ? mode.Id + "#" + Of(mode.GetType().Assembly) + "#" + mode.RulesFingerprint : "";
    /// <summary>Why a peer cannot take part in the mode this server's world runs, or null. An ordinary world asks nothing:
    /// a package that is merely installed on one side changes nothing there.</summary>
    public static string ModeIncompatibility(string peerMode, string serverMode) {
        peerMode ??= ""; serverMode ??= "";
        if (serverMode.Length == 0 || peerMode == serverMode) return null;
        string[] server = serverMode.Split('#'), peer = peerMode.Split('#');
        string name = ScModes.Of(GameManager.Project)?.DisplayName ?? server[0];
        if (peerMode.Length == 0 || peer[0] != server[0]) return $"服务器的世界运行“{name}”模式，本机未安装或未启用对应附属包：请安装与服务器同一次发布的附属包";
        if (peer.Length < 3 || server.Length < 3 || peer[1] != server[1]) return $"“{name}”附属包 {Short(peer.Length > 1 ? peer[1] : "")} 与服务器 {Short(server.Length > 1 ? server[1] : "")} 不是同一次发布";
        return $"“{name}”的竞技规则与服务器不一致（{Short(peer[2])} / {Short(server[2])}）：请两端使用同一次发布的附属包";
    }
    static string Short(string id) => string.IsNullOrEmpty(id) ? "?" : id.StartsWith(ModulePrefix, StringComparison.Ordinal) ? "开发构建 " + id[ModulePrefix.Length..Math.Min(id.Length, ModulePrefix.Length + 8)]
        : id.Length > 8 ? id[..8] : id;
}
