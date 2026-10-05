using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>The bullet of a CS shot, for the native attack's projectile context (post-mp-bugs-20260930 item 3). CS guns
/// are hit-scan: no bullet flies, so <c>ProjectileAttackment.Projectile</c> was null, and a mod reading the shot there
/// (its owner, its round, its speed, where it struck, which world) found nothing. This is that bullet as it is at the
/// moment it hits, with the members a native bullet carries then (verified against the 1.9.3.1 Projectile) - and nothing
/// that makes it a second bullet: it is never added to SubsystemProjectiles, never updated, drawn, saved or collided, its
/// stopped action is Disappear and it cannot collide with terrain or bodies. Its speed is the vanilla musket's own
/// (SubsystemMusketBlockBehavior fires its rounds at 60, 80 and 120 m/s): the fastest for a bullet, the musket ball's for
/// the Zeus's bolt, which stays an electric attack (ScSurvivalBalance.ElectricAttack) in every other respect.</summary>
public sealed class ScBulletProjectile : Projectile {
    public const float BulletSpeed = 120f;
    public const float ElectricSpeed = 60f;
    public bool Electric { get; private init; }
    public static ScBulletProjectile For(Entity owner, int round, Vector3 hitPoint, Vector3 direction, float power, bool electric, double gameTime) {
        float length = direction.Length();
        Vector3 unit = float.IsFinite(length) && length > 1e-6f ? direction / length : -Vector3.UnitZ;
        var bullet = new ScBulletProjectile {
            Electric = electric, Value = round, Position = hitPoint, Velocity = unit * (electric ? ElectricSpeed : BulletSpeed), CreationTime = gameTime,
            OwnerEntity = owner, Project = owner?.Project, Rotation = Vector3.Zero, AngularVelocity = Vector3.Zero,
            ProjectileStoppedAction = ProjectileStoppedAction.Disappear, Damping = 0, TerrainCollidable = false, BodyCollidable = false, NoChunk = true,
        };
        bullet.m_attackPower = power;
        return bullet;
    }
    /// <summary>The round a held gun fires, as the bullet's item: the gun's own ammunition; a gun without ammunition (the
    /// Zeus) is its own item. Anything that is not a gun is passed through.</summary>
    public static int RoundOf(int weaponValue) {
        if (Terrain.ExtractContents(weaponValue) != BlocksManager.GetBlockIndex<ScGunBlock>(true)) return weaponValue;
        GunSpec spec = ScGunBlock.SpecOf(weaponValue);
        return spec is null || spec.RechargeSeconds > 0 ? weaponValue : ScAmmoBlock.Value(ScReloadTransaction.AmmoKind(spec));
    }
    /// <summary>As <see cref="RoundOf(int)"/> for a gun known only by its specification (an NPC's).</summary>
    public static int RoundOf(GunSpec spec) => spec is null ? 0 : spec.RechargeSeconds > 0
        ? Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true), 0, GunSpec.MakeData(Math.Max(0, Array.IndexOf(GunSpec.All, spec)), 0))
        : ScAmmoBlock.Value(ScReloadTransaction.AmmoKind(spec));
}
