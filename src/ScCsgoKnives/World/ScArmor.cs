using System.Globalization;
using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>How an attack reaches a CS protection (current-direction-20260929 §3). Only these physical channels are
/// protected; fire, poison, drowning, hunger, falls, explosions, magic, percentage damage and scripts that change health
/// directly keep the native rules.</summary>
public enum ScArmorChannel { Bullet, Melee, Projectile }

/// <summary>What CS body protection does (first experimental numbers, not CS2's exact rule): the share of an attack's
/// power it absorbs on the covered region, by channel, and the absorption budget of a new protection (attack-power
/// units, not health). Head protection covers the head; body protection the torso and the arms; nothing covers the legs.
/// CS bullets: head 60%, body 50%. Ordinary melee: body 25% (a melee hit has no trusted region, so it never uses or
/// wears the head protection). Other projectiles: head 50% when the hit geometry says head, else body 40%.</summary>
public static class ScArmorRules {
    public const int Vest = 0, Helmet = 1;
    public static float Reduction(int kind) => ReductionFor(kind, ScArmorChannel.Bullet);
    // Distinct names, not overloads: regressions resolve Reduction/Absorb with GetMethod(name).
    public static float ReductionFor(int kind, ScArmorChannel channel) => channel switch {
        ScArmorChannel.Melee => kind == Helmet ? .35f : .25f,
        ScArmorChannel.Projectile => kind == Helmet ? .5f : .4f,
        _ => kind == Helmet ? .6f : .5f
    };
    public static int Budget(int kind) => kind == Helmet ? 100 : 150;
    public static bool Covers(int kind, ScHitPart part) => kind == Helmet ? part == ScHitPart.Head : part is ScHitPart.Body or ScHitPart.Arm or ScHitPart.Neck or ScHitPart.Stomach;
    /// <summary>The protection covering <paramref name="part"/> (-1: none, the legs).</summary>
    public static int KindFor(ScHitPart part) => part == ScHitPart.Head ? Helmet : part is ScHitPart.Body or ScHitPart.Arm or ScHitPart.Neck or ScHitPart.Stomach ? Vest : -1;
    /// <summary>Absorbed power of a CS bullet: the covered share, never more than the protection has left.</summary>
    public static float Absorb(float power, int kind, float remaining) => AbsorbFor(power, kind, ScArmorChannel.Bullet, remaining);
    public static float AbsorbFor(float power, int kind, ScArmorChannel channel, float remaining) => Math.Max(0, Math.Min(power * ReductionFor(kind, channel), remaining));
}

/// <summary>The configurations a wearer can be given: none, half (body protection only) or full (body and head). Head
/// protection alone is never made or spawned; <see cref="HelmetOnly"/> only names data that says so (kept, not "fixed").</summary>
public enum ScArmorConfig { None, Half, Full, HelmetOnly }

/// <summary>One protection: configured or not, what is left of its budget, and that budget. Wear is exact: what an
/// attack absorbs is taken in thousandths, so many small hits wear exactly as much as one large one. <see cref="Left"/>
/// is the displayed whole value (rounded up) and <see cref="Worn"/> the thousandths already taken from it.</summary>
public readonly record struct ScArmorPiece(bool Owned, int Left, int Capacity, int Worn = 0) {
    public static ScArmorPiece New(int kind) => new(true, ScArmorRules.Budget(kind), ScArmorRules.Budget(kind));
    public bool Valid => Owned ? Capacity is > 0 and <= 10000 && Left >= 0 && Left <= Capacity && Worn is >= 0 and <= 999 && (Left > 0 || Worn == 0) : Left == 0 && Capacity == 0 && Worn == 0;
    public long Milli => Owned ? Left * 1000L - Worn : 0;
    public float Remaining => Milli / 1000f;
    public long MissingMilli => Owned ? Capacity * 1000L - Milli : 0;
    public bool Protects => Owned && Milli > 0;
    /// <summary>This protection with <paramref name="milli"/> thousandths left (clamped to its budget).</summary>
    public ScArmorPiece WithMilli(long milli) {
        milli = Math.Clamp(milli, 0, Capacity * 1000L);
        int left = (int)((milli + 999) / 1000);
        return this with { Left = left, Worn = (int)(left * 1000L - milli) };
    }
    /// <summary>Thousandths taken by an absorption of <paramref name="absorbed"/> power (never more than is left).</summary>
    public long WearOf(float absorbed) => Math.Clamp((long)MathF.Round(Math.Max(0, absorbed) * 1000), 0, Milli);
}

/// <summary>A wearer's protection values. Immutable: a workbench quote freezes the state it was made for and commits only
/// while the wearer still has exactly that state. Stored as text: <c>1|owned,left,capacity[,worn]|…</c> (body, head);
/// the worn field is written only when a partial unit is used, so whole values read exactly as before.</summary>
public sealed record ScArmorState(ScArmorPiece Vest, ScArmorPiece Helmet) {
    public static readonly ScArmorState None = new(default(ScArmorPiece), default(ScArmorPiece));
    public static ScArmorState For(ScArmorConfig config) => config switch {
        ScArmorConfig.Half => new(ScArmorPiece.New(ScArmorRules.Vest), default),
        ScArmorConfig.Full => new(ScArmorPiece.New(ScArmorRules.Vest), ScArmorPiece.New(ScArmorRules.Helmet)),
        _ => None
    };
    public ScArmorConfig Config => (Vest.Owned, Helmet.Owned) switch { (false, false) => ScArmorConfig.None, (true, false) => ScArmorConfig.Half, (true, true) => ScArmorConfig.Full, _ => ScArmorConfig.HelmetOnly };
    public ScArmorPiece this[int kind] => kind == ScArmorRules.Helmet ? Helmet : Vest;
    public ScArmorState With(int kind, ScArmorPiece piece) => kind == ScArmorRules.Helmet ? this with { Helmet = piece } : this with { Vest = piece };
    public bool Damaged => Vest.MissingMilli > 0 || Helmet.MissingMilli > 0;
    public ScArmorState Repaired => new(Vest.Owned ? Vest with { Left = Vest.Capacity, Worn = 0 } : Vest, Helmet.Owned ? Helmet with { Left = Helmet.Capacity, Worn = 0 } : Helmet);
    public bool Valid => Vest.Valid && Helmet.Valid;
    public string Encode() => "1|" + Piece(Vest) + "|" + Piece(Helmet);
    static string Piece(ScArmorPiece p) => string.Create(CultureInfo.InvariantCulture, $"{(p.Owned ? 1 : 0)},{p.Left},{p.Capacity}") + (p.Worn > 0 ? "," + p.Worn.ToString(CultureInfo.InvariantCulture) : "");
    public static bool TryDecode(string text, out ScArmorState state) {
        state = None;
        var parts = text?.Split('|');
        if (parts is not { Length: 3 } || parts[0] != "1" || !TryPiece(parts[1], out var vest) || !TryPiece(parts[2], out var helmet)) return false;
        state = new(vest, helmet); return true;
    }
    static bool TryPiece(string text, out ScArmorPiece piece) {
        piece = default; var f = text.Split(',');
        if (f.Length is not (3 or 4) || f[0] is not ("0" or "1") || !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int left)
            || !int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int capacity)) return false;
        int worn = 0;
        if (f.Length == 4 && (!int.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out worn) || worn == 0)) return false;
        piece = new(f[0] == "1", left, capacity, worn); return piece.Valid;
    }
    public static string ConfigName(ScArmorConfig c) => c switch { ScArmorConfig.Half => "半甲", ScArmorConfig.Full => "全甲", ScArmorConfig.HelmetOnly => "仅头部防护", _ => "无甲" };
    /// <summary>Player-facing summary, e.g. "全甲 · 头部 80/100 · 躯干 0/150（躯干防护已耗尽）".</summary>
    public string Describe() {
        string text = ConfigName(Config);
        if (Helmet.Owned) text += $" · 头部 {Helmet.Left}/{Helmet.Capacity}";
        if (Vest.Owned) text += $" · 躯干 {Vest.Left}/{Vest.Capacity}";
        var spent = new List<string>();
        if (Vest.Owned && !Vest.Protects) spent.Add("躯干防护已耗尽");
        if (Helmet.Owned && !Helmet.Protects) spent.Add("头部防护已耗尽");
        return spent.Count == 0 ? text : text + "（" + string.Join("，", spent) + "）";
    }
}

/// <summary>A creature whose protection lives in <see cref="SubsystemScArmor"/> under this key (tactical enemies and
/// companions).</summary>
public interface IScArmorKey { string ArmorKey { get; } }

/// <summary>What one attack's protection did (current-direction-20260929 §3/§4): planned when the injury is computed,
/// committed only when the engine really applies the injury (ComponentHealth.Injured: alive, not invulnerable, amount
/// above zero). A shield, cancelled or friendly-fire-blocked attack, creative invulnerability or a mod that zeroes the
/// injury never wears anything. Committed once per attack; repeated queries of the same attack do nothing.</summary>
public sealed class ScArmorSettlement {
    public string Key; public ScArmorChannel Channel;
    public readonly List<(int Kind, float Power, float Absorbed)> Planned = [];
    public float Helmet, Vest; // what was actually absorbed at the commit
    public bool Committed;
    public bool Absorbs => Planned.Any(p => p.Absorbed > 0);
}

/// <summary>CS body protection values of every wearer in this world (current-direction-20260929): the players' (made,
/// configured and repaired at the weapon workbench, keyed by the world's player index), the companions' (keyed by the
/// companion's entity ID, which the engine saves and never reuses: NextID only grows, and a sleeping companion wakes with
/// the same ID) and the tactical enemies' (drawn once when an enemy is created, keyed by its squad and role). Numbers
/// only: no item, clothing slot or visible gear. Saved with the world; a new world or a reload never fills anything. A
/// wearer without an entry has no protection. Older supported readers do not know this subsystem; the compatibility
/// capsule keeps its saved values and returns them unchanged. An entry this build cannot read is kept verbatim and never
/// overwritten; a newer schema refuses the load before anything is written. A player's protection ends when the player
/// dies and the death drops the inventory (the world's own death rule).</summary>
public sealed class SubsystemScArmor : Subsystem, IUpdateable {
    public const int Schema = 1;
    readonly Dictionary<string, ScArmorState> m_states = new(StringComparer.Ordinal);
    readonly Dictionary<string, object> m_unreadable = new(StringComparer.Ordinal);
    public static string PlayerKey(int playerIndex) => "player-" + playerIndex.ToString(CultureInfo.InvariantCulture);
    public static string EnemyKey(string squad, int role) => "enemy-" + squad + "-" + role.ToString(CultureInfo.InvariantCulture);
    public static string CompanionKey(int entityId) => "companion-" + entityId.ToString(CultureInfo.InvariantCulture);
    public static bool ValidKey(string key) => key is { Length: > 0 and <= 128 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    public override void Load(ValuesDictionary values) {
        base.Load(values);
        if (values.GetValue("Schema", Schema) != Schema) throw new InvalidOperationException("防护装备存档版本不受支持，原数据未改动。");
        m_states.Clear(); m_unreadable.Clear();
        if (values.GetValue<ValuesDictionary>("Wearers", null) is not { } wearers) return;
        foreach (var pair in wearers) {
            if (pair.Value is string text && ValidKey(pair.Key) && ScArmorState.TryDecode(text, out var state)) { if (state != ScArmorState.None) m_states[pair.Key] = state; continue; }
            m_unreadable[pair.Key] = pair.Value;
            KnifeDiagnostics.WarnOnce("armor-unreadable-" + pair.Key, $"[CS_ARMOR] protection entry '{pair.Key}' is not readable by this build; kept unchanged");
        }
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values);
        values.SetValue("Schema", Schema);
        var wearers = new ValuesDictionary();
        foreach (var pair in m_unreadable) wearers.SetValue(pair.Key, pair.Value);
        foreach (var pair in m_states.OrderBy(p => p.Key, StringComparer.Ordinal)) wearers.SetValue(pair.Key, pair.Value.Encode());
        values.SetValue("Wearers", wearers);
    }
    public ScArmorState Get(string key) => key is not null && m_states.TryGetValue(key, out var state) ? state : ScArmorState.None;
    public IEnumerable<string> Keys => m_states.Keys;
    public bool Readable(string key) => key is not null && !m_unreadable.ContainsKey(key);
    /// <summary>Gives a new wearer its protection; refused when the key already has any entry (never re-drawn or refilled).</summary>
    public bool TryCreate(string key, ScArmorState state) {
        if (!ValidKey(key) || state is null || !state.Valid || m_states.ContainsKey(key) || m_unreadable.ContainsKey(key)) return false;
        if (state != ScArmorState.None) m_states[key] = state;
        return true;
    }
    /// <summary>Replaces the wearer's state only while it still is <paramref name="expected"/> (a frozen quote).</summary>
    public bool TryReplace(string key, ScArmorState expected, ScArmorState next) {
        if (!ValidKey(key) || next is null || !next.Valid || m_unreadable.ContainsKey(key) || Get(key) != expected) return false;
        if (next == ScArmorState.None) m_states.Remove(key); else m_states[key] = next;
        return true;
    }
    public bool Remove(string key) => key is not null && m_states.Remove(key);
    // ---- multiplayer mirror (ScNetMirror): a remote client shows the server's values and never computes its own ----
    public IEnumerable<KeyValuePair<string, string>> NetworkEntries() => m_states.Select(p => new KeyValuePair<string, string>(p.Key, p.Value.Encode()));
    /// <summary>Client: the server's entry for <paramref name="key"/> (empty: no protection).</summary>
    public void ApplyNetworkEntry(string key, string encoded) {
        if (!ValidKey(key)) return;
        if (string.IsNullOrEmpty(encoded) || !ScArmorState.TryDecode(encoded, out var state) || state == ScArmorState.None) m_states.Remove(key);
        else m_states[key] = state;
    }
    // ---- the local players' protection HUD (drawing only reads the values) ----
    readonly Dictionary<ComponentPlayer, ScArmorHud> m_huds = [];
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public void Update(float dt) {
        try { ScArmorHud.UpdateAll(this, m_huds); } catch (Exception e) { KnifeDiagnostics.WarnOnce("armor-hud", "armor HUD update failed: " + e.Message); }
        ScNetMirror.ArmorTick(this);
    }
    public override void Dispose() { foreach (var hud in m_huds.Values) hud.Dispose(); m_huds.Clear(); base.Dispose(); }

    // ---- settlement: plan when the injury is computed, commit when the engine applies it ----
    static readonly ConditionalWeakTable<Attackment, ScArmorSettlement> s_pending = new();
    static readonly ConditionalWeakTable<ComponentHealth, object> s_listening = new();
    /// <summary>A committed settlement that absorbed something, for feedback (sounds, sparks) outside the CS bullet path.</summary>
    public static event Action<Attackment, ScArmorSettlement> Settled;
    /// <summary>What reaches the wearer after its protection, region by region, without changing anything yet; the
    /// settlement is attached to <paramref name="attack"/> and committed when the injury is really applied.</summary>
    public float Plan(Attackment attack, string key, ScArmorChannel channel, IEnumerable<(ScHitPart Part, float Power)> regions) {
        float rest = 0;
        if (attack is null || key is null || !m_states.TryGetValue(key, out var state) || s_pending.TryGetValue(attack, out _)) { foreach (var r in regions) rest += r.Power; return rest; }
        var s = new ScArmorSettlement { Key = key, Channel = channel };
        var left = new[] { state.Vest.Remaining, state.Helmet.Remaining };
        foreach (var (part, power) in regions) {
            int kind = ScArmorRules.KindFor(part);
            if (kind < 0 || !(power > 0) || !state[kind].Protects) { rest += Math.Max(0, power); continue; }
            float absorbed = ScArmorRules.AbsorbFor(power, kind, channel, left[kind]);
            left[kind] -= absorbed; rest += power - absorbed; s.Planned.Add((kind, power, absorbed));
        }
        if (s.Absorbs) Attach(attack, s);
        return rest;
    }
    static void Attach(Attackment attack, ScArmorSettlement s) {
        s_pending.AddOrUpdate(attack, s);
        if (attack.Target?.FindComponent<ComponentHealth>() is not { } health || s_listening.TryGetValue(health, out _)) return;
        s_listening.Add(health, null);
        health.Injured += injury => {
            if (injury?.Attackment is { } a && s_pending.TryGetValue(a, out var pending) && !pending.Committed) Commit(injury.ComponentHealth?.Project ?? a.Target?.Project, a, pending);
        };
    }
    static bool Commit(Project project, Attackment attack, ScArmorSettlement s) {
        s.Committed = true; s_pending.Remove(attack);
        if (project?.FindSubsystem<SubsystemScArmor>(false) is not { } store || !store.m_states.TryGetValue(s.Key, out var state)) return false;
        foreach (var (kind, _, absorbed) in s.Planned) {
            var piece = state[kind]; long wear = piece.WearOf(absorbed);
            if (wear <= 0) continue;
            state = state.With(kind, piece.WithMilli(piece.Milli - wear));
            if (kind == ScArmorRules.Helmet) s.Helmet += wear / 1000f; else s.Vest += wear / 1000f;
        }
        store.m_states[s.Key] = state;
        if (attack is ScSurvivalBalance.BulletAttack bullet) bullet.Absorbed = new(s.Helmet, s.Vest);
        if (s.Helmet > 0 || s.Vest > 0) Settled?.Invoke(attack, s);
        return true;
    }
    /// <summary>The settlement planned for <paramref name="attack"/>, committed or not (null without protection).</summary>
    public static ScArmorSettlement SettlementOf(Attackment attack) => attack is not null && s_pending.TryGetValue(attack, out var s) ? s : null;

    /// <summary>The ordinary physical attacks of other creatures and mods (native MeleeAttackment and ProjectileAttackment
    /// that are not CS bullets), before the native pass: the wearer's protection takes its channel share of the power and
    /// the native clothing, resilience and attack resilience then work on the rest, once. Called from the ProcessAttackment
    /// hook, after friendly fire was ruled out; a shield or a mod that later zeroes the attack leaves nothing committed.</summary>
    public static void BeforeNative(Attackment attack) {
        if (attack is null || attack is ScSurvivalBalance.BulletAttack || !(attack.AttackPower > 0) || !attack.EnableArmorProtection) return;
        ScArmorChannel? channel = attack is MeleeAttackment ? ScArmorChannel.Melee : attack is ProjectileAttackment ? ScArmorChannel.Projectile : null;
        if (channel is null || KeyOf(attack.Target) is not { } key || attack.Target.Project?.FindSubsystem<SubsystemScArmor>(false) is not { } store) return;
        // A melee hit has no trusted region: the body. A projectile uses the target's hit geometry; only a head found there counts.
        var part = ScHitPart.Body;
        if (channel == ScArmorChannel.Projectile && attack.Target.FindComponent<ComponentBody>() is { } body) {
            var found = ScGunHitTest.PartAt(body, attack.HitPoint, attack.HitDirection);
            if (found != ScHitPart.Unknown) part = found;
        }
        attack.AttackPower = store.Plan(attack, key, channel.Value, [(part, attack.AttackPower)]);
    }

    /// <summary>The key of <paramref name="target"/>'s protection, or null: a player outside creative mode (creative players
    /// are not hurt, so nothing is worn), or a creature that names its own key.</summary>
    public static string KeyOf(Entity target) {
        if (target is null) return null;
        // A world's mode that settles this target's injuries itself keeps the survival protection out of them (deathmatch-addon).
        if (ScModes.OwnsInjuries(target)) return null;
        if (target.FindComponent<ComponentPlayer>() is { } player)
            return player.PlayerData is { } data && target.Project?.FindSubsystem<SubsystemGameInfo>(false)?.WorldSettings.GameMode != GameMode.Creative ? PlayerKey(data.PlayerIndex) : null;
        return target.FindComponent<IScArmorKey>()?.ArmorKey;
    }
    /// <summary>The death of a player whose inventory the world drops ends that player's protection (no item is dropped).
    /// A world or mod that keeps the inventory on death keeps the protection too.</summary>
    public void PlayerDied(ComponentPlayer player, bool dropsInventory) {
        if (!dropsInventory || player?.PlayerData is not { } data) return;
        string key = PlayerKey(data.PlayerIndex);
        m_states.Remove(key);
    }
}
