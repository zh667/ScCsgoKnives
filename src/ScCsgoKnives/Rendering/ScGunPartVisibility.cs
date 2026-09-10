namespace Game;

/// <summary>Weapon helper geometry is not a permanent part of a gun. CS2's graph hides/scales it
/// outside its reload stage; do not rely on authoring it offscreen (wide phone FOV exposes it).</summary>
public static class ScGunPartVisibility {
    public static bool Visible(string asset,string joint,string clip,float time,bool frontRemoved) {
        // The loader is a reload prop, not the gun's cylinder. Draw clips sometimes leave
        // its bone in the bind pose; that exposes a floating ring beside every R8 finish.
        if(asset=="revolver" && joint is "loader_handle" or "loader_holder")
            return clip?.StartsWith("reload",StringComparison.Ordinal)==true;
        if(asset=="sawedoff" && joint=="shell") {
            if(clip is null||!clip.StartsWith("reload",StringComparison.Ordinal))return false;
            var sections=Cs2Rig.GetReloadSections(asset);
            return sections is not null && time>=sections.LoopStart && time<sections.OutroStart;
        }
        if(asset=="cz75a" && joint=="magazine2")
            return !frontRemoved && time < Cs2Rig.CzFrontDetachTime(clip);
        return true;
    }
}
