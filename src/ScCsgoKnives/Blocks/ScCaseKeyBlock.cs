using Engine.Graphics;

namespace Game;

public class ScCaseKeyBlock : FlatBlock {
    Texture2D m_texture;

    public override void Initialize() {
        m_texture = ContentManager.Get<Texture2D>("Textures/ScCsgoBox/csgo_key0");
        base.Initialize();
    }

    public override Texture2D GetDefaultTexture(int value) => m_texture;

    public override int GetTextureSlotCount(int value) => 1;

    public override int GetFaceTextureSlot(int face, int value) => 0;

    // 1.6.0 (user decision 2026-11-25): cases open without a key. The key stays a registered block so keys in test saves of
    // the earlier 1.6.0 candidates keep their item identity, but it has no recipe and no creative entry.
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() { yield break; }
    public override IEnumerable<int> GetCreativeValues() => [];
}
