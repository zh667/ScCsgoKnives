using System.Xml.Linq;
using TemplatesDatabase;
namespace Game;

/// <summary>Reject unsupported formats before gameplay, without converting them to legacy stamps.
/// XML hook exceptions are swallowed by the API; an ephemeral error makes Subsystem.Load fail too.</summary>
public static class ScGunSaveGuard {
    public const string ErrorKey = "GunFormatLoadError";
    static InvalidOperationException Refused(string detail) => new(
        "枪械存档格式无法安全读取，请使用支持该格式的模组版本；已拒绝加载和保存，原世界文件未改动：" + detail);

    public static void Validate(ValuesDictionary values) {
        string error = values.GetValue<string>(ErrorKey, null);
        if (error is not null) throw new InvalidOperationException(error);
        if (values.ContainsKey("GunDataLayout")) {
            int stamp = values.GetValue<int>("GunDataLayout");
            if (stamp != GunSpec.DataLayout && stamp != ScGunRegistry.LegacyStamp)
                throw Refused($"GunDataLayout={stamp}");
        }
        if (values.ContainsKey("GunRegistry")) {
            var registry = values.GetValue<ValuesDictionary>("GunRegistry");
            if (registry is null) throw Refused("GunRegistry is null");
            if (!registry.ContainsKey("Schema")) throw Refused("GunRegistry 缺少 Schema（记录格式版本）；不能当作 schema 1/2/3 读取，请提供来源版本或原始备份");
            int schema = registry.GetValue<int>("Schema", 0);
            if (!ScGunRegistry.IsKnownSchema(schema)) throw Refused($"GunRegistry.Schema={schema}");
            // A growth mode name this build does not know means a rule set it cannot honour; refuse before play.
            string mode = registry.GetValue<string>("GrowthMode", null);
            if (mode is not null && !Enum.TryParse<ScGunGrowthMode>(mode, out _)) throw Refused($"GunRegistry.GrowthMode={mode}");
        }
    }

    /// <summary>Marks this world's gun subsystem so its own Load fails before gameplay or autosave. The XML hook's
    /// exceptions are swallowed by the API, so an ephemeral error field is the only thing that reliably stops it.</summary>
    public static void Refuse(XElement project, string detail) {
        var groups = project.Element("Subsystems")?.Elements("Values")
            .Where(e => (string)e.Attribute("Name") == "ScGunBlockBehavior").ToArray() ?? [];
        foreach (var group in groups) {
            group.Elements("Value").Where(v => (string)v.Attribute("Name") == ErrorKey).Remove();
            group.Add(new XElement("Value", new XAttribute("Name", ErrorKey), new XAttribute("Type", "string"), new XAttribute("Value", detail)));
        }
        KnifeLog.Error("gun load refused: " + detail);
    }

    public static bool BeforeLoad(XElement project) {
        var groups = project.Element("Subsystems")?.Elements("Values")
            .Where(e => (string)e.Attribute("Name") == "ScGunBlockBehavior").ToArray() ?? [];
        try {
            if (groups.Length > 1) throw Refused("duplicate ScGunBlockBehavior");
            foreach (var group in groups) {
                // Parse a detached subtree; validation never edits source fields or records.
                var values = new ValuesDictionary(); values.ApplyOverrides(new XElement(group));
                Validate(values);
            }
            return true;
        }
        catch (Exception e) {
            foreach (var group in groups) {
                group.Elements("Value").Where(v => (string)v.Attribute("Name") == ErrorKey).Remove();
                group.Add(new XElement("Value", new XAttribute("Name", ErrorKey),
                    new XAttribute("Type", "string"), new XAttribute("Value", e.Message)));
            }
            KnifeLog.Error("gun format load refused: " + e.Message);
            return false;
        }
    }

    internal static void ValidateSave(bool loaded, int sourceLayout, ScGunRegistry registry) {
        if (!loaded || registry is null) throw Refused("world load did not complete");
        if (sourceLayout != 0 && sourceLayout != GunSpec.DataLayout && sourceLayout != ScGunRegistry.LegacyStamp)
            throw Refused($"GunDataLayout={sourceLayout}");
        if (registry.UnknownSchema) throw Refused("unsupported registry/recovery schema");
    }
}
