using Engine;
namespace Game;

public static class ScShotAim {
    // Touch rays point at the finger; the weapon sight is always at camera centre.
    public static Ray3 Select(bool touch, Ray3? dig, Ray3? hit, Ray3 camera) {
        Ray3 ray = touch ? camera : dig ?? hit ?? camera;
        float length = ray.Direction.LengthSquared();
        if (!float.IsFinite(length) || length < .000001f) ray = camera;
        return new Ray3(ray.Position, Vector3.Normalize(ray.Direction));
    }
}
