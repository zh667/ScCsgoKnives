namespace Game;

/// <summary>Item data belongs to variants and ammunition, never vanilla tool wear.</summary>
public abstract class ScNoDurabilityBlock : Block {
    protected ScNoDurabilityBlock() => Durability = -1;
    public sealed override int GetDurability(int value) => -1;
    public sealed override int GetDamage(int value) => 0;
    public sealed override int SetDamage(int value, int damage) => value;
}
