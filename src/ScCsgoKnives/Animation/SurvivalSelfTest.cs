using Engine;
using TemplatesDatabase;
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
        /// <summary>Fault injection: RemoveSlotItems throws on this slot; AddSlotItems silently does nothing on FailAddSlot; OnAdd runs inside AddSlotItems (a hostile callback).</summary>
        public int ThrowOnRemoveSlot = -1, FailAddSlot = -1;
        public bool FailAddAll;
        public Action OnAdd;
        public int GetSlotValue(int i) => Values[i];
        public int GetSlotCount(int i) => Counts[i];
        public int GetSlotCapacity(int i, int v) => i == 0 ? 1 : 40;
        public int GetSlotProcessCapacity(int i, int v) => 0;
        public void AddSlotItems(int i, int v, int n) { if (n == 0 || i == FailAddSlot || FailAddAll) return; if (Counts[i] > 0 && Values[i] != v) throw new InvalidOperationException("mixed slot"); Values[i] = v; Counts[i] += n; var hook = OnAdd; OnAdd = null; hook?.Invoke(); }
        public int RemoveSlotItems(int i, int n) { if (i == ThrowOnRemoveSlot) throw new InvalidOperationException("injected removal failure"); if (i == RefuseSlot) return 0; n = Math.Min(n, Counts[i]); Counts[i] -= n; return n; }
        public void ProcessSlotItems(int i, int v, int count, int process, out int result, out int resultCount) { result = v; resultCount = 0; }
        public void DropAllItems(Vector3 position) => Array.Clear(Counts);
    }
    public static void Run(Action<string, bool, string> check) {
        ScGunRegistry.Current ??= new ScGunRegistry(); // headless: the gun state table a world would own
        ScGunRecoverySelfTest.Run(check);
        ScGunSaveGuardSelfTest.Run(check);
        ScCreativeSkinsSelfTest.Run(check);
        ScCreativeCountersSelfTest.Run(check);
        ScGunSkinGrowthSelfTest.Run(check);
        ScGun0282MigrationSelfTest.Run(check);
        ScPolishSelfTest.Run(check);
        ScGunGrowthSelfTest.Run(check);
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
        Test("negev-paid-150", () => { int negev = Array.FindIndex(GunSpec.All, g => g.Name == "negev"); var i = new Inventory(); i.AddSlotItems(0, Terrain.MakeBlockValue(512, 0, GunSpec.MakeData(negev, 127)), 1); i.AddSlotItems(1, ammo, 5); var t = Tx(i, 5, 150); return t.Discard() && t.InsertMagazine() && R(i) == 150 && i.Counts[1] == 0; });
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
            && ScSurvivalBalance.Power("taser") == 150 && ScSurvivalBalance.Power("glock18") == 10.5f && ScSurvivalBalance.BasePower("ak47") == 10
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
            var bound = new ScSmokeDisturbance { Center = new Vector3(1, 2, 3), Remaining = 2.2f }; bound.SmokeIds.Add(7);
            var l = ScSmokeDisturbance.Load(bound.Save());
            var expired = new ScSmokeDisturbance { Remaining = 0 }; expired.SmokeIds.Add(1); var overlong = new ScSmokeDisturbance { Remaining = 99 }; overlong.SmokeIds.Add(1);
            bool saved = l is not null && l.Center == new Vector3(1, 2, 3) && l.Remaining == 2.2f && l.SmokeIds.SequenceEqual([7]) && ScSmokeDisturbance.Load(expired.Save()) is null
                && ScSmokeDisturbance.Load(overlong.Save()) is null;
            return hold && refilling && gone && saved && ScSmokeDisturbance.Total == 3.5f && ScSmokeDisturbance.Clearing(null, Vector3.Zero, null) == 0;
        });
        Test("smoke-opening-opens-sight", () => {
            var smoke = new ScGrenadeState { Kind = 2, Id = 1, Effect = true, Age = 2, Remaining = 12, Position = -Vector3.UnitY * 1.5f };
            Vector3 a = new(-5, 0, 0), b = new(5, 0, 0);
            var opening = new ScSmokeDisturbance { Center = Vector3.Zero }; opening.SmokeIds.Add(1);
            float intact = ScSmokeVolume.EffectiveInsideLength(a, b, smoke, null), open = ScSmokeVolume.EffectiveInsideLength(a, b, smoke, [opening]);
            bool blockedBefore = ScSmokeVolume.Blocks([smoke], a, b) && Math.Abs(intact - 5.5f) < .2f; // radius 3 with the 0.5 m soft edge inside it
            bool openNow = open <= .5f + 1e-3f /* only the 0.5 m soft rim on each side is left */ && !ScSmokeVolume.Blocks([smoke], a, b, null, [opening]) && ScSmokeVolume.Density(smoke, Vector3.Zero, [opening]) == 0 && ScSmokeVolume.Density(smoke, Vector3.Zero) == 1;
            var side = new ScSmokeDisturbance { Center = new Vector3(0, 0, 4) }; side.SmokeIds.Add(1); // opening beside the path: sight still blocked
            bool sideBlocked = ScSmokeVolume.Blocks([smoke], a, b, null, [side]);
            opening.Remaining = 0; bool refilled = ScSmokeVolume.Blocks([smoke], a, b, null, [opening]) && Math.Abs(ScSmokeVolume.EffectiveInsideLength(a, b, smoke, [opening]) - 5.5f) < .2f;
            return blockedBefore && openNow && sideBlocked && refilled;
        });
        Test("scope-key-press-edge", () => {
            bool latch = false;
            bool first = SubsystemScGunBlockBehavior.PressEdge(ref latch, true), held = SubsystemScGunBlockBehavior.PressEdge(ref latch, true), released = SubsystemScGunBlockBehavior.PressEdge(ref latch, false);
            bool again = SubsystemScGunBlockBehavior.PressEdge(ref latch, true), idle = SubsystemScGunBlockBehavior.PressEdge(ref latch, false);
            bool drawnHeld = true; bool noActionOnDraw = !SubsystemScGunBlockBehavior.PressEdge(ref drawnHeld, true); // button already down when the gun comes up
            return first && !held && !released && again && !idle && noActionOnDraw && !latch;
        });
        Test("impact-sound-folders", () => new[] { "Stone", "Wood", "Plant", "Metal", "Soft", "Dirt", "Glass" }.All(m => SubsystemScGunBlockBehavior.ImpactFolder(m) == m)
            && SubsystemScGunBlockBehavior.ImpactFolder("Leaves") == "Plant" && SubsystemScGunBlockBehavior.ImpactFolder("Sand") == "Dirt" && SubsystemScGunBlockBehavior.ImpactFolder("Snow") == "Soft"
            && SubsystemScGunBlockBehavior.ImpactFolder("") is null && SubsystemScGunBlockBehavior.ImpactFolder(null) is null && SubsystemScGunBlockBehavior.ImpactFolder("Marble") == "Stone");
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
        foreach (string asset in new[] { "ak47", "awp", "glock18", "elite", "taser", "m249", "nova", "karambit", "grenade_hegrenade", "grenade_molotov", "m4a1s" }) {
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
        // ---- M4 gun state: every change goes through ScGunMutation; these follow the plan's T01-T12 matrix headlessly ----
        int Gun(int variant, int rounds) => Terrain.MakeBlockValue(512, 0, GunSpec.MakeData(variant, rounds));
        int Data(Inventory i, int slot) => Terrain.ExtractData(i.Values[slot]);
        ScGunResult Shoot(Inventory i, int slot, string holder = "player:0:0", bool creative = false) {
            var m = ScGunMutation.Prepare(i, slot, holder, out var why);
            return m is null ? why : m.Commit(r => { r.Rounds = Math.Max(0, r.Rounds - 1); if (!creative) r.Durability = Math.Max(0, r.Durability - 1); if (GunSpec.All[r.Variant].RechargeSeconds > 0 && r.Rounds <= 0) r.RechargeReadyAt = 100 + GunSpec.All[r.Variant].RechargeSeconds; });
        }
        ScGunResult Reload(Inventory i, int slot, int ammoValue, int cost, int capacity, string holder = "player:0:0") {
            var t = new ScReloadTransaction(i, slot, i.Values[slot], ammoValue, cost, capacity, holder);
            if (!t.Discard()) return ScGunResult.Invalid;
            return t.InsertMagazine() ? ScGunResult.Success : t.LastResult == ScGunResult.Success ? ScGunResult.Invalid : t.LastResult;
        }
        int Dur(Inventory i, int slot) => GunSpec.GetDurability(Data(i, slot));
        Test("gun-durability-exact", () => {
            var i = new Inventory(); i.AddSlotItems(0, Gun(0, 30), 1);
            bool fresh = GunSpec.IsFresh(Data(i, 0)) && Dur(i, 0) == 1500 && GunSpec.GetRounds(Data(i, 0)) == 30 && !GunSpec.GetSilencerOff(Data(i, 0));
            bool shot = Shoot(i, 0) == ScGunResult.Success && !GunSpec.IsFresh(Data(i, 0)) && GunSpec.GetId(Data(i, 0)) >= GunSpec.FirstId && Dur(i, 0) == 1499 && GunSpec.GetRounds(Data(i, 0)) == 29;
            int id = GunSpec.GetId(Data(i, 0)); bool stable = Shoot(i, 0) == ScGunResult.Success && GunSpec.GetId(Data(i, 0)) == id && Dur(i, 0) == 1498; // the id is the identity; writes keep it
            bool text = ScGunDurability.PercentText(1500, 1500) == "100%" && ScGunDurability.PercentText(1499, 1500) == ">99%" && ScGunDurability.PercentText(1, 1500) == "<1%"
                && ScGunDurability.PercentText(1290, 1500) == "86%" && ScGunDurability.PercentText(0, 1500) == "0%";
            bool classes = GunSpec.All.All(g => ScGunDurability.Full(g.Name) >= 100) && ScGunDurability.Full("ak47") == 1500 && ScGunDurability.Full("awp") == 200 && ScGunDurability.Full("taser") == 100 && ScGunDurability.Full("negev") == 4000;
            return fresh && shot && stable && text && classes;
        });
        Test("m4-t01-same-slot-swap", () => {
            var i = new Inventory(); i.AddSlotItems(0, Gun(0, 30), 1);
            for (int n = 0; n < 29; n++) if (Shoot(i, 0) != ScGunResult.Success) return false;
            for (int n = 0; n < 9; n++) { i.AddSlotItems(1, 900, 1); if (Reload(i, 0, 900, 1, 30) != ScGunResult.Success) return false; for (int k = 0; k < 30; k++) if (Shoot(i, 0) != ScGunResult.Success) return false; }
            int a = i.Values[0]; bool wornA = Dur(i, 0) == 1201;
            i.RemoveSlotItems(0, 1); i.AddSlotItems(0, Gun(0, 30), 1);            // a brand-new AK in the same slot
            bool freshB = Shoot(i, 0) == ScGunResult.Success && Dur(i, 0) == 1499;
            int b = i.Values[0]; i.RemoveSlotItems(0, 1); i.AddSlotItems(0, a, 1);   // the old one back
            bool keptA = Dur(i, 0) == 1201 && GunSpec.GetDurability(Terrain.ExtractData(b)) == 1499 && GunSpec.GetRounds(Data(i, 0)) == 0;
            return wornA && freshB && keptA;
        });
        Test("m4-t02-silencer-keeps-wear", () => {
            int magazine = GunSpec.All[1].Magazine; // M4A1-S
            var i = new Inventory(); i.AddSlotItems(0, Gun(1, magazine), 1);
            for (int n = 0; n < magazine; n++) if (Shoot(i, 0) != ScGunResult.Success) return false;
            var m = ScGunMutation.Prepare(i, 0, "player:0:0", out _); bool detached = m.Commit(r => r.SilencerOff = true) == ScGunResult.Success && GunSpec.GetSilencerOff(Data(i, 0));
            i.AddSlotItems(1, 900, 1); bool reloaded = Reload(i, 0, 900, 1, magazine) == ScGunResult.Success && GunSpec.GetRounds(Data(i, 0)) == magazine && GunSpec.GetSilencerOff(Data(i, 0));
            return detached && reloaded && Shoot(i, 0) == ScGunResult.Success && Dur(i, 0) == 1500 - magazine - 1 && GunSpec.GetSilencerOff(Data(i, 0));
        });
        Test("m4-t03-transfer-chain", () => {
            var player = new Inventory(); player.AddSlotItems(0, Gun(2, 5), 1);
            for (int n = 0; n < 3; n++) if (Shoot(player, 0) != ScGunResult.Success) return false;
            int value = player.Values[0]; var before = GunSpec.TryGetSnapshot(Terrain.ExtractData(value), out var s0) ? s0 : default;
            player.RemoveSlotItems(0, 1); var chest = new Inventory(); chest.AddSlotItems(3, value, 1);          // into a chest
            int dropped = chest.Values[3]; chest.RemoveSlotItems(3, 1);                                            // dropped: a bare value on the ground
            var other = new Inventory(); other.AddSlotItems(2, dropped, 1);                                         // picked up by another player
            var saved = ScGunRegistry.Current.Save(50); var reloaded = ScGunRegistry.Load(saved, 0);               // the world saved and reopened
            bool same = reloaded.TryGetSnapshot(before.Id, out var s1) && s1.Rounds == 2 && s1.Durability == 197 && s1.Variant == 2 && s1.Revision == before.Revision
                && GunSpec.GetId(Terrain.ExtractData(other.Values[2])) == before.Id;
            return before.Rounds == 2 && before.Durability == 197 && same && Shoot(other, 2, "player:1:2") == ScGunResult.Success && GunSpec.GetDurability(Terrain.ExtractData(other.Values[2])) == 196;
        });
        Test("m4-t04-copy-split", () => {
            // A locator that reports every other Inventory stub holding the id, like the engine scan does in the game.
            var savedLocator = ScGunMutation.HolderLocator;
            var world = new List<(Inventory Inv, int Slot, string Key)>();
            ScGunMutation.HolderLocator = (rid, except) => world.Where(w => w.Inv.Counts[w.Slot] > 0 && GunSpec.GetId(Terrain.ExtractData(w.Inv.Values[w.Slot])) == rid && w.Key != except).Select(w => w.Key).ToArray();
            try {
                var a = new Inventory(); a.AddSlotItems(0, Gun(0, 30), 1); world.Add((a, 0, "A"));
                if (Shoot(a, 0, "A") != ScGunResult.Success) return false;
                int id = GunSpec.GetId(Data(a, 0));
                var b = new Inventory(); b.AddSlotItems(4, a.Values[0], 1); world.Add((b, 4, "B"));               // the same value copied elsewhere
                // Case 1: the ORIGINAL is used first while the copy exists: the original moves to its own record, the copy keeps the old one.
                bool originalFirst = Shoot(a, 0, "A") == ScGunResult.Success && GunSpec.GetId(Data(a, 0)) != id && Dur(a, 0) == 1498
                    && GunSpec.GetId(Terrain.ExtractData(b.Values[4])) == id && GunSpec.GetDurability(Terrain.ExtractData(b.Values[4])) == 1499;
                bool copyThen = Shoot(b, 4, "B") == ScGunResult.Success && GunSpec.GetId(Terrain.ExtractData(b.Values[4])) == id && GunSpec.GetDurability(Terrain.ExtractData(b.Values[4])) == 1498 && Dur(a, 0) == 1498;
                // Case 2: a reload wiped every Holder; a copy of a used gun is used first: it gets its own record.
                var reloaded = ScGunRegistry.Load(ScGunRegistry.Current.Save(0), 0); var savedRegistry = ScGunRegistry.Current; ScGunRegistry.Current = reloaded;
                try {
                    var c = new Inventory(); c.AddSlotItems(1, a.Values[0], 1); world.Add((c, 1, "C"));             // copy of a (record id2, durability 1498)
                    int id2 = GunSpec.GetId(Data(a, 0));
                    bool afterReload = Shoot(c, 1, "C") == ScGunResult.Success && GunSpec.GetId(Terrain.ExtractData(c.Values[1])) != id2 && GunSpec.GetDurability(Terrain.ExtractData(c.Values[1])) == 1497
                        && reloaded.TryGetSnapshot(id2, out var orig) && orig.Durability == 1498;
                    bool originalStill = Shoot(a, 0, "A") == ScGunResult.Success && GunSpec.GetId(Data(a, 0)) == id2 && Dur(a, 0) == 1497 && GunSpec.GetDurability(Terrain.ExtractData(c.Values[1])) == 1497;
                    if (!(originalFirst && copyThen && afterReload && originalStill)) throw new InvalidOperationException($"originalFirst={originalFirst} copyThen={copyThen} afterReload={afterReload} originalStill={originalStill}");
                    return true;
                } finally { ScGunRegistry.Current = savedRegistry; }
            } finally { ScGunMutation.HolderLocator = savedLocator; }
        });
        Test("m4-nested-registration-refused", () => {
            // A hostile inventory runs another fresh-gun commit from inside AddSlotItems: the inner one must be refused, and
            // the outer item must end up pointing at the record that was published for it.
            var i = new Inventory(); i.AddSlotItems(0, Gun(0, 30), 1); i.AddSlotItems(3, Gun(4, 20), 1);
            ScGunResult inner = ScGunResult.Success;
            i.OnAdd = () => { var m = ScGunMutation.Prepare(i, 3, "player:0:3", out var w); inner = m is null ? w : m.Commit(r => r.Rounds--); };
            var outer = ScGunMutation.Prepare(i, 0, "player:0:0", out _);
            var result = outer.Commit(r => r.Rounds--);
            int id0 = GunSpec.GetId(Data(i, 0));
            bool consistent = result == ScGunResult.Success && inner == ScGunResult.Busy && ScGunRegistry.Current.TryGetSnapshot(id0, out var s0) && s0.Rounds == 29 && s0.Variant == 0
                && GunSpec.IsFresh(Data(i, 3)) && GunSpec.GetRounds(Data(i, 3)) == 20;
            bool later = Shoot(i, 3, "player:0:3") == ScGunResult.Success && GunSpec.GetId(Data(i, 3)) != id0 && GunSpec.GetRounds(Data(i, 3)) == 19 && GunSpec.GetRounds(Data(i, 0)) == 29;
            return consistent && later;
        });
        Test("m4-rollback-on-faults", () => {
            int blank = 950, mech = 951; var entry = ScWeaponCrafting.All.First(e => e.Name == "ak47");
            // Deducting the second material throws: the first material comes back, the record is untouched, no exception escapes.
            var i = new Inventory(); i.AddSlotItems(0, Gun(0, 30), 1); i.AddSlotItems(2, blank, 1); i.AddSlotItems(3, mech, 1);
            if (Shoot(i, 0) != ScGunResult.Success) return false;
            var quote = ScWeaponRepair.Prepare(ScWeaponRepair.Candidates(i, 512).Single(), entry, false, kind => kind == 0 ? blank : mech);
            i.ThrowOnRemoveSlot = 3; int rev = GunSpec.TryGetSnapshot(Data(i, 0), out var s1) ? s1.Revision : -1;
            var r1 = ScWeaponRepair.TryRepair(i, quote, "player:0:0"); i.ThrowOnRemoveSlot = -1;
            bool threw = r1 == ScGunResult.InventoryRejected && i.Counts[2] == 1 && i.Counts[3] == 1 && Dur(i, 0) == 1499 && GunSpec.TryGetSnapshot(Data(i, 0), out var s2) && s2.Revision == rev && ScGunRegistry.Current.Recovery.Count == 0;
            // The slot silently drops the added gun: the gun and the ammo paid come back.
            var j = new Inventory(); j.AddSlotItems(0, Gun(0, 0), 1); j.AddSlotItems(1, 900, 2); j.FailAddSlot = 0; int gunValue = j.Values[0];
            var r2 = Reload(j, 0, 900, 1, 30); j.FailAddSlot = -1;
            int gunSlot = Enumerable.Range(0, j.Values.Length).FirstOrDefault(idx => j.Values[idx] == gunValue && j.Counts[idx] == 1, -1);
            bool dropped = r2 == ScGunResult.InventoryRejected && j.Counts[1] == 2 && gunSlot >= 0 && j.Counts.Sum() == 3 && GunSpec.GetRounds(Terrain.ExtractData(j.Values[gunSlot])) == 0 && ScGunRegistry.Current.Recovery.Count == 0; // a fresh gun requiring an id came back, ammo too; existing IDs update in place
            // The restore itself is refused: the loss is recorded and reported, not hidden.
            var k = new Inventory(); k.AddSlotItems(0, Gun(0, 30), 1); k.AddSlotItems(2, blank, 1); k.AddSlotItems(3, mech, 1);
            if (Shoot(k, 0) != ScGunResult.Success) return false;
            var quoteK = ScWeaponRepair.Prepare(ScWeaponRepair.Candidates(k, 512).Single(), entry, false, kind => kind == 0 ? blank : mech);
            int pendingBefore = ScGunRegistry.Current.Recovery.Count;
            k.ThrowOnRemoveSlot = 3; k.FailAddAll = true; var r3 = ScWeaponRepair.TryRepair(k, quoteK, "player:0:0"); k.ThrowOnRemoveSlot = -1; k.FailAddAll = false;
            bool reported = r3 == ScGunResult.RecoveryPending && ScGunRegistry.Current.Recovery.Count == pendingBefore + 1 && ScGunRegistry.Current.Recovery.Batches.Last().Steps[0].Value == blank && ScGunMutation.Explain(r3).Contains("尚未归还") && Dur(k, 0) == 1499;
            int delivered = ScGunRegistry.Current.Recovery.Retry(_ => k);
            return threw && dropped && reported && delivered == 1 && k.Counts[2] == 1 && ScGunRegistry.Current.Recovery.Count == pendingBefore;
        });
        Test("m4-strict-record-parse", () => {
            var d = new ScGunRegistry().Save(0); var records = new ValuesDictionary();
            const string tail = "ct=0,k=0,gl=0,gp=-1,gv=0,rc=0,ov=0";
            records.SetValue("1", "v=0,r=30,s=0,d=1500,m=badMax,n=badRevision,c=badCharge,p=0," + tail);
            records.SetValue("2", "v=0,r=30,s=0,d=1500");
            records.SetValue("3", "v=0,r=30,s=0,d=1500,m=1500,n=0,c=-1,p=0," + tail + ",extra=1");
            records.SetValue("4", "v=0,r=30,s=2,d=1500,m=1500,n=0,c=-1,p=0," + tail);
            records.SetValue("5", "v=0,r=30,s=0,d=1500,m=1500,n=0,c=-1,p=0," + tail);
            d.SetValue("Records", records);
            var r = ScGunRegistry.Load(d, 0);
            bool strict = r.Count == 1 && r.QuarantinedCount == 4 && r.TryGetSnapshot(5, out _) && !r.TryGetSnapshot(1, out _) && r.Next == 6;
            bool kept = r.Save(0).GetValue<ValuesDictionary>("Records").GetValue<string>("1") == "v=0,r=30,s=0,d=1500,m=badMax,n=badRevision,c=badCharge,p=0," + tail;
            return strict && kept;
        });
        // ---- gun finishes (P1). A finish is a record field: appearance only, and every other field must
        // come out of the transaction untouched. Material kinds stand in for the blocks, which are not
        // registered headlessly, so the costs are exercised without a BlocksManager.
        int MatValue(int kind) => 950 + kind;
        int AwpVariant() => Array.FindIndex(GunSpec.All, g => g.Name == "awp");
        ScGunSkin SkinOf(string key) => ScGunSkinCatalog.All.First(x => x.Key == key);
        void Stock(Inventory i, ScGunSkin skin) {
            var (b, m, paint) = ScGunSkinCatalog.Cost[skin.Tier];
            i.AddSlotItems(2, MatValue(ScWeaponMaterialBlock.Blank), b + 4);
            i.AddSlotItems(3, MatValue(ScWeaponMaterialBlock.Mechanism), m + 4);
            i.AddSlotItems(4, MatValue(ScWeaponMaterialBlock.Paint), paint + 4);
        }
        ScGunResult ApplySkin(Inventory i, int slot, ScGunSkin skin, string holder = "player:0:0", bool free = false) {
            var q = ScWeaponSkinning.Prepare(i, slot, skin, free, MatValue);
            return q is null ? ScGunResult.Invalid : ScWeaponSkinning.Apply(i, q, holder);
        }
        Test("skin-catalog-shape", () => {
            bool ids = ScGunSkinCatalog.All.Length == 11 && ScGunSkinCatalog.All.Select(x => x.PaintId).Distinct().Count() == 11
                && ScGunSkinCatalog.All.Select(x => x.Key).Distinct().Count() == 11
                && ScGunSkinCatalog.All.All(x => x.PaintId > 0 && GunSpec.All.Any(g => g.Name == x.Gun));
            bool counts = ScGunSkinCatalog.For(AwpVariant()).Count() == 3
                && ScGunSkinCatalog.For(Array.FindIndex(GunSpec.All, g => g.Name == "ak47")).Count() == 4
                && ScGunSkinCatalog.For(Array.FindIndex(GunSpec.All, g => g.Name == "m4a1s")).Count() == 4
                && !ScGunSkinCatalog.For(Array.FindIndex(GunSpec.All, g => g.Name == "deagle")).Any();
            bool known = ScGunSkinCatalog.IsKnown(ScGunSkinCatalog.None) && ScGunSkinCatalog.IsKnown(51) && !ScGunSkinCatalog.IsKnown(9999)
                && ScGunSkinCatalog.Find(51).Key == "am_lightning_awp" && ScGunSkinCatalog.Find(9999) is null;
            bool fits = ScGunSkinCatalog.Fits(SkinOf("am_lightning_awp"), AwpVariant())
                && !ScGunSkinCatalog.Fits(SkinOf("am_lightning_awp"), Array.FindIndex(GunSpec.All, g => g.Name == "ak47"));
            bool tiers = ScGunSkinCatalog.Cost[ScSkinTier.Standard] == (2, 1, 1) && ScGunSkinCatalog.Cost[ScSkinTier.Premium] == (4, 2, 2)
                && ScGunSkinCatalog.Cost[ScSkinTier.Special] == (6, 3, 3) && ScGunSkinCatalog.RemovalCost == (1, 1, 1);
            bool names = ScGunSkinCatalog.Material("awp", 51) == "awp_hd__am_lightning_awp" && ScGunSkinCatalog.Material("awp", ScGunSkinCatalog.None) == "awp_hd"
                && ScGunSkinCatalog.Material("ak47", 51) == "ak47_hd" && ScGunSkinCatalog.Icon("awp", 51) == "awp_slot__am_lightning_awp"
                && ScGunSkinCatalog.Material("awp", 9999) == "awp_hd";
            return ids && counts && known && fits && tiers && names;
        });
        Test("skin-apply-keeps-state", () => {
            var skin = SkinOf("am_lightning_awp");
            var i = new Inventory(); i.AddSlotItems(0, Gun(AwpVariant(), 5), 1); Stock(i, skin);
            for (int n = 0; n < 3; n++) if (Shoot(i, 0) != ScGunResult.Success) return false;
            var m = ScGunMutation.Prepare(i, 0, "player:0:0", out _);
            if (m.Commit(r => { r.SilencerOff = false; r.RechargeReadyAt = 42; }) != ScGunResult.Success) return false;
            var before = GunSpec.TryGetSnapshot(Data(i, 0), out var b) ? b : default;
            int blanks = i.Counts[2], mechs = i.Counts[3], paints = i.Counts[4];
            if (ApplySkin(i, 0, skin) != ScGunResult.Success) return false;
            var after = GunSpec.TryGetSnapshot(Data(i, 0), out var a) ? a : default;
            bool kept = after.SkinId == skin.PaintId && after.Rounds == before.Rounds && after.Durability == before.Durability
                && after.MaxDurability == before.MaxDurability && after.SilencerOff == before.SilencerOff
                && after.RechargeReadyAt == before.RechargeReadyAt && after.Id == before.Id && after.Revision == before.Revision + 1;
            var (cb, cm, cp) = ScGunSkinCatalog.Cost[skin.Tier];
            bool charged = i.Counts[2] == blanks - cb && i.Counts[3] == mechs - cm && i.Counts[4] == paints - cp;
            bool again = ScWeaponSkinning.Prepare(i, 0, skin, false, MatValue) is null && i.Counts[2] == blanks - cb; // the finish it already wears is refused, free
            return kept && charged && again;
        });
        Test("skin-registers-fresh-template", () => {
            var skin = SkinOf("cu_fireserpent_ak47_bravo");
            int ak = Array.FindIndex(GunSpec.All, g => g.Name == "ak47");
            var i = new Inventory(); i.AddSlotItems(0, Gun(ak, GunSpec.All[ak].Magazine), 1); Stock(i, skin);
            bool fresh = GunSpec.IsFresh(Data(i, 0));
            if (ApplySkin(i, 0, skin) != ScGunResult.Success) return false;
            // A finished gun can never be a stackable template again: it now owns an instance record.
            return fresh && !GunSpec.IsFresh(Data(i, 0)) && GunSpec.GetId(Data(i, 0)) >= GunSpec.FirstId
                && GunSpec.GetSkinId(Data(i, 0)) == skin.PaintId && GunSpec.GetRounds(Data(i, 0)) == GunSpec.All[ak].Magazine;
        });
        Test("skin-strip-and-wrong-gun", () => {
            var skin = SkinOf("am_lightning_awp");
            var i = new Inventory(); i.AddSlotItems(0, Gun(AwpVariant(), 5), 1); Stock(i, skin); i.AddSlotItems(5, MatValue(ScWeaponMaterialBlock.Paint), 4);
            if (ApplySkin(i, 0, skin) != ScGunResult.Success) return false;
            int blanks = i.Counts[2];
            bool wrongGun = ScWeaponSkinning.Prepare(i, 0, SkinOf("cu_fireserpent_ak47_bravo"), false, MatValue) is null; // an AK finish is not offered on an AWP
            var strip = ScWeaponSkinning.Prepare(i, 0, null, false, MatValue);
            bool stripped = strip is not null && strip.Cost[MatValue(ScWeaponMaterialBlock.Blank)] == ScGunSkinCatalog.RemovalCost.Blank
                && ScWeaponSkinning.Apply(i, strip, "player:0:0") == ScGunResult.Success && GunSpec.GetSkinId(Data(i, 0)) == ScGunSkinCatalog.None
                && i.Counts[2] == blanks - ScGunSkinCatalog.RemovalCost.Blank;
            bool free = ApplySkin(i, 0, skin, free: true) == ScGunResult.Success && GunSpec.GetSkinId(Data(i, 0)) == skin.PaintId;
            return wrongGun && stripped && free;
        });
        Test("skin-rollback-on-faults", () => {
            var skin = SkinOf("gs_awp_gungnir"); // Special: 6 blanks, 3 mechanisms, 3 paint
            var i = new Inventory(); i.AddSlotItems(0, Gun(AwpVariant(), 5), 1);
            if (Shoot(i, 0) != ScGunResult.Success) return false;
            int worn = Dur(i, 0);
            bool poor = ApplySkin(i, 0, skin) == ScGunResult.InsufficientMaterials && GunSpec.GetSkinId(Data(i, 0)) == ScGunSkinCatalog.None;
            Stock(i, skin);
            int blanks = i.Counts[2], paints = i.Counts[4];
            i.ThrowOnRemoveSlot = 4; var threw = ApplySkin(i, 0, skin); i.ThrowOnRemoveSlot = -1;
            bool rolled = threw == ScGunResult.InventoryRejected && i.Counts[2] == blanks && i.Counts[4] == paints
                && GunSpec.GetSkinId(Data(i, 0)) == ScGunSkinCatalog.None && Dur(i, 0) == worn;
            var last = ApplySkin(i, 0, skin);
            bool ok = last == ScGunResult.Success && GunSpec.GetSkinId(Data(i, 0)) == skin.PaintId && Dur(i, 0) == worn;
            // A slot that silently drops what is added only matters when the item value has to change, which
            // is when a factory template is registered by the finish; an already-registered gun keeps its value.
            var f = new Inventory(); f.AddSlotItems(0, Gun(AwpVariant(), GunSpec.All[AwpVariant()].Magazine), 1); Stock(f, skin);
            int fBlanks = f.Counts[2]; f.FailAddSlot = 0;
            var dropped = ApplySkin(f, 0, skin); f.FailAddSlot = -1;
            bool refused = dropped != ScGunResult.Success && f.Counts[2] == fBlanks
                && (f.Counts[0] == 0 || GunSpec.GetSkinId(Terrain.ExtractData(f.Values[0])) == ScGunSkinCatalog.None);
            if (!(poor && rolled && refused && ok)) throw new InvalidOperationException($"poor={poor} threw={threw} rolled={rolled} dropped={dropped} refused={refused} last={last} skin={GunSpec.GetSkinId(Data(i, 0))} dur={Dur(i, 0)}/{worn} blanks={i.Counts[2]}/{blanks} paints={i.Counts[4]}/{paints} fBlanks={f.Counts[2]}/{fBlanks}");
            return true;
        });
        Test("skin-registry-full-is-safe", () => {
            var saved = ScGunRegistry.Current; var full = new ScGunRegistry(); while (!full.IsFull) full.Allocate(0, 0, false, 1); ScGunRegistry.Current = full;
            try {
                var skin = SkinOf("am_lightning_awp");
                var i = new Inventory(); i.AddSlotItems(0, Gun(AwpVariant(), 5), 1); Stock(i, skin);
                int blanks = i.Counts[2];
                // A factory template needs a record before it can hold a finish; with no room it stays plain and pays nothing.
                return ApplySkin(i, 0, skin) == ScGunResult.RegistryFull && GunSpec.IsFresh(Data(i, 0))
                    && GunSpec.GetSkinId(Data(i, 0)) == ScGunSkinCatalog.None && i.Counts[2] == blanks;
            } finally { ScGunRegistry.Current = saved; }
        });
        Test("skin-survives-transfer-and-two-saves", () => {
            var skin = SkinOf("cu_m4a1s_printstream");
            int m4 = Array.FindIndex(GunSpec.All, g => g.Name == "m4a1s");
            var player = new Inventory(); player.AddSlotItems(0, Gun(m4, 5), 1); Stock(player, skin);
            if (ApplySkin(player, 0, skin) != ScGunResult.Success) return false;
            if (Shoot(player, 0) != ScGunResult.Success) return false;
            var want = GunSpec.TryGetSnapshot(Data(player, 0), out var w) ? w : default;
            int value = player.Values[0];
            player.RemoveSlotItems(0, 1);
            var chest = new Inventory(); chest.AddSlotItems(3, value, 1);          // chest
            int dropped = chest.Values[3]; chest.RemoveSlotItems(3, 1);              // ground / projectile / moving block carry the bare value
            var other = new Inventory(); other.AddSlotItems(2, dropped, 1);          // another player
            var once = ScGunRegistry.Load(ScGunRegistry.Current.Save(0), 0);
            var twice = ScGunRegistry.Load(once.Save(0), 0);                          // save, quit, re-enter, save, re-enter
            bool kept = twice.TryGetSnapshot(want.Id, out var got) && got.SkinId == skin.PaintId && got.Rounds == want.Rounds
                && got.Durability == want.Durability && got.SilencerOff == want.SilencerOff && got.Revision == want.Revision
                && GunSpec.GetId(Terrain.ExtractData(other.Values[2])) == want.Id;
            return kept && want.SkinId == skin.PaintId;
        });
        Test("skin-copies-are-independent", () => {
            var skin = SkinOf("gs_m4a1s_snakebite_gold");
            int m4 = Array.FindIndex(GunSpec.All, g => g.Name == "m4a1s");
            var a = new Inventory(); a.AddSlotItems(0, Gun(m4, 5), 1); Stock(a, skin);
            if (ApplySkin(a, 0, skin) != ScGunResult.Success) return false;
            int id = GunSpec.GetId(Data(a, 0));
            var b = new Inventory(); b.AddSlotItems(1, a.Values[0], 1);              // a creative copy of the finished gun
            var savedLocator = ScGunMutation.HolderLocator;
            ScGunMutation.HolderLocator = (rid, except) => rid == id && except != "A" ? ["A"] : Array.Empty<string>();
            try {
                bool split = Shoot(b, 1, "B") == ScGunResult.Success && GunSpec.GetId(Terrain.ExtractData(b.Values[1])) != id
                    && GunSpec.GetSkinId(Terrain.ExtractData(b.Values[1])) == skin.PaintId;   // the copy keeps the finish
                int copyDur = GunSpec.GetDurability(Terrain.ExtractData(b.Values[1]));
                // Two shots on the original, none on the copy: the two records must now differ and the copy
                // must be exactly where it was left.
                var shotA = Shoot(a, 0, "A"); var shotA2 = Shoot(a, 0, "A");
                bool apart = shotA == ScGunResult.Success && shotA2 == ScGunResult.Success && Dur(a, 0) == copyDur - 1
                    && GunSpec.GetDurability(Terrain.ExtractData(b.Values[1])) == copyDur
                    && GunSpec.GetSkinId(Data(a, 0)) == skin.PaintId;
                if (!(split && apart)) throw new InvalidOperationException($"split={split} apart={apart} id={id} copyId={GunSpec.GetId(Terrain.ExtractData(b.Values[1]))} copyDur={copyDur} aDur={Dur(a, 0)} shots={shotA}/{shotA2}");
                return true;
            } finally { ScGunMutation.HolderLocator = savedLocator; }
        });
        Test("skin-never-merges-a-stack", () => {
            var skin = SkinOf("am_lightning_awp");
            var i = new Inventory(); i.AddSlotItems(0, Gun(AwpVariant(), 5), 1); Stock(i, skin);
            int plain = i.Values[0];
            if (ApplySkin(i, 0, skin) != ScGunResult.Success) return false;
            int finished = i.Values[0];
            var j = new Inventory(); j.AddSlotItems(0, Gun(AwpVariant(), 5), 1); Stock(j, skin);
            if (ApplySkin(j, 0, SkinOf("cu_medieval_dragon_awp")) != ScGunResult.Success) return false;
            // Different finish, and finished versus factory, are different item values, so no inventory can
            // put them in one stack even before MaxStacking is considered.
            bool distinct = finished != plain && finished != j.Values[0] && GunSpec.GetId(Terrain.ExtractData(finished)) != GunSpec.GetId(Terrain.ExtractData(j.Values[0]));
            if (!distinct) throw new InvalidOperationException($"plain={plain} finished={finished} other={j.Values[0]} idF={GunSpec.GetId(Terrain.ExtractData(finished))} idO={GunSpec.GetId(Terrain.ExtractData(j.Values[0]))}");
            return true;
        });
        Test("skin-schema-1-converts-to-none", () => {
            // A schema-1 world: seven fields, no finish. Everything else must survive untouched.
            var d = new ValuesDictionary(); d.SetValue("Schema", ScGunRegistry.SchemaWithoutSkins); d.SetValue("Next", 3);
            var records = new ValuesDictionary(); records.SetValue("1", "0,17,1,900,1500,4,12.5"); records.SetValue("2", "2,3,0,200,200,0,-1");
            d.SetValue("Records", records);
            var r = ScGunRegistry.Load(d, 100);
            bool converted = r.Count == 2 && r.QuarantinedCount == 0
                && r.TryGetSnapshot(1, out var a) && a is { Variant: 0, Rounds: 17, SilencerOff: true, Durability: 900, MaxDurability: 1500, Revision: 4, SkinId: ScGunSkinCatalog.None }
                && Math.Abs(a.RechargeReadyAt - 112.5) < 1e-3
                && r.TryGetSnapshot(2, out var b) && b is { Variant: 2, Rounds: 3, Durability: 200, SkinId: ScGunSkinCatalog.None };
            // Saving writes schema 2 with the finish field; reading that back changes nothing else.
            var again = ScGunRegistry.Load(r.Save(100), 100);
            bool stable = again.Save(0).GetValue<int>("Schema") == ScGunRegistry.Schema
                && again.TryGetSnapshot(1, out var c) && c.Rounds == 17 && c.Durability == 900 && c.Revision == 4 && c.SkinId == ScGunSkinCatalog.None;
            return converted && stable;
        });
        Test("skin-bad-records-are-quarantined", () => {
            var d = new ValuesDictionary(); d.SetValue("Schema", ScGunRegistry.Schema); d.SetValue("Next", 1);
            var records = new ValuesDictionary();
            const string skinTail = "ct=0,k=0,gl=0,gp=-1,gv=0,rc=0,ov=0";
            records.SetValue("1", "v=0,r=30,s=0,d=1500,m=1500,n=0,c=-1,p=9999," + skinTail);  // a finish this build does not know
            records.SetValue("2", "v=0,r=30,s=0,d=1500,m=1500,n=0,c=-1,p=51," + skinTail);    // an AWP finish on an AK
            records.SetValue("3", "0,30,0,1500,1500,0,-1");                                    // a schema-1 row inside a schema-3 table
            records.SetValue("4", "v=2,r=5,s=0,d=200,m=200,n=0,c=-1,p=51," + skinTail);       // valid: Lightning Strike on the AWP
            d.SetValue("Records", records);
            var r = ScGunRegistry.Load(d, 0);
            bool only = r.Count == 1 && r.QuarantinedCount == 3 && r.TryGetSnapshot(4, out var ok) && ok.SkinId == 51 && ok.Variant == 2;
            bool untouched = r.Save(0).GetValue<ValuesDictionary>("Records").GetValue<string>("1") == "v=0,r=30,s=0,d=1500,m=1500,n=0,c=-1,p=9999," + skinTail;
            bool refused = !r.TryGetSnapshot(1, out _) && !r.TryGetSnapshot(2, out _) && !r.TryGetSnapshot(3, out _);
            var unknown = new ValuesDictionary(); unknown.SetValue("Schema", 99);
            var far = ScGunRegistry.Load(unknown, 0);
            return only && untouched && refused && far.UnknownSchema && far.Disabled && far.Save(0) == unknown;
        });
        Test("skin-cannot-be-written-by-a-plain-mutation", () => {
            var i = new Inventory(); i.AddSlotItems(0, Gun(AwpVariant(), 5), 1);
            if (Shoot(i, 0) != ScGunResult.Success) return false;
            var m = ScGunMutation.Prepare(i, 0, "player:0:0", out _);
            // An unknown paint ID, or one from another gun, is refused by the record validator itself.
            bool unknown = m.Commit(r => r.SkinId = 9999) == ScGunResult.Invalid && GunSpec.GetSkinId(Data(i, 0)) == ScGunSkinCatalog.None;
            var m2 = ScGunMutation.Prepare(i, 0, "player:0:0", out _);
            bool wrongGun = m2.Commit(r => r.SkinId = SkinOf("cu_fireserpent_ak47_bravo").PaintId) == ScGunResult.Invalid
                && GunSpec.GetSkinId(Data(i, 0)) == ScGunSkinCatalog.None;
            return unknown && wrongGun;
        });
        Test("m4-t13-save-snapshots", () => {
            var reg = new ScGunRegistry(); var saved = ScGunRegistry.Current; ScGunRegistry.Current = reg;
            try {
                var i = new Inventory(); i.AddSlotItems(0, Gun(0, 30), 1); i.AddSlotItems(1, 900, 3);
                if (Shoot(i, 0) != ScGunResult.Success) return false;
                var first = reg.Save(10); string firstRow = first.GetValue<ValuesDictionary>("Records").GetValue<string>("1");
                for (int n = 0; n < 5; n++) if (Shoot(i, 0) != ScGunResult.Success) return false;             // the world keeps changing while the first snapshot would be written
                if (Reload(i, 0, 900, 1, 30) != ScGunResult.Success) return false;
                var second = reg.Save(20); string secondRow = second.GetValue<ValuesDictionary>("Records").GetValue<string>("1");
                bool immutable = first.GetValue<ValuesDictionary>("Records").GetValue<string>("1") == firstRow && firstRow.StartsWith("v=0,r=29,s=0,d=1499,m=1500,n=1,") && secondRow.StartsWith("v=0,r=30,s=0,d=1494,m=1500,n=7,");
                var fromFirst = ScGunRegistry.Load(first, 0); var fromSecond = ScGunRegistry.Load(second, 0);
                bool distinct = fromFirst.TryGetSnapshot(1, out var a) && a.Rounds == 29 && a.Durability == 1499 && fromSecond.TryGetSnapshot(1, out var b) && b.Rounds == 30 && b.Durability == 1494 && b.Revision == 7;
                bool matches = GunSpec.TryGetSnapshot(Data(i, 0), out var live) && live.Rounds == 30 && live.Durability == 1494 && live.Revision == 7 && i.Counts[1] == 2;
                return immutable && distinct && matches;
            } finally { ScGunRegistry.Current = saved; }
        });
        Test("m4-holder-keys-unique", () => {
            var a = new Inventory(); var b = new Inventory();
            return ScGunHolders.Key(a, 0) != ScGunHolders.Key(b, 0) && ScGunHolders.Key(a, 0) != ScGunHolders.Key(a, 1) && ScGunHolders.Key(a, 2) == ScGunHolders.Key(a, 2)
                && ScGunHolders.Key(new object(), 0) != ScGunHolders.Key(new object(), 0) && ScGunHolders.Key(null, 3) == "none:3";
        });
        Test("m4-t05-full-table-new-guns", () => {
            var saved = ScGunRegistry.Current; var full = new ScGunRegistry(); while (!full.IsFull) full.Allocate(0, 0, false, 1); ScGunRegistry.Current = full;
            try {
                var i = new Inventory(); i.AddSlotItems(0, Gun(0, 30), 1); i.AddSlotItems(1, 900, 3); i.AddSlotItems(2, 950, 2);
                bool shot = Shoot(i, 0) == ScGunResult.RegistryFull && GunSpec.IsFresh(Data(i, 0)) && GunSpec.GetRounds(Data(i, 0)) == 30 && full.Count == GunSpec.LastId;
                i.AddSlotItems(3, Gun(0, 0), 1); i.ActiveSlotIndex = 3;
                bool reload = Reload(i, 3, 900, 1, 30, "player:0:3") == ScGunResult.RegistryFull && i.Counts[1] == 3 && GunSpec.GetRounds(Data(i, 3)) == 0;
                var m = ScGunMutation.Prepare(i, 0, "player:0:0", out _); bool silencer = m.Commit(r => r.SilencerOff = true) == ScGunResult.RegistryFull && !GunSpec.GetSilencerOff(Data(i, 0));
                bool clone = full.Clone(1) == -1;
                return shot && reload && silencer && clone && i.Counts[2] == 2;
            } finally { ScGunRegistry.Current = saved; }
        });
        Test("m4-t06-full-table-existing-gun", () => {
            var saved = ScGunRegistry.Current; var reg = new ScGunRegistry(); ScGunRegistry.Current = reg;
            try {
                var i = new Inventory(); i.AddSlotItems(0, Gun(0, 30), 1); i.AddSlotItems(1, 900, 2); i.AddSlotItems(2, 950, 2); i.AddSlotItems(3, 951, 2);
                if (Shoot(i, 0) != ScGunResult.Success) return false;
                while (!reg.IsFull) reg.Allocate(0, 0, false, 1);
                for (int n = 0; n < 29; n++) if (Shoot(i, 0) != ScGunResult.Success) return false;
                bool reload = Reload(i, 0, 900, 1, 30) == ScGunResult.Success && i.Counts[1] == 1 && GunSpec.GetRounds(Data(i, 0)) == 30;
                var entry = ScWeaponCrafting.All.First(e => e.Name == "ak47");
                var quote = ScWeaponRepair.Prepare(ScWeaponRepair.Candidates(i, 512).Single(), entry, false, kind => kind == 0 ? 950 : 951);
                bool repair = ScWeaponRepair.TryRepair(i, quote, "player:0:0") == ScGunResult.Success && Dur(i, 0) == 1500 && i.Counts[2] == 1 && i.Counts[3] == 1;
                var loaded = ScGunRegistry.Load(reg.Save(0), 0);
                return reload && repair && loaded.Count == reg.Count && loaded.IsFull;
            } finally { ScGunRegistry.Current = saved; }
        });
        Test("m4-t07-reload-faults", () => {
            var i = new Inventory(); i.AddSlotItems(0, Gun(0, 3), 1); i.ActiveSlotIndex = 0; int before = i.Values[0];
            bool noAmmo = Reload(i, 0, 900, 1, 30) == ScGunResult.InsufficientMaterials && GunSpec.GetRounds(Data(i, 0)) == 3 && i.Values[0] == before;
            i.AddSlotItems(1, 900, 2);
            var blocked = new Inventory(); blocked.AddSlotItems(0, Gun(0, 0), 1); blocked.AddSlotItems(1, 900, 2); blocked.RefuseSlot = 0;
            bool refused = Reload(blocked, 0, 900, 1, 30) == ScGunResult.InventoryRejected && blocked.Counts[1] == 2 && GunSpec.GetRounds(Data(blocked, 0)) == 0;
            var t = new ScReloadTransaction(i, 0, i.Values[0], 900, 1, 30, "player:0:0"); bool started = t.Discard();
            if (Shoot(i, 0) != ScGunResult.Success) return false;                                                     // the gun changed under the transaction
            bool stale = !t.InsertMagazine() && (t.LastResult == ScGunResult.StateChanged || !t.Valid) && i.Counts[1] == 2 && GunSpec.GetRounds(Data(i, 0)) == 2;
            var m = ScGunMutation.Prepare(i, 0, "player:0:0", out _);
            bool once = m.Commit(r => r.Rounds = 30, 900, 1) == ScGunResult.Success && i.Counts[1] == 1 && m.Commit(r => r.Rounds = 30, 900, 1) == ScGunResult.StateChanged && i.Counts[1] == 1;
            var busy = ScGunMutation.Prepare(i, 0, "player:0:0", out _);
            bool reentry = busy.Commit(r => { var inner = ScGunMutation.Prepare(i, 0, "player:0:0", out _); if (inner.Commit(x => x.Rounds = 1) != ScGunResult.Busy) throw new InvalidOperationException("re-entry not refused"); r.Rounds = 29; }) == ScGunResult.Success && GunSpec.GetRounds(Data(i, 0)) == 29;
            return noAmmo && refused && started && stale && once && reentry;
        });
        Test("m4-t08-repair-quote", () => {
            var i = new Inventory(); i.AddSlotItems(0, Gun(0, 30), 1); i.AddSlotItems(2, 950, 3); i.AddSlotItems(3, 951, 3);
            for (int n = 0; n < 30; n++) if (Shoot(i, 0) != ScGunResult.Success) return false;
            var entry = ScWeaponCrafting.All.First(e => e.Name == "ak47");
            var quote = ScWeaponRepair.Prepare(ScWeaponRepair.Candidates(i, 512).Single(), entry, false, kind => kind == 0 ? 950 : 951);
            bool priced = quote.Durability == 1470 && quote.Full == 1500 && quote.Cost[950] == 1 && quote.Cost[951] == 1;
            i.AddSlotItems(1, 900, 1); if (Reload(i, 0, 900, 1, 30) != ScGunResult.Success) return false;                  // the record moved on: the quote is stale
            bool stale = ScWeaponRepair.TryRepair(i, quote, "player:0:0") == ScGunResult.StateChanged && Dur(i, 0) == 1470 && i.Counts[2] == 3;
            var quote2 = ScWeaponRepair.Prepare(ScWeaponRepair.Candidates(i, 512).Single(), entry, false, kind => kind == 0 ? 950 : 951);
            int swapped = Gun(0, 30); int mine = i.Values[0]; i.RemoveSlotItems(0, 1); i.AddSlotItems(0, swapped, 1);            // a same-looking fresh gun swapped in
            bool swappedOut = ScWeaponRepair.TryRepair(i, quote2, "player:0:0") == ScGunResult.StateChanged && i.Counts[2] == 3;
            i.RemoveSlotItems(0, 1); i.AddSlotItems(0, mine, 1);
            bool once = ScWeaponRepair.TryRepair(i, quote2, "player:0:0") == ScGunResult.Success && Dur(i, 0) == 1500 && i.Counts[2] == 2 && i.Counts[3] == 2
                && ScWeaponRepair.TryRepair(i, quote2, "player:0:0") == ScGunResult.StateChanged && i.Counts[2] == 2;
            var j = new Inventory(); j.AddSlotItems(0, Gun(0, 30), 1); if (Shoot(j, 0) != ScGunResult.Success) return false;
            var poorQuote = ScWeaponRepair.Prepare(ScWeaponRepair.Candidates(j, 512).Single(), entry, false, kind => kind == 0 ? 950 : 951);
            bool poor = ScWeaponRepair.TryRepair(j, poorQuote, "player:0:0") == ScGunResult.InsufficientMaterials && GunSpec.GetDurability(Terrain.ExtractData(j.Values[0])) == 1499;
            return priced && stale && swappedOut && once && poor;
        });
        Test("m4-t09-wear-kinds", () => {
            int taser = Array.FindIndex(GunSpec.All, g => g.Name == "taser"), awp = 2;
            var i = new Inventory(); i.AddSlotItems(0, Gun(awp, 5), 1);
            var last = ScGunMutation.Prepare(i, 0, "player:0:0", out _); if (last.Commit(r => r.Durability = 1) != ScGunResult.Success) return false;
            bool lastPoint = Shoot(i, 0) == ScGunResult.Success && Dur(i, 0) == 0 && ScGunDurability.IsBroken(Data(i, 0)) && GunSpec.GetRounds(Data(i, 0)) == 4; // the last point fires, then broken
            var c = new Inventory(); c.AddSlotItems(0, Gun(0, 30), 1);
            bool creative = Shoot(c, 0, creative: true) == ScGunResult.Success && Dur(c, 0) == 1500 && GunSpec.GetRounds(Data(c, 0)) == 29;
            var z = new Inventory(); z.AddSlotItems(0, Gun(taser, 1), 1);
            bool zeus = Shoot(z, 0) == ScGunResult.Success && GunSpec.GetRounds(Data(z, 0)) == 0 && Dur(z, 0) == 99 && GunSpec.TryGetSnapshot(Data(z, 0), out var zs) && Math.Abs(zs.RechargeReadyAt - 110) < 1e-6;
            return lastPoint && creative && zeus;
        });
        Test("m4-t10-zeus-per-instance", () => {
            int taser = Array.FindIndex(GunSpec.All, g => g.Name == "taser");
            var a = new Inventory(); a.AddSlotItems(0, Gun(taser, 1), 1); var b = new Inventory(); b.AddSlotItems(0, Gun(taser, 1), 1);
            var ma = ScGunMutation.Prepare(a, 0, "player:0:0", out _); if (ma.Commit(r => { r.Rounds = 0; r.RechargeReadyAt = 10; }) != ScGunResult.Success) return false;
            var mb = ScGunMutation.Prepare(b, 0, "player:1:0", out _); if (mb.Commit(r => { r.Rounds = 0; r.RechargeReadyAt = 15; }) != ScGunResult.Success) return false;
            int ida = GunSpec.GetId(Data(a, 0)), idb = GunSpec.GetId(Data(b, 0));
            var loaded = ScGunRegistry.Load(ScGunRegistry.Current.Save(6), 100);                                       // saved at t=6, reopened with the clock at 100
            bool separate = loaded.TryGetSnapshot(ida, out var sa) && loaded.TryGetSnapshot(idb, out var sb) && Math.Abs(sa.RechargeReadyAt - 104) < 1e-3 && Math.Abs(sb.RechargeReadyAt - 109) < 1e-3;
            var ready = ScGunMutation.Prepare(a, 0, "player:0:0", out _); bool restored = ready.Commit(r => { r.Rounds = 1; r.RechargeReadyAt = -1; }) == ScGunResult.Success
                && GunSpec.TryGetSnapshot(Data(a, 0), out var sa2) && sa2.RechargeReadyAt < 0 && GunSpec.TryGetSnapshot(Data(b, 0), out var sb2) && sb2.RechargeReadyAt == 15;
            var q = ScGunMutation.Prepare(b, 0, "player:1:0", out _); bool repairKeepsClock = q.Commit(r => r.Durability = r.MaxDurability) == ScGunResult.Success && GunSpec.TryGetSnapshot(Data(b, 0), out var sb3) && sb3.RechargeReadyAt == 15;
            return separate && restored && repairKeepsClock;
        });
        Test("m4-t11-registry-round-trips", () => {
            var r = new ScGunRegistry(); int id = r.Allocate(3, 7, true, 1134); r.Get(id).Revision = 4; // the Desert Eagle: a 1200-shot pistol
            var once = ScGunRegistry.Load(r.Save(0), 0); var twice = ScGunRegistry.Load(once.Save(0), 0);
            bool round = twice.TryGetSnapshot(id, out var s) && s is { Variant: 3, Rounds: 7, SilencerOff: true, Durability: 1134, MaxDurability: 1200, Revision: 4 } && twice.Next == r.Next && twice.Count == 1;
            var unknown = new ValuesDictionary(); unknown.SetValue("Schema", 99); unknown.SetValue("Whatever", "kept");
            var refused = ScGunRegistry.Load(unknown, 0); bool preserved = refused.UnknownSchema && refused.Disabled && refused.Save(0) == unknown && refused.Count == 0;
            bool empty = ScGunRegistry.Load(null, 0).Count == 0 && ScGunRegistry.Load(new ScGunRegistry().Save(0), 0).Next == GunSpec.FirstId;
            return round && preserved && empty;
        });
        Test("m4-t12-bad-records", () => {
            var d = new ScGunRegistry().Save(0); var records = new ValuesDictionary();
            const string tail12 = "ct=0,k=0,gl=0,gp=-1,gv=0,rc=0,ov=0";
            records.SetValue("1", "v=0,r=30,s=0,d=1500,m=1500,n=0,c=-1,p=0," + tail12);
            records.SetValue("2", "v=0,r=99,s=0,d=1500,m=1500,n=0,c=-1,p=0," + tail12);   // over this model's capacity
            records.SetValue("3", "v=63,r=1,s=0,d=10,m=10,n=0,c=-1,p=0," + tail12);       // a model this build does not have
            records.SetValue("4", "junk");
            records.SetValue("5", "v=0,r=1,s=0,d=2000,m=1500,n=0,c=-1,p=0," + tail12);    // durability above its own maximum
            d.SetValue("Records", records); d.SetValue("Next", 2);
            var r = ScGunRegistry.Load(d, 0);
            bool kept = r.Count == 1 && r.QuarantinedCount == 4 && r.Next == 6 && !r.TryGetSnapshot(2, out _) && !r.TryGetSnapshot(5, out _);
            var again = r.Save(0).GetValue<ValuesDictionary>("Records"); bool verbatim = again.GetValue<string>("2") == "v=0,r=99,s=0,d=1500,m=1500,n=0,c=-1,p=0," + tail12 && again.GetValue<string>("4") == "junk";
            var saved = ScGunRegistry.Current; ScGunRegistry.Current = r;
            try {
                var i = new Inventory(); i.AddSlotItems(0, Terrain.MakeBlockValue(512, 0, GunSpec.WithId(0, 2)), 1); i.AddSlotItems(1, Terrain.MakeBlockValue(512, 0, GunSpec.WithId(5, 1)), 1);
                bool missing = Shoot(i, 0) == ScGunResult.MissingRecord && !ScGunBlock.IsKnown(i.Values[0]);
                bool mismatch = Shoot(i, 1, "player:0:1") == ScGunResult.MissingRecord && !GunSpec.IsUsable(Data(i, 1)) && r.TryGetSnapshot(1, out var s1) && s1.Rounds == 30; // a record of another model is not this gun's
                return kept && verbatim && missing && mismatch;
            } finally { ScGunRegistry.Current = saved; }
        });
        Test("gun-registry-save-and-limits", () => {
            var full = new ScGunRegistry(); int allocated = 0; while (!full.IsFull) { if (full.Allocate(0, 0, false, 1) > 0) allocated++; }
            bool limit = allocated == GunSpec.LastId && full.Allocate(0, 0, false, 1) == -1 && full.Clone(1) == -1 && full.PeekNextId() == -1 && full.TryGetSnapshot(GunSpec.LastId, out _);
            var saved = ScGunRegistry.Current; ScGunRegistry.Current = full;
            int partial = GunSpec.MakeData(0, 7); bool fallback = GunSpec.IsFresh(partial) && GunSpec.GetRounds(partial) == 30;
            ScGunRegistry.Current = saved;
            return limit && fallback;
        });
        Test("gun-old-format-kept-untouched", () => {
            int before = ScGunRegistry.Current.Count;
            bool foreign = new[] { 65858, 116173, 115720 }.All(d => GunSpec.IsForeign(d) && GunSpec.GetId(d) == -1 && !GunSpec.IsFresh(d) && GunSpec.GetRounds(d) == 0 && GunSpec.GetDurability(d) == 0
                && !ScGunBlock.IsKnown(Terrain.MakeBlockValue(512, 0, d)) && ScGunBlock.IsOldFormat(Terrain.MakeBlockValue(512, 0, d)));
            var i = new Inventory(); i.AddSlotItems(0, Terrain.MakeBlockValue(512, 0, 65858), 1);
            bool refused = Shoot(i, 0) == ScGunResult.Foreign && i.Values[0] == Terrain.MakeBlockValue(512, 0, 65858) && ScGunRegistry.Current.Count == before;
            return foreign && refused && GunSpec.DataLayout == 5;
        });
        Test("gun-unusable-without-record", () => {
            int before = ScGunRegistry.Current.Count; int d = 49154;
            var i = new Inventory(); i.AddSlotItems(0, Terrain.MakeBlockValue(512, 0, d), 1);
            bool refused = !GunSpec.IsForeign(d) && !GunSpec.IsFresh(d) && !GunSpec.IsUsable(d) && GunSpec.GetRounds(d) == 0 && GunSpec.GetDurability(d) == 0
                && Shoot(i, 0) == ScGunResult.MissingRecord && i.Values[0] == Terrain.MakeBlockValue(512, 0, d) && ScGunRegistry.Current.Count == before
                && !ScGunBlock.IsKnown(i.Values[0]) && ScGunBlock.IsOldFormat(i.Values[0]);
            return refused;
        });
        Test("world-layout-status", () => {
            var C = ScGunRegistry.WorldStatus.Compatible; var L = ScGunRegistry.WorldStatus.Legacy; var N = ScGunRegistry.WorldStatus.New;
            bool stamped = ScGunRegistry.Classify(5, false, false) == C && ScGunRegistry.Classify(5, true, true) == C
                && ScGunRegistry.Classify(4, false, false) == L && ScGunRegistry.Classify(4, true, true) == L
                && ScGunRegistry.Classify(6, true, false) == ScGunRegistry.WorldStatus.Unknown && ScGunRegistry.Classify(3, false, false) == ScGunRegistry.WorldStatus.Unknown;
            bool inferred = ScGunRegistry.Classify(0, true, true) == C && ScGunRegistry.Classify(0, true, false) == C
                && ScGunRegistry.Classify(0, false, true) == L && ScGunRegistry.Classify(0, false, false) == N;
            bool legacy1 = ScGunRegistry.Classify(0, false, true) == L; int stamp1 = ScGunRegistry.StampFor(legacy1);
            bool legacy2 = ScGunRegistry.Classify(stamp1, true, true) == L; int stamp2 = ScGunRegistry.StampFor(legacy2);
            bool fresh = ScGunRegistry.Classify(0, false, false) == N && ScGunRegistry.Classify(ScGunRegistry.StampFor(false), true, false) == C;
            return stamped && inferred && stamp1 == 4 && legacy2 && stamp2 == 4 && fresh;
        });
        Test("legacy-world-disables-guns", () => {
            var saved = ScGunRegistry.Current; var legacy = new ScGunRegistry { LegacyWorld = true }; ScGunRegistry.Current = legacy;
            try {
                var i = new Inventory(); i.AddSlotItems(0, Terrain.MakeBlockValue(512, 0, GunSpec.MakeData(0, 30)), 1);
                return GunSpec.IsForeign(Data(i, 0)) && !GunSpec.IsUsable(Data(i, 0)) && !ScGunBlock.IsKnown(i.Values[0]) && Shoot(i, 0) == ScGunResult.Foreign && legacy.Count == 0;
            } finally { ScGunRegistry.Current = saved; }
        });
        Test("gun-repair-cost", () => {
            var ak = ScWeaponCrafting.All.First(e => e.Name == "ak47"); var glock = ScWeaponCrafting.All.First(e => e.Name == "glock18");
            var m249 = ScWeaponCrafting.All.First(e => e.Name == "m249"); var knife = ScWeaponCrafting.All.First(e => e.Knife);
            int blank = ScWeaponRepair.Blank, mech = ScWeaponRepair.Mechanism;
            var full = ScWeaponRepair.FullCost(ak); var pistol = ScWeaponRepair.FullCost(glock); var mg = ScWeaponRepair.FullCost(m249);
            bool fullOk = full[blank] == 1 && full[mech] == 1 && full.Count == 2 && pistol[blank] == 1 && !pistol.ContainsKey(mech) && mg[blank] == 2 && mg[mech] == 1
                && ScWeaponRepair.FullCost(knife).Count == 0 && ScWeaponCrafting.All.Where(e => !e.Knife).All(e => ScWeaponRepair.FullCost(e).Values.Sum() >= 1
                    && ScWeaponRepair.FullCost(e).Values.Sum() <= Math.Max(1, (e.B + e.M + e.H + e.O) * 3 / 10 + 1));
            var none = ScWeaponRepair.Cost(ak, 1500, 1500); var one = ScWeaponRepair.Cost(ak, 1499, 1500); var broken = ScWeaponRepair.Cost(ak, 0, 1500); var half = ScWeaponRepair.Cost(m249, 750, 4000);
            return fullOk && none.Count == 0 && one[blank] == 1 && one[mech] == 1 && broken[blank] == 1 && broken[mech] == 1 && half[blank] == 2 && half[mech] == 1;
        });
        Test("smoke-opening-bound-to-its-smokes", () => {
            var near = new ScGrenadeState { Kind = 2, Id = 1, Effect = true, Age = 2, Remaining = 12, Position = -Vector3.UnitY * 1.5f };
            var behindWall = new ScGrenadeState { Kind = 2, Id = 2, Effect = true, Age = 2, Remaining = 12, Position = -Vector3.UnitY * 1.5f + Vector3.UnitZ * 2 };
            var opening = new ScSmokeDisturbance { Center = Vector3.Zero }; opening.SmokeIds.Add(1);
            Vector3 a = new(-5, 0, 0), b = new(5, 0, 0);
            bool nearOpened = ScSmokeVolume.Density(near, Vector3.Zero, [opening]) == 0 && !ScSmokeVolume.Blocks([near], a, b, null, [opening]);
            bool otherWhole = ScSmokeVolume.Density(behindWall, Vector3.Zero, [opening]) == 1 && ScSmokeVolume.Blocks([behindWall], a, b, null, [opening]);
            var l = ScSmokeDisturbance.Load(opening.Save());
            bool saved = l is not null && l.SmokeIds.SequenceEqual([1]) && ScSmokeDisturbance.Load(new ScSmokeDisturbance { Center = Vector3.Zero }.Save()) is null; // an opening naming no smoke is dropped
            bool sameDensity = Math.Abs(ScSmokeVolume.EffectiveInsideLength(a, b, near, null) - 5.5f) < .2f; // the same soft-edged density the overlay uses, integrated
            return nearOpened && otherWhole && saved && sameDensity;
        });
        Test("third-person-body-fist", () => {
            Matrix root = Matrix.CreateScale(.0241f) * Matrix.CreateRotationX(-MathF.PI / 2);
            Matrix body = ScThirdPersonMath.BodyAbsolute(root, .5f, new Vector3(10, 64, -3));
            Matrix hand = ScThirdPersonMath.HandAbsolute(new Vector3(7.48f, .12f, 51.5f), new Vector2(ScThirdPersonStance.Grenade.RightRaise, ScThirdPersonStance.Grenade.RightSwing), body);
            Vector3 fist = Vector3.Transform(ScThirdPersonMath.HandEndLocal(true), hand), shoulder = hand.Translation;
            Vector3 forward = Matrix.CreateRotationY(.5f).Forward;
            bool height = shoulder.Y > 64 + 1.1f && shoulder.Y < 64 + 1.4f && fist.Y < shoulder.Y && fist.Y > 64 + .6f;
            bool reach = Math.Abs((fist - shoulder).Length() - 20.5f * .0241f) < .05f && Vector3.Dot(fist - shoulder, forward) > .2f; // the raised arm reaches forward
            return height && reach && ScGrenadeState.Finite(fist);
        });
        Test("workbench-no-inherited-index", () => typeof(ScWeaponWorkbenchBlock).GetFields().All(f => f.Name != "Index"));
        Test("unknown-gun-preserved", () => ScGunBlock.AssetIndex(63) == -1 && ScGunBlock.AssetIndex(42) == -1 && GunSpec.GetVariant(GunSpec.WithId(42, 5)) == 42 && GunSpec.GetId(GunSpec.WithId(42, 5)) == 5);
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
