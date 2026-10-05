using System.Xml.Linq;
using GameEntitySystem;
namespace Game;

/// <summary>A mode a world was set up for (deathmatch-addon, design §14): its stable id, the name to show a player, and
/// whether the world is meant to be played only with it.</summary>
public sealed record ScWorldMode(string Id, string Name, bool Required);

/// <summary>The modes a world carries, kept in the world's compatibility capsule: every build of this family reads and
/// keeps the list, whether or not the package that runs a mode is installed. A world with a required mode whose package
/// is absent is dormant: it loads, its mode data stays as saved, nothing of the mode runs, and its weapons do not travel
/// to other worlds. The marker is never removed by a build that does not know the mode.</summary>
public static class ScWorldModes {
    static SubsystemScCompatibility Store(Project project) => project?.FindSubsystem<SubsystemScCompatibility>(false);
    public static IReadOnlyList<ScWorldMode> Read(XElement capsule) =>
        capsule?.Element("Modes")?.Elements("Mode").Select(e => new ScWorldMode((string)e.Attribute("Id"), (string)e.Attribute("Name") ?? (string)e.Attribute("Id"), (bool?)e.Attribute("Required") == true))
            .Where(m => !string.IsNullOrEmpty(m.Id)).ToArray() ?? [];
    public static IReadOnlyList<ScWorldMode> Of(Project project) {
        try { return Store(project) is { } store ? Read(store.ReadCapsule()) : []; }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("world-modes-read", "[CS_MODE] world mode list unreadable, kept as saved: " + e.Message); return []; }
    }
    /// <summary>Adds the mode's marker to this world, or updates its name/required flag. False when the world has no capsule store.</summary>
    public static bool Mark(Project project, ScWorldMode mode) {
        if (Store(project) is not { } store || string.IsNullOrEmpty(mode?.Id)) return false;
        var capsule = store.ReadCapsule();
        var modes = capsule.Element("Modes");
        if (modes is null) capsule.Add(modes = new XElement("Modes"));
        modes.Elements("Mode").Where(e => (string)e.Attribute("Id") == mode.Id).Remove();
        modes.Add(new XElement("Mode", new XAttribute("Id", mode.Id), new XAttribute("Name", mode.Name ?? mode.Id), new XAttribute("Required", mode.Required)));
        store.WriteCapsule(capsule);
        return true;
    }
    /// <summary>Takes the mode's marker off this world (the package that owns the mode decides when; its saved data is its own).</summary>
    public static bool Unmark(Project project, string id) {
        if (Store(project) is not { } store) return false;
        var capsule = store.ReadCapsule();
        var hit = capsule.Element("Modes")?.Elements("Mode").Where(e => (string)e.Attribute("Id") == id).ToArray() ?? [];
        if (hit.Length == 0) return false;
        foreach (var e in hit) e.Remove();
        store.WriteCapsule(capsule);
        return true;
    }
    /// <summary>The required modes of this world that nothing loaded runs.</summary>
    public static IEnumerable<ScWorldMode> Dormant(Project project) {
        string running = ScModes.Of(project)?.Id;
        return Of(project).Where(m => m.Required && m.Id != running);
    }
    /// <summary>This world is set up for a mode: its starting gifts, its weapons' travel to other worlds and the like do not apply.</summary>
    public static bool Dedicated(Project project) => Of(project).Any(m => m.Required);
    public static string DormantNotice(ScWorldMode mode) => $"本世界是“{mode.Name}”专用世界；当前未安装或未启用对应附属包，模式数据已原样保留，竞技功能休眠。";
}
