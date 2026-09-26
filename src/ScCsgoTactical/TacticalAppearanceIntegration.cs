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
        var nmm = ModsManager.Dlls.Values.FirstOrDefault(a => a.GetName().Name == "sc-nekomekomodel");
        if (nmm == null ||
            !ModsManager.Dlls.Values.Any(a => a.GetName().Name == "neorxna")) return;

        var assembly = ModsManager.Dlls.Values.FirstOrDefault(a => a.GetName().Name == "ScCsgoAppearance");
        if (assembly == null) {
            byte[] bytes = null;
            entity.GetFile(Payload, stream => bytes = ModsManager.StreamToBytes(stream));
            if (bytes == null) throw new InvalidOperationException("CS战术拓展缺少内置玩家外观组件，请重新安装完整拓展包。");
            assembly = Assembly.Load(bytes);
            // The author's 1.1 package has assembly version 0.0.0.0. Our old, otherwise
            // equivalent source build used the SDK default 1.0.0.0. Bind that one old
            // identity only for this adapter; never rewrite the provider or global DLL map.
            ResolveEventHandler sourceBuildResolver = null;
            if (nmm.FullName == "sc-nekomekomodel, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null") {
                sourceBuildResolver = (_, request) =>
                    ReferenceEquals(request.RequestingAssembly, assembly) &&
                    request.Name == "sc-nekomekomodel, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"
                        ? nmm : null;
                AppDomain.CurrentDomain.AssemblyResolve += sourceBuildResolver;
            }
            try { assembly.GetTypes(); } // Resolve before publishing; retain the scoped handler for later JITs.
            catch {
                if (sourceBuildResolver != null) AppDomain.CurrentDomain.AssemblyResolve -= sourceBuildResolver;
                throw;
            }
            ModsManager.Dlls[assembly.FullName] = assembly;
        }
        entity.HandleAssembly(assembly);
        Log.Information("CS战术拓展：已启用内置 T/CT 玩家外观。");
    }
}
