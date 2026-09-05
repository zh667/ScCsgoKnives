using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Engine.Graphics;

namespace Game;

/// <summary>
/// The knife's metallic-roughness shader (Shaders/KnifePbr.vsh + .psh, embedded).
/// Survivalcraft's <see cref="Shader"/> compiles GLSL ES straight from source, so
/// the mod needs no other mod and no newer API for PBR. Construction compiles the
/// program and throws with the driver's log if it fails; the renderer catches that
/// and falls back to the plain lit shader.
/// </summary>
public sealed class KnifePbrShader : Shader {
    public readonly ShaderParameter WorldViewProjection;
    public readonly ShaderParameter WorldView;
    public readonly ShaderParameter ViewToWorld;
    public readonly ShaderParameter BaseColor;
    public readonly ShaderParameter Orm;
    public readonly ShaderParameter NormalMap;
    public readonly ShaderParameter Env;
    public readonly ShaderParameter Brdf;
    public readonly ShaderParameter BaseSampler;
    public readonly ShaderParameter OrmSampler;
    public readonly ShaderParameter NormalSampler;
    public readonly ShaderParameter EnvSampler;
    public readonly ShaderParameter BrdfSampler;
    public readonly ShaderParameter LightDir1;
    public readonly ShaderParameter LightDir2;
    public readonly ShaderParameter LightColor1;
    public readonly ShaderParameter LightColor2;
    public readonly ShaderParameter Params;
    public readonly ShaderParameter Params2;
    public readonly ShaderParameter ScopeCutout;

    KnifePbrShader(string vertexSource, string pixelSource) : base(vertexSource, pixelSource) {
        WorldViewProjection = GetParameter("u_worldViewProjectionMatrix", true);
        WorldView = GetParameter("u_worldViewMatrix", true);
        ViewToWorld = GetParameter("u_viewToWorld", true);
        BaseColor = GetParameter("u_baseColor", true);
        Orm = GetParameter("u_orm", true);
        NormalMap = GetParameter("u_normalMap", true);
        Env = GetParameter("u_env", true);
        Brdf = GetParameter("u_brdf", true);
        BaseSampler = GetParameter("u_baseSampler", true);
        OrmSampler = GetParameter("u_ormSampler", true);
        NormalSampler = GetParameter("u_normalSampler", true);
        EnvSampler = GetParameter("u_envSampler", true);
        BrdfSampler = GetParameter("u_brdfSampler", true);
        LightDir1 = GetParameter("u_lightDir1", true);
        LightDir2 = GetParameter("u_lightDir2", true);
        LightColor1 = GetParameter("u_lightColor1", true);
        LightColor2 = GetParameter("u_lightColor2", true);
        Params = GetParameter("u_params", true);
        Params2 = GetParameter("u_params2", true);
        ScopeCutout = GetParameter("u_scopeCutout", true);
    }

    public static KnifePbrShader Create() =>
        new(ReadResource("Shaders.KnifePbr.vsh"), ReadResource("Shaders.KnifePbr.psh"));

    static string ReadResource(string suffix) {
        Assembly assembly = typeof(KnifePbrShader).Assembly;
        string name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"embedded shader '{suffix}' is missing from the mod assembly.");
        using Stream stream = assembly.GetManifestResourceStream(name);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
