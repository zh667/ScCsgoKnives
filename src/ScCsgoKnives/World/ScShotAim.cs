using Engine;
namespace Game;

public static class ScShotAim {
    public readonly record struct Shot(Ray3 Ray, float Spread, bool Alternate);
    public static Shot Capture(string gun, bool touch, bool scoped, bool silenced, bool alternateFire,
        Ray3? dig, Ray3? hit, Ray3 camera, float speed, float fallbackSpread) {
        bool alternate = scoped || silenced || alternateFire;
        float fallback = scoped ? fallbackSpread * .35f : fallbackSpread;
        // Legacy tuning has only hip-fire cones. Its fixed multiplier made the
        // SSG/auto-snipers less precise than their actual scoped weapon data.
        if (scoped && Cs2Weapons.Get(gun) is { SpreadDegreesAlternate: > 0 } data)
            fallback = Math.Min(fallback, data.SpreadDegreesAlternate);
        return new(Select(touch || scoped, dig, hit, camera),
            Cs2Weapons.SpreadDegrees(gun, alternate, speed, fallback), alternate);
    }
    // Touch rays point at the finger; the weapon sight is always at camera centre.
    public static Ray3 Select(bool touch, Ray3? dig, Ray3? hit, Ray3 camera) {
        Ray3 ray = touch ? camera : dig ?? hit ?? camera;
        float length = ray.Direction.LengthSquared();
        if (!float.IsFinite(length) || length < .000001f) ray = camera;
        return new Ray3(ray.Position, Vector3.Normalize(ray.Direction));
    }
}
