// Exports the actual runtime mesh builders without packaging, a game world or a GPU.
using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;

if (args.Length != 2 || args[1] is not ("baseline" or "verify")) throw new ArgumentException("SupplyModelCheck <output> <baseline|verify>");
Dispatcher.Initialize();
string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
bool baseline = args[1] == "baseline";
var report = new List<object>();
for (int kind = 0; kind < (baseline ? 8 : 13); kind++) {
    var mesh = ScSurvivalMesh.Build(kind);
    var icon = ScSurvivalMesh.InventoryMesh(mesh);
    void Check(bool ok, string name) { if (!ok) throw new Exception($"kind {kind}: {name}"); }
    Check(mesh.Vertices.Count > 0 && mesh.Vertices.Count < ushort.MaxValue, "vertex range");
    Check(mesh.Indices.Count % 3 == 0 && mesh.Indices.All(i => i < mesh.Vertices.Count), "index range");
    Check(mesh.Vertices.All(v => float.IsFinite(v.Position.LengthSquared()) && v.TextureCoordinates.X >= 0 && v.TextureCoordinates.X <= 1 && v.TextureCoordinates.Y >= 0 && v.TextureCoordinates.Y <= 1), "finite geometry and bounded UV");
    Check(icon.Vertices.All(v => v.IsEmissive) && mesh.Vertices.All(v => !v.IsEmissive), "independent UI lighting");
    Check(mesh.Indices.SequenceEqual(icon.Indices), "icon topology");
    int degenerate = 0;
    for (int i = 0; i < mesh.Indices.Count; i += 3) {
        Vector3 a = mesh.Vertices[mesh.Indices[i]].Position, b = mesh.Vertices[mesh.Indices[i + 1]].Position, c = mesh.Vertices[mesh.Indices[i + 2]].Position;
        if (Vector3.Cross(b-a,c-a).LengthSquared() < 1e-16f) degenerate++;
    }
    if (!baseline) Check(degenerate == 0, "no degenerate triangles");
    if (!baseline) Check(mesh.Indices.Count/6 <= (kind==6?3000:kind is 3 or 5?1600:1200), "authored triangle budget (excluding reverse copies)");
    var bounds = mesh.CalculateBoundingBox();
    if (kind == 6 && !baseline) Check(bounds.Min.X >= -.5f && bounds.Max.X <= .5f && bounds.Min.Y >= -.501f && bounds.Max.Y <= .51f && bounds.Min.Z >= -.5f && bounds.Max.Z <= .5f, "bench stays in cell");
    var block = kind == 6 ? (Block)new ScWeaponWorkbenchBlock() : new ScAmmoBlock();
    if (kind < 8) {
        var rotation = block.GetFirstPersonRotation(0) * (MathF.PI / 180);
        Matrix m = Matrix.CreateScale(block.GetFirstPersonScale(0)) * Matrix.CreateFromYawPitchRoll(rotation.Y,rotation.X,rotation.Z) * Matrix.CreateTranslation(block.GetFirstPersonOffset(0));
        Check(mesh.Vertices.All(v => Vector3.Transform(v.Position,m).Z < -.1f), "held model outside camera");
    }
    var vertices = mesh.Vertices.Select(v => new float[]{v.Position.X,v.Position.Y,v.Position.Z,v.TextureCoordinates.X,v.TextureCoordinates.Y,v.Color.R/255f,v.Color.G/255f,v.Color.B/255f,v.Color.A/255f}).ToArray();
    File.WriteAllText(Path.Combine(output,$"item{kind}.json"),JsonSerializer.Serialize(new{vertices,indices=mesh.Indices.ToArray()}));
    report.Add(new {kind,vertices=mesh.Vertices.Count,triangles=mesh.Indices.Count/3,degenerate,min=new[]{bounds.Min.X,bounds.Min.Y,bounds.Min.Z},max=new[]{bounds.Max.X,bounds.Max.Y,bounds.Max.Z}});
}
File.WriteAllText(Path.Combine(output,"geometry.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Exported {report.Count} actual runtime meshes; geometry, UV, icon and camera checks passed ({args[1]}).");
if (!baseline) {
    void Check(bool ok,string name){if(!ok)throw new Exception(name);}
    Check(TacticalItemMesh.RecruitRadioKind(0)==8 && TacticalItemMesh.RecruitRadioKind(1)==9 && TacticalItemMesh.RecruitRadioKind(2)==10,"legacy/CT/T mapping");
    Check(TacticalItemMesh.SquadRadioKind(0)==11 && TacticalItemMesh.SquadRadioKind(1)==12,"3/5-person mapping");
    // Exercise native CPU DrawMeshBlock batching with zero scene light and dark input tint.
    // The fake texture never goes to a GPU. This does not validate actual texture upload.
    var field=typeof(ScSurvivalMesh).GetField("s_surface",BindingFlags.NonPublic|BindingFlags.Static);
    var saved=field.GetValue(null);
    var texture=(Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
    float oldLight=LightingManager.LightIntensityByLightValue[15];
    int nativeDraws=0;
    try {
        field.SetValue(null,texture);LightingManager.LightIntensityByLightValue[15]=0;
        TacticalItemMesh.LoadRadios();TacticalItemMesh.LoadRadios(); // Idempotent second block initialization.
        var cases=new List<(Block Block,int Data,int Kind)>();
        cases.Add((new ScAmmoBlock(),0,0));cases.Add((new ScAmmoBlock(),1,1));
        for(int i=0;i<5;i++)cases.Add((new ScWeaponMaterialBlock(),i,i==4?7:i+2));
        cases.Add((new ScWeaponWorkbenchBlock(),0,6));
        for(int i=0;i<3;i++)cases.Add((new ScTacticalBeaconBlock(),i,8+i));
        for(int i=0;i<2;i++)cases.Add((new ScTacticalSquadBlock(),i,11+i));
        foreach(var (block,data,kind) in cases){
            // Workbench.Initialize touches base game registry; inject only its three mesh fields.
            if(kind==6)foreach(string name in new[]{"m_world","m_item","m_icon"})typeof(ScWeaponWorkbenchBlock).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(block,name=="m_icon"?ScSurvivalMesh.InventoryMesh(ScSurvivalMesh.Build(6)):ScSurvivalMesh.Build(6));
            int value=Terrain.MakeBlockValue(700,0,data);
            foreach(var mode in new[]{DrawBlockMode.World,DrawBlockMode.UI,DrawBlockMode.World,DrawBlockMode.UI}){
                var renderer=new PrimitivesRenderer3D();var matrix=Matrix.Identity;
                block.DrawBlock(renderer,value,new Color(20,20,20),1,ref matrix,new DrawBlockEnvironmentData{Light=15,DrawBlockMode=mode});
                var batch=renderer.TexturedBatch(texture,true,0,null,RasterizerState.CullCounterClockwiseScissor,null,SamplerState.PointClamp);
                Check(batch.TriangleVertices.Count==ScSurvivalMesh.Build(kind).Vertices.Count,$"native {kind} {mode}: emitted vertices");
                Check(batch.TriangleVertices.All(v=>v.Color.A==255),$"native {kind} {mode}: opaque");
                if(mode==DrawBlockMode.UI)Check(batch.TriangleVertices.All(v=>v.Color.R>=150 && v.Color.G>=150 && v.Color.B>=150),$"native {kind}: UI survives preceding dark world draw");
                nativeDraws++;
            }
        }
        var a=ScSurvivalMesh.Build(11);var b=ScSurvivalMesh.Build(12);
        Check(a.Indices.SequenceEqual(b.Indices) && a.Vertices.Zip(b.Vertices).All(p=>p.First.Position==p.Second.Position),"squad variants share geometry");
        Check(a.Vertices.Zip(b.Vertices).Any(p=>p.First.TextureCoordinates!=p.Second.TextureCoordinates),"squad labels have distinct UVs");
    } finally { field.SetValue(null,saved);LightingManager.LightIntensityByLightValue[15]=oldLight; }
    File.WriteAllText(Path.Combine(output,"native-draw.json"),JsonSerializer.Serialize(new{nativeDraws,radioMappings=true,distinctSquadLabels=true,zeroLightUi=true,scope="native CPU batching; no GPU upload, game world or installed package"},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"Passed {nativeDraws} native CPU draw batches, radio data mapping and distinct 3/5 labels.");
    var polish=new List<object>();
    ScPolishSelfTest.Run((name,ok,detail)=>{polish.Add(new{name,ok,detail});Check(ok,name+": "+detail);});
    File.WriteAllText(Path.Combine(output,"existing-polish.json"),JsonSerializer.Serialize(polish,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"Passed {polish.Count} existing ScPolishSelfTest checks.");
}
