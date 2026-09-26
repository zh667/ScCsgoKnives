using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine;
using Engine.Graphics;
using GameEntitySystem;
namespace Game;

// Queues only transforms; native renderer still controls when extras are flushed.
public static class ScNpcWeaponRenderer {
    [StructLayout(LayoutKind.Sequential)]
    struct Vertex {public Vector3 Position;public Color Color;public Vector2 UV;public float Emissive;}
    static readonly VertexDeclaration declaration=new(new VertexElement(0,VertexElementFormat.Vector3,"POSITION"),new VertexElement(12,VertexElementFormat.NormalizedByte4,"COLOR"),new VertexElement(16,VertexElementFormat.Vector2,"TEXCOORD"),new VertexElement(24,VertexElementFormat.Single,"EMISSIVE"));
    sealed class Vertices(BlockMesh source):VertexBuffer(declaration,source.Vertices.Count){
        public void Upload(){
            var data=new Vertex[source.Vertices.Count];for(int i=0;i<data.Length;i++){var v=source.Vertices.Array[i];data[i]=new(){Position=v.Position,Color=v.Color,UV=v.TextureCoordinates,Emissive=v.IsEmissive?1:0};}
            SetData(data,0,data.Length);
        }
        public override void HandleDeviceReset(){base.HandleDeviceReset();Upload();}
    }
    sealed class Indices(BlockMesh source):IndexBuffer(source.Vertices.Count<=65536?IndexFormat.SixteenBits:IndexFormat.ThirtyTwoBits,source.Indices.Count){
        public void Upload(){
            if(IndexFormat==IndexFormat.SixteenBits){var data=new ushort[source.Indices.Count];for(int i=0;i<data.Length;i++)data[i]=checked((ushort)source.Indices.Array[i]);SetData(data,0,data.Length);}
            else SetData(source.Indices.Array,0,source.Indices.Count);
        }
        public override void HandleDeviceReset(){base.HandleDeviceReset();Upload();}
    }
    sealed class Mesh:IDisposable {
        public readonly Vertices Vertices;
        public readonly Indices Indices;
        public readonly int Count;
        public Mesh(BlockMesh source){
            Count=source.Indices.Count;
            try{Vertices=new(source);Vertices.Upload();Indices=new(source);Indices.Upload();}catch{Dispose();throw;}
        }
        public void Dispose(){Vertices?.Dispose();Indices?.Dispose();}
    }
    readonly record struct Command(Mesh Mesh,Texture2D Texture,Matrix Transform,float Light,SamplerState Sampler,Project Project);
    sealed class Batch:BaseBatch {
        public readonly List<Command> Commands=new(128);
        public override bool IsEmpty()=>Commands.Count==0;
        public override void Clear()=>Commands.Clear();
        public override void Flush(Matrix matrix,Vector4 color,bool clearAfterFlush=true){
            try{
                Display.DepthStencilState=Engine.Graphics.DepthStencilState.Default;Display.RasterizerState=Engine.Graphics.RasterizerState.CullCounterClockwiseScissor;Display.BlendState=Engine.Graphics.BlendState.AlphaBlend;
                foreach(var c in Commands){
                    using var timing=ScTacticalPerformance.Measure(c.Project,ScTacticalPerformance.Stage.WeaponSubmit);
                    shader.GetParameter("u_worldViewProjectionMatrix").SetValue(c.Transform*matrix);shader.GetParameter("u_color").SetValue(color);shader.GetParameter("u_light").SetValue(c.Light);
                    shader.GetParameter("u_texture").SetValue(c.Texture);shader.GetParameter("u_samplerState").SetValue(c.Sampler);
                    Display.DrawIndexed(PrimitiveType.TriangleList,shader,c.Mesh.Vertices,c.Mesh.Indices,0,c.Mesh.Count);
                }
            }finally{if(clearAfterFlush)Clear();}
        }
    }
    static readonly Dictionary<BlockMesh,Mesh> meshes=new();
    static readonly ConditionalWeakTable<PrimitivesRenderer3D,Batch> batches=new();
    static Shader shader;
    static bool failed;
    public static int CachedMeshes=>meshes.Count;
    // If a driver rejects the shader, retain the native path without exact-size
    // reallocations for every additional part/actor in the growing crowd.
    public static void ReserveFallback(PrimitivesRenderer3D renderer,BlockMesh mesh,Texture2D texture,bool legacy){
        var batch=renderer.TexturedBatch(texture,true,0,null,RasterizerState.CullCounterClockwiseScissor,null,legacy?SamplerState.LinearWrap:SamplerState.PointClamp);
        int vertices=checked(batch.TriangleVertices.Count+mesh.Vertices.Count),indices=checked(batch.TriangleIndices.Count+mesh.Indices.Count);
        if(vertices>batch.TriangleVertices.Capacity)batch.TriangleVertices.Capacity=Math.Max(vertices,checked(batch.TriangleVertices.Capacity*2));
        if(indices>batch.TriangleIndices.Capacity)batch.TriangleIndices.Capacity=Math.Max(indices,checked(batch.TriangleIndices.Capacity*2));
    }
    public static void Clear(){
        foreach(var pair in batches){pair.Value.Clear();pair.Key.m_allBatches.Remove(pair.Value);}batches.Clear();
        foreach(var mesh in meshes.Values)mesh.Dispose();meshes.Clear();shader?.Dispose();shader=null;failed=false;
    }
    public static bool Queue(PrimitivesRenderer3D renderer,BlockMesh mesh,Texture2D texture,Matrix transform,int light,bool legacy,Project project=null){
        if(failed)return false;if(mesh.Indices.Count==0)return true;
        try{
            if(shader==null)shader=new Shader(VertexShader,PixelShader);
            if(!meshes.TryGetValue(mesh,out var gpu)){
                using var timing=ScTacticalPerformance.Measure(project,ScTacticalPerformance.Stage.WeaponUpload);
                gpu=new(mesh);meshes.Add(mesh,gpu);
            }
            var batch=batches.GetValue(renderer,static r=>{var b=new Batch{Layer=0};r.m_allBatches.Add(b);r.m_sortNeeded=true;return b;});
            batch.Commands.Add(new(gpu,texture,transform,LightingManager.LightIntensityByLightValue[Math.Clamp(light,0,15)],legacy?SamplerState.LinearWrap:SamplerState.PointClamp,project));return true;
        }catch(Exception e){failed=true;KnifeDiagnostics.WarnOnce("npc-gpu",$"NPC static buffers unavailable: {e.Message}; using original renderer.");return false;}
    }
    // The old CPU path truncates lighting to byte precision before interpolation.
    const string VertexShader="""
        #ifdef HLSL
        float4x4 u_worldViewProjectionMatrix;
        float4 u_color;
        float u_light;
        void main(in float3 a_position:POSITION,in float4 a_color:COLOR,in float2 a_texcoord:TEXCOORD,in float a_emissive:EMISSIVE,out float2 v_texcoord:TEXCOORD,out float4 v_color:COLOR,out float4 sv_position:SV_POSITION){
            v_color=float4(floor(a_color.rgb*255.0*(a_emissive>0.5?1.0:u_light))/255.0,a_color.a)*u_color;
            v_texcoord=a_texcoord;sv_position=mul(float4(a_position,1.0),u_worldViewProjectionMatrix);
        }
        #endif
        #ifdef GLSL
        // <Semantic Name='POSITION' Attribute='a_position' />
        // <Semantic Name='COLOR' Attribute='a_color' />
        // <Semantic Name='TEXCOORD' Attribute='a_texcoord' />
        // <Semantic Name='EMISSIVE' Attribute='a_emissive' />
        uniform mat4 u_worldViewProjectionMatrix;
        uniform vec4 u_color;
        uniform float u_light;
        attribute vec3 a_position;
        attribute vec4 a_color;
        attribute vec2 a_texcoord;
        attribute float a_emissive;
        varying vec2 v_texcoord;
        varying vec4 v_color;
        void main(){
            v_color=vec4(floor(a_color.rgb*255.0*(a_emissive>0.5?1.0:u_light))/255.0,a_color.a)*u_color;
            v_texcoord=a_texcoord;gl_Position=u_worldViewProjectionMatrix*vec4(a_position,1.0);
            OPENGL_POSITION_FIX;
        }
        #endif
        """;
    const string PixelShader="""
        #ifdef HLSL
        Texture2D u_texture;
        SamplerState u_samplerState;
        void main(in float2 v_texcoord:TEXCOORD,in float4 v_color:COLOR,out float4 svTarget:SV_TARGET){float4 c=v_color*u_texture.Sample(u_samplerState,v_texcoord);if(c.a<=0.0)discard;svTarget=c;}
        #endif
        #ifdef GLSL
        // <Sampler Name='u_samplerState' Texture='u_texture' />
        #ifdef GL_ES
        precision mediump float;
        #endif
        uniform sampler2D u_texture;
        varying vec2 v_texcoord;
        varying vec4 v_color;
        void main(){vec4 c=v_color*texture2D(u_texture,v_texcoord);if(c.a<=0.0)discard;gl_FragColor=c;}
        #endif
        """;
}
