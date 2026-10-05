using System.Text.Json;
namespace Game;

/// <summary>The world's state: the host builds the map (Editing), opens it (Lobby), starts a match (Countdown → Running)
/// and the match shows its result (Results) before the lobby returns (design §8).</summary>
public enum DmPhase { Editing, Lobby, Countdown, Running, Results }
/// <summary>One player's state. Only SpawnProtected and Alive are lives: the others carry nothing, hurt nobody and cannot
/// be hurt.</summary>
public enum DmPlayerPhase { Preparing, SpawnPending, SpawnProtected, Alive, DeathView, Spectating, Disconnected }
public enum DmLoadoutOutcome { Rejected, Saved, NextLife, Now }

public abstract record DmEvent;
public sealed record DmPhaseEvent(DmPhase Phase, string Reason) : DmEvent;
/// <summary>A player's phase, life or figures changed (the caller republishes that player).</summary>
public sealed record DmPlayerEvent(string Key) : DmEvent;
/// <summary>A point is promised to this player's next life: the client goes there and reports when it can act.</summary>
public sealed record DmPrepareEvent(string Key, int LifeId, DmSpawnPoint Spawn) : DmEvent;
/// <summary>The life begins: position, 100/100 and helmet, the whole loadout and the protection, as one step.</summary>
public sealed record DmCommitEvent(string Key, int LifeId, DmSpawnPoint Spawn, DmLoadout Loadout, int Revision, double ProtectedUntil) : DmEvent;
/// <summary>A loadout change inside the protection: the equipment is exchanged now, nothing else about the life changes.</summary>
public sealed record DmReissueEvent(string Key, DmLoadout Former, DmLoadout Loadout, int Revision) : DmEvent;
/// <summary>A life ended in a death: the one kill event of that life.</summary>
public sealed record DmDeathEvent(DmKill Kill) : DmEvent;
/// <summary>A life ended without a death (the match ended, the player left or went to watch): its equipment is taken back.</summary>
public sealed record DmStripEvent(string Key) : DmEvent;
public sealed record DmProtectionEvent(string Key, bool ByAttack) : DmEvent;
/// <summary>Text for one player (Key) or for everyone (null).</summary>
public sealed record DmNoticeEvent(string Key, string Text) : DmEvent;
public sealed record DmResultEvent(DmResult Result) : DmEvent;

/// <summary>What the authority knows about one hit it is about to settle.</summary>
public sealed class DmHit {
    /// <summary>Null: no player made it (the world).</summary>
    public string AttackerKey;
    public ScAttackKind Kind;
    public string Weapon = "";
    public int GunVariant = -1;
    /// <summary>Damage per region, after distance and (for a gun) the weapon's own head multiplier.</summary>
    public IReadOnlyList<(ScHitPart Part, float Damage)> Regions = [];
    /// <summary>The weapon's armour ratio (CS: half of it reaches health through armour).</summary>
    public float ArmourRatio = 2;
    /// <summary>Armour does not apply (fire, falling, the world).</summary>
    public bool IgnoresArmour;
    public bool NoScope, ThroughSmoke, AttackerBlind;
    public int Penetrations;
}

public sealed class DmPlayer {
    public string Key; public string Name = "";
    public DmPlayerPhase Phase = DmPlayerPhase.Preparing;
    /// <summary>When the present phase began.</summary>
    public double Since;
    public bool Connected = true;
    /// <summary>The player asked to play: lives are given while a match runs.</summary>
    public bool Entered;
    public int LifeId, Health, Armour; public bool Helmet;
    /// <summary>What the player wants next, and what the present life was given (DM-03).</summary>
    public DmLoadout Desired = DmLoadout.Empty, Active = DmLoadout.Empty;
    public int DesiredRevision, ActiveRevision;
    /// <summary>The point promised to the next life while the client gets ready.</summary>
    public DmSpawnPoint Pending; public double PreparedAt, RetryAt;
    public double ProtectedUntil;
    public int Kills, Deaths, Assists;
    /// <summary>Confirmed kills of this match by gun model: the competitive counter's number (DM-09).</summary>
    public readonly Dictionary<int, int> GunKills = [];
    /// <summary>Throwables used in this life, by kind.</summary>
    public readonly int[] GrenadesUsed = new int[DmCatalogue.GrenadeKinds];
    /// <summary>Health damage others did to this life: who, how much, when (bounded: this life only).</summary>
    public readonly List<(string Attacker, int Health, double At)> Ledger = [];
    public double? OutsideSince;
    public DmKill LastDeath;
    /// <summary>Had a life in the present match.</summary>
    public bool Participated;
    public long LastSeen;
    public bool CanFight => Phase is DmPlayerPhase.Alive or DmPlayerPhase.SpawnProtected;
    /// <summary>Throwables of a kind the present life may still throw.</summary>
    public int GrenadesLeft(int kind) => kind < 0 || kind >= GrenadesUsed.Length ? 0 : Math.Max(0, Active.Grenades.Count(g => g == kind) - GrenadesUsed[kind]);
}

/// <summary>The authority's deathmatch: who is in which state, whose life ends how, who scored (DM-03, DM-05, DM-06,
/// DM-09; design §5.2, §8, §11). No engine, no network: time is passed in, what must happen in the world comes out as
/// events, in order. One instance per loaded arena world; a client never runs one.</summary>
public sealed class DmMatch {
    public DmPhase Phase { get; private set; } = DmPhase.Editing;
    public DmRules Rules { get; private set; } = new();
    public int MatchId { get; private set; }
    public double PhaseSince { get; private set; }
    public double StartedAt { get; private set; }
    public double Deadline { get; private set; }
    public int KillSequence { get; private set; }
    public DmResult LastResult { get; private set; }
    readonly Dictionary<string, DmPlayer> m_players = new(StringComparer.Ordinal);
    readonly Queue<DmEvent> m_events = new();
    readonly Dictionary<int, double> m_spawnUsed = [];
    double m_noPointTold = double.NegativeInfinity;
    long m_seen;
    /// <summary>At most this many absent players' preferences are kept in the world.</summary>
    public const int RememberedPlayers = 64;

    public IEnumerable<DmPlayer> Players => m_players.Values;
    public DmPlayer Find(string key) => key is not null && m_players.TryGetValue(key, out var p) ? p : null;
    public IReadOnlyDictionary<int, double> SpawnUsed => m_spawnUsed;
    public IReadOnlySet<int> Reserved => m_players.Values.Where(p => p.Pending is not null).Select(p => p.Pending.Id).ToHashSet();
    public bool TryDequeue(out DmEvent e) => m_events.TryDequeue(out e);
    public double SecondsLeft(double now) => Phase == DmPhase.Running ? Math.Max(0, Deadline - now) : 0;
    /// <summary>The mode's rules apply to the players (nothing does while the host is editing the map).</summary>
    public bool Governing => Phase != DmPhase.Editing;
    public bool Running => Phase == DmPhase.Running;
    int Playing => m_players.Values.Count(p => p.Connected && p.Entered);
    void Emit(DmEvent e) => m_events.Enqueue(e);
    void Set(DmPlayer p, DmPlayerPhase phase, double now) { p.Phase = phase; p.Since = now; Emit(new DmPlayerEvent(p.Key)); }

    // ---------------------------------------------------------------- players
    /// <summary>A player is in the world (new, or back under the same stable key: the figures of the running match are
    /// that player's still; a connection slot means nothing).</summary>
    public DmPlayer Join(string key, string name, double now) {
        if (!m_players.TryGetValue(key, out var p)) m_players[key] = p = new DmPlayer { Key = key };
        p.Name = name ?? ""; p.Connected = true; p.LastSeen = ++m_seen; p.Pending = null; p.OutsideSince = null;
        Set(p, p.Entered && Running ? DmPlayerPhase.SpawnPending : DmPlayerPhase.Preparing, now);
        p.RetryAt = now;
        return p;
    }
    /// <summary>The player is gone. A life it had ends without a death or a kill; its figures stay for its return.</summary>
    public void Leave(string key, double now) {
        if (Find(key) is not { Connected: true } p) return;
        if (p.CanFight) Emit(new DmStripEvent(key));
        p.Connected = false; p.Pending = null; p.Ledger.Clear(); p.OutsideSince = null;
        Set(p, DmPlayerPhase.Disconnected, now);
        if (Running && Playing == 0) End("empty", now);
        Forget();
    }
    void Forget() {
        foreach (var gone in m_players.Values.Where(p => !p.Connected && !p.Participated).OrderByDescending(p => p.LastSeen).Skip(RememberedPlayers).ToList()) m_players.Remove(gone.Key);
    }

    // ---------------------------------------------------------------- the host
    public bool SetRules(DmRules rules) {
        if (rules is null || !rules.Valid || Phase is not (DmPhase.Editing or DmPhase.Lobby)) return false;
        Rules = rules; return true;
    }
    void To(DmPhase phase, string reason, double now) { Phase = phase; PhaseSince = now; Emit(new DmPhaseEvent(phase, reason)); }
    /// <summary>Editing → Lobby: the mode's rules begin to apply to everyone in the world.</summary>
    public bool OpenLobby(double now) {
        if (Phase != DmPhase.Editing) return false;
        foreach (var p in m_players.Values.Where(p => p.Connected)) Set(p, DmPlayerPhase.Preparing, now);
        To(DmPhase.Lobby, "", now); return true;
    }
    /// <summary>Lobby → Editing (never during a match: stop it first).</summary>
    public bool Edit(double now) {
        if (Phase != DmPhase.Lobby) return false;
        To(DmPhase.Editing, "", now); return true;
    }
    /// <summary>Lobby → Countdown when the arena has nothing that blocks a match. Returns the reason otherwise.</summary>
    public string Start(IEnumerable<DmArenaIssue> issues, double now) {
        if (Phase != DmPhase.Lobby) return Phase == DmPhase.Editing ? "先结束地图编辑并开放大厅" : "比赛已经在进行";
        if (issues.FirstOrDefault(i => i.Blocks) is { } blocking) return blocking.Message;
        To(DmPhase.Countdown, "", now); return null;
    }
    /// <summary>The host ends the countdown or the match now.</summary>
    public bool Stop(double now) {
        if (Phase == DmPhase.Countdown) { To(DmPhase.Lobby, "stopped", now); return true; }
        if (Phase == DmPhase.Running) { End("stopped", now); return true; }
        return false;
    }

    // ---------------------------------------------------------------- loadouts and entry
    /// <summary>The player's request for a whole loadout (design §5.2). Validated as one; a refusal changes nothing.
    /// Inside the protection it is exchanged now; a life that fights gets it at its next life; otherwise it is kept for
    /// the life to come.</summary>
    public (DmLoadoutOutcome Outcome, DmLoadoutError Error) RequestLoadout(string key, DmLoadout loadout, double now) {
        if (Find(key) is not { Connected: true } p || loadout is null) return (DmLoadoutOutcome.Rejected, DmLoadoutError.UnknownGun);
        var error = DmCatalogue.Validate(loadout, Rules);
        if (error != DmLoadoutError.None) return (DmLoadoutOutcome.Rejected, error);
        if (!p.Desired.SameAs(loadout)) { p.Desired = loadout; p.DesiredRevision++; }
        if (p.Phase == DmPlayerPhase.SpawnProtected && now < p.ProtectedUntil) {
            if (p.ActiveRevision != p.DesiredRevision) {
                var former = p.Active; p.Active = p.Desired; p.ActiveRevision = p.DesiredRevision;
                Emit(new DmReissueEvent(key, former, p.Active, p.ActiveRevision));
                Emit(new DmPlayerEvent(key));
            }
            return (DmLoadoutOutcome.Now, DmLoadoutError.None);
        }
        Emit(new DmPlayerEvent(key));
        return (p.Phase == DmPlayerPhase.Alive ? DmLoadoutOutcome.NextLife : DmLoadoutOutcome.Saved, DmLoadoutError.None);
    }
    /// <summary>The player asks to play. With nothing chosen it has to say so (<paramref name="confirmEmpty"/>): no kit is
    /// ever filled in for a player (DM-03). False changes nothing.</summary>
    public bool Enter(string key, bool confirmEmpty, double now) {
        if (Find(key) is not { Connected: true } p || !Governing) return false;
        if (p.Desired.IsEmpty && !confirmEmpty) { Emit(new DmNoticeEvent(key, "还没有选择任何装备：先配装，或确认空手入场")); return false; }
        if (!p.Entered && Playing >= DmFixed.MaxPlayers) { Emit(new DmNoticeEvent(key, $"本局已有 {DmFixed.MaxPlayers} 名参赛者，可以观战")); return false; }
        p.Entered = true;
        if (Running && p.Phase is DmPlayerPhase.Preparing or DmPlayerPhase.Spectating) { p.RetryAt = now; Set(p, DmPlayerPhase.SpawnPending, now); }
        else Emit(new DmPlayerEvent(key));
        return true;
    }
    /// <summary>The player stops playing and watches. A life that was fighting past its protection ends as its own death
    /// (nobody scores): leaving a fight is not a way out of dying.</summary>
    public void Spectate(string key, double now) {
        if (Find(key) is not { Connected: true } p) return;
        p.Entered = false; p.Pending = null;
        if (p.Phase == DmPlayerPhase.Alive && Running) FinishLife(p, null, DmDeathCause.Suicide, new DmHit(), false, now);
        else if (p.CanFight) Emit(new DmStripEvent(key));
        Set(p, DmPlayerPhase.Spectating, now);
    }

    // ---------------------------------------------------------------- time
    /// <summary>Advances everything that depends on time. <paramref name="choose"/> picks a point for a player's next life
    /// (null: none is acceptable now).</summary>
    public void Tick(double now, Func<DmPlayer, DmSpawnPoint> choose) {
        if (Phase == DmPhase.Countdown && now - PhaseSince >= DmFixed.CountdownSeconds) Begin(now);
        else if (Phase == DmPhase.Running && now >= Deadline) End("time", now);
        else if (Phase == DmPhase.Results && now - PhaseSince >= DmFixed.ResultsSeconds) To(DmPhase.Lobby, "", now);
        if (!Running) return;
        foreach (var p in m_players.Values.ToList()) {
            if (!p.Connected) continue;
            switch (p.Phase) {
                case DmPlayerPhase.DeathView when now - p.Since >= DmFixed.DeathViewSeconds:
                    p.RetryAt = now; Set(p, p.Entered ? DmPlayerPhase.SpawnPending : DmPlayerPhase.Preparing, now); break;
                case DmPlayerPhase.SpawnPending when p.Pending is null:
                    if (now < p.RetryAt) break;
                    if (choose(p) is { } point) { p.LifeId++; p.Pending = point; p.PreparedAt = now; Emit(new DmPrepareEvent(p.Key, p.LifeId, point)); }
                    else {
                        p.RetryAt = now + .5;
                        if (now - m_noPointTold >= 5) { m_noPointTold = now; Emit(new DmNoticeEvent(null, "暂时没有安全的复活点，等待中（房主可检查复活点设置）")); }
                    }
                    break;
                case DmPlayerPhase.SpawnPending when now - p.PreparedAt > DmFixed.ReadyTimeoutSeconds:
                    p.Pending = null; p.Entered = false;
                    Emit(new DmNoticeEvent(p.Key, "复活准备超时，已转为观战；可重新入场"));
                    Set(p, DmPlayerPhase.Spectating, now); break;
                case DmPlayerPhase.SpawnProtected when now >= p.ProtectedUntil:
                    Set(p, DmPlayerPhase.Alive, now); Emit(new DmProtectionEvent(p.Key, false)); break;
            }
            if (p.CanFight && p.OutsideSince is { } since && now - since >= DmFixed.OutOfBoundsSeconds) FinishLife(p, null, DmDeathCause.OutOfBounds, new DmHit(), false, now);
        }
    }
    void Begin(double now) {
        MatchId++; KillSequence = 0; StartedAt = now; Deadline = now + Rules.Minutes * 60.0; m_spawnUsed.Clear();
        foreach (var p in m_players.Values) {
            p.Kills = p.Deaths = p.Assists = 0; p.GunKills.Clear(); p.Participated = false; p.LastDeath = null; p.Ledger.Clear(); p.Pending = null; p.OutsideSince = null;
        }
        Forget();
        To(DmPhase.Running, "", now);
        foreach (var p in m_players.Values.Where(p => p.Connected && p.Entered)) { p.RetryAt = now; Set(p, DmPlayerPhase.SpawnPending, now); }
    }
    void End(string reason, double now) {
        var result = new DmResult { MatchId = MatchId, Reason = reason, Formal = m_players.Values.Count(p => p.Participated) >= 2, Seconds = Math.Min(now, Deadline) - StartedAt, Scores = Scoreboard(true) };
        LastResult = result;
        foreach (var p in m_players.Values) {
            if (p.CanFight) Emit(new DmStripEvent(p.Key));
            p.Pending = null; p.Ledger.Clear(); p.OutsideSince = null;
            if (p.Connected) Set(p, DmPlayerPhase.Preparing, now);
        }
        To(DmPhase.Results, reason, now);
        Emit(new DmResultEvent(result));
    }
    // ---------------------------------------------------------------- respawn
    /// <summary>The client reports it stands at the promised point and can act. The life begins if that promise is still
    /// the present one and the point is still safe; otherwise another point is chosen.</summary>
    public bool Ready(string key, int lifeId, double now, Func<DmSpawnPoint, bool> stillSafe) {
        if (!Running || Find(key) is not { Phase: DmPlayerPhase.SpawnPending, Pending: { } point } p || p.LifeId != lifeId) return false;
        if (!stillSafe(point)) { p.Pending = null; p.RetryAt = now; return false; }
        p.Pending = null; p.Health = DmFixed.Health; p.Armour = DmFixed.Armour; p.Helmet = true;
        p.Active = p.Desired; p.ActiveRevision = p.DesiredRevision; Array.Clear(p.GrenadesUsed); p.Ledger.Clear(); p.OutsideSince = null;
        p.ProtectedUntil = now + DmFixed.ProtectionSeconds; p.Participated = true; m_spawnUsed[point.Id] = now;
        Set(p, DmPlayerPhase.SpawnProtected, now);
        Emit(new DmCommitEvent(key, lifeId, point, p.Active, p.ActiveRevision, p.ProtectedUntil));
        return true;
    }

    // ---------------------------------------------------------------- combat
    /// <summary>Right before a weapon action of this player is committed (DM-05): refused unless the player has a life in
    /// a running match; a protection ends here, before the action takes effect.</summary>
    public bool AcceptAttack(string key, double now) {
        if (!Running || Find(key) is not { CanFight: true } p) return false;
        if (p.Phase == DmPlayerPhase.SpawnProtected) { Set(p, DmPlayerPhase.Alive, now); Emit(new DmProtectionEvent(key, true)); }
        return true;
    }
    /// <summary>A throwable of this kind left the player's hand. False: the present life had none left to throw.</summary>
    public bool GrenadeThrown(string key, int kind) {
        if (Find(key) is not { CanFight: true } p || p.GrenadesLeft(kind) <= 0) return false;
        p.GrenadesUsed[kind]++; return true;
    }
    /// <summary>Whether an attack of <paramref name="attackerKey"/> may land on <paramref name="victimKey"/>: only a life
    /// can be hit (a protected one takes the hit and no damage); a player without a life hits nobody, except with
    /// something it threw while it had one.</summary>
    public bool MayHurt(string attackerKey, string victimKey, bool delayed) =>
        Running && Find(victimKey) is { CanFight: true } && Find(attackerKey) is { } a && (a.CanFight || delayed && a.Participated);

    /// <summary>Settles one hit on a life: the only place health and armour change (DM-08). Null: nothing was settled
    /// (no running match, no life, a protected life, an attacker that may not hurt).</summary>
    public DmSettlement? Hurt(string victimKey, DmHit hit, double now) {
        if (!Running || now >= Deadline || Find(victimKey) is not { Phase: DmPlayerPhase.Alive } victim) return null;
        var attacker = Find(hit.AttackerKey);
        bool delayed = hit.Kind is ScAttackKind.Explosion or ScAttackKind.Fire or ScAttackKind.Bomb;
        if (hit.AttackerKey is not null && (attacker is null || !(attacker.CanFight || delayed && attacker.Participated))) return null;
        var settled = hit.IgnoresArmour
            ? DmCombat.SettleRegions(victim.Health, 0, false, hit.Regions, 1, hit.ArmourRatio, out var part) with { Armour = victim.Armour }
            : DmCombat.SettleRegions(victim.Health, victim.Armour, victim.Helmet, hit.Regions, 1, hit.ArmourRatio, out part);
        victim.Health = settled.Health; victim.Armour = settled.Armour;
        if (settled.HealthLost > 0 && attacker is not null && attacker != victim) {
            victim.Ledger.Add((attacker.Key, settled.HealthLost, now));
            if (victim.Ledger.Count > 64) victim.Ledger.RemoveAt(0);
        }
        if (settled.Lethal) FinishLife(victim, attacker, attacker is null ? DmDeathCause.Environment : attacker == victim ? DmDeathCause.Suicide : DmDeathCause.Kill, hit, part == ScHitPart.Head, now);
        else Emit(new DmPlayerEvent(victim.Key));
        return settled;
    }
    /// <summary>Damage nobody made (a fall, fire of the world, drowning): health only. Protection covers it as well.</summary>
    public DmSettlement? HurtByWorld(string victimKey, float health, double now) =>
        Hurt(victimKey, new DmHit { IgnoresArmour = true, Regions = [(ScHitPart.Body, health)] }, now);
    /// <summary>The player is inside the arena or not; outside for too long ends the life (Tick).</summary>
    public void SetInside(string key, bool inside, double now) {
        if (Find(key) is not { CanFight: true } p) return;
        if (inside) p.OutsideSince = null; else p.OutsideSince ??= now;
    }

    void FinishLife(DmPlayer victim, DmPlayer killer, DmDeathCause cause, DmHit hit, bool headshot, double now) {
        if (!victim.CanFight) return;                                   // one end per life
        bool credited = cause == DmDeathCause.Kill && killer is not null;
        victim.Deaths++; victim.Health = 0;
        var assists = new List<string>();
        foreach (var group in victim.Ledger.Where(l => now - l.At <= DmFixed.AssistSeconds && l.Attacker != victim.Key && (!credited || l.Attacker != killer.Key)).GroupBy(l => l.Attacker))
            if (group.Sum(l => l.Health) >= DmFixed.AssistDamage && Find(group.Key) is { } helper) { helper.Assists++; assists.Add(helper.Key); Emit(new DmPlayerEvent(helper.Key)); }
        if (credited) {
            killer.Kills++;
            if (hit.GunVariant >= 0) killer.GunKills[hit.GunVariant] = killer.GunKills.GetValueOrDefault(hit.GunVariant) + 1;
            Emit(new DmPlayerEvent(killer.Key));
        }
        var kill = new DmKill { MatchId = MatchId, Sequence = ++KillSequence, VictimKey = victim.Key, VictimName = victim.Name, VictimLife = victim.LifeId,
            KillerKey = credited ? killer.Key : null, KillerName = credited ? killer.Name : "", KillerLife = credited ? killer.LifeId : 0, Cause = cause,
            Weapon = hit.Weapon ?? "", GunVariant = credited ? hit.GunVariant : -1, Headshot = credited && headshot, NoScope = credited && hit.NoScope,
            ThroughSmoke = credited && hit.ThroughSmoke, Penetration = credited && hit.Penetrations > 0, AttackerBlind = credited && hit.AttackerBlind,
            Assists = assists, At = now - StartedAt };
        victim.LastDeath = kill; victim.Ledger.Clear(); victim.OutsideSince = null;
        Set(victim, DmPlayerPhase.DeathView, now);
        Emit(new DmDeathEvent(kill));
    }

    // ---------------------------------------------------------------- figures
    public List<DmScore> Scoreboard(bool participantsOnly = false) =>
        DmScores.Rank(m_players.Values.Where(p => participantsOnly ? p.Participated : p.Participated || p.Connected)
            .Select(p => new DmScore(p.Key, p.Name, p.Kills, p.Deaths, p.Assists, p.Connected, p.Entered)));

    // ---------------------------------------------------------------- the world's copy
    sealed record SavedPlayer(string Key, string Name, string Loadout);
    sealed record Saved(int Schema, bool Lobby, int MatchId, DmRules Rules, DmResult LastResult, DmResult Interrupted, List<SavedPlayer> Players);
    static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };
    /// <summary>What the world keeps: the rules, whether the lobby is open, the match counter, the last result and the
    /// players' loadout preferences. Never a running match: lives, protection and pending spawns are not persistent. A
    /// match that runs while the world is saved leaves its figures as an "interrupted" result: a world that is loaded
    /// from that save opens in the lobby with them, and nothing is replayed (design §14).</summary>
    public string Encode(double now) => JsonSerializer.Serialize(new Saved(DmIds.Schema, Phase != DmPhase.Editing, MatchId, Rules, LastResult,
        Running ? new DmResult { MatchId = MatchId, Reason = "interrupted", Formal = m_players.Values.Count(p => p.Participated) >= 2, Seconds = Math.Min(now, Deadline) - StartedAt, Scores = Scoreboard(true) } : null,
        m_players.Values.OrderByDescending(p => p.LastSeen).Take(RememberedPlayers).Select(p => new SavedPlayer(p.Key, p.Name, p.Desired.Encode())).ToList()), s_json);
    /// <summary>The match of a world that is being loaded. Null text: a new arena world. False: the text is not this
    /// version's (the caller keeps it as it is and does not run the mode).</summary>
    public static bool TryDecode(string text, out DmMatch match) {
        match = new DmMatch();
        if (string.IsNullOrWhiteSpace(text)) return true;
        try {
            var saved = JsonSerializer.Deserialize<Saved>(text, s_json);
            if (saved is null || saved.Schema != DmIds.Schema || saved.Rules is null || !saved.Rules.Valid) return false;
            match.Rules = saved.Rules; match.MatchId = Math.Max(0, saved.MatchId); match.LastResult = saved.Interrupted ?? saved.LastResult;
            match.Phase = saved.Lobby ? DmPhase.Lobby : DmPhase.Editing;
            foreach (var sp in saved.Players ?? []) {
                if (string.IsNullOrEmpty(sp.Key) || !DmLoadout.TryDecode(sp.Loadout, out var loadout)) continue;
                // a preference this build cannot issue (a gun or paint it does not have) is dropped for that player only
                bool valid = DmCatalogue.Validate(loadout, saved.Rules with { Grenades = true }) == DmLoadoutError.None;
                match.m_players[sp.Key] = new DmPlayer { Key = sp.Key, Name = sp.Name ?? "", Connected = false, Phase = DmPlayerPhase.Disconnected, Desired = valid ? loadout : DmLoadout.Empty, LastSeen = ++match.m_seen };
            }
            return true;
        }
        catch (JsonException) { match = new DmMatch(); return false; }
    }
}
