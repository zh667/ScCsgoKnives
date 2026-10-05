using System;
using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Small closed, shaded meshes for survival supplies. No weapon/hand resources are replaced.</summary>
public static class ScSurvivalMesh {
    public const string Texture = "Textures/ScCsgoKnives/survival_surface";
    public static BlockMesh InventoryMesh(BlockMesh source) {
        var mesh = new BlockMesh();
        foreach (var v in source.Vertices) {
            var copy = v;
            // UI has baked directional shading, independent of scene light.
            copy.IsEmissive = true;
            mesh.Vertices.Add(copy);
        }
        foreach (int i in source.Indices) mesh.Indices.Add(i);
        return mesh;
    }
    static Texture2D s_surface;
    /// <summary>Managed thread ids: where Preload ran (the main thread) and where the texture was actually created.</summary>
    public static int MainThread { get; private set; } = -1;
    public static int SurfaceThread { get; private set; } = -1;
    public static bool SurfaceDispatched { get; private set; }

    /// <summary>
    /// The shared supply atlas, created once on the main thread.
    ///
    /// Engine.Graphics.Texture2D.Load calls GL.GenTextures / TexImage2D on whatever
    /// thread asks (no Dispatcher, no thread check - read off Engine.dll), and
    /// ContentManager caches whatever comes back. The bench's GenerateTerrainVertices
    /// runs on the terrain updater's worker thread, so a world loaded with a bench
    /// already placed made that thread the first to ask for this texture: a GL object
    /// with no context behind it, cached for the session, sampled as black by the
    /// placed benches, the held bench and every supply icon. Preload() from
    /// Block.Initialize resolves it on the main thread first; a worker that still finds
    /// it missing hands the load to the main thread and waits.
    /// </summary>
    public static Texture2D Surface {
        get {
            if (s_surface is not null) return s_surface;
            if (MainThread < 0 || Environment.CurrentManagedThreadId == MainThread) {
                s_surface = ContentManager.Get<Texture2D>(Texture);
                SurfaceThread = Environment.CurrentManagedThreadId;
                return s_surface;
            }
            Dispatcher.Dispatch(() => {
                if (s_surface is null) {
                    s_surface = ContentManager.Get<Texture2D>(Texture);
                    SurfaceThread = Environment.CurrentManagedThreadId;
                    SurfaceDispatched = true;
                }
            }, waitUntilCompleted: true);
            return s_surface;
        }
    }

    /// <summary>Called from the blocks' Initialize (main thread, before any world exists).</summary>
    public static void Preload() {
        if (MainThread < 0) MainThread = Environment.CurrentManagedThreadId;
        _ = Surface;
    }
    // 0..7: supplies; 8..12: legacy/CT/T/three-person/five-person radios.
    public static BlockMesh Build(int kind) => ScSupplyGeometry.Build(kind);
}

// This base has no static Index, so the API still allocates each concrete block dynamically.
public abstract class ScSupplyBlock : ScNoDurabilityBlock {
    protected ScSupplyBlock() {
        FirstPersonScale = .3f; FirstPersonOffset = new(.4f, -.3f, -.55f); FirstPersonRotation = new(0, 25, 0);
        InHandScale = .4f; InHandOffset = new(0, .1f, -.2f);
    }
    readonly Dictionary<int,BlockMesh> m_meshes=[];
    readonly Dictionary<int,BlockMesh> m_icons=[];
    protected abstract int MeshKind(int value);
    public override void Initialize() { ScSurvivalMesh.Preload(); base.Initialize(); }
    public override Texture2D GetDefaultTexture(int value) => ScSurvivalMesh.Surface;
    public override Vector3 GetIconViewOffset(int value,DrawBlockEnvironmentData env) => new(1.1f,.8f,2);
    public override void DrawBlock(PrimitivesRenderer3D renderer,int value,Color color,float size,ref Matrix matrix,DrawBlockEnvironmentData env) {
        int kind=MeshKind(value);
        if(!m_meshes.TryGetValue(kind,out var mesh)) {
            // Build the UI copy at the same time as the world copy.  The engine's
            // mesh draw path is allowed to apply lighting/state to the supplied
            // BlockMesh; creating the icon lazily meant a dark world draw (most
            // visibly after placing the workbench and loading the world again)
            // could bake black vertex colours into the subsequently cached icon.
            mesh=ScSurvivalMesh.Build(kind);
            m_meshes[kind]=mesh;
            m_icons[kind]=ScSurvivalMesh.InventoryMesh(mesh);
        }
        if(env?.DrawBlockMode == DrawBlockMode.UI) mesh=m_icons[kind];
        Texture2D texture=GetDefaultTexture(value);
        // UI icons must not inherit the world/slot tint.  On reload the engine
        // can pass a dark lighting colour here (the diagnostic showed 56/71),
        // which multiplies the atlas into a black icon even though the texture
        // and vertices are valid.
        Color drawColor=env?.DrawBlockMode == DrawBlockMode.UI ? Color.White : color;
        BlocksManager.DrawMeshBlock(renderer,mesh,texture,drawColor,size,ref matrix,env);
    }
    public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int value,int x,int y,int z) { }
}
