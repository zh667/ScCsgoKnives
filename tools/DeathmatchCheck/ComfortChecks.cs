// 2026-10-21 player requests for the deathmatch: no red screen when hurt, nobody hungry or cold, and a static, weatherless
// noon by default - without the world's own settings ever being saved changed. Engine objects are built bare (no Load), as
// the arena itself would have them after loading; the save is written and read back by the engine's own serialisers.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using GameEntitySystem;
using TemplatesDatabase;
using Game;

static partial class Program {
    static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    /// <summary>A survival world as created (living, changing time, weather on); the engine's constructor needs the language
    /// files for its palette names, so the fields its own save writes are set here.</summary>
    static WorldSettings NewWorld() {
        var w = Bare<WorldSettings>(); w.Name = ""; w.OriginalSerializationVersion = ""; w.Seed = ""; w.BlocksTextureName = "";
        w.GameMode = GameMode.Survival; w.EnvironmentBehaviorMode = EnvironmentBehaviorMode.Living; w.TimeOfDayMode = TimeOfDayMode.Changing;
        w.AreWeatherEffectsEnabled = true; w.AreSeasonsChanging = true; w.YearDays = 24; w.IsAdventureRespawnAllowed = true; w.AreAdventureSurvivalMechanicsEnabled = true;
        w.AreSupernaturalCreaturesEnabled = true; w.IsFriendlyFireEnabled = true; w.Palette = new WorldPalette(new ValuesDictionary { { "Names", string.Join(";", Enumerable.Range(0, 16).Select(i => "Colour" + (char)('A' + i))) } });
        return w;
    }
    /// <summary>An arena world (enabled, on the authority) whose match is in <paramref name="phase"/>.</summary>
    static (Project P, SubsystemScDeathmatch Dm, SubsystemGameInfo Info, DmMatch Match) ComfortWorld(DmPhase phase, bool fixedEnvironment = true) {
        var p = new Project(); var info = new SubsystemGameInfo { WorldSettings = NewWorld() };
        var dm = new SubsystemScDeathmatch();
        foreach (var s in new Subsystem[] { info, dm }) { s.m_project = p; p.m_subsystems.Add(s); }
        typeof(SubsystemScDeathmatch).GetField("m_info", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dm, info);
        typeof(SubsystemScDeathmatch).GetProperty("Enabled").SetValue(dm, true);
        var match = new DmMatch(); match.SetRules(new DmRules { FixedEnvironment = fixedEnvironment });
        if (phase != DmPhase.Editing) match.OpenLobby(0);
        typeof(DmMatch).GetProperty("Phase").SetValue(match, phase);
        typeof(SubsystemScDeathmatch).GetProperty("Match").SetValue(dm, match);
        return (p, dm, info, match);
    }
    static (ComponentPlayer Player, ComponentHealth Health, ComponentVitalStats Vitals) ComfortPlayer(Project p, bool player = true) {
        var self = Bare<ComponentPlayer>(); var health = new ComponentHealth(); var vitals = Bare<ComponentVitalStats>();
        var e = Bare<Entity>(); e.m_project = p; e.m_components = player ? [self, health, vitals] : [health, vitals];
        foreach (var c in e.m_components) c.m_entity = e;
        return (self, health, vitals);
    }
    static string Settings(WorldSettings w) => $"{w.EnvironmentBehaviorMode}/{w.TimeOfDayMode}/{w.AreWeatherEffectsEnabled}";

    static void Comfort() {
        // ---- the host's rule: on by default, also for the rules saved before it existed
        Test("E01", "the arena environment rule is on by default", new DmRules().FixedEnvironment);
        bool oldRead = DmRules.TryDecode("{\"Minutes\":12,\"Grenades\":true}", out var old);
        Test("E02", "rules saved before the rule existed read as on, the rest unchanged", oldRead && old.FixedEnvironment && old.Minutes == 12 && old.Grenades);
        bool offRead = DmRules.TryDecode(new DmRules { FixedEnvironment = false }.Encode(), out var off);
        Test("E03", "the host's 'follow the world' survives a round trip", offRead && !off.FixedEnvironment);
        var saved = new DmMatch(); saved.SetRules(new DmRules { Minutes = 7 });
        string earlier = saved.Encode(0).Replace(",\"FixedEnvironment\":true", "");
        bool matchRead = DmMatch.TryDecode(earlier, out var restored);
        Test("E04", "a match saved by an earlier build loads with the rule on", !earlier.Contains("FixedEnvironment") && matchRead && restored.Rules.FixedEnvironment && restored.Rules.Minutes == 7, earlier);

        // ---- when the environment is held
        foreach (var (phase, rule, expected, i) in new[] { (DmPhase.Editing, true, false), (DmPhase.Lobby, true, true), (DmPhase.Countdown, true, true), (DmPhase.Running, true, true),
            (DmPhase.Results, true, true), (DmPhase.Lobby, false, false), (DmPhase.Running, false, false) }.Select((c, i) => (c.Item1, c.Item2, c.Item3, i))) {
            var w = ComfortWorld(phase, rule);
            Test($"E05{(char)('a' + i)}", $"environment held in {phase} with the rule {(rule ? "on" : "off")}: {expected}", w.Dm.HoldsEnvironment == expected);
        }
        var frozen = ComfortWorld(DmPhase.Running);
        typeof(SubsystemScDeathmatch).GetProperty("Frozen").SetValue(frozen.Dm, "newer data");
        Test("E06", "a world whose deathmatch data is frozen holds nothing", !frozen.Dm.HoldsEnvironment);

        // ---- holding and giving back
        var a = ComfortWorld(DmPhase.Lobby); var world = a.Info.WorldSettings; string own = Settings(world);
        a.Dm.ArenaEnvironment.Update(a.P, a.Info, a.Dm.HoldsEnvironment);
        Test("E07", "the lobby holds a static world without weather at noon", Settings(world) == "Static/Day/False" && own == "Living/Changing/True", Settings(world));
        a.Dm.ArenaEnvironment.Update(a.P, a.Info, true);
        Test("E08", "holding again keeps the world's own settings, not the held ones", a.Dm.ArenaEnvironment.Own is { } kept && $"{kept.Environment}/{kept.Time}/{kept.Weather}" == own);
        a.Match.Edit(1);
        a.Dm.ArenaEnvironment.Update(a.P, a.Info, a.Dm.HoldsEnvironment);
        Test("E09", "back to map editing gives the world its own settings back", Settings(world) == own && a.Dm.ArenaEnvironment.Own is null, Settings(world));
        var creative = ComfortWorld(DmPhase.Lobby); var cw = creative.Info.WorldSettings;
        cw.EnvironmentBehaviorMode = EnvironmentBehaviorMode.Static; cw.TimeOfDayMode = TimeOfDayMode.Night; cw.AreWeatherEffectsEnabled = false;
        creative.Dm.ArenaEnvironment.Update(creative.P, creative.Info, true); bool noon = Settings(cw) == "Static/Day/False";
        creative.Dm.ArenaEnvironment.Release(creative.Info);
        Test("E10", "a world with settings of its own (eternal night) gets exactly them back", noon && Settings(cw) == "Static/Night/False", Settings(cw));

        // ---- what is saved meanwhile: the engine's own writers, the correction, the engine's own reader
        var b = ComfortWorld(DmPhase.Running); var held = b.Info.WorldSettings;
        b.Dm.ArenaEnvironment.Update(b.P, b.Info, true);
        XElement Save() {
            var subsystems = new ValuesDictionary(); var gameInfo = new ValuesDictionary(); held.Save(gameInfo, false); subsystems.SetValue("GameInfo", gameInfo);
            var other = new ValuesDictionary(); other.SetValue("EnvironmentBehaviorMode", EnvironmentBehaviorMode.Static); subsystems.SetValue("ScDeathmatch", other);
            var project = new XElement("Project"); var node = new XElement("Subsystems"); project.Add(node); subsystems.Save(node); return project;
        }
        WorldSettings Read(XElement project) {
            var all = new ValuesDictionary(); all.ApplyOverrides(project.Element("Subsystems"));
            var read = NewWorld(); read.Load(all.GetValue<ValuesDictionary>("GameInfo")); return read;
        }
        string Other(XElement project) => (string)project.Element("Subsystems").Elements("Values").Single(v => (string)v.Attribute("Name") == "ScDeathmatch").Element("Value").Attribute("Value");
        var raw = Save();
        Test("E11", "fixture: a save taken during the hold carries the held settings", Settings(Read(raw)) == "Static/Day/False", Settings(Read(raw)));
        bool corrected = DmEnvironment.CorrectSaved(raw, "GameInfo", b.Dm.ArenaEnvironment.Own.Value);
        Test("E12", "the corrected save reads back as the world's own settings; the live world stays held; nothing else touched",
            corrected && Settings(Read(raw)) == "Living/Changing/True" && Settings(held) == "Static/Day/False" && Other(raw) == "Static", $"{Settings(Read(raw))} live {Settings(held)} other {Other(raw)}");
        Test("E13", "a save without the game-info values is reported, not invented", !DmEnvironment.CorrectSaved(new XElement("Project", new XElement("Subsystems")), "GameInfo", b.Dm.ArenaEnvironment.Own.Value));
        var previous = GameManager.m_project;
        try {
            GameManager.m_project = b.P; var mine = Save(); DmEnvironment.CorrectSaved(mine);
            GameManager.m_project = new Project(); var another = Save(); DmEnvironment.CorrectSaved(another);
            GameManager.m_project = b.P; b.Dm.ArenaEnvironment.Release(b.Info); var after = Save(); DmEnvironment.CorrectSaved(after);
            Test("E14", "the engine's save hook corrects the world that held, also after the hold ended, and no other world",
                Settings(Read(mine)) == "Living/Changing/True" && Settings(Read(another)) == "Static/Day/False" && Settings(Read(after)) == "Living/Changing/True", $"{Settings(Read(mine))} / {Settings(Read(another))} / {Settings(Read(after))}");
            b.Dm.ArenaEnvironment.Update(b.P, b.Info, true); DmEnvironment.Forget(b.P); var gone = Save(); DmEnvironment.CorrectSaved(gone);
            Test("E15", "a world that is gone is not corrected any more", Settings(Read(gone)) == "Static/Day/False");
        }
        finally { GameManager.m_project = previous; }

        // ---- the players: no red screen, nobody hungry or cold - for governed players only
        var loader = new DeathmatchModLoader();
        foreach (var (phase, governed, i) in new[] { (DmPhase.Running, true, 'a'), (DmPhase.Lobby, true, 'b'), (DmPhase.Editing, false, 'c') }) {
            var w = ComfortWorld(phase); var you = ComfortPlayer(w.P);
            float red = .8f, model = 1; bool pain = true; int flash = 6;
            loader.ChangeVisualEffectOnInjury(you.Health, 1, ref red, ref pain, ref flash, ref model);
            Test($"E16{i}", $"{phase}: a hurt player's red screen {(governed ? "is cleared; sound, bar flash and body tint stay" : "is the game's own")}",
                (governed ? red == 0 : red == .8f) && pain && flash == 6 && model == 1, $"red {red}");
            float food = .05f, temperature = 2f, dt = .016f; bool skip = false;
            loader.OnVitalStatsUpdateFood(you.Vitals, ref food, ref dt, ref skip);
            loader.OnVitalStatsUpdateTemperature(you.Vitals, ref temperature, ref dt, ref skip);
            Test($"E17{i}", $"{phase}: food and body temperature {(governed ? "are held where survival mechanics are off (0.9, 12)" : "are the game's own")}",
                (governed ? food == DmFixed.Food && temperature == DmFixed.Temperature : food == .05f && temperature == 2f) && !skip && dt == .016f, $"{food} {temperature}");
        }
        var c = ComfortWorld(DmPhase.Running); var creature = ComfortPlayer(c.P, player: false);
        float tint = .7f, m = 1; bool s = true; int f = 3;
        loader.ChangeVisualEffectOnInjury(creature.Health, 1, ref tint, ref s, ref f, ref m);
        Test("E18", "a creature in an arena world keeps the game's own injury effects", tint == .7f);
        Test("E19", "the deathmatch overrides the engine hooks of all three (registration: the real-game run)", new[] { "ChangeVisualEffectOnInjury", "OnVitalStatsUpdateFood", "OnVitalStatsUpdateTemperature", "OnProjectXmlSaved" }.All(h => typeof(DeathmatchModLoader).GetMethod(h)?.DeclaringType == typeof(DeathmatchModLoader)));
    }
}
