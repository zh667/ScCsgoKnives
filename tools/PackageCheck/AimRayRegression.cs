using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;

/// <summary>Where a shot leaves the character (docs/tasks/first-person-eye-shot-20261001.md, user decisions 2026-10-01): the
/// damage ray always leaves the eye; in first person along the crosshair (the aim), in every other view (third person, debug,
/// orbit, perspective-view mods) along the character's own look, i.e. where the first-person crosshair would be, so switching
/// views changes neither the line nor the impact. A strike leaves the eye along the aim (the character's look under a free
/// camera). In multiplayer the server starts a client's shot at its own copy of that player's eye along the direction the
/// client's view chose, never consulting a camera of its own.</summary>
static class AimRayRegression {
    internal record Result(string Name, bool Ok, string Detail);

    /// <summary>A transport for the server and client roles, without a network.</summary>
    public class FakeTransport : DispatchProxy {
        public object Role, Handshake, Peers;
        public Func<ComponentPlayer, bool> Local = _ => true;
        public readonly List<(ushort Op, byte[] Payload)> Sent = [];
        /// <summary>Server → clients messages (2026-10-02), with the peer left out.</summary>
        public readonly List<(ushort Op, byte[] Payload, object Except)> Broadcasts = [];
        protected override object Invoke(MethodInfo method, object[] args) => method.Name switch {
            "get_Role" => Role,
            "get_Handshake" => Handshake,
            "get_HandshakeDetail" => "",
            "get_Peers" => Peers,
            "IsLocal" => Local((ComponentPlayer)args[0]),
            "SendToServer" => Record((ushort)args[0], (byte[])args[1]),
            "SendTo" => true,
            "Broadcast" => Broadcast((ushort)args[0], (byte[])args[1], args[2]),
            _ => throw new NotSupportedException(method.Name)
        };
        bool Record(ushort op, byte[] payload) { Sent.Add((op, payload)); return true; }
        object Broadcast(ushort op, byte[] payload, object except) { Broadcasts.Add((op, payload, except)); return null; }
    }

    static ComponentPlayer Player(int index, Vector3 eye, float yaw, float pitch, Camera camera) {
        var body = (ComponentBody)RuntimeHelpers.GetUninitializedObject(typeof(ComponentBody));
        body.Position = eye - new Vector3(0, 1.6f, 0);
        body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        var model = (ComponentCreatureModel)RuntimeHelpers.GetUninitializedObject(typeof(ComponentHumanModel));
        model.m_eyePosition = eye;
        model.m_eyeRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
        var data = (PlayerData)RuntimeHelpers.GetUninitializedObject(typeof(PlayerData));
        data.PlayerIndex = index;
        if (camera is not null) {
            var widget = (GameWidget)RuntimeHelpers.GetUninitializedObject(typeof(GameWidget));
            widget.m_activeCamera = camera; widget.PlayerData = data; data.m_gameWidget = widget;
        }
        // No camera: a remote client's player on the server has no game widget (its GameWidget getter throws).
        var player = (ComponentPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ComponentPlayer));
        player.PlayerData = data; player.ComponentBody = body; player.ComponentCreatureModel = model;
        return player;
    }
    static T Camera<T>() where T : Camera => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static Vector3 MusketOrigin(ComponentPlayer p) {
        Matrix m = p.ComponentBody.Matrix;
        return p.ComponentCreatureModel.EyePosition + m.Right * .3f - m.Up * .2f;
    }
    static string V(Vector3 v) => FormattableString.Invariant($"{v.X:0.000},{v.Y:0.000},{v.Z:0.000}");

    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("aim-ray/" + name, ok, detail));
        var aimRay = mod.GetType("Game.ScAimRay", true);
        var resolveMethod = aimRay.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
        Ray3 Resolve(ComponentPlayer p, Ray3 aim, bool melee = false) {
            try { return (Ray3)resolveMethod.Invoke(null, [p, aim, melee]); } catch (TargetInvocationException e) { throw e.InnerException; }
        }
        Vector3 look = Vector3.Normalize(new Vector3(.2f, -.1f, -1f));

        // Single player: the same character state seen through each view.
        var eye = new Vector3(10.5f, 66.62f, 20.5f);
        Ray3 Through<T>(Ray3 aim, bool melee = false) where T : Camera => Resolve(Player(0, eye, .4f, -.1f, Camera<T>()), aim, melee);
        var fpp = Player(0, eye, .4f, -.1f, Camera<FppCamera>());
        Vector3 own = Vector3.Normalize(Matrix.CreateFromQuaternion(fpp.ComponentCreatureModel.EyeRotation).Forward);
        var r = Resolve(fpp, new Ray3(eye, look));
        Check("first person: the damage ray leaves the eye along the crosshair (not the musket origin 0.36 m beside it)",
            Vector3.Distance(r.Position, eye) < 1e-4f && Vector3.Distance(r.Direction, look) < 1e-5f && Vector3.Distance(MusketOrigin(fpp), eye) > .3f,
            $"origin {V(r.Position)} eye {V(eye)} musket {V(MusketOrigin(fpp))} dir {V(r.Direction)}");
        var crosshair = Resolve(fpp, new Ray3(eye, own));
        var cameraBehind = eye + new Vector3(0, 1.2f, 3.5f);
        foreach (var (view, shot) in new[] {
            ("third person", Through<TppCamera>(new Ray3(cameraBehind, look))),
            ("debug camera", Through<DebugCamera>(new Ray3(eye + new Vector3(4, 1, 0), look))),
            ("orbit camera", Through<OrbitCamera>(new Ray3(eye + new Vector3(0, 3, 3), look))) })
            Check($"{view}: the same line as the first-person crosshair (the eye along the character's look), whatever the camera's position and direction",
                Vector3.Distance(shot.Position, crosshair.Position) < 1e-4f && Vector3.Distance(shot.Direction, crosshair.Direction) < 1e-4f && Vector3.Distance(shot.Direction, look) > .1f,
                $"origin {V(shot.Position)} dir {V(shot.Direction)} crosshair {V(crosshair.Position)} {V(crosshair.Direction)} camera dir {V(look)}");

        r = Through<TppCamera>(new Ray3(cameraBehind, look), melee: true);
        Check("a strike leaves the eye along the aim in third person", Vector3.Distance(r.Position, eye) < 1e-4f && Vector3.Distance(r.Direction, look) < 1e-5f, $"{V(r.Position)} {V(r.Direction)}");
        r = Through<DebugCamera>(new Ray3(eye + new Vector3(4, 1, 0), look), melee: true);
        Check("a strike under a free camera leaves the eye along the character's look", Vector3.Distance(r.Position, eye) < 1e-4f && Vector3.Distance(r.Direction, own) < 1e-4f, $"{V(r.Position)} {V(r.Direction)}");

        // Third person (2026-10-01): the held gun's barrel aims at what the shot meets, so the tracer leaves the muzzle along it.
        var math = mod.GetType("Game.ScThirdPersonMath", true).GetMethod("ConvergeBore", BindingFlags.Public | BindingFlags.Static);
        if (math is null) { Check("third-person barrel converges on the aim point (ScThirdPersonMath.ConvergeBore)", false, "method missing: an older core, barrel parallel to the look"); goto multiplayer; }
        Vector3 Bore(Vector3 grip, Vector3 muzzle, Vector3 fist, Vector3 aim, Vector3 target, float max) => (Vector3)math.Invoke(null, [grip, muzzle, fist, aim, target, max]);
        Matrix World(Vector3 grip, Vector3 fist, Vector3 forward) => Matrix.CreateTranslation(-grip) * Matrix.CreateWorld(fist, forward, Vector3.UnitY);
        float Miss(Vector3 grip, Vector3 muzzle, Vector3 fist, Vector3 bore, Vector3 target) {
            Vector3 m = Vector3.Transform(muzzle, World(grip, fist, bore)); Vector3 d = target - m; float t = Vector3.Dot(d, bore);
            return MathF.Sqrt(MathF.Max(0, d.LengthSquared() - t * t));
        }
        Vector3 gripL = new(0, -.08f, .1f), muzzleL = new(0, .03f, -.62f), fistW = new(.07f, 72f, 0), level = -Vector3.UnitZ;
        Vector3 eyeW = new(0, 72.55f, .4f);
        foreach (var (name, lookDir, distance) in new[] { ("level, wall 6 m", level, 6f), ("looking down at the ground 3 m ahead", Vector3.Normalize(new Vector3(0, -.48f, -.88f)), 3f),
                                                         ("looking up, 20 m", Vector3.Normalize(new Vector3(.1f, .6f, -.8f)), 20f) }) {
            Vector3 target = eyeW + lookDir * distance, aim = lookDir;
            Vector3 bore = Bore(gripL, muzzleL, fistW, aim, target, .35f);
            float miss = Miss(gripL, muzzleL, fistW, bore, target), parallel = Miss(gripL, muzzleL, fistW, aim, target);
            Check($"third-person barrel converges ({name}): the line from the muzzle meets the aim point", miss < .01f && parallel > .1f, $"miss {miss:0.000} m (parallel barrel {parallel:0.000} m)");
        }
        Vector3 far = Bore(gripL, muzzleL, fistW, level, eyeW + level * 64, .35f);
        Check("third-person barrel at 64 m stays almost parallel to the look", MathF.Acos(Math.Clamp(Vector3.Dot(far, level), -1, 1)) < .01f, V(far));
        Vector3 close = Bore(gripL, muzzleL, fistW, level, eyeW + level * .2f + new Vector3(0, 2, 0), .35f);
        float closeAngle = MathF.Acos(Math.Clamp(Vector3.Dot(close, level), -1, 1));
        Check("third-person barrel turns at most the bound for a point right above the gun", MathF.Abs(closeAngle - .35f) < 1e-3f, $"{closeAngle:0.0000} rad");

    multiplayer:
        // Round 10: a skinned player (the CS player appearance) is drawn by the appearance, which reports its muzzles; the shot's
        // tracer takes them (the user's log: every third-person shot "tracer-from origin").
        var tp = mod.GetType("Game.ScThirdPerson", true);
        var report = tp.GetMethod("ReportMuzzle", BindingFlags.Public | BindingFlags.Static);
        if (report is null) Check("a skinned player's drawn muzzle is the tracer's start (ScThirdPerson.ReportMuzzle)", false, "method missing: an older core");
        else {
            var entity = (GameEntitySystem.Entity)RuntimeHelpers.GetUninitializedObject(typeof(GameEntitySystem.Entity));
            var human = (ComponentHumanModel)RuntimeHelpers.GetUninitializedObject(typeof(ComponentHumanModel));
            var owner = (ComponentPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ComponentPlayer));
            entity.m_components = [human, owner]; human.m_entity = entity; owner.m_entity = entity;
            var getMuzzle = tp.GetMethod("TryGetMuzzleWorld", BindingFlags.Public | BindingFlags.Static);
            (bool Ok, Vector3 At) Muzzle(string gun, string bone) { var args = new object[] { owner, gun, bone, null }; bool ok = (bool)getMuzzle.Invoke(null, args); return (ok, (Vector3)args[3]); }
            Vector3 right = new(1, 2, 3), leftMuzzle = new(4, 5, 6);
            var before = Muzzle("awp", null);
            report.Invoke(null, [human, "awp", right, leftMuzzle]);
            var got = Muzzle("awp", null); var other = Muzzle("ak47", null);
            report.Invoke(null, [human, "elite", right, leftMuzzle]);
            var dualLeft = Muzzle("elite", mod.GetType("Game.GunSpec", true).GetMethod("ForAsset").Invoke(null, ["elite"]) is { } elite ? (string)elite.GetType().GetField("LeftMuzzleBone")?.GetValue(elite) : null);
            Check("a skinned player's drawn muzzle is the tracer's start (only for the gun drawn; the Dual Berettas' left muzzle for their left bone)",
                !before.Ok && got.Ok && got.At == right && !other.Ok && (dualLeft.Ok && dualLeft.At == leftMuzzle),
                $"before {before.Ok}, awp {got.Ok} {V(got.At)}, ak47 {other.Ok}, elite left {dualLeft.Ok} {V(dualLeft.At)}");
        }

        // Multiplayer: client input → server, through the real message code.
        var net = mod.GetType("Game.ScNet", true); var guns = mod.GetType("Game.ScNetGuns", true);
        var transportProperty = net.GetProperty("Transport", BindingFlags.Public | BindingFlags.Static);
        var transportType = mod.GetType("Game.IScNetTransport", true); var peerType = mod.GetType("Game.ScNetPeer", true);
        object Enum(string type, string name) => System.Enum.Parse(mod.GetType(type, true), name);
        var sendInput = guns.GetMethod("SendInput", BindingFlags.Public | BindingFlags.Static);
        var receiveInput = guns.GetMethod("ReceiveInput", BindingFlags.NonPublic | BindingFlags.Static);
        var remoteInput = guns.GetMethod("RemoteInput", BindingFlags.Public | BindingFlags.Static);
        object original = transportProperty.GetValue(null);
        try {
            var client = (FakeTransport)DispatchProxy.Create(transportType, typeof(FakeTransport));
            client.Role = Enum("Game.ScNetRole", "Client"); client.Handshake = Enum("Game.ScNetHandshake", "Accepted");
            client.Peers = Activator.CreateInstance(typeof(List<>).MakeGenericType(peerType));
            transportProperty.SetValue(null, client);
            var clientEye = new Vector3(3.5f, 70.62f, -8.5f);
            // (2026-10-02, mp-state-consistency: the input names the player whose selection it is for; mpb's had no player.)
            object[] inputArgs = [true, false, true, false, false, false, 0, new Ray3(clientEye + new Vector3(.3f, -.2f, 0), look), true, false];
            sendInput.Invoke(null, sendInput.GetParameters().Length == 11 ? [Player(1, clientEye, 0, 0, null), .. inputArgs] : inputArgs);
            int opInput = (ushort)guns.GetField("OpInput").GetValue(null);
            var payload = client.Sent.LastOrDefault(m => m.Op == opInput).Payload;
            Check("multiplayer: the client sends its input", payload is not null, $"{client.Sent.Count} message(s)");
            if (payload is not null) {
                var peer = Activator.CreateInstance(peerType); peerType.GetProperty("PlayerIndex").SetValue(peer, 1);
                var peers = Activator.CreateInstance(typeof(List<>).MakeGenericType(peerType)); ((System.Collections.IList)peers).Add(peer);
                var server = (FakeTransport)DispatchProxy.Create(transportType, typeof(FakeTransport));
                server.Role = Enum("Game.ScNetRole", "Host"); server.Handshake = Enum("Game.ScNetHandshake", "NotApplicable"); server.Peers = peers;
                server.Local = p => p.PlayerData?.PlayerIndex != 1;
                transportProperty.SetValue(null, server);
                // The server's copy of that player stands a little elsewhere (interpolation), looks another way and has no camera.
                var serverEye = clientEye + new Vector3(.4f, 0, .25f);
                var remote = Player(1, serverEye, -1.2f, .3f, null);
                var reader = Activator.CreateInstance(mod.GetType("Game.ScNetReader", true), [payload]);
                try { receiveInput.Invoke(null, [peer, remote, reader]); } catch (TargetInvocationException e) { throw e.InnerException; }
                var input = remoteInput.Invoke(null, [remote]);
                var aim = (Ray3)input.GetType().GetField("Aim").GetValue(input);
                Ray3 shot;
                try { shot = Resolve(remote, aim); } catch (Exception e) { Check("multiplayer: the server resolves the shot without a camera of its own", false, e.GetType().Name + ": " + e.Message); return results; }
                Check("multiplayer: the server starts the client's shot at its own copy of that player's eye, along the direction the client sent (not its copy's look)",
                    Vector3.Distance(aim.Position, serverEye) < 1e-4f && Vector3.Distance(shot.Position, serverEye) < 1e-4f && Vector3.Distance(shot.Direction, look) < 1e-4f,
                    $"aim {V(aim.Position)} shot {V(shot.Position)} expected {V(serverEye)} dir {V(shot.Direction)} sent {V(look)}");
            }
        }
        catch (Exception e) { Check("multiplayer fixture", false, e.GetType().Name + ": " + e.Message); }
        finally { transportProperty.SetValue(null, original); }
        return results;
    }
}
