using System.Runtime.CompilerServices;
using Engine;
namespace Game;

/// <summary>Broad candidates are not hits. A definite mesh miss remains a miss;
/// unknown models retain a small, explicit body fallback and never become heads.</summary>
public static class ScGunHitTest {
    public sealed class Trace {
        public int Visited, BroadCandidates, PreciseTests, PreciseMisses, FallbackCandidates;
    }
    public readonly record struct Hit(ComponentBody Body,float Distance,ScHitPart Part,string Reason);
    sealed class Cache {
        public int Frame=-1; public Matrix Body; public object Model;
        public ScPartBox[] Parts;
    }
    static readonly ConditionalWeakTable<ComponentCreatureModel,Cache> s_cache=new();
    /// <summary>Headless geometry seam. Null in game; no file or live-state writes.</summary>
    public static Func<ComponentBody,ScPartBox[]> PoseProvider;
    static readonly ScHeadRule NoHead=new([],Vector3.One);
    public static float Tolerance(BoundingBox box)=>Math.Clamp(Math.Min(box.Size().X,box.Size().Z)*.1f,0,.08f);
    public static float? BoxDistance(BoundingBox box,Vector3 origin,Vector3 direction,float maximum) {
        float? d=ScHeadshot.Intersect(new ScPartBox(box,Matrix.Identity,false),origin,direction);
        return d is float value && value<=maximum?value:null;
    }
    public static (ScHitPart Part,float Distance) Precise(IEnumerable<ScPartBox> parts,Vector3 origin,Vector3 direction,float maximum,float tolerance) {
        var list=parts as ScPartBox[] ?? parts.ToArray();
        var direct=ScHeadshot.Resolve(list,origin,direction,maximum);
        if(direct.Part!=ScHitPart.Unknown) return direct;
        float closest=float.MaxValue;
        foreach(var p in list) {
            if(p.Head) continue;
            // World-distance padding expressed independently along each local axis.
            float sx=Vector3.TransformNormal(Vector3.UnitX,p.World).Length();
            float sy=Vector3.TransformNormal(Vector3.UnitY,p.World).Length();
            float sz=Vector3.TransformNormal(Vector3.UnitZ,p.World).Length();
            if(Math.Min(sx,Math.Min(sy,sz))<1e-6f) continue;
            var pad=new Vector3(tolerance/sx,tolerance/sy,tolerance/sz);
            float? d=ScHeadshot.Intersect(new ScPartBox(new BoundingBox(p.Local.Min-pad,p.Local.Max+pad),p.World,false),origin,direction);
            if(d is float distance && distance<=maximum) closest=Math.Min(closest,distance);
        }
        return closest<float.MaxValue?(ScHitPart.Body,closest):(ScHitPart.Unknown,-1);
    }
    static ScPartBox[] Pose(ComponentBody body) {
        if(PoseProvider is not null) return PoseProvider(body);
        var model=body.Entity?.FindComponent<ComponentCreatureModel>();
        if(model?.Model is null || model.AnimationController is null || model.m_boneTransforms is null) return null;
        var cache=s_cache.GetOrCreateValue(model);
        if(cache.Frame==Time.FrameIndex && cache.Body==body.Matrix && ReferenceEquals(cache.Model,model.Model)) return cache.Parts;
        cache.Frame=Time.FrameIndex;cache.Body=body.Matrix;cache.Model=model.Model;cache.Parts=null;
        try {
            // Synchronize current logical parameters and sample without advancing
            // the controller or applying root-motion velocity a second time.
            model.SyncAnimationParameters();
            var sampled=new Matrix?[model.Model.Bones.Count];
            model.AnimationController.ComputeBoneTransforms(sampled);
            if(model.Model.HasSkin || model.Model.HasAnimations) {
                int root=model.Model.RootBone.Index;
                var rootPose=sampled[root] ?? model.Model.RootBone.Transform;
                rootPose=Matrix.CreateRotationY(model.AnimationController.RootBoneRotation)*rootPose;
                sampled[root]=rootPose*body.Matrix;
            }
            var absolute=new Matrix[sampled.Length];
            var original=model.m_boneTransforms;
            try { model.m_boneTransforms=sampled;model.ProcessBoneHierarchy(model.Model.RootBone,Matrix.Identity,absolute); }
            finally {model.m_boneTransforms=original;}
            var rule=ScHeadRules.For(model.ModelRoute,ScHeadshotProbe.VanillaHeadClass(model),ScHeadshotProbe.HasHeadMesh(model.Model,ScHeadRule.Default)) ?? NoHead;
            cache.Parts=ScHeadshotProbe.Parts(model.Model,rule,absolute).ToArray();
            if(cache.Parts.Length==0) cache.Parts=null;
            if(cache.Parts is not null) {
                // A controller that has not produced a world-placed pose must not
                // turn a perfectly valid target into a permanent definite miss.
                var lo=new Vector3(float.MaxValue);var hi=new Vector3(float.MinValue);
                foreach(var p in cache.Parts) foreach(float x in new[]{p.Local.Min.X,p.Local.Max.X})
                    foreach(float y in new[]{p.Local.Min.Y,p.Local.Max.Y}) foreach(float z in new[]{p.Local.Min.Z,p.Local.Max.Z}) {
                        var v=Vector3.Transform(new Vector3(x,y,z),p.World);lo=Vector3.Min(lo,v);hi=Vector3.Max(hi,v);
                    }
                var b=body.BoundingBox;
                if(!float.IsFinite(lo.X+lo.Y+lo.Z+hi.X+hi.Y+hi.Z) || hi.X<b.Min.X-.6f || lo.X>b.Max.X+.6f || hi.Y<b.Min.Y-.6f || lo.Y>b.Max.Y+.6f || hi.Z<b.Min.Z-.6f || lo.Z>b.Max.Z+.6f)
                    throw new InvalidOperationException("controller pose is not placed at logical body");
            }
        } catch(Exception e) {cache.Parts=null;KnifeDiagnostics.WarnOnce("gun-logic-pose-"+model.ModelRoute,"Gun hit pose unavailable; narrow body-only fallback: "+e.Message);}
        return cache.Parts;
    }
    public static Hit? Raycast(IEnumerable<ComponentBody> bodies,ComponentBody shooter,Vector3 origin,Vector3 direction,float maximum) {
        return RaycastObserved(bodies,shooter,origin,direction,maximum,null);
    }
    public static Hit? RaycastObserved(IEnumerable<ComponentBody> bodies,ComponentBody shooter,Vector3 origin,Vector3 direction,float maximum,Trace trace) {
        Hit? best=null;
        foreach(var body in bodies) {
            if(body==shooter || (shooter is not null && body.Entity is not null && body.Entity==shooter.Entity) || body.Entity?.FindComponent<ComponentHealth>()?.Health<=0) continue;
            if(trace is not null) trace.Visited++;
            float limit=best?.Distance ?? maximum;
            var box=body.BoundingBox;float pad=Tolerance(box);
            // Broad selection permits animated heads/limbs outside the physics box.
            var broad=new BoundingBox(box.Min-new Vector3(.6f),box.Max+new Vector3(.6f));
            if(BoxDistance(broad,origin,direction,limit) is null) continue;
            if(trace is not null) trace.BroadCandidates++;
            var parts=Pose(body);
            if(parts is not null) {
                if(trace is not null) trace.PreciseTests++;
                var hit=Precise(parts,origin,direction,limit,pad);
                if(trace is not null && hit.Part==ScHitPart.Unknown) trace.PreciseMisses++;
                if(hit.Part!=ScHitPart.Unknown && hit.Distance<=limit && (best is null || hit.Distance<best.Value.Distance)) best=new(body,hit.Distance,hit.Part,"logical mesh pose");
            } else {
                if(trace is not null) trace.FallbackCandidates++;
                float? d=BoxDistance(new BoundingBox(box.Min-new Vector3(pad),box.Max+new Vector3(pad)),origin,direction,limit);
                if(d is float distance && distance<=limit && (best is null || distance<best.Value.Distance)) best=new(body,distance,ScHitPart.Body,"unavailable model; narrow body fallback");
            }
        }
        return best;
    }
}
