using Engine;
namespace Game;

// Authored adaptation for vanilla's two rigid arms; not a CS2 character skeleton.
public static class ScThirdPersonMotion {
    public static Vector2 Right(ScWeaponAction a) {
        float w=a.Weight, wave=MathF.Sin(a.Progress*MathF.PI)*w;
        return a.Kind switch {
            ScWeaponActionKind.Draw=>new(-.85f*(1-a.Progress)*w,.08f*wave),
            ScWeaponActionKind.Reload=>new(.22f*wave,.12f*wave),
            ScWeaponActionKind.Inspect=>new(.25f*wave,-.2f*wave),
            ScWeaponActionKind.Shoot=>new(.10f*wave,0),
            ScWeaponActionKind.Slash=>new(.75f*wave,.3f*wave),
            ScWeaponActionKind.Grenade or ScWeaponActionKind.Prepare=>new(.85f*wave,0),
            _=>Vector2.Zero
        };
    }
    public static Vector2 Left(ScWeaponAction a) {
        float wave=MathF.Sin(a.Progress*MathF.PI)*a.Weight;
        return a.Kind switch {
            ScWeaponActionKind.Reload=>new(-.8f*wave,.4f*wave),
            ScWeaponActionKind.Attach or ScWeaponActionKind.Detach=>new(.22f*wave,.2f*wave),
            _=>Vector2.Zero
        };
    }
    public static Matrix WeaponRotation(ScWeaponAction a) {
        float wave=MathF.Sin(a.Progress*MathF.PI)*a.Weight;
        return a.Kind switch {
            ScWeaponActionKind.Inspect=>Matrix.CreateRotationZ(.65f*wave*MathF.Sin(a.Progress*MathF.PI*2))*Matrix.CreateRotationY(.35f*wave),
            ScWeaponActionKind.Reload=>Matrix.CreateRotationZ(-.18f*wave)*Matrix.CreateRotationX(.2f*wave),
            ScWeaponActionKind.Draw=>Matrix.CreateRotationX(-.6f*(1-a.Progress)*a.Weight),
            _=>Matrix.Identity
        };
    }
}
