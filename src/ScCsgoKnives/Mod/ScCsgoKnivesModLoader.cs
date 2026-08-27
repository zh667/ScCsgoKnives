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
        Log.Information($"[ScCsgoKnives] 0.3.0 initialized. block={index}, creativeValues=[{string.Join(",", values)}].");
    }

    public override void OnFirstPersonModelDrawing(ComponentFirstPersonModel componentFirstPersonModel, Camera camera, int itemValue, ref Matrix matrix, out bool skip) {
        skip = false;
        int contents = Terrain.ExtractContents(itemValue);
        if (contents != BlocksManager.GetBlockIndex<ScKnifeBlock>(true)) {
            KnifeAnimationController.Update(componentFirstPersonModel, itemValue);
            return;
        }

        Matrix sharedMovement = matrix;
        KnifeFramePose pose = KnifeAnimationController.Update(componentFirstPersonModel, itemValue);
        FirstPersonKnifeArmsRenderer.Draw(componentFirstPersonModel, camera, sharedMovement, pose);
        matrix = pose.Weapon * matrix;
    }
}
