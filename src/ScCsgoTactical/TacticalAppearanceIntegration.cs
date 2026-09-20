using System.Reflection;
using Engine;
using NuGet.Versioning;

namespace Game;

// Keep framework-dependent types out of this assembly: the native loader calls GetTypes()
// on every root DLL even when the optional frameworks are not installed.
public static class TacticalAppearanceIntegration {
    public const string Payload = "Integrations/ScCsgoAppearance.bin";
    static bool Enabled(string package, string minimum) => ModsManager.ModList.Any(mod =>
        !mod.IsDisabled && mod.modInfo?.PackageName == package &&
        NuGetVersion.TryParse(mod.modInfo.Version, out var version) &&
        version >= NuGetVersion.Parse(minimum));

    public static void Initialize(ModEntity entity) {
        if (entity == null || !Enabled("ysf.nekomekomodel", "1.1") || !Enabled("ysf.neorxna", "1.4")) return;
        // An old standalone installation still owns its loader. Do not register its hooks twice.
        if (Enabled("zh667.ScCsgoAppearance", "1.0")) return;
        if (entity.Loaders.Any(loader => loader.GetType().FullName == "Game.AppearanceModLoader")) return;
        if (!ModsManager.Dlls.Values.Any(a => a.GetName().Name == "sc-nekomekomodel") ||
            !ModsManager.Dlls.Values.Any(a => a.GetName().Name == "neorxna")) return;

        var assembly = ModsManager.Dlls.Values.FirstOrDefault(a => a.GetName().Name == "ScCsgoAppearance");
        if (assembly == null) {
            byte[] bytes = null;
            entity.GetFile(Payload, stream => bytes = ModsManager.StreamToBytes(stream));
            if (bytes == null) throw new InvalidOperationException("CS战术拓展缺少内置玩家外观组件，请重新安装完整拓展包。");
            assembly = Assembly.Load(bytes);
            assembly.GetTypes(); // Resolve the optional frameworks before publishing the assembly.
            ModsManager.Dlls[assembly.FullName] = assembly;
        }
        entity.HandleAssembly(assembly);
        Log.Information("CS战术拓展：已启用内置 T/CT 玩家外观。");
    }
}
