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
    readonly Dictionary<string,ModelAnimation> clips;
    readonly Dictionary<string,int> bones;
    readonly Dictionary<string,Matrix?[]> holds=new();
    readonly Dictionary<(string Asset,string Bone),int> propIndices=new();
    public ScAgentActions(Model source) {
        // Also covers NMM list previews and restored player appearances before pose creation.
        ScActorAnimations.Ensure(source);
        model=source;sampled=new Matrix?[source.Bones.Count];
        clips=source.Animations.ToDictionary(a=>a.Name);
        bones=source.Bones.ToDictionary(b=>b.Name,b=>b.Index);
        var spine=source.FindBone("spine_0",false);
        bool Upper(ModelBone b)=>b!=null&&(b==spine||Upper(b.ParentBone));
        upper=source.Bones.Where(Upper).Select(b=>b.Index).ToArray();
        var idle=source.Animations.FirstOrDefault(a=>a.Name=="aim");
        if(idle!=null){player.SetAnimation(source,idle);player.Time=0;ScActorSampler.Sample(player,sampled);}
        Matrix Absolute(ModelBone b)=>(sampled[b.Index]??b.Transform)*(b.ParentBone==null?Matrix.CreateRotationY(MathF.PI):Absolute(b.ParentBone));
        var hand=source.FindBone("hand_R",false);
        var basis=hand==null?Matrix.Identity:Absolute(hand);basis.Translation=Vector3.Zero;
        inverseIdleHand=Matrix.Invert(basis);Array.Clear(sampled);
    }
    public Matrix WeaponWorld(Vector3 grip, Matrix animatedHandWorld) => Matrix.CreateTranslation(-grip)*inverseIdleHand*animatedHandWorld;
    public static string WorldAsset(string asset) => asset?.StartsWith("grenade_")==true?(asset=="grenade_molotov"?"molotov":"grenade"):asset;
    public bool HasProp(string asset,string bone)=>bones.ContainsKey("cswp_"+WorldAsset(asset)+"/"+bone);
    public Matrix PropFrame(string asset,string bone,Matrix[] absolute) {
        if(!propIndices.TryGetValue((asset,bone),out int index)){
            string prefix="cswp_"+WorldAsset(asset)+"/";
            if(!bones.TryGetValue(prefix+bone,out index)&&!bones.TryGetValue(prefix+"weapon",out index))index=-1;
            propIndices[(asset,bone)]=index;
        }
        return index<0?Matrix.Identity:absolute[index];
    }
    public Matrix RootWorld(ScThirdPersonWeapon weapon,Matrix[] absolute)=>weapon.WorldRootInverse*PropFrame(weapon.Asset,"weapon",absolute);
    public (BlockMesh Mesh,Matrix Transform) WorldPart(ScThirdPersonWeapon weapon,ScThirdPersonWeapon.Group group,Matrix[] absolute)=>WorldPartFor(weapon.Asset,group,absolute);
    public (BlockMesh Mesh,Matrix Transform) WorldPartFor(string asset,ScThirdPersonWeapon.Group group,Matrix[] absolute) {
        if(group.VertexBones==null)return (group.Mesh,group.WorldInverse*PropFrame(asset,group.WorldBone,absolute));
        for(int i=0;i<group.Mesh.Vertices.Count;i++){
            var v=group.Mesh.Vertices[i];v.Position=Vector3.Transform(v.Position,group.VertexInverses[i]*PropFrame(asset,group.VertexBones[i],absolute));group.WorldScratch.Vertices[i]=v;
        }
        return (group.WorldScratch,Matrix.Identity);
    }
    public bool ShowWorldPart(ScThirdPersonWeapon weapon,ScThirdPersonWeapon.Group group,ScWeaponAction action)=>ShowWorldPartFor(weapon.Asset,group,action);
    public bool ShowWorldPartFor(string asset,ScThirdPersonWeapon.Group group,ScWeaponAction action) {
        // FPP helper shells have no matching world prop and can otherwise orbit the player.
        if(group.WorldBone=="shell"&&!HasProp(asset,"shell"))return false;
        if(asset=="revolver"&&group.WorldBone is "loader_handle" or "loader_holder")return action.Active&&action.Kind==ScWeaponActionKind.Reload;
        // Remaining visibility comes from the world clip's own scale tracks.
        return true;
    }
    public string ClipFor(ScWeaponAction action) {
        string asset=WorldAsset(action.Asset);
        string prefix=action.Kind==ScWeaponActionKind.Draw?"draw":action.Kind==ScWeaponActionKind.Reload?
            (action.Clip?.Contains("Empty")==true?"reloadEmpty":"reload"):null;
        if(prefix==null)return null;
        string name=prefix+"_"+asset;
        if(clips.ContainsKey(name))return name;
        name="reload_"+asset;
        return action.Kind==ScWeaponActionKind.Reload&&clips.ContainsKey(name)?name:null;
    }
    public void Apply(Matrix?[] local, ScWeaponAction action) => ApplyHeld(local,action,action.Asset);
    public void ApplyHeld(Matrix?[] local, ScWeaponAction action,string heldAsset) {
        if(heldAsset!=null&&clips.TryGetValue("hold_"+WorldAsset(heldAsset),out var hold)){
            if(!holds.TryGetValue(hold.Name,out var held)){
                held=new Matrix?[model.Bones.Count];player.SetAnimation(model,hold);player.Time=0;ScActorSampler.Sample(player,held);holds[hold.Name]=held;
            }
            foreach(int i in upper)if(held[i] is Matrix target)local[i]=target;
        }
        if(action.Asset!=heldAsset)return;
        if(!action.Active)return;
        float weight=action.Weight;
        if(ClipFor(action) is {} name){
            var clip=clips[name];
            if(player.Animation!=clip)player.SetAnimation(model,clip);
            float phase=action.Progress;
            if(action.LoopedReload)phase=Math.Clamp(action.ClipTime/Math.Max(.001f,Cs2Rig.Duration(action.Asset,action.Clip)),0,1);
            player.Time=phase*clip.Duration;
            Array.Clear(sampled);ScActorSampler.Sample(player,sampled);
            foreach(int i in upper)if(sampled[i] is Matrix target)local[i]=Blend(local[i]??model.Bones[i].Transform,target,weight);
            return;
        }
        // No world inspect clips exist in the export. Move the shared upper-body
        // parent so both hands retain their prop contacts, including dual weapons.
        float wave=MathF.Sin(action.Progress*MathF.PI)*weight;
        if(action.Kind==ScWeaponActionKind.Inspect){
            Rotate(local,"spine_2",Matrix.CreateRotationZ(-.14f*wave)*Matrix.CreateRotationX(.25f*wave*MathF.Sin(action.Progress*MathF.PI*2)));
            Rotate(local,"head_0",Matrix.CreateRotationZ(.12f*wave));
        }else if(action.Kind==ScWeaponActionKind.Shoot){
            Rotate(local,"spine_2",Matrix.CreateRotationZ(-.05f*wave));
        }else if(action.Kind is ScWeaponActionKind.Attach or ScWeaponActionKind.Detach){
            Rotate(local,"arm_lower_L",Matrix.CreateRotationY(.45f*wave));
            Rotate(local,"hand_L",Matrix.CreateRotationX(.7f*wave*MathF.Sin(action.Progress*MathF.PI*4)));
        }else if(action.Kind is ScWeaponActionKind.Slash or ScWeaponActionKind.Grenade or ScWeaponActionKind.Prepare){
            Rotate(local,"spine_2",Matrix.CreateRotationY(.35f*wave)*Matrix.CreateRotationZ(-.18f*wave));
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
