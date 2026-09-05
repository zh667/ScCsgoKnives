using Engine;

namespace Game;

/// <summary>Bakes a posed CS2 weapon into a normalized inventory/world mesh.</summary>
public static class Cs2BlockMesh {
    public static void Append(BlockMesh target, Cs2SkinnedMesh.Vertex[] vertices, IEnumerable<int> indices) {
        if (vertices.Length == 0 || vertices.Length > ushort.MaxValue)
            throw new InvalidOperationException("CS2 block mesh has an unsupported vertex count.");
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var v in vertices) { lo = Vector3.Min(lo, v.Position); hi = Vector3.Max(hi, v.Position); }
        Vector3 size = hi - lo, center = (lo + hi) * 0.5f;
        float extent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (!float.IsFinite(extent) || extent <= 0.0001f)
            throw new InvalidOperationException("CS2 block mesh has invalid bounds.");
        foreach (var v in vertices)
            target.Vertices.Add(new BlockMeshVertex {
                Position = (v.Position - center) / extent,
                Color = Color.White, TextureCoordinates = v.TextureCoordinate
            });
        foreach (int i in indices) target.Indices.Add(checked((ushort)i));
    }
}
