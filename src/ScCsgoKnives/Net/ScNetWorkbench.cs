using Engine;
using GameEntitySystem;
namespace Game;

public enum ScWorkbenchOpKind : byte { Craft = 1, Repair, GunSkin, KnifeSkin, Counter, CreativeLevel, GrowthMode, MaterializeHotbar, Armor }

/// <summary>One workbench commit as a player chose it. <see cref="Expected"/>/<see cref="Revision"/>/<see cref="State"/> are
/// what the player's quote was made for; the server re-derives the quote from its own state and refuses when it differs.</summary>
public readonly record struct ScWorkbenchOp(ScWorkbenchOpKind Kind, Point3 Bench, int Value = 0, int Quantity = 1, int Slot = -1, int Expected = 0, int Revision = -1, int Arg = 0,
    string Target = "", string State = "");

/// <summary>Result of a workbench commit: <see cref="Code"/> is the ScGunResult (or 1/0 for a craft), <see cref="Detail"/> an
/// optional explanation from the server.</summary>
public readonly record struct ScWorkbenchResult(int Code, string Detail = "") {
    public const int Unknown = -2;
    public bool Ok(ScWorkbenchOpKind kind) => kind is ScWorkbenchOpKind.Craft or ScWorkbenchOpKind.KnifeSkin or ScWorkbenchOpKind.GrowthMode
        or ScWorkbenchOpKind.MaterializeHotbar or ScWorkbenchOpKind.Armor ? Code == 1 : Code == (int)ScGunResult.Success;
}

/// <summary>Workbench commits over the network (current-direction-20260929 §6, M2). The dialogs stay on the player's own
/// screen; each commit goes through <see cref="Run"/>: done at once in single player and on the host, sent to the server by
/// a remote client, which repeats the same quote on its own inventory and world and answers with the result.</summary>
public static class ScNetWorkbench {
    public const ushort OpRequest = 50, OpResult = 51, OpOpen = 52;
    static int s_next;
    /// <summary>The last answer this client got (read by the two-process tests).</summary>
    public static ScWorkbenchResult? LastResult;
    public const int MaxPending = 64;
    public const double RequestTimeout = 30;
    sealed record Pending(Project World, IScNetTransport Transport, double At, Action<ScWorkbenchResult> Done);
    static readonly Dictionary<int, Pending> s_pending = [];
    static readonly ScWorkbenchResult Uncertain = new(ScWorkbenchResult.Unknown, "联机：操作结果未知，请核对库存和状态后再操作；不会自动重试。");
    static void Finish(int id, ScWorkbenchResult result) {
        if (!s_pending.Remove(id, out var pending)) return;
        try { pending.Done(result); }
        catch (Exception e) { KnifeLog.Warning("workbench callback closed: " + e.Message); }
    }
    /// <summary>Client-only bookkeeping. An unanswered request is not proof that the server did not commit it.</summary>
    public static void Tick() {
        foreach (var (id, p) in s_pending.ToArray())
            if (!ReferenceEquals(p.World, GameManager.Project) || !ReferenceEquals(p.Transport, ScNet.Transport)
                || !ScNet.IsRemoteClient || ScNet.ClientBlocked || ScNet.Now < p.At || ScNet.Now - p.At >= RequestTimeout)
                Finish(id, Uncertain);
    }
    public static void WorldClosed(Project world) {
        foreach (var (id, p) in s_pending.ToArray()) if (ReferenceEquals(p.World, world)) Finish(id, Uncertain);
    }
    public static void SessionClosed(IScNetTransport transport) {
        foreach (var (id, p) in s_pending.ToArray()) if (ReferenceEquals(p.Transport, transport)) Finish(id, Uncertain);
    }
    /// <summary>Fallback for the engine's parameterless exit hook; a new world's pending requests are left alone.</summary>
    public static void ClearOrphaned() {
        foreach (var (id, p) in s_pending.ToArray())
            if (GameManager.Project is null || !ReferenceEquals(p.World, GameManager.Project)) Finish(id, Uncertain);
    }

    public static void Register() {
        ScNet.OnServer(OpRequest, Receive);
        ScNet.OnClient(OpResult, ReceiveResult);
        ScNet.OnClient(OpOpen, ReceiveOpen);
    }

    // ---- opening: a client never runs an interaction itself (1.9.3.2_MP only pokes and sends it; the server repeats it
    // for that player), so the server tells the client to open its own workbench dialog.
    /// <summary>Server: a remote client's player interacted with the workbench at <paramref name="hit"/>.</summary>
    public static void OpenOnClient(ComponentPlayer player, TerrainRaycastResult hit) {
        var face = hit.CellFace;
        ScNet.Trace($"workbench P{player.PlayerData.PlayerIndex} open at {face.X},{face.Y},{face.Z}");
        ScNet.SendTo(ScNet.PeerOf(player), OpOpen, w => w.Int(face.X).Int(face.Y).Int(face.Z).Int(face.Face).Ray(hit.Ray).Float(hit.Distance));
    }
    public static int OpensReceived;
    static void ReceiveOpen(ScNetReader r) {
        int x = r.Int(), y = r.Int(), z = r.Int(), face = r.Int(); Ray3 ray = r.Ray(); float distance = r.Float();
        OpensReceived++;
        var project = GameManager.Project;
        var player = project?.FindSubsystem<SubsystemPlayers>(false)?.ComponentPlayers.FirstOrDefault(ScNet.IsLocal);
        var bench = project?.FindSubsystem<SubsystemScWeaponWorkbench>(false);
        if (player is null || bench is null || face is < 0 or > 5) return;
        int value = project.FindSubsystem<SubsystemTerrain>(true).Terrain.GetCellValue(x, y, z);
        bench.OnInteract(new TerrainRaycastResult { CellFace = new CellFace(x, y, z, face), Value = value, Ray = ray, Distance = distance }, player.ComponentMiner);
    }

    /// <summary>Runs a commit: <paramref name="local"/> here (single player, MP host), or sent to the server by a remote
    /// client, <paramref name="done"/> then running when the answer arrives.</summary>
    public static void Run(ScWorkbenchOp op, Func<ScWorkbenchResult> local, Action<ScWorkbenchResult> done) {
        Tick();
        if (!ScNet.IsRemoteClient) { done(local()); return; }
        if (ScNet.ClientBlocked) { done(new(-1, ScNet.BlockedMessage)); return; }
        if (s_pending.Count >= MaxPending || s_next == int.MaxValue) { done(new(-1, "联机：待确认操作过多，本次请求未发送。")); return; }
        // Never reuse a request number while this process lives: a late answer cannot complete a new world's request.
        int id = ++s_next; s_pending[id] = new(GameManager.Project, ScNet.Transport, ScNet.Now, done);
        try { if (!ScNet.Send(OpRequest, w => Write(w.Int(id), op))) Finish(id, new(-1, "联机：请求没有发出，请重试。")); }
        catch (Exception e) { KnifeLog.Warning("workbench send outcome unknown: " + e.Message); Finish(id, Uncertain); }
    }

    static void Write(ScNetWriter w, ScWorkbenchOp op) =>
        w.Byte((byte)op.Kind).Int(op.Bench.X).Int(op.Bench.Y).Int(op.Bench.Z).Int(op.Value).Int(op.Quantity).Int(op.Slot).Int(op.Expected).Int(op.Revision).Int(op.Arg)
         .String(op.Target).String(op.State);
    static ScWorkbenchOp Read(ScNetReader r) => new((ScWorkbenchOpKind)r.Byte(), new Point3(r.Int(), r.Int(), r.Int()), r.Int(), r.Int(), r.Int(), r.Int(), r.Int(), r.Int(),
        r.String(128), r.String(128));

    static void Receive(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        int id = r.Int(); var op = Read(r); r.Finish();
        ScWorkbenchResult result;
        try { result = ScWorkbenchOps.Execute(player, op); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("scnet-workbench-" + op.Kind, $"[ScCsgoNet] workbench {op.Kind} for {from} failed: {e.Message}"); result = Uncertain; }
        KnifeLog.Diagnostic($"[ScCsgoNet] server: workbench {op.Kind} value {op.Value} slot {op.Slot} x{op.Quantity} for {from} -> {result.Code} {result.Detail}");
        ScNetMirror.Flush(); // the changed record/protection rows arrive before the answer
        ScNet.SendTo(from, OpResult, w => w.Int(id).Int(result.Code).String(result.Detail ?? ""));
    }

    static void ReceiveResult(ScNetReader r) {
        int id = r.Int(); var result = new ScWorkbenchResult(r.Int(), r.String(512));
        r.Finish(); Tick();
        if (!s_pending.ContainsKey(id)) return;
        LastResult = result;
        Finish(id, result);
    }
}
