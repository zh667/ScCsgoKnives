// mp-user-logs-20261002: the CS appearance integration's two multiplayer corrections, on the real NMM 1.1 / NEO 1.4
// assemblies, without a game:
//  1. NEO's item-anchor bone buffer (ComponentNeoModel.AbsoluteBoneTransforms) is valid before NMM draws a held item on a
//     model that is not ours - also when NEO's own update never ran for that player (a multiplayer client observing another
//     player with the platform's CompatNet mod, or an entity's first drawn frame).
//  2. A player's CS T/CT model choice travels owner → server → the other clients (ScNetAppearance); choices that do not
//     involve our models are not sent.
// Not covered here: NMM's own SetResModel applying a different model (it loads model resources; it is the call NMM's own
// selection dialog makes), and any real session.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Engine;
using Engine.Graphics;
using Game;
using NekoMeko.Common;
using NekoMeko.Components;
using Neorxna.Components;

var checks = new List<(string Name, bool Ok, string Detail)>();
void Check(string name, bool ok, string detail = "") { checks.Add((name, ok, detail)); Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " :: " + detail : "")}"); }
T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
void SetAuto(object o, Type declaring, string property, object value) {
    var p = declaring.GetProperty(property, Any);
    if (p?.SetMethod is not null) { p.SetValue(o, value); return; }
    (declaring.GetField($"<{property}>k__BackingField", Any) ?? throw new MissingFieldException(declaring.Name, property)).SetValue(o, value);
}
try {
    // ---------------------------------------------------------------- 1. NEO's item-anchor buffer
    Model Skeleton(int bones) {
        var model = new Model();
        for (int i = 0; i < bones; i++) {
            var bone = new ModelBone { Model = model, Index = i, Name = "b" + i, Transform = Matrix.CreateTranslation(0, .1f * (i + 1), 0) };
            if (i > 0) { bone.ParentBone = model.m_bones[i - 1]; model.m_bones[i - 1].m_childBones.Add(bone); } else model.m_rootBone = bone;
            model.m_bones.Add(bone);
        }
        return model;
    }
    (ComponentCsPlayerAppearance Adapter, ComponentHumanModel Human, ComponentNeoModel Neo) Player(int bones) {
        var human = Blank<ComponentHumanModel>(); human.m_model = Skeleton(bones); human.m_boneTransforms = Enumerable.Repeat((Matrix?)Matrix.Identity, bones).ToArray();
        var neo = Blank<ComponentNeoModel>(); neo.AbsoluteBoneTransforms = Array.Empty<Matrix>();
        var adapter = Blank<ComponentCsPlayerAppearance>();
        adapter.TargetComponent = human; SetAuto(adapter, typeof(ComponentNekoMekoModel), "ComponentNeoModel", neo);
        return (adapter, human, neo);
    }
    var ensure = typeof(ComponentCsPlayerAppearance).GetMethod("EnsureNeoItemBones", Any) ?? throw new MissingMethodException("EnsureNeoItemBones");
    bool Ensure(ComponentCsPlayerAppearance adapter, ComponentHumanModel human) => (bool)ensure.Invoke(adapter, [human]);

    var p = Player(12);
    Check("the reported state: NEO never updated this player, its buffer is empty and the model has bones", p.Neo.AbsoluteBoneTransforms.Length == 0 && p.Human.Model.Bones.Count == 12);
    bool ok = Ensure(p.Adapter, p.Human);
    var filled = p.Neo.AbsoluteBoneTransforms;
    // What NEO's own update would have written: the engine's bone hierarchy for the current pose.
    var expected = new Matrix[12]; p.Human.ProcessBoneHierarchy(p.Human.Model.RootBone, Matrix.Identity, expected);
    Check("the buffer is made valid for every bone of the current model, as NEO's own update fills it", ok && filled.Length == 12 && filled.SequenceEqual(expected) && expected.Any(m => m != default), $"length {filled.Length}");
    // An index NMM would use (any bone of this model, e.g. the hand) is inside the array now.
    Check("every bone index of the model is inside the buffer", p.Human.Model.Bones.All(b => b.Index < p.Neo.AbsoluteBoneTransforms.Length));
    p.Human.m_boneTransforms[0] = Matrix.CreateTranslation(5, 0, 0);
    Ensure(p.Adapter, p.Human);
    Check("while NEO stays silent the buffer follows the pose of each draw", ReferenceEquals(p.Neo.AbsoluteBoneTransforms, filled) && Math.Abs(filled[0].Translation.X - 5) < 1e-4f, $"x {filled[0].Translation.X}");
    var neoOwn = new Matrix[12]; neoOwn[3] = Matrix.CreateTranslation(9, 9, 9); p.Neo.AbsoluteBoneTransforms = neoOwn;
    Check("a buffer NEO filled itself is left alone", Ensure(p.Adapter, p.Human) && ReferenceEquals(p.Neo.AbsoluteBoneTransforms, neoOwn) && neoOwn[3].Translation == new Vector3(9, 9, 9));
    // NMM swapped to a model with more bones after NEO sized its array (same frame, or NEO not updating).
    p.Human.m_model = Skeleton(20); p.Human.m_boneTransforms = Enumerable.Repeat((Matrix?)Matrix.Identity, 20).ToArray();
    ok = Ensure(p.Adapter, p.Human);
    Check("a buffer too short for the current model is replaced", ok && p.Neo.AbsoluteBoneTransforms.Length == 20 && !ReferenceEquals(p.Neo.AbsoluteBoneTransforms, neoOwn), $"length {p.Neo.AbsoluteBoneTransforms.Length}");
    var q = Player(8); q.Human.m_boneTransforms = new Matrix?[4];
    Check("without a usable pose nothing is touched and NMM's drawing is skipped", !Ensure(q.Adapter, q.Human) && q.Neo.AbsoluteBoneTransforms.Length == 0);
    var noModel = Player(3); noModel.Human.m_model = null;
    Check("without a model nothing is touched", !Ensure(noModel.Adapter, noModel.Human));

    // ---------------------------------------------------------------- 2. the CS model choice over the network
    var transportProperty = typeof(ScNet).GetProperty("Transport");
    var saved = transportProperty.GetValue(null);
    try {
        Fake Transport(ScNetRole role, ScNetHandshake handshake, params int[] peers) {
            var t = (Fake)DispatchProxy.Create(typeof(IScNetTransport), typeof(Fake));
            t.Role = role; t.Handshake = handshake; t.Peers = peers.Select(i => new ScNetPeer { PlayerIndex = i }).ToList();
            transportProperty.SetValue(null, t); return t;
        }
        ComponentCsPlayerAppearance Appearance(int index, string modelKey, string skinKey) {
            var adapter = Blank<ComponentCsPlayerAppearance>();
            var player = Blank<ComponentPlayer>(); player.PlayerData = Blank<PlayerData>(); player.PlayerData.PlayerIndex = index; player.PlayerData.ComponentPlayer = player;
            var entity = Blank<GameEntitySystem.Entity>(); entity.m_components = [player, adapter]; player.m_entity = entity; adapter.m_entity = entity;
            SetAuto(adapter, typeof(ComponentNekoMekoModel), "ComponentPlayer", player);
            Select(adapter, modelKey, skinKey); return adapter;
        }
        void Select(ComponentCsPlayerAppearance adapter, string modelKey, string skinKey) {
            SetAuto(adapter, typeof(ComponentNekoMekoModel), "ResModel", modelKey is null ? null : new NekoResModel { Key = modelKey });
            SetAuto(adapter, typeof(ComponentNekoMekoModel), "ResSkin", skinKey is null ? null : new NekoResSkin { Key = skinKey });
        }
        var checkAt = typeof(ScNetAppearance).GetField("s_checkAt", BindingFlags.Static | BindingFlags.NonPublic);
        void Tick(ComponentCsPlayerAppearance adapter) { checkAt.SetValue(null, 0d); ScNetAppearance.Tick(adapter); }
        (string Model, string Skin) Keys(byte[] payload, bool indexed, out int index) { var r = new ScNetReader(payload); index = indexed ? r.Int() : -1; return (r.String(96), r.String(96)); }
        ScNetAppearance.Register(); ScNetAppearance.Clear();

        // a client's own choice
        var client = Transport(ScNetRole.Client, ScNetHandshake.Accepted); client.Local = pl => pl.PlayerData.PlayerIndex == 1;
        var me = Appearance(1, "Model-ScMale-BoneSet-ScMale", "Skin-Walter-ModelKey-ScMale");
        Tick(me);
        Check("a choice that does not involve a CS model is not sent", client.Sent.Count == 0);
        Select(me, "zh667.cs.ct", "zh667.cs.ct.default"); Tick(me); Tick(me);
        var sent = client.Sent.SingleOrDefault();
        Check("taking the CT model is announced to the server once", sent.Op == ScNetAppearance.OpSelect && sent.Payload is not null && Keys(sent.Payload, false, out _) == ("zh667.cs.ct", "zh667.cs.ct.default"), $"{client.Sent.Count} message(s)");
        Select(me, "zh667.cs.t", "zh667.cs.t.default"); Tick(me);
        Check("changing to the T model is announced", client.Sent.Count == 2 && Keys(client.Sent[1].Payload, false, out _).Model == "zh667.cs.t");
        Select(me, "Model-ScMale-BoneSet-ScMale", "Skin-Walter-ModelKey-ScMale"); Tick(me); Tick(me);
        Check("leaving the CS model is announced with the model it was left for (observers take the agent off)", client.Sent.Count == 3 && Keys(client.Sent[2].Payload, false, out _).Model == "Model-ScMale-BoneSet-ScMale");
        Select(me, "Model-ScFemale-BoneSet-ScFemale", "Skin-Doris-ModelKey-ScFemale"); Tick(me);
        Check("after that, other NMM choices are again not ours to send", client.Sent.Count == 3);
        ScNetAppearance.Clear();
        var blocked = Transport(ScNetRole.Client, ScNetHandshake.TimedOut); blocked.Local = pl => pl.PlayerData.PlayerIndex == 1;
        Select(me, "zh667.cs.ct", "zh667.cs.ct.default"); Tick(me);
        Check("a client whose CS network layer is not accepted sends nothing", blocked.Sent.Count == 0);

        // the server: applies the choice to its own instance of that player and tells the other clients
        ScNetAppearance.Clear();
        var server = Transport(ScNetRole.Host, ScNetHandshake.NotApplicable, 1, 2); server.Local = pl => pl.PlayerData.PlayerIndex == 0;
        var serverInstance = Appearance(1, "zh667.cs.ct", "zh667.cs.ct.default"); // already holds what the client announces: NMM's own SetResModel is not exercised here
        var players = new SubsystemPlayers(); var project = new GameEntitySystem.Project(); project.m_subsystems.Add(players);
        serverInstance.ComponentPlayer.ComponentHealth = Blank<ComponentHealth>(); players.m_playersData.Add(serverInstance.ComponentPlayer.PlayerData);
        var savedProject = GameManager.m_project; GameManager.m_project = project;
        try {
            var w = new ScNetWriter(); w.String("zh667.cs.ct").String("zh667.cs.ct.default");
            ScNet.ReceiveOnServer(server.Peers[0], ScNetAppearance.OpSelect, w.ToArray());
            var relayed = server.Broadcasts.SingleOrDefault();
            Check("the server relays a client's CS choice to the other clients, naming the player", relayed.Op == ScNetAppearance.OpPlayerModel && ReferenceEquals(relayed.Except, server.Peers[0])
                && relayed.Payload is not null && Keys(relayed.Payload, true, out int index) == ("zh667.cs.ct", "zh667.cs.ct.default") && index == 1, $"{server.Broadcasts.Count} broadcast(s)");
            ScNet.ReceiveOnServer(server.Peers[0], ScNetAppearance.OpSelect, [200, 1, 2]);
            Check("a malformed choice is dropped", server.Broadcasts.Count == 1);
            // the host's own choice goes to every client
            var host = Appearance(0, "zh667.cs.t", "zh667.cs.t.default");
            Tick(host);
            var own = server.Broadcasts.Last();
            Check("the host's own CS choice is broadcast to every client", server.Broadcasts.Count == 2 && own.Except is null && Keys(own.Payload, true, out int hostIndex).Model == "zh667.cs.t" && hostIndex == 0);
        }
        finally { GameManager.m_project = savedProject; }

        // an observer: keeps what it was told until that player's component exists and NMM initialised it
        ScNetAppearance.Clear();
        var observer = Transport(ScNetRole.Client, ScNetHandshake.Accepted); observer.Local = pl => pl.PlayerData.PlayerIndex == 2;
        var told = new ScNetWriter(); told.Int(1).String("zh667.cs.ct").String("zh667.cs.ct.default");
        ScNet.ReceiveOnClient(ScNetAppearance.OpPlayerModel, told.ToArray());
        var seen = Appearance(1, "zh667.cs.ct", "zh667.cs.ct.default");
        int applied = ScNetAppearance.Applied; Tick(seen);
        Check("an observer whose copy already shows that model changes nothing", ScNetAppearance.Applied == applied);
        var mine = Appearance(2, "Model-ScMale-BoneSet-ScMale", "Skin-Walter-ModelKey-ScMale");
        var forMe = new ScNetWriter(); forMe.Int(2).String("zh667.cs.t").String("zh667.cs.t.default");
        ScNet.ReceiveOnClient(ScNetAppearance.OpPlayerModel, forMe.ToArray()); Tick(mine);
        Check("a message about this process's own player never changes its model", mine.ModelKey == "Model-ScMale-BoneSet-ScMale" && observer.Sent.Count == 0);
    }
    finally { transportProperty.SetValue(null, saved); ScNetAppearance.Clear(); }
}
catch (Exception e) { Check("harness completed", false, e.ToString()); }
int failed = checks.Count(c => !c.Ok);
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(new { failed, checks = checks.Select(c => new { c.Name, c.Ok, c.Detail }),
    thirdParty = new { nmm = typeof(ComponentNekoMekoModel).Assembly.ManifestModule.ModuleVersionId, neo = typeof(ComponentNeoModel).Assembly.ManifestModule.ModuleVersionId },
    scope = "offline, real NMM/NEO assemblies; not a game session" }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count} checks, {failed} failed");
return failed == 0 ? 0 : 1;

public class Fake : DispatchProxy {
    public ScNetRole Role; public ScNetHandshake Handshake; public List<ScNetPeer> Peers = [];
    public Func<ComponentPlayer, bool> Local = _ => true;
    public readonly List<(ushort Op, byte[] Payload)> Sent = [];
    public readonly List<(ushort Op, byte[] Payload, ScNetPeer Except)> Broadcasts = [];
    protected override object Invoke(MethodInfo method, object[] a) {
        switch (method.Name) {
            case "get_Role": return Role;
            case "get_Handshake": return Handshake;
            case "get_HandshakeDetail": return "";
            case "get_Peers": return Peers;
            case "IsLocal": return Local((ComponentPlayer)a[0]);
            case "SendToServer": Sent.Add(((ushort)a[0], (byte[])a[1])); return true;
            case "SendTo": return true;
            case "Broadcast": Broadcasts.Add(((ushort)a[0], (byte[])a[1], (ScNetPeer)a[2])); return null;
            default: throw new NotSupportedException(method.Name);
        }
    }
}
