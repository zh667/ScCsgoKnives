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
