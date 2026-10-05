// The match state machine (DmMatch): who is in which state, whose life ends how, who scored.
using Engine;
using Game;

static partial class Program {
    static int V(string name) => Array.FindIndex(GunSpec.All, g => g.Name == name);
    static DmLoadout Kit(string primary = "ak47", string secondary = "glock18") => new() { Primary = primary is null ? null : new(V(primary)), Secondary = secondary is null ? null : new(V(secondary)) };
    static readonly List<DmArenaIssue> NoIssues = [];

    /// <summary>A match with a clock and a list of points handed out in turn.</summary>
    sealed class Table {
        public DmMatch M = new(); public double Now = 1000; public readonly List<DmEvent> Events = [];
        public List<DmSpawnPoint> Points = [new(1, -10, 64, -10, 0), new(2, 10, 64, 10, 0), new(3, 0, 64, 10, 0), new(4, 10, 64, -10, 0)];
        int m_next;
        public bool NoPoints; public Func<DmSpawnPoint, bool> Safe = _ => true;
        public void Tick(double seconds = 0) { Now += seconds; M.Tick(Now, p => NoPoints ? null : Points.Where(s => !M.Reserved.Contains(s.Id)).Skip(m_next++ % 2).FirstOrDefault() ?? Points.FirstOrDefault(s => !M.Reserved.Contains(s.Id))); Drain(); }
        public void Drain() { while (M.TryDequeue(out var e)) Events.Add(e); }
        public List<T> Take<T>() where T : DmEvent { Drain(); var found = Events.OfType<T>().ToList(); Events.RemoveAll(e => e is T); return found; }
        public DmPlayer Join(string key, DmLoadout kit = null, bool enter = true) {
            var p = M.Join(key, "玩家" + key, Now);
            if (kit is not null) M.RequestLoadout(key, kit, Now);
            if (enter) M.Enter(key, kit is null, Now);
            return p;
        }
        public void Start() { M.OpenLobby(Now); }
        public void Run() { if (M.Phase == DmPhase.Editing) M.OpenLobby(Now); M.Start(NoIssues, Now); Tick(DmFixed.CountdownSeconds); }
        /// <summary>Brings every pending player into a life (ready at once).</summary>
        public void SpawnAll() {
            Tick();
            foreach (var prepare in Take<DmPrepareEvent>()) M.Ready(prepare.Key, prepare.LifeId, Now, Safe);
            Drain();
        }
        public void EndProtection() { Tick(DmFixed.ProtectionSeconds); }
        public DmSettlement? Shoot(string attacker, string victim, float damage, ScHitPart part = ScHitPart.Body, Action<DmHit> with = null) {
            var hit = new DmHit { AttackerKey = attacker, Kind = ScAttackKind.Shot, Weapon = "ak47", GunVariant = V("ak47"), ArmourRatio = 1.55f, Regions = [(part, damage)] };
            with?.Invoke(hit);
            if (!M.AcceptAttack(attacker, Now)) return null;
            var result = M.Hurt(victim, hit, Now); Drain(); return result;
        }
        public static Table Fight(params string[] keys) {
            var t = new Table(); t.Start();
            foreach (string key in keys) t.Join(key, Kit());
            t.Run(); t.SpawnAll(); t.EndProtection(); t.Events.Clear();
            return t;
        }
    }

    static void Match() {
        // ---- phases and the host
        var t = new Table();
        Test("M01", "a new arena world is in map editing: the mode governs nobody", t.M.Phase == DmPhase.Editing && !t.M.Governing && t.M.MatchId == 0);
        Test("M02", "a match cannot start from map editing", t.M.Start(NoIssues, t.Now) is not null && t.M.Phase == DmPhase.Editing);
        t.M.OpenLobby(t.Now);
        string blocked = t.M.Start([new DmArenaIssue(DmArenaIssueCode.TooFewSpawns, true, 0, "至少需要两个不同位置的合法复活点")], t.Now);
        Test("M03", "an arena problem that blocks is the reason the host is given; nothing starts", blocked == "至少需要两个不同位置的合法复活点" && t.M.Phase == DmPhase.Lobby);
        Test("M04", "advice alone does not block", t.M.Start([new DmArenaIssue(DmArenaIssueCode.SpawnsClustered, false, 0, "x")], t.Now) is null && t.M.Phase == DmPhase.Countdown);
        t.Tick(DmFixed.CountdownSeconds - .1); bool waiting = t.M.Phase == DmPhase.Countdown; t.Tick(.2);
        Test("M05", "the match begins when the countdown has run, with a new match number", waiting && t.M.Phase == DmPhase.Running && t.M.MatchId == 1 && Math.Abs(t.M.SecondsLeft(t.Now) - 600) < .01, $"{t.M.Phase} {t.M.SecondsLeft(t.Now)}");
        Test("M06", "rules and the map cannot be changed under a running match", !t.M.SetRules(new DmRules { Minutes = 3 }) && !t.M.Edit(t.Now) && t.M.Rules.Minutes == 10);

        // ---- entry: nothing is filled in for a player
        t = new Table(); t.Start();
        var a = t.M.Join("a", "甲", t.Now);
        bool refused = !t.M.Enter("a", false, t.Now); var notice = t.Take<DmNoticeEvent>();
        Test("M07", "entering with nothing chosen is refused with a reason, and no kit is made up", refused && !a.Entered && notice.Count == 1 && notice[0].Key == "a" && a.Desired.IsEmpty);
        Test("M08", "entering empty-handed on purpose is allowed", t.M.Enter("a", true, t.Now) && a.Entered);
        t.Run(); t.SpawnAll();
        var commit = t.Take<DmCommitEvent>();
        Test("M09", "an empty-handed life is given nothing", commit.Count == 1 && commit[0].Loadout.IsEmpty && a.Phase == DmPlayerPhase.SpawnProtected && a.Health == 100 && a.Armour == 100 && a.Helmet);

        // ---- the respawn transaction and the protection
        t = new Table(); t.Start(); a = t.Join("a", Kit()); var b = t.Join("b", Kit("awp"));
        t.Run(); t.Tick();
        var prepares = t.Take<DmPrepareEvent>();
        Test("M10", "each pending life is promised its own point; nobody is alive before it reports ready", prepares.Count == 2 && prepares[0].Spawn.Id != prepares[1].Spawn.Id && a.Phase == DmPlayerPhase.SpawnPending && !a.CanFight && t.Take<DmCommitEvent>().Count == 0);
        Test("M11", "a pending player cannot attack and cannot be hurt", !t.M.AcceptAttack("a", t.Now) && t.M.Hurt("a", new DmHit { AttackerKey = "b", Regions = [(ScHitPart.Body, 50f)] }, t.Now) is null && !t.M.MayHurt("b", "a", false));
        t.Now += 4;                                                                // the client took four seconds to load
        bool wrongLife = t.M.Ready("a", prepares.First(p => p.Key == "a").LifeId + 1, t.Now, _ => true);
        bool ready = t.M.Ready("a", prepares.First(p => p.Key == "a").LifeId, t.Now, _ => true);
        commit = t.Take<DmCommitEvent>();
        Test("M12", "a ready report for another life number is ignored", !wrongLife && ready && commit.Count == 1);
        Test("M13", "the three seconds start when the life can act, not when the point was promised", Math.Abs(a.ProtectedUntil - (t.Now + 3)) < 1e-9 && Math.Abs(commit[0].ProtectedUntil - (t.Now + 3)) < 1e-9);
        Test("M14", "the life starts with 100 health, 100 armour, a helmet and the whole chosen loadout", a.Health == 100 && a.Armour == 100 && a.Helmet && commit[0].Loadout.SameAs(Kit()) && a.Active.SameAs(Kit()) && a.ActiveRevision == a.DesiredRevision);
        Test("M15", "the same ready report again does not start the life twice", !t.M.Ready("a", commit[0].LifeId, t.Now, _ => true) && t.Take<DmCommitEvent>().Count == 0);
        t.M.Ready("b", prepares.First(p => p.Key == "b").LifeId, t.Now, _ => true); t.Events.Clear();
        var none = t.M.Hurt("a", new DmHit { AttackerKey = "b", Kind = ScAttackKind.Shot, Regions = [(ScHitPart.Body, 80f)] }, t.Now);
        Test("M16", "a protected life takes the hit and no damage", none is null && a.Health == 100 && a.Armour == 100 && t.M.MayHurt("b", "a", false));
        t.Tick(2.9); bool still = a.Phase == DmPlayerPhase.SpawnProtected; t.Tick(.2);
        var ended = t.Take<DmProtectionEvent>();
        Test("M17", "the protection ends by itself after three seconds", still && a.Phase == DmPlayerPhase.Alive && ended.Any(e => e.Key == "a" && !e.ByAttack));

        // ---- an attack ends the protection first
        t = new Table(); t.Start(); a = t.Join("a", Kit()); b = t.Join("b", Kit()); t.Run(); t.SpawnAll(); t.Events.Clear();
        bool accepted = t.M.AcceptAttack("a", t.Now); var byAttack = t.Take<DmProtectionEvent>();
        Test("M18", "a protected player's attack is accepted and the protection is gone before it takes effect", accepted && a.Phase == DmPlayerPhase.Alive && byAttack.Count == 1 && byAttack[0].ByAttack && b.Phase == DmPlayerPhase.SpawnProtected);
        var hurt = t.M.Hurt("a", new DmHit { AttackerKey = "b", Kind = ScAttackKind.Shot, ArmourRatio = 1.55f, Regions = [(ScHitPart.Body, 36f)] }, t.Now);
        Test("M19", "having attacked, that player can be hurt at once (by a still protected enemy's bullet only after that enemy attacks)", hurt is { Health: 73, Armour: 96 } && a.Health == 73);

        // ---- loadout timing (DM-03)
        t = new Table(); t.Start(); a = t.Join("a", Kit()); t.Join("b", Kit()); t.Run(); t.SpawnAll(); t.Events.Clear();
        double until = a.ProtectedUntil; t.Now += 1;
        var now = t.M.RequestLoadout("a", Kit("awp"), t.Now); var reissue = t.Take<DmReissueEvent>();
        Test("M20", "inside the protection a new loadout is exchanged at once", now.Outcome == DmLoadoutOutcome.Now && reissue.Count == 1 && reissue[0].Former.SameAs(Kit()) && reissue[0].Loadout.SameAs(Kit("awp")) && a.Active.SameAs(Kit("awp")));
        Test("M21", "exchanging does not extend the protection or touch health and armour", a.ProtectedUntil == until && a.Phase == DmPlayerPhase.SpawnProtected && a.Health == 100);
        var sameAgain = t.M.RequestLoadout("a", Kit("awp"), t.Now);
        Test("M22", "the same request again changes nothing (no second exchange)", sameAgain.Outcome == DmLoadoutOutcome.Now && t.Take<DmReissueEvent>().Count == 0);
        t.EndProtection(); t.Events.Clear(); int revision = a.ActiveRevision;
        var later = t.M.RequestLoadout("a", Kit("m4a4"), t.Now);
        Test("M23", "a life that fights gets a new choice at its next life, not now", later.Outcome == DmLoadoutOutcome.NextLife && t.Take<DmReissueEvent>().Count == 0 && a.Active.SameAs(Kit("awp")) && a.ActiveRevision == revision && a.Desired.SameAs(Kit("m4a4")));
        t.Shoot("b", "a", 400); t.Tick(DmFixed.DeathViewSeconds + .1); t.SpawnAll();
        commit = t.Take<DmCommitEvent>();
        Test("M24", "the next life is given the newer choice, whole", commit.Count == 1 && commit[0].Key == "a" && commit[0].Loadout.SameAs(Kit("m4a4")) && a.Active.SameAs(Kit("m4a4")));
        t.M.AcceptAttack("a", t.Now); t.Events.Clear();
        var afterAttack = t.M.RequestLoadout("a", Kit("awp"), t.Now);
        Test("M25", "after its own attack ended the protection a player cannot swap to a fresh gun", afterAttack.Outcome == DmLoadoutOutcome.NextLife && t.Take<DmReissueEvent>().Count == 0 && a.Active.SameAs(Kit("m4a4")));
        var wrong = t.M.RequestLoadout("a", new DmLoadout { Primary = new(V("glock18")) }, t.Now);
        Test("M26", "a loadout that is not valid as a whole is refused and the former choice stands", wrong.Outcome == DmLoadoutOutcome.Rejected && wrong.Error == DmLoadoutError.WrongClass && a.Desired.SameAs(Kit("awp")));
        Test("M27", "catalogue: a pistol is no primary, a rifle no secondary, the Zeus has its own place, an unknown number is refused",
            DmCatalogue.Validate(new DmLoadout { Secondary = new(V("ak47")) }, new()) == DmLoadoutError.WrongClass && DmCatalogue.Validate(new DmLoadout { Zeus = new(V("taser")) }, new()) == DmLoadoutError.None
            && DmCatalogue.Validate(new DmLoadout { Zeus = new(V("ak47")) }, new()) == DmLoadoutError.WrongClass && DmCatalogue.Validate(new DmLoadout { Primary = new(999) }, new()) == DmLoadoutError.UnknownGun);
        var skin = ScGunSkinCatalog.Available.FirstOrDefault(s => ScGunSkinCatalog.Fits(s, V("ak47"))); var other = ScGunSkinCatalog.Available.FirstOrDefault(s => !ScGunSkinCatalog.Fits(s, V("ak47")));
        Test("M28", "every available paint of a gun can be chosen free; another gun's paint and an unknown paint cannot", skin is not null && other is not null
            && DmCatalogue.Validate(new DmLoadout { Primary = new(V("ak47"), skin.PaintId, true) }, new()) == DmLoadoutError.None
            && DmCatalogue.Validate(new DmLoadout { Primary = new(V("ak47"), other.PaintId) }, new()) == DmLoadoutError.SkinDoesNotFit
            && DmCatalogue.Validate(new DmLoadout { Primary = new(V("ak47"), 987654) }, new()) == DmLoadoutError.UnknownSkin, $"{skin?.Name} / {other?.Name}");
        var groups = DmCatalogue.Groups.Where(g => g.Group is not (DmCatalogue.Group.Knives or DmCatalogue.Group.Gear)).ToDictionary(g => g.Name, g => DmCatalogue.GunsOf(g.Group).Count());
        Test("M29", "the wheel's gun groups hold all 34 firearms (the Zeus is equipment) and none holds more than twelve", groups.Values.Sum() == 34 && groups.Values.All(n => n is > 0 and <= 12) && DmCatalogue.GunsOf(DmCatalogue.Group.Gear).Single() == V("taser"), string.Join(" ", groups.Select(g => $"{g.Key}:{g.Value}")));
        var off = new DmRules(); var on = new DmRules { Grenades = true };
        Test("M30", "throwables: refused while the host has them off; at most three, two flashbangs, one of each other kind",
            DmCatalogue.Validate(new DmLoadout { Grenades = [0] }, off) == DmLoadoutError.GrenadesOff && DmCatalogue.Validate(new DmLoadout { Grenades = [0, 1, 1] }, on) == DmLoadoutError.None
            && DmCatalogue.Validate(new DmLoadout { Grenades = [1, 1, 1] }, on) == DmLoadoutError.TooManyOfKind && DmCatalogue.Validate(new DmLoadout { Grenades = [0, 0] }, on) == DmLoadoutError.TooManyOfKind
            && DmCatalogue.Validate(new DmLoadout { Grenades = [0, 1, 2, 3] }, on) == DmLoadoutError.TooManyGrenades && DmCatalogue.Validate(new DmLoadout { Grenades = [9] }, on) == DmLoadoutError.UnknownGrenade);

        // ---- kills, score, the one event of a life
        t = Table.Fight("a", "b", "c"); a = t.M.Find("a"); b = t.M.Find("b"); var c = t.M.Find("c");
        t.Shoot("a", "b", 36); t.Shoot("a", "b", 36); t.Shoot("a", "b", 36); t.Events.Clear();
        var last = t.Shoot("a", "b", 36); var deaths = t.Take<DmDeathEvent>();
        Test("M31", "the hit that ends a life makes exactly one kill event: +1 for the killer, a death for the victim", last is { Lethal: true } && deaths.Count == 1 && deaths[0].Kill is { KillerKey: "a", VictimKey: "b", Sequence: 1, Cause: DmDeathCause.Kill, Weapon: "ak47" } && a.Kills == 1 && b.Deaths == 1 && b.Phase == DmPlayerPhase.DeathView);
        Test("M32", "the kill counts for the killer's gun model (the competitive counter) and nowhere else", a.GunKills.GetValueOrDefault(V("ak47")) == 1 && a.GunKills.Count == 1 && b.GunKills.Count == 0);
        var after = t.Shoot("a", "b", 36); var late = t.M.Hurt("b", new DmHit { AttackerKey = "c", Kind = ScAttackKind.Shot, Regions = [(ScHitPart.Body, 99f)] }, t.Now);
        Test("M33", "further hits on the ended life settle nothing and score nothing", after is null && late is null && t.Take<DmDeathEvent>().Count == 0 && a.Kills == 1 && c.Kills == 0 && b.Deaths == 1);
        Test("M34", "a player without a life cannot attack (its late shot is refused)", !t.M.AcceptAttack("b", t.Now) && t.M.Hurt("a", new DmHit { AttackerKey = "b", Kind = ScAttackKind.Shot, Regions = [(ScHitPart.Body, 99f)] }, t.Now) is null && a.Health == 100);

        // ---- assists
        t = Table.Fight("a", "b", "c", "d"); b = t.M.Find("b"); c = t.M.Find("c"); var d = t.M.Find("d");
        t.Shoot("c", "b", 40);                                                    // 40 * .775 = 31 health
        t.Shoot("d", "b", 20);                                                    // 15 health: under the 20 needed
        t.Now += 5; t.Shoot("a", "b", 400); deaths = t.Take<DmDeathEvent>();
        Test("M35", "20 or more health within ten seconds of the end is an assist; less is not; the killer is not its own helper", deaths.Count == 1 && deaths[0].Kill.Assists.SequenceEqual(["c"]) && c.Assists == 1 && d.Assists == 0 && t.M.Find("a").Assists == 0 && c.Kills == 0);
        t = Table.Fight("a", "b", "c"); c = t.M.Find("c");
        t.Shoot("c", "b", 60); t.Now += 11; t.Shoot("a", "b", 400);
        Test("M36", "damage older than ten seconds gives no assist", t.Take<DmDeathEvent>()[0].Kill.Assists.Count == 0 && c.Assists == 0);
        t = Table.Fight("a", "b", "c"); b = t.M.Find("b"); c = t.M.Find("c");
        t.Shoot("c", "b", 60); t.Shoot("a", "b", 400); t.Tick(DmFixed.DeathViewSeconds + .1); t.SpawnAll(); t.EndProtection(); t.Events.Clear();
        t.Shoot("a", "b", 400);
        Test("M37", "damage to a former life gives no assist on the next one", t.Take<DmDeathEvent>()[0].Kill.Assists.Count == 0 && c.Assists == 1 && b.Deaths == 2);

        // ---- how: facts of the attack that ended the life
        t = Table.Fight("a", "b");
        t.Shoot("a", "b", 400, ScHitPart.Head, h => { h.NoScope = true; h.ThroughSmoke = true; h.AttackerBlind = true; h.Penetrations = 1; });
        var kill = t.Take<DmDeathEvent>()[0].Kill;
        Test("M38", "head, no-scope, through smoke, through cover and blinded are separate facts and can all hold", kill is { Headshot: true, NoScope: true, ThroughSmoke: true, Penetration: true, AttackerBlind: true });
        t = Table.Fight("a", "b"); t.Shoot("a", "b", 400, ScHitPart.Body);
        kill = t.Take<DmDeathEvent>()[0].Kill;
        Test("M39", "a body kill carries no mark it did not earn", kill is { Headshot: false, NoScope: false, ThroughSmoke: false, Penetration: false, AttackerBlind: false });
        t = Table.Fight("a", "b"); b = t.M.Find("b"); b.Armour = 0; b.Helmet = false;
        t.M.AcceptAttack("a", t.Now); t.M.Hurt("b", new DmHit { AttackerKey = "a", Kind = ScAttackKind.Shot, Weapon = "nova", GunVariant = V("nova"), ArmourRatio = 1, Regions = [(ScHitPart.Body, 90f), (ScHitPart.Head, 20f)] }, t.Now);
        Test("M40", "a shotgun kill whose head pellet did not end the life is not a head kill", t.Take<DmDeathEvent>() is [{ Kill.Headshot: false }]);

        // ---- deaths nobody scores
        t = Table.Fight("a", "b"); a = t.M.Find("a");
        t.M.Hurt("a", new DmHit { AttackerKey = "a", Kind = ScAttackKind.Explosion, Weapon = "grenade_hegrenade", ArmourRatio = 1.2f, Regions = [(ScHitPart.Body, 400f)] }, t.Now);
        kill = t.Take<DmDeathEvent>()[0].Kill;
        Test("M41", "one's own grenade: a death, nobody scores", kill is { Cause: DmDeathCause.Suicide, KillerKey: null, Scored: false } && a.Deaths == 1 && a.Kills == 0 && t.M.Find("b").Kills == 0);
        t = Table.Fight("a", "b"); a = t.M.Find("a");
        var fall = t.M.HurtByWorld("a", 30, t.Now); bool armourKept = a.Armour == 100 && a.Health == 70;
        t.M.HurtByWorld("a", 500, t.Now); kill = t.Take<DmDeathEvent>()[0].Kill;
        Test("M42", "the world's damage takes health only and its death scores for nobody", fall is { HealthLost: 30 } && armourKept && kill is { Cause: DmDeathCause.Environment, KillerKey: null } && a.Deaths == 1);
        t = Table.Fight("a", "b"); a = t.M.Find("a");
        t.M.SetInside("a", false, t.Now); t.Tick(2); t.M.SetInside("a", true, t.Now); t.Tick(2); bool alive = a.Phase == DmPlayerPhase.Alive;
        t.M.SetInside("a", false, t.Now); t.Tick(2.9); bool notYet = a.Phase == DmPlayerPhase.Alive; t.Tick(.2);
        deaths = t.Take<DmDeathEvent>();
        Test("M43", "outside the arena: back within three seconds nothing happens; three seconds outside is one death, nobody's kill", alive && notYet && deaths.Count == 1 && deaths[0].Kill is { Cause: DmDeathCause.OutOfBounds, KillerKey: null } && a.Deaths == 1);
        t = Table.Fight("a", "b"); a = t.M.Find("a"); b = t.M.Find("b");
        t.M.AcceptAttack("a", t.Now); t.Shoot("b", "a", 400); t.Events.Clear();   // a threw while alive, then died
        var boom = t.M.Hurt("b", new DmHit { AttackerKey = "a", Kind = ScAttackKind.Explosion, Weapon = "grenade_hegrenade", ArmourRatio = 1.2f, Regions = [(ScHitPart.Body, 400f)] }, t.Now);
        Test("M44", "a grenade thrown in life still counts after its thrower died", boom is { Lethal: true } && a.Kills == 1 && t.Take<DmDeathEvent>() is [{ Kill.KillerKey: "a" }]);
        t = Table.Fight("a", "b"); t.Join("watcher", Kit(), false);
        Test("M45", "something from a player who never had a life in this match hurts nobody", t.M.Hurt("b", new DmHit { AttackerKey = "watcher", Kind = ScAttackKind.Explosion, Regions = [(ScHitPart.Body, 400f)] }, t.Now) is null && t.M.Hurt("b", new DmHit { AttackerKey = "ghost", Kind = ScAttackKind.Shot, Regions = [(ScHitPart.Body, 400f)] }, t.Now) is null);

        // ---- death view, next life, old messages
        t = Table.Fight("a", "b"); b = t.M.Find("b"); int firstLife = b.LifeId;
        t.Shoot("a", "b", 400); t.Tick(DmFixed.DeathViewSeconds - .1); bool viewing = b.Phase == DmPlayerPhase.DeathView && t.Take<DmPrepareEvent>().Count == 0;
        t.Tick(.2); t.Tick(); prepares = t.Take<DmPrepareEvent>();
        Test("M46", "about two seconds of death view, then the next life is prepared with a new life number", viewing && prepares.Count == 1 && prepares[0].LifeId == firstLife + 1 && b.Phase == DmPlayerPhase.SpawnPending);
        Test("M47", "a ready report of the former life cannot start the new one", !t.M.Ready("b", firstLife, t.Now, _ => true) && b.Phase == DmPlayerPhase.SpawnPending);
        t.M.RequestLoadout("b", Kit("awp"), t.Now);
        t.M.Ready("b", prepares[0].LifeId, t.Now, _ => true);
        Test("M48", "a choice made while waiting is the one the life starts with (one version, never a mix)", t.Take<DmCommitEvent>() is [{ } c2] && c2.Loadout.SameAs(Kit("awp")) && b.Active.SameAs(Kit("awp")));

        // ---- no safe point, a point gone bad, a client that never reports
        t = new Table(); t.Start(); a = t.Join("a", Kit()); t.NoPoints = true; t.Run();
        for (int i = 0; i < 40; i++) t.Tick(.25);
        var told = t.Take<DmNoticeEvent>();
        Test("M49", "with no safe point the player waits: no life anywhere else, and the host is told (not every frame)", a.Phase == DmPlayerPhase.SpawnPending && a.Pending is null && t.Take<DmPrepareEvent>().Count == 0 && told.Count is >= 2 and <= 3 && told.All(n => n.Key is null), $"{told.Count}");
        t.NoPoints = false; t.Tick(.6);
        Test("M50", "when a point becomes safe the life is prepared", t.Take<DmPrepareEvent>().Count == 1);
        t = new Table(); t.Start(); a = t.Join("a", Kit()); t.Run(); t.Tick(); var promised = t.Take<DmPrepareEvent>()[0];
        bool refusedReady = !t.M.Ready("a", promised.LifeId, t.Now, _ => false); t.Tick(); var anew = t.Take<DmPrepareEvent>();
        Test("M51", "a promised point that turned unsafe is not used: another is promised under a new life number", refusedReady && t.Take<DmCommitEvent>().Count == 0 && anew.Count == 1 && anew[0].LifeId == promised.LifeId + 1 && !t.M.Ready("a", promised.LifeId, t.Now, _ => true));
        t = new Table(); t.Start(); a = t.Join("a", Kit()); t.Run(); t.Tick(); t.Events.Clear();
        t.Tick(DmFixed.ReadyTimeoutSeconds - .5); bool pendingStill = a.Phase == DmPlayerPhase.SpawnPending; t.Tick(1);
        Test("M52", "a client that never reports ready becomes a spectator after ten seconds (no endless untouchable player)", pendingStill && a.Phase == DmPlayerPhase.Spectating && !a.Entered && a.Pending is null && t.Take<DmNoticeEvent>().Any(n => n.Key == "a"));
        Test("M53", "a spectator re-enters by asking", t.M.Enter("a", false, t.Now) && a.Phase == DmPlayerPhase.SpawnPending);

        // ---- the end of a match
        t = Table.Fight("a", "b", "c"); a = t.M.Find("a"); b = t.M.Find("b");
        t.Shoot("a", "b", 400); t.Shoot("a", "c", 400); t.Tick(DmFixed.DeathViewSeconds + .1); t.SpawnAll(); t.EndProtection();
        t.Shoot("b", "a", 400); t.Shoot("b", "c", 400);
        t.Now = t.M.Deadline - .01; t.Events.Clear(); t.Tick(.02);
        var result = t.Take<DmResultEvent>().SingleOrDefault()?.Result;
        Test("M54", "the match ends on time: results, every life taken back without a death", t.M.Phase == DmPhase.Results && result is { Reason: "time", Formal: true, MatchId: 1 } && t.M.Players.All(p => !p.CanFight) && t.Take<DmStripEvent>().Count >= 1, $"{t.M.Phase} {result?.Reason}");
        var scores = result?.Scores ?? [];
        Test("M55", "equal kills share a place: no winner is invented", scores.Count == 3 && scores.Count(s => s.Place == 1) == 2 && scores.First(s => s.Key == "c").Place == 3 && scores.First(s => s.Key == "a").Kills == 2, string.Join(" ", scores.Select(s => $"{s.Key}:{s.Kills}k/{s.Deaths}d#{s.Place}")));
        int kills = a.Kills;
        Test("M56", "after the end nothing scores and nobody attacks", !t.M.AcceptAttack("a", t.Now) && t.M.Hurt("b", new DmHit { AttackerKey = "a", Kind = ScAttackKind.Explosion, Regions = [(ScHitPart.Body, 400f)] }, t.Now) is null && a.Kills == kills);
        t.Tick(DmFixed.ResultsSeconds + .1);
        Test("M57", "the lobby returns after the results and the result stays readable", t.M.Phase == DmPhase.Lobby && t.M.LastResult is { Reason: "time" });
        t.M.Start(NoIssues, t.Now); t.Tick(DmFixed.CountdownSeconds + .1);
        Test("M58", "the next match starts from zero with the next number; nothing of the last one carries over", t.M.MatchId == 2 && t.M.Players.All(p => p.Kills == 0 && p.Deaths == 0 && p.Assists == 0 && p.GunKills.Count == 0) && t.M.KillSequence == 0);
        t = Table.Fight("a"); t.M.Stop(t.Now); result = t.Take<DmResultEvent>().Single().Result;
        Test("M59", "one player alone is practice: its result is not a formal one; the host can stop a match", result is { Formal: false, Reason: "stopped" } && t.M.Phase == DmPhase.Results);
        t = new Table(); t.Start(); t.M.Start(NoIssues, t.Now);
        Test("M60", "the host can call off the countdown", t.M.Stop(t.Now) && t.M.Phase == DmPhase.Lobby && t.M.MatchId == 0);

        // ---- leaving, returning, identity
        t = Table.Fight("a", "b"); a = t.M.Find("a"); t.Shoot("a", "b", 400); t.Events.Clear();
        t.M.Leave("a", t.Now); var strips = t.Take<DmStripEvent>();
        Test("M61", "a player who leaves alive is taken out without a death or a kill, and keeps its figures", strips.Any(s => s.Key == "a") && a.Deaths == 0 && a.Kills == 1 && a.Phase == DmPlayerPhase.Disconnected && t.M.Phase == DmPhase.Running);
        var back = t.M.Join("a", "甲", t.Now); var stranger = t.M.Join("z", "甲", t.Now);
        Test("M62", "the same identity returns to its own figures and a new life; another identity with the same name gets nothing", ReferenceEquals(back, a) && back.Kills == 1 && back.Phase == DmPlayerPhase.SpawnPending && stranger.Kills == 0 && !stranger.Entered && stranger.Phase == DmPlayerPhase.Preparing);
        t = Table.Fight("a", "b"); t.M.Leave("a", t.Now); t.M.Leave("b", t.Now);
        Test("M63", "when every participant has left the match ends and says why", t.M.Phase == DmPhase.Results && t.M.LastResult is { Reason: "empty" });
        t = Table.Fight("a", "b"); a = t.M.Find("a"); t.M.Spectate("a", t.Now); deaths = t.Take<DmDeathEvent>();
        Test("M64", "leaving a fight to watch counts as that player's own death: nobody scores", deaths is [{ Kill: { Cause: DmDeathCause.Suicide, KillerKey: null } }] && a.Deaths == 1 && a.Phase == DmPlayerPhase.Spectating && !a.Entered && t.M.Find("b").Kills == 0);
        t = new Table(); t.Start(); a = t.Join("a", Kit()); t.Join("b", Kit()); t.Run(); t.SpawnAll(); t.Events.Clear();
        t.M.Spectate("a", t.Now);
        Test("M65", "going to watch during the protection costs nothing", t.Take<DmDeathEvent>().Count == 0 && t.Take<DmStripEvent>().Any(s => s.Key == "a") && a.Deaths == 0 && a.Phase == DmPlayerPhase.Spectating);
        t = new Table(); t.Start();
        for (int i = 0; i < DmFixed.MaxPlayers; i++) t.Join("p" + i, Kit());
        var extra = t.M.Join("p16", "多", t.Now); t.M.RequestLoadout("p16", Kit(), t.Now);
        Test("M66", "sixteen play; the seventeenth is told and may watch", !t.M.Enter("p16", false, t.Now) && !extra.Entered && t.M.Players.Count(p => p.Entered) == 16);
        t = new Table(); t.Start();
        for (int i = 0; i < 300; i++) { t.M.Join("v" + i, "访客", t.Now); t.M.Leave("v" + i, t.Now); }
        Test("M67", "visitors who never played do not accumulate without bound", t.M.Players.Count() <= DmMatch.RememberedPlayers, $"{t.M.Players.Count()}");

        // ---- throwables of a life
        t = new Table(); t.M.SetRules(new DmRules { Grenades = true }); t.Start(); a = t.Join("a", Kit() with { Grenades = [1, 1, 0] }); t.Join("b", Kit()); t.Run(); t.SpawnAll(); t.EndProtection();
        bool first = t.M.GrenadeThrown("a", 1), secondFlash = t.M.GrenadeThrown("a", 1), third = t.M.GrenadeThrown("a", 1), he = t.M.GrenadeThrown("a", 0), smoke = t.M.GrenadeThrown("a", 2);
        Test("M68", "a life throws what its loadout held and no more", first && secondFlash && !third && he && !smoke && a.GrenadesLeft(1) == 0 && a.GrenadesLeft(0) == 0);
        t.Shoot("b", "a", 400); t.Tick(DmFixed.DeathViewSeconds + .1); t.SpawnAll();
        Test("M69", "the next life has the full allowance again", a.GrenadesLeft(1) == 2 && a.GrenadesLeft(0) == 1);

        // ---- what the world keeps
        t = Table.Fight("a", "b"); t.Shoot("a", "b", 400); t.M.RequestLoadout("b", Kit("awp"), t.Now);
        string text = t.M.Encode(t.Now); bool decoded = DmMatch.TryDecode(text, out var loaded);
        Test("M70", "a world saved under a running match loads into the lobby: no lives, no countdown, the figures kept as an interrupted result", decoded && loaded.Phase == DmPhase.Lobby && loaded.MatchId == 1 && loaded.LastResult is { Reason: "interrupted", MatchId: 1 } r70 && r70.Scores.First(s => s.Key == "a").Kills == 1
            && loaded.Players.All(p => !p.Connected && !p.CanFight && p.Kills == 0), text.Length.ToString());
        Test("M71", "loadout preferences return with their owners", loaded.Find("b")?.Desired.SameAs(Kit("awp")) == true && loaded.Find("a")?.Desired.SameAs(Kit()) == true);
        var rejoined = loaded.Join("a", "甲", t.Now);
        Test("M72", "after a load a returning player prepares; its preference is there and nothing is entered for it", rejoined.Phase == DmPlayerPhase.Preparing && !rejoined.Entered && rejoined.Desired.SameAs(Kit()));
        Test("M73", "the editing state is kept as editing", DmMatch.TryDecode(new DmMatch().Encode(0), out var editing) && editing.Phase == DmPhase.Editing);
        Test("M74", "data of a later layout or broken text is not interpreted", !DmMatch.TryDecode(text.Replace("\"Schema\":1", "\"Schema\":2"), out _) && !DmMatch.TryDecode("{broken", out _) && DmMatch.TryDecode("", out var fresh) && fresh.Phase == DmPhase.Editing);
        Test("M75", "the kept text does not grow with a match's length (no lives, no ledger, no feed)", text.Length < 2000, text.Length.ToString());
    }

    static void View() {
        var view = new DmView();
        var k1 = new DmKill { MatchId = 3, Sequence = 1, VictimKey = "b", KillerKey = "a" }; var k2 = k1 with { Sequence = 2 };
        bool once = view.AddKill(k1, 10), twice = view.AddKill(k1, 11), next = view.AddKill(k2, 12), old = view.AddKill(k1, 13);
        Test("V01", "a kill event is shown once however often it arrives", once && !twice && next && !old && view.Feed.Count == 2);
        for (int i = 3; i <= 12; i++) view.AddKill(k1 with { Sequence = i }, 20 + i);
        Test("V02", "the feed keeps the newest few lines only", view.Feed.Count == DmView.FeedLines && view.Feed[^1].Kill.Sequence == 12);
        Test("V03", "a new match's first kill is not mistaken for an old number", view.AddKill(new DmKill { MatchId = 4, Sequence = 1 }, 40) && view.Feed.Count == 1);
        var ranked = DmScores.Rank([new("x", "X", 5, 2, 0, true, true), new("y", "Y", 7, 9, 1, true, true), new("z", "Z", 5, 1, 3, true, true), new("w", "W", 0, 0, 0, false, false)]);
        Test("V04", "the board: by kills; equal kills share the place and the next place skips", ranked.Select(r => r.Key).SequenceEqual(["y", "z", "x", "w"]) && ranked.Select(r => r.Place).SequenceEqual([1, 2, 2, 4]));
        var w = new ScNetWriter(); var kill = new DmKill { MatchId = 2, Sequence = 9, VictimKey = "b", VictimName = "乙", KillerKey = "a", KillerName = "甲", Weapon = "awp", GunVariant = 2, Headshot = true, NoScope = true, ThroughSmoke = true, Assists = ["c"], At = 12.5 };
        DmNet.WriteDeath(w, kill, 3, new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        var r = new ScNetReader(w.ToArray()); var read = System.Text.Json.JsonSerializer.Deserialize<DmKill>(r.String());
        Test("V05", "a kill event survives the wire with every mark", read is { MatchId: 2, Sequence: 9, VictimKey: "b", KillerKey: "a", Weapon: "awp", GunVariant: 2, Headshot: true, NoScope: true, ThroughSmoke: true, Penetration: false, AttackerBlind: false } && read.Assists.SequenceEqual(["c"]) && read.Scored && r.Int() == 3 && r.Vector3() == new Vector3(1, 2, 3) && r.Bool() && r.Vector3() == new Vector3(4, 5, 6) && r.End);
        var ops = typeof(DmNet).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(ushort)).Select(f => (ushort)f.GetRawConstantValue()).ToArray();
        Test("V06", "the package's fourteen message numbers are distinct and all in 100-113", ops.Length == 14 && ops.Distinct().Count() == 14 && ops.Min() == 100 && ops.Max() == 113);
    }
}
