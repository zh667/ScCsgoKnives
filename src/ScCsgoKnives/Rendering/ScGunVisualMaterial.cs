using Engine.Graphics;
namespace Game;

/// <summary>All gun views select the colour texture and PBR stem together. A fallback must not mix
/// factory colour with a skin's normal/ORM maps. Native mesh selection also observes this resolved stem.</summary>
public static class ScGunVisualMaterial {
    static readonly Dictionary<(string Asset, int Skin), (Texture2D Texture, string Material)> s_cache = [];
    public static T Resolve<T>(string asset, int skin, Func<string, T> load, out string material) where T : class {
        string wanted = ScGunSkinCatalog.Material(asset, skin);
        material = wanted;
        var texture = load(wanted);
        if (texture is not null || wanted == asset + "_hd") return texture;
        material = asset + "_hd";
        return load(material);
    }
    public static Texture2D Load(string asset, int skin, out string material) {
        if (s_cache.TryGetValue((asset, skin), out var cached)) { material = cached.Material; return cached.Texture; }
        Texture2D Try(string key) { try { return ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + key); } catch { return null; } }
        var texture = Resolve(asset, skin, Try, out material);
        if (skin != 0) {
            string wanted = ScGunSkinCatalog.Material(asset, skin);
            if (texture is null || material != wanted) KnifeLog.Warning($"gun skin texture unavailable: {asset} paint {skin}, requested {wanted}, resolved {material}");
            else KnifeLog.Trace($"gun skin material loaded: {asset} paint {skin} -> {material}");
        }
        if (texture is not null) s_cache[(asset, skin)] = (texture, material);
        return texture;
    }
    public static void Clear() => s_cache.Clear(); // ContentManager owns texture lifetime
}
