using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Simplified jointed corpse for CS actors (agent-followup-140 F3). Not CS2's physics: 18 Verlet joint
/// particles taken from the last living pose, distance constraints for every body segment plus torso bracing, range
/// limits for knees, elbows and neck, gravity, a bounded push from the hit direction, and contact with the real block
/// collision boxes (friction on support). Fixed 30 Hz, at most 2 sub-steps per frame and 6 solver iterations; sleeps
/// at rest or after 6 s. At most <see cref="MaxActive"/> simulate per world; others keep the old collapse. Visual
/// only: health, drops and rewards never depend on it.</summary>
public sealed class TacticalRagdoll {
    public const int MaxActive=6,Iterations=6,MaxSubSteps=2;
    public const float Step=1/30f,Gravity=-10,Sleep=6,Damping=.985f;
    static readonly string[] Joints=["pelvis","spine_2","neck_0","head_0","arm_upper_L","arm_lower_L","hand_L","arm_upper_R","arm_lower_R","hand_R",
        "leg_upper_L","leg_lower_L","ankle_L","leg_upper_R","leg_lower_R","ankle_R","ball_L","ball_R"];
    const int Pelvis=0,Spine=1,Neck=2,Head=3,ShoulderL=4,ElbowL=5,HandL=6,ShoulderR=7,ElbowR=8,HandR=9,HipL=10,KneeL=11,AnkleL=12,HipR=13,KneeR=14,AnkleR=15,BallL=16,BallR=17,N=18;
    static readonly float[] Radius=[.12f,.13f,.1f,.11f,.07f,.06f,.05f,.07f,.06f,.05f,.08f,.07f,.06f,.08f,.07f,.06f,.04f,.04f];
    static readonly (int A,int B)[] Links=[(Pelvis,Spine),(Spine,Neck),(Neck,Head),(Neck,ShoulderL),(Neck,ShoulderR),(ShoulderL,ElbowL),(ElbowL,HandL),
        (ShoulderR,ElbowR),(ElbowR,HandR),(Pelvis,HipL),(Pelvis,HipR),(HipL,KneeL),(KneeL,AnkleL),(HipR,KneeR),(KneeR,AnkleR),(AnkleL,BallL),(AnkleR,BallR),
        // bracing keeps the chest and hips rigid boxes
        (ShoulderL,ShoulderR),(HipL,HipR),(Spine,ShoulderL),(Spine,ShoulderR),(Pelvis,ShoulderL),(Pelvis,ShoulderR),(Spine,HipL),(Spine,HipR),(Pelvis,Neck)];
    // (a, b, min fraction, max fraction) of the rest distance: straight knee/elbow at most, never folded flat; neck bends a little;
    // the ankle only flexes a little, so the toes (part of the collision set) cannot swing through a block.
    static readonly (int A,int B,float Min,float Max)[] Ranges=[(HipL,AnkleL,.5f,1),(HipR,AnkleR,.5f,1),(ShoulderL,HandL,.35f,1),(ShoulderR,HandR,.35f,1),
        (Spine,Head,.8f,1.02f),(KneeL,Spine,.55f,2),(KneeR,Spine,.55f,2),(KneeL,BallL,.9f,1.05f),(KneeR,BallR,.9f,1.05f)];
    // Driven bones: (bone, frame start, frame end, side from, side to). All other bones follow their parent.
    static readonly (string Bone,int S,int E,int SideA,int SideB)[] Driven=[
        ("pelvis",Pelvis,Spine,HipR,HipL),("spine_0",Pelvis,Spine,HipR,HipL),("spine_1",Pelvis,Spine,HipR,HipL),
        ("spine_2",Spine,Neck,ShoulderR,ShoulderL),("spine_3",Spine,Neck,ShoulderR,ShoulderL),("clavicle_L",Spine,Neck,ShoulderR,ShoulderL),("clavicle_R",Spine,Neck,ShoulderR,ShoulderL),
        ("neck_0",Neck,Head,ShoulderR,ShoulderL),("head_0",Neck,Head,ShoulderR,ShoulderL),
        ("arm_upper_L",ShoulderL,ElbowL,ShoulderR,ShoulderL),("arm_lower_L",ElbowL,HandL,ShoulderR,ShoulderL),
        ("arm_upper_R",ShoulderR,ElbowR,ShoulderR,ShoulderL),("arm_lower_R",ElbowR,HandR,ShoulderR,ShoulderL),
        ("leg_upper_L",HipL,KneeL,HipR,HipL),("leg_lower_L",KneeL,AnkleL,HipR,HipL),("leg_upper_R",HipR,KneeR,HipR,HipL),("leg_lower_R",KneeR,AnkleR,HipR,HipL),("ankle_L",AnkleL,BallL,HipR,HipL),("ankle_R",AnkleR,BallR,HipR,HipL)];
    sealed class Budget{public int Active;}
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameEntitySystem.Project,Budget> s_active=new(); // closed worlds are not retained

    readonly Model model;readonly SubsystemTerrain terrain;readonly GameEntitySystem.Project project;
    readonly Vector3[] p=new Vector3[N],prev=new Vector3[N],rest=new Vector3[N];
    readonly float[] length;readonly (float Min,float Max)[] range;
    readonly Matrix[] restWorld;readonly Matrix?[] restLocal;readonly int[] drive;readonly ModelBone[] order;
    float accumulator,age,still;
    public bool Asleep {get;private set;}
    bool counted;

    TacticalRagdoll(GameEntitySystem.Project project,Model model,SubsystemTerrain terrain,Matrix[] world,Matrix?[] local,ModelBone[] order){
        this.project=project;this.model=model;this.terrain=terrain;this.order=order;restWorld=world;restLocal=local;
        drive=Enumerable.Repeat(-1,model.Bones.Count).ToArray();
        for(int d=0;d<Driven.Length;d++)if(model.FindBone(Driven[d].Bone,false) is {} bone)drive[bone.Index]=d;
        for(int i=0;i<Joints.Length;i++){var b=model.FindBone(Joints[i],false);rest[i]=world[b.Index].Translation;}
        // The head particle sits at the top of the skull, 0.2 m along the neck->head direction.
        var up=rest[Head]-rest[Neck];rest[Head]+=up.LengthSquared()>1e-6f?Vector3.Normalize(up)*.2f:Vector3.UnitY*.2f;
        Array.Copy(rest,p,N);Array.Copy(rest,prev,N);
        length=Links.Select(l=>Vector3.Distance(rest[l.A],rest[l.B])).ToArray();
        range=Ranges.Select(r=>{float d=Vector3.Distance(rest[r.A],rest[r.B]);return (d*r.Min,d*r.Max);}).ToArray();
    }
    /// <summary>Starts a corpse from the last living world pose, or returns null (missing bones or budget full).</summary>
    public static TacticalRagdoll TryStart(ComponentCreatureModel owner,Matrix[] world,Matrix?[] local,ModelBone[] order,Vector3 velocity,Vector3 away){
        var model=owner.Model;var project=owner.Project;
        if(model is null||project is null||Joints.Any(j=>model.FindBone(j,false) is null))return null;
        var budget=s_active.GetOrCreateValue(project);lock(budget){if(budget.Active>=MaxActive)return null;budget.Active++;}
        var r=new TacticalRagdoll(project,model,project.FindSubsystem<SubsystemTerrain>(true),world,local,order){counted=true};
        // Initial motion: body velocity (bounded) plus a push away from the cause, stronger at the chest so it topples.
        velocity=velocity.LengthSquared()>36?Vector3.Normalize(velocity)*6:velocity;
        away=away.LengthSquared()>1e-6f?Vector3.Normalize(new Vector3(away.X,0,away.Z)):Vector3.Zero;
        for(int i=0;i<N;i++){float push=i is Spine or Neck or Head or ShoulderL or ShoulderR?1.6f:i==Pelvis?.6f:.9f;r.prev[i]=r.p[i]-(velocity+away*push)*Step;}
        return r;
    }
    public void Release(){if(!counted)return;counted=false;if(s_active.TryGetValue(project,out var budget))lock(budget)budget.Active=Math.Max(0,budget.Active-1);}
    public static int ActiveCount(GameEntitySystem.Project project)=>s_active.TryGetValue(project,out var budget)?budget.Active:0;

    public void Advance(float dt){
        if(Asleep)return;
        accumulator=Math.Min(accumulator+Math.Max(0,dt),Step*MaxSubSteps);
        while(accumulator>=Step){accumulator-=Step;Simulate();}
    }
    void Simulate(){
        age+=Step;float fastest=0;
        for(int i=0;i<N;i++){
            var v=(p[i]-prev[i])*Damping;prev[i]=p[i];p[i]+=v+Vector3.UnitY*Gravity*Step*Step;fastest=Math.Max(fastest,v.Length()/Step);
        }
        for(int k=0;k<Iterations;k++){
            for(int l=0;l<Links.Length;l++)Satisfy(Links[l].A,Links[l].B,length[l],length[l]);
            for(int r=0;r<Ranges.Length;r++)Satisfy(Ranges[r].A,Ranges[r].B,range[r].Min,range[r].Max);
            for(int i=0;i<N;i++)Collide(i);
        }
        still=fastest<.08f?still+Step:0;
        if(age>=Sleep||still>=.5f){Asleep=true;Release();}
    }
    void Satisfy(int a,int b,float min,float max){
        var d=p[b]-p[a];float len=d.Length();if(len<1e-6f)return;
        float target=Math.Clamp(len,min,max);if(target==len)return;
        var fix=d*((len-target)/len*.5f);p[a]+=fix;p[b]-=fix;
    }
    /// <summary>Pushes one particle out of every collision box it overlaps (expanded by its radius), along the
    /// shallowest axis; support contact removes sliding speed.</summary>
    void Collide(int i){
        float r=Radius[i];var q=p[i];
        int x0=Terrain.ToCell(q.X-r),x1=Terrain.ToCell(q.X+r),y0=Terrain.ToCell(q.Y-r),y1=Terrain.ToCell(q.Y+r),z0=Terrain.ToCell(q.Z-r),z1=Terrain.ToCell(q.Z+r);
        for(int x=x0;x<=x1;x++)for(int y=y0;y<=y1;y++)for(int z=z0;z<=z1;z++){
            if(y<0||y>255||terrain.Terrain.GetChunkAtCell(x,z) is null)continue;
            int value=terrain.Terrain.GetCellValue(x,y,z);var block=BlocksManager.Blocks[Terrain.ExtractContents(value)];
            if(block is null||!block.IsCollidable_(value))continue;
            foreach(var box in block.GetCustomCollisionBoxes(terrain,value)){
                var min=box.Min+new Vector3(x,y,z)-new Vector3(r);var max=box.Max+new Vector3(x,y,z)+new Vector3(r);
                if(q.X<=min.X||q.X>=max.X||q.Y<=min.Y||q.Y>=max.Y||q.Z<=min.Z||q.Z>=max.Z)continue;
                float up=max.Y-q.Y,down=q.Y-min.Y,east=max.X-q.X,west=q.X-min.X,south=max.Z-q.Z,north=q.Z-min.Z;
                float best=Math.Min(Math.Min(Math.Min(up,down),Math.Min(east,west)),Math.Min(south,north));
                if(best==up){q.Y=max.Y;var v=q-prev[i];prev[i]=new Vector3(q.X-v.X*.4f,q.Y,q.Z-v.Z*.4f);} // support: friction
                else if(best==down)q.Y=min.Y;else if(best==east)q.X=max.X;else if(best==west)q.X=min.X;else if(best==south)q.Z=max.Z;else q.Z=min.Z;
            }
        }
        p[i]=q;
    }
    static Matrix Frame(Vector3 dir,Vector3 side){
        var x=dir.LengthSquared()>1e-8f?Vector3.Normalize(dir):Vector3.UnitY;
        var z=Vector3.Cross(x,side);z=z.LengthSquared()>1e-8f?Vector3.Normalize(z):Vector3.Normalize(Vector3.Cross(x,MathF.Abs(x.Y)<.9f?Vector3.UnitY:Vector3.UnitX));
        var y=Vector3.Cross(z,x);
        return new Matrix(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1);
    }
    /// <summary>Writes local bone transforms for the current particle state (world space, like the living pose).</summary>
    public void Pose(Matrix?[] output,Matrix[] scratch){
        foreach(var bone in order){
            Matrix world;
            if(drive[bone.Index] is int d and >=0){
                var (_,s,e,a,b)=Driven[d];
                var R=Matrix.Transpose(Frame(rest[e]-rest[s],rest[b]-rest[a]))*Frame(p[e]-p[s],p[b]-p[a]);
                world=restWorld[bone.Index]*Matrix.CreateTranslation(-rest[s])*R*Matrix.CreateTranslation(p[s]);
            }else{
                var local=restLocal[bone.Index]??bone.Transform;
                world=bone.ParentBone is null?local:local*scratch[bone.ParentBone.Index];
            }
            scratch[bone.Index]=world;
            output[bone.Index]=bone.ParentBone is null?world:world*Matrix.Invert(scratch[bone.ParentBone.Index]);
        }
    }
    public Vector3 Joint(int i)=>p[i];
    public int JointCount=>N;
}
