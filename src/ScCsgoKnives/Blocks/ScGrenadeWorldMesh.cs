namespace Game;

/// <summary>World item / projectile geometry from the source body, with viewmodel-only accessories removed.</summary>
public static class ScGrenadeWorldMesh {
    public sealed class Geometry {
        public Cs2SkinnedMesh.Vertex[] Vertices;
        public (string Material,int[] Indices)[] Parts;
    }
    public static Geometry Build(Cs2SkinnedMesh mesh,bool molotov,bool thrown) {
        bool Excluded(string joint) => molotov && joint.StartsWith("lighter",StringComparison.Ordinal)
            || thrown && joint is "pin" or "ring" or "handle";
        bool[] include=Enumerable.Range(0,mesh.Skinned.Length).Select(i=>!mesh.VertexUsesJoint(i,Excluded)).ToArray();
        var parts=new List<(string Material,int[] Indices)>();
        foreach (var part in mesh.Primitives) {
            if (part.Material.EndsWith("_flame",StringComparison.Ordinal)) continue;
            var indices=new List<int>();
            for (int i=0;i<part.Indices.Length;i+=3) {
                int a=part.Indices[i],b=part.Indices[i+1],c=part.Indices[i+2];
                if (include[a] && include[b] && include[c]) { indices.Add(a);indices.Add(b);indices.Add(c); }
            }
            if (indices.Count>0) parts.Add((part.Material,indices.ToArray()));
        }
        // Compact before normalizing bounds: a discarded lighter or pulled pin
        // must not shrink the bottle/body by expanding its hidden bounding box.
        int[] used=parts.SelectMany(p=>p.Indices).Distinct().Order().ToArray();
        var map=used.Select((old,index)=>(old,index)).ToDictionary(p=>p.old,p=>p.index);
        return new Geometry {Vertices=used.Select(i=>mesh.Skinned[i]).ToArray(),
            Parts=parts.Select(p=>(p.Material,p.Indices.Select(i=>map[i]).ToArray())).ToArray()};
    }
}
