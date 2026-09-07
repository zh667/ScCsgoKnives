namespace Game;

public sealed class ScKnifeStrike {
    public double Next, HitAt = -1;
    public bool Heavy;
    public int Value, Slot;
    public long Revision;
    public IInventory Inventory;
    public static float Power(bool heavy) => heavy ? 12 : 7;
    // F14 (community plan 2026-09-07): light 2.2 blocks, heavy 1.8 blocks; 0.28.x was 1.6 / 1.3.
    // The raycast runs at the strike instant (TakeHit), not at the button press.
    public static float Range(bool heavy) => heavy ? 1.8f : 2.2f;
    public static double Interval(bool heavy) => heavy ? 1 : .45;
    public bool Start(double now, bool heavy) {
        if (now < Next || HitAt >= 0) return false;
        Heavy = heavy; HitAt = now + (heavy ? .30 : .15); Next = now + Interval(heavy); return true;
    }
    public bool TakeHit(double now) {
        if (HitAt < 0 || now < HitAt) return false;
        HitAt = -1; return true;
    }
    public void Cancel() => HitAt = -1;
}
