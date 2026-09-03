using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Draws knife meshes with <see cref="KnifePbrShader"/>: CS's own factory-finish
/// roughness / metalness / occlusion / normal maps, lit by CS:MC's prefiltered
/// studio environment plus Survivalcraft's directional lights. Used by the
/// first-person renderer (geometry in view space, identity view) and by the
/// inspect screen (same call, its own camera).
///
/// Everything degrades to the caller's plain-shader path: a shader that fails to
/// compile on this device, or a knife without PBR maps, just returns false.
/// Failures are logged once with the driver's message so a remote log is enough
/// to diagnose them.
/// </summary>
public static class KnifePbrRenderer {
    public struct Lighting {
        /// <summary>Unit vectors in view space, pointing towards each light.</summary>
        public Vector3 Dir1, Dir2;
        /// <summary>Survivalcraft's item light, 0..1: scales the environment and the lights.</summary>
        public float Intensity;
    }

    const string TextureRoot = "Textures/ScCsgoKnives/";

    static KnifePbrShader s_shader;
    static bool s_shaderFailed;
    static Texture2D s_env, s_brdf;
    static bool s_sharedFailed;
    static readonly Dictionary<int, (Texture2D Orm, Texture2D Normal)> s_variantTextures = new();
    static readonly HashSet<int> s_variantFailed = [];
    static bool s_announced;

    /// <summary>False once the shader or the shared maps failed, or when tuning turns PBR off.</summary>
    public static bool Enabled => KnifeTuning.PbrEnabled > 0.5f && !s_shaderFailed && !s_sharedFailed;

    public static string Status =>
        s_shaderFailed ? "shader failed" : s_sharedFailed ? "environment maps missing" : s_shader is null ? "not compiled yet" : "active";

    /// <summary>
    /// Lights for the first-person pass: Survivalcraft's two fixed light directions
    /// moved into view space, scaled by the held item's light value.
    /// </summary>
    public static Lighting FirstPersonLighting(Camera camera, float light) => new() {
        Dir1 = Vector3.Normalize(Vector3.TransformNormal(LightingManager.DirectionToLight1, camera.ViewMatrix)),
        Dir2 = Vector3.Normalize(Vector3.TransformNormal(LightingManager.DirectionToLight2, camera.ViewMatrix)),
        Intensity = light,
    };

    static bool EnsureShared() {
        if (s_shaderFailed || s_sharedFailed) return false;
        if (s_shader is null) {
            try {
                s_shader = KnifePbrShader.Create();
            }
            catch (Exception e) {
                s_shaderFailed = true;
                KnifeLog.Error($"[ScCsgoKnives] PBR shader did not compile on this device; knives use the plain shader. {e.GetType().Name}: {e.Message}");
                return false;
            }
        }
        if (s_env is null || s_brdf is null) {
            try {
                s_env = ContentManager.Get<Texture2D>(TextureRoot + "env_specular_rgbm");
                s_brdf = ContentManager.Get<Texture2D>(TextureRoot + "env_brdf");
            }
            catch (Exception e) {
                s_sharedFailed = true;
                KnifeLog.Error($"[ScCsgoKnives] PBR environment maps failed to load; knives use the plain shader. {e.Message}");
                return false;
            }
        }
        if (!s_announced) {
            s_announced = true;
            KnifeLog.Information($"[ScCsgoKnives] PBR shader compiled; env {s_env.Width}x{s_env.Height}, brdf {s_brdf.Width}x{s_brdf.Height}.");
        }
        return true;
    }

    static bool TryGetVariantTextures(int variant, out Texture2D orm, out Texture2D normal) {
        if (s_variantTextures.TryGetValue(variant, out var cached)) {
            (orm, normal) = cached;
            return true;
        }
        orm = normal = null;
        if (s_variantFailed.Contains(variant)) return false;
        string asset = CsmcKnifeRig.GetAssetName(variant);
        try {
            orm = ContentManager.Get<Texture2D>($"{TextureRoot}{asset}_orm");
            normal = ContentManager.Get<Texture2D>($"{TextureRoot}{asset}_normal");
            s_variantTextures[variant] = (orm, normal);
            return true;
        }
        catch (Exception e) {
            s_variantFailed.Add(variant);
            KnifeDiagnostics.WarnOnce($"pbr-textures-{asset}", $"{asset} has no PBR maps ({e.Message}); it uses the plain shader.");
            return false;
        }
    }

    /// <summary>
    /// Draws one knife part. <paramref name="world"/> already contains the view
    /// (the renderer works in view space); <paramref name="viewToWorld"/> is the
    /// camera's inverted view matrix, which keeps the reflected environment fixed
    /// to the world so it sweeps across the blade as the player turns.
    /// Returns false when PBR is unavailable so the caller can draw its own way.
    /// </summary>
    public static bool TryDrawPart(Model model, Texture2D baseColor, int variant, Matrix world, Matrix projection,
        Matrix viewToWorld, in Lighting lighting, bool applyBoneTransform) {
        if (!Enabled || baseColor is null || model is null) return false;
        if (!EnsureShared() || !TryGetVariantTextures(variant, out Texture2D orm, out Texture2D normal)) return false;
        if (!KnifeDiagnostics.IsFinite(world)) return false;

        Display.DepthStencilState = DepthStencilState.Default;
        Display.RasterizerState = RasterizerState.CullNoneScissor;

        KnifePbrShader shader = s_shader;
        shader.BaseColor.SetValue(baseColor);
        shader.Orm.SetValue(orm);
        shader.NormalMap.SetValue(normal);
        shader.Env.SetValue(s_env);
        shader.Brdf.SetValue(s_brdf);
        shader.BaseSampler.SetValue(SamplerState.LinearClamp);
        shader.OrmSampler.SetValue(SamplerState.LinearClamp);
        shader.NormalSampler.SetValue(SamplerState.LinearClamp);
        shader.EnvSampler.SetValue(SamplerState.LinearClamp);
        shader.BrdfSampler.SetValue(SamplerState.LinearClamp);

        shader.ViewToWorld.SetValue(viewToWorld);
        shader.LightDir1.SetValue(lighting.Dir1);
        shader.LightDir2.SetValue(lighting.Dir2);
        float direct = lighting.Intensity * KnifeTuning.PbrDirectIntensity;
        shader.LightColor1.SetValue(new Vector3(direct));
        shader.LightColor2.SetValue(new Vector3(direct));
        shader.Params.SetValue(new Vector4(
            KnifeTuning.PbrEnvRange,
            KnifeTuning.PbrEnvIntensity * lighting.Intensity,
            KnifeTuning.PbrExposure,
            KnifeTuning.PbrNormalFlipY));
        shader.Params2.SetValue(new Vector4(
            KnifeTuning.PbrRoughnessBias,
            MathUtils.DegToRad(KnifeTuning.PbrEnvYawDegrees),
            KnifeTuning.PbrEnvSaturation,
            KnifeTuning.PbrDebug));

        foreach (ModelMesh mesh in model.Meshes) {
            Matrix meshWorld = applyBoneTransform
                ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone) * world
                : world;
            shader.WorldView.SetValue(meshWorld);
            shader.WorldViewProjection.SetValue(meshWorld * projection);
            foreach (ModelMeshPart part in mesh.MeshParts) {
                Display.DrawIndexed(
                    PrimitiveType.TriangleList,
                    shader,
                    part.VertexBuffer,
                    part.IndexBuffer,
                    part.StartIndex,
                    part.IndicesCount
                );
            }
        }
        return true;
    }
}
