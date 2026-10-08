using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;

static class AirdropMaterialRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly core,Assembly tactical){
        List<Result> results=[];
        void Test(string name,Action check){try{check();results.Add(new(name,true,""));}catch(Exception e){results.Add(new(name,false,e.ToString()));}}
        void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        var type=core.GetType("Game.TacticalItemMesh")??tactical.GetType("Game.TacticalItemMesh",true);
        var draw=type.GetMethod("DrawCrate");
        var texture=(Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));GC.SuppressFinalize(texture);
        var mesh=new BlockMesh();
        for(int i=0;i<3;i++)mesh.Vertices.Add(new(){Position=new(i*.25f,i*.1f,-1-i*.15f),
            TextureCoordinates=new(i*.2f,i*.3f),Color=new(171,137,213,255),IsEmissive=i==1});
        foreach(int index in new[]{0,1,2})mesh.Indices.Add(index);
        void Draw(PrimitivesRenderer3D renderer,DrawBlockEnvironmentData env,Matrix matrix,float size,Color color){
            Require(draw!=null,"Missing dedicated opaque crate draw path");
            draw.Invoke(null,[renderer,mesh,texture,color,size,matrix,env]);
        }
        Test("airdrop-material-opaque-not-alpha-test-or-background-blend",()=>{
            var renderer=new PrimitivesRenderer3D();Draw(renderer,new(){Light=15},Matrix.Identity,1,Color.White);
            var batch=renderer.TexturedBatches.Single();
            Require(!batch.UseAlphaTest&&ReferenceEquals(batch.BlendState,BlendState.Opaque),"Source alpha still clips/blends the crate");
            Require(ReferenceEquals(batch.Texture,texture)&&ReferenceEquals(batch.DepthStencilState,DepthStencilState.Default)
                &&ReferenceEquals(batch.RasterizerState,RasterizerState.CullCounterClockwiseScissor)
                &&ReferenceEquals(batch.SamplerState,SamplerState.PointClamp),"Texture/depth/culling/sampling changed");
        });
        foreach(string scenario in new[]{"world-light","emissive-ui","projected-view","null-environment"})Test("airdrop-native-geometry-and-colour-"+scenario,()=>{
            var env=scenario=="null-environment"?null:new DrawBlockEnvironmentData{Light=scenario=="world-light"?4:scenario=="emissive-ui"?0:15};
            if(scenario=="projected-view")env.ViewProjectionMatrix=Matrix.CreatePerspectiveFieldOfView(.9f,1.6f,.1f,100);
            var transform=Matrix.CreateRotationY(.4f)*Matrix.CreateTranslation(.2f,-.3f,-2);
            var expected=new PrimitivesRenderer3D();var actual=new PrimitivesRenderer3D();
            var tint=new Color(123,205,171,255);
            BlocksManager.DrawMeshBlock(expected,mesh,texture,tint,.4f,ref transform,env);
            Draw(actual,env,transform,.4f,tint);
            Require(expected.TexturedBatches.Single().TriangleVertices.SequenceEqual(actual.TexturedBatches.Single().TriangleVertices),"Native transforms/lighting/UVs differ");
            Require(expected.TexturedBatches.Single().TriangleIndices.SequenceEqual(actual.TexturedBatches.Single().TriangleIndices),"Native topology differs");
        });
        Test("airdrop-batch-appends-with-correct-index-base",()=>{
            var renderer=new PrimitivesRenderer3D();var before=mesh.Vertices.ToArray();
            Draw(renderer,new(){Light=15},Matrix.Identity,1,Color.White);Draw(renderer,new(){Light=15},Matrix.Identity,1,Color.White);
            var batch=renderer.TexturedBatches.Single();
            Require(batch.TriangleVertices.Count==6&&batch.TriangleIndices.SequenceEqual(new[]{0,1,2,3,4,5}),"Appended mesh corrupts index base");
            Require(before.SequenceEqual(mesh.Vertices),"Draw mutates cached mesh");
        });
        Test("airdrop-descent-opaque-other-tactical-items-unchanged",()=>{
            var items=(IDictionary<string,(BlockMesh Mesh,Texture2D Texture)>)type.GetField("Items").GetValue(null);
            var forward=type.GetMethod("Draw");
            foreach(string name in new[]{"airdrop","repair_item","defuser_item"}){
                bool existed=items.TryGetValue(name,out var original);items[name]=(mesh,texture);
                try{
                    var renderer=new PrimitivesRenderer3D();forward.Invoke(null,[name,renderer,Color.White,1f,Matrix.Identity,new DrawBlockEnvironmentData{Light=15}]);
                    var batch=renderer.TexturedBatches.Single();bool crate=name=="airdrop";
                    Require(batch.UseAlphaTest==!crate&&ReferenceEquals(batch.BlendState,crate?BlendState.Opaque:BlendState.AlphaBlend),"Wrong material path for "+name);
                }finally{if(existed)items[name]=original;else items.Remove(name);}
            }
        });
        return results;
    }
}
