using Engine;
using Engine.Animation;
using Engine.Graphics;
namespace Game;

// Per-model sampler: action time is supplied by gameplay, never advanced by a camera.
// Only the upper body is replaced (the whole body below the root while planting); navigation and entity/root transforms remain native.
public sealed class ScAgentActions {
    readonly Model model;
    readonly AnimationPlayer player=new();
    readonly Matrix?[] sampled,crouched;
    readonly int[] upper,planted;
    readonly Matrix inverseIdleHand;
    readonly Dictionary<string,ModelAnimation> clips;
    readonly Dictionary<string,int> bones;
    readonly Dictionary<string,Matrix?[]> holds=new();
    readonly Dictionary<string,int[]> holdIndices=new();
    readonly Dictionary<(string Asset,string Bone),int> propIndices=new();
    public ScAgentActions(Model source) {
        // Also covers NMM list previews and restored player appearances before pose creation.
        ScActorAnimations.Ensure(source);
        model=source;sampled=new Matrix?[source.Bones.Count];crouched=new Matrix?[source.Bones.Count];
        clips=source.Animations.ToDictionary(a=>a.Name);
        bones=source.Bones.ToDictionary(b=>b.Name,b=>b.Index);
        var spine=source.FindBone("spine_0",false);
        bool Upper(ModelBone b)=>b!=null&&(b==spine||Upper(b.ParentBone));
        upper=source.Bones.Where(Upper).Select(b=>b.Index).ToArray();
        // Planting bends the whole body (CS2's clip lowers the pelvis and kneels): the pelvis and legs too, never the root.
        var pelvis=source.FindBone("pelvis",false);
        bool Body(ModelBone b)=>b!=null&&(b==pelvis||Body(b.ParentBone));
        planted=source.Bones.Where(b=>Upper(b)||Body(b)&&!b.Name.StartsWith("cswp_",StringComparison.Ordinal)).Select(b=>b.Index).Distinct().ToArray();
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
        // The grenade that follows a throw is drawn with the same world clip as a freshly selected one.
        string prefix=action.Kind==ScWeaponActionKind.Draw||action.Kind==ScWeaponActionKind.Grenade&&action.Clip=="deploy"?"draw":action.Kind==ScWeaponActionKind.Reload?
            (action.Clip?.Contains("Empty")==true?"reloadEmpty":"reload"):null;
        if(prefix==null)return null;
        string name=prefix+"_"+asset;
        if(clips.ContainsKey(name))return name;
        name="reload_"+asset;
        return action.Kind==ScWeaponActionKind.Reload&&clips.ContainsKey(name)?name:null;
    }
    public void Apply(Matrix?[] local, ScWeaponAction action) => ApplyHeld(local,action,action.Asset);
    // ---- throw (video-feedback-20260929 R2): CS2's own world pull-pin and throw clips, timed by the gameplay timeline ----
    readonly Dictionary<string,float> releases=new();
    /// <summary>World clip of a throw stage for this item, or null when the actor's clip cache has none (older package).</summary>
    public string ThrowClip(string asset,int stage,bool low)=>ThrowClipFor(asset,stage,low,false);
    /// <summary>As ThrowClip, crouched (r2-c4-completion-20260929: CS2's pullpin_crouch / crouch_throw_far / _near);
    /// the standing clip when the cache has no crouched one.</summary>
    public string ThrowClipFor(string asset,int stage,bool low,bool crouch) {
        string name=stage==0?"pullpin_":stage==2?(low?"throwlow_":"throwhigh_"):null;
        if(name==null)return null;string world=WorldAsset(asset);
        if(crouch&&clips.ContainsKey(name+"crouch_"+world))return name+"crouch_"+world;
        return clips.ContainsKey(name+world)?name+world:null;
    }
    /// <summary>Seconds into a throw clip at which the right hand moves fastest relative to the chest: the moment
    /// the grenade leaves it. Measured on the clip itself, once.</summary>
    public float ReleaseTime(string clipName) {
        if(releases.TryGetValue(clipName,out float known))return known;
        var clip=clips[clipName];var hand=model.FindBone("hand_R",false);var root=model.FindBone("spine_0",false);float best=clip.Duration*.25f;
        if(hand!=null&&root!=null){
            var pose=new Matrix?[model.Bones.Count];var sampler=new AnimationPlayer();sampler.SetAnimation(model,clip);
            Vector3 At(float t){Array.Clear(pose);sampler.Time=t;ScActorSampler.Sample(sampler,pose);var m=Matrix.Identity;
                for(var b=hand;b!=null&&b!=root.ParentBone;b=b.ParentBone)m*=pose[b.Index]??b.Transform;return m.Translation;}
            const int steps=60;float fastest=0;var previous=At(0);
            for(int i=1;i<=steps;i++){float t=clip.Duration*i/steps;var now=At(t);float speed=Vector3.Distance(now,previous);if(speed>fastest){fastest=speed;best=t;}previous=now;}
        }
        return releases[clipName]=best;
    }
    /// <summary>The held item's pose with the throw on top: pull-pin through its clip, held ready in the hold pose,
    /// the throw with its release on the gameplay release and its follow-through over the rest of the action.
    /// False when this model has no throw clips; the caller then keeps the older behaviour.</summary>
    public bool ApplyThrow(Matrix?[] local,string heldAsset,ScThrowPhase phase)=>ApplyThrowPosed(local,heldAsset,phase,0);
    /// <summary>ApplyThrow for an actor crouched by <paramref name="crouch"/> (0 standing .. 1 crouched): the standing
    /// and crouched clips are sampled at the same gameplay phase, each with its own release moment, and blended by
    /// the crouch factor, so crouching or standing up mid-throw moves the arms continuously and never restarts or
    /// advances the release. Legs stay with the gait (standing, crouched, running or in the air).</summary>
    public bool ApplyThrowPosed(Matrix?[] local,string heldAsset,ScThrowPhase phase,float crouch) {
        if(!phase.Active||phase.Asset!=heldAsset||ThrowClip(heldAsset,0,phase.Low)==null||ThrowClip(heldAsset,2,phase.Low)==null)return false;
        ApplyHeld(local,default,heldAsset);
        if(ThrowClip(heldAsset,phase.Stage,phase.Low) is not {} name)return true; // held ready: the hold pose
        crouch=Math.Clamp(crouch,0,1);string low=ThrowClipFor(heldAsset,phase.Stage,phase.Low,true);
        float weight=SampleThrow(crouch>=1&&low!=null?low:name,phase,sampled);
        if(crouch>0&&crouch<1&&low!=null&&low!=name){SampleThrow(low,phase,crouched);foreach(int i in upper)if(sampled[i] is Matrix a&&crouched[i] is Matrix b)sampled[i]=Blend(a,b,crouch);}
        foreach(int i in upper)if(sampled[i] is Matrix target)local[i]=Blend(local[i]??model.Bones[i].Transform,target,weight);
        return true;
    }
    /// <summary>Samples one throw clip at the gameplay phase into <paramref name="into"/>; returns the blend weight.</summary>
    float SampleThrow(string name,ScThrowPhase phase,Matrix?[] into){
        var clip=clips[name];float time,weight;
        if(phase.Stage==0){time=phase.Pull*clip.Duration;weight=Math.Clamp(Math.Min(phase.Pull/.08f,(1-phase.Pull)/.08f),0,1);}
        else{
            float release=ReleaseTime(name);
            time=phase.Released||phase.Wind>=1?release+phase.Follow*(clip.Duration-release):phase.Wind*release;
            weight=phase.Released?Math.Clamp((1-phase.Follow)/.15f,0,1):Math.Clamp(phase.Wind/.12f,0,1);
        }
        if(player.Animation!=clip)player.SetAnimation(model,clip);
        player.Time=Math.Clamp(time,0,clip.Duration);
        Array.Clear(into);ScActorSampler.Sample(player,into);
        if(phase.Stage==0||!phase.Released&&phase.Wind<1)KeepPropInHand(clip,player.Time,into);
        return weight;
    }
    // The engine's glTF loader resamples every clip to 30 keys a second (GltfAnimationConverter.EstimateKeyFrameCount,
    // API 1.9.3.1), so the export's denser mount keys do not survive loading: between two keys the prop mount cuts the
    // chord of the hand's arc, up to 0.1 m at the fastest part of a throw (c02: the crouched overhand at 21 m/s left the
    // grenade 4 cm off the fingers). At a key the mount stays the clip's; between keys it keeps, relative to the right
    // hand, the offset interpolated between its offsets at those two keys.
    readonly Dictionary<ModelAnimation,float[]> mountKeys=new();
    Matrix?[] keyA,keyB;
    int mountBone=-2,handBone=-1,anchorBone=-1;
    void KeepPropInHand(ModelAnimation clip,float time,Matrix?[] pose){
        if(mountBone==-2){
            mountBone=bones.GetValueOrDefault("cs_weapon_mount",-1);handBone=bones.GetValueOrDefault("hand_R",-1);anchorBone=bones.GetValueOrDefault("spine_2",-1);
            bool Under(ModelBone b)=>b!=null&&(b.Index==anchorBone||Under(b.ParentBone));
            if(handBone<0||anchorBone<0||!Under(model.Bones[handBone])||mountBone>=0&&model.Bones[mountBone].ParentBone?.Index!=anchorBone)mountBone=-1;
            keyA=new Matrix?[model.Bones.Count];keyB=new Matrix?[model.Bones.Count];
        }
        if(mountBone<0||pose[mountBone] is null)return;
        if(!mountKeys.TryGetValue(clip,out var times))mountKeys[clip]=times=clip.Channels.FirstOrDefault(c=>c.TargetBoneName=="cs_weapon_mount")?.Sampler?.KeyTimes;
        if(times is not {Length:>1})return;
        int i=Array.BinarySearch(times,time);if(i>=0)return;i=~i;if(i<=0||i>=times.Length)return;
        float k0=times[i-1],k1=times[i],f=(time-k0)/Math.Max(1e-6f,k1-k0);
        Matrix Hand(Matrix?[] p){var m=Matrix.Identity;for(var b=model.Bones[handBone];b!=null&&b.Index!=anchorBone;b=b.ParentBone)m*=p[b.Index]??b.Transform;return m;}
        Matrix Offset(float at,Matrix?[] into){player.Time=at;Array.Clear(into);ScActorSampler.Sample(player,into);return (into[mountBone]??model.Bones[mountBone].Transform)*Matrix.Invert(Hand(into));}
        var offset=Blend(Offset(k0,keyA),Offset(k1,keyB),f);player.Time=time;
        pose[mountBone]=offset*Hand(pose);
    }
    // ---- planting the C4 (r2-c4-completion-20260929): CS2's world planting clips, timed by the gameplay clock ----
    readonly Dictionary<string,float> placements=new();
    public string PlantClip(bool crouch)=>crouch&&clips.ContainsKey("plant_crouch_c4")?"plant_crouch_c4":clips.ContainsKey("plant_c4")?"plant_c4":null;
    /// <summary>Seconds into a planting clip at which the bomb is set down: the earliest moment after which the bomb prop
    /// stays within RestTolerance of where it ends (CS2 dips it, lays it flat and leaves it while the hand moves away; c05:
    /// the lowest dip came 0.1 s before the rest and left the bomb 8 cm higher at the commit), measured on the clip once,
    /// in 40-97% of it. The lowest point is the fallback when the clip never comes to rest. The gameplay commit (3.2 s)
    /// is mapped onto it.</summary>
    public const float RestTolerance=.015f;
    public float PlaceTime(string clipName) {
        if(placements.TryGetValue(clipName,out float known))return known;
        var clip=clips[clipName];float best=clip.Duration*.8f;
        if(bones.TryGetValue("cswp_c4/weapon",out int prop)){
            var pose=new Matrix?[model.Bones.Count];var sampler=new AnimationPlayer();sampler.SetAnimation(model,clip);
            Vector3 At(float t){Array.Clear(pose);sampler.Time=t;ScActorSampler.Sample(sampler,pose);var m=Matrix.Identity;
                for(var b=model.Bones[prop];b!=null;b=b.ParentBone)m*=pose[b.Index]??b.Transform;return m.Translation;}
            const int steps=99;var times=new List<float>();var points=new List<Vector3>();
            for(int i=0;i<=steps;i++){float t=clip.Duration*i/steps;if(t<clip.Duration*.4f||t>clip.Duration*.97f)continue;times.Add(t);points.Add(At(t));}
            if(points.Count>0){
                int lowest=0;for(int i=1;i<points.Count;i++)if(points[i].Y<points[lowest].Y-1e-4f)lowest=i;best=times[lowest];
                var end=points[^1];int rest=points.Count-1;
                while(rest>0&&Vector3.Distance(points[rest-1],end)<RestTolerance)rest--;
                if(rest<points.Count-1&&rest>=lowest-2)best=times[rest];
            }
        }
        return placements[clipName]=best;
    }
    /// <summary>Clip time for a plant phase: the operating part is stretched onto the clip up to its set-down moment,
    /// the recovery plays the rest of the clip.</summary>
    public float PlantClipTime(string clipName,ScPlantPhase phase){
        var clip=clips[clipName];float place=PlaceTime(clipName);
        return phase.Placed?place+phase.Recovery*(clip.Duration-place):phase.Operating*place;
    }
    /// <summary>The held C4's pose with the plant on top, the crouched and standing clips blended by the crouch factor.
    /// Unlike the throws the plant drives the pelvis and legs as well: the upper body alone left the bomb 0.2 m above the
    /// ground at the commit (c02), because CS2's clip kneels lower than the crouch gait. False when this model has no
    /// planting clips; the caller then keeps the held pose.</summary>
    public bool ApplyPlant(Matrix?[] local,ScPlantPhase phase,float crouch) {
        if(!phase.Active||PlantClip(false) is not {} stand)return false;
        ApplyHeld(local,default,"c4");
        crouch=Math.Clamp(crouch,0,1);string low=PlantClip(true);
        void Sample(string name,Matrix?[] into){var clip=clips[name];if(player.Animation!=clip)player.SetAnimation(model,clip);player.Time=Math.Clamp(PlantClipTime(name,phase),0,clip.Duration);Array.Clear(into);ScActorSampler.Sample(player,into);}
        Sample(crouch>=1&&low!=null?low:stand,sampled);
        if(crouch>0&&crouch<1&&low!=null&&low!=stand){Sample(low,crouched);foreach(int i in planted)if(sampled[i] is Matrix a&&crouched[i] is Matrix b)sampled[i]=Blend(a,b,crouch);}
        float weight=Math.Clamp(Math.Min(phase.Seconds/.15f,(ScPlantPhase.EndSeconds-phase.Seconds)/.2f),0,1);
        foreach(int i in planted)if(sampled[i] is Matrix target)local[i]=Blend(local[i]??model.Bones[i].Transform,target,weight);
        return true;
    }
    public void ApplyHeld(Matrix?[] local, ScWeaponAction action,string heldAsset) {
        if(heldAsset!=null&&clips.TryGetValue("hold_"+WorldAsset(heldAsset),out var hold)){
            if(!holds.TryGetValue(hold.Name,out var held)){
                held=new Matrix?[model.Bones.Count];player.SetAnimation(model,hold);player.Time=0;ScActorSampler.Sample(player,held);holds[hold.Name]=held;
                holdIndices[hold.Name]=upper.Where(i=>held[i].HasValue).ToArray();
            }
            foreach(int i in holdIndices[hold.Name])local[i]=held[i];
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
    /// <summary>The player's look pitch on a held gun (rounds 10/11, 2026-10-01). CS2 aims a world model with a procedural
    /// node ("Aim IK", CNmGraphDocAimCSNode in animation/graphs/worldmodel/worldmodel.vnmgraph: inputs aim_angle_pitch/yaw,
    /// weapon category/type/action, crouch weight; the bone work is game code, no aim clips exist), so its visible result is
    /// reproduced: every rotation about one model-space axis — horizontal and square to the held gun's barrel, so the gun
    /// only pitches, never rolls or yaws (round 10 bent each spine bone about its own local Z by the whole pitch: the user
    /// saw a strange twist and far too much bend; r11a's axis through the clavicles was diagonal in the rifle stance — the
    /// barrel followed 0.33 of a 0.5 look and rolled 0.37) — the spine leans a little (spine_0/1/2 10 % each), the arms and the weapon mount turn together about the
    /// shoulders by the rest (70 %: the gun ends on the look, the hands stay on it), the neck adds 50 % for the head. A
    /// positive pitch (looking up) lifts the barrel's front (<see cref="LiftAxis"/>); ThrowPoseCheck measures it on the
    /// packaged CT/T actors.</summary>
    public void ApplyAimPitch(Matrix?[] local,float pitch,string heldAsset){
        if(pitch==0||heldAsset==null)return;
        ModelBone neck=model.FindBone("neck_0",false),clavL=model.FindBone("clavicle_L",false),clavR=model.FindBone("clavicle_R",false),
            armL=model.FindBone("arm_upper_L",false),armR=model.FindBone("arm_upper_R",false),mount=model.FindBone("cs_weapon_mount",false);
        ModelBone[] spines=[model.FindBone("spine_0",false),model.FindBone("spine_1",false),model.FindBone("spine_2",false)];
        if(neck==null||clavL==null||clavR==null||armL==null||armR==null||mount==null||spines.Any(b=>b==null))return;
        var abs=aimAbsolute??=new Matrix[model.Bones.Count];
        Absolute(local,abs);
        // The vertical of the space the bones are composed in (after the root bone, Y up: the crouch squash and the lie-down
        // use the same axis), so the barrel stays in its own vertical plane and follows the look. Round 12: the pelvis-to-neck
        // line of the stance leans sideways, which made the barrel drift 26° off its yaw at the 1.2 rad clamp (ThrowPoseCheck).
        Vector3 up=Vector3.UnitY;
        // The barrel as the hold pose points it (the drawn weapon's frame, forward −Z as ScThirdPersonWeapon draws it).
        var weapon=ScThirdPersonWeapon.For(heldAsset,ScGunNativeMesh.Resolve(heldAsset,0,out _,out _)!=null);
        if(weapon==null)return;
        Vector3 axis=LiftAxis(up,Vector3.TransformNormal(-Vector3.UnitZ,RootWorld(weapon,abs)));
        if(axis==Vector3.Zero)return;
        void Turn(ModelBone bone,float angle,Vector3 pivot){
            Matrix turned=abs[bone.Index]*Matrix.CreateTranslation(-pivot)*Matrix.CreateFromAxisAngle(axis,angle)*Matrix.CreateTranslation(pivot);
            local[bone.Index]=turned*Matrix.Invert(bone.ParentBone!=null?abs[bone.ParentBone.Index]:Matrix.Identity);
            Absolute(local,abs);
        }
        foreach(var spine in spines)Turn(spine,.1f*pitch,abs[spine.Index].Translation);
        Vector3 shoulders=(abs[armL.Index].Translation+abs[armR.Index].Translation)*.5f;
        Turn(clavL,.7f*pitch,shoulders);Turn(clavR,.7f*pitch,shoulders);Turn(mount,.7f*pitch,shoulders);
        Turn(neck,.5f*pitch,abs[neck.Index].Translation);
    }
    /// <summary>The axis about which a positive angle lifts <paramref name="front"/> toward <paramref name="up"/> (unit length):
    /// horizontal and square to the front, so the turn neither twists nor rolls. Zero when the front is vertical.
    /// Round 12 (2026-10-01): r11b read the sign on up × axis, which is the barrel's back, so looking up bent the gun, the
    /// spine and the head down (user: "第一人称视角抬头看天，切换第三人称却是人物头埋地"); the sign is now read on the front itself.</summary>
    public static Vector3 LiftAxis(Vector3 up,Vector3 front){
        front-=up*Vector3.Dot(front,up);
        if(front.LengthSquared()<1e-8f)return Vector3.Zero;
        front=Vector3.Normalize(front);
        Vector3 axis=Vector3.Normalize(Vector3.Cross(up,front));
        return Vector3.Dot(Vector3.TransformNormal(front,Matrix.CreateFromAxisAngle(axis,.1f)),up)>0?axis:-axis;
    }
    Matrix[] aimAbsolute;
    void Absolute(Matrix?[] local,Matrix[] abs){
        void Walk(ModelBone bone,Matrix parent){
            abs[bone.Index]=(local[bone.Index]??bone.Transform)*parent;
            foreach(var child in bone.ChildBones)Walk(child,abs[bone.Index]);
        }
        Walk(model.RootBone,Matrix.Identity);
    }
    void Rotate(Matrix?[] local,string name,Matrix rotation){
        var b=model.FindBone(name,false);if(b==null)return;var m=local[b.Index]??b.Transform;var p=m.Translation;m.Translation=Vector3.Zero;local[b.Index]=rotation*m*Matrix.CreateTranslation(p);
    }
    static Matrix Blend(Matrix a,Matrix b,float t){
        a.Decompose(out var sa,out var ra,out var pa);b.Decompose(out var sb,out var rb,out var pb);
        return Matrix.CreateScale(Vector3.Lerp(sa,sb,t))*Matrix.CreateFromQuaternion(Quaternion.Slerp(ra,rb,t))*Matrix.CreateTranslation(Vector3.Lerp(pa,pb,t));
    }
}
