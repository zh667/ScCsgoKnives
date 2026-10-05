using System.Reflection;

/// <summary>Which builds play together in multiplayer (docs/tasks/first-person-eye-shot-20261001.md; user 2026-10-01: phone
/// players use Lite, computer players Full, in one session): the release pipeline stamps one gameplay identity into the Full
/// and split Lite cores and both agents builds of a release; the handshake compares that identity (not the exact module) and
/// requires the agents on both sides or on neither, refusing with a reason the player can act on.</summary>
static class NetIdentityRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("net-identity/" + name, ok, detail));
        var identity = mod.GetType("Game.ScNetIdentity", false);
        if (identity is null) { Check("the core knows which builds play together (Game.ScNetIdentity)", false, "type missing: an older core, where Full and Lite refuse each other"); return results; }
        var incompatibility = identity.GetMethod("Incompatibility", BindingFlags.Public | BindingFlags.Static);
        var of = identity.GetMethod("Of", BindingFlags.Public | BindingFlags.Static);
        string Why(string peerBuild, string peerAgents, string serverBuild, string serverAgents) => (string)incompatibility.Invoke(null, [peerBuild, peerAgents, serverBuild, serverAgents]);
        const string release = "0123456789abcdef0123456789abcdef", other = "fedcba9876543210fedcba9876543210";

        Check("Full and Lite + agents of one release accept each other (same identity, agents on both sides)", Why(release, release, release, release) is null);
        Check("Full and Lite without agents on both sides accept each other", Why(release, "", release, "") is null);
        string why = Why(other, release, release, release);
        Check("another release is refused, naming the release", why?.Contains("不是同一次发布") == true, why);
        why = Why(release, "", release, release);
        Check("agents on the server only: refused, asking for the agents package", why?.Contains("请安装同版本探员包") == true, why);
        why = Why(release, release, release, "");
        Check("agents on the client only: refused, explaining the server has none", why?.Contains("服务器没有探员") == true, why);
        why = Why(release, other, release, release);
        Check("agents of another release: refused", why?.Contains("探员包") == true && why.Contains("不是同一次发布"), why);
        why = Why("module:" + Guid.NewGuid(), "", "module:" + Guid.NewGuid(), "");
        Check("two different development builds are refused as before (exact module)", why is not null, why);

        string own = (string)of.Invoke(null, [mod]);
        Check("this core carries a release gameplay identity (built by the release pipeline)", !own.StartsWith("module:") && own.Length >= 16, own);
        string plain = (string)of.Invoke(null, [typeof(NetIdentityRegression).Assembly]);
        Check("an unstamped assembly is identified by its exact module", plain == "module:" + typeof(NetIdentityRegression).Assembly.ManifestModule.ModuleVersionId, plain);
        return results;
    }
}
