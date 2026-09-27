using System.Diagnostics;
using Engine;
using Engine.Graphics;
using GameEntitySystem;
namespace Game;

/// <summary>Only equipment actually owned by this batch/world, never the catalogue.</summary>
public static class ScWeaponPreparation {
    public static int Value(Entity entity)=>entity.FindComponent<ComponentTacticalEnemy>()?.State?.DisplayValue
        ??entity.FindComponent<ComponentTacticalInventory>()?.GetSlotValue(0)??0;
    public static void Request(int value){
        if(!EffectiveGunStats.TrySnapshotValue(value,out var state))return;
        string asset=GunSpec.All[state.Variant].Name;
        string material=ScGunSkinCatalog.Material(asset,state.SkinId);
        ScTexturePreparation.Request(material);
        ScNpcWeaponGeometry.Request(asset,ScGunNativeMesh.UsesLegacy(asset,material));
    }
    public static void Restore(Project project){
        var values=new HashSet<int>();
        foreach(var entity in project.Entities){
            if(entity.FindComponent<ComponentTacticalModel>()==null)continue;
            int value=Value(entity);if(value!=0)values.Add(value);
        }
        var equipped=values.ToArray();
        var clock=Stopwatch.StartNew();
        for(int i=0;i<equipped.Length;i++){
            // Bounded lookahead. Full textures retain dimensions; decoder also
            // enforces its own memory reservation and suffix precedence.
            for(int j=i;j<Math.Min(i+2,equipped.Length);j++)Request(equipped[j]);
            if(!EffectiveGunStats.TrySnapshotValue(equipped[i],out var state))continue;
            string asset=GunSpec.All[state.Variant].Name;
            using var timing=ScTacticalPerformance.Measure(project,ScTacticalPerformance.Stage.WeaponPrepare,asset);
            var texture=ScGunVisualMaterial.Load(asset,state.SkinId,out var material);
            var geometry=ScNpcWeaponGeometry.For(asset,ScGunNativeMesh.UsesLegacy(asset,material));
            if(geometry==null||texture==null)continue;
            foreach(var g in geometry.Groups)if(g.Texture!=asset+"_hd")ScTexturePreparation.Load("Textures/ScCsgoKnives/"+g.Texture);
            ScNpcWeaponRenderer.PrepareGeometry(geometry,project);
        }
        Log.Information(FormattableString.Invariant($"[CS_PERF] restored-weapons count={equipped.Length} ms={clock.Elapsed.TotalMilliseconds:F2} texturesReady={ScTexturePreparation.Ready} textureWaitMs={ScTexturePreparation.WaitMilliseconds:F2} uploadMs={ScTexturePreparation.UploadMilliseconds:F2}"));
    }
}
