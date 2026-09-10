using System.IO;
using System.Text.Json;
using Engine;
namespace Game;

/// <summary>Separate settings so adding a preset never invalidates the legacy
/// composition tuning hash or overwrites a player's camera/lighting settings.</summary>
public static class ScGunplaySettings {
    public static string Path=>ScLocalSettings.PathFor("ScCsgoGunplay.json");
    public static bool Enabled=true;
    public static string Diagnostics="off";
    sealed class Settings {
        public int Version {get;set;}=1;
        public string Preset {get;set;}="survival";
        public string Diagnostics {get;set;}="off";
    }
    public static string DiagnosticMode(string value)=>value is "off" or "summary" or "sampled"?value:"off";
    public static void Load() {
        try {
            if(!Storage.FileExists(Path)) {
                Enabled=true;Diagnostics="off";
                // A first-run write failure must not silently disable the approved gunplay preset.
                try {
                    Storage.CreateDirectory(Storage.GetDirectoryName(Path));
                    ScUiSettings.WriteAtomic(Storage.GetSystemPath(Path),JsonSerializer.SerializeToUtf8Bytes(new Settings(),new JsonSerializerOptions{WriteIndented=true}));
                } catch(Exception e) { KnifeLog.Warning("[CS_UI_0413] cannot create gunplay defaults; survival preset retained: "+e); }
            } else {
                using var stream=Storage.OpenFile(Path,OpenFileMode.Read);
                var settings=JsonSerializer.Deserialize<Settings>(stream);
                if(settings?.Version!=1 || settings.Preset is not ("survival" or "classic")) throw new InvalidDataException("Expected Version 1 and Preset survival/classic");
                Enabled=settings.Preset=="survival";
                Diagnostics="off"; // Release policy also overrides earlier sampled/summary settings without rewriting player files.
            }
            KnifeLog.Trace("Gunplay preset: "+(Enabled?"survival v1 (approved 35-gun handling)":"classic (prior GunNumbers respected)"));
            KnifeLog.Trace("Gunplay diagnostics: "+Diagnostics+"; sampled details are rate-limited, summaries include every completed shot.");
        } catch(Exception e) {
            Enabled=false;Diagnostics="off";KnifeLog.Warning("Cannot load gunplay settings; file preserved, using classic for this session: "+e.Message);
        }
    }
}
