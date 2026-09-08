using System.Text.Json;
using Engine;
namespace Game;

/// <summary>Where CS2 itself puts a StatTrak module on each weapon.
///
/// The bone, rotation and offset are Valve's own `stattrak` and `stattrak_legacy` attachments, read out of the
/// 35 model dumps by tools/extract_stattrak_attachments.py; nothing here is estimated or eyeballed. A gun drawn
/// on its HD body takes `stattrak`, one drawn on its legacy body takes `stattrak_legacy`, so fitting a counter
/// never switches a weapon between the two eras or between their UV sets.
///
/// The official module and encoded digit mesh are imported by tools/import_cs2_stattrak.py.
/// ScStatTrakRenderer draws them in first person, third person and dropped-item views.</summary>
public static class ScGunStatTrak {
    /// <summary>Packaged paths the module display needs.</summary>
    public const string ModuleModel = "Models/ScCsgoKnives/stattrak_module";
    public const string ModuleMaterial = "stattrak_module";
    public const string DigitAtlas = "stattrak_digit_atlas";
    /// <summary>Valve's own preview value on the module material. Never a count to display.</summary>
    public const int SchemaPreviewValue = 654321;
    /// <summary>The official display panel is six digits; the record keeps the real number whatever it shows.</summary>
    public const long PanelMaximum = 999999;
    public static string PanelText(long kills) => Math.Clamp(kills, 0, PanelMaximum).ToString("000000", System.Globalization.CultureInfo.InvariantCulture);

    public sealed record Placement(string Bone, Quaternion Rotation, Vector3 Offset) {
        /// <summary>Module Source inches -> parent bone Source inches. Convert axes/units only
        /// after composing the bone: attachment * boneAbsolute * view/world placement.</summary>
        public Matrix Matrix => Matrix.CreateFromQuaternion(Rotation) * Matrix.CreateTranslation(Offset);
    }
    sealed class Entry { public Raw stattrak { get; set; } public Raw stattrak_legacy { get; set; } }
    sealed class Raw { public string bone { get; set; } public float[] rotation { get; set; } public float[] offset { get; set; } }
    sealed class File { public int Version { get; set; } public Dictionary<string, Entry> Guns { get; set; } }

    static readonly Dictionary<string, (Placement Hd, Placement Legacy)> s_placements = Load();
    static Dictionary<string, (Placement, Placement)> Load() {
        var result = new Dictionary<string, (Placement, Placement)>(StringComparer.Ordinal);
        using var stream = typeof(ScGunStatTrak).Assembly.GetManifestResourceStream("Game.AnimationData.gun_stattrak.json");
        var file = JsonSerializer.Deserialize<File>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (file?.Version != 1 || file.Guns is null) throw new InvalidOperationException("Invalid StatTrak attachment catalog");
        static Placement Convert(Raw raw) =>
            raw?.rotation is { Length: 4 } r && raw.offset is { Length: 3 } o && !string.IsNullOrEmpty(raw.bone)
            && r.All(float.IsFinite) && o.All(float.IsFinite)
                ? new Placement(raw.bone, new Quaternion(r[0], r[1], r[2], r[3]), new Vector3(o[0], o[1], o[2])) : null;
        foreach (var (gun, entry) in file.Guns) result[gun] = (Convert(entry.stattrak), Convert(entry.stattrak_legacy));
        // Every model the mod ships must have at least the current-body attachment, or a counter would have no
        // reliable place to sit on it.
        foreach (var spec in GunSpec.All)
            if (!result.TryGetValue(spec.Name, out var pair) || pair.Item1 is null)
                throw new InvalidOperationException("No CS2 stattrak attachment for " + spec.Name);
        return result;
    }

    /// <summary>The official placement for this gun on the body it is actually drawn on.</summary>
    public static Placement For(string asset, bool legacyBody) {
        if (asset is null || !s_placements.TryGetValue(asset, out var pair)) return null;
        return legacyBody ? pair.Legacy ?? pair.Hd : pair.Hd;
    }
    public static bool Has(string asset, bool legacyBody) => For(asset, legacyBody) is not null;

    /// <summary>Whether all official module resources can be loaded by this package.</summary>
    public static bool ModuleAvailable => s_module ??= Probe();
    static bool? s_module;
    static bool Probe() {
        try {
            ContentManager.Get<Engine.Graphics.Texture2D>("Textures/ScCsgoKnives/" + DigitAtlas);
            ContentManager.Get<Engine.Graphics.Texture2D>("Textures/ScCsgoKnives/" + ModuleMaterial);
            ContentManager.Get<ObjModel>(ModuleModel);
            if (ScStatTrakRenderer.Parts.Length != 2) throw new InvalidOperationException("Invalid StatTrak geometry");
            return true;
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("stattrak-module",
                "CS2 StatTrak module assets are not in this package, so the counter is not drawn on the weapon; "
                + "the count is shown in the attribute page and item description. Missing: " + ModuleModel + ".obj, "
                + "Textures/ScCsgoKnives/" + DigitAtlas + ".png (" + e.Message + ")");
            return false;
        }
    }
    /// <summary>Test seam so a headless check can assert both branches without shipping the assets.</summary>
    internal static void ResetProbe() => s_module = null;
}
