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

    /// <summary>1.6.0 (feedback-1124-170 D15): the standalone key shape, all copper (it was 3 iron + 1 copper).</summary>
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        string text = LanguageControl.TryGet(out string t, "ScCsgoBox", "Recipe", "Key") ? t : "制作一把武器箱钥匙";
        yield return ScAmmoBlock.Recipe(Terrain.MakeBlockValue(BlockIndex), 1, text,
            [null, "copperingot", null, "copperingot", "copperingot", null, "copperingot", null, null]);
    }
}
