using System.Diagnostics;
using Engine;
using Engine.Graphics;
namespace Game;

// The loading screen executes these actions on the render thread. Decode shared
// actor resources before gameplay; never create GL resources on a worker thread.
public static class ScTacticalWarmup {
    static void Run(string stage,Action action){
        long bytes=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();
        try{action();Log.Information(FormattableString.Invariant($"[CS_PERF] warmup stage={stage} ms={clock.Elapsed.TotalMilliseconds:F2} allocKB={(GC.GetAllocatedBytesForCurrentThread()-bytes)/1024d:F1}"));}
        catch(Exception e){KnifeDiagnostics.WarnOnce("warmup-"+stage,$"CS warmup {stage}: {e.Message}; native lazy loading remains available.");}
    }
    public static void Add(List<Action> actions){
        foreach(string role in new[]{"ct","t"}){
            actions.Add(()=>Run(role+"-geometry",()=>ContentManager.Get<Model>("Models/ScCsgoTactical/"+role)));
            actions.Add(()=>Run(role+"-animation",()=>ScActorAnimations.Ensure(ContentManager.Get<Model>("Models/ScCsgoTactical/"+role))));
            actions.Add(()=>Run(role+"-textures",()=>{
                var model=ContentManager.Get<Model>("Models/ScCsgoTactical/"+role);
                foreach(var mesh in model.Meshes)foreach(var part in mesh.MeshParts){var material=model.GetMaterial(part.MaterialIndex);if(material.BaseColorTexture!=null)model.GetTexture(material.BaseColorTexture.TextureIndex);}
            }));
        }
        actions.Add(PrepareShader);
    }
    public static void PrepareShader()=>Run("weapon-shader",()=>ScNpcWeaponRenderer.Prepare());
}
