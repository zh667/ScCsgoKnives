namespace Game;

public sealed class SubsystemScKnifeBlockBehavior : SubsystemBlockBehavior {
    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScKnifeBlock>()];

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

