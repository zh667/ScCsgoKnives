using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;
namespace Game;

/// <summary>One touch button's saved layout. Position is normalised to the controls area, never raw pixels, so a
/// rotation, a resolution change or a different UI scale moves it with the screen instead of off it.</summary>
public sealed class ScButtonLayout {
    public bool Enabled { get; set; } = true;
    /// <summary>Centre of the button as a fraction of the controls area, 0 = left/top, 1 = right/bottom.</summary>
    public float X { get; set; }
    public float Y { get; set; }
    /// <summary>1 = the original 104x60. Clamped to 0.5 - 2.0, and never below a 48-unit touch target.</summary>
    public float Scale { get; set; } = 1f;
    public float Background { get; set; } = .35f;
    public float Foreground { get; set; } = .9f;
    public ScButtonLayout Copy() => (ScButtonLayout)MemberwiseClone();
    public void Normalize() {
        X = float.IsFinite(X) ? Math.Clamp(X, 0, 1) : .5f;
        Y = float.IsFinite(Y) ? Math.Clamp(Y, 0, 1) : .5f;
        Scale = float.IsFinite(Scale) ? Math.Clamp(Scale, .5f, 2f) : 1f;
        Background = float.IsFinite(Background) ? Math.Clamp(Background, .1f, 1f) : .35f;
        Foreground = float.IsFinite(Foreground) ? Math.Clamp(Foreground, .4f, 1f) : .9f;
    }
}

/// <summary>The mod's touch functions, by stable id. The id is what the layout file stores, so renaming a label or
/// reordering the list can never move somebody's silencer button onto their grenade.</summary>
public static class ScGunFunctions {
    public const string Fire = "fire", Reload = "reload", Scope = "scope", Silencer = "silencer", Burst = "burst",
                        RevolverAlt = "revolver_alt", Inspect = "inspect", KnifeHeavy = "knife_heavy",
                        ThrowStrong = "throw_strong", ThrowWeak = "throw_weak", Plant = "plant_c4", C4Timer = "c4_timer", Voice="agent_voice";
    public static readonly string[] All = [Reload, Scope, Silencer, Burst, RevolverAlt, Inspect, KnifeHeavy, ThrowStrong, ThrowWeak, Fire, Plant, C4Timer, Voice];
    public static string Label(string id) => id switch {
        Voice=>"语音", Fire => "开火", Reload => "换弹", Scope => "开镜", Silencer => "消音器", Burst => "连发", RevolverAlt => "速射",
        Inspect => "检视", KnifeHeavy => "重刀", ThrowStrong => "强投", ThrowWeak => "轻投", Plant => "放置 C4", C4Timer => "C4 设时", _ => id,
    };
    /// <summary>Buttons that never appear at the same time share a default row; the rows are what the original
    /// two-button layout used, so an existing player's thumb finds the same places.</summary>
    static int DefaultRow(string id) => id switch {
        Reload or KnifeHeavy or ThrowWeak or Plant => 0,
        Scope or Silencer or Burst or RevolverAlt or ThrowStrong or C4Timer => 1,
        _ => 2,
    };
    /// <summary>The original layout expressed in the normalised space: 160 units in from the side and
    /// 150 + row*68 up from the bottom of an 850x478 controls area, measured to the button's centre.</summary>
    public static ScButtonLayout Default(string id, bool leftHanded) {
        const float width = 850f, height = 850f * 9f / 16f;
        float centreX = leftHanded ? 160f + 52f : width - 160f - 52f;
        float centreY = height - (150f + DefaultRow(id) * 68f) - 30f;
        return id==Voice?new ScButtonLayout{X=leftHanded?.15f:.85f,Y=.18f,Scale=.8f}:id == Fire ? new ScButtonLayout { X = leftHanded ? .13f : .87f, Y = .7f, Enabled = false }
            : new ScButtonLayout { X = centreX / width, Y = centreY / height };
    }
}

/// <summary>Local, versioned interface settings: the mod's touch buttons, the kill feed and the gun crosshair.
///
/// Kept in its own file so it can never invalidate the legacy composition tuning hash or overwrite a player's
/// camera and lighting settings, and read on this device only - it is never written into a world and never sent
/// to another player. A file this build cannot read is left exactly as it is and the session falls back to the
/// defaults, so a newer layout is never silently rewritten into an older one.</summary>
public static class ScUiSettings {
    public static string Path => ScLocalSettings.PathFor("ScCsgoUi.json");
    public const int Version = 1;

    public static bool SimpleMaterials;
    public static ScHudPosition AmmoHud=new();
    /// <summary>The CS protection HUD (current-direction-20260929): its own position, apart from the ammo HUD.</summary>
    public static ScHudPosition ArmorHud=ScArmorHud.DefaultPosition();
    public static bool ArmorHudEnabled=true;
    /// <summary>Defaults for worlds that have no natural-squad rules yet (new worlds, or first load after an update).
    /// Each world keeps its own rules afterwards; changing these never rewrites another world.</summary>
    public static bool NaturalEnemies=true;
    public static int EnemyGraceDays=ScEnemySpawnPolicy.GraceDays;
    public static ScEnemyDensity EnemyDensity=ScEnemyDensity.Standard;
    public static ScEnemyRules EnemyDefaults=>new ScEnemyRules(NaturalEnemies,EnemyGraceDays,EnemyDensity).Normalize();
    public static bool CustomButtons = true;
    public static bool ButtonOnlyFire;
    public static readonly Dictionary<int,int> C4Fuses=[];
    public static int C4Fuse(int player) => Math.Clamp(C4Fuses.GetValueOrDefault(player,20),5,300);
    public static bool KillFeed = true;
    /// <summary>CS2-style direction marks when CS damage actually hurts this player; in creative mode the same switch shows
    /// the weaker preview when a CS attack reached the player and only creative invulnerability kept it from hurting (never
    /// for blocked, filtered or missed attacks). Round 12 (2026-10-01 user request) merged the former separate creative
    /// switch into this one; its old file key is ignored.</summary>
    public static bool DamageIndicator = true;
    /// <summary>Show the predicted path while preparing one's own throw.</summary>
    public static bool GrenadePreview = true;
    public static bool KillSound = true;
    public static bool GunCrosshair = true;
    public static bool SniperHipCrosshair = true;
    public static ScStarterPlan StarterPlan = ScStarterPlan.None;
    public static string CrosshairStyle = StyleVanilla;
    public static Color CrosshairColor = Color.White;
    public static ScCrosshairShape CrosshairShape = new();
    /// <summary>False when the file could not be read: the session runs on defaults and nothing is written back.</summary>
    public static bool Writable { get; private set; } = true;

    public const string StyleVanilla = "vanilla", StyleCross = "cross", StyleDot = "dot";
    public static readonly string[] Styles = [StyleVanilla, StyleCross, StyleDot];
    public static string StyleLabel(string style) => style switch {
        StyleCross => "十字线", StyleDot => "圆点", _ => "原版样式",
    };
    public static readonly (string Name, Color Color)[] Presets = [
        ("白", Color.White), ("绿", new Color(90, 255, 120)), ("青", new Color(90, 220, 255)),
        ("黄", new Color(255, 225, 90)), ("红", new Color(255, 95, 85)),
    ];

    static readonly Dictionary<string, ScButtonLayout> s_right = [];
    static readonly Dictionary<string, ScButtonLayout> s_left = [];
    /// <summary>The set for the hand the player is using. The other hand's layout is kept untouched, so switching
    /// back and forth never overwrites one with the other.</summary>
    public static Dictionary<string, ScButtonLayout> Hand(bool leftHanded) => leftHanded ? s_left : s_right;
    public static Dictionary<string, ScButtonLayout> Current => Hand(SettingsManager.LeftHandedLayout);
    public static ScButtonLayout Layout(string id) => Layout(Current, id, SettingsManager.LeftHandedLayout);
    public static ScButtonLayout Layout(Dictionary<string, ScButtonLayout> set, string id, bool leftHanded) {
        if (!set.TryGetValue(id, out var layout)) set[id] = layout = ScGunFunctions.Default(id, leftHanded);
        return layout;
    }
    public static void ResetHand(bool leftHanded) {
        var set = Hand(leftHanded); set.Clear();
        foreach (string id in ScGunFunctions.All) set[id] = ScGunFunctions.Default(id, leftHanded);
    }
    public static void ResetAll() {
        AmmoHud=new();ArmorHud=ScArmorHud.DefaultPosition();ArmorHudEnabled=true;NaturalEnemies=true;EnemyGraceDays=ScEnemySpawnPolicy.GraceDays;EnemyDensity=ScEnemyDensity.Standard;
        ScGunBindings.Reset();
        ScGamepadBindings.Keys.Clear(); ScGamepadBindings.Threshold = .5f;
        ResetHand(false); ResetHand(true);
        SimpleMaterials = ScResourcePolicy.Edition == "Optimized512";
        CustomButtons = true; KillFeed = true; KillSound = true; GunCrosshair = true; SniperHipCrosshair = true; DamageIndicator = true; GrenadePreview = true;
        ButtonOnlyFire = false;
        C4Fuses.Clear();
        CrosshairStyle = StyleVanilla; CrosshairColor = Color.White;
        StarterPlan = ScStarterPlan.None;
        CrosshairShape = new();
    }
    /// <summary>A deep copy of one hand's layout, for an editor that must not change anything until it is saved.</summary>
    public static Dictionary<string, ScButtonLayout> CopyHand(bool leftHanded) {
        var copy = new Dictionary<string, ScButtonLayout>(StringComparer.Ordinal);
        foreach (string id in ScGunFunctions.All) copy[id] = Layout(Hand(leftHanded), id, leftHanded).Copy();
        return copy;
    }
    public static void ReplaceHand(bool leftHanded, Dictionary<string, ScButtonLayout> layouts) {
        var set = Hand(leftHanded);
        foreach (string id in ScGunFunctions.All) if (layouts.TryGetValue(id, out var l)) { var c = l.Copy(); c.Normalize(); set[id] = c; }
    }

    sealed class File {
        public ScHudPosition AmmoHud {get;set;}=new();
        public ScHudPosition ArmorHud {get;set;}
        public bool ArmorHudEnabled {get;set;}=true;
        public bool NaturalEnemies {get;set;}=true;
        public int EnemyGraceDays {get;set;}=ScEnemySpawnPolicy.GraceDays;
        public int EnemyDensity {get;set;}=(int)ScEnemyDensity.Standard;
        public int Version { get; set; } = ScUiSettings.Version;
        public bool? SimpleMaterials { get; set; }
        public bool CustomButtonsEnabled { get; set; } = true;
        public bool ButtonOnlyFire { get; set; }
        public Dictionary<int,int> C4Fuses {get;set;}=[];
        public bool KillFeedEnabled { get; set; } = true;
        public bool DamageIndicatorEnabled { get; set; } = true;
        public bool GrenadePreviewEnabled { get; set; } = true;
        public bool KillSoundEnabled { get; set; } = true;
        public bool GunCrosshairEnabled { get; set; } = true;
        public bool SniperHipCrosshair { get; set; } = true;
        public ScStarterPlan StarterPlan { get; set; } = ScStarterPlan.None;
        public string GunCrosshairStyle { get; set; } = StyleVanilla;
        public string GunCrosshairColor { get; set; } = "255,255,255";
        public ScCrosshairShape CrosshairShape { get; set; } = new();
        public Dictionary<string, ScButtonLayout> Buttons { get; set; } = [];
        public Dictionary<string, ScButtonLayout> ButtonsLeftHanded { get; set; } = [];
        public Dictionary<string, string> KeyBindings { get; set; } = [];
        public Dictionary<string, string> GamepadBindings { get; set; } = [];
        public float GamepadTriggerThreshold { get; set; } = .5f;
    }
    static readonly JsonSerializerOptions s_json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static string ColorText(Color c) => string.Create(CultureInfo.InvariantCulture, $"{c.R},{c.G},{c.B}");
    public static bool TryParseColor(string text, out Color color) {
        color = Color.White;
        string[] parts = (text ?? "").Split(',');
        if (parts.Length != 3) return false;
        var bytes = new byte[3];
        for (int i = 0; i < 3; i++) if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes[i])) return false;
        color = new Color(bytes[0], bytes[1], bytes[2]);
        return true;
    }

    public static void Load() {
        ResetAll();
        Writable = true;
        try {
            if (!Storage.FileExists(Path)) { Save(); return; }
            File file;
            using (var stream = Storage.OpenFile(Path, OpenFileMode.Read)) file = JsonSerializer.Deserialize<File>(stream);
            if (file is null) throw new InvalidDataException("empty settings file");
            if (file.Version != Version) throw new InvalidDataException($"Version {file.Version} is not {Version}");
            SimpleMaterials = file.SimpleMaterials ?? (ScResourcePolicy.Edition == "Optimized512");
            AmmoHud=(file.AmmoHud??new()).Normalize();ArmorHud=(file.ArmorHud??ScArmorHud.DefaultPosition()).Normalize();ArmorHudEnabled=file.ArmorHudEnabled;NaturalEnemies=file.NaturalEnemies;
            var enemy=new ScEnemyRules(file.NaturalEnemies,file.EnemyGraceDays,(ScEnemyDensity)file.EnemyDensity).Normalize();EnemyGraceDays=enemy.GraceDays;EnemyDensity=enemy.Density;
            CustomButtons = file.CustomButtonsEnabled;
            ButtonOnlyFire = file.ButtonOnlyFire;
            foreach(var p in file.C4Fuses??[])if(p.Key>=0)C4Fuses[p.Key]=Math.Clamp(p.Value,5,300);
            foreach (var (id, key) in file.GamepadBindings ?? [])
                if (ScGunFunctions.All.Contains(id) && ScGamepadBindings.Valid(key)) ScGamepadBindings.Keys[id] = key;
            ScGamepadBindings.Threshold = float.IsFinite(file.GamepadTriggerThreshold) ? Math.Clamp(file.GamepadTriggerThreshold, .15f, .9f) : .5f;
            foreach (var (id, key) in file.KeyBindings ?? [])
                if (ScGunFunctions.All.Contains(id) && ScGunBindings.Valid(key)) ScGunBindings.Keys[id] = key;
            KillFeed = file.KillFeedEnabled; DamageIndicator = file.DamageIndicatorEnabled; GrenadePreview = file.GrenadePreviewEnabled;
            KillSound = file.KillSoundEnabled;
            GunCrosshair = file.GunCrosshairEnabled;
            SniperHipCrosshair = file.SniperHipCrosshair;
            StarterPlan = Enum.IsDefined(file.StarterPlan)?file.StarterPlan:ScStarterPlan.None;
            CrosshairStyle = Array.IndexOf(Styles, file.GunCrosshairStyle) >= 0 ? file.GunCrosshairStyle : StyleVanilla;
            if (TryParseColor(file.GunCrosshairColor, out var parsed)) CrosshairColor = parsed;
            CrosshairShape = (file.CrosshairShape ?? new()).Normalize();
            // A missing button only takes its own default; the rest of the file is never rewritten around it.
            foreach (var (id, layout) in file.Buttons) if (Array.IndexOf(ScGunFunctions.All, id) >= 0 && layout is not null) { layout.Normalize(); s_right[id] = layout; }
            foreach (var (id, layout) in file.ButtonsLeftHanded) if (Array.IndexOf(ScGunFunctions.All, id) >= 0 && layout is not null) { layout.Normalize(); s_left[id] = layout; }
        }
        catch (Exception e) {
            ResetAll();
            Writable = false;
            KnifeLog.Warning("Cannot read the CS gun interface settings; the file is preserved and this session uses the defaults: " + e.Message);
        }
    }

    /// <summary>Writes the current settings. Returns false and keeps the last valid file when the write fails or
    /// the file on disk is a version this build did not understand.</summary>
    public static bool Save() {
        if (!Writable) return false;
        try {
            var file = new File {
                AmmoHud=AmmoHud.Copy().Normalize(),ArmorHud=ArmorHud.Copy().Normalize(),ArmorHudEnabled=ArmorHudEnabled,NaturalEnemies=NaturalEnemies,EnemyGraceDays=EnemyDefaults.GraceDays,EnemyDensity=(int)EnemyDefaults.Density,
                SimpleMaterials = SimpleMaterials,
                KeyBindings = new(ScGunBindings.Keys),
                GamepadBindings = new(ScGamepadBindings.Keys), GamepadTriggerThreshold = ScGamepadBindings.Threshold,
                CustomButtonsEnabled = CustomButtons, KillFeedEnabled = KillFeed, KillSoundEnabled = KillSound, DamageIndicatorEnabled = DamageIndicator, GrenadePreviewEnabled = GrenadePreview,
                ButtonOnlyFire = ButtonOnlyFire,
                C4Fuses = new(C4Fuses),
                CrosshairShape = CrosshairShape.Normalize(),
                GunCrosshairEnabled = GunCrosshair, GunCrosshairStyle = CrosshairStyle, GunCrosshairColor = ColorText(CrosshairColor),
                SniperHipCrosshair = SniperHipCrosshair,
                StarterPlan = StarterPlan,
            };
            foreach (string id in ScGunFunctions.All) {
                file.Buttons[id] = Layout(s_right, id, false);
                file.ButtonsLeftHanded[id] = Layout(s_left, id, true);
            }
            Storage.CreateDirectory(Storage.GetDirectoryName(Path));
            WriteAtomic(Storage.GetSystemPath(Path), ScCompatibility.PreserveUiSettings(JsonSerializer.SerializeToUtf8Bytes(file, s_json),Storage.GetSystemPath(Path),ScGunFunctions.All));
            return true;
        }
        catch (Exception e) {
            KnifeLog.Warning("[CS_UI_0413] settings save failed (previous file preserved), path=" + Path + ": " + e);
            return false;
        }
    }
    /// <summary>Same-directory rename after a flushed, verified temporary write. Storage.MoveFile
    /// deletes its destination first, so it must not be used for this replacement.</summary>
    internal static void WriteAtomic(string path, byte[] bytes, Action beforeReplace = null) {
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(bytes); stream.Flush(true);
            }
            if (!System.IO.File.ReadAllBytes(pending).AsSpan().SequenceEqual(bytes))
                throw new IOException("Settings temporary file verification failed");
            beforeReplace?.Invoke();
            System.IO.File.Move(pending, path, overwrite: true);
        }
        finally { if (System.IO.File.Exists(pending)) System.IO.File.Delete(pending); }
    }
}
