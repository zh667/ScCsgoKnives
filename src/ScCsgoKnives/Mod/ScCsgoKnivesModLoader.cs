using Engine;

namespace Game;

public class ScCsgoKnivesModLoader : ModLoader {
    public override void __ModInitialize() {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("OnFirstPersonModelDrawing", this);
    }

    public override void OnLoadingFinished(List<Action> actions) {
        int index = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        int[] values = BlocksManager.Blocks[index].GetCreativeValues().ToArray();
        Log.Information($"[ScCsgoKnives] 0.2.0 initialized. block={index}, creativeValues=[{string.Join(",", values)}].");
    }

    public override void OnFirstPersonModelDrawing(ComponentFirstPersonModel componentFirstPersonModel, Camera camera, int itemValue, ref Matrix matrix, out bool skip) {
        skip = false;
        KnifeAnimationController.Apply(componentFirstPersonModel, itemValue, ref matrix);
    }
}
