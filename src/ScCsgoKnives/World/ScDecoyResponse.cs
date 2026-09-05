namespace Game;

public sealed class ScDecoyResponse {
    public double Next;
    public bool TryStart(double now) { if (now<Next) return false;Next=now+18;return true; }
    public static bool Investigates(CreatureCategory category) => category==CreatureCategory.LandPredator;
}
