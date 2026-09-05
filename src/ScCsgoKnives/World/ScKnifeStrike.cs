namespace Game;

public sealed class ScKnifeStrike {
    public double Next, HitAt = -1;
    public bool Heavy;
    public int Value, Slot;
    public long Revision;
    public IInventory Inventory;
    public static float Power(bool heavy) => heavy ? 12 : 7;
    public static float Range(bool heavy) => heavy ? 1.3f : 1.6f;
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
