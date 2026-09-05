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
        const int ammo = 900;
        Inventory Setup(int rounds, int count) {
            var i = new Inventory(); i.AddSlotItems(0, Terrain.MakeBlockValue(512, 0, GunSpec.MakeData(0, rounds)), 1); i.AddSlotItems(1, ammo, count); return i;
        }
        ScReloadTransaction Tx(Inventory i, int cost = 1, int capacity = 30) => new(i, 0, i.GetSlotValue(0), ammo, cost, capacity);
        int R(Inventory i) => GunSpec.GetRounds(Terrain.ExtractData(i.GetSlotValue(0)));
        void Test(string name, Func<bool> test) { try { check("survival/" + name, test(), name); } catch (Exception e) { check("survival/" + name, false, e.ToString()); } }
        Test("cancel-before-drop", () => { var i = Setup(12, 2); var t = Tx(i); t.Cancel(); return !t.Discard() && !t.InsertMagazine() && R(i) == 12 && i.Counts[1] == 2; });
        Test("discard-survives-cancel", () => { var i = Setup(12, 2); var t = Tx(i); t.Discard(); t.Cancel(); return !t.InsertMagazine() && R(i) == 0 && i.Counts[1] == 2; });
        Test("insert-once", () => { var i = Setup(12, 2); var t = Tx(i); return t.Discard() && !t.Discard() && t.InsertMagazine() && !t.InsertMagazine() && R(i) == 30 && i.Counts[1] == 1; });
        Test("unavailable-after-drop", () => { var i = Setup(12, 1); var t = Tx(i); t.Discard(); i.RemoveSlotItems(1, 1); return !t.InsertMagazine() && R(i) == 0 && i.Counts[1] == 0; });
        Test("same-type-swap-epoch", () => { var i = Setup(12, 2); var t = Tx(i); ScInventoryTransaction.Changed(i); return !t.Discard() && R(i) == 12 && i.Counts[1] == 2; });
        Test("slot-switch", () => { var i = Setup(12, 2); var t = Tx(i); i.ActiveSlotIndex = 1; return !t.Discard() && R(i) == 12; });
        Test("negev-paid-150", () => { var i = Setup(127, 5); var t = Tx(i, 5, 150); return t.Discard() && t.InsertMagazine() && R(i) == 150 && i.Counts[1] == 0; });
        Test("negev-insufficient", () => { var i = Setup(0, 4); var t = Tx(i, 5, 150); t.Discard(); return !t.InsertMagazine() && R(i) == 0 && i.Counts[1] == 4; });
        Test("tube-preserves-rounds", () => { var i = Setup(3, 2); var t = Tx(i, 1, 8); bool added = t.InsertShell() && t.InsertShell(); t.Cancel(); return added && !t.InsertShell() && R(i) == 5 && i.Counts[1] == 0; });
        Test("mag7-discards-remainder", () => { var i = Setup(3, 5); var t = Tx(i, 5, 5); return t.Discard() && t.InsertMagazine() && R(i) == 5 && i.Counts[1] == 0; });
        Test("creative-free", () => { var i = Setup(0, 0); var t = Tx(i, 0); return t.Discard() && t.InsertMagazine() && R(i) == 30 && i.Counts[1] == 0; });
        Test("partial-removal-rollback", () => { var i = Setup(0, 2); i.AddSlotItems(2, ammo, 3); i.RefuseSlot = 2; var t = Tx(i, 5, 150); t.Discard(); return !t.InsertMagazine() && R(i) == 0 && i.Counts[1] == 2 && i.Counts[2] == 3; });
        Test("death-before-event", () => { var i = Setup(12, 2); var t = Tx(i); i.DropAllItems(default); return !t.Discard() && i.Counts[0] == 0 && i.Counts[1] == 0; });
        Test("saved-commits-only", () => {
            var i = Setup(12, 2); var t = Tx(i); t.Discard();
            var loaded = Setup(R(i), i.Counts[1]); var next = Tx(loaded);
            return R(loaded) == 0 && next.Discard() && next.InsertMagazine() && R(loaded) == 30 && loaded.Counts[1] == 1;
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
        Test("animal-shot-targets", () => Math.Ceiling(70 / ScSurvivalBalance.Power("ak47")) == 7 && Math.Ceiling(70 / ScSurvivalBalance.Power("awp")) == 2);
        Test("workbench-no-inherited-index", () => typeof(ScWeaponWorkbenchBlock).GetFields().All(f => f.Name != "Index"));
        Test("unknown-gun-preserved", () => ScGunBlock.AssetIndex(63) == -1 && GunSpec.GetVariant(GunSpec.SetRounds(GunSpec.MakeData(63, 10), 7)) == 63);
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
        Test("smoke-render-budget",()=>ScSmokeVolume.SpriteCount(0)==24 && ScSmokeVolume.SpriteCount(20)==12 && ScSmokeVolume.SpriteCount(50)==6 && 16*ScSmokeVolume.SpriteCount(0)<=384);
        foreach (string grenade in ScGrenadeBlock.Assets) {
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
