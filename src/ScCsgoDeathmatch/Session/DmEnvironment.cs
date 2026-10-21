using System.Xml.Linq;
using Engine.Serialization;
using GameEntitySystem;
namespace Game;

/// <summary>The world a deathmatch is fought in (2026-10-21 player request: "世界默认改成静态，没有天气，永远中午"): while the
/// mode governs the world and the host's rule <see cref="DmRules.FixedEnvironment"/> is on (the default), the world runs
/// with the engine's own settings for a static world without weather at a fixed noon (EnvironmentBehaviorMode.Static,
/// TimeOfDayMode.Day, no weather effects) - settings vanilla offers in creative worlds only.
///
/// The world's own settings are not changed. They are put back when the hold ends (the host returns to map editing or
/// switches the rule off, the world is left), and whatever is saved meanwhile carries them, not the held ones: the save
/// is corrected after the engine wrote it (<see cref="CorrectSaved"/>, the engine's OnProjectXmlSaved), so no save -
/// an autosave, the app going to the background, a crash afterwards - keeps a static world. Each process holds its own
/// copy: the server from its match, a client from what the server says.</summary>
public sealed class DmEnvironment {
    public readonly record struct Settings(EnvironmentBehaviorMode Environment, TimeOfDayMode Time, bool Weather) {
        public static Settings Of(WorldSettings world) => new(world.EnvironmentBehaviorMode, world.TimeOfDayMode, world.AreWeatherEffectsEnabled);
        public void ApplyTo(WorldSettings world) { world.EnvironmentBehaviorMode = Environment; world.TimeOfDayMode = Time; world.AreWeatherEffectsEnabled = Weather; }
    }
    /// <summary>What a governed arena runs with.</summary>
    public static readonly Settings Arena = new(EnvironmentBehaviorMode.Static, TimeOfDayMode.Day, false);

    /// <summary>The world's own settings while they are held (null: nothing held).</summary>
    public Settings? Own { get; private set; }

    /// <summary>Every frame: hold the arena's settings while <paramref name="wanted"/>, give the world its own back when not.</summary>
    public void Update(Project project, SubsystemGameInfo info, bool wanted) {
        if (info?.WorldSettings is not { } world) return;
        if (wanted) {
            if (Own is null) {
                Own = Settings.Of(world);
                // kept for the whole life of the world: a save the engine took while the hold was on may be written after it ended
                s_saved = new Saved(project, info.ValuesDictionary?.DatabaseObject?.Name ?? "GameInfo", Own.Value);
            }
            if (Settings.Of(world) != Arena) Arena.ApplyTo(world);
        }
        else Release(info);
    }
    public void Release(SubsystemGameInfo info) {
        if (Own is not { } own) return;
        if (info?.WorldSettings is { } world) own.ApplyTo(world);
        Own = null;
    }
    /// <summary>The world is gone (after its last save): nothing of it is corrected any more.</summary>
    public static void Forget(Project project) { if (s_saved is { } saved && ReferenceEquals(saved.Project, project)) s_saved = null; }

    sealed record Saved(Project Project, string GameInfo, Settings Own);
    static volatile Saved s_saved;
    /// <summary>The engine wrote a save (OnProjectXmlSaved, on its saving thread, before the file is written): the world's own
    /// settings go into it in place of any held ones. Only for the world that held them; a save of another world, and every
    /// save of a world that never held them, is left as it is.</summary>
    public static void CorrectSaved(XElement project) {
        if (s_saved is not { } saved || !ReferenceEquals(GameManager.Project, saved.Project)) return;
        CorrectSaved(project, saved.GameInfo, saved.Own);
    }
    /// <summary>Writes <paramref name="own"/> into the saved values of the game-info subsystem. False when that part is not in
    /// the save (logged; nothing is changed).</summary>
    public static bool CorrectSaved(XElement project, string gameInfo, Settings own) {
        var values = project?.Element("Subsystems")?.Elements("Values").FirstOrDefault(v => (string)v.Attribute("Name") == gameInfo);
        if (values is null) { KnifeLog.Warning($"[CS_DM] save has no {gameInfo} values: the arena's environment could not be taken out of it"); return false; }
        Set(values, "EnvironmentBehaviorMode", own.Environment);
        Set(values, "TimeOfDayMode", own.Time);
        Set(values, "AreWeatherEffectsEnabled", own.Weather);
        return true;
    }
    static void Set(XElement values, string name, object value) {
        foreach (var entry in values.Elements("Value").Where(v => (string)v.Attribute("Name") == name)) entry.SetAttributeValue("Value", HumanReadableConverter.ConvertToString(value));
    }
}
