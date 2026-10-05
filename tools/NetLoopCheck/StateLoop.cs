// mp-state-consistency-20261002 (OpenSpec change weapon-state-consistency, cases C01-C12): gun state between a server and
// its clients, end to end, on the REAL platform assemblies without starting a game.
//
// One process plays the server and two clients ("A", who acts, and "B", who only watches). Each has its own world
// (project, entities, creative inventories, gun table); the process-wide "current" world, gun table and network role
// are switched to the end that is running, as NetLoopCheck's handshake check does for the role. Everything that crosses
// between them is a real packet: the CS adapter's own packet 197 and the platform's own InventorySyncPacket, serialised
// with the platform's writer, decoded by the platform's PacketManager and applied by the packet's own Handle, to real
// ComponentCreativeInventory objects resolved by entity id and component type as in the game.
//
// What stands in for the game: the gun state machine itself (SubsystemScGunBlockBehavior.UpdateGun needs models, audio
// and a rendered player). The harness performs the same calls in the same order: a client frame sends its input, then
// predicts a shot it may show; a server frame takes the presses and the held trigger of each remote input, commits one
// shot through ScGunMutation, counts it for the client, and ends with the adapter's end-of-update hook and the
// platform's end-of-frame inventory flush. Timing of a real connection, the Android runtime and anything visible or
// audible are NOT covered: those stay with the user's two-device test.
//
// --state     the cases on the present core (every check is expected to pass)
// --baseline  target assertions written against members that exist in the delivered builds as well; run against a
//             delivered core and adapter they are expected to FAIL (the faults the user reported), against the present
//             ones to pass.
// --gunloop   GunLoop.cs: the real gun state machine (SubsystemScGunBlockBehavior.Update) on a client end and a server
//             end, over the same wire.
// Every mode registers the message handlers as the game does: the core's own loader call (ScNet.RegisterCore; on a
// build without it, each module's Register in the loader's order), then the optional packages given with --modules.
// (mpc3's fault - the shot confirmation and the hit feedback both numbered 42 - was invisible here before: this
// harness registered four modules by hand and the hit feedback was not among them.)
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;

static partial class StateLoop {
    sealed record Check(string Case, string Name, bool Ok, string Detail);
    static readonly List<Check> s_checks = [];
    static void Test(string @case, string name, bool ok, string detail = "") {
        s_checks.Add(new(@case, name, ok, detail));
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} [{@case}] {name}{(detail.Length > 0 ? " :: " + detail : "")}");
    }
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    // ---------------------------------------------------------------- worlds
    const int PaletteSlots = 4000;   // a creative catalogue behind the ten open slots, the size of a modded game's
    sealed class World {
        public string Name;
        public Project Project = new();
        public ScGunRegistry Registry = new();
        public SubsystemPlayers Players = new();
        public readonly Dictionary<int, ComponentPlayer> Player = [];
        public ComponentCreativeInventory Inventory(int index) => (ComponentCreativeInventory)Player[index].ComponentMiner.Inventory;
        public int Slot(int index, int slot) => Inventory(index).GetSlotValue(slot);
        public int Held(int index) => Player[index].ComponentMiner.ActiveBlockValue;
    }
    static World Build(string name) {
        var w = new World { Name = name };
        w.Project.m_subsystems.Add(w.Players);
        w.Project.m_subsystems.Add(new SubsystemInventories());
        for (int index = 0; index <= 2; index++) AddPlayer(w, index);
        return w;
    }
    static ComponentPlayer AddPlayer(World w, int index) {
        var data = Blank<PlayerData>(); data.PlayerIndex = index;
        var player = Blank<ComponentPlayer>(); player.PlayerData = data; data.ComponentPlayer = player;
        player.ComponentHealth = Blank<ComponentHealth>();
        var body = Blank<ComponentBody>(); body.Position = new Vector3(index * 3, 64, 0); player.ComponentBody = body;
        var miner = Blank<ComponentMiner>(); player.ComponentMiner = miner;
        var creative = new ComponentCreativeInventory { OpenSlotsCount = 10 };
        for (int i = 0; i < 10; i++) creative.m_slots.Add(0);
        for (int i = 0; i < PaletteSlots; i++) creative.m_slots.Add(Terrain.MakeBlockValue(1 + i % 600, 0, i / 600));
        miner.Inventory = creative;
        var entity = new Entity { Id = 100 + index, m_project = w.Project, m_components = [player, creative, miner] };
        player.m_entity = entity; creative.m_entity = entity; miner.m_entity = entity;
        foreach (var former in w.Project.m_entities.Keys.Where(e => e.Id == entity.Id).ToList()) w.Project.m_entities.Remove(former);   // a respawn replaces the entity
        w.Project.m_entities[entity] = true;
        w.Players.m_playersData.RemoveAll(d => d.PlayerIndex == index);
        typeof(SubsystemPlayers).GetField("m_mainPlayer", All)?.SetValue(w.Players, null);
        w.Players.m_playersData.Add(data);
        w.Player[index] = player;
        return player;
    }

    // ---------------------------------------------------------------- the platform and its wire
    static bool s_server, s_client;
    static readonly List<Packet> s_queued = [];
    static readonly List<ClientSession> s_sessions = [];
    static ModLoader s_adapter; static Type s_adapterType;
    static ClientSession s_sessionA, s_sessionB;
    static void Enter(World w, bool server) { s_server = server; s_client = !server; GameManager.m_project = w.Project; ScGunRegistry.Current = w.Registry; }
    static byte[] Encode(Packet p) { var w = new PacketStreamWriter(); w.Write((byte)0x88); w.Write(p.ID); p.Serialize(new PacketSerializer(w)); return w.Data(); }
    static List<Packet> Take() { var batch = s_queued.ToList(); s_queued.Clear(); return batch; }
    static bool For(Packet p, ClientSession session) => (p.To is null || ReferenceEquals(p.To, session)) && !ReferenceEquals(p.Except, session);
    /// <summary>The server's packets to one client process, through the platform's wire; returns how many it handled.</summary>
    static int Deliver(IEnumerable<Packet> batch, World to, ClientSession session) {
        Enter(to, false); int n = 0;
        foreach (var p in batch) { if (!For(p, session)) continue; foreach (var d in PacketManager.DecodePackets(Encode(p))) { d.Handle(false); n++; } }
        return n;
    }
    /// <summary>Everything A queued, to the server, on A's connection.</summary>
    static int ToServer(World server, IEnumerable<Packet> batch = null) {
        var packets = (batch ?? Take()).ToList(); Enter(server, true); int n = 0;
        foreach (var p in packets) foreach (var d in PacketManager.DecodePackets(Encode(p))) { d.From = s_sessionA; d.Handle(true); n++; }
        return n;
    }
    static bool IsCs(Packet p) => p.GetType().Name == "ScCsgoPacket";
    static ushort Op(Packet p) => (ushort)p.GetType().GetField("Operation").GetValue(p);
    static string Shape(IEnumerable<Packet> batch) => string.Join(" ", batch.Select(p => IsCs(p) ? "cs" + Op(p) + (p.To is null ? "" : ReferenceEquals(p.To, s_sessionA) ? ">A" : ">B") : p.GetType().Name.Replace("Packet", "") + (p.To is null ? "" : ReferenceEquals(p.To, s_sessionA) ? ">A" : ">B")));

    static int s_gun, s_skinTemplate, s_ak, s_glock;
    static int Fresh(int variant) => Terrain.MakeBlockValue(s_gun, 0, GunSpec.WithId(variant, GunSpec.FreshFull));
    static int Instance(int variant, int id) => Terrain.MakeBlockValue(s_gun, 0, GunSpec.WithId(variant, id));
    static int IdOf(int value) => GunSpec.GetId(Terrain.ExtractData(value));
    static bool IsGun(int value) => Terrain.ExtractContents(value) == s_gun;
    static bool Usable(int value) => IsGun(value) && GunSpec.IsUsable(Terrain.ExtractData(value));

    // ---------------------------------------------------------------- the production registration
    /// <summary>Handlers that were replaced by a later registration (a build that refuses the second one lists it in ScNet.Conflicts instead).</summary>
    static readonly List<string> s_overwrites = [];
    static readonly List<Assembly> s_modules = [];
    static Dictionary<string, MethodInfo> HandlerTable() {
        var table = new Dictionary<string, MethodInfo>();
        foreach (string field in new[] { "s_onServer", "s_onClient" })
            if (typeof(ScNet).GetField(field, All)?.GetValue(null) is System.Collections.IDictionary handlers)
                foreach (System.Collections.DictionaryEntry e in handlers) table[$"{(field == "s_onServer" ? "client to server" : "server to client")} {e.Key}"] = ((Delegate)e.Value).Method;
        return table;
    }
    static string Describe(MethodInfo m) => $"{m.DeclaringType?.FullName?.Replace("Game.", "")}.{m.Name}";
    static void Registering(string what, Action register) {
        var before = HandlerTable(); register();
        foreach (var (key, method) in HandlerTable())
            if (before.TryGetValue(key, out var former) && former != method) s_overwrites.Add($"{key}: {Describe(former)} replaced by {Describe(method)} ({what})");
    }
    static readonly string[] CoreModules = ["ScNetMirror", "ScNetGuns", "ScNetFeedback", "ScNetWorkbench", "ScNetGrenades", "ScNetC4", "ScNetPresentation"];
    static void RegisterCore() {
        if (typeof(ScNet).GetMethod("RegisterCore", BindingFlags.Public | BindingFlags.Static) is { } production) Registering("ScNet.RegisterCore", () => production.Invoke(null, null));
        else foreach (string module in CoreModules)   // a delivered build: the same calls, in its loader's order
            if (typeof(ScNet).Assembly.GetType("Game." + module)?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes) is { } register) Registering(module, () => register.Invoke(null, null));
    }
    /// <summary>An optional package's handlers, registered by the call its own mod loader makes.</summary>
    static bool RegisterModule(string path) {
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
        foreach (string type in new[] { "Game.TacticalNet", "Game.ScNetAppearance", "Game.DmNet" })
            if (assembly.GetType(type)?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes) is { } register) {
                Registering(type, () => register.Invoke(null, null)); s_modules.Add(assembly); return true;
            }
        return false;
    }
    /// <summary>Every message number a module declares (public const ushort Op*), by module.</summary>
    static List<(string Module, string Name, int Op)> DeclaredOps() {
        var list = new List<(string, string, int)>();
        foreach (var assembly in new[] { typeof(ScNet).Assembly }.Concat(s_modules)) {
            Type[] types; try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t is not null).ToArray(); }
            foreach (var type in types.Where(t => t.IsAbstract && t.IsSealed))
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(ushort) && f.Name.StartsWith("Op", StringComparison.Ordinal)))
                    list.Add((type.Name, f.Name, (ushort)f.GetRawConstantValue()));
        }
        return list;
    }
    static string Duplicates(IEnumerable<(string Module, string Name, int Op)> ops) =>
        string.Join("; ", ops.GroupBy(o => o.Op).Where(g => g.Count() > 1).Select(g => $"{g.Key} = {string.Join(" = ", g.Select(o => o.Module + "." + o.Name))}"));

    static void Platform(string adapterPath, string compatPath, string[] modules) {
        var mySession = new ClientSession { PlayerIndex = 1, NetworkGuid = Guid.NewGuid(), NetworkState = NetworkState.Playing };
        var sessions = s_sessions;
        NetworkManager.IsServerRunningFunc = () => s_server; NetworkManager.IsClientRunningFunc = () => s_client;
        NetworkManager.MySessionFunc = () => s_client ? mySession : null; NetworkManager.ServerSessionsFunc = () => sessions;
        NetworkManager.QueueAction = o => { if (o is Packet p) s_queued.Add(p); };
        PacketManager.Initialize();
        s_sessionA = new ClientSession { PlayerIndex = 1, NetworkGuid = mySession.NetworkGuid, NetworkState = NetworkState.Playing };
        s_sessionB = new ClientSession { PlayerIndex = 2, NetworkGuid = Guid.NewGuid(), NetworkState = NetworkState.Playing };
        sessions.Add(s_sessionA); sessions.Add(s_sessionB);
        // The CS blocks the cases use (the game registers them from its block table).
        int free = 700;
        foreach (Type type in new[] { typeof(ScGunBlock), typeof(ScGunSkinTemplateBlock), typeof(ScGunCounterTemplateBlock) }) {
            while (BlocksManager.Blocks[free] is not null && BlocksManager.Blocks[free] is not AirBlock) free++;
            var block = (Block)Activator.CreateInstance(type); block.BlockIndex = free; BlocksManager.Blocks[free] = block; BlocksManager.BlockTypeToIndex[type] = free++;
        }
        s_gun = BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)]; s_skinTemplate = BlocksManager.BlockTypeToIndex[typeof(ScGunSkinTemplateBlock)];
        s_ak = Array.FindIndex(GunSpec.All, g => g.Name == "ak47"); s_glock = Array.FindIndex(GunSpec.All, g => g.Name == "glock18");
        if (s_ak < 0 || s_glock < 0) throw new InvalidOperationException("ak47/glock18 not in GunSpec.All");
        var entity = new AdapterEntity(File.ReadAllBytes(adapterPath), compatPath is null ? null : File.ReadAllBytes(compatPath)) { modInfo = new ModInfo { Name = "CS", PackageName = "zh667.ScCsgoKnives", Version = "1.4.0" } };
        ModsManager.ModList.Add(entity);
        RegisterCore();                 // the core loader's order: its handlers, then the transport
        ScNet.Initialize(entity);
        s_adapter = (ModLoader)ScNet.Transport; s_adapterType = s_adapter?.GetType();
        if (s_adapter is null) throw new InvalidOperationException("adapter not attached: " + ScNet.Decision);
        s_adapter.OnLoadingFinished([]);
        // The agents package registers with the core; the appearance package's joins need its own third-party models, so
        // it is registered by the case that reads the table (StateCases C13), after the play.
        // (the deathmatch package registers through its own loader's initialisation, in DmLoop.cs)
        foreach (string module in modules.Where(m => Path.GetFileName(m).Contains("Tactical", StringComparison.OrdinalIgnoreCase))) RegisterModule(module);
        s_laterModules = modules.Where(m => !Path.GetFileName(m).Contains("Tactical", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(m).Contains("Deathmatch", StringComparison.OrdinalIgnoreCase)).ToArray();
        s_deathmatchModule = modules.FirstOrDefault(m => Path.GetFileName(m).Contains("Deathmatch", StringComparison.OrdinalIgnoreCase));
    }
    static string[] s_laterModules = [];
    static string s_deathmatchModule;
    sealed class AdapterEntity(byte[] adapter, byte[] compat) : ModEntity {
        public override bool GetFile(string filename, Action<Stream> action) {
            byte[] bytes = filename == ScNet.AdapterPayload ? adapter : filename == ScNet.CompatPayload ? compat : null;
            if (bytes is null) return false;
            action(new MemoryStream(bytes)); return true;
        }
    }
    static object Call(string method, params object[] a) => s_adapterType.GetMethod(method).Invoke(s_adapter, a);

    /// <summary>A and B join the server's world: each hello on its own connection, the answers back.</summary>
    static void Join(World server, World a, World b) {
        Enter(b, false); s_adapter.OnNetworkPlayerStateChanged(NetworkState.ProjectLoaded); s_adapter.SubsystemUpdate(null, .016f);
        var hello = Take(); Enter(server, true);
        foreach (var p in hello) foreach (var d in PacketManager.DecodePackets(Encode(p))) { d.From = s_sessionB; d.Handle(true); }
        Deliver(Take(), b, s_sessionB);
        Enter(a, false); s_adapter.OnNetworkPlayerStateChanged(NetworkState.ProjectLoaded); s_adapter.SubsystemUpdate(null, .016f);
        ToServer(server); Deliver(Take(), a, s_sessionA);
    }

    // ---------------------------------------------------------------- frames (what stands in for the gun state machine)
    static readonly Ray3 Aim = new(new Vector3(3, 65.6f, 0), -Vector3.UnitZ);
    static ScGunResult Shoot(World s, int index, out ScGunMutation tx, Action<ScGunRecord> change = null) {
        Enter(s, true);
        var player = s.Player[index]; var inventory = player.ComponentMiner.Inventory;
        tx = ScGunMutation.Prepare(inventory, inventory.ActiveSlotIndex, ScGunHolders.PlayerKey(player, inventory.ActiveSlotIndex), out var why);
        return tx is null ? why : tx.Commit(change ?? (r => r.Rounds = Math.Max(0, r.Rounds - 1)));
    }
    /// <summary>The platform's end of a server frame: the adapter's end-of-update hook, then the platform's own inventory flush.</summary>
    static void EndServerFrame(World s) { Enter(s, true); s_adapter.SubsystemUpdate(null, .016f); InventoryPacketHelpers.FlushPendingSync(); }

    static partial void RunDeathmatch(ref bool ran);
    public static int Run(string adapterPath, string compatPath, string output, string refs, string mode, string[] modules) {
        bool baseline = mode == "baseline";
        try {
            Platform(adapterPath, compatPath, modules);
            bool ran = false;
            if (baseline) Baseline(); else if (mode == "gunloop") GunLoop.Run(); else if (mode == "dmloop") { RunDeathmatch(ref ran); if (!ran) Test("harness", "the deathmatch loop is compiled in (ScCsgoDeathmatch.dll among the references)", false); } else Cases.Run();
        }
        catch (Exception e) { Test("harness", "completed", false, (e is TargetInvocationException t ? t.InnerException : e).ToString()); }
        int failed = s_checks.Count(c => !c.Ok);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            mode = baseline ? "baseline target assertions (expected to fail on the delivered build that has the reported fault, pass on the present build)" : mode == "gunloop" ? "the gun state machine on a client and a server end" : mode == "dmloop" ? "the deathmatch package on a server and two clients" : "state cases",
            modules = modules.Select(Path.GetFileName).ToArray(), registered = HandlerTable().Count,
            failed, total = s_checks.Count, checks = s_checks,
            platform = new[] { "Survivalcraft", "Survivalcraft.Multiplayer" }.ToDictionary(n => n, n => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == n)?.ManifestModule.ModuleVersionId.ToString() ?? "not loaded"),
            core = typeof(ScNet).Assembly.ManifestModule.ModuleVersionId.ToString(), adapter = s_adapterType?.Assembly.ManifestModule.ModuleVersionId.ToString(),
            scope = "offline, one process, real platform packets and inventories; not a game session, no timing, nothing seen or heard"
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"{s_checks.Count} checks, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    // ================================================================ baseline: members of mpb only (reflection where they differ)
    static readonly Type Guns = typeof(ScNetGuns);
    static void ClientInput(ComponentPlayer player, bool press) {
        var send = Guns.GetMethod("SendInput", BindingFlags.Public | BindingFlags.Static);
        object[] tail = [true, false, press, false, false, false, 0, Aim, true, false];
        send.Invoke(null, send.GetParameters().Length == 11 ? [player, .. tail] : tail);
    }
    static void Predict(ComponentPlayer player, int data) {
        var predict = Guns.GetMethod("PredictShot", BindingFlags.Public | BindingFlags.Static);
        predict.Invoke(null, predict.GetParameters()[0].ParameterType == typeof(int) ? [data] : [player]);
    }
    /// <summary>The rounds the client's ammo HUD reads for the gun in this player's hand.</summary>
    static int Hud(World w, int index = 1) {
        Enter(w, false);
        var player = w.Player[index];
        // (The gun update observes the item in hand before it reads the rounds: UpdatePlayer.)
        Guns.GetMethod("Observe", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [player]);
        int rounds = GunSpec.GetRounds(Terrain.ExtractData(player.ComponentMiner.ActiveBlockValue));
        return Guns.GetMethod("ShownRounds", BindingFlags.Public | BindingFlags.Static) is { } shown ? (int)shown.Invoke(null, [player, rounds]) : rounds;
    }
    /// <summary>Lets every prediction time out, then runs the client's per-frame tick.</summary>
    static void ExpirePredictions(World w) {
        Enter(w, false);
        if (typeof(ScNet).GetField("Clock") is { } clock) { double now = ((Func<double>)clock.GetValue(null))() + 10; clock.SetValue(null, (Func<double>)(() => now)); }
        else if (Guns.GetField("s_predictions", All)?.GetValue(null) is System.Collections.IDictionary old)
            foreach (System.Collections.DictionaryEntry entry in old) entry.Value.GetType().GetField("At", All).SetValue(entry.Value, -100d);
        Guns.GetMethod("ClientTick").Invoke(null, null);
    }
    static int HitsReceived() => (int)typeof(ScNetFeedback).GetField("HitsReceived").GetValue(null);
    static List<string> Conflicts() => typeof(ScNet).GetField("Conflicts")?.GetValue(null) as List<string> ?? [];
    /// <summary>Shots the client shows that the server has not settled (-1 on a build without the member).</summary>
    static int PendingOf(World w, int index = 1) {
        Enter(w, false);
        Guns.GetMethod("Observe", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [w.Player[index]]);
        return Guns.GetMethod("PendingShots", BindingFlags.Public | BindingFlags.Static) is { } pending ? (int)pending.Invoke(null, [w.Player[index]]) : -1;
    }
    /// <summary>The server counts a committed shot for the client it was fired for (the gun's Fire does this in the game).</summary>
    static void ServerCounts(World s, int index = 1) { Enter(s, true); Guns.GetMethod("ServerShot", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [s.Player[index]]); }
    static int Rounds(World w, int id, int variant) { var saved = ScGunRegistry.Current; ScGunRegistry.Current = w.Registry; try { return GunSpec.GetRounds(GunSpec.WithId(variant, id)); } finally { ScGunRegistry.Current = saved; } }
    static void ServerRows(World s) { Enter(s, true); ScNetMirror.Flush(); }

    static void Baseline() {
        if (typeof(ScNet).GetField("Clock") is { } clockField) { double t = 1000; clockField.SetValue(null, (Func<double>)(() => t)); }
        var server = Build("server"); var a = Build("client A"); var b = Build("client B");
        Join(server, a, b);
        Test("setup", "two clients accepted", ScNet.Peers.Count == 2, $"{ScNet.Peers.Count} peers");

        // ---- T5 (R07): one number, one meaning, in the registration the game performs
        string duplicates = Duplicates(DeclaredOps().Where(o => o.Op >= 32));
        Test("R07", "T5 no message number has two meanings: every module's declared numbers differ and no handler was replaced or refused in the game's own registration",
            duplicates.Length == 0 && s_overwrites.Count == 0 && Conflicts().Count == 0, $"declared twice: [{duplicates}]; replaced: [{string.Join("; ", s_overwrites)}]; refused: [{string.Join("; ", Conflicts())}]; {HandlerTable().Count} handlers");

        // ---- T1/T2 (C01): a fresh AK, first shot
        int fresh = Fresh(s_ak);
        foreach (var w in new[] { server, a, b }) { w.Inventory(1).m_slots[0] = fresh; w.Inventory(1).ActiveSlotIndex = 0; }
        Enter(a, false); ClientInput(a.Player[1], true); Predict(a.Player[1], Terrain.ExtractData(fresh));
        ToServer(server);
        int hits = HitsReceived();
        var shot = Shoot(server, 1, out _);
        ServerCounts(server);
        EndServerFrame(server); ServerRows(server);
        var batch = Take();
        Deliver(batch, a, s_sessionA); Deliver(batch, b, s_sessionB);
        int serverValue = server.Slot(1, 0), serverRounds = Rounds(server, IdOf(serverValue), s_ak);
        // ---- T6/T7 (R07/R08): what the confirmation of a shot at nothing does on the client, before any time passes
        Enter(a, false);
        Test("R07", "T6 a shot at nothing that the server confirmed tells the client of no hit", HitsReceived() == hits, $"hit messages taken by the client: {HitsReceived() - hits}; packets [{Shape(batch)}]");
        Test("R08", "T7 the confirmation settles the shown shot at once: nothing pending, the readout is the server's 29 with no time-out", PendingOf(a) == 0 && Hud(a) == serverRounds && serverRounds == 29,
            $"pending {PendingOf(a)}, HUD {Hud(a)}, server {serverRounds}");
        ExpirePredictions(a);
        Test("C01", "T1 after the server gave the fresh AK its record, the client's slot holds the same item as the server's", shot == ScGunResult.Success && a.Slot(1, 0) == serverValue,
            $"server slot id {IdOf(serverValue)}, client slot id {IdOf(a.Slot(1, 0))}; packets [{Shape(batch)}]");
        Test("C01", "T1 and so does the watching client's copy of that inventory", b.Slot(1, 0) == serverValue, $"B's copy id {IdOf(b.Slot(1, 0))}");
        Test("C01", "T2 the client's ammo readout shows the server's rounds (29), not the template's 30", Hud(a) == serverRounds && serverRounds == 29, $"HUD {Hud(a)}, server {serverRounds}");

        // ---- T3 (C03): the server separates a duplicated Glock, record 7 -> the next free id
        Enter(server, true);
        while (server.Registry.Next < 7) server.Registry.Allocate(s_ak, 30, false, 1200);
        int seven = server.Registry.Allocate(s_glock, 5, false, 1185);
        while (server.Registry.Next < 12) server.Registry.Allocate(s_ak, 30, false, 1200);
        int glock7 = Instance(s_glock, seven);
        foreach (var w in new[] { server, a, b }) { w.Inventory(1).m_slots[0] = glock7; w.Inventory(1).ActiveSlotIndex = 0; }
        server.Inventory(0).m_slots[3] = glock7;                      // the other holder of record 7 (a copy handed on)
        ServerRows(server); batch = Take(); Deliver(batch, a, s_sessionA); Deliver(batch, b, s_sessionB);
        var locator = ScGunMutation.HolderLocator;
        ScGunMutation.HolderLocator = (id, holder) => id == seven ? ["the other holder"] : [];
        try {
            Enter(a, false);
            for (int i = 0; i < 5; i++) { ClientInput(a.Player[1], true); Predict(a.Player[1], Terrain.ExtractData(a.Held(1))); }
            int mirrorSevenWhilePredicting = Rounds(a, seven, s_glock);
            ToServer(server);
            shot = Shoot(server, 1, out var split);
            int twelve = split?.Id ?? -1;
            EndServerFrame(server); ServerRows(server);
            // the server's reload of the separated gun
            var reload = Shoot(server, 1, out _, r => r.Rounds = 20);
            EndServerFrame(server); ServerRows(server);
            batch = Take(); Deliver(batch, a, s_sessionA); Deliver(batch, b, s_sessionB);
            Test("C03", "T3 the server separated the copy: record 7 keeps 5 rounds, the client's gun is record 12 with 20 after its reload", shot == ScGunResult.Success && reload == ScGunResult.Success && twelve == 12 && Rounds(server, seven, s_glock) == 5 && Rounds(server, twelve, s_glock) == 20,
                $"shot {shot}, reload {reload}, new id {twelve}, server 7={Rounds(server, seven, s_glock)} 12={Rounds(server, twelve, s_glock)}");
            Test("C03", "T3 a prediction never changes the client's copy of record 7", mirrorSevenWhilePredicting == 5, $"client's record 7 read {mirrorSevenWhilePredicting} while 5 shots were predicted");
            Test("C03", "T3 the client's slot is bound to record 12", IdOf(a.Slot(1, 0)) == twelve, $"client slot id {IdOf(a.Slot(1, 0))}");
            ExpirePredictions(a);
            Test("C03", "T3 after every prediction timed out the readout is record 12's 20 rounds, and record 7 is still the server's 5", Hud(a) == 20 && Rounds(a, seven, s_glock) == 5, $"HUD {Hud(a)}, client's record 7 = {Rounds(a, seven, s_glock)}");
        }
        finally { ScGunMutation.HolderLocator = locator; }

        // ---- T4 (C07): predictions of one world must not touch the next world's record of the same number
        Enter(a, false);
        a.Inventory(1).m_slots[0] = glock7;
        ClientInput(a.Player[1], true); Predict(a.Player[1], Terrain.ExtractData(glock7));
        var next = Build("client A, next world");
        Enter(next, false);
        bool parsed = next.Registry.ApplyNetworkRow(seven, "v=" + s_glock + ",r=20,s=0,d=1200,m=1200,n=1,c=-1,p=0,ct=0,k=0,gl=0,gp=-1,gv=0,rc=0,ov=0,kc=0", 0);
        next.Inventory(1).m_slots[0] = glock7;
        ExpirePredictions(next);
        Test("C07", "T4 a prediction left over from the former world does not change the new world's record 7", parsed && Rounds(next, seven, s_glock) == 20 && Hud(next) == 20, $"row parsed {parsed}, new world's record 7 = {Rounds(next, seven, s_glock)}, HUD {Hud(next)}");
    }
}
