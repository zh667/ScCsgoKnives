using System.IO;
using System.Text.Json;
using Engine;
namespace Game;

/// <summary>Separate settings so adding a preset never invalidates the legacy
/// composition tuning hash or overwrites a player's camera/lighting settings.</summary>
public static class ScGunplaySettings {
    public const string Path="app:/ScCsgoGunplay.json";
    public static bool Enabled=true;
    public static string Diagnostics="sampled";
    sealed class Settings {
        public int Version {get;set;}=1;
        public string Preset {get;set;}="survival";
        public string Diagnostics {get;set;}="sampled";
    }
    public static string DiagnosticMode(string value)=>value is "off" or "summary" or "sampled"?value:"off";
    public static void Load() {
        try {
            if(!Storage.FileExists(Path)) {
                Enabled=true;Diagnostics="sampled";
                using var stream=Storage.OpenFile(Path,OpenFileMode.Create);
                JsonSerializer.Serialize(stream,new Settings(),new JsonSerializerOptions{WriteIndented=true});
            } else {
                using var stream=Storage.OpenFile(Path,OpenFileMode.Read);
                var settings=JsonSerializer.Deserialize<Settings>(stream);
                if(settings?.Version!=1 || settings.Preset is not ("survival" or "classic")) throw new InvalidDataException("Expected Version 1 and Preset survival/classic");
                Enabled=settings.Preset=="survival";
                Diagnostics=DiagnosticMode(settings.Diagnostics);
                if(Diagnostics!=settings.Diagnostics) KnifeLog.Warning("Unknown gun Diagnostics value; diagnostics disabled, gameplay preset unchanged, file preserved.");
            }
            KnifeLog.Information("Gunplay preset: "+(Enabled?"survival v1 (approved 35-gun handling)":"classic (prior GunNumbers respected)"));
            KnifeLog.Information("Gunplay diagnostics: "+Diagnostics+"; sampled details are rate-limited, summaries include every completed shot.");
        } catch(Exception e) {
            Enabled=false;Diagnostics="off";KnifeLog.Warning("Cannot load gunplay settings; file preserved, using classic for this session: "+e.Message);
        }
    }
}
