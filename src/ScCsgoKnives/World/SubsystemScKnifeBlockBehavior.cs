namespace Game;

public sealed class SubsystemScKnifeBlockBehavior : SubsystemBlockBehavior, IUpdateable {
    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScKnifeBlock>()];

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    // The capture run steps from here, outside the draw: each step renders the
    // scene once more through ScreenCaptureManager, which is the frame that counts.
    public void Update(float dt) => KnifeQa.Step();

    public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
        int value = inventory.GetSlotValue(slotIndex);
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScKnifeBlock>(true)) return false;

        if (!KnifeAnimationController.TriggerInspect(componentPlayer)) return true;

        string name = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(
            componentPlayer.Project.FindSubsystem<SubsystemTerrain>(true),
            value
        );
        componentPlayer.ComponentGui.DisplaySmallMessage(
            string.Format(LanguageControl.Get("ScCsgoKnives", "Message", "Inspect"), name),
            Engine.Color.White,
            true,
            false
        );
        return true;
    }
}

