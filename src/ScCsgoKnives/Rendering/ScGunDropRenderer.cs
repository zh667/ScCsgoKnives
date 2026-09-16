using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Drops reuse the assembled, metre-scale held mesh. Inventory icons remain independent.</summary>
public static class ScGunDropRenderer {
    public static Matrix Frame(Pickable item, double now) {
        if (item.StuckMatrix is Matrix stuck) return stuck;
        float age = (float)(now - item.CreationTime);
        Matrix world = Matrix.CreateRotationY((float)MathUtils.Remainder(now, Math.PI * 2));
        world.Translation = item.Position + new Vector3(0, .25f * MathUtils.Saturate(3 * age) + .04f * MathF.Sin(3 * age), 0);
        return world;
    }
    sealed class Centers { public readonly Dictionary<bool, (Vector3 Center,Vector3 Span)> Values = []; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScThirdPersonWeapon, Centers> s_centers = new();
    sealed class DropState { public string Asset; public bool Legacy; public ScThirdPersonWeapon Weapon; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pickable, DropState> s_drops = new();
    static (Vector3 Center,Vector3 Span) Bounds(ScThirdPersonWeapon weapon, bool silencerOff) {
        var cache = s_centers.GetOrCreateValue(weapon).Values;
        if (cache.TryGetValue(silencerOff, out var center)) return center;
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var group in weapon.Groups) {
            if (group.Silencer && silencerOff) continue;
            foreach (int i in group.Mesh.Indices) {var v=group.Mesh.Vertices[i]; min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position); }
        }
        return cache[silencerOff] = ((min + max) * .5f,max-min);
    }
    public static Vector3 Center(ScThirdPersonWeapon weapon,bool silencerOff) => Bounds(weapon,silencerOff).Center;
    public static Vector3 Span(ScThirdPersonWeapon weapon,bool silencerOff) => Bounds(weapon,silencerOff).Span;
    public static float ScaleFor(Vector3 span) {
        float extent=Math.Max(span.X,Math.Max(span.Y,span.Z));
        return float.IsFinite(extent)&&extent>0?Math.Clamp(.44f/extent,1,3):1;
    }
    public static float OtherSize(int value,float original) {
        int c=Terrain.ExtractContents(value);
        if(c==BlocksManager.GetBlockIndex<ScKnifeBlock>(true))return Math.Max(original,.45f);
        if(c==BlocksManager.GetBlockIndex<ScGrenadeBlock>(true))return Math.Max(original,.3f/.65f);
        if(c==BlocksManager.GetBlockIndex<ScC4Block>(true))return Math.Max(original,1f);
        return original;
    }
    public static bool Draw(Pickable item, SubsystemPickables subsystem, Color color) {
        if (Terrain.ExtractContents(item.Value) != BlocksManager.GetBlockIndex<ScGunBlock>(true) || !ScGunBlock.IsKnown(item.Value)) return false;
        string asset = GunSpec.All[ScGunBlock.GetVariant(item.Value)].Name;
        bool legacy = ScGunNativeMesh.Resolve(asset, ScGunBlock.SkinOf(item.Value), out var mainTexture, out _) is not null;
        // Keep the geometry for this drop's lifetime. More than twelve visible gun models must not
        // repeatedly evict/rebuild one another in the small third-person asset cache every frame.
        var state = s_drops.GetOrCreateValue(item);
        if (state.Weapon is null || state.Asset != asset || state.Legacy != legacy) {
            state.Asset = asset; state.Legacy = legacy; state.Weapon = ScThirdPersonWeapon.ForDrop(asset, legacy);
        }
        var weapon = state.Weapon;
        if (weapon is null) return false;
        bool silencerOff = GunSpec.GetSilencerOff(Terrain.ExtractData(item.Value));
        var bounds=Bounds(weapon,silencerOff);float scale=ScaleFor(bounds.Span);
        Matrix frame=Frame(item, subsystem.m_subsystemGameInfo.TotalElapsedGameTime);
        if(item.StuckMatrix is null)frame.Translation+=Vector3.UnitY*Math.Max(0,bounds.Span.Y*scale*.5f-.20f);
        Matrix world = Matrix.CreateTranslation(-bounds.Center) * Matrix.CreateScale(scale) * frame;
        var source = item.DrawBlockEnvironmentData();
        var env = new DrawBlockEnvironmentData { DrawBlockMode = DrawBlockMode.World,
            SubsystemTerrain = source.SubsystemTerrain, Light = source.Light, Humidity = source.Humidity,
            Temperature = source.Temperature, InWorldMatrix = world };
        var renderer = item.PrimitivesRenderer();
        foreach (var group in weapon.Groups) {
            if (group.Silencer && silencerOff) continue;
            Texture2D texture = group.Texture == asset + "_hd" ? mainTexture : ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + group.Texture);
            if (legacy) ScGunNativeMesh.DrawWorld(renderer, group.Mesh, texture, color, 1, ref world, env);
            else BlocksManager.DrawMeshBlock(renderer, group.Mesh, texture, color, 1, ref world, env);
        }
        ScStatTrakRenderer.DrawThirdPerson(item.Value, asset, legacy, world, renderer,
            LightingManager.LightIntensityByLightValue[Math.Clamp(env.Light, 0, 15)] * color.R / 255f);
        return true;
    }
}
