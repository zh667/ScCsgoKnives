using GameEntitySystem;
namespace Game;

/// <summary>The deathmatch as the core asks about it (ScMode): one instance per arena world, registered for that Project
/// only. Every answer is "the world's own rules" (the base class) until the host opened the lobby; a survival world
/// never has an instance registered at all.</summary>
public sealed class DmMode(SubsystemScDeathmatch owner) : ScMode {
    public override string Id => DmIds.Mode;
    public override string DisplayName => DmIds.ModeName;
    public override string RulesFingerprint => DmWeapons.Fingerprint;
    public override bool Governs(ComponentPlayer player) => owner.Governs(player);
    /// <summary>CS2's numbers for every player alike: no growth, no paint bonus, no device preset (C1).</summary>
    public override bool TryGunStats(ComponentPlayer shooter, GunSpec spec, int value, bool alternate, out EffectiveGunStats stats) {
        stats = default;
        return owner.Governs(shooter) && DmWeapons.TryStats(spec, alternate, out stats);
    }
    public override bool? MayHurt(Entity attacker, Entity target, int sourcePlayer = -1) {
        if (!owner.Enabled || target?.FindComponent<ComponentPlayer>() is not { } victim || !owner.Governs(victim)) return null;
        if (!owner.Authority) return owner.View.RowOf(victim.PlayerData.PlayerIndex) is { Phase: DmPlayerPhase.Alive or DmPlayerPhase.SpawnProtected };
        var shooter = attacker?.FindComponent<ComponentPlayer>();
        string attackerKey = shooter is not null ? owner.KeyOf(shooter) : owner.KeyOfIndex(sourcePlayer);
        return owner.Match.MayHurt(attackerKey, owner.KeyOf(victim), shooter is null || sourcePlayer >= 0);
    }
    public override bool OwnsInjuries(Entity target) => target?.FindComponent<ComponentPlayer>() is { } victim && owner.Governs(victim);
    public override bool AcceptAttack(ComponentPlayer attacker, ScAttackKind kind) {
        if (!owner.Governs(attacker)) return true;
        if (!owner.Authority) return owner.View.Of(attacker).Fighting;
        if (owner.StateOf(attacker) is not { } p) return false;
        if (kind == ScAttackKind.Throw && p.GrenadesLeft(ScGrenadeBlock.Kind(attacker.ComponentMiner.ActiveBlockValue)) <= 0) return false;
        return owner.Match.AcceptAttack(p.Key, owner.Now);
    }
    public override void AttackCommitted(ComponentPlayer attacker, ScAttackKind kind, int weaponValue) {
        if (kind == ScAttackKind.Throw && owner.Authority && owner.Governs(attacker)) owner.Thrown(attacker, weaponValue);
    }
    public override bool CountsThrowables(ComponentPlayer player) => owner.Governs(player);
    /// <summary>The CS armour HUD shows the deathmatch's own protection (round 3, the user: "护甲现在就用核心CS自己的护甲
    /// HUD吧"), as CS2 does (round 4: "全部仿照CS2"): one armour value, the shield with the helmet on it while a helmet is
    /// worn, nothing without armour. Nothing while the player is not fighting. Read from the view, so a client shows what
    /// its server said.</summary>
    /// <summary>CS2's fixed spray patterns for every governed player (round 5, DmRecoil).</summary>
    public override IScRecoil Recoil(ComponentPlayer player) => owner.Governs(player) && DmWeapons.Ready ? owner.Recoil : null;
    /// <summary>CS2's hitboxes on every governed player (round 5, DmHitboxes): head, neck, chest, stomach, arms, legs.</summary>
    public override ScHitCapsule[] HitCapsules(ComponentBody body) =>
        body?.Entity?.FindComponent<ComponentPlayer>() is { } player && owner.Governs(player) ? DmHitboxes.For(body) : null;
    /// <summary>CS2's penetration for every governed shooter (round 6, DmPenetration): rounds cross blocks by material and
    /// thickness (wood easily) and go on through players.</summary>
    public override IScPenetration Penetration(ComponentPlayer shooter) => owner.Governs(shooter) && DmWeapons.Ready && DmPenetrationRules.Ready ? owner.Penetration : null;
    public override ScArmorReadout? ShownArmour(ComponentPlayer player) {
        if (!owner.Governs(player)) return null;
        var self = owner.View.Of(player);
        return self.Fighting ? ScArmorReadout.Cs2(Math.Clamp(self.Armour, 0, 100), self.Helmet) : default(ScArmorReadout);
    }
}
