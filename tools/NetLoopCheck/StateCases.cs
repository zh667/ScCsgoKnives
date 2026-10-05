// mp-state-consistency-20261002: the cases of openspec/changes/weapon-state-consistency/verification.md that can be decided
// offline (C01-C12, and R07-R10 as far as the wire decides them: C13-C16), on the present core. See StateLoop.cs for what
// is real here and what stands in for the game; the gun state machine itself runs in GunLoop.cs.
using System.Reflection;
using Engine;
using Game;
using Game.Network;

static partial class StateLoop {
    static class Cases {
        static double s_clock = 5000;
        static World S, A, B;
        static ComponentPlayer PA => A.Player[1];
        static IScNetTransport Transport => ScNet.Transport;

        public static void Run() {
            ScNet.Clock = () => s_clock;
            s_adapterType.GetField("Now").SetValue(null, (Func<double>)(() => s_clock));
            S = Build("server"); A = Build("client A"); B = Build("client B");
            Join(S, A, B);
            Enter(A, false);
            Test("setup", "A and B are accepted on protocol 7; the adapter offers the platform's inventory replication", ScNet.Peers.Count == 2 && Transport is IScNetInventorySync && Transport.Handshake == ScNetHandshake.Accepted,
                $"{ScNet.Peers.Count} peers, {Transport.Handshake}, inventory sync {(Transport is IScNetInventorySync)}");
            C01(); C02(); C03(); C04(); C05(); C06(); C07(); C08(); C09(); C10(); C11(); C14(); C15(); C16(); C17(); C12(); C13();
        }

        // ---------------------------------------------------------------- frames
        /// <summary>The same item in the same slot on every end, in hand: the state after the platform synced a drag.</summary>
        static void Give(int slot, int value, bool hold = true) {
            foreach (var w in new[] { S, A, B }) { w.Inventory(1).m_slots[slot] = value; if (hold) w.Inventory(1).ActiveSlotIndex = slot; }
        }
        /// <summary>One frame of A's client: its input for the item in hand, a predicted shot when it may show one.</summary>
        static void ClientFrame(bool trigger = false, bool press = false, bool reload = false, double dt = 1 / 60.0) {
            Enter(A, false); s_clock += dt;
            int value = A.Held(1);
            if (!Usable(value)) {
                ScNetGuns.SendInput(PA, true, false, false, false, false, false, 0, Aim, true, false);
                ScNetGuns.DropPrediction();
                if (IsGun(value)) ScNetMirror.ClientWants(IdOf(value));
            }
            else {
                ScNetGuns.SendInput(PA, true, trigger, press, false, reload, false, 0, Aim, true, false);
                if ((trigger || press) && ScNetGuns.ShownRounds(PA, GunSpec.GetRounds(Terrain.ExtractData(value))) > 0) ScNetGuns.PredictShot(PA);
            }
            ScNetGuns.ClientTick();
        }
        /// <summary>The server's gun update for one remote player: a press or the held trigger fires one committed shot.</summary>
        static int GunUpdate(int index) {
            var player = S.Player[index];
            if (ScNetGuns.RemoteInput(player) is not { } remote) return 0;
            remote.Expire(ScNet.Now);
            bool press = remote.TakeHit() | remote.TakeDigPress() | remote.TakeCustomPress();
            remote.TakePressAim();
            int value = player.ComponentMiner.ActiveBlockValue; int shots = 0;
            if ((press || remote.Dig || remote.Custom) && Usable(value) && GunSpec.GetRounds(Terrain.ExtractData(value)) > 0 && Shoot(S, index, out _) == ScGunResult.Success) { ScNetGuns.ServerShot(player); shots = 1; }
            // What the gun update ends with for a remote client's player (SubsystemScGunBlockBehavior.UpdatePlayer): shots that
            // client has shown and the server will not fire are settled as skipped.
            value = player.ComponentMiner.ActiveBlockValue;
            ScNetGuns.ServerSettle(player, Usable(value) && GunSpec.GetRounds(Terrain.ExtractData(value)) > 0 && (remote.Dig || remote.Custom || remote.HasPresses));
            return shots;
        }
        /// <summary>One server frame: the gun subsystem's record tick, each remote player's gun update, the frame's end.</summary>
        static int ServerFrame(bool fire = true, Action during = null, double dt = 1 / 60.0) {
            Enter(S, true); s_clock += dt;
            ScNetMirror.RecordsTick(S.Registry, 0);
            int shots = fire ? GunUpdate(1) : 0;
            during?.Invoke();
            EndServerFrame(S);
            return shots;
        }
        /// <summary>Server frames without gun updates (time passes, ticks run); returns everything the server sent.</summary>
        static List<Packet> Settle(int frames = 8) { for (int i = 0; i < frames; i++) ServerFrame(false); return Take(); }
        static void Back(IEnumerable<Packet> batch) { var list = batch.ToList(); Deliver(list, A, s_sessionA); Deliver(list, B, s_sessionB); }
        /// <summary>A's packets to the server, a server frame with gun updates, the settled answer back to both clients.</summary>
        static (int Shots, List<Packet> Batch) RoundTrip() {
            ToServer(S); int shots = ServerFrame(); var batch = Take(); batch.AddRange(Settle()); Back(batch);
            return (shots, batch);
        }
        static int Pending() { Enter(A, false); ScNetGuns.Observe(PA); return ScNetGuns.PendingShots(PA); }
        static bool Row(Packet p) => IsCs(p) && Op(p) == ScNetMirror.OpRecords;
        /// <summary>The shot confirmation a record message ends with (ScNetMirror.SendRows), read from the packet's own bytes.</summary>
        static (bool Has, int Rows, int Selection, int Value, int Fired, int Skipped) AckOf(Packet p) {
            if (!Row(p)) return default;
            var r = new ScNetReader((byte[])p.GetType().GetField("Payload").GetValue(p));
            int rows = r.Int(); for (int i = 0; i < rows; i++) { r.Int(); if (r.Bool()) r.String(); }
            return r.Bool() ? (true, rows, r.Int(), r.Int(), r.Int(), r.Int()) : (false, rows, 0, 0, 0, 0);
        }
        static bool Ack(Packet p) => AckOf(p).Has;
        static bool ToA(Packet p) => ReferenceEquals(p.To, s_sessionA);
        static int ServerRounds(int id, int variant) => Rounds(S, id, variant);
        static int MirrorRounds(World w, int id, int variant) => Rounds(w, id, variant);
        static ScRemoteGunInput Remote { get { Enter(S, true); return ScNetGuns.RemoteInput(S.Player[1]); } }
        static int NewGun(int variant, int rounds, int durability = 1200) { Enter(S, true); return S.Registry.Allocate(variant, rounds, false, durability); }

        // ================================================================ C01 a fresh AK's first shot, slots 0/5/9
        static void C01() {
            foreach (int slot in new[] { 0, 5, 9 }) {
                int fresh = Fresh(s_ak); Give(slot, fresh);
                int next = S.Registry.Next, published = ScNetSlots.Published;
                ClientFrame(press: true);
                int hudAtOnce = Hud(A), pendingAtOnce = Pending(), recordsAtOnce = A.Registry.Count;
                ToServer(S);
                int shots = ServerFrame();
                var batch = Take();
                int serverValue = S.Slot(1, slot), id = IdOf(serverValue);
                Test("C01", $"slot {slot}: the client shows its own shot at once (29) without touching a record", hudAtOnce == 29 && pendingAtOnce == 1 && !A.Registry.TryGetSnapshot(id, out _), $"HUD {hudAtOnce}, pending {pendingAtOnce}, records on A {recordsAtOnce}");
                Test("C01", $"slot {slot}: the server fires once and gives the gun its own record", shots == 1 && id == next && S.Registry.Next == next + 1 && ServerRounds(id, s_ak) == 29 && !GunSpec.IsFresh(Terrain.ExtractData(serverValue)),
                    $"shots {shots}, id {id} (next was {next}), rounds {ServerRounds(id, s_ak)}");
                int iRow = batch.FindIndex(Row), iInventory = batch.FindIndex(p => p is InventorySyncPacket), iAck = batch.FindIndex(Ack);
                var ack = iAck >= 0 ? AckOf(batch[iAck]) : default;
                Test("C01", $"slot {slot}: in that same frame the server sends the record and, in that same message to A, the shot's confirmation; then that inventory once", iRow >= 0 && iAck >= 0 && iAck < iInventory && batch.Count(p => p is InventorySyncPacket) == 1 && ToA(batch[iAck])
                    && batch.Count(Ack) == 1 && ack.Rows == 1 && ack.Fired == 1 && ack.Skipped == 0 && ack.Value == serverValue && batch.Where(Row).All(p => p.To is not null && batch.IndexOf(p) < iInventory) && batch.Where(Row).Count() == 2 && ScNetSlots.Published == published + 1,
                    $"{Shape(batch)}; confirmation: {ack.Rows} row, fired {ack.Fired}, skipped {ack.Skipped}, for item id {IdOf(ack.Value)}");
                // The record and the confirmation cannot arrive apart: the client never reads the shot's rounds without the
                // word that it was its own shot (28 would show), nor the word without the rounds.
                Test("C01", $"slot {slot}: no packet of the batch carries a confirmation without its record row or to anyone but A", batch.Where(Ack).All(p => AckOf(p).Rows > 0 && ToA(p)) && !batch.Any(p => IsCs(p) && Op(p) == 42));
                // What mpb sent (everything but the inventory): the assertion that follows can tell the difference.
                Deliver(batch.Where(p => p is not InventorySyncPacket), B, s_sessionB);
                Test("C01", $"slot {slot}: control - a client that gets the record but not the inventory keeps the template", B.Slot(1, slot) == fresh && B.Slot(1, slot) != serverValue);
                Back(batch);
                Test("C01", $"slot {slot}: A and the watching B hold the server's item in that slot, and its record", A.Slot(1, slot) == serverValue && B.Slot(1, slot) == serverValue && MirrorRounds(A, id, s_ak) == 29 && MirrorRounds(B, id, s_ak) == 29,
                    $"server {serverValue}, A {A.Slot(1, slot)}, B {B.Slot(1, slot)}");
                Test("C01", $"slot {slot}: A reads 29, nothing is left predicted", Hud(A) == 29 && Pending() == 0, $"HUD {Hud(A)}, pending {Pending()}");
                var quiet = Settle(30);
                Test("C01", $"slot {slot}: standing still it stays so and nothing more is sent", quiet.Count == 0 && A.Slot(1, slot) == serverValue && Hud(A) == 29, Shape(quiet));
                // the second shot: an existing record, so only its row and the confirmation travel
                ClientFrame(press: true); var (shots2, batch2) = RoundTrip();
                Test("C01", $"slot {slot}: the next shot sends the record row with its confirmation only, never the inventory again", shots2 == 1 && batch2.Count(p => p is InventorySyncPacket) == 0 && batch2.Any(Row) && batch2.Count(Ack) == 1 && AckOf(batch2.First(Ack)).Fired == 2 && ScNetSlots.Published == published + 1
                    && S.Registry.Next == next + 1 && Hud(A) == 28 && ServerRounds(id, s_ak) == 28, $"{Shape(batch2)}; row message {batch2.Where(Row).Sum(p => Encode(p).Length)} B, the inventory packet was {Encode(batch[iInventory]).Length} B for {S.Inventory(1).SlotsCount} slots");
            }
        }

        // ================================================================ C02 finishes taken one at a time
        static void C02() {
            var skins = ScGunSkinCatalog.All.Where(s => Array.FindIndex(GunSpec.All, g => g.Name == s.Gun) >= 0).Take(3).ToArray();
            var bound = new List<(int Slot, int Value)>();
            for (int k = 0; k < skins.Length; k++) {
                int slot = k + 1, template = Terrain.MakeBlockValue(s_skinTemplate, 0, skins[k].PaintId);
                Give(slot, template);
                ClientFrame(); ToServer(S);
                // the gun subsystem turns a held template into an instance (SubsystemScGunBlockBehavior.Update, authority)
                ScGunResult result = default;
                ServerFrame(false, () => { var inv = S.Player[1].ComponentMiner.Inventory; result = ScGunSkinTemplateBlock.Materialize(inv, inv.ActiveSlotIndex, ScGunHolders.PlayerKey(S.Player[1], inv.ActiveSlotIndex)); });
                var batch = Take(); Back(batch);
                int value = S.Slot(1, slot);
                Enter(A, false);
                bool skinned = IsGun(A.Slot(1, slot)) && GunSpec.TryGetSnapshot(Terrain.ExtractData(A.Slot(1, slot)), out var snapshot) && snapshot.SkinId == skins[k].PaintId && snapshot.Rounds > 0;
                Test("C02", $"finish {k + 1} ({skins[k].Gun}): taken in hand it becomes that client's own gun with its finish, with no further action", result == ScGunResult.Success && A.Slot(1, slot) == value && B.Slot(1, slot) == value && skinned && Usable(A.Held(1)),
                    $"{result}; server {value}, A {A.Slot(1, slot)}, B {B.Slot(1, slot)}; [{Shape(batch)}]");
                bound.Add((slot, value));
                Test("C02", $"finish {k + 1}: the guns taken before it are unchanged on every end", bound.All(g => S.Slot(1, g.Slot) == g.Value && A.Slot(1, g.Slot) == g.Value && B.Slot(1, g.Slot) == g.Value));
            }
        }

        // ================================================================ C03 a duplicated Glock is separated (7 -> 12 in the user's log)
        static void C03() {
            int old = NewGun(s_glock, 5, 1185);
            int glockOld = Instance(s_glock, old);
            Give(0, glockOld);
            Enter(S, true); S.Inventory(0).m_slots[3] = glockOld;                  // the same record held somewhere else as well
            Back(Settle());
            int next = S.Registry.Next, count = S.Registry.Count;
            var locator = ScGunMutation.HolderLocator;
            ScGunMutation.HolderLocator = (id, holder) => id == old ? ["the other holder"] : [];
            try {
                ClientFrame(press: true);
                Test("C03", "before the server acts A reads 4 (5 less its shot); its copy of the old record still says 5", Hud(A) == 4 && MirrorRounds(A, old, s_glock) == 5, $"HUD {Hud(A)}, mirror {MirrorRounds(A, old, s_glock)}");
                ToServer(S); int shots = ServerFrame(); var batch = Take(); batch.AddRange(Settle());
                int separated = IdOf(S.Slot(1, 0));
                Test("C03", "the server gives the acting copy its own record; the old record keeps its 5 rounds and its wear", shots == 1 && separated == next && S.Registry.Count == count + 1 && ServerRounds(old, s_glock) == 5 && ServerRounds(separated, s_glock) == 4
                    && S.Slot(0, 3) == glockOld && Durability(S, old) == 1185, $"new id {separated}, old {ServerRounds(old, s_glock)} rounds / {Durability(S, old)} wear, new {ServerRounds(separated, s_glock)} rounds; [{Shape(batch)}]");
                Back(batch);
                Test("C03", "A's slot and B's copy of it name the new record", IdOf(A.Slot(1, 0)) == separated && IdOf(B.Slot(1, 0)) == separated, $"A {IdOf(A.Slot(1, 0))}, B {IdOf(B.Slot(1, 0))}");
                // the server's reload of the separated gun
                Shoot(S, 1, out _, r => r.Rounds = 20); Back(Settle());
                Test("C03", "after the server's reload A reads the new record's 20", Hud(A) == 20 && MirrorRounds(A, separated, s_glock) == 20 && MirrorRounds(A, old, s_glock) == 5, $"HUD {Hud(A)}");
                // five more shots shown that the server does not fire (the trigger is already up there): it says so, by number
                for (int i = 0; i < 5; i++) ClientFrame(press: true);
                int shown = Hud(A); int giveUps = ScNetGuns.GiveUps;
                ClientFrame();                                                   // (the count of shots shown goes out with the next frame's input)
                ToServer(S); Remote.DropWeaponInput(); ServerFrame(false, () => ScNetGuns.ServerSettle(S.Player[1], false)); var skipped = Take(); skipped.AddRange(Settle()); Back(skipped);
                Test("C03", "shots the server will not fire are settled by its word, with no time passing: the slot reads the new record's 20 again, never the old record's 5", shown == 15 && Hud(A) == 20 && MirrorRounds(A, old, s_glock) == 5 && MirrorRounds(A, separated, s_glock) == 20 && Pending() == 0
                    && skipped.Count(Ack) == 1 && AckOf(skipped.First(Ack)).Skipped == 5 && ScNetGuns.GiveUps == giveUps,
                    $"shown {shown} while predicting, then HUD {Hud(A)}; mirror old {MirrorRounds(A, old, s_glock)}, new {MirrorRounds(A, separated, s_glock)}; [{Shape(skipped)}] skipped {(skipped.Any(Ack) ? AckOf(skipped.First(Ack)).Skipped : -1)}");
            }
            finally { ScGunMutation.HolderLocator = locator; }
        }
        static int Durability(World w, int id) => w.Registry.TryGetSnapshot(id, out var s) ? s.Durability : -1;

        // ================================================================ C04 record and slot in either order, a lost record
        static void C04() {
            // slot first
            Give(2, Fresh(s_ak));
            ClientFrame(press: true); ToServer(S); ServerFrame(); var batch = Take(); batch.AddRange(Settle());
            int id = IdOf(S.Slot(1, 2));
            Deliver(batch.Where(p => p is InventorySyncPacket), A, s_sessionA);
            Enter(A, false);
            Test("C04", "slot before record: the gun is not usable and nothing is made up for it", A.Slot(1, 2) == S.Slot(1, 2) && !Usable(A.Held(1)) && GunSpec.GetRounds(Terrain.ExtractData(A.Held(1))) == 0, $"A slot id {IdOf(A.Slot(1, 2))}, usable {Usable(A.Held(1))}");
            ClientFrame(press: true, trigger: true); ToServer(S); int shots = ServerFrame(); Take();
            Test("C04", "while it waits the client sends no trigger and predicts nothing; the server fires nothing", shots == 0 && Pending() == 0 && ServerRounds(id, s_ak) == 29, $"shots {shots}, server rounds {ServerRounds(id, s_ak)}");
            Deliver(batch.Where(p => p is not InventorySyncPacket), A, s_sessionA); Deliver(batch, B, s_sessionB);
            Test("C04", "the record arrives: usable, 29, without any further action", Usable(A.Held(1)) && Hud(A) == 29, $"HUD {Hud(A)}");
            // record first is C01's order (asserted there); a record that never arrives:
            Give(3, Fresh(s_ak));
            ClientFrame(press: true); ToServer(S); ServerFrame(); batch = Take(); batch.AddRange(Settle());
            id = IdOf(S.Slot(1, 3));
            Deliver(batch.Where(p => p is InventorySyncPacket), A, s_sessionA); Deliver(batch, B, s_sessionB);   // A's row is lost
            int wants = ScNetMirror.WantsSent, answered = ScNetMirror.WantsAnswered;
            for (int i = 0; i < 20; i++) ClientFrame();                                                          // 0.33 s: still waiting
            bool early = ScNetMirror.WantsSent == wants;
            for (int i = 0; i < 20; i++) ClientFrame();                                                          // past 0.5 s: asks
            ToServer(S); Back(Settle());
            Test("C04", "a record that does not arrive is asked for after half a second, and the gun then works", early && ScNetMirror.WantsSent == wants + 1 && ScNetMirror.WantsAnswered == answered + 1 && Usable(A.Held(1)) && Hud(A) == 29,
                $"asked {ScNetMirror.WantsSent - wants}, answered {ScNetMirror.WantsAnswered - answered}, HUD {Hud(A)}");
            // a server that never answers: bounded
            foreach (var w in new[] { A }) { w.Inventory(1).m_slots[4] = Instance(s_ak, 900); w.Inventory(1).ActiveSlotIndex = 4; }
            wants = ScNetMirror.WantsSent;
            for (int i = 0; i < 60 * 90; i++) { ClientFrame(); Take(); }                                          // 90 s, nothing delivered
            Test("C04", "asking is bounded (5 requests over 15 s, then it only waits) and the gun stays unusable", ScNetMirror.WantsSent == wants + ScNetMirror.WantAttempts && !Usable(A.Held(1)), $"{ScNetMirror.WantsSent - wants} requests");
            A.Inventory(1).m_slots[4] = 0;
        }

        // ================================================================ C05 two commits in one frame, repeats, an old row, a failed send
        static void C05() {
            Give(6, Fresh(s_ak)); Give(7, Fresh(s_glock), hold: false);
            int published = ScNetSlots.Published;
            ServerFrame(false, () => {
                Shoot(S, 1, out _);
                S.Inventory(1).ActiveSlotIndex = 7; Shoot(S, 1, out _); S.Inventory(1).ActiveSlotIndex = 6;
            });
            var batch = Take(); batch.AddRange(Settle());
            Back(batch);
            Test("C05", "two guns given their records in one frame: that inventory is sent once, with both", batch.Count(p => p is InventorySyncPacket) == 1 && ScNetSlots.Published == published + 1 && A.Slot(1, 6) == S.Slot(1, 6) && A.Slot(1, 7) == S.Slot(1, 7)
                && !GunSpec.IsFresh(Terrain.ExtractData(A.Slot(1, 6))) && !GunSpec.IsFresh(Terrain.ExtractData(A.Slot(1, 7))), Shape(batch));
            // everything delivered again, twice
            int next = S.Registry.Next, akId = IdOf(S.Slot(1, 6)), rounds = ServerRounds(akId, s_ak), slotA = A.Slot(1, 6);
            Back(batch); Back(batch);
            Test("C05", "the same messages delivered twice more change nothing on any end", A.Slot(1, 6) == slotA && MirrorRounds(A, akId, s_ak) == rounds && ServerRounds(akId, s_ak) == rounds && S.Registry.Next == next && Hud(A) == rounds, $"HUD {Hud(A)}");
            // an older row after a newer one
            ClientFrame(press: true); ToServer(S); ServerFrame(); var first = Take(); first.AddRange(Settle());
            ClientFrame(press: true); ToServer(S); ServerFrame(); var second = Take(); second.AddRange(Settle());
            int stale = A.Registry.NetworkRowsStale;
            Deliver(second, A, s_sessionA); Deliver(first, A, s_sessionA); Back(second.Where(p => false));
            Deliver(first, B, s_sessionB); Deliver(second, B, s_sessionB);
            Test("C05", "an older record row arriving after a newer one is dropped", MirrorRounds(A, akId, s_ak) == rounds - 2 && ServerRounds(akId, s_ak) == rounds - 2 && A.Registry.NetworkRowsStale == stale + 1 && Hud(A) == rounds - 2,
                $"A's record {MirrorRounds(A, akId, s_ak)}, server {ServerRounds(akId, s_ak)}, stale rows {A.Registry.NetworkRowsStale - stale}");
            // what orders the platform's own inventory packet (it carries no version)
            Packet inventory = batch.First(p => p is InventorySyncPacket), cs = batch.First(IsCs);
            Test("C05", "the platform's inventory packet and the CS packet travel on the same channel (0), which the platform sends reliable-ordered: an older snapshot cannot overtake a newer one", inventory.Channel == 0 && cs.Channel == 0,
                $"channels {inventory.Channel}/{cs.Channel}; delivery method per the platform's NetworkServer.Send / RelayHost.Send (ReliableOrdered)");
            // a send that fails: the row stays due, nothing is evaluated again
            var real = ScNet.Transport; var failing = new Failing(real) { FailRecords = true };
            ScNet.Attach(failing);
            try {
                int failures = ScNetMirror.RowSendFailures;
                ClientFrame(press: true); ToServer(S); ServerFrame(); var lost = Take(); lost.AddRange(Settle());
                Back(lost);
                bool notYet = MirrorRounds(A, akId, s_ak) == rounds - 2 && ServerRounds(akId, s_ak) == rounds - 3 && ScNetMirror.RowSendFailures > failures && !lost.Any(Row);
                failing.FailRecords = false;
                var resent = Settle(); Back(resent);
                Test("C05", "a record row that could not be sent stays due and goes out with a later tick; the shot is not fired again", notYet && resent.Any(Row) && MirrorRounds(A, akId, s_ak) == rounds - 3 && ServerRounds(akId, s_ak) == rounds - 3 && S.Registry.Next == next,
                    $"failed sends {ScNetMirror.RowSendFailures - failures}; A's record {MirrorRounds(A, akId, s_ak)}, server {ServerRounds(akId, s_ak)}");
            }
            finally { ScNet.Attach(real); }
        }
        /// <summary>The real transport, except that record rows cannot be queued while <see cref="FailRecords"/> is set.</summary>
        sealed class Failing(IScNetTransport inner) : IScNetTransport, IScNetInventorySync {
            public bool FailRecords;
            public ScNetRole Role => inner.Role;
            public ScNetHandshake Handshake => inner.Handshake;
            public string HandshakeDetail => inner.HandshakeDetail;
            public IReadOnlyList<ScNetPeer> Peers => inner.Peers;
            public bool IsLocal(ComponentPlayer player) => inner.IsLocal(player);
            public bool SendToServer(ushort op, byte[] payload) => inner.SendToServer(op, payload);
            public bool SendTo(ScNetPeer peer, ushort op, byte[] payload) => !(FailRecords && op == ScNetMirror.OpRecords) && inner.SendTo(peer, op, payload);
            public void Broadcast(ushort op, byte[] payload, ScNetPeer except) => inner.Broadcast(op, payload, except);
            public bool Publish(IInventory inventory) => ((IScNetInventorySync)inner).Publish(inventory);
            public bool Correct(IInventory inventory, ScNetPeer peer) => ((IScNetInventorySync)inner).Correct(inventory, peer);
            public bool AnnounceActiveSlot(IInventory inventory, ScNetPeer except) => ((IScNetInventorySync)inner).AnnounceActiveSlot(inventory, except);
        }

        // ================================================================ C06 a failed transaction publishes what it restored
        static void C06() {
            int fresh = Fresh(s_ak); Give(8, fresh);
            int count = S.Registry.Count, published = ScNetSlots.Published;
            ScGunResult result = default; int candidate = -1;
            ServerFrame(false, () => {
                var player = S.Player[1]; var inv = player.ComponentMiner.Inventory;
                var tx = ScGunMutation.Prepare(inv, 8, ScGunHolders.PlayerKey(player, 8), out _);
                candidate = S.Registry.PeekNextId();
                typeof(ScGunMutation).GetField("AfterRecordWrite", All).SetValue(tx, (Action)(() => throw new InvalidOperationException("fault after the record was written")));
                result = tx.Commit(r => r.Rounds--);
            });
            var batch = Take(); batch.AddRange(Settle()); Back(batch);
            Enter(A, false);
            Test("C06", "a commit that fails after its record was written restores the slot and leaves no record", result != ScGunResult.Success && S.Slot(1, 8) == fresh && S.Registry.Count == count && !S.Registry.TryGetSnapshot(candidate, out _), $"{result}; server slot fresh {S.Slot(1, 8) == fresh}, records {S.Registry.Count}/{count}");
            Test("C06", "what is published is the restored slot, and no record of the abandoned number reaches a client", batch.Count(p => p is InventorySyncPacket) == 1 && ScNetSlots.Published == published + 1 && A.Slot(1, 8) == fresh && B.Slot(1, 8) == fresh && !A.Registry.TryGetSnapshot(candidate, out _),
                Shape(batch));
            // the gun is moved between Prepare and Commit
            published = ScNetSlots.Published; int next = S.Registry.Next;
            ServerFrame(false, () => {
                var player = S.Player[1]; var inv = player.ComponentMiner.Inventory;
                var tx = ScGunMutation.Prepare(inv, 8, ScGunHolders.PlayerKey(player, 8), out _);
                S.Inventory(1).m_slots[8] = 0; S.Inventory(1).m_slots[9] = fresh;
                result = tx.Commit(r => r.Rounds--);
                S.Inventory(1).m_slots[9] = S.Slot(1, 9) == fresh ? 0 : S.Slot(1, 9); S.Inventory(1).m_slots[8] = fresh;
            });
            batch = Take(); batch.AddRange(Settle());
            Test("C06", "a gun moved away before the commit: refused, no number used, nothing published", result == ScGunResult.StateChanged && S.Registry.Next == next && ScNetSlots.Published == published && !batch.Any(p => p is InventorySyncPacket), $"{result}; [{Shape(batch)}]");
            // an inventory that refuses the write
            var refusing = new Refusing { OpenSlotsCount = 10 };
            for (int i = 0; i < 10; i++) refusing.m_slots.Add(0);
            refusing.m_slots[0] = fresh; refusing.m_entity = S.Player[2].Entity;
            var saved = S.Player[2].ComponentMiner.Inventory; S.Player[2].ComponentMiner.Inventory = refusing;
            try {
                Enter(S, true);
                var tx = ScGunMutation.Prepare(refusing, 0, ScGunHolders.Key(refusing, 0), out _);
                refusing.Refuse = true;
                result = tx.Commit(r => r.Rounds--);
                Test("C06", "an inventory that does not take the new item: refused, the slot keeps the template, no record left behind", result != ScGunResult.Success && refusing.GetSlotValue(0) == fresh && S.Registry.Next == next && S.Registry.Count == count, $"{result}");
            }
            finally { S.Player[2].ComponentMiner.Inventory = saved; Enter(S, true); EndServerFrame(S); Take(); }
        }
        sealed class Refusing : ComponentCreativeInventory {
            public bool Refuse;
            public override void AddSlotItems(int slotIndex, int value, int count) { if (!Refuse) base.AddSlotItems(slotIndex, value, count); }
        }

        // ================================================================ C07 a prediction never outlives its world, player or session
        static void C07() {
            int id = NewGun(s_glock, 20); int gun = Instance(s_glock, id);
            Give(0, gun); Back(Settle());
            for (int i = 0; i < 3; i++) ClientFrame(press: true);
            var unsent = Take();
            Test("C07", "three shots shown, none confirmed: A reads 17 and its record still says 20", Hud(A) == 17 && MirrorRounds(A, id, s_glock) == 20);
            // the world is replaced (same record number in the next one), with no clean-up call at all
            var next = Build("client A, next world");
            Enter(next, false); next.Registry.ApplyNetworkRow(id, "v=" + s_glock + ",r=20,s=0,d=1200,m=1200,n=1,c=-1,p=0,ct=0,k=0,gl=0,gp=-1,gv=0,rc=0,ov=0,kc=0", 0);
            next.Inventory(1).m_slots[0] = gun; next.Inventory(1).ActiveSlotIndex = 0;
            Test("C07", "the next world's record of the same number reads 20 at once: the prediction belongs to the former world's table", Hud(next) == 20 && ScNetGuns.PendingShots(next.Player[1]) == 0, $"HUD {Hud(next)}");
            s_clock += 10; Enter(next, false); ScNetGuns.ClientTick();
            Test("C07", "and stays 20 after every time-out", Hud(next) == 20 && MirrorRounds(next, id, s_glock) == 20);
            // back in the first world: the player's entity is replaced (respawn)
            Enter(A, false);
            for (int i = 0; i < 2; i++) ClientFrame(press: true);
            Take();
            var before = PA; var respawned = AddPlayer(A, 1);
            A.Inventory(1).m_slots[0] = gun; A.Inventory(1).ActiveSlotIndex = 0;
            Test("C07", "a replaced player entity starts with nothing predicted", !ReferenceEquals(before, respawned) && ScNetGuns.PendingShots(respawned) == 0 && Hud(A) == 20, $"HUD {Hud(A)}");
            // the world closes (OnProjectDisposed) and a session is accepted again
            ClientFrame(press: true); Take();
            int selection = ScNetGuns.LocalSelection;
            ScNetGuns.WorldClosed();
            Test("C07", "closing the world drops the prediction and starts another selection", Pending() == 0 && ScNetGuns.LocalSelection != selection);
            // the server: a connection that leaves takes its held trigger and its input sequence with it
            Enter(A, false); ClientFrame(trigger: true); ToServer(S);
            bool held = Remote.Dig && Remote.Sequenced;
            s_sessions.Remove(s_sessionA); Enter(S, true); s_adapter.SubsystemUpdate(null, .016f);
            var gone = ScNetGuns_Input(S.Player[1]);
            Test("C07", "a departed connection's trigger is let go and its input sequence forgotten", held && gone is { Dig: false, Sequenced: false } && ScNet.Peers.Count == 1, $"held {held}, peers {ScNet.Peers.Count}");
            s_sessions.Insert(0, s_sessionA);
            Enter(A, false); Call("SendHelloForTest", -1); ToServer(S); Deliver(Take(), A, s_sessionA);
            Test("C07", "A is accepted again and plays on", ScNet.Peers.Count == 2 && Transport.Handshake == ScNetHandshake.Accepted, $"{Transport.Handshake}");
            Take();
            // (A's replaced player: the server and B keep their own copies; put the gun back in hand everywhere.)
            Give(0, gun); Back(Settle());
        }
        static ScRemoteGunInput ScNetGuns_Input(ComponentPlayer player) {
            var table = typeof(ScNetGuns).GetField("s_remote", All).GetValue(null);
            object[] args = [player, null];
            return (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, args) ? (ScRemoteGunInput)args[1] : null;
        }

        // ================================================================ C08 reload, confirmations and new shots interleaved
        static void C08() {
            int id = NewGun(s_glock, 20); int gun = Instance(s_glock, id);
            Give(1, gun); Back(Settle());
            int giveUps = ScNetGuns.GiveUps;
            for (int i = 0; i < 3; i++) ClientFrame(press: true);
            ClientFrame();                                                     // (the count of shots shown goes out with the next frame's input)
            ToServer(S);
            int fired = ServerFrame() + ServerFrame();                         // the server gets to two of the three; the third press is still waiting there
            var batch = Take(); batch.AddRange(Settle()); Back(batch);                 // (Settle runs no gun update: the third press stays queued)
            Test("C08", "two of three shown shots confirmed: record 18, one still pending, A reads 17", fired == 2 && MirrorRounds(A, id, s_glock) == 18 && Pending() == 1 && Hud(A) == 17, $"fired {fired}, mirror {MirrorRounds(A, id, s_glock)}, pending {Pending()}, HUD {Hud(A)}");
            Deliver(batch.Where(Ack), A, s_sessionA); Deliver(batch.Where(Ack).Reverse(), A, s_sessionA);
            Test("C08", "the same confirmations again, in any order, pay nothing off twice", Pending() == 1 && Hud(A) == 17 && MirrorRounds(A, id, s_glock) == 18);
            // the server starts its reload instead of firing the third (what its state machine does when the magazine or the
            // request says so): the shot shown is settled as skipped, in the frame the reload's rounds are written
            Enter(S, true); Remote.DropWeaponInput();
            ServerFrame(false, () => { Shoot(S, 1, out _, r => r.Rounds = 20); ScNetGuns.ServerSettle(S.Player[1], false); });
            var reload = Take(); reload.AddRange(Settle()); Back(reload);
            var word = reload.Where(Ack).Select(AckOf).FirstOrDefault();
            Test("C08", "the reload's rounds and the word that the third shot will not be fired arrive in one message: A reads the server's 20, never 19 or 21", reload.Count(Ack) == 1 && word.Rows == 1 && word.Fired == 2 && word.Skipped == 1
                && MirrorRounds(A, id, s_glock) == 20 && Hud(A) == 20 && Pending() == 0, $"[{Shape(reload)}] fired {word.Fired} skipped {word.Skipped}; mirror {MirrorRounds(A, id, s_glock)}, HUD {Hud(A)}, pending {Pending()}");
            ClientFrame(press: true); var (shots, _) = RoundTrip();
            Test("C08", "a new shot after the reload is confirmed once: record 19", shots == 1 && MirrorRounds(A, id, s_glock) == 19 && ServerRounds(id, s_glock) == 19 && Hud(A) == 19 && Pending() == 0, $"mirror {MirrorRounds(A, id, s_glock)}, pending {Pending()}, HUD {Hud(A)}");
            s_clock += 2; Enter(A, false); ScNetGuns.ClientTick();
            Test("C08", "nothing here was settled by a time-out, and the record was never written by a prediction", Hud(A) == 19 && Pending() == 0 && MirrorRounds(A, id, s_glock) == ServerRounds(id, s_glock) && ScNetGuns.GiveUps == giveUps, $"give-ups {ScNetGuns.GiveUps - giveUps}");
        }

        // ================================================================ C09 the input is for the gun it was pressed for
        static void C09() {
            int a = NewGun(s_ak, 10), b = NewGun(s_ak, 10); int gunA = Instance(s_ak, a), gunB = Instance(s_ak, b);
            Give(0, gunA); Give(1, gunB, hold: false); Back(Settle());
            // the server's slot changed under the client (an automatic refill, a provider's direct write): the client still shows A
            Enter(S, true); S.Inventory(1).m_slots[0] = gunB; S.Inventory(1).m_slots[1] = 0;
            int stale = Remote.Stale, corrections = ScNetSlots.Corrections;
            ClientFrame(press: true); ToServer(S); int shots = ServerFrame(); var batch = Take();
            Test("C09", "a press for gun A while the server holds gun B in that slot fires nothing and costs B nothing", shots == 0 && ServerRounds(b, s_ak) == 10 && ServerRounds(a, s_ak) == 10 && Remote.Stale == stale + 1 && !Remote.Dig, $"shots {shots}, B {ServerRounds(b, s_ak)}, stale {Remote.Stale - stale}");
            // the client keeps naming it: after half a second the server sends that client its inventory again
            var sent = new List<Packet>();
            for (int i = 0; i < 40; i++) { ClientFrame(trigger: true); ToServer(S); shots += ServerFrame(); sent.AddRange(Take()); }
            var correction = sent.Where(p => p is InventorySyncPacket).ToList();
            Test("C09", "still nothing fired; the server corrects that one client's inventory, once", shots == 0 && ServerRounds(b, s_ak) == 10 && correction.Count == 1 && ReferenceEquals(correction[0].To, s_sessionA) && ScNetSlots.Corrections == corrections + 1, $"shots {shots}; [{Shape(correction)}]");
            Deliver(sent, A, s_sessionA);
            ClientFrame(press: true); var (after, _) = RoundTrip();
            Test("C09", "with the corrected slot the client's next press fires gun B", A.Slot(1, 0) == gunB && after == 1 && ServerRounds(b, s_ak) == 9 && ServerRounds(a, s_ak) == 10, $"A slot id {IdOf(A.Slot(1, 0))}, B {ServerRounds(b, s_ak)}");
            // another active slot on the server
            Give(0, gunA); Give(1, gunB, hold: false); Back(Settle());
            Enter(S, true); S.Inventory(1).ActiveSlotIndex = 1;
            ClientFrame(press: true); ToServer(S); shots = ServerFrame(); Take();
            Test("C09", "a press naming slot 0 while the server's active slot is 1 fires nothing", shots == 0 && ServerRounds(b, s_ak) == 9 && ServerRounds(a, s_ak) == 10);
            // the client keeps holding slot 0: which slot is in hand is its own choice, so the server follows it and tells the others
            int adopted = Remote.SlotsAdopted; sent = [];
            for (int i = 0; i < 40; i++) { ClientFrame(); ToServer(S); shots += ServerFrame(); sent.AddRange(Take()); }
            var announce = sent.Where(p => p is ActiveSlotChangePacket).ToList();
            Enter(S, true);
            Test("C09", "after half a second of the client naming slot 0 the server's active slot follows it; the other clients are told, the owner is not", shots == 0 && S.Inventory(1).ActiveSlotIndex == 0 && Remote.SlotsAdopted == adopted + 1
                && announce.Count == 1 && ReferenceEquals(announce[0].Except, s_sessionA) && announce[0].To is null, $"server active slot {S.Inventory(1).ActiveSlotIndex}; [{Shape(sent.Where(p => !IsCs(p)))}]");
            Deliver(sent, B, s_sessionB);
            ClientFrame(press: true); var (onA, _) = RoundTrip();
            Test("C09", "and the next press fires the gun in that slot", onA == 1 && ServerRounds(a, s_ak) == 9 && ServerRounds(b, s_ak) == 9, $"A {ServerRounds(a, s_ak)}, B {ServerRounds(b, s_ak)}");
            // a press queued for A does not fire B after a switch
            ClientFrame(press: true); ToServer(S);                                               // the press for A arrives
            foreach (var w in new[] { S, A, B }) w.Inventory(1).ActiveSlotIndex = 1;             // the platform's ActiveSlotChangePacket, then
            ClientFrame(); ToServer(S);                                                          // A's input for B, no press
            shots = ServerFrame(); Take();
            Test("C09", "a press queued for gun A is dropped when the next input is for gun B", shots == 0 && ServerRounds(b, s_ak) == 9 && ServerRounds(a, s_ak) == 9, $"shots {shots}");
            ClientFrame(press: true); var (onB, _) = RoundTrip();
            Test("C09", "and a press for B fires B", onB == 1 && ServerRounds(b, s_ak) == 8);
            // a fresh gun: the second press still names the template while the server already made it an instance
            Give(2, Fresh(s_ak)); stale = Remote.Stale;
            ClientFrame(press: true); ToServer(S); int first = ServerFrame(); var held = Take();          // A has not received anything yet
            ClientFrame(press: true); ToServer(S); int second = ServerFrame(); held.AddRange(Take()); held.AddRange(Settle());
            int id = IdOf(S.Slot(1, 2));
            Test("C09", "a fresh gun's second press, still naming the template, is that same gun's: both shots fire, nothing refused", first == 1 && second == 1 && ServerRounds(id, s_ak) == 28 && Remote.Stale == stale, $"shots {first}+{second}, rounds {ServerRounds(id, s_ak)}, stale {Remote.Stale - stale}");
            Back(held);
            Test("C09", "and A ends on the server's 28", Hud(A) == 28 && A.Slot(1, 2) == S.Slot(1, 2), $"HUD {Hud(A)}");
            // long after, an input naming the template is no longer that gun
            s_clock += ScNetGuns.RewriteWindow + 1;
            A.Inventory(1).m_slots[2] = Fresh(s_ak); stale = Remote.Stale;
            ClientFrame(press: true); ToServer(S); shots = ServerFrame(); Take();
            Test("C09", "the template's name stops being accepted for that slot after the rewrite window", shots == 0 && Remote.Stale == stale + 1 && ServerRounds(id, s_ak) == 28);
            A.Inventory(1).m_slots[2] = S.Slot(1, 2);
        }

        // ================================================================ C10 a short press, a repeat, a client that goes silent
        static void C10() {
            int id = NewGun(s_ak, 30); int gun = Instance(s_ak, id);
            Give(3, gun); Back(Settle());
            ClientFrame(); ToServer(S); ServerFrame(); Take();
            // press and release before the server's next frame
            ClientFrame(press: true); ClientFrame();
            var inputs = Take();
            ToServer(S, inputs);
            int shots = ServerFrame() + ServerFrame() + ServerFrame(); Take();
            Test("C10", "a press released before the server's next frame fires exactly once", shots == 1 && ServerRounds(id, s_ak) == 29, $"shots {shots}");
            int repeats = Remote.Repeats;
            ToServer(S, inputs); ToServer(S, inputs);
            shots = ServerFrame() + ServerFrame(); Take();
            Test("C10", "the same messages delivered again are ignored: no second shot", shots == 0 && ServerRounds(id, s_ak) == 29 && Remote.Repeats == repeats + inputs.Count * 2, $"repeats {Remote.Repeats - repeats}");
            Back(Settle());
            // the trigger held, then the client goes silent
            ClientFrame(trigger: true); ToServer(S);
            int expired = Remote.Expired; shots = 0; int frames = 0; double start = s_clock, lastShotAt = start;
            for (; frames < 60 * 4; frames++) {                                    // 4 s of server frames, one shot each 0.1 s while held
                Enter(S, true); s_clock += 1 / 60.0; ScNetMirror.RecordsTick(S.Registry, 0);
                var remote = ScNetGuns.RemoteInput(S.Player[1]); remote.Expire(ScNet.Now);
                if (frames % 6 == 0 && remote.Dig && Shoot(S, 1, out _) == ScGunResult.Success) { ScNetGuns.ServerShot(S.Player[1]); shots++; lastShotAt = s_clock; }
                EndServerFrame(S);
            }
            Take();
            Test("C10", "a held trigger stops within the lease once the client goes silent; the magazine is not emptied", shots is >= 9 and <= 11 && lastShotAt - start <= ScNetGuns.InputLease + .02 && Remote.Expired == expired + 1 && ServerRounds(id, s_ak) == 29 - shots && ServerRounds(id, s_ak) >= 18,
                $"{shots} shots in {lastShotAt - start:0.00} s, then none for {s_clock - lastShotAt:0.0} s; {ServerRounds(id, s_ak)} rounds left");
            // the client comes back, still holding: fresh input, no backlog
            int before = ServerRounds(id, s_ak);
            ClientFrame(trigger: true); ToServer(S);
            int one = ServerFrame(); Take();
            Test("C10", "when its input resumes the trigger holds again, one shot a frame of the gun's own cadence, none made up for the silence", one == 1 && Remote.Dig && ServerRounds(id, s_ak) == before - 1, $"first frame {one} shot");
            ClientFrame(); ToServer(S); Back(Settle());
            // the plant key and a held throw
            Enter(A, false); ScNetC4.SendInput(true, 30); ToServer(S);
            Enter(S, true);
            bool planting = ScNetC4.RemotePlant(S.Player[1]).Held;
            for (int i = 0; i < 12; i++) { Enter(A, false); s_clock += ScNetGuns.InputRefresh; ScNetC4.SendInput(true, 30); ToServer(S); }
            Enter(S, true);
            bool renewed = ScNetC4.RemotePlant(S.Player[1]).Held && ScNetC4.RemotePlant(S.Player[1]).Expired == 0;
            s_clock += ScNetGuns.InputLease + .1;
            var plant = ScNetC4.RemotePlant(S.Player[1]);
            Test("C10", "the plant key: renewed every quarter second it stays held for 3 s; a second of silence releases it (the plant is then cancelled like any release)", planting && renewed && !plant.Held && plant.Expired == 1, $"held {planting}, renewed {renewed}, after silence held {plant.Held}");
            Enter(A, false); ScNetGrenades.SendStart(false, (Aim.Position, Aim.Position, Aim.Direction)); ToServer(S);
            Enter(S, true);
            bool throwing = ScNetGrenades.RemoteThrow(S.Player[1]).Pressed && !ScNetGrenades.HeldExpired(S.Player[1]);
            s_clock += ScNetGuns.InputLease + .1;
            bool expiredThrow = ScNetGrenades.HeldExpired(S.Player[1]), again = ScNetGrenades.HeldExpired(S.Player[1]);
            Test("C10", "a held throw: a second of silence ends the hold once (the throw is cancelled, nothing thrown or taken)", throwing && expiredThrow && !again && !ScNetGrenades.RemoteThrow(S.Player[1]).Pressed);
            Take();
        }

        // ================================================================ C11 an ordinary move uses no number
        static void C11() {
            int id = NewGun(s_glock, 12, 900); int gun = Instance(s_glock, id);
            Give(4, gun); Back(Settle());
            int next = S.Registry.Next, count = S.Registry.Count;
            foreach (var w in new[] { S, A, B }) { w.Inventory(1).m_slots[4] = 0; w.Inventory(1).m_slots[5] = gun; w.Inventory(1).ActiveSlotIndex = 5; }   // a drag, synced by the platform
            ClientFrame(press: true); var (shots, batch) = RoundTrip();
            Test("C11", "a gun moved to another slot keeps its record and number: the shot there uses no new id and sends no inventory", shots == 1 && IdOf(S.Slot(1, 5)) == id && S.Registry.Next == next && S.Registry.Count == count && ServerRounds(id, s_glock) == 11
                && Durability(S, id) == 900 && !batch.Any(p => p is InventorySyncPacket) && Hud(A) == 11, $"id {IdOf(S.Slot(1, 5))}, next {S.Registry.Next}/{next}; [{Shape(batch)}]");
        }

        // ================================================================ C12 a peer of the former protocol is refused, by name
        static void C12() {
            int version = (int)s_adapterType.GetField("ProtocolVersion").GetRawConstantValue();
            Enter(A, false); Call("SendHelloProtocolForTest", version - 1); ToServer(S);
            int peers = ScNet.Peers.Count; Deliver(Take(), A, s_sessionA);
            Enter(A, false);
            Test("C12", $"a client of CS network protocol {version - 1} (the build before) is refused with that reason; its CS weapons stay off", version == 7 && Transport.Handshake == ScNetHandshake.Rejected && Transport.HandshakeDetail.Contains("CS 网络协议") && Transport.HandshakeDetail.Contains("/" + (version - 1)) && Transport.HandshakeDetail.Contains("/" + version) && peers == 1 && ScNet.ClientBlocked,
                Transport.HandshakeDetail);
            bool sent = ScNet.Send(ScNetGuns.OpInput, w => w.Int(0));
            Test("C12", "a refused client sends no gun input", !sent && Take().Count == 0);
            Enter(A, false); Call("SendHelloForTest", -1); ToServer(S); Deliver(Take(), A, s_sessionA);
            Test("C12", "the present protocol is accepted again", Transport.Handshake == ScNetHandshake.Accepted && ScNet.Peers.Count == 2);
        }

        // ================================================================ C17 (U01) what a player is told on joining
        static void C17() {
            Enter(A, false);
            Type platform = s_adapterType.Assembly.GetType("Game.ScPlatformCompat", true);
            bool verified = (bool)platform.GetProperty("BuildVerified").GetValue(null);
            string notice = (string)platform.GetMethod("PlayerNotice").Invoke(null, null), summary = (string)platform.GetProperty("Summary").GetValue(null);
            string gate = (string)platform.GetProperty("CommandGateStatus").GetValue(null); bool compat = (bool)platform.GetProperty("HasCompatNet").GetValue(null);
            // The platform build under test is the user's own (not in the adapter's verified list): the case the notice was shown in.
            Test("C17", "on a platform build the adapter has not verified, an accepted player is shown no notice about it; the build ids and the state of the corrections stay in the log line", (verified || notice is null || !notice.Contains("未经验证"))
                && (notice is null || compat) && summary.Contains(verified ? "platform verified" : "platform unverified") && summary.Contains("terrain:") && Transport.Handshake == ScNetHandshake.Accepted,
                $"verified {verified}; notice: {notice ?? "none"}; log: {summary}");
            Test("C17", "the notices that say something does not work are still there: a correction that could not be enabled, and (C12) a refused protocol", notice is null == !(compat && !(bool)platform.GetProperty("CommandGateInstalled").GetValue(null) && !gate.StartsWith("upstream fixed")),
                $"CompatNet {compat}, corrections: {gate}");
        }

        // ================================================================ C13 (R07) one number, one meaning, in the game's own registration
        static void C13() {
            // The appearance package registers last here (see Platform); the agents package registered with the core.
            foreach (string module in s_laterModules) RegisterModule(module);
            var declared = DeclaredOps().Where(o => o.Op >= ScNet.FirstGameOp).ToList();
            var registered = ScNet.Ops.ToList();
            string duplicates = Duplicates(declared);
            Test("C13", "every message number the core and the optional packages declare is declared once", duplicates.Length == 0 && declared.Count >= 28, $"{declared.Count} numbers in {declared.Select(o => o.Module).Distinct().Count()} modules ({string.Join(",", s_modules.Select(m => m.GetName().Name).DefaultIfEmpty("no optional package given"))}); twice: [{duplicates}]");
            Test("C13", "the game's own registration (ScNet.RegisterCore, then each package's loader call) refused nothing and replaced nothing", ScNet.Conflicts.Count == 0 && s_overwrites.Count == 0, $"refused [{string.Join("; ", ScNet.Conflicts)}], replaced [{string.Join("; ", s_overwrites)}]");
            Test("C13", "no number is handled in both directions, and every handled number is a declared one", registered.GroupBy(o => o.Op).All(g => g.Count() == 1) && registered.All(o => declared.Any(d => d.Op == o.Op)),
                $"{registered.Count} handlers; undeclared: [{string.Join(",", registered.Where(o => declared.All(d => d.Op != o.Op)).Select(o => o.Op))}]");
            string[] expected = ["ScNetGuns.ReceiveInput", "ScNetGuns.ReceiveShot", "ScNetGuns.ReceiveReload", "ScNetFeedback.ReceiveHit", "ScNetFeedback.ReceiveDamage", "ScNetFeedback.ReceiveHitSound", "ScNetFeedback.ReceiveNotice",
                "ScNetFeedback.ReceiveVoice", "ScNetMirror.ApplyRecords", "ScNetMirror.AnswerWant"];
            Test("C13", "the hit feedback, the gun input, the reload word and the record message each keep their own handler", expected.All(e => registered.Any(o => o.Handler.EndsWith(e, StringComparison.Ordinal)))
                && registered.Single(o => o.Op == ScNetFeedback.OpHit).Handler.EndsWith("ScNetFeedback.ReceiveHit", StringComparison.Ordinal) && registered.Single(o => o.Op == ScNetGuns.OpReload).Handler.EndsWith("ScNetGuns.ReceiveReload", StringComparison.Ordinal),
                string.Join(" ", registered.Select(o => $"{o.Op}:{o.Handler.Replace("Game.", "")}")));
            // A second initialisation, and a module that claims a taken number.
            int handlers = registered.Count;
            ScNet.RegisterCore(); ScNetGuns.Register(); ScNetFeedback.Register();
            Test("C13", "registering again (a second initialisation) is the same registration: nothing refused, nothing added", ScNet.Conflicts.Count == 0 && ScNet.Ops.Count() == handlers);
            ScNet.ClientHandler other = r => throw new InvalidOperationException("the wrong handler ran");
            ScNet.OnClient(ScNetFeedback.OpHit, other);
            ScNet.ServerHandler crossing = (from, player, r) => throw new InvalidOperationException("the wrong handler ran");
            ScNet.OnServer(ScNetMirror.OpRecords, crossing);
            bool refused = ScNet.Conflicts.Count == 2 && ScNet.Ops.Count() == handlers && ScNet.Ops.Single(o => o.Op == ScNetFeedback.OpHit).Handler.EndsWith("ScNetFeedback.ReceiveHit", StringComparison.Ordinal);
            Test("C13", "another handler for a taken number is refused and recorded, in the same direction and in the other one; the first handler stands", refused, string.Join(" | ", ScNet.Conflicts));
            ScNet.Conflicts.Clear();
        }

        // ================================================================ C14 (R07) what is and is not a hit confirmation
        static void C14() {
            int id = NewGun(s_ak, 30); int gun = Instance(s_ak, id);
            Give(0, gun); Back(Settle());
            int hits = ScNetFeedback.HitsReceived, dropped = ScNet.Dropped, unread = ScNet.Unread;
            // thirty shots at nothing, each confirmed
            int fired = 0;
            for (int i = 0; i < 30; i++) { ClientFrame(press: true); fired += RoundTrip().Shots; }
            Test("C14", "thirty shots at nothing, all confirmed by the server: the client is told of no hit, no message was dropped or left unread", fired == 30 && ScNetFeedback.HitsReceived == hits && ScNet.Dropped == dropped && ScNet.Unread == unread && Hud(A) == 0 && Pending() == 0,
                $"fired {fired}, hit messages {ScNetFeedback.HitsReceived - hits}, dropped {ScNet.Dropped - dropped}, unread {ScNet.Unread - unread}, HUD {Hud(A)}");
            // real results still arrive: a hit, a kill, a head hit
            var peer = Peer();
            foreach (int outcome in new[] { 1, 2, 3 }) { Enter(S, true); ScNetFeedback.Hit(S.Player[1], outcome, "匪徒", "AK-47", 12.5f); }
            Back(Take());
            Test("C14", "a hit, a kill and a head hit the server reports are each taken by the client", ScNetFeedback.HitsReceived == hits + 3 && ScNet.Dropped == dropped, $"taken {ScNetFeedback.HitsReceived - hits}");
            // what is not one
            hits = ScNetFeedback.HitsReceived;
            (string Name, Action<ScNetWriter> Write)[] wrong = [
                ("mpc3's shot confirmation (selection, count) under the hit number", w => w.Int(7).Int(3)),
                ("mpc3's confirmation with a large selection", w => w.Int(0x01020301).Int(1)),
                ("outcome 0", w => w.Byte(0).String("x").String("y").Float(1)),
                ("outcome 4", w => w.Byte(4).String("x").String("y").Float(1)),
                ("outcome 200", w => w.Byte(200).String("x").String("y").Float(1)),
                ("a hit with bytes after it", w => w.Byte(1).String("x").String("y").Float(1).Int(5)),
                ("a hit cut short", w => w.Byte(2).String("x")),
                ("a negative distance", w => w.Byte(1).String("x").String("y").Float(-3)),
                ("a distance of 1e9", w => w.Byte(2).String("x").String("y").Float(1e9f)),
                ("nothing at all", w => { }),
            ];
            var taken = new List<string>(); int before = ScNet.Dropped;
            foreach (var (name, write) in wrong) {
                Enter(S, true); ScNet.SendTo(peer, ScNetFeedback.OpHit, write); int was = ScNetFeedback.HitsReceived, drops = ScNet.Dropped;
                Back(Take());
                if (ScNetFeedback.HitsReceived != was || ScNet.Dropped != drops + 1) taken.Add(name);
            }
            Test("C14", $"{wrong.Length} payloads that are not a hit confirmation (a misrouted shot confirmation, outcomes outside 1-3, trailing or missing bytes, impossible distances) are each dropped, none shown", taken.Count == 0 && ScNetFeedback.HitsReceived == hits && ScNet.Dropped == before + wrong.Length,
                taken.Count == 0 ? $"{ScNet.Dropped - before} dropped" : "taken as hits or not dropped: " + string.Join("; ", taken));
            // the same for the messages around it
            before = ScNet.Dropped; int damage = ScNetFeedback.DamageMarksReceived, notices = ScNetFeedback.NoticesReceived, reloads = ScNetGuns.ReloadLog.Count;
            Enter(S, true);
            ScNet.SendTo(peer, ScNetFeedback.OpDamage, w => w.Vector3(Vector3.UnitX).Float(.5f).Int(1));
            ScNet.SendTo(peer, ScNetFeedback.OpNotice, w => w.String("x").Int(0).Byte(1));
            ScNet.SendTo(peer, ScNetGuns.OpReload, w => w.Int(0).Int(1).Byte(9).Int(0));
            ScNet.SendTo(peer, ScNetGuns.OpReload, w => w.Int(0).Int(1).Byte(1).Int(0).Int(0));
            ScNet.SendTo(peer, ScNetMirror.OpRecords, w => w.Int(0).Bool(true).Int(1).Int(2));
            ScNet.SendTo(peer, ScNetMirror.OpRecords, w => w.Int(0).Bool(false).Byte(1));
            Back(Take());
            Test("C14", "a damage mark, a notice, a reload word and a record message with wrong or extra bytes are dropped whole", ScNet.Dropped == before + 6 && ScNetFeedback.DamageMarksReceived == damage && ScNetFeedback.NoticesReceived == notices && ScNetGuns.ReloadLog.Count == reloads && Hud(A) == 0,
                $"dropped {ScNet.Dropped - before} of 6");
            Enter(S, true); Shoot(S, 1, out _, r => r.Rounds = 30); Back(Settle());
        }
        static ScNetPeer Peer() { Enter(S, true); return ScNet.PeerOf(S.Player[1]); }

        // ================================================================ C15 (R08/R09) confirmation by number: late, repeated, after a give-up
        static void C15() {
            int id = NewGun(s_ak, 30); int gun = Instance(s_ak, id);
            Give(2, gun); Back(Settle());
            int giveUps = ScNetGuns.GiveUps;
            // The record row of a shot and its confirmation cross frames with new shots: the client keeps firing while the
            // answers to its first shots are on their way, and never reads more or fewer rounds than server - unsettled.
            var held = new List<List<Packet>>(); var readings = new List<string>(); bool exact = true; int serverShots = 0;
            for (int frame = 0; frame < 12; frame++) {
                ClientFrame(press: frame < 8);                                   // eight taps, then four frames of nothing
                ToServer(S); serverShots += ServerFrame(); held.Add(Take());
                if (held.Count > 3) { Back(held[0]); held.RemoveAt(0); }         // the answers are three frames late
                int shown = Math.Min(frame + 1, 8);
                // (Record rows go out ten times a second, so the answers arrive in groups: whatever has arrived, the
                // readout is the record less the shots not settled yet, which is 30 less the shots shown.)
                exact &= Hud(A) == 30 - shown && MirrorRounds(A, id, s_ak) - Pending() == Hud(A) && MirrorRounds(A, id, s_ak) <= 30 && Pending() >= 0;
                readings.Add($"{Hud(A)}/{MirrorRounds(A, id, s_ak)}/{Pending()}");
            }
            foreach (var late in held) Back(late); Back(Settle());
            Test("C15", "eight taps with the answers three frames late and in groups: every frame the readout is 30 less the shots shown (the record less what is not settled yet), never jumping; it ends on 22 with nothing pending and no time-out", exact && serverShots == 8 && Hud(A) == 22 && Pending() == 0 && ServerRounds(id, s_ak) == 22 && ScNetGuns.GiveUps == giveUps,
                "HUD/record/pending per frame: " + string.Join(" ", readings));
            // A shot whose answer never comes is given up after PredictionGiveUp, loudly; then a new shot is shown and the old
            // answer arrives at last: it settles the old shot only.
            ClientFrame(press: true); ToServer(S); int lateShot = ServerFrame(); var lateAnswer = Take(); lateAnswer.AddRange(Settle());   // the answer is held back
            s_clock += ScNetGuns.PredictionGiveUp + .1; Enter(A, false); ScNetGuns.ClientTick();
            bool gaveUp = ScNetGuns.GiveUps == giveUps + 1 && Pending() == 0 && Hud(A) == 22;                        // the record still says 22: the row was held back too
            ClientFrame(press: true);                                                                                // a new shot, shown now
            int afterNew = Hud(A), pendingNew = Pending();
            Back(lateAnswer);                                                                                        // the old shot's row and word
            Test("C15", "a shot the server never answered is given up after 5 s (counted, logged); the old answer arriving after a new shot settles the old shot only: the new one stays pending and the readout is the server's 21 less it", lateShot == 1 && gaveUp && afterNew == 21 && pendingNew == 1
                && Pending() == 1 && Hud(A) == 20 && MirrorRounds(A, id, s_ak) == 21, $"gave up {ScNetGuns.GiveUps - giveUps}; before the late answer HUD {afterNew} pending {pendingNew}; after it HUD {Hud(A)} pending {Pending()} record {MirrorRounds(A, id, s_ak)}");
            var (newShot, _) = RoundTrip();
            Test("C15", "the new shot is then confirmed by its own answer: 20, nothing pending", newShot == 1 && Hud(A) == 20 && Pending() == 0 && ServerRounds(id, s_ak) == 20);
            // An answer for a former selection of the same gun never settles the present one.
            ClientFrame(press: true); ToServer(S); ServerFrame(); var former = Take(); former.AddRange(Settle());      // answer for selection n, held back
            foreach (var w in new[] { S, A, B }) w.Inventory(1).ActiveSlotIndex = 3;                                  // the gun is put away ...
            ClientFrame(); ToServer(S); ServerFrame(); Take();
            foreach (var w in new[] { S, A, B }) w.Inventory(1).ActiveSlotIndex = 2;                                  // ... and taken out again: a new selection
            ClientFrame(press: true); int shownNow = Pending();
            Back(former);
            Test("C15", "an answer for the former selection (the gun put away and taken out again) settles nothing of the present one", shownNow == 1 && Pending() == 1 && MirrorRounds(A, id, s_ak) == 19 && Hud(A) == 18, $"pending {Pending()}, record {MirrorRounds(A, id, s_ak)}, HUD {Hud(A)}");
            RoundTrip();
            Test("C15", "and the present selection's own answer settles it", Pending() == 0 && Hud(A) == 18 && ServerRounds(id, s_ak) == 18, $"pending {Pending()}, HUD {Hud(A)}, server {ServerRounds(id, s_ak)}");
            // The server fires a shot the client never showed (its release arrived late): the client follows the count, and
            // its next shown shot is a new number.
            Enter(S, true); Shoot(S, 1, out _); ScNetGuns.ServerShot(S.Player[1]); Back(Settle());
            Test("C15", "a shot only the server fired: the readout follows the record (17), nothing pending and nothing negative", Hud(A) == 17 && Pending() == 0 && MirrorRounds(A, id, s_ak) == 17);
            ClientFrame(press: true); int one = Pending(); RoundTrip();
            Test("C15", "the next shown shot is counted as a new one and confirmed once", one == 1 && Pending() == 0 && Hud(A) == 16 && ServerRounds(id, s_ak) == 16, $"pending before {one}, after {Pending()}, HUD {Hud(A)}");
        }

        // ================================================================ C16 (R10) the reload word on the wire
        static void C16() {
            int id = NewGun(s_ak, 2); int gun = Instance(s_ak, id);
            Give(4, gun); Back(Settle());
            ClientFrame(); ToServer(S); ServerFrame(); Take();
            ScNetGuns.ReloadLog.Clear();
            // the client's request travels with its input, as a counted event with an id
            Enter(A, false); int request = ScNetGuns.RequestReload();
            ClientFrame(); ToServer(S);
            Enter(S, true);
            bool asked = Remote.ReloadId == request && Remote.TakeReload() && !Remote.TakeReload();
            Test("C16", "a reload the client starts reaches the server once, with its id, in the client's input", request != 0 && asked, $"request {request}, server has {Remote.ReloadId}");
            // the server's words for it
            foreach (var phase in new[] { ScNetGuns.ReloadPhase.Accepted, ScNetGuns.ReloadPhase.Completed }) { Enter(S, true); ScNetGuns.ReloadResult(S.Player[1], request, phase); }
            Back(Take());
            Test("C16", "the server's words (accepted, completed) reach that client in order, with the server's rounds", ScNetGuns.ReloadLog.Count == 2 && ScNetGuns.ReloadLog[0] == (request, ScNetGuns.ReloadPhase.Accepted, 2) && ScNetGuns.ReloadLog[1].Phase == ScNetGuns.ReloadPhase.Completed,
                string.Join(" ", ScNetGuns.ReloadLog.Select(l => $"#{l.Id} {l.Phase} {l.Rounds}")));
            // a request for a gun the server does not hold in that slot is answered: refused
            ScNetGuns.ReloadLog.Clear();
            Enter(S, true); S.Inventory(1).ActiveSlotIndex = 5;
            Enter(A, false); int stale = ScNetGuns.RequestReload(); ClientFrame(); ToServer(S); Back(Take());
            Test("C16", "a reload asked for a gun the server does not hold in hand is refused by name, not left unanswered", ScNetGuns.ReloadLog.Count == 1 && ScNetGuns.ReloadLog[0] == (stale, ScNetGuns.ReloadPhase.Refused, 0), string.Join(" ", ScNetGuns.ReloadLog.Select(l => $"#{l.Id} {l.Phase}")));
            Enter(S, true); S.Inventory(1).ActiveSlotIndex = 4;
            // a request whose trigger lease ran out before the server took it
            ScNetGuns.ReloadLog.Clear();
            Enter(A, false); int expired = ScNetGuns.RequestReload(); ClientFrame(trigger: true); ToServer(S);
            s_clock += ScNetGuns.InputLease + .1;
            Enter(S, true); Remote.Expire(ScNet.Now); ScNetGuns.ServerRefuseDropped(S.Player[1]); Back(Take());
            Test("C16", "a reload request dropped with an expired input lease is refused, not forgotten", ScNetGuns.ReloadLog.Count == 1 && ScNetGuns.ReloadLog[0] == (expired, ScNetGuns.ReloadPhase.Refused, 0), string.Join(" ", ScNetGuns.ReloadLog.Select(l => $"#{l.Id} {l.Phase}")));
            ClientFrame(); ToServer(S); ServerFrame(); Take();
        }
    }
}
