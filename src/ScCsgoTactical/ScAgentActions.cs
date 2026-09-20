using Engine;
using Engine.Animation;
using Engine.Graphics;
namespace Game;

// Per-model sampler: action time is supplied by gameplay, never advanced by a camera.
// Only the upper body is replaced; navigation, feet and entity/root transforms remain native.
public sealed class ScAgentActions {
    readonly Model model;
    readonly AnimationPlayer player=new();
    readonly Matrix?[] sampled;
    readonly int[] upper;
    readonly Matrix inverseIdleHand;
    public ScAgentActions(Model source) {
        model=source;sampled=new Matrix?[source.Bones.Count];
        var spine=source.FindBone("spine_0",false);
        bool Upper(ModelBone b)=>b!=null&&(b==spine||Upper(b.ParentBone));
        upper=source.Bones.Where(Upper).Select(b=>b.Index).ToArray();
        var idle=source.Animations.FirstOrDefault(a=>a.Name=="aim");
        if(idle!=null){player.SetAnimation(source,idle);player.Time=0;player.SampleBoneTransforms(sampled);}
        Matrix Absolute(ModelBone b)=>(sampled[b.Index]??b.Transform)*(b.ParentBone==null?Matrix.CreateRotationY(MathF.PI):Absolute(b.ParentBone));
        var hand=source.FindBone("hand_R",false);
        var basis=hand==null?Matrix.Identity:Absolute(hand);basis.Translation=Vector3.Zero;
        inverseIdleHand=Matrix.Invert(basis);Array.Clear(sampled);
    }
    public Matrix WeaponWorld(Vector3 grip, Matrix animatedHandWorld) => Matrix.CreateTranslation(-grip)*inverseIdleHand*animatedHandWorld;
    public string ClipFor(ScWeaponAction action) {
        string asset=action.Asset;
        if(asset?.StartsWith("grenade_")==true)asset=asset=="grenade_molotov"?"molotov":"grenade";
        string prefix=action.Kind==ScWeaponActionKind.Draw?"draw":action.Kind==ScWeaponActionKind.Reload?
            (action.Clip?.Contains("Empty")==true?"reloadEmpty":"reload"):null;
        if(prefix==null)return null;
        string name=prefix+"_"+asset;
        if(model.Animations.Any(a=>a.Name==name))return name;
        name="reload_"+asset;
        return action.Kind==ScWeaponActionKind.Reload&&model.Animations.Any(a=>a.Name==name)?name:null;
    }
    public void Apply(Matrix?[] local, ScWeaponAction action) {
        if(!action.Active)return;
        float weight=action.Weight;
        if(ClipFor(action) is {} name){
            var clip=model.Animations.First(a=>a.Name==name);
            if(player.Animation!=clip)player.SetAnimation(model,clip);
            float phase=action.Progress;
            if(action.LoopedReload)phase=Math.Clamp(action.ClipTime/Math.Max(.001f,Cs2Rig.Duration(action.Asset,action.Clip)),0,1);
            player.Time=phase*clip.Duration;
            Array.Clear(sampled);player.SampleBoneTransforms(sampled);
            foreach(int i in upper)if(sampled[i] is Matrix target)local[i]=Blend(local[i]??model.Bones[i].Transform,target,weight);
            return;
        }
        // Authored adaptation: source world files have no inspect clips. Keep the weapon
        // gripped and turn it toward the face with a bounded wrist/forearm motion.
        float wave=MathF.Sin(action.Progress*MathF.PI)*weight;
        if(action.Kind==ScWeaponActionKind.Inspect){
            Rotate(local,"arm_upper_R",Matrix.CreateRotationZ(-.28f*wave));
            Rotate(local,"arm_lower_R",Matrix.CreateRotationY(.38f*wave));
            Rotate(local,"hand_R",Matrix.CreateRotationX(.65f*wave*MathF.Sin(action.Progress*MathF.PI*2)));
            Rotate(local,"arm_lower_L",Matrix.CreateRotationY(-.3f*wave));
            Rotate(local,"head_0",Matrix.CreateRotationZ(.12f*wave));
        }else if(action.Kind==ScWeaponActionKind.Shoot){
            Rotate(local,"arm_upper_R",Matrix.CreateRotationZ(-.07f*wave));
            Rotate(local,"arm_upper_L",Matrix.CreateRotationZ(-.05f*wave));
        }else if(action.Kind is ScWeaponActionKind.Attach or ScWeaponActionKind.Detach){
            Rotate(local,"arm_lower_L",Matrix.CreateRotationY(.45f*wave));
            Rotate(local,"hand_L",Matrix.CreateRotationX(.7f*wave*MathF.Sin(action.Progress*MathF.PI*4)));
        }else if(action.Kind is ScWeaponActionKind.Slash or ScWeaponActionKind.Grenade or ScWeaponActionKind.Prepare){
            Rotate(local,"arm_upper_R",Matrix.CreateRotationZ(-.8f*wave));
            Rotate(local,"arm_lower_R",Matrix.CreateRotationY(.3f*wave));
        }
    }
    void Rotate(Matrix?[] local,string name,Matrix rotation){
        var b=model.FindBone(name,false);if(b==null)return;var m=local[b.Index]??b.Transform;var p=m.Translation;m.Translation=Vector3.Zero;local[b.Index]=rotation*m*Matrix.CreateTranslation(p);
    }
    static Matrix Blend(Matrix a,Matrix b,float t){
        a.Decompose(out var sa,out var ra,out var pa);b.Decompose(out var sb,out var rb,out var pb);
        return Matrix.CreateScale(Vector3.Lerp(sa,sb,t))*Matrix.CreateFromQuaternion(Quaternion.Slerp(ra,rb,t))*Matrix.CreateTranslation(Vector3.Lerp(pa,pb,t));
    }
}
