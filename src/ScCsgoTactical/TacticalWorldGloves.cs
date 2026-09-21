using Engine;
using Engine.Graphics;
namespace Game;

// One binding per player. Shared mesh scratch is consumed immediately into native batches.
// The source world meshes keep their own inverse binds and the full finger hierarchy.
public sealed class TacticalWorldGloves {
    public string Key {get;}
    public Model Model {get;}
    public Cs2SkinnedMesh Mesh {get;}
    public int[] BodyMeshOrders {get;}
    readonly int[] joints;
    readonly Texture2D[] textures;
    readonly Cs2Rig.Pose pose=new(){Bones=new(StringComparer.Ordinal)};
    public TacticalWorldGloves(Model model,TacticalArms.Glove glove){
        Model=model;Key=glove.Key;Mesh=TacticalArms.Mesh("world_"+glove.Mesh);
        joints=Mesh.Joints.Select(n=>model.FindBone(n,false)?.Index??-1).ToArray();
        foreach(string n in Mesh.Joints)pose.Bones[n]=Matrix.Identity;
        for(int i=0;i<joints.Length;i++)if(joints[i]<0)pose.Bones.Remove(Mesh.Joints[i]);
        if(Mesh.UnresolvedWeight(pose)>.001f)throw new InvalidOperationException("World glove skeleton mismatch.");
        BodyMeshOrders=Enumerable.Range(0,model.Meshes.Count).Where(i=>!model.Meshes[i].Name.Contains("thirdperson_default_gloves",StringComparison.Ordinal)).ToArray();
        if(BodyMeshOrders.Length==model.Meshes.Count)throw new InvalidOperationException("No separate default glove mesh.");
        textures=Mesh.Primitives.Select(p=>ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/tactical_arm_"+glove.Key+(p.Material.EndsWith("left")?"_left":"_right"))).ToArray();
    }
    public void Sample(Matrix[] absolute){
        // .skin local coordinates are inches; native model bones are in metres.
        for(int i=0;i<joints.Length;i++)if(joints[i]>=0)pose.Bones[Mesh.Joints[i]]=Matrix.CreateScale(.0254f)*absolute[joints[i]];
        Mesh.SetPose(pose,Matrix.Identity);Mesh.Skin();
    }
    public void Draw(ComponentHumanModel human,Camera camera){
        if(camera.GameWidget.IsEntityFirstPersonTarget(human.Entity))return;
        Sample(human.AbsoluteBoneTransformsForCamera);
        var renderer=human.m_subsystemModelsRenderer;
        // Match the body's smooth, cached sample. The root lies on terrain: sampling its
        // single cell alternates between dark ground and lit air at surface boundaries.
        float light=renderer.m_componentModels.TryGetValue(human,out var data)?data.Light:
            renderer.CalculateModelLight(new(){ComponentModel=human,ComponentBody=human.m_componentCreature.ComponentBody})
            ??renderer.m_subsystemSky.SkyLightIntensity;
        Queue(renderer.PrimitivesRenderer,camera.InvertedViewMatrix,light,human.Opacity??1);
    }
    public void Queue(PrimitivesRenderer3D renderer,Matrix invertedView,float light,float opacity){
        for(int p=0;p<Mesh.Primitives.Length;p++){
            var batch=renderer.TexturedBatch(textures[p],false,0,opacity<1?DepthStencilState.DepthRead:DepthStencilState.Default,RasterizerState.CullNoneScissor,opacity<1?BlendState.AlphaBlend:BlendState.Opaque,SamplerState.LinearWrap);
            int start=batch.TriangleVertices.Count;
            foreach(var vertex in Mesh.Skinned){
                var normal=Vector3.Normalize(Vector3.TransformNormal(vertex.Normal,invertedView));
                float intensity=Math.Clamp(light*(LightingManager.LightAmbient+Math.Max(0,Vector3.Dot(normal,LightingManager.DirectionToLight1))+Math.Max(0,Vector3.Dot(normal,LightingManager.DirectionToLight2))),0,1);
                batch.TriangleVertices.Add(new(vertex.Position,new Color(intensity,intensity,intensity,opacity),vertex.TextureCoordinate));
            }
            foreach(int index in Mesh.Primitives[p].Indices)batch.TriangleIndices.Add(start+index);
        }
    }
}
