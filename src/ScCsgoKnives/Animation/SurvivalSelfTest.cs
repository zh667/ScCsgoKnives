using Engine;
using GameEntitySystem;

namespace Game;

public static class SurvivalSelfTest {
    sealed class Inventory : IInventory {
        public Project Project => null;
        public int SlotsCount => 8;
        public int VisibleSlotsCount { get; set; } = 8;
        public int ActiveSlotIndex { get; set; }
        public readonly int[] Values = new int[8], Counts = new int[8];
        public int RefuseSlot = -1;
        public int GetSlotValue(int i) => Values[i];
        public int GetSlotCount(int i) => Counts[i];
        public int GetSlotCapacity(int i, int v) => i == 0 ? 1 : 40;
        public int GetSlotProcessCapacity(int i, int v) => 0;
        public void AddSlotItems(int i, int v, int n) { if (n == 0) return; if (Counts[i] > 0 && Values[i] != v) throw new InvalidOperationException("mixed slot"); Values[i] = v; Counts[i] += n; }
        public int RemoveSlotItems(int i, int n) { if (i == RefuseSlot) return 0; n = Math.Min(n, Counts[i]); Counts[i] -= n; return n; }
        public void ProcessSlotItems(int i, int v, int count, int process, out int result, out int resultCount) { result = v; resultCount = 0; }
        public void DropAllItems(Vector3 position) => Array.Clear(Counts);
    }
    public static void Run(Action<string, bool, string> check) {
        ScPolishSelfTest.Run(check);
        const int ammo = 900;
        Inventory Setup(int rounds, int count) {
            var i = new Inventory(); i.AddSlotItems(0, Terrain.MakeBlockValue(512, 0, GunSpec.MakeData(0, rounds)), 1); i.AddSlotItems(1, ammo, count); return i;
        }
        ScReloadTransaction Tx(Inventory i, int cost = 1, int capacity = 30) => new(i, 0, i.GetSlotValue(0), ammo, cost, capacity);
        int R(Inventory i) => GunSpec.GetRounds(Terrain.ExtractData(i.GetSlotValue(0)));
        void Test(string name, Func<bool> test) { try { check("survival/" + name, test(), name); } catch (Exception e) { check("survival/" + name, false, e.ToString()); } }
        Test("cancel-before-drop", () => { var i = Setup(12, 2); var t = Tx(i); t.Cancel(); return !t.Discard() && !t.InsertMagazine() && R(i) == 12 && i.Counts[1] == 2; });
        Test("cancel-after-drop-preserves-rounds", () => { var i = Setup(12, 2); var t = Tx(i); t.Discard(); t.Cancel(); return !t.InsertMagazine() && R(i) == 12 && i.Counts[1] == 2; });
        Test("insert-once", () => { var i = Setup(12, 2); var t = Tx(i); return t.Discard() && !t.Discard() && t.InsertMagazine() && !t.InsertMagazine() && R(i) == 30 && i.Counts[1] == 1; });
        Test("unavailable-after-drop", () => { var i = Setup(12, 1); var t = Tx(i); t.Discard(); i.RemoveSlotItems(1, 1); return !t.InsertMagazine() && R(i) == 12 && i.Counts[1] == 0; });
        Test("same-type-swap-epoch", () => { var i = Setup(12, 2); var t = Tx(i); ScInventoryTransaction.Changed(i); return !t.Discard() && R(i) == 12 && i.Counts[1] == 2; });
        Test("slot-switch", () => { var i = Setup(12, 2); var t = Tx(i); i.ActiveSlotIndex = 1; return !t.Discard() && R(i) == 12; });
        Test("negev-paid-150", () => { var i = Setup(127, 5); var t = Tx(i, 5, 150); return t.Discard() && t.InsertMagazine() && R(i) == 150 && i.Counts[1] == 0; });
        Test("negev-insufficient", () => { var i = Setup(0, 4); var t = Tx(i, 5, 150); t.Discard(); return !t.InsertMagazine() && R(i) == 0 && i.Counts[1] == 4; });
        Test("tube-preserves-rounds", () => { var i = Setup(3, 2); var t = Tx(i, 1, 8); bool added = t.InsertShell() && t.InsertShell(); t.Cancel(); return added && !t.InsertShell() && R(i) == 5 && i.Counts[1] == 0; });
        Test("mag7-discards-remainder", () => { var i = Setup(3, 5); var t = Tx(i, 5, 5); return t.Discard() && t.InsertMagazine() && R(i) == 5 && i.Counts[1] == 0; });
        Test("creative-free", () => { var i = Setup(0, 0); var t = Tx(i, 0); return t.Discard() && t.InsertMagazine() && R(i) == 30 && i.Counts[1] == 0; });
        Test("reload-world-mode-change",()=>{var i=Setup(0,1);return Tx(i,0).ModeMatches(true) && !Tx(i,0).ModeMatches(false) && Tx(i,1).ModeMatches(false) && !Tx(i,1).ModeMatches(true);});
        Test("partial-removal-rollback", () => { var i = Setup(0, 2); i.AddSlotItems(2, ammo, 3); i.RefuseSlot = 2; var t = Tx(i, 5, 150); t.Discard(); return !t.InsertMagazine() && R(i) == 0 && i.Counts[1] == 2 && i.Counts[2] == 3; });
        Test("death-before-event", () => { var i = Setup(12, 2); var t = Tx(i); i.DropAllItems(default); return !t.Discard() && i.Counts[0] == 0 && i.Counts[1] == 0; });
        Test("saved-commits-only", () => {
            var i = Setup(12, 2); var t = Tx(i); t.Discard();
            var loaded = Setup(R(i), i.Counts[1]); var next = Tx(loaded);
            return R(loaded) == 12 && next.Discard() && next.InsertMagazine() && R(loaded) == 30 && loaded.Counts[1] == 1;
        });
        Test("craft-full-inventory-no-charge", () => { var i = Setup(0, 5); for (int n = 2; n < 8; n++) i.AddSlotItems(n, 901, 1); return !ScWeaponCrafting.TryCraft(i, 902, new Dictionary<int,int> { [ammo] = 3 }) && i.Counts[1] == 5; });
        Test("craft-atomic-success", () => { var i = Setup(0, 5); return ScWeaponCrafting.TryCraft(i, 902, new Dictionary<int,int> { [ammo] = 3 }) && i.Counts[1] == 2 && i.Values[2] == 902 && i.Counts[2] == 1; });
        Test("craft-rollback", () => { var i = Setup(0, 5); i.AddSlotItems(2, 901, 1); i.RefuseSlot = 2; return !ScWeaponCrafting.TryCraft(i, 902, new Dictionary<int,int> { [ammo] = 3, [901] = 1 }) && i.Counts[1] == 5 && i.Counts[2] == 1; });
        Test("craft-all57-low-level", () => ScWeaponCrafting.All.Length == 57 && ScWeaponCrafting.All.All(e => e.Level >= 1 && e.Level <= 6 && e.B > 0 && e.H == 1));
        Test("knives-shared-recovery", () => { var k = new ScKnifeStrike(); return k.Start(0, true) && !k.Start(.1, false) && !k.TakeHit(.1) && k.TakeHit(.3) && !k.TakeHit(.3) && !k.Start(.9, false) && k.Start(1, false); });
        Test("knives-cancel-keeps-recovery", () => { var k = new ScKnifeStrike(); k.Start(0, true); k.Cancel(); return !k.TakeHit(.4) && !k.Start(.5, false) && k.Start(1, false); });
        foreach (string knife in CsmcKnifeRig.FrozenKnifeOrder)
            foreach (string alias in new[] { "slash1", "slash2", "stab", "stabHit", "slashHit1", "slashHit2" })
                {
                    bool valid = Cs2Rig.HasAlias(knife, alias);
                    foreach (float time in new[] { 0f, .15f, .30f, .6f }) {
                        var pose = Cs2Rig.Sample(knife, alias, time);
                        foreach (var mesh in new[] { Cs2SkinnedMesh.Arms, Cs2SkinnedMesh.Weapon(knife) }) {
                            valid &= mesh is not null && mesh.SetPose(pose, Cs2Placement.Placement());
                            if (mesh is not null) { mesh.Skin(); valid &= mesh.Skinned.All(v => float.IsFinite(v.Position.X) && float.IsFinite(v.Position.Y) && float.IsFinite(v.Position.Z)); }
                        }
                    }
                    check($"survival/knife/{knife}/{alias}", valid, "CS2 weapon and real arms skin at 0/.15/.30/.6 seconds");
                }
        foreach (var gun in GunSpec.All) {
            Test("damage/" + gun.Name, () => ScSurvivalBalance.Power(gun.Name) > 0 && Math.Abs(ScSurvivalBalance.PelletPower(gun, 0) * gun.Pellets - ScSurvivalBalance.Power(gun.Name)) < .0001f
                && ScSurvivalBalance.Falloff(gun, 64) > 0 && ScSurvivalBalance.Falloff(gun, 64) <= 1);
        }
        Test("animal-shot-targets", () => Math.Ceiling(70 / ScSurvivalBalance.Power("ak47")) == 5 && Math.Ceiling(70 / ScSurvivalBalance.Power("awp")) == 2);
        Test("gun-power-x1.5", () => ScSurvivalBalance.GunPowerMultiplier == 1.5f && ScSurvivalBalance.Power("ak47") == 15 && ScSurvivalBalance.Power("awp") == 57
            && ScSurvivalBalance.Power("taser") == 27 && ScSurvivalBalance.Power("glock18") == 10.5f && ScSurvivalBalance.BasePower("ak47") == 10
            && GunSpec.All.All(g => Math.Abs(ScSurvivalBalance.Power(g.Name) - ScSurvivalBalance.BasePower(g.Name) * 1.5f) < .0001f));
        Test("knife-range-2.2-1.8", () => ScKnifeStrike.Range(false) == 2.2f && ScKnifeStrike.Range(true) == 1.8f && ScKnifeStrike.Power(false) == 7 && ScKnifeStrike.Power(true) == 12);
        Test("throw-speed-inherits-velocity", () => {
            Vector3 d = ScGrenadeBallistics.Direction(Vector3.UnitZ, false);
            Vector3 stand = ScGrenadeBallistics.LaunchVelocity(d, Vector3.Zero, false), run = ScGrenadeBallistics.LaunchVelocity(d, Vector3.UnitZ * 4, false), back = ScGrenadeBallistics.LaunchVelocity(d, -Vector3.UnitZ * 4, false);
            Vector3 weak = ScGrenadeBallistics.LaunchVelocity(ScGrenadeBallistics.Direction(Vector3.UnitZ, true), Vector3.Zero, true);
            return Math.Abs(stand.Length() - 20) < .001f && run.Z > stand.Z && back.Z < stand.Z && Math.Abs(run.Z - stand.Z - 5) < .001f
                && Math.Abs(weak.Length() - 10) < .001f && d.Y > 0 && ScGrenadeBallistics.Fuse(3) == 2 && ScGrenadeBallistics.Fuse(2) == 1.5f;
        });
        Test("throw-step-clamped", () => ScGrenadeBallistics.Step(.016f) == .016f && ScGrenadeBallistics.Step(3) == .5f && ScGrenadeBallistics.Step(float.NaN) == 0);
        Test("smoke-settles-before-pop", () => {
            var s = new ScGrenadeState { Kind = 2, Remaining = 0, Age = 5 };
            bool flying = !ScGrenadeBallistics.Settled(s); s.Grounded = true; s.Rested = .05f; bool touching = !ScGrenadeBallistics.Settled(s);
            s.Rested = .2f; bool rested = ScGrenadeBallistics.Settled(s); s.Grounded = false; bool lifted = !ScGrenadeBallistics.Settled(s);
            var l = ScGrenadeState.Load(new ScGrenadeState { Kind = 2, Grounded = true, Rested = .3f, Remaining = 0 }.Save());
            return flying && touching && rested && lifted && l.Grounded && l.Rested == .3f && ScGrenadeState.Load(new ScGrenadeState { Kind = 2, Rested = -1 }.Save()) is null;
        });
        Test("after-throw-previous-slot", () => ScGrenadeBallistics.FollowUpSlot(6, 4, 0) == 0   // held slot 0 before the grenade: back to 0, whatever it holds
            && ScGrenadeBallistics.FollowUpSlot(6, 4, 5) == 5 && ScGrenadeBallistics.FollowUpSlot(6, 4, -1) == -1  // no earlier slot known: stay
            && ScGrenadeBallistics.FollowUpSlot(6, 4, 4) == -1 && ScGrenadeBallistics.FollowUpSlot(6, 4, 6) == -1); // thrown slot itself / out of range: stay
        Test("slot-history-dwell", () => {
            var h = new ScSlotHistory(); h.Observe(0, 0);                       // knife held from t=0
            h.Observe(1, 5); h.Observe(2, 5.05); h.Observe(3, 5.1);              // wheel through 1 and 2 to the grenade in slot 3
            bool wheel = h.Current == 3 && h.Previous == 0;
            h.Observe(0, 9); h.Observe(3, 9.01);                                  // blinked to 0 and back: the grenade slot itself is not "previous", 0 still is
            bool blink = h.Previous == 0 && h.Current == 3;
            h.Observe(5, 20); h.Observe(3, 21);                                   // held slot 5 for a second, then the grenade
            bool held = h.Previous == 5 && h.Current == 3;
            var fresh = new ScSlotHistory(); fresh.Observe(3, 0);                 // world loaded holding the grenade
            return wheel && blink && held && fresh.Previous == -1;
        });
        Test("smoke-heated-by-fire", () => {
            var fire = new ScGrenadeState { Kind = 3, Effect = true, Remaining = 5, Position = Vector3.Zero };
            ScGrenadeState Smoke(Vector3 at, bool popped = false) => new() { Kind = 2, Effect = popped, Position = at };
            bool inside = ScFireArea.Heats(fire, Smoke(new Vector3(1, .5f, 1))) && ScFireArea.Heats(fire, Smoke(new Vector3(2.4f, 1.1f, 0)));
            bool outside = !ScFireArea.Heats(fire, Smoke(new Vector3(3.5f, 0, 0))) && !ScFireArea.Heats(fire, Smoke(new Vector3(0, 2, 0))) && !ScFireArea.Heats(fire, Smoke(new Vector3(0, -1, 0)));
            bool onlyOnce = !ScFireArea.Heats(fire, Smoke(Vector3.Zero, popped: true));
            fire.Remaining = 0; bool dead = !ScFireArea.Heats(fire, Smoke(Vector3.Zero));
            bool notDecoy = !ScFireArea.Heats(new ScGrenadeState { Kind = 4, Effect = true, Remaining = 5 }, new ScGrenadeState { Kind = 5 });
            return inside && outside && onlyOnce && dead && notDecoy;
        });
        Test("smoke-he-opening", () => {
            var d = new ScSmokeDisturbance { Center = Vector3.Zero };
            bool hold = d.Clearing(Vector3.Zero) == 1 && d.Clearing(new Vector3(2.4f, 0, 0)) == 1 && Math.Abs(d.Clearing(new Vector3(2.75f, 0, 0)) - .5f) < .001f && d.Clearing(new Vector3(3, 0, 0)) == 0;
            d.Remaining = 1; bool refilling = Math.Abs(d.Clearing(Vector3.Zero) - .5f) < .001f;
            d.Remaining = 0; bool gone = d.Clearing(Vector3.Zero) == 0 && !d.Active;
            var l = ScSmokeDisturbance.Load(new ScSmokeDisturbance { Center = new Vector3(1, 2, 3), Remaining = 2.2f }.Save());
            bool saved = l is not null && l.Center == new Vector3(1, 2, 3) && l.Remaining == 2.2f && ScSmokeDisturbance.Load(new ScSmokeDisturbance { Remaining = 0 }.Save()) is null
                && ScSmokeDisturbance.Load(new ScSmokeDisturbance { Remaining = 99 }.Save()) is null;
            return hold && refilling && gone && saved && ScSmokeDisturbance.Total == 3.5f && ScSmokeDisturbance.Clearing(null, Vector3.Zero) == 0;
        });
        Test("smoke-opening-opens-sight", () => {
            var smoke = new ScGrenadeState { Kind = 2, Effect = true, Age = 2, Remaining = 12, Position = -Vector3.UnitY * 1.5f };
            Vector3 a = new(-5, 0, 0), b = new(5, 0, 0);
            var opening = new ScSmokeDisturbance { Center = Vector3.Zero };
            float intact = ScSmokeVolume.EffectiveInsideLength(a, b, smoke, null), open = ScSmokeVolume.EffectiveInsideLength(a, b, smoke, [opening]);
            bool blockedBefore = ScSmokeVolume.Blocks([smoke], a, b) && Math.Abs(intact - 6) < .001f;
            bool openNow = open <= .5f + 1e-3f /* only the 0.5 m soft rim on each side is left */ && !ScSmokeVolume.Blocks([smoke], a, b, null, [opening]) && ScSmokeVolume.Density(smoke, Vector3.Zero, [opening]) == 0 && ScSmokeVolume.Density(smoke, Vector3.Zero) == 1;
            var side = new ScSmokeDisturbance { Center = new Vector3(0, 0, 4) }; // opening beside the path: sight still blocked
            bool sideBlocked = ScSmokeVolume.Blocks([smoke], a, b, null, [side]);
            opening.Remaining = 0; bool refilled = ScSmokeVolume.Blocks([smoke], a, b, null, [opening]) && Math.Abs(ScSmokeVolume.EffectiveInsideLength(a, b, smoke, [opening]) - 6) < .001f;
            return blockedBefore && openNow && sideBlocked && refilled;
        });
        Test("third-person-arm-maths", () => {
            // Closed-form angles must invert the engine's own rotation matrices for a range of directions.
            bool roundTrip = true;
            foreach (float raise in new[] { -.5f, 0f, .4f, 1.05f, 1.5f }) foreach (float swing in new[] { -.6f, -.2f, 0f, .35f, .7f }) {
                Vector3 d = ScThirdPersonMath.ArmDirection(new Vector2(raise, swing));
                Vector2 back = ScThirdPersonMath.AnglesToward(d);
                roundTrip &= Math.Abs(back.X - raise) < 1e-3f && Math.Abs(back.Y - swing) < 1e-3f && Math.Abs(d.Length() - 1) < 1e-4f;
            }
            Vector3 rest = ScThirdPersonMath.ArmDirection(Vector2.Zero), forward = ScThirdPersonMath.ArmDirection(new Vector2(MathF.PI / 2, 0)), inward = ScThirdPersonMath.ArmDirection(new Vector2(0, .5f));
            bool axes = (rest + Vector3.UnitZ).Length() < 1e-4f && (forward - Vector3.UnitY).Length() < 1e-4f && inward.X < -.4f; // body frame: -Z down, +Y forward, +X right
            Matrix body = Matrix.CreateScale(.0241f) * Matrix.CreateRotationX(-MathF.PI / 2) * Matrix.CreateRotationY(.7f) * Matrix.CreateTranslation(3, 0, -2);
            Matrix hand = ScThirdPersonMath.HandAbsolute(new Vector3(7.48f, .12f, 51.5f), new Vector2(1.05f, .35f), body);
            Vector3 fist = Vector3.Transform(ScThirdPersonMath.HandEndLocal(true), hand), shoulder = hand.Translation;
            Vector2 solved = ScThirdPersonMath.AnglesToward(ScThirdPersonMath.BodyDirection(shoulder, fist, body));
            bool ik = Math.Abs(solved.X - 1.05f) < .02f && Math.Abs(solved.Y - .35f) < .12f; // the fist sits 2.41 units off the bone axis, hence the swing tolerance
            Matrix world = ScThirdPersonMath.WeaponWorld(new Vector3(.02f, -.05f, .1f), fist, ScThirdPersonMath.AimDirection(body.Forward, .3f), Vector3.UnitY);
            bool grip = (Vector3.Transform(new Vector3(.02f, -.05f, .1f), world) - fist).Length() < 1e-3f && Vector3.Dot(Vector3.TransformNormal(-Vector3.UnitZ, world), ScThirdPersonMath.AimDirection(body.Forward, .3f)) > .999f;
            bool aim = Math.Abs(ScThirdPersonMath.AimDirection(-Vector3.UnitZ, .5f).Y - MathF.Sin(.5f)) < 1e-4f && ScThirdPersonMath.AimDirection(-Vector3.UnitZ, 0) == -Vector3.UnitZ;
            return roundTrip && axes && ik && grip && aim;
        });
        Test("third-person-stances", () => ScThirdPerson.StanceForGun("ak47") == ScThirdPersonStance.Rifle && ScThirdPerson.StanceForGun("glock18") == ScThirdPersonStance.Pistol
            && ScThirdPerson.StanceForGun("taser") == ScThirdPersonStance.Pistol && ScThirdPerson.StanceForGun("elite") == ScThirdPersonStance.Dual && ScThirdPerson.StanceForGun("awp") == ScThirdPersonStance.Rifle
            && GunSpec.All.All(g => ScThirdPerson.StanceForGun(g.Name) is not null));
        foreach (string asset in new[] { "ak47", "awp", "glock18", "elite", "taser", "m249", "nova", "karambit", "grenade_hegrenade", "grenade_molotov" }) {
            Test("third-person-weapon/" + asset, () => {
                var w = ScThirdPersonWeapon.For(asset);
                if (w is null || w.Vertices == 0 || !w.HasRightGrip) throw new InvalidOperationException(w is null ? "no weapon" : $"vertices {w.Vertices} rightGrip {w.HasRightGrip}");
                Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
                foreach (var g in w.Groups) foreach (var v in g.Mesh.Vertices) { lo = Vector3.Min(lo, v.Position); hi = Vector3.Max(hi, v.Position); }
                Vector3 size = hi - lo; bool metres = size.Length() > .1f && size.Length() < 2f;                         // a real-scale weapon, not a unit cube
                bool gripInside = w.GripRight.X > lo.X - .1f && w.GripRight.X < hi.X + .1f && w.GripRight.Z > lo.Z - .15f && w.GripRight.Z < hi.Z + .15f;
                bool forward = !GunSpec.All.Any(g => g.Name == asset) || w.Muzzle.Z < w.GripRight.Z - .1f;                 // the muzzle is ahead (-Z) of the grip on every gun
                bool ok = metres && gripInside && forward && (asset == "grenade_hegrenade" || asset == "karambit" || w.HasLeftGrip);
                if (!ok) throw new InvalidOperationException($"size {size} lo {lo} hi {hi} gripR {w.GripRight} gripL {w.GripLeft} ({w.HasLeftGrip}) muzzle {w.Muzzle} vertices {w.Vertices}");
                return true;
            });
        }
        Test("headshot-geometry", () => {
            // A human-sized target in a vanilla-like bone space: inch units (0.0254) and a yawed, translated root.
            Matrix bone = Matrix.CreateScale(.0254f) * Matrix.CreateRotationY(1.1f) * Matrix.CreateTranslation(new Vector3(10, 3, -7));
            var body = new ScPartBox(new BoundingBox(new Vector3(-12, 0, -8), new Vector3(12, 48, 8)), bone, false);
            var head = new ScPartBox(new BoundingBox(new Vector3(-8, 48, -8), new Vector3(8, 64, 8)), bone, true);
            var parts = new[] { body, head };
            Vector3 headCentre = Vector3.Transform(new Vector3(0, 56, 0), bone), bodyCentre = Vector3.Transform(new Vector3(0, 24, 0), bone);
            var fromAbove = ScHeadshot.Resolve(parts, headCentre + Vector3.UnitY * 5, -Vector3.UnitY, 64);          // top-down: head first
            var fromBelow = ScHeadshot.Resolve(parts, bodyCentre - Vector3.UnitY * 5, Vector3.UnitY, 64);           // body shields the head
            var insideHead = ScHeadshot.Resolve(parts, headCentre, Vector3.UnitX, 64);
            var miss = ScHeadshot.Resolve(parts, headCentre + new Vector3(3, 0, 0), -Vector3.UnitY, 64);
            var tooFar = ScHeadshot.Resolve(parts, headCentre + Vector3.UnitY * 5, -Vector3.UnitY, 1);
            var twice = ScHeadshot.Resolve(parts, headCentre + Vector3.UnitY * 5, -Vector3.UnitY * 2, 64);            // unnormalised: parameter halves
            float headTop = 8 * .0254f; // half height of the head box in metres
            return fromAbove.Part == ScHitPart.Head && Math.Abs(fromAbove.Distance - (5 - headTop)) < .01f
                && fromBelow.Part == ScHitPart.Body && Math.Abs(fromBelow.Distance - (5 - 24 * .0254f)) < .01f
                && insideHead.Part == ScHitPart.Head && insideHead.Distance == 0
                && miss.Part == ScHitPart.Unknown && miss.Distance == -1 && tooFar.Part == ScHitPart.Unknown
                && twice.Part == ScHitPart.Head && Math.Abs(twice.Distance - (5 - headTop) / 2) < .01f;
        });
        Test("headshot-bind-pose-compose", () => {
            var bones = new List<(Matrix, int)> { (Matrix.CreateScale(.0254f), -1), (Matrix.CreateTranslation(0, 50, 0), 0), (Matrix.CreateTranslation(0, 0, 10), 1) };
            var a = ScHeadshot.ComposeBindPose(bones); var b = ScHeadshot.ComposeBindPose(bones, 2);
            var shrunk = new ScHeadRule(["Head"], new Vector3(.5f, 1, 1)).Apply(new BoundingBox(new Vector3(-4, 0, 0), new Vector3(4, 2, 2)));
            return (a[1].Translation - new Vector3(0, 1.27f, 0)).Length() < 1e-4f && (a[2].Translation - new Vector3(0, 1.27f, .254f)).Length() < 1e-4f
                && (b[2].Translation - new Vector3(0, 2.54f, .508f)).Length() < 1e-4f
                && shrunk.Min == new Vector3(-2, 0, 0) && shrunk.Max == new Vector3(2, 2, 2);
        });
        Test("headshot-rules", () => {
            bool vanilla = ScHeadRules.VanillaHeadModels.Length == 32 && ScHeadRules.VanillaHeadModels.All(ScHeadRules.IsRegistered)
                && ScHeadRules.For("Models/Cow", false, false) == ScHeadRule.Default && ScHeadRules.For("Models/Bass", false, false) is null
                && ScHeadRules.For("Models/ModCreature", true, true) == ScHeadRule.Default && ScHeadRules.For("Models/ModCreature", true, false) is null
                && ScHeadRules.For("Models/ModCreature", false, true) is null && ScHeadRules.For(null, false, true) is null;
            ScHeadRules.Register("Models/SelfTestDisabled", null);
            bool disabled = ScHeadRules.IsRegistered("Models/SelfTestDisabled") && ScHeadRules.For("Models/SelfTestDisabled", true, true) is null;
            ScHeadRules.Register("Models/SelfTestCustom", new ScHeadRule(["Skull"], Vector3.One));
            bool custom = ScHeadRules.For("Models/SelfTestCustom", false, false).IsHead("Skull") && !ScHeadRules.For("Models/SelfTestCustom", false, false).IsHead("Head");
            return vanilla && disabled && custom && ScHeadshot.MultiplierFor(GunSpec.All.First(g => g.Name == "taser")) == 1
                && ScHeadshot.MultiplierFor(GunSpec.All.First(g => g.Name == "ak47")) == 2 && ScHeadshot.Multiplier == 2;
        });
        Test("hit-marker-priority", () => {
            var f = new ScCombatFeedback();
            f.Record(1, "a", "ak47", 5, 10); f.Record(3, "b", "ak47", 5, 10); bool headWins = f.LastKind == 3 && f.LastAt == 10;
            f.Record(1, "c", "ak47", 5, 10); bool hitDoesNotDemote = f.LastKind == 3;
            f.Record(2, "d", "ak47", 5, 10); bool killWins = f.LastKind == 2 && f.KillAt == 10;
            f.Record(3, "e", "ak47", 5, 10); bool killHolds = f.LastKind == 2;
            f.Record(3, "f", "ak47", 5, 10.2); bool laterReplaces = f.LastKind == 3 && f.LastAt == 10.2 && f.KillAt == 10;
            f.Record(0, "g", "ak47", 5, 11); bool nothingOnMiss = f.LastKind == 3 && f.LastAt == 10.2;
            return headWins && hitDoesNotDemote && killWins && killHolds && laterReplaces && nothingOnMiss && ScCombatFeedback.HeadshotOutcome == 3
                && ScCombatFeedback.HeadshotColor is { R: 255, G: 210, B: 50 };
        });
        Test("smoke-neutral-grey", () => ScGrenadeVisuals.Smoke(new() { Kind = 2, Effect = true, Age = 2, Remaining = 13 }, 0).All(sp => sp.Color.R == sp.Color.G && sp.Color.G == sp.Color.B)
            && ScGrenadeVisuals.SmokeInside(1) is { R: 128, G: 128, B: 128, A: 255 } && ScGrenadeVisuals.SmokeInside(0).A == 0);
        Test("gun-durability-levels", () => {
            int fresh = GunSpec.MakeData(0, 30);
            bool layout = GunSpec.GetDurability(fresh) == 7 && GunSpec.GetDurability(GunSpec.SetRounds(GunSpec.SetDurability(fresh, 3), 5)) == 3
                && GunSpec.GetRounds(GunSpec.SetDurability(fresh, 3)) == 30 && ScGunDurability.Percent(7) == 100 && ScGunDurability.Percent(1) == 14 && ScGunDurability.Percent(0) == 0
                && ScGunDurability.IsLow(GunSpec.SetDurability(fresh, 1)) && !ScGunDurability.IsLow(GunSpec.SetDurability(fresh, 2)) && ScGunDurability.IsBroken(GunSpec.SetDurability(fresh, 0)) && !ScGunDurability.IsLow(GunSpec.SetDurability(fresh, 0));
            bool classes = GunSpec.All.All(g => ScGunDurability.ShotsPerLevel(g.Name) >= 1) && ScGunDurability.ShotsPerLevel("ak47") == 214 && ScGunDurability.ShotsPerLevel("awp") == 29
                && ScGunDurability.ShotsPerLevel("taser") == 14 && ScGunDurability.ShotsPerLevel("negev") == 571 && ScGunDurability.ShotsPerLevel("nova") == 43 && ScGunDurability.ShotsPerLevel("elite") == 171;
            int shots = 0, level = 7;
            for (int i = 0; i < 213; i++) level = ScGunDurability.Wear("ak47", level, ref shots);
            bool holds = level == 7 && shots == 213;
            level = ScGunDurability.Wear("ak47", level, ref shots); bool drops = level == 6 && shots == 0;
            level = 1; shots = 213; level = ScGunDurability.Wear("ak47", level, ref shots); bool breaks = level == 0;
            bool stays = ScGunDurability.Wear("ak47", 0, ref shots) == 0;
            return layout && classes && holds && drops && breaks && stays;
        });
        Test("gun-repair-cost", () => {
            var ak = ScWeaponCrafting.All.First(e => e.Name == "ak47"); var glock = ScWeaponCrafting.All.First(e => e.Name == "glock18");
            var m249 = ScWeaponCrafting.All.First(e => e.Name == "m249"); var knife = ScWeaponCrafting.All.First(e => e.Knife);
            int blank = ScWeaponRepair.Blank, mech = ScWeaponRepair.Mechanism;
            var full = ScWeaponRepair.FullCost(ak); var pistol = ScWeaponRepair.FullCost(glock); var mg = ScWeaponRepair.FullCost(m249);
            bool fullOk = full[blank] == 1 && full[mech] == 1 && full.Count == 2 && pistol[blank] == 1 && !pistol.ContainsKey(mech) && mg[blank] == 2 && mg[mech] == 1
                && ScWeaponRepair.FullCost(knife).Count == 0 && ScWeaponCrafting.All.Where(e => !e.Knife).All(e => ScWeaponRepair.FullCost(e).Values.Sum() >= 1
                    && ScWeaponRepair.FullCost(e).Values.Sum() <= Math.Max(1, (e.B + e.M + e.H + e.O) * 3 / 10 + 1));
            var none = ScWeaponRepair.Cost(ak, 7); var one = ScWeaponRepair.Cost(ak, 6); var broken = ScWeaponRepair.Cost(ak, 0); var half = ScWeaponRepair.Cost(m249, 4);
            return fullOk && none.Count == 0 && one[blank] == 1 && one[mech] == 1 && broken[blank] == 1 && broken[mech] == 1 && half[blank] == 1 && half[mech] == 1;
        });
        Test("gun-repair-transaction", () => {
            int blank = 950, mech = 951; // material item values stand in for the blocks, which are not registered in the headless self-test
            int worn = Terrain.MakeBlockValue(512, 0, GunSpec.Pack(0, 12, false, 2));
            var i = new Inventory(); i.AddSlotItems(0, worn, 1); i.AddSlotItems(2, blank, 3); i.AddSlotItems(3, mech, 1);
            var c = ScWeaponRepair.Candidates(i, 512).Single();
            var cost = new Dictionary<int, int> { [blank] = 1, [mech] = 1 };
            bool picked = c.Slot == 0 && c.Level == 2;
            bool ok = ScWeaponRepair.TryRepair(i, c, cost) && GunSpec.GetDurability(Terrain.ExtractData(i.Values[0])) == 7 && GunSpec.GetRounds(Terrain.ExtractData(i.Values[0])) == 12
                && i.Counts[2] == 2 && i.Counts[3] == 0 && !ScWeaponRepair.Candidates(i, 512).Any();
            bool again = !ScWeaponRepair.TryRepair(i, c, cost) && i.Counts[2] == 2;                      // target changed (already repaired): refused, nothing deducted
            var j = new Inventory(); j.AddSlotItems(0, worn, 1); j.AddSlotItems(2, blank, 1);
            bool poor = !ScWeaponRepair.TryRepair(j, ScWeaponRepair.Candidates(j, 512).Single(), cost) && j.Counts[2] == 1 && j.Values[0] == worn; // short one mechanism: untouched
            var k = new Inventory(); k.AddSlotItems(0, worn, 1); k.AddSlotItems(2, blank, 2); k.AddSlotItems(3, mech, 2); k.RefuseSlot = 3;
            bool rollback = !ScWeaponRepair.TryRepair(k, ScWeaponRepair.Candidates(k, 512).Single(), cost) && k.Counts[2] == 2 && k.Values[0] == worn; // removal fails midway: blanks restored
            bool free = ScWeaponRepair.TryRepair(j, ScWeaponRepair.Candidates(j, 512).Single(), new Dictionary<int, int>()) && GunSpec.GetDurability(Terrain.ExtractData(j.Values[0])) == 7;
            return picked && ok && again && poor && rollback && free;
        });
        Test("workbench-no-inherited-index", () => typeof(ScWeaponWorkbenchBlock).GetFields().All(f => f.Name != "Index"));
        Test("unknown-gun-preserved", () => ScGunBlock.AssetIndex(63) == -1 && ScGunBlock.AssetIndex(42) == -1 && GunSpec.GetVariant(GunSpec.SetRounds(GunSpec.MakeData(42, 10), 7)) == 42);
        Test("throw-once", () => { var i=Setup(0,1);var tx=new ScThrowTransaction(i);int spawned=0;return tx.Commit(false,()=>true,()=>{spawned++;return true;}) && !tx.Commit(false,()=>true,()=>true) && spawned==1 && i.Counts[0]==0; });
        Test("throw-capacity-no-charge", () => { var i=Setup(0,1);return !new ScThrowTransaction(i).Commit(false,()=>false,()=>true) && i.Counts[0]==1; });
        Test("throw-spawn-rollback", () => { var i=Setup(0,1);return !new ScThrowTransaction(i).Commit(false,()=>true,()=>false) && i.Counts[0]==1; });
        Test("throw-cancel-before-release", () => { var i=Setup(0,1);var tx=new ScThrowTransaction(i);tx.Cancel();return !tx.Commit(false,()=>true,()=>true) && i.Counts[0]==1; });
        Test("throw-same-item-swap", () => { var i=Setup(0,1);var tx=new ScThrowTransaction(i);ScInventoryTransaction.Changed(i);return !tx.Valid && !tx.Commit(false,()=>true,()=>true) && i.Counts[0]==1; });
        Test("throw-creative", () => { var i=Setup(0,1);return new ScThrowTransaction(i).Commit(true,()=>true,()=>true) && i.Counts[0]==1; });
        Test("grenade-save-fuse-owner", () => { var g=new ScGrenadeState {Kind=0,Owner=7,Remaining=.22f,Position=new Vector3(1,2,3),Velocity=new Vector3(4,5,6)};var l=ScGrenadeState.Load(g.Save());return l.Owner==7 && l.Remaining==.22f && l.Position==g.Position && l.Velocity==g.Velocity; });
        Test("grenade-active-limits", () => { var list=Enumerable.Range(0,16).Select(i=>new ScGrenadeState {Owner=i/4}).ToArray();return !ScGrenadeState.CanAdd(list,9) && !ScGrenadeState.CanAdd(list.Take(4),0) && ScGrenadeState.CanAdd(list.Take(4),1); });
        Test("grenade-he-flash-falloff", () => ScGrenadeState.HePower(0)==24 && ScGrenadeState.HePower(4)==0 && ScGrenadeState.FlashDuration(0,1)==2 && ScGrenadeState.FlashDuration(0,-1)<.31f && ScGrenadeState.FlashDuration(16,1)==0);
        Test("smoke-finite-segment",()=> Math.Abs(ScSmokeVolume.InsideLength(new Vector3(-5,0,0),new Vector3(5,0,0),Vector3.Zero,3)-6)<.001f
            && ScSmokeVolume.InsideLength(new Vector3(-5,0,0),new Vector3(-4,0,0),Vector3.Zero,3)==0
            && ScSmokeVolume.InsideLength(new Vector3(-5,3,0),new Vector3(5,3,0),Vector3.Zero,3)==0);
        Test("smoke-near-contact-and-expiry",()=> {
            var s=new ScGrenadeState {Kind=2,Effect=true,Age=2,Remaining=12,Position=-Vector3.UnitY*1.5f};
            bool blocked=ScSmokeVolume.Blocks([s],new Vector3(-5,0,0),new Vector3(5,0,0));
            bool near=ScSmokeVolume.Blocks([s],Vector3.Zero,Vector3.UnitX);
            s.Remaining=0;return blocked && !near && !ScSmokeVolume.Blocks([s],new Vector3(-5,0,0),new Vector3(5,0,0));
        });
        Test("smoke-save-no-reset",()=> {var s=ScGrenadeState.Load(new ScGrenadeState {Kind=2,Effect=true,Age=8,Remaining=7}.Save());return s.Effect && s.Age==8 && s.Remaining==7;});
        Test("smoke-render-budget",()=>ScSmokeVolume.SpriteCount(0)==24 && ScSmokeVolume.SpriteCount(20)==16 && ScSmokeVolume.SpriteCount(50)==12 && 16*ScGrenadeVisuals.Smoke(new(){Kind=2,Effect=true,Age=2,Remaining=13},0).Count<=768);
        Test("fire-overlap-budget",()=> {
            var a=new ScGrenadeState {Kind=3,Effect=true,Remaining=6};var b=new ScGrenadeState {Kind=4,Effect=true,Remaining=7};
            return ScFireArea.Exposure([a,b],Vector3.Zero,1,_=>true).Power==4 && ScFireArea.Exposure([a,b],Vector3.Zero,.25f,_=>true).Power==1;
        });
        Test("fire-wall-and-height",()=> {
            var s=new ScGrenadeState {Kind=3,Effect=true,Remaining=6};
            return ScFireArea.Exposure([s],Vector3.Zero,1,_=>false).Power==0 && !ScFireArea.Contains(s,Vector3.UnitY*3) && !ScFireArea.Contains(s,Vector3.UnitX*3);
        });
        Test("fire-expiry-budget",()=> {var s=new ScGrenadeState {Kind=4,Effect=true,Remaining=.1f};return Math.Abs(ScFireArea.Exposure([s],Vector3.Zero,1,_=>true).Power-.4f)<.001f;});
        Test("smoke-extinguishes-fire",()=> {
            var fire=new ScGrenadeState {Kind=3,Effect=true,Remaining=6};var smoke=new ScGrenadeState {Kind=2,Effect=true,Remaining=15,Age=1};
            bool near=ScFireArea.SmokeTouches(fire,smoke);smoke.Position=Vector3.UnitX*20;return near && !ScFireArea.SmokeTouches(fire,smoke);
        });
        Test("decoy-anti-chain",()=> {var d=new ScDecoyResponse();return d.TryStart(0) && !d.TryStart(10) && !d.TryStart(17.9) && d.TryStart(18);});
        Test("decoy-animal-policy",()=>ScDecoyResponse.Investigates(CreatureCategory.LandPredator) && !ScDecoyResponse.Investigates(CreatureCategory.LandOther) && !ScDecoyResponse.Investigates(CreatureCategory.Bird));
        Test("grenade-all-six-enabled-frozen",()=> Enumerable.Range(0,6).All(ScGrenadeBlock.Enabled) && string.Join(",",ScGrenadeBlock.Assets)=="grenade_hegrenade,grenade_flashbang,grenade_smokegrenade,grenade_molotov,grenade_incendiary,grenade_decoy");
        foreach (string grenade in ScGrenadeBlock.Assets) {
            Test("grenade-world-accessories/"+grenade,()=> {
                var mesh=Cs2SkinnedMesh.Weapon(grenade);mesh.SetPose(Cs2Rig.Sample(grenade,"idle",0),Cs2Placement.Placement());mesh.Skin();
                var item=ScGrenadeWorldMesh.Build(mesh,grenade=="grenade_molotov",false);
                var flight=ScGrenadeWorldMesh.Build(mesh,grenade=="grenade_molotov",true);
                bool compact=item.Parts.All(p=>p.Indices.All(i=>i>=0 && i<item.Vertices.Length)) && flight.Parts.All(p=>p.Indices.All(i=>i>=0 && i<flight.Vertices.Length));
                return compact && flight.Vertices.Length>1000 && (grenade=="grenade_molotov"
                    ? item.Vertices.Length<mesh.Skinned.Length-2000 && flight.Vertices.Length==item.Vertices.Length
                    : flight.Vertices.Length<item.Vertices.Length);
            });
            foreach (string alias in new[] {"deploy","idle","inspect","inspect2","pullpin","holdHigh","holdLow","throwHigh","throwLow"}) {
                Test("grenade/"+grenade+"/"+alias,()=> {
                    if (!Cs2Rig.HasAlias(grenade,alias)) return false;
                    for (int frame=0;frame<5;frame++) {
                        var pose=Cs2Rig.Sample(grenade,alias,Cs2Rig.Duration(grenade,alias)*frame/4);
                        foreach (var mesh in new[] {Cs2SkinnedMesh.Arms,Cs2SkinnedMesh.Weapon(grenade)}) {
                            if (mesh is null || !mesh.SetPose(pose,Cs2Placement.Placement()) || mesh.UnresolvedWeight(pose)>.001f) return false;
                            mesh.Skin();if (!mesh.Skinned.All(v=>ScGrenadeState.Finite(v.Position))) return false;
                        }
                    }
                    return true;
                });
            }
            foreach (string alias in new[] {"throwHigh","throwLow"}) Test("grenade-release/"+grenade+"/"+alias,()=>Cs2Rig.GrenadeReleaseTime(grenade,alias)>=0 && Cs2Rig.GrenadeReleaseTime(grenade,alias)<Cs2Rig.Duration(grenade,alias));
        }
        foreach (var gun in GunSpec.All) {
            if (gun.RechargeSeconds > 0) continue;
            int variant = Array.IndexOf(GunSpec.All, gun) + CsmcKnifeRig.KnifeCount;
            foreach (bool empty in new[] { false, true }) {
                string alias = KnifeAnimationController.ReloadClip(variant, empty);
                bool good = ScReloadTransaction.IsTube(gun.Name) ? Cs2Rig.GetReloadSections(gun.Name) is not null : Cs2Rig.ReloadMilestones(gun.Name, alias) is not null;
                check($"survival/reload-events/{gun.Name}/{empty}", good, alias ?? "missing reload");
            }
        }
    }
}
