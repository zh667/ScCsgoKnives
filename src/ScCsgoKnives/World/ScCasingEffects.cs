using System.Text.Json;
using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>CS2 mesh/attachment/event-driven cosmetic debris. No entities or saved state.</summary>
public sealed class ScCasingEffects {
    public sealed class ModelInfo {public string Model{get;set;} public string Texture{get;set;}}
    public sealed class Cue {
        public string Model{get;set;} public float At{get;set;} public string Bone{get;set;} public string Attachment{get;set;}
        public float[] Offset{get;set;} public float[] Rotation{get;set;} public float[] SpeedMin{get;set;} public float[] SpeedMax{get;set;}
    }
    public sealed class Data {public Dictionary<string,ModelInfo> Models{get;set;} public Dictionary<string,Cue[]> Cues{get;set;}}
    public static readonly Data Definitions=Read();
    static Data Read(){using var stream=typeof(ScCasingEffects).Assembly.GetManifestResourceStream("Game.AnimationData.cs2_casings.json");return JsonSerializer.Deserialize<Data>(stream);}
    public static IReadOnlyList<Cue> Events(string gun,string clip)=>Definitions.Cues.GetValueOrDefault(gun+":"+Cs2Rig.ResolvedClip(gun,clip))??[];
    sealed record Pending(ComponentPlayer Player,ComponentFirstPersonModel Model,IInventory Inventory,int Slot,string Gun,long Sequence,double At,Cue Cue);
    public sealed class Debris {public Vector3 Position,Velocity,Spin;public float Age,RestYaw;public int Bounces;public bool Resting;public string Model;}
    readonly List<Pending> m_pending=[];
    readonly List<Debris> m_live=[];
    readonly Dictionary<string,(BlockMesh Mesh,Texture2D Texture)> m_models=[];
    readonly HashSet<string> m_failed=[];
    readonly PrimitivesRenderer3D m_renderer=new();
    readonly Engine.Random m_random=new();
    readonly DrawBlockEnvironmentData m_environment=new(){Light=15,DrawBlockMode=DrawBlockMode.World};
    SubsystemTerrain m_terrain;
    public static int Limit=>ScResourcePolicy.Lite?16:48;
    public static float Lifetime=>ScResourcePolicy.Lite?1.2f:2.5f;
    // Bounded perspective compensation, shared by flight and rest. Close-to-eye shells must
    // not look huge while the same shell becomes a needle two metres away.
    public static float DisplayScaleAtDistance(float distance)=>Math.Clamp(1.4f*MathF.Sqrt(Math.Max(0,distance)),.8f,2.4f);
    public void Queue(ComponentPlayer p,ComponentFirstPersonModel model,string gun,string clip){
        foreach(var cue in Events(gun,clip)){
            if(m_pending.Count>=32)m_pending.RemoveAt(0);
            m_pending.Add(new(p,model,p.ComponentMiner.Inventory,p.ComponentMiner.Inventory.ActiveSlotIndex,gun,KnifeAnimationController.ActionToken(model),KnifeClock.Now+cue.At,cue));
        }
    }
    public void Clear(){m_pending.Clear();m_live.Clear();m_models.Clear();m_failed.Clear();}
    public static Vector3 AdvanceVelocity(Vector3 velocity,float dt)=>velocity-Vector3.UnitY*(600*Cs2Placement.InchesToEngine*Math.Clamp(dt,0,.05f));
    public void Update(float dt,SubsystemTerrain terrain,SubsystemAudio audio){
        m_terrain=terrain;
        double now=KnifeClock.Now;
        for(int i=m_pending.Count-1;i>=0;i--){
            var p=m_pending[i];
            if(p.Player.ComponentHealth.Health<=0 || p.Player.ComponentMiner.Inventory!=p.Inventory || p.Inventory.ActiveSlotIndex!=p.Slot
                || !ScGunCrosshair.HoldingGun(p.Player) || ScGunBlock.SpecOf(p.Player.ComponentMiner.ActiveBlockValue)?.Name!=p.Gun
                || KnifeAnimationController.ActionToken(p.Model)!=p.Sequence){m_pending.RemoveAt(i);continue;}
            if(now<p.At)continue;
            if(now-p.At<.2 && CsmcFirstPersonRenderer.TryGetCasingFrame(p.Player,p.Gun,p.Cue,out Matrix transform)){
                var cue=p.Cue;Vector3 speed=new(m_random.Float(Math.Min(cue.SpeedMin[0],cue.SpeedMax[0]),Math.Max(cue.SpeedMin[0],cue.SpeedMax[0])),
                    m_random.Float(Math.Min(cue.SpeedMin[1],cue.SpeedMax[1]),Math.Max(cue.SpeedMin[1],cue.SpeedMax[1])),m_random.Float(Math.Min(cue.SpeedMin[2],cue.SpeedMax[2]),Math.Max(cue.SpeedMin[2],cue.SpeedMax[2])));
                if(m_live.Count>=Limit)m_live.RemoveAt(0);
                m_live.Add(new(){Model=cue.Model,Position=transform.Translation,Velocity=Vector3.TransformNormal(speed,transform),Spin=new(m_random.Float(-12,12),m_random.Float(-12,12),m_random.Float(-12,12))});
            }
            m_pending.RemoveAt(i);
        }
        float step=Math.Clamp(dt,0,.05f);
        for(int i=m_live.Count-1;i>=0;i--){
            var d=m_live[i];d.Age+=Math.Max(0,dt);
            if(d.Age>Lifetime||!ScGrenadeState.Finite(d.Position)){m_live.RemoveAt(i);continue;}
            if(d.Resting)continue;
            d.Velocity=AdvanceVelocity(d.Velocity,step);Vector3 next=d.Position+d.Velocity*step;
            var hit=terrain.Raycast(d.Position,next,false,true,(v,_)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
            if(hit.HasValue){Vector3 n=CellFace.FaceToVector3(hit.Value.CellFace.Face);ResolveContact(d,hit.Value.HitPoint(),n);
                // The source particle defines debris collisions, not a mandatory tink on every bounce.
                // Keep cosmetic casings silent instead of adding a prominent, repetitive metal sound.
            }else d.Position=next;
        }
    }
    public void Draw(Camera camera){
        foreach(var d in m_live){if(Vector3.DistanceSquared(d.Position,camera.ViewPosition)>24*24 || m_failed.Contains(d.Model))continue;
            if(!m_models.TryGetValue(d.Model,out var model)){
                try {
                var info=Definitions.Models[d.Model];var source=ContentManager.Get<ObjModel>("Models/ScCsgoKnives/"+info.Model);var mesh=new BlockMesh();
                foreach(var part in source.Meshes)foreach(var p in part.MeshParts)mesh.AppendModelMeshPart(p,BlockMesh.GetBoneAbsoluteTransform(part.ParentBone),false,false,true,false,Color.White);
                model=(mesh,ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+info.Texture));m_models.Add(d.Model,model);
                } catch(Exception e){m_failed.Add(d.Model);KnifeDiagnostics.WarnOnce("casing/"+d.Model,"Casing resource: "+e.Message);continue;}
            }
            Matrix world=DebrisTransform(d,camera.ViewPosition);
            m_environment.InWorldMatrix=world;m_environment.SubsystemTerrain=m_terrain;
            if(m_terrain is not null)m_environment.Light=Terrain.ExtractLight(m_terrain.Terrain.GetCellValue(Terrain.ToCell(d.Position.X),Terrain.ToCell(d.Position.Y),Terrain.ToCell(d.Position.Z)));
            BlocksManager.DrawMeshBlock(m_renderer,model.Mesh,model.Texture,Color.White,1,ref world,m_environment);
        }
        m_renderer.Flush(camera.ViewProjectionMatrix);
    }
    public static void ResolveContact(Debris d,Vector3 point,Vector3 normal){
        d.Position=point+normal*.025f;
        d.Velocity=(d.Velocity-2*Vector3.Dot(d.Velocity,normal)*normal)*.32f;d.Bounces++;
        // All eight imported shells have their long axis along local Z. Rest it horizontally.
        // Hitting a wall twice must not freeze a shell in midair.
        if(normal.Y>.5f&&(d.Bounces>=(ScResourcePolicy.Lite?1:2)||d.Velocity.LengthSquared()<.5f)){
            d.Resting=true;d.RestYaw=d.Spin.Y*d.Age;d.Velocity=Vector3.Zero;
        }
    }
    public static Matrix DebrisTransform(Debris d,Vector3 eye)=>Matrix.CreateScale(DisplayScaleAtDistance(Vector3.Distance(d.Position,eye)))
        *(d.Resting?Matrix.CreateRotationY(d.RestYaw):Matrix.CreateFromYawPitchRoll(d.Spin.Y*d.Age,d.Spin.X*d.Age,d.Spin.Z*d.Age))*Matrix.CreateTranslation(d.Position);
}
