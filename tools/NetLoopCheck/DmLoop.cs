// deathmatch-addon (OpenSpec change deathmatch-addon): the deathmatch package on a server and two clients, end to end, on
// the REAL platform assemblies without starting a game.
//
// The server end runs the package's own subsystem (SubsystemScDeathmatch.Update: the match, the armoury, every position,
// inventory and health change); each client end runs the same subsystem as a client. What crosses between them is what
// crosses in play: the CS adapter's packet with the package's fourteen messages, the gun record rows and the
// platform's own inventory packets, serialised, decoded and handled by the platform's code. The package is initialised
// by its own mod loader (its numbers claimed through ScNet, its save group registered with the compatibility capsule,
// its counter source installed), the players' inventories are real creative inventories and the guns real records of a
// real registry.
//
// What stands in for the game: the engine's attack path. A shot is delivered as the engine delivers it to the mod hook -
// the core's own attack object with its hit regions and frozen facts, the injury passed to the package's
// CalculateCreatureInjuryAmount, and then the engine's own rule (Injury.Process, read from the 1.9.3.2 assembly:
// health -= amount when amount > 0 and the target is not invulnerable or the injury ignores it) applied by this file.
// NOT covered: the engine really calling that hook for every damage source, movement and cameras, anything seen or
// heard, timing and loss of a real connection, the Android runtime. Those are the user's in-game test.
#if DM_LOOP
using System.Xml.Linq;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using TemplatesDatabase;

static partial class StateLoop {
    static partial void RunDeathmatch(ref bool ran) { ran = true; DmLoop.Run(); }

    static class DmLoop {
        sealed class End { public World W; public SubsystemScDeathmatch Dm; public SubsystemTime Time; public ClientSession Me; }
        const float Dt = 1 / 30f;
        static double s_clock = 5000;
        static End S, A, B;
        static List<Packet> s_toClients = [];
        static DeathmatchModLoader s_loader;
        static int s_stone, s_awp, s_deagle, s_m4;
        static ClientSession s_me;

        static End Arm(World w, ValuesDictionary saved, bool server, ClientSession me) {
            var project = w.Project;
            var time = new SubsystemTime { m_gameTime = s_clock }; var info = new SubsystemGameInfo { WorldSettings = Blank<WorldSettings>() }; info.WorldSettings.GameMode = GameMode.Creative;
            var terrain = new Terrain();
            for (int cx = -1; cx <= 1; cx++) for (int cz = -1; cz <= 1; cz++) terrain.AllocateChunk(cx, cz).State = TerrainChunkState.Valid;
            for (int x = -16; x < 32; x++) for (int z = -16; z < 32; z++) terrain.SetCellValueFast(x, 63, z, s_stone);
            var st = new SubsystemTerrain { Terrain = terrain }; var bodies = new SubsystemBodies(); var compat = new SubsystemScCompatibility();
            foreach (Subsystem s in new Subsystem[] { time, info, st, bodies, compat }) { s.m_project = project; project.m_subsystems.Add(s); }
            compat.Load(new ValuesDictionary());
            foreach (int index in new[] { 0, 1, 2 }) {
                var player = w.Player[index]; var entity = player.Entity;
                var health = new ComponentHealth { Health = 1, IsInvulnerable = true }; health.m_entity = entity; player.ComponentHealth = health;   // a creative world: its players are invulnerable to the engine
                var body = new ComponentBody { Position = new Vector3(20 + index * 2, 64, 20), BoxSize = new Vector3(.65f, 1.8f, .65f) }; body.m_entity = entity; player.ComponentBody = body;
                player.ComponentLocomotion = new ComponentLocomotion();
                entity.m_components = [player, (Component)player.ComponentMiner.Inventory, player.ComponentMiner, body, health];
            }
            Enter(w, server); s_me = me;
            var dm = new SubsystemScDeathmatch(); dm.m_project = project; project.m_subsystems.Add(dm); dm.Load(saved ?? new ValuesDictionary());
            return new End { W = w, Dm = dm, Time = time, Me = me };
        }
        static void In(End e) { Enter(e.W, ReferenceEquals(e, S)); s_me = e.Me; }

        /// <summary>One frame: each client takes what the server sent last frame and runs; what it sends reaches the server on
        /// its own connection; then the server runs and ends its frame as the platform does.</summary>
        static void Frame(Action server = null) {
            s_clock += Dt;
            var batch = s_toClients; s_toClients = []; var up = new List<(List<Packet>, ClientSession)>();
            foreach (var (end, session) in new[] { (A, s_sessionA), (B, s_sessionB) }) {
                if (end is null) continue;
                In(end); Deliver(batch, end.W, session);
                In(end); end.Time.m_gameTime = s_clock; end.Dm.Update(Dt);
                var sent = Take(); if (sent.Count > 0) up.Add((sent, session));
            }
            In(S); S.Time.m_gameTime = s_clock;
            foreach (var (packets, session) in up) foreach (var p in packets) foreach (var d in PacketManager.DecodePackets(Encode(p))) { In(S); d.From = session; d.Handle(true); }
            In(S); server?.Invoke(); S.Dm.Update(Dt); EndServerFrame(S.W); In(S); s_toClients = Take();
        }
        static void Run(double seconds) { for (int i = 0, n = (int)Math.Ceiling(seconds / Dt); i < n; i++) Frame(); }
        static bool Until(Func<bool> done, double seconds) { for (int i = 0, n = (int)Math.Ceiling(seconds / Dt); i < n; i++) { if (done()) return true; Frame(); } return done(); }
        /// <summary>A client acts now; what it sent reaches the server at once, and the server's answers go out with its next frame.</summary>
        static void From(End client, Action act) {
            In(S); s_toClients.AddRange(Take());
            In(client); act(); var sent = Take(); In(S);
            foreach (var p in sent) foreach (var d in PacketManager.DecodePackets(Encode(p))) { In(S); d.From = ReferenceEquals(client, A) ? s_sessionA : s_sessionB; d.Handle(true); }
            In(S); s_toClients.AddRange(Take());
        }

        static DmPlayer P(int index) { In(S); return S.Dm.StateOf(S.W.Player[index]); }
        static DmView.Self Self(End client, int index) => client.Dm.View.Of(client.W.Player[index]);
        static ScAttackFacts s_facts;
        /// <summary>A shot of one player at another on the server, delivered as the engine delivers it to the mod hook. Returns the
        /// amount the engine was told to take (-1: the mode refused the attack, -2: the mode stops it at the target).</summary>
        static float Hit(int attackerIndex, int victimIndex, float power, ScHitPart part, string weapon = "ak47", Action<ScAttackFacts> with = null) {
            In(S);
            var attacker = S.W.Player[attackerIndex]; var victim = S.W.Player[victimIndex];
            if (!ScModes.AcceptAttack(attacker, ScAttackKind.Shot)) return -1;
            var hits = new ScShotHits(); hits.Add(part, power, victim.ComponentBody.Position, Vector3.UnitX);
            var facts = new ScAttackFacts { Kind = ScAttackKind.Shot, Weapon = weapon, AttackerPlayer = attackerIndex, AttackId = ++s_attack };
            with?.Invoke(facts); s_facts = facts;
            // (The engine's constructor words the cause of death from the attacker's creature data, which a headless player
            // does not have; the attack's own fields are set as that constructor sets them.)
            var attack = Blank<ScSurvivalBalance.GunAttack>();
            attack.Target = victim.Entity; attack.Attacker = attacker.Entity; attack.HitPoint = victim.ComponentBody.Position; attack.HitDirection = Vector3.UnitX; attack.AttackPower = power;
            typeof(ScSurvivalBalance.BulletAttack).GetProperty("Hits").SetValue(attack, hits); typeof(ScSurvivalBalance.BulletAttack).GetProperty("Facts").SetValue(attack, facts);
            if (attack.DisableFriendlyFire()) return -2;
            var injury = new Injury(attack.CalculateInjuryAmount(), null, false, "shot") { ComponentHealth = victim.ComponentHealth, Attackment = attack };
            s_loader.CalculateCreatureInjuryAmount(injury);
            var health = victim.ComponentHealth;                         // Injury.Process
            if (injury.Amount > 0 && (injury.IgnoreInvulnerability || !health.IsInvulnerable) && health.Health > 0) health.Health = MathUtils.Max(health.Health - injury.Amount, 0f);
            return injury.Amount;
        }
        static long s_attack;
        static int GunIdAt(World w, int index, int slot) { int value = w.Slot(index, slot); return IsGun(value) ? IdOf(value) : -1; }
        static string Slots(World w, int index) => string.Join(",", Enumerable.Range(0, 10).Select(s => w.Slot(index, s) == 0 ? "-" : IsGun(w.Slot(index, s)) ? "g" + IdOf(w.Slot(index, s)) : "x"));
        static ValuesDictionary Through(ValuesDictionary values) { var xml = new XElement("Values"); values.Save(xml); var back = new ValuesDictionary(); back.ApplyOverrides(XElement.Parse(xml.ToString())); return back; }

        public static void Run() {
            ScNet.Clock = () => s_clock;
            s_adapterType.GetField("Now").SetValue(null, (Func<double>)(() => s_clock));
            NetworkManager.MySessionFunc = () => s_client ? s_me : null;
            BlocksManager.Blocks[0] ??= new AirBlock();
            int free = 730; while (BlocksManager.Blocks[free] is not null && BlocksManager.Blocks[free] is not AirBlock) free++;
            var stone = new GraniteBlock { BlockIndex = free }; BlocksManager.Blocks[free] = stone; s_stone = free++;
            foreach (Type type in new[] { typeof(ScKnifeBlock), typeof(ScGrenadeBlock) }) { while (BlocksManager.Blocks[free] is not null && BlocksManager.Blocks[free] is not AirBlock) free++; var block = (Block)Activator.CreateInstance(type); block.BlockIndex = free; BlocksManager.Blocks[free] = block; BlocksManager.BlockTypeToIndex[type] = free++; }
            s_awp = Array.FindIndex(GunSpec.All, g => g.Name == "awp"); s_deagle = Array.FindIndex(GunSpec.All, g => g.Name == "deagle"); s_m4 = Array.FindIndex(GunSpec.All, g => g.Name == "m4a4");

            // ================================================================ D01 the package's own initialisation
            int handlersBefore = HandlerTable().Count;
            s_loader = new DeathmatchModLoader();
            Registering("DeathmatchModLoader.__ModInitialize", () => s_loader.__ModInitialize());
            s_loader.__ModInitialize();                                   // a second initialisation registers nothing twice
            s_modules.Add(typeof(DmNet).Assembly);
            var declared = DeclaredOps().Where(o => o.Op >= 32).ToList();
            Test("D01", "the package's loader claims its fourteen message numbers and no number has two meanings (core, agents and deathmatch together)",
                HandlerTable().Count == handlersBefore + 14 && Duplicates(declared).Length == 0 && s_overwrites.Count == 0 && Conflicts().Count == 0 && declared.Count(o => o.Module == "DmNet") == 14,
                $"{HandlerTable().Count - handlersBefore} handlers added; declared twice [{Duplicates(declared)}]; replaced [{string.Join("; ", s_overwrites)}]; refused [{string.Join("; ", Conflicts())}]");
            Test("D01", "the CS2 profile is in the package and names its build", DmWeapons.Ready && DmWeapons.Cs2Version.Length > 0 && DmWeapons.Fingerprint.Length == 16, $"{DmWeapons.LoadError} {DmWeapons.Cs2Version} {DmWeapons.Fingerprint}");

            // ================================================================ D02 an ordinary world, then the host's explicit decision
            var server = Build("server"); S = Arm(server, null, true, null);
            In(S);
            var host = server.Player[0];
            var before = new ValuesDictionary(); S.Dm.Save(before);
            Test("D02", "a world the package was merely loaded with is not an arena world: no mode, no marker, nothing saved, survival rules for everybody",
                !S.Dm.Enabled && ScModes.Of(server.Project) is null && ScWorldModes.Of(server.Project).Count == 0 && before.Count == 0 && !ScModes.FreeUse(host) && ScModes.For(host) is null && S.Dm.Match is null && ScModes.AcceptAttack(host, ScAttackKind.Shot),
                $"enabled {S.Dm.Enabled}, saved keys {before.Count}");
            Test("D02", "the arena tools do nothing before the world is made an arena world", S.Dm.SetRegion(new Point3(1, 60, 1), new Point3(14, 80, 14)) is not null && S.Dm.StartMatch() is not null);
            string enabled = S.Dm.EnableArena();
            Test("D02", "the host makes the world an arena world: marked as a dedicated world, the mode registered for this world only, in map editing (rules not in force yet)",
                enabled is null && S.Dm.Enabled && ScWorldModes.Of(server.Project) is [{ Id: DmIds.Mode, Required: true }] && ScWorldModes.Dedicated(server.Project) && ScModes.Of(server.Project) is DmMode && S.Dm.Match is { Phase: DmPhase.Editing } && !ScModes.FreeUse(host) && ScModes.For(host) is null,
                $"{enabled}; modes {ScWorldModes.Of(server.Project).Count}");
            var other = Build("another world");
            Test("D02", "another world loaded in the same process has no mode", ScModes.Of(other.Project) is null && !ScWorldModes.Dedicated(other.Project));
            In(S);
            S.Dm.SetRegion(new Point3(1, 60, 1), new Point3(14, 80, 14)); S.Dm.SetLobby(new Vector3(8.5f, 64, 8.5f), 0);
            Test("D02", "one respawn point is not enough to start", S.Dm.AddSpawn(new Vector3(3.5f, 64, 3.5f), 0) is null && S.Dm.OpenLobby() is null && S.Dm.StartMatch() is { Length: > 0 } && S.Dm.EditMap() is null);
            S.Dm.AddSpawn(new Vector3(12.5f, 64, 12.5f), 3.14f); S.Dm.AddSpawn(new Vector3(3.5f, 64, 12.5f), 1.57f); S.Dm.AddSpawn(new Vector3(12.5f, 64, 3.5f), -1.57f);
            S.Dm.AddSpawn(new Vector3(40.5f, 64, 3.5f), 0);              // outside the region: reported, never used
            var issues = S.Dm.ArenaIssues();
            Test("D02", "the arena on real terrain: four usable points, the one outside reported, nothing blocks", DmArenaRules.CanStart(issues) && issues.Any(i => i.Code == DmArenaIssueCode.SpawnOutside && i.SpawnId == 5) && DmArenaRules.Usable(S.Dm.Arena, S.Dm.Probe).Count == 4,
                string.Join("; ", issues.Select(i => i.Code + "#" + i.SpawnId)));

            // ================================================================ D03 the clients load the server's world; the handshake
            var world = new ValuesDictionary(); S.Dm.Save(world); world = Through(world);
            var meA = new ClientSession { PlayerIndex = 1, NetworkGuid = s_sessionA.NetworkGuid, NetworkState = NetworkState.Playing };
            var meB = new ClientSession { PlayerIndex = 2, NetworkGuid = s_sessionB.NetworkGuid, NetworkState = NetworkState.Playing };
            var a = Build("client A"); A = Arm(a, world, false, meA); var b = Build("client B"); B = Arm(b, world, false, meB);
            In(S); string serverMode = ScNetIdentity.Mode(server.Project); In(A); string clientMode = ScNetIdentity.Mode(a.Project);
            Test("D03", "a client that loaded the server's world runs the same mode with the same rules fingerprint; a client without the package, or with other rules, is told why it cannot join",
                serverMode.Length > 0 && clientMode == serverMode && ScNetIdentity.ModeIncompatibility(clientMode, serverMode) is null && ScNetIdentity.ModeIncompatibility("", serverMode) is { Length: > 0 }
                && ScNetIdentity.ModeIncompatibility(serverMode + "x", serverMode) is { Length: > 0 } && ScNetIdentity.ModeIncompatibility("", "") is null, serverMode);
            Test("D03", "the client holds no match of its own and decides nothing", A.Dm.Enabled && A.Dm.Match is null && !A.Dm.Authority && A.Dm.Arena.Spawns.Count == 5);
            Join(server, a, b);
            In(S);
            Test("D03", "both clients are accepted", ScNet.Peers.Count == 2, $"{ScNet.Peers.Count} peers");
            Test("D03", "with other players online a world cannot be turned into an arena world", new Func<bool>(() => { var late = Arm(Build("late"), null, true, null); In(late); bool refused = late.Dm.EnableArena() is { Length: > 0 } && !late.Dm.Enabled; In(S); return refused; })());
            Run(.3);
            In(S); string keyA = S.Dm.KeyOf(server.Player[1]), keyB = S.Dm.KeyOf(server.Player[2]);
            Test("D03", "a remote player is known by its account id, the host's own player by its seat - never by a connection slot", keyA == s_sessionA.NetworkGuid.ToString("N") && keyB == s_sessionB.NetworkGuid.ToString("N") && S.Dm.KeyOf(host) == "local:0", $"{keyA} {keyB} {S.Dm.KeyOf(host)}");
            Test("D03", "the clients see the world's state: map editing, the arena, nobody governed", A.Dm.View is { Enabled: true, Phase: DmPhase.Editing } && B.Dm.View.Phase == DmPhase.Editing && A.Dm.View.Arena.Revision == S.Dm.Arena.Revision && !ScModes.FreeUse(a.Player[1]));

            // ================================================================ D04 the lobby: the rules come into force on both ends
            In(S); server.Inventory(1).m_slots[2] = Fresh(s_ak);              // something a player carried while the map was built
            S.Dm.OpenLobby(); Run(.3);
            In(S); bool serverGoverned = ScModes.FreeUse(server.Player[1]) && ScModes.For(server.Player[1]) is DmMode && ScModes.OwnsInjuries(server.Player[1].Entity);
            In(A); bool clientGoverned = ScModes.FreeUse(a.Player[1]) && ScModes.For(a.Player[1]) is DmMode;
            Test("D04", "when the lobby opens the mode governs every player on the server and on each client", serverGoverned && clientGoverned && A.Dm.View.Phase == DmPhase.Lobby && B.Dm.View.Phase == DmPhase.Lobby);
            Test("D04", "the players are brought to the preparation place with nothing in their hands (server and client copies)", Vector3.Distance(server.Player[1].ComponentBody.Position, S.Dm.Arena.Lobby) < .1f && Vector3.Distance(a.Player[1].ComponentBody.Position, S.Dm.Arena.Lobby) < .1f
                && server.Slot(1, 2) == 0 && a.Slot(1, 2) == 0, $"server {server.Player[1].ComponentBody.Position}, client {a.Player[1].ComponentBody.Position}; slots {Slots(server, 1)} / {Slots(a, 1)}");
            In(S); bool s1 = ScModes.For(server.Player[1]).TryGunStats(server.Player[1], GunSpec.All[s_ak], Fresh(s_ak), false, out var serverStats);
            In(A); bool s2 = ScModes.For(a.Player[1]).TryGunStats(a.Player[1], GunSpec.All[s_ak], Fresh(s_ak), false, out var clientStats);
            var survival = EffectiveGunStats.Resolve(GunSpec.All[s_ak], Fresh(s_ak), false);
            Test("D04", "the AK's numbers in the arena are CS2's on both ends (36, head x4), not the survival ones", s1 && s2 && serverStats.Power == 36 && serverStats.HeadMultiplier == 4 && clientStats.Power == serverStats.Power && clientStats.CycleSeconds == serverStats.CycleSeconds && survival.Power != 36,
                $"server {serverStats.Power}/{serverStats.HeadMultiplier}, client {clientStats.Power}, survival {survival.Power}");
            In(S);
            Test("D04", "in the lobby nobody can attack or be hurt", !ScModes.AcceptAttack(server.Player[1], ScAttackKind.Shot) && Hit(1, 2, 36, ScHitPart.Body) == -1 && server.Player[2].ComponentHealth.Health == 1);

            // ================================================================ D05 loadouts and entry are wishes the server validates
            var kitA = new DmLoadout { Primary = new(s_ak, 0, true), Secondary = new(s_glock) }; var kitB = new DmLoadout { Primary = new(s_awp), Secondary = new(s_deagle) };
            From(A, () => A.Dm.RequestEnter(a.Player[1], false)); Run(.2);
            Test("D05", "entering with nothing chosen is refused: nothing is filled in for the player", !P(1).Entered && P(1).Desired.IsEmpty && Self(A, 1).Notice is { Text.Length: > 0 });
            From(A, () => A.Dm.RequestLoadout(a.Player[1], new DmLoadout { Primary = new(s_glock) })); Run(.2);
            Test("D05", "a loadout that is not valid is refused by the server and the client is told", P(1).Desired.IsEmpty && Self(A, 1).Answer is { Outcome: DmLoadoutOutcome.Rejected, Error: DmLoadoutError.WrongClass }, $"{Self(A, 1).Answer?.Outcome} {Self(A, 1).Answer?.Error}; desired empty {P(1).Desired.IsEmpty}");
            From(A, () => { A.Dm.RequestLoadout(a.Player[1], kitA); A.Dm.RequestEnter(a.Player[1], false); });
            From(B, () => { B.Dm.RequestLoadout(b.Player[2], kitB); B.Dm.RequestEnter(b.Player[2], false); });
            Run(.2);
            Test("D05", "the server keeps each client's own loadout under that client's identity", P(1).Entered && P(1).Desired.SameAs(kitA) && P(2).Entered && P(2).Desired.SameAs(kitB) && Self(A, 1).Answer is { Outcome: DmLoadoutOutcome.Saved } && Self(A, 1).Desired.SameAs(kitA) && Self(B, 2).Desired.SameAs(kitB),
                $"A entered {P(1).Entered} desired {P(1).Desired.Encode()} answer {Self(A, 1).Answer?.Outcome}/{Self(A, 1).Answer?.Error}; B entered {P(2).Entered} desired {P(2).Desired.Encode()}; view A {Self(A, 1).Desired.Encode()} B {Self(B, 2).Desired.Encode()}");
            In(S);
            Test("D05", "nothing is issued before a life begins", server.Inventory(1).m_slots.Take(10).All(v => v == 0) && S.Dm.Armory.Count == 0);

            // ================================================================ D06 the match starts; the respawn transaction
            In(S); int recordsBefore = server.Registry.Count;
            string started = S.Dm.StartMatch();
            Test("D06", "the host starts the match", started is null && S.Dm.Match.Phase == DmPhase.Countdown, started ?? "");
            Run(DmFixed.CountdownSeconds - .5);
            Test("D06", "during the countdown nobody has a life yet and the clients see the countdown", S.Dm.Match.Phase == DmPhase.Countdown && A.Dm.View.Phase == DmPhase.Countdown && !P(1).CanFight);
            bool live = Until(() => P(1).CanFight && P(2).CanFight, 3);
            Test("D06", "both clients were promised a point, reported ready and were given their lives", live && S.Dm.Match.Phase == DmPhase.Running && P(1).Phase == DmPlayerPhase.SpawnProtected, $"{P(1).Phase} {P(2).Phase}");
            Run(.2);
            In(S);
            Vector3 atA = server.Player[1].ComponentBody.Position, atB = server.Player[2].ComponentBody.Position;
            Test("D06", "each life stands on its own authored point inside the arena - server copy and the client's own body alike", S.Dm.Arena.Spawns.Take(4).Any(sp => Vector3.Distance(sp.Position, atA) < .1f) && S.Dm.Arena.Spawns.Take(4).Any(sp => Vector3.Distance(sp.Position, atB) < .1f)
                && Vector3.Distance(atA, atB) > 2 && Vector3.Distance(a.Player[1].ComponentBody.Position, atA) < .1f && Vector3.Distance(b.Player[2].ComponentBody.Position, atB) < .1f, $"A {atA} B {atB}");
            int akId = GunIdAt(server, 1, DmSlots.Primary), glockId = GunIdAt(server, 1, DmSlots.Secondary), awpId = GunIdAt(server, 2, DmSlots.Primary), deagleId = GunIdAt(server, 2, DmSlots.Secondary);
            Test("D06", "the whole loadout is in the server's inventory: each gun a record of the armoury, of the chosen model, in its fixed slot, and nothing else",
                new[] { akId, glockId, awpId, deagleId }.All(id => id > 0 && S.Dm.Armory.Owns(id)) && S.Dm.Armory.VariantOf(akId) == s_ak && S.Dm.Armory.VariantOf(awpId) == s_awp && S.Dm.Armory.Count == 4
                && Enumerable.Range(2, 8).All(s => server.Slot(1, s) == 0), $"A {Slots(server, 1)} B {Slots(server, 2)}; armoury {S.Dm.Armory.Count}");
            Test("D06", "each record is leased to its player for this match and life, and starts with a full magazine", S.Dm.Armory.LeaseOf(akId) is { } lease && lease.PlayerKey == keyA && lease.MatchId == 1 && lease.LifeId == P(1).LifeId
                && Rounds(server, akId, s_ak) == 30 && Rounds(server, awpId, s_awp) == 5 && server.Registry.Count == recordsBefore + 4);
            Test("D06", "the client's own inventory and both watchers' copies hold the same items as the server's (records before slots, one publication)", Enumerable.Range(0, 10).All(s => a.Slot(1, s) == server.Slot(1, s) && b.Slot(1, s) == server.Slot(1, s) && b.Slot(2, s) == server.Slot(2, s))
                && Rounds(a, akId, s_ak) == 30, $"server {Slots(server, 1)} | A {Slots(a, 1)} | B's copy {Slots(b, 1)}");
            Test("D06", "the client is shown its life: 100 health, 100 armour, helmet, protected for about three seconds from now", Self(A, 1) is { Phase: DmPlayerPhase.SpawnProtected, Health: 100, Armour: 100, Helmet: true } sa && sa.Active.SameAs(kitA)
                && sa.ProtectedUntil - s_clock is > 2.3 and <= 3.01, $"{Self(A, 1).Phase} {Self(A, 1).Health}/{Self(A, 1).Armour} protection left {Self(A, 1).ProtectedUntil - s_clock:0.00}");
            Test("D06", "the scoreboard rows reach both clients", A.Dm.View.Rows.Count(r => r.Playing) == 2 && B.Dm.View.Rows.Any(r => r.PlayerIndex == 1 && r.Key == keyA) && A.Dm.View.MatchId == 1);

            // ================================================================ D07 protection
            In(S);
            float onProtected = Hit(0, 2, 36, ScHitPart.Body);
            Test("D07", "a player without a life (the host, watching) cannot attack", onProtected == -1);
            In(S); P(1).Phase = DmPlayerPhase.Alive;                          // (A's protection is over for the next two checks)
            float blocked = Hit(1, 2, 36, ScHitPart.Body);
            Test("D07", "a protected life takes the hit and loses nothing", blocked == 0 && server.Player[2].ComponentHealth.Health == 1 && P(2).Health == 100 && P(2).Armour == 100 && P(2).Phase == DmPlayerPhase.SpawnProtected);
            float byProtected = Hit(2, 1, 115, ScHitPart.Leg, "awp");        // B, still protected, fires: its protection ends first
            Test("D07", "a protected player's own shot ends its protection before the shot takes effect", byProtected > 0 && P(2).Phase == DmPlayerPhase.Alive && P(1).Health == 100 - 86, $"amount {byProtected}, B {P(2).Phase}, A health {P(1).Health}");
            Run(.2);
            Test("D07", "both clients see the protection gone and the victim its new health", Self(B, 2).Phase == DmPlayerPhase.Alive && Self(A, 1).Health == 14 && A.Dm.View.RowOf(2)?.Phase == DmPlayerPhase.Alive, $"{Self(B, 2).Phase} {Self(A, 1).Health}");

            // ================================================================ D08 one settlement, the engine's health a picture of it
            float amount = Hit(1, 2, 36, ScHitPart.Body);                    // AK body on 100/100: 27 health, 4 armour (hand-computed, see DeathmatchCheck C01)
            Test("D08", "an AK body shot on 100/100 takes 27 health and 4 armour, and the engine is told to take exactly that picture (0.27) although the world is creative", MathF.Abs(amount - .27f) < .0001f && P(2).Health == 73 && P(2).Armour == 96 && MathF.Abs(server.Player[2].ComponentHealth.Health - .73f) < .0001f,
                $"amount {amount}, {P(2).Health}/{P(2).Armour}, engine health {server.Player[2].ComponentHealth.Health}");
            float head = Hit(1, 2, 36 * 4, ScHitPart.Head, "ak47", f => { f.ThroughSmoke = true; });
            Test("D08", "the shot that ends the life: the engine is told to take nothing (it never sees a death), the attack is marked lethal for the shooter's feedback", head == 0 && s_facts.Lethal && server.Player[2].ComponentHealth.Health > 0 && P(2).Phase == DmPlayerPhase.DeathView && P(2).Deaths == 1 && P(1).Kills == 1,
                $"amount {head}, lethal {s_facts.Lethal}, engine health {server.Player[2].ComponentHealth.Health}, {P(2).Phase}");
            float after = Hit(1, 2, 36, ScHitPart.Body);
            Test("D08", "the dead player's body cannot be hit again and the kill is not counted twice", after == -2 && P(1).Kills == 1 && P(2).Deaths == 1);
            Frame();
            In(S);
            Test("D08", "the dead player's equipment is taken back at once: its inventory is empty and its records are parked, never deleted", Enumerable.Range(0, 10).All(s => server.Slot(2, s) == 0) && S.Dm.Armory.LeaseOf(awpId) is null && S.Dm.Armory.Owns(awpId) && S.Dm.Armory.Count == 4
                && server.Registry.Count == recordsBefore + 4 && Vector3.Distance(server.Player[2].ComponentBody.Position, S.Dm.Arena.Lobby) < .1f && server.Player[2].ComponentHealth.Health == 1, Slots(server, 2));
            Run(.2);
            var feedA = A.Dm.View.Feed.LastOrDefault().Kill; var feedB = B.Dm.View.Feed.LastOrDefault().Kill; var feedS = S.Dm.View.Feed.LastOrDefault().Kill;
            Test("D08", "one kill event reaches everybody, with the marks the server established (head, through smoke) and no others", new[] { feedA, feedB, feedS }.All(k => k is { Sequence: 1, MatchId: 1, Headshot: true, ThroughSmoke: true, NoScope: false, Penetration: false, AttackerBlind: false, Weapon: "ak47" } && k.KillerKey == keyA && k.VictimKey == keyB)
                && A.Dm.View.Feed.Count == 1, $"{feedA?.Sequence} {feedA?.Headshot} {feedA?.ThroughSmoke}");
            Test("D08", "the victim's client knows it died and by whom; its inventory copy is empty; the board shows 1 kill and 1 death", Self(B, 2).OwnDeath is { KillerKey: var killer } && killer == keyA && Self(B, 2).Phase == DmPlayerPhase.DeathView && Enumerable.Range(0, 10).All(s => b.Slot(2, s) == 0)
                && A.Dm.View.RowOf(1) is { Kills: 1 } && A.Dm.View.RowOf(2) is { Deaths: 1 }, $"{Self(B, 2).Phase}; B slots {Slots(b, 2)}");
            In(S); var counterServer = S.Dm.Counter(server.Slot(1, DmSlots.Primary)); In(A); var counterClient = A.Dm.Counter(a.Slot(1, DmSlots.Primary)); In(B); var counterOther = B.Dm.Counter(Fresh(s_ak));
            Test("D08", "the competitive counter of the killer's AK reads 1 on the server and on its owner's client, from the match's figures: the gun's record carries no kill, counter or growth", counterServer is (true, 1) && counterClient is (true, 1) && counterOther is null
                && server.Registry.TryGetSnapshot(akId, out var akRecord) && akRecord is { KillCount: 0, CounterInstalled: false, Level: 0 }, $"server {counterServer}, client {counterClient}");

            // ================================================================ D09 the next life reuses the same records
            bool again = Until(() => P(2).CanFight, DmFixed.DeathViewSeconds + 3);
            Run(.2); In(S);
            Test("D09", "about two seconds later the next life begins: the same two records are lent again, full, and the pool has not grown", again && GunIdAt(server, 2, DmSlots.Primary) == awpId && GunIdAt(server, 2, DmSlots.Secondary) == deagleId && Rounds(server, awpId, s_awp) == 5
                && S.Dm.Armory.Count == 4 && server.Registry.Count == recordsBefore + 4 && P(2).Health == 100 && P(2).Armour == 100 && P(2).LifeId == 2 && b.Slot(2, 0) == server.Slot(2, 0), $"{Slots(server, 2)}; armoury {S.Dm.Armory.Count}; life {P(2).LifeId}");
            // inside the protection B changes its primary: exchanged now, no new protection
            double until = P(2).ProtectedUntil; var kitB2 = kitB with { Primary = new(s_m4) };
            From(B, () => B.Dm.RequestLoadout(b.Player[2], kitB2)); Run(.2); In(S);
            int m4Id = GunIdAt(server, 2, DmSlots.Primary);
            Test("D09", "a loadout changed inside the protection is exchanged at once: the new gun is issued, the old one parked, the kept pistol untouched, the protection not extended", S.Dm.Armory.VariantOf(m4Id) == s_m4 && S.Dm.Armory.LeaseOf(awpId) is null && GunIdAt(server, 2, DmSlots.Secondary) == deagleId
                && P(2).ProtectedUntil == until && Self(B, 2).Answer is { Outcome: DmLoadoutOutcome.Now } && b.Slot(2, 0) == server.Slot(2, 0) && S.Dm.Armory.Count == 5, $"{Slots(server, 2)}; answer {Self(B, 2).Answer?.Outcome}");
            Run(3.1);
            From(B, () => B.Dm.RequestLoadout(b.Player[2], kitB)); Run(.2); In(S);
            Test("D09", "the same request from a life that fights changes nothing now: it is for the next life", GunIdAt(server, 2, DmSlots.Primary) == m4Id && Self(B, 2).Answer is { Outcome: DmLoadoutOutcome.NextLife } && P(2).Desired.SameAs(kitB) && P(2).Active.SameAs(kitB2));

            // ================================================================ D10 the authority's inventory
            In(S);
            server.Inventory(2).m_slots[6] = Fresh(s_ak); server.Inventory(2).m_slots[0] = 0; server.Inventory(2).m_slots[1] = Instance(s_glock, glockId);
            Frame(); Run(.1); In(S);
            Test("D10", "slots a client (or anything else) rewrote are put back by the server: the issued items in their places, nothing extra", GunIdAt(server, 2, 0) == m4Id && GunIdAt(server, 2, 1) == deagleId && server.Slot(2, 6) == 0 && b.Slot(2, 0) == server.Slot(2, 0) && b.Slot(2, 6) == 0, Slots(server, 2));

            // ================================================================ D11 many lives: the pool follows simultaneous use, not the number of lives
            int kills = P(1).Kills; int[] primaries = [s_awp, s_m4, s_ak, Array.FindIndex(GunSpec.All, g => g.Name == "famas"), Array.FindIndex(GunSpec.All, g => g.Name == "nova")];
            for (int life = 0; life < 30; life++) {
                In(S); P(1).Health = 100; P(1).Armour = 100;
                From(B, () => B.Dm.RequestLoadout(b.Player[2], kitB with { Primary = new(primaries[life % primaries.Length]) }));
                if (P(1).Phase != DmPlayerPhase.Alive) Until(() => P(1).Phase == DmPlayerPhase.Alive, 4);
                Until(() => P(2).Phase == DmPlayerPhase.Alive, DmFixed.DeathViewSeconds + DmFixed.ProtectionSeconds + 3);
                Hit(1, 2, 500, ScHitPart.Body);
                Frame();
            }
            Run(.3); In(S);
            Test("D11", "thirty more lives with five different primaries: thirty more kills, and the pool holds one record per model in use - it did not grow with the lives", P(1).Kills == kills + 30 && P(2).Deaths == 31 && S.Dm.Armory.Count <= 2 + 1 + primaries.Length && server.Registry.Count == recordsBefore + S.Dm.Armory.Count
                && S.Dm.Armory.Ids.All(id => S.Dm.Armory.VariantOf(id) == (server.Registry.TryGetSnapshot(id, out var snap) ? snap.Variant : -1)), $"kills {P(1).Kills - kills}, armoury {S.Dm.Armory.Count}, registry +{server.Registry.Count - recordsBefore}");
            Test("D11", "every client applied each kill once: the feed's last number is the server's, the board agrees", A.Dm.View.LastSequence == S.Dm.Match.KillSequence && B.Dm.View.LastSequence == S.Dm.Match.KillSequence && A.Dm.View.RowOf(1)?.Kills == P(1).Kills && B.Dm.View.RowOf(2)?.Deaths == P(2).Deaths,
                $"server {S.Dm.Match.KillSequence}, A {A.Dm.View.LastSequence}, B {B.Dm.View.LastSequence}");
            In(A); var counterMany = A.Dm.Counter(a.Slot(1, DmSlots.Primary));
            Test("D11", "the AK's counter on its owner's client reads the match's 31 kills with it", counterMany is (true, 31), $"{counterMany}");

            // ================================================================ D12 the arena's terrain; leaving the arena
            In(S); bool protectedServer = S.Dm.ProtectsCell(5, 64, 5) && !S.Dm.ProtectsCell(60, 64, 60); In(A); bool protectedClient = A.Dm.ProtectsCell(5, 64, 5) && !A.Dm.ProtectsCell(60, 64, 60);
            s_loader.TerrainChangeCell(a.Project.FindSubsystem<SubsystemTerrain>(true), 5, 64, 5, s_stone, out bool skipClient); In(S); s_loader.TerrainChangeCell(server.Project.FindSubsystem<SubsystemTerrain>(true), 5, 64, 5, s_stone, out bool skipServer);
            s_loader.TerrainChangeCell(server.Project.FindSubsystem<SubsystemTerrain>(true), 60, 64, 60, s_stone, out bool skipFar); s_loader.TerrainChangeCell(other.Project.FindSubsystem<SubsystemTerrain>(false) ?? server.Project.FindSubsystem<SubsystemTerrain>(true), 60, 64, 60, s_stone, out bool _);
            Test("D12", "during a match the arena's cells refuse every change, with the same answer on the server and on a client; cells elsewhere are free", protectedServer && protectedClient && skipServer && skipClient && !skipFar);
            Until(() => P(1).Phase == DmPlayerPhase.Alive, 6);
            In(S); int deathsA = P(1).Deaths; server.Player[1].ComponentBody.Position = new Vector3(40.5f, 64, 8.5f);
            for (int i = 0; i < (int)(2.5 / Dt); i++) Frame(() => server.Player[1].ComponentBody.Position = new Vector3(40.5f, 64, 8.5f));
            bool stillAlive = P(1).Phase == DmPlayerPhase.Alive && Self(A, 1).Notice is { } warned && warned.Text.Contains("竞技区域");
            for (int i = 0; i < (int)(1 / Dt); i++) Frame(() => { if (P(1).Phase == DmPlayerPhase.Alive) server.Player[1].ComponentBody.Position = new Vector3(40.5f, 64, 8.5f); });
            Run(.2); In(S);
            Test("D12", "outside the arena: a warning, and after three seconds one death that is nobody's kill", stillAlive && P(1).Deaths == deathsA + 1 && P(2).Kills == 0 && A.Dm.View.Feed.Last().Kill is { Cause: DmDeathCause.OutOfBounds, KillerKey: null }, $"alive before {stillAlive}; deaths {P(1).Deaths - deathsA}");

            // ================================================================ D13 a save under a running match, through the engine's own serialisation
            In(S); var running = new ValuesDictionary(); S.Dm.Save(running);
            // the group exactly as a save writes it, for the load check that takes it through the game's hooks without the package
            if (Environment.GetEnvironmentVariable("DM_GROUP_OUT") is { Length: > 0 } groupOut) { var group = new XElement("Values", new XAttribute("Name", DmIds.Subsystem)); running.Save(group); group.Save(groupOut); }
            running = Through(running);
            var reloadedWorld = Build("server reloaded"); reloadedWorld.Registry = server.Registry;
            var R = Arm(reloadedWorld, running, true, null); In(R);
            Test("D13", "the world saved under a running match loads into the lobby: the arena and the pool as they were, every record parked, the figures kept as an interrupted result, nothing replayed", R.Dm.Enabled && R.Dm.Frozen is null && R.Dm.Match is { Phase: DmPhase.Lobby, MatchId: 1 }
                && R.Dm.Match.LastResult is { Reason: "interrupted" } interrupted && interrupted.Scores.First(sc => sc.Key == keyA).Kills == P(1).Kills && R.Dm.Arena.Encode() == S.Dm.Arena.Encode()
                && R.Dm.Armory.Count == S.Dm.Armory.Count && R.Dm.Armory.Parked.Count() == R.Dm.Armory.Count && R.Dm.Match.Find(keyB)?.Desired.SameAs(P(2).Desired) == true, $"{R.Dm.Frozen} {R.Dm.Match?.Phase}");
            var newer = Through(running); newer.SetValue("Schema", 99); newer.SetValue("Later", "kept");
            var frozenWorld = Build("newer data"); var F = Arm(frozenWorld, newer, true, null); In(F); var written = new ValuesDictionary(); F.Dm.Save(written);
            Test("D13", "data of a later layout is not interpreted: the mode does not run, the player is told, and a save writes every value back exactly as it was loaded", F.Dm.Frozen is { Length: > 0 } && !F.Dm.Enabled && ScModes.Of(frozenWorld.Project) is null && F.Dm.EnableArena() is { Length: > 0 }
                && written.GetValue<int>("Schema") == 99 && written.GetValue<string>("Later") == "kept" && written.GetValue<string>("Armory") == running.GetValue<string>("Armory") && written.GetValue<string>("Match") == running.GetValue<string>("Match") && written.Count == newer.Count, F.Dm.Frozen ?? "");
            var broken = Through(running); broken.SetValue("Armory", "1|oops");
            var brokenWorld = Build("broken data"); var K = Arm(brokenWorld, broken, true, null); In(K); var kept = new ValuesDictionary(); K.Dm.Save(kept);
            Test("D13", "data that cannot be read is kept as it is, never guessed at or reset", K.Dm.Frozen is { Length: > 0 } && kept.GetValue<string>("Armory") == "1|oops" && kept.GetValue<string>("Arena") == running.GetValue<string>("Arena"));

            // ================================================================ D14 a player leaves; the match ends
            In(S); Until(() => P(2).CanFight, 6);
            In(S); int bGun = GunIdAt(server, 2, 0);
            s_sessions.Remove(s_sessionB); ScNet.NotifyPeerLeft(ScNet.Peers.FirstOrDefault(p => p.PlayerIndex == 2)); var gone = server.Players.m_playersData.First(d => d.PlayerIndex == 2); server.Players.m_playersData.Remove(gone);
            var bEnd = B; B = null;
            Run(.3); In(S);
            Test("D14", "a player who leaves gives its records back and keeps its figures; the match goes on", S.Dm.Match.Find(keyB) is { Connected: false, Deaths: 31 } && S.Dm.Armory.LeaseOf(bGun) is null && S.Dm.Match.Phase == DmPhase.Running && A.Dm.View.Rows.Any(r => r.Key == keyB && !r.Connected), $"{S.Dm.Match.Find(keyB)?.Phase}");
            In(S); string stopped = S.Dm.StopMatch(); Run(.3); In(S);
            Test("D14", "the host ends the match: results on the server and the client, every life taken back, the inventories empty", stopped is null && S.Dm.Match.Phase == DmPhase.Results && A.Dm.View.Phase == DmPhase.Results && A.Dm.View.Result is { Reason: "stopped", Formal: true } result && result.Scores.First(sc => sc.Key == keyA).Kills == P(1).Kills
                && Enumerable.Range(0, 10).All(s => server.Slot(1, s) == 0 && a.Slot(1, s) == 0) && S.Dm.Armory.Parked.Count() == S.Dm.Armory.Count, $"{S.Dm.Match.Phase}; A {Slots(server, 1)}");
            Test("D14", "after the end nobody attacks or is hurt", Hit(1, 0, 500, ScHitPart.Body) == -1);
            Run(DmFixed.ResultsSeconds + .3); In(S);
            Test("D14", "the lobby returns; the host can go back to editing the map, where the mode governs nobody again", S.Dm.Match.Phase == DmPhase.Lobby && A.Dm.View.Phase == DmPhase.Lobby && S.Dm.EditMap() is null && !ScModes.FreeUse(server.Player[1]) && ScModes.For(server.Player[1]) is null);
            Run(.3); In(A);
            Test("D14", "the client follows: map editing, survival rules for its own weapons", A.Dm.View.Phase == DmPhase.Editing && !ScModes.FreeUse(a.Player[1]));
            In(S); S.Dm.Dispose();
            Test("D14", "when the world is closed the mode is gone with it", ScModes.Of(server.Project) is null);
        }
    }
}
#endif
