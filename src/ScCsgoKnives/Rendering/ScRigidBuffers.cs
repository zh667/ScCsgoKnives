using Engine.Graphics;
namespace Game;
// Bounded cache: the engine strongly registers graphics resources, so eviction must dispose them.
public static class ScRigidBuffers {
    sealed class Vertices(Cs2SkinnedMesh.Vertex[] data) : VertexBuffer(Cs2SkinnedMesh.Declaration, data.Length) {
        public override void HandleDeviceReset() { base.HandleDeviceReset(); SetData(data, 0, data.Length); }
    }
    sealed class Indices(int[] data) : IndexBuffer(IndexFormat.ThirtyTwoBits, data.Length) {
        public override void HandleDeviceReset() { base.HandleDeviceReset(); SetData(data, 0, data.Length); }
    }
    sealed class Entry {
        public Vertices Buffer;
        public readonly Dictionary<int[], Indices> Parts = new();
        public long Used;
        public void Dispose() { Buffer.Dispose(); foreach (var buffer in Parts.Values) buffer.Dispose(); }
    }
    static readonly Dictionary<Cs2SkinnedMesh.Vertex[], Entry> s_meshes = new();
    static long s_clock;
    public static void Clear() {
        foreach (var entry in s_meshes.Values) entry.Dispose();
        s_meshes.Clear();
    }
    public static void Draw(Shader shader, Cs2SkinnedMesh.Vertex[] vertices, int[] indices) {
        if (!s_meshes.TryGetValue(vertices, out var entry)) {
            if (s_meshes.Count >= 8) {
                var oldest = s_meshes.MinBy(p => p.Value.Used);
                oldest.Value.Dispose(); s_meshes.Remove(oldest.Key);
            }
            var buffer = new Vertices(vertices);
            try { buffer.SetData(vertices, 0, vertices.Length); }
            catch { buffer.Dispose(); throw; }
            s_meshes[vertices] = entry = new Entry { Buffer = buffer };
        }
        entry.Used = ++s_clock;
        if (!entry.Parts.TryGetValue(indices, out var ib)) {
            ib = new Indices(indices);
            try { ib.SetData(indices, 0, indices.Length); }
            catch { ib.Dispose(); throw; }
            entry.Parts[indices] = ib;
        }
        Display.DrawIndexed(PrimitiveType.TriangleList, shader, entry.Buffer, ib, 0, indices.Length);
    }
}
