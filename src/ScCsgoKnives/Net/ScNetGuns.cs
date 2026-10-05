using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>What a remote client's gun input is on the server. The server runs the same gun state machine
/// (SubsystemScGunBlockBehavior.UpdateGun) for that player that single player runs for the local one, fed from here
/// instead of devices: held buttons as states, presses as counted events (never lost between two frames), and the aim
/// ray the shot uses. Nothing the client computed is taken over: rounds, cadence, spread, hits and damage are the server's.</summary>
public sealed class ScRemoteGunInput {
    public bool Available, Context, Dig, Custom, Touch;
    /// <summary>The client's own footing: a remote player's body on the server is interpolated, not simulated, so it never
    /// stands on anything there; the gun's stance (accuracy) uses what the client reports.</summary>
    public bool Grounded = true, Jumping;
    public int Zoom;
    public Ray3 Aim = new(Vector3.Zero, -Vector3.UnitZ);
    public bool HasAim;
    int m_hits, m_reloads, m_secondaries, m_digPresses, m_customPresses;
    /// <summary>The ray each press was sent with, oldest first. A click is aimed where the player pointed when clicking:
    /// the release (whose aim is the camera again) or a refresh can arrive in the same server frame, before that frame
    /// takes the press, and <see cref="Aim"/> is then already the later ray (mp15 M2: a single-tap head shot at 7 m went
    /// along the camera instead).</summary>
    readonly Queue<Ray3> m_pressAims = new();
    /// <summary>When the last message was taken (ScNet.Now); a held trigger is only valid for <see cref="ScNetGuns.InputLease"/> after it.</summary>
    public double ReceivedAt;
    // ---- mp-state-consistency-20261002 (W3/W4): whose gun this input is for, and what was already taken
    /// <summary>The client's selection sequence, slot and item value of the last input the server accepted (-1: none yet).</summary>
    public int Selection = -1, Slot = -1, Value;
    /// <summary>The sequence number of the last message taken; an equal or older one is a repeat and changes nothing.</summary>
    public int Sequence; public bool Sequenced;
    /// <summary>The server's account of this client's shots under <see cref="Selection"/>, in the client's own numbering:
    /// <see cref="ClientShots"/> shots the client says it has shown, of which the server committed <see cref="Fired"/> and
    /// will never fire <see cref="Skipped"/>. Sent to that client with the record rows whenever it changes.</summary>
    public int Fired, Skipped, ClientShots; public bool AckDue;
    /// <summary>Shots that client has shown and the server has neither executed nor said it will not (mpd2 ammo jitter,
    /// 2026-10-02): the server executes a remote client's shot when it is owed, not by its own reading of the trigger.</summary>
    public int Owed => ClientShots - Fired - Skipped;
    /// <summary>When the newest of those shots was reported (ScNet.Now) and the aim it was reported with: the shot is
    /// executed along that ray, and for <see cref="ScNetGuns.ShotLead"/> after that moment even if the trigger is already up.</summary>
    public double ShotsAt = double.NegativeInfinity; public Ray3 ShotAim = new(Vector3.Zero, -Vector3.UnitZ);
    /// <summary>The reload the client last asked for (0: none), and one that was dropped unanswered (a selection the server
    /// does not hold, an expired lease): the client is told it was refused.</summary>
    public int ReloadId, DroppedReloadId;
    /// <summary>Since when the client's inputs name a selection the server does not hold (-1: they agree), and when its
    /// inventory was last sent again because of that.</summary>
    public double StaleSince = -1, CorrectedAt = double.NegativeInfinity;
    /// <summary>Diagnostics: inputs dropped as stale, held states that expired, repeated messages ignored, times the
    /// server took over the slot the client holds.</summary>
    public int Stale, Expired, Repeats, SlotsAdopted;
    public bool TakeHit() { if (m_hits <= 0) return false; m_hits--; return true; }
    public bool TakeReload() { if (m_reloads <= 0) return false; m_reloads--; return true; }
    public bool TakeSecondary() { if (m_secondaries <= 0) return false; m_secondaries--; return true; }
    /// <summary>A trigger press (dig or custom fire) the client made, even if its release arrived in the same server frame.</summary>
    public bool TakeDigPress() { if (m_digPresses <= 0) return false; m_digPresses--; return true; }
    public bool TakeCustomPress() { if (m_customPresses <= 0) return false; m_customPresses--; return true; }
    /// <summary>The aim of the press taken this frame (the oldest queued); null when none is queued. Once no press is left
    /// the queue is emptied, so an aim never outlives its press.</summary>
    public Ray3? TakePressAim() {
        Ray3? aim = m_pressAims.Count > 0 ? m_pressAims.Dequeue() : null;
        if (m_hits <= 0 && m_digPresses <= 0 && m_customPresses <= 0) m_pressAims.Clear();
        return aim;
    }
    internal void Add(bool hit, bool reload, bool secondary, bool digPress, bool customPress, Ray3 aim, int reloadId = 0) {
        if (reload && reloadId != 0) ReloadId = reloadId;
        if (hit || digPress || customPress) { if (m_pressAims.Count >= 4) m_pressAims.Dequeue(); m_pressAims.Enqueue(aim); }
        // Bounded: a client cannot bank presses for later.
        if (digPress) m_digPresses = Math.Min(m_digPresses + 1, 2);
        if (customPress) m_customPresses = Math.Min(m_customPresses + 1, 2);
        if (hit) m_hits = Math.Min(m_hits + 1, 4);
        if (reload) m_reloads = Math.Min(m_reloads + 1, 2);
        if (secondary) m_secondaries = Math.Min(m_secondaries + 1, 2);
    }
    bool HasWeaponInput => Dig || Custom || m_hits + m_reloads + m_secondaries + m_digPresses + m_customPresses > 0;
    /// <summary>Lets go of the trigger and forgets every press not taken yet (a selection the server does not hold, an
    /// expired lease). Shots already committed stay; nothing is fired later in their place.</summary>
    public void DropWeaponInput() {
        if (m_reloads > 0 && ReloadId != 0) DroppedReloadId = ReloadId;
        Dig = Custom = false; m_hits = m_reloads = m_secondaries = m_digPresses = m_customPresses = 0; m_pressAims.Clear();
    }
    /// <summary>A press (not the held trigger) is still waiting to be taken.</summary>
    public bool HasPresses => m_hits + m_digPresses + m_customPresses > 0;
    /// <summary>The held trigger and untaken presses are valid for <see cref="ScNetGuns.InputLease"/> after the last message:
    /// a client that stops sending (stalled, suspended, a dead link the platform has not noticed yet) stops firing here.
    /// True when something was let go.</summary>
    public bool Expire(double now) {
        if (!HasWeaponInput || now - ReceivedAt <= ScNetGuns.InputLease) return false;
        DropWeaponInput(); Expired++; return true;
    }
    public void Reset() { Available = Context = false; DropWeaponInput(); Selection = Slot = -1; Value = 0; Sequenced = false; Fired = Skipped = ClientShots = 0; AckDue = false; ShotsAt = double.NegativeInfinity; ReloadId = DroppedReloadId = 0; StaleSince = -1; CorrectedAt = double.NegativeInfinity; }
}

/// <summary>Guns over the network (current-direction-20260929 §6, M1/M2).
/// Client: sends its gun input; plays its own shot (animation, sound, recoil, tracer) as a prediction without touching
/// any record; the ammo HUD counts predicted shots until the server's rows confirm them.
/// Server: keeps each remote player's input for UpdateGun; tells the other clients about every shot so they see and hear it.</summary>
public static class ScNetGuns {
    /// <summary>37: the server's word on a reload. (mpc3 gave the shot confirmation op 42, the hit feedback's number: every
    /// confirmation was read as a hit. The confirmation now travels inside the record message, ScNetMirror.OpRecords, so
    /// rounds and their confirmation can never arrive apart.)</summary>
    public const ushort OpInput = 40, OpShot = 41, OpReload = 37, OpKnife = 46;
    [Flags] enum InputFlags : ushort { Available = 1, Dig = 2, Custom = 4, Touch = 8, Hit = 16, Reload = 32, Secondary = 64, Context = 128, DigPress = 256, CustomPress = 512, Grounded = 1024, Jumping = 2048 }

    /// <summary>How long a held trigger (and presses not taken yet) stay valid without a new message. A client refreshes
    /// its input every <see cref="InputRefresh"/> s while nothing changes (every frame while firing with a moving aim), so
    /// the lease tolerates three refreshes in a row arriving late or stalled behind a resend on the ordered channel, and
    /// still stops an automatic gun within one second of the client going silent.</summary>
    public const double InputLease = 1.0, InputRefresh = .25;
    /// <summary>A selection the server does not hold is first only ignored (a slot change of the server's still on its way
    /// to the client); when the client keeps naming it this long, the server sends it that inventory again, at most every
    /// <see cref="StaleCorrectEvery"/> s.</summary>
    public const double StaleCorrectAfter = .5, StaleCorrectEvery = 2;
    /// <summary>For this long after the server rewrote a slot (a template got its record, a copy was separated) an input
    /// naming the former value of that slot is the same selection: the client has not seen the new value yet.</summary>
    public const double RewriteWindow = 5;
    /// <summary>A shown shot is settled by the server, by number: fired, or skipped (it will not fire it). Only when neither
    /// word arrives for this long is it given up locally, with a warning: that is a fault, not the normal path, and a
    /// late word for a given-up shot can no longer touch a newer one.</summary>
    public const double PredictionGiveUp = 5.0;
    /// <summary>How long the server keeps a client's reload request it cannot start yet (the gun still busy there).</summary>
    public const double ReloadRequestLease = 1.0;
    /// <summary>mpd2 ammo jitter (2026-10-02). The two ends used to time a held trigger's shots each by its own frames, and a
    /// release decided which of them had "one more": the client showed a shot, the server had not got to it when the
    /// release arrived, marked it skipped, and the readout rose by one. The server now executes a remote client's shot
    /// when that client reports having shown it, and checks it against its own schedule for that gun instead of producing
    /// the schedule itself: a reported shot may run this far ahead of, or behind, the time the server's schedule has for
    /// it (frame rounding on either end, a link whose delay varies, reports arriving several to a frame). Over any run of
    /// shots the cadence still holds - n shots take at least n-1 intervals less this allowance, once, not per shot - and a
    /// report that is further ahead waits for its time while the trigger is down, or is refused once it is up.
    /// It is also how long after its report a shown shot is still executed when the trigger is already up.</summary>
    public const double ShotLead = .125;

    static readonly ConditionalWeakTable<ComponentPlayer, ScRemoteGunInput> s_remote = new();

    public static void Register() {
        ScNet.OnServer(OpInput, ReceiveInput);
        ScNet.OnServer(OpKnife, ReceiveKnife);
        ScNet.OnClient(OpShot, ReceiveShot);
        ScNet.OnClient(OpReload, ReceiveReload);
        ScNet.PeerLeft += peer => { if (peer.Player(GameManager.Project) is { } p && s_remote.TryGetValue(p, out var input)) input.Reset(); };
        // A connection accepted (again) starts from nothing: no sequence, selection or held trigger of an earlier session.
        ScNet.PeerAccepted += peer => { if (peer.Player(GameManager.Project) is { } p && s_remote.TryGetValue(p, out var input)) input.Reset(); };
        ScNet.ClientAccepted += ResetClient;
    }

    /// <summary>The world closed (either role): nothing of it may act on the next one, whose records reuse the same numbers.</summary>
    public static void WorldClosed() { ResetClient(); s_rewrites.Clear(); }

    // ---------------------------------------------------------------- server
    /// <summary>Server: the replicated input of a remote client's player; null for players this process reads itself.</summary>
    public static ScRemoteGunInput RemoteInput(ComponentPlayer player) => ScNet.IsRemoteDriven(player) ? s_remote.GetOrCreateValue(player) : null;

    static int Item(int value) => Terrain.ReplaceLight(value, 0);
    static bool IsGunItem(int value) {
        int contents = Terrain.ExtractContents(value);
        return contents != 0 && (contents == BlocksManager.GetBlockIndex<ScGunBlock>(false) || ScGunSkinTemplateBlock.IsTemplate(value) || ScGunCounterTemplateBlock.IsTemplate(value));
    }

    // ---- slots the server rewrote lately (newest last): the one legitimate way a client's selection can name a value the
    // server no longer holds in that slot.
    readonly record struct Rewrite(object Storage, int Slot, int Old, int New, double At);
    static readonly List<Rewrite> s_rewrites = [];
    /// <summary>Server: a committed transaction replaced this slot's item (a fresh gun or template got its record, a
    /// duplicate was separated).</summary>
    public static void SlotRewritten(IInventory inventory, int slot, int oldValue, int newValue) {
        if (!ScNet.IsHost || inventory is null || Item(oldValue) == Item(newValue)) return;
        if (s_rewrites.Count >= 16) s_rewrites.RemoveAt(0);
        s_rewrites.Add(new(ScInventoryIdentity.Storage(inventory), slot, Item(oldValue), Item(newValue), ScNet.Now));
    }
    static bool Rewritten(IInventory inventory, int slot, int claimed, int held) {
        object storage = ScInventoryIdentity.Storage(inventory);
        double now = ScNet.Now; int target = held;
        for (int i = s_rewrites.Count - 1; i >= 0; i--) {
            var w = s_rewrites[i];
            if (now - w.At > RewriteWindow) break;
            if (!ReferenceEquals(w.Storage, storage) || w.Slot != slot || w.New != target) continue;
            if (w.Old == claimed) return true;
            target = w.Old;
        }
        return false;
    }
    /// <summary>Whether the selection a client's input names (slot, item) is what the server holds for that player now, or
    /// what the server itself turned into it a moment ago.</summary>
    static bool Selected(ComponentPlayer player, int slot, int value, out int held) {
        IInventory inventory = player.ComponentMiner?.Inventory;
        held = inventory is null ? 0 : Item(player.ComponentMiner.ActiveBlockValue);
        if ((inventory?.ActiveSlotIndex ?? -1) != slot) return false;
        return held == value || inventory is not null && Rewritten(inventory, slot, value, held);
    }

    /// <summary>Server: whether that player still holds the gun (slot and item) its counted input is for.</summary>
    internal static bool Holds(ComponentPlayer player, ScRemoteGunInput input) => input.Selection != -1 && Selected(player, input.Slot, input.Value, out _);

    static void ReceiveInput(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        var flags = (InputFlags)(ushort)r.Int();
        int zoom = r.Byte();
        Ray3 aim = r.Ray();
        int sequence = r.Int(), selection = r.Int(), slot = r.Byte(), value = Item(r.Int());
        int shots = r.Int(), reloadId = r.Int();
        r.Finish();
        if (shots < 0 || shots > 1 << 24) throw new System.IO.InvalidDataException($"shot count {shots}");
        if (slot == 255) slot = -1;
        var input = s_remote.GetOrCreateValue(player);
        // A message that is not newer than the last one taken (a repeat, a replay) changes nothing: no press is counted twice.
        if (input.Sequenced && sequence - input.Sequence <= 0) { input.Repeats++; return; }
        input.Sequence = sequence; input.Sequenced = true;
        double now = ScNet.Now;
        input.Available = flags.HasFlag(InputFlags.Available);
        input.Context = flags.HasFlag(InputFlags.Context);
        input.Grounded = flags.HasFlag(InputFlags.Grounded);
        input.Jumping = flags.HasFlag(InputFlags.Jumping);
        input.Touch = flags.HasFlag(InputFlags.Touch);
        // The ray starts at the player's own eye: a client may aim anywhere, but never shoot from somewhere else.
        input.Aim = FromEye(player, aim);
        input.HasAim = true;
        input.ReceivedAt = now;
        if (!Selected(player, slot, value, out int held)) {
            // The client pressed for an item the server does not hold in that slot (it switched, or a slot change of the
            // server's has not reached it): nothing of it is applied to what the server holds instead, and the trigger is
            // let go. The next input that names the server's selection plays on.
            input.DropWeaponInput(); input.Stale++;
            // (A reload asked for with it is answered: refused. The client must not show a reload nobody performs.)
            if (flags.HasFlag(InputFlags.Reload) && reloadId != 0) input.DroppedReloadId = reloadId;
            RefuseDroppedReload(player, input, selection);
            ScNet.Trace($"input P{player.PlayerData?.PlayerIndex} stale: slot {slot} item {value} named, server holds slot {player.ComponentMiner?.Inventory?.ActiveSlotIndex ?? -1} item {held}");
            if (input.StaleSince < 0) input.StaleSince = now;
            else if (now - input.StaleSince >= StaleCorrectAfter) {
                IInventory inventory = player.ComponentMiner?.Inventory;
                if (inventory is not null && slot >= 0 && slot != inventory.ActiveSlotIndex) {
                    // Which slot is in hand is the client's own choice (the platform's active-slot message sets the server's
                    // copy without question). The client has named another slot for this long: the server's copy missed a
                    // change (one made here for that player, or a lost one). It follows the client; the others are told.
                    inventory.ActiveSlotIndex = slot;
                    if (inventory.ActiveSlotIndex == slot) {
                        input.SlotsAdopted++; input.StaleSince = now;
                        bool told = ScNetSlots.ActiveSlotAdopted(inventory, from);
                        ScNet.Trace($"input P{player.PlayerData?.PlayerIndex}: the client holds slot {slot}; the server's active slot follows it (others told: {told})");
                    }
                }
                else if (now - input.CorrectedAt >= StaleCorrectEvery && (IsGunItem(value) || IsGunItem(held))) {
                    input.CorrectedAt = now;
                    bool corrected = ScNetSlots.Correct(inventory, from);
                    ScNet.Trace($"input P{player.PlayerData?.PlayerIndex} stale for {now - input.StaleSince:0.0} s: inventory sent again ({corrected})");
                }
            }
            return;
        }
        input.StaleSince = -1;
        if (selection != input.Selection || slot != input.Slot) {
            // Another gun (or the same one taken out again): what was held or queued for the former one does not carry over.
            if (input.Selection != -1) { input.DropWeaponInput(); RefuseDroppedReload(player, input, input.Selection); }
            input.Selection = selection; input.Fired = input.Skipped = input.ClientShots = 0; input.AckDue = false;
        }
        // The client's own count of the shots it has shown for this selection (never lower than what it said before),
        // when the newest was reported and where it was aimed.
        if (shots > input.ClientShots) { input.ClientShots = shots; input.ShotsAt = now; input.ShotAim = input.Aim; }
        input.Slot = slot; input.Value = held;
        input.Dig = flags.HasFlag(InputFlags.Dig);
        input.Custom = flags.HasFlag(InputFlags.Custom);
        input.Zoom = Math.Clamp(zoom, 0, 3);
        input.Add(flags.HasFlag(InputFlags.Hit), flags.HasFlag(InputFlags.Reload), flags.HasFlag(InputFlags.Secondary), flags.HasFlag(InputFlags.DigPress), flags.HasFlag(InputFlags.CustomPress), input.Aim, reloadId);
    }

    // ---- shot confirmation (W3/W7). Client and server run the same gun on the same input, a trip apart. The client numbers
    // the shots it shows for a selection (1, 2, 3 ...) and tells the server its count with its input. The server answers
    // in the same numbering: how many it has committed (Fired) and how many of the client's it will never fire (Skipped:
    // the trigger was already up there, the gun was reloading or empty). Shot n is settled once Fired + Skipped >= n.
    // Nothing is settled by time, so a late answer can only settle the shots it is about, never a newer one; and the
    // answer travels in the same message as the record rows (ScNetMirror), so a client never reads the rounds of a shot
    // without its confirmation or the other way round.
    /// <summary>Server: a shot was committed for this remote client's player.</summary>
    public static void ServerShot(ComponentPlayer player) {
        if (RemoteInput(player) is not { } input) return;
        input.Fired++; input.AckDue = true;
    }
    /// <summary>Server, after each gun update of a remote client's player (and while it holds no usable gun):
    /// <paramref name="willFire"/> says whether the server may still execute shots that client has already shown (rounds
    /// left, not reloading, and either the trigger still down or the report no older than <see cref="ShotLead"/>). When it
    /// will not, they are skipped: the client stops counting them at once instead of waiting.</summary>
    public static void ServerSettle(ComponentPlayer player, bool willFire) {
        if (RemoteInput(player) is not { } input || willFire || input.Fired + input.Skipped >= input.ClientShots) return;
        input.Skipped = input.ClientShots - input.Fired; input.AckDue = true;
    }
    /// <summary>What a client is told with its record rows: its selection, the item the server holds for it, and the count.</summary>
    internal readonly record struct Ack(int Selection, int Value, int Fired, int Skipped);
    internal static bool TryAck(ScNetPeer peer, out Ack ack) {
        ack = default;
        if (peer?.Player(GameManager.Project) is not { } player || !s_remote.TryGetValue(player, out var input) || !input.AckDue || input.Selection == -1) return false;
        ack = new(input.Selection, Item(player.ComponentMiner?.ActiveBlockValue ?? 0), input.Fired, input.Skipped);
        return true;
    }
    internal static void AckSent(ScNetPeer peer) { if (peer?.Player(GameManager.Project) is { } player && s_remote.TryGetValue(player, out var input)) input.AckDue = false; }

    // ---- reloads (W7). A client shows its reload at once and asks the server for it (a counted event with an id, in its
    // input); the server performs the reload and says what became of it. A reload the server does not perform is taken
    // back on the client; one the server starts by itself (the magazine ran dry there) is shown on the client.
    public enum ReloadPhase : byte { Accepted = 1, Refused = 2, Completed = 3, Cancelled = 4 }
    /// <summary>Server: what became of a remote client's reload (<paramref name="id"/> 0: one the server started itself).</summary>
    public static void ReloadResult(ComponentPlayer player, int id, ReloadPhase phase) {
        if (RemoteInput(player) is not { } input || ScNet.PeerOf(player) is not { } peer) return;
        int selection = input.Selection, rounds = GunSpec.GetRounds(Terrain.ExtractData(player.ComponentMiner?.ActiveBlockValue ?? 0));
        ScNet.Trace($"reload P{player.PlayerData?.PlayerIndex} #{id}: {phase} at {rounds} rounds");
        ScNet.SendTo(peer, OpReload, w => w.Int(selection).Int(id).Byte((byte)phase).Int(rounds));
    }
    static void RefuseDroppedReload(ComponentPlayer player, ScRemoteGunInput input, int selection) {
        if (input.DroppedReloadId == 0) return;
        int id = input.DroppedReloadId; input.DroppedReloadId = 0;
        if (ScNet.PeerOf(player) is { } peer) ScNet.SendTo(peer, OpReload, w => w.Int(selection).Int(id).Byte((byte)ReloadPhase.Refused).Int(0));
    }
    /// <summary>Server, each gun update: a reload request that was dropped unanswered (an expired lease) is refused.</summary>
    public static void ServerRefuseDropped(ComponentPlayer player) { if (RemoteInput(player) is { DroppedReloadId: not 0 } input) RefuseDroppedReload(player, input, input.Selection); }

    /// <summary>On by default; the two-process tests switch it off once to show what it changes.</summary>
    public static bool Compensate = true;
    /// <summary>Test diagnostics: every compensated body's rewound positions go to <see cref="ScNet.Trace"/>.</summary>
    public static bool DebugRewind;
    /// <summary>Server: for a remote client's shot, how far each body may have moved since that client drew it (lag
    /// compensation for hit-scan shots over the same window the engine compensates its own projectiles, plus the body's
    /// present place); null for a shooter read here.</summary>
    public static Func<ComponentBody, IReadOnlyList<Vector3>> RewindFor(ComponentPlayer shooter) {
        if (!Compensate || !ScNet.IsRemoteDriven(shooter) || ScNet.Transport is not IScNetRewind rewind) return null;
        var seen = new Vector3[RewindSamples];
        return body => {
            int n = body is null ? 0 : rewind.TryRewound(body, shooter, seen);
            if (DebugRewind && body is not null && n > 0)
                ScNet.Trace($"rewind body {body.Entity?.Id} now {body.Position.X:0.00},{body.Position.Y:0.00},{body.Position.Z:0.00} seen {string.Join(" ", seen.Take(n).Select(v => $"{v.X:0.00},{v.Y:0.00},{v.Z:0.00}"))} {(rewind is IScNetRewindInfo info ? info.Describe(shooter) : "")}");
            if (n <= 0) return s_unmoved;
            // Where the body is now is always tested too: a body that jostles between the snapshots a client interpolates
            // (pushed, stacked) can fall between the rewound samples, and compensation must never turn a hit into a miss.
            var moved = new List<Vector3>(n + 1) { Vector3.Zero };
            for (int i = 0; i < Math.Min(n, seen.Length); i++) {
                Vector3 d = body.Position - seen[i];
                if (d.LengthSquared() <= ScGunHitTest.MaxMoved * ScGunHitTest.MaxMoved) moved.Add(d); // a teleport is not a movement to follow
            }
            return moved;
        };
    }
    /// <summary>Samples across the window a remote client draws other bodies in (the engine's own projectile compensation
    /// uses the same count).</summary>
    public const int RewindSamples = 4;
    static readonly Vector3[] s_unmoved = [Vector3.Zero];

    /// <summary>The server's own origin for a client's aim: its copy of that player's eye (ScAimRay) with the client's
    /// direction. A
    /// client may aim anywhere but never shoots from anywhere else, and every trace on the server runs from here, so a
    /// wall between the character and what its camera saw stops the shot here too (post-mp-bugs-20260930 review C: the
    /// former 2.5 m allowance kept a client's origin without any occlusion check).</summary>
    static Ray3 FromEye(ComponentPlayer player, Ray3 aim) {
        Vector3 origin = ScAimRay.Eye(player, player.ComponentBody.Position);
        Vector3 d = aim.Direction; float length = d.Length();
        return new Ray3(origin, float.IsFinite(length) && length > 1e-6f ? d / length : -Vector3.UnitZ);
    }

    /// <summary>Server: a remote client's knife swing; the server runs the same strike for that player from its aim.</summary>
    static void ReceiveKnife(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        bool heavy = r.Bool(), available = r.Bool(); Ray3 aim = r.Ray();
        var input = s_remote.GetOrCreateValue(player);
        input.Available = available; input.Aim = FromEye(player, aim); input.HasAim = true;
        GameManager.Project?.FindSubsystem<SubsystemScKnifeBlockBehavior>(false)?.RequestAttack(player, heavy);
    }
    /// <summary>Client: this client's knife swing (it shows the swing itself; the server strikes).</summary>
    public static bool SendKnife(bool heavy, bool available, Ray3 aim) =>
        ScNet.IsRemoteClient && ScNet.Send(OpKnife, w => w.Bool(heavy).Bool(available).Ray(aim));

    // ---------------------------------------------------------------- client: input
    static InputFlags s_lastFlags;
    static int s_lastZoom = -1;
    static Ray3 s_lastAim;
    static double s_lastSentAt;
    static bool s_pendingSecondary, s_pendingReload;
    static int s_inputSequence, s_sentShots, s_reloadId, s_reloadPendingId;
    // ---- what this client holds, as it tells the server with every input: a selection is one item taken into one slot.
    static object s_selInventory; static int s_selSlot = -1, s_selValue, s_selection, s_sentSelection = -1, s_sentSlot = -1, s_sentValue;
    /// <summary>This client's selection sequence (test diagnostics).</summary>
    public static int LocalSelection => s_selection;

    /// <summary>Client, every frame for its own player: the slot and item in hand. Another slot, another inventory or
    /// another item is a new selection, and everything predicted or pressed for the former one ends with it. A fresh gun
    /// getting its record from the server (the same slot's value changes from the template to the instance) is the same
    /// selection, as it is for the gun's own state (ScHeldWeaponSelection).</summary>
    static void Select(ComponentPlayer player, IInventory inventory, int slot, int value) {
        value = Item(value);
        object storage = inventory is null ? null : ScInventoryIdentity.Storage(inventory);
        if (ReferenceEquals(storage, s_selInventory) && slot == s_selSlot && value == s_selValue) { EnsurePrediction(player); return; }
        bool allocated = ReferenceEquals(storage, s_selInventory) && slot == s_selSlot && Terrain.ExtractContents(value) == Terrain.ExtractContents(s_selValue)
            && Terrain.ExtractContents(value) == BlocksManager.GetBlockIndex<ScGunBlock>(false)
            && GunSpec.IsFresh(Terrain.ExtractData(s_selValue)) && !GunSpec.IsFresh(Terrain.ExtractData(value))
            && GunSpec.GetVariant(Terrain.ExtractData(s_selValue)) == GunSpec.GetVariant(Terrain.ExtractData(value));
        s_selInventory = storage; s_selSlot = slot; s_selValue = value;
        if (allocated) {
            // The same gun, now with its record: a confirmation that named the new value while the slot still held the fresh
            // gun is applied here, together with the slot.
            EnsurePrediction(player);
            if (s_deferredAck is { } ack && ack.Selection == s_selection && ack.Value == value) { s_deferredAck = null; ApplyAck(ack.Selection, ack.Value, ack.Fired, ack.Skipped); }
            return;
        }
        s_selection++; s_prediction = null; s_deferredAck = null; s_pendingReload = s_pendingSecondary = false; s_reloadPendingId = 0;
        EnsurePrediction(player);
    }
    /// <summary>Client, at the start of its own player's gun update: the slot and item in hand are observed before anything
    /// reads the rounds to show, so a confirmation that waited for the slot's new value (a fresh gun's record) is counted
    /// in the same frame as the slot.</summary>
    public static void Observe(ComponentPlayer player) {
        if (!ScNet.IsRemoteClient || ScNet.ClientBlocked || player is null) return;
        IInventory inventory = player.ComponentMiner?.Inventory;
        Select(player, inventory, inventory?.ActiveSlotIndex ?? -1, inventory is null ? 0 : player.ComponentMiner.ActiveBlockValue);
    }
    static void EnsurePrediction(ComponentPlayer player) {
        if (player is null || ScGunRegistry.Current is null || Current(s_prediction, player)) return;
        s_prediction = new Prediction { Registry = ScGunRegistry.Current, Player = player, Selection = s_selection };
    }

    /// <summary>Client: the local player's gun input this frame (held states and this frame's presses), with the selection
    /// it is for.</summary>
    /// <param name="context">The client's own window, menus and dialogs allow play (ScGunBindings.ContextAvailable), apart from
    /// any action reservation that <paramref name="available"/> also counts.</param>
    /// <param name="bursting">Rounds of a press are still being fired after the button went up (a burst): the server fires
    /// them along the latest aim, so it follows every frame as while the trigger is held.</param>
    public static void SendInput(ComponentPlayer player, bool available, bool dig, bool hit, bool custom, bool reload, bool touch, int zoom, Ray3 aim, bool context, bool bursting) {
        if (!ScNet.IsRemoteClient || ScNet.ClientBlocked) return;
        IInventory inventory = player?.ComponentMiner?.Inventory;
        int slot = inventory?.ActiveSlotIndex ?? -1;
        Select(player, inventory, slot, inventory is null ? 0 : player.ComponentMiner.ActiveBlockValue);
        int shots = Current(s_prediction, player) ? s_prediction.Shots : 0;
        var flags = (available ? InputFlags.Available : 0) | (context ? InputFlags.Context : 0) | (s_grounded ? InputFlags.Grounded : 0) | (s_jumping ? InputFlags.Jumping : 0) | (dig ? InputFlags.Dig : 0) | (custom ? InputFlags.Custom : 0) | (touch ? InputFlags.Touch : 0);
        bool events = hit || reload || s_pendingReload || s_pendingSecondary;
        bool firing = dig || custom || bursting;
        bool aimMoved = Vector3.Dot(aim.Direction, s_lastAim.Direction) < .99998f || Vector3.DistanceSquared(aim.Position, s_lastAim.Position) > .0025f;
        bool selected = s_selection != s_sentSelection || s_selSlot != s_sentSlot || s_selValue != s_sentValue;
        // States go when they change, presses at once, and while the trigger is held the aim follows every frame;
        // otherwise a slow refresh keeps the server's aim current for the next press and renews the held trigger's lease.
        // (The count of shots shown goes out as soon as it changes: the server settles shots by that number.)
        if (!events && !selected && shots == s_sentShots && flags == s_lastFlags && zoom == s_lastZoom && !(firing && aimMoved) && ScNet.Now - s_lastSentAt < InputRefresh) return;
        // A trigger going down is also sent as a counted press, so a tap shorter than a server frame still fires. A trigger
        // still down from the former selection is a new press for this one.
        bool digPress = dig && (!s_lastFlags.HasFlag(InputFlags.Dig) || s_selection != s_sentSelection), customPress = custom && (!s_lastFlags.HasFlag(InputFlags.Custom) || s_selection != s_sentSelection);
        var send = flags | (hit ? InputFlags.Hit : 0) | (reload || s_pendingReload ? InputFlags.Reload : 0) | (s_pendingSecondary ? InputFlags.Secondary : 0)
            | (digPress ? InputFlags.DigPress : 0) | (customPress ? InputFlags.CustomPress : 0);
        int sequence = s_inputSequence + 1, selection = s_selection, value = s_selValue, reloadId = send.HasFlag(InputFlags.Reload) ? s_reloadPendingId : 0;
        if (ScNet.Send(OpInput, w => w.Int((ushort)send).Byte((byte)Math.Clamp(zoom, 0, 255)).Ray(aim).Int(sequence).Int(selection).Byte((byte)(slot is >= 0 and < 255 ? slot : 255)).Int(value).Int(shots).Int(reloadId))) {
            s_inputSequence = sequence;
            s_lastFlags = flags; s_lastZoom = zoom; s_lastAim = aim; s_lastSentAt = ScNet.Now;
            s_sentSelection = selection; s_sentSlot = s_selSlot; s_sentValue = value; s_sentShots = shots;
            s_pendingReload = s_pendingSecondary = false;
        }
    }
    static bool s_grounded = true, s_jumping;
    /// <summary>Client, each frame: this player's footing, sent with its gun input (when it changes).</summary>
    public static void LocalStance(bool grounded, bool jumping) { s_grounded = grounded; s_jumping = jumping; }
    /// <summary>Client: a secondary action (scope, burst, silencer, R8 fan) or a touch reload, sent with the next input.</summary>
    public static void QueueSecondary() { if (ScNet.IsRemoteClient) s_pendingSecondary = true; }
    /// <summary>Client: this client started showing a reload (the key, or a trigger on an empty magazine): the server is
    /// asked to perform it, with the next input. Returns the request's id (0 outside a client).</summary>
    public static int RequestReload() {
        if (!ScNet.IsRemoteClient || ScNet.ClientBlocked) return 0;
        s_pendingReload = true;
        return s_reloadPendingId = ++s_reloadId;
    }

    // ---------------------------------------------------------------- client: predicted rounds
    // mp-state-consistency-20261002 (W3/W7). The mirror (ScGunRegistry) only ever holds what the server sent. What this
    // client shows before the server confirms it is kept here, apart from it, for its present selection in this world:
    // how many shots it has shown (Shots) and up to which of them the server has settled (Resolved = fired + skipped, in
    // the same numbering). The shots in between are pending and come off the rounds shown. Nothing here is ever written
    // into a record. (mpb subtracted the prediction inside the mirror row and wrote remembered rounds back by record
    // number alone; mpc3 settled by elapsed time and by a count without numbering, and its confirmations never arrived.)
    sealed class Prediction {
        public object Registry; public ComponentPlayer Player; public int Selection;
        /// <summary>Shots shown under this selection; follows the server's count when that runs ahead.</summary>
        public int Shots;
        /// <summary>The server's word: shots 1..Resolved are settled (fired or skipped).</summary>
        public int Resolved;
        /// <summary>Shots 1..Floor are no longer waited for locally (the gun was put away, or the server never answered).</summary>
        public int Floor;
        /// <summary>When each pending shot was shown, oldest first.</summary>
        public readonly Queue<double> Times = new();
        public int Pending => Math.Max(0, Shots - Math.Max(Resolved, Floor));
        public void Trim() { while (Times.Count > Pending) Times.Dequeue(); }
    }
    static Prediction s_prediction;
    static (int Selection, int Value, int Fired, int Skipped)? s_deferredAck;
    /// <summary>Predicted shots given up without the server's word (test diagnostics; a fault when it happens in play).</summary>
    public static int GiveUps;
    static bool Current(Prediction p, ComponentPlayer player) =>
        p is not null && ReferenceEquals(p.Registry, ScGunRegistry.Current) && p.Selection == s_selection && ReferenceEquals(p.Player, player);

    /// <summary>Client: a shot this client shows before the server confirms it.</summary>
    public static void PredictShot(ComponentPlayer player) {
        if (!ScNet.IsRemoteClient || player is null || ScGunRegistry.Current is null) return;
        EnsurePrediction(player);
        s_prediction.Shots++; s_prediction.Times.Enqueue(ScNet.Now); s_prediction.Trim();
    }
    /// <summary>Client: shots shown for the gun this player holds now that the server has not settled yet (0 for any other
    /// player, selection, world or session).</summary>
    public static int PendingShots(ComponentPlayer player) => ScNet.IsRemoteClient && Current(s_prediction, player) ? s_prediction.Pending : 0;
    /// <summary>Client: the rounds to show and to predict with for the gun in hand: the server's, less the shots shown but
    /// not settled. Everywhere else (and for every other gun) the server's value is the value.</summary>
    public static int ShownRounds(ComponentPlayer player, int serverRounds) => Math.Max(0, serverRounds - PendingShots(player));
    /// <summary>Client, every frame: a prediction of another selection, world or player is gone at once; a shot the server
    /// has not settled for <see cref="PredictionGiveUp"/> is given up (and said so in the log).</summary>
    public static void ClientTick() {
        if (s_prediction is not { } p) return;
        if (!ScNet.IsRemoteClient || !ReferenceEquals(p.Registry, ScGunRegistry.Current) || p.Selection != s_selection) { s_prediction = null; s_deferredAck = null; return; }
        double now = ScNet.Now;
        while (p.Times.Count > 0 && now - p.Times.Peek() > PredictionGiveUp) {
            p.Floor = Math.Max(p.Resolved, p.Floor) + 1; p.Trim(); GiveUps++;
            KnifeDiagnostics.WarnOnce("scnet-prediction-giveup", $"[ScCsgoNet] client: the server did not settle a shown shot within {PredictionGiveUp:0} s; it is no longer counted (shots {p.Shots}, settled {p.Resolved})");
        }
    }
    /// <summary>Client: the gun in hand is not usable (put away, dead, waiting for its record): nothing stays pending.</summary>
    public static void DropPrediction() { if (s_prediction is { } p) { p.Floor = p.Shots; p.Times.Clear(); } }
    /// <summary>Client: the server's count for this client's selection, read with the record rows it belongs to.</summary>
    internal static void ApplyAck(int selection, int value, int fired, int skipped) {
        if (selection != s_selection || fired < 0 || skipped < 0) return;                 // another selection's: nothing of it applies
        if (Item(value) != s_selValue) { s_deferredAck = (selection, Item(value), fired, skipped); return; }   // for the slot's next value (a fresh gun's record)
        if (s_prediction is not { } p || p.Selection != selection || !ReferenceEquals(p.Registry, ScGunRegistry.Current)) return;
        int resolved = fired + skipped;
        if (resolved > p.Resolved) p.Resolved = resolved;
        // The server counted shots this client never showed (it fired where the client's gun was busy): follow its count,
        // so the next shot shown here is a new number.
        if (p.Resolved > p.Shots) p.Shots = p.Resolved;
        p.Trim();
    }
    /// <summary>What the server said about this client's reloads, newest last (test diagnostics; bounded).</summary>
    public static readonly List<(int Id, ReloadPhase Phase, int Rounds)> ReloadLog = [];
    static void ReceiveReload(ScNetReader r) {
        int selection = r.Int(), id = r.Int(); var phase = (ReloadPhase)r.Byte(); int rounds = r.Int();
        r.Finish();
        if (phase is < ReloadPhase.Accepted or > ReloadPhase.Cancelled) throw new System.IO.InvalidDataException($"reload phase {(byte)phase}");
        if (ReloadLog.Count >= 32) ReloadLog.RemoveAt(0);
        ReloadLog.Add((id, phase, rounds));
        if (selection != s_selection) return;                                                // for a gun no longer in hand
        var player = GameManager.Project?.FindSubsystem<SubsystemPlayers>(false)?.ComponentPlayers.FirstOrDefault(ScNet.IsLocal);
        if (player is not null) GameManager.Project.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.ServerReload(player, id, phase);
    }
    /// <summary>Client: a new session (handshake accepted) or a closed world: no input, selection or prediction carries over.</summary>
    static void ResetClient() {
        s_prediction = null; s_deferredAck = null; s_selInventory = null; s_selSlot = -1; s_selValue = 0; s_selection++; s_sentSelection = -1; s_sentSlot = -1; s_sentValue = 0; s_sentShots = 0;
        s_lastFlags = 0; s_lastZoom = -1; s_lastSentAt = 0; s_pendingReload = s_pendingSecondary = false; s_reloadPendingId = 0;
    }

    // ---------------------------------------------------------------- shots seen by others
    public enum Impact : byte { None, Block, Body }
    public readonly record struct Pellet(Vector3 End, Impact Kind, int BlockValue);

    /// <summary>Counters read by the two-process tests (shots the server reported, other players' shots this client showed).</summary>
    public static int ShotsBroadcast, RemoteShotsShown;

    /// <summary>Server: tells every other client about a shot (its sound, tracers and impacts are theirs to show).</summary>
    public static void BroadcastShot(ComponentPlayer shooter, int gunValue, bool silenced, Vector3 origin, IReadOnlyList<Pellet> pellets, IReadOnlyList<Vector3> splashes) {
        if (!ScNet.IsHost || ScNet.Peers.Count == 0) return;
        ShotsBroadcast++;
        ScNet.Broadcast(OpShot, w => {
            w.Int(shooter.Entity.Id).Int(gunValue).Bool(silenced).Vector3(origin).Byte((byte)Math.Min(pellets.Count, 32));
            for (int i = 0; i < Math.Min(pellets.Count, 32); i++) w.Vector3(pellets[i].End).Byte((byte)pellets[i].Kind).Int(pellets[i].BlockValue);
            w.Byte((byte)Math.Min(splashes.Count, 4));
            for (int i = 0; i < Math.Min(splashes.Count, 4); i++) w.Vector3(splashes[i]);
        }, ScNet.PeerOf(shooter));
    }

    static void ReceiveShot(ScNetReader r) {
        int entityId = r.Int(), gunValue = r.Int();
        bool silenced = r.Bool();
        Vector3 origin = r.Vector3();
        int n = r.Byte();
        if (n > 32) throw new System.IO.InvalidDataException("too many pellets");
        var pellets = new Pellet[n];
        for (int i = 0; i < n; i++) {
            Vector3 end = r.Vector3(); var kind = (Impact)r.Byte(); int block = r.Int();
            pellets[i] = new(end, kind <= Impact.Body ? kind : Impact.None, block);
        }
        int s = r.Byte();
        if (s > 4) throw new System.IO.InvalidDataException("too many splashes");
        var splashes = new Vector3[s];
        for (int i = 0; i < s; i++) splashes[i] = r.Vector3();
        var project = GameManager.Project;
        RemoteShotsShown++;
        project?.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.ShowRemoteShot(entityId, gunValue, silenced, origin, pellets, splashes);
    }
}
