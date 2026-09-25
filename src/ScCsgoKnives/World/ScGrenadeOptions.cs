using System.Text.Json;
using System.IO;
using Engine;
namespace Game;
/// <summary>Separate device file so historical UI settings writers cannot erase this choice.</summary>
public static class ScGrenadeOptions {
    public static bool Quick {get;set;}=true;
    public static bool Writable {get;private set;}=true;
    static string Path=>ScLocalSettings.PathFor("ScCsgoGrenades.json");
    static ScGrenadeOptions(){try{if(Storage.FileExists(Path)){using var s=Storage.OpenFile(Path,OpenFileMode.Read);using var json=JsonDocument.Parse(s);if(json.RootElement.GetProperty("Version").GetInt32()!=1)throw new InvalidDataException();Quick=json.RootElement.GetProperty("Quick").GetBoolean();}}catch{Writable=false;}}
    public static bool Save(){if(!Writable)return false;try{ScUiSettings.WriteAtomic(Storage.GetSystemPath(Path),JsonSerializer.SerializeToUtf8Bytes(new{Version=1,Quick}));return true;}catch{return false;}}
}
