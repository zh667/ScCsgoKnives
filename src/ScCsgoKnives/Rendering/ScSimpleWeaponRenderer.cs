using Engine;
using Engine.Graphics;
using System.IO;

namespace Game;

/// <summary>One color sample, geometric normals and scene lighting. No PBR maps are loaded.</summary>
public static class ScSimpleWeaponRenderer {
    static Shader s_shader;
    static bool s_failed;
    static string Source(string suffix) {
        var assembly = typeof(ScSimpleWeaponRenderer).Assembly;
        using var reader = new StreamReader(assembly.GetManifestResourceStream(
            assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal))));
        return reader.ReadToEnd();
    }
    static Shader Prepare(Texture2D texture, Matrix world, Matrix projection,
        in KnifePbrRenderer.Lighting lighting, float aperture) {
        if (s_failed) return null;
        try { s_shader ??= new Shader(Source("Shaders.KnifePbr.vsh"), Source("Shaders.WeaponSimple.psh")); }
        catch (Exception e) {
            s_failed = true;
            KnifeDiagnostics.WarnOnce("simple-shader", "Simplified material could not compile; using full material. " + e.Message);
            return null;
        }
        Display.DepthStencilState = DepthStencilState.Default;
        Display.RasterizerState = RasterizerState.CullNoneScissor;
        Display.BlendState = BlendState.Opaque;
        s_shader.GetParameter("u_baseColor", true).SetValue(texture);
        s_shader.GetParameter("u_baseSampler", true).SetValue(SamplerState.LinearWrap);
        s_shader.GetParameter("u_lightDir1", true).SetValue(lighting.Dir1);
        s_shader.GetParameter("u_lightDir2", true).SetValue(lighting.Dir2);
        s_shader.GetParameter("u_light", true).SetValue(lighting.Intensity);
        s_shader.GetParameter("u_scopeCutout", true).SetValue(new Vector2(aperture, projection.M22));
        s_shader.GetParameter("u_worldViewMatrix", true).SetValue(world);
        s_shader.GetParameter("u_worldViewProjectionMatrix", true).SetValue(world * projection);
        return s_shader;
    }
    public static bool DrawMesh(Cs2SkinnedMesh.Vertex[] vertices, int[] indices, Texture2D texture,
        Matrix world, Matrix projection, in KnifePbrRenderer.Lighting lighting, float aperture, bool rigid) {
        if (!KnifeDiagnostics.IsFinite(world)) return false;
        var shader = Prepare(texture, world, projection, in lighting, aperture);
        if (shader is null) return false;
        if (rigid) ScRigidBuffers.Draw(shader, vertices, indices);
        else Display.DrawUserIndexed(PrimitiveType.TriangleList, shader, Cs2SkinnedMesh.Declaration,
            vertices, 0, vertices.Length, indices, 0, indices.Length);
        return true;
    }
    public static bool DrawModel(Model model, Texture2D texture, Matrix world, Matrix projection,
        in KnifePbrRenderer.Lighting lighting, bool bones, float aperture) {
        foreach (var mesh in model.Meshes) {
            Matrix transform = bones ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone) * world : world;
            var shader = Prepare(texture, transform, projection, in lighting, aperture);
            if (shader is null) return false;
            foreach (var part in mesh.MeshParts)
                Display.DrawIndexed(PrimitiveType.TriangleList, shader, part.VertexBuffer, part.IndexBuffer, part.StartIndex, part.IndicesCount);
        }
        return true;
    }
}
