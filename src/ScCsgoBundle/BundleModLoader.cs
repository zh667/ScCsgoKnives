using System.Text;

namespace Game;

/// <summary>Single-file distribution adapter; does not alter any gameplay/save data.</summary>
public sealed class BundleModLoader : ModLoader {
    const string TacticalPackage = "zh667.ScCsgoTactical";
    ModEntity alias;
    // Keep the core loader as the primary loader for single-loader API callbacks.
    public override int Priority => 1000;

    public override void __ModInitialize() {
        if(Entity?.modInfo?.PackageName!="zh667.ScCsgoKnives" ||
            !Entity.ModFiles.ContainsKey("ScCsgoTactical.dll"))
            throw new InvalidOperationException("CS总包内容不完整，请重新安装完整的总包文件。");
        if(ModsManager.ModList.Any(m=>!m.IsDisabled&&m.modInfo?.PackageName==TacticalPackage))
            throw new InvalidOperationException("CS总包已内置战术同伴拓展。请停用旧的独立战术拓展，仅保留一个总包，再重新加载模组。");
    }

    public override void OnLoadingFinished(List<Action> actions) => actions.Add(RegisterIdentity);

    void RegisterIdentity() {
        // Adding an identity during HandleAssembly would invalidate the native
        // assembly lookup built earlier. Defer until all native loading is done.
        var existing=ModsManager.ModList.FirstOrDefault(m=>m.modInfo?.PackageName==TacticalPackage);
        if(existing!=null){
            if(ReferenceEquals(existing,alias))return;
            throw new InvalidOperationException("战术拓展身份重复，请仅保留CS总包。");
        }
        ModInfo metadata=null;
        Entity.GetFile("Integrations/ScCsgoTactical.modinfo.json",s=>metadata=ModsManager.DeserializeJson(Encoding.UTF8.GetString(ModsManager.StreamToBytes(s))));
        if(metadata?.PackageName!=TacticalPackage)
            throw new InvalidOperationException("CS总包缺少战术拓展身份信息。");
        // Identity only: no archive, loaders, hooks, blocks or resources are duplicated.
        // Native GetModEntity (old-world checks) and SubsystemUsedMods (save) both
        // consult ModList. Native reboot clears it before the next installation loads.
        alias=new ModEntity{modInfo=metadata,ModFilePath=Entity.ModFilePath,IsDependencyChecked=true};
        ModsManager.ModList.Add(alias);
        ModsManager.PackageNameToModEntity[TacticalPackage]=alias;
    }
}
