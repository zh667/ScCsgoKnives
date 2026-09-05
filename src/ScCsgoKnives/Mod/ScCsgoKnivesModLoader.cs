using Engine;

namespace Game;

public class ScCsgoKnivesModLoader : ModLoader {
    /// <summary>
    /// The version modinfo.json declares, so the log line cannot drift from the
    /// package the way a hardcoded string did -- 0.8.1 shipped announcing itself
    /// as 0.7.0, which made it look like the wrong build was installed.
    /// </summary>
    string ModVersion => Entity?.modInfo?.Version ?? "unknown";

    public override void __ModInitialize() {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("OnFirstPersonModelDrawing", this);
        ModsManager.RegisterHook("IsCrosshairVisible", this);   // hooks only fire for loaders that registered them (0.15.9 forgot this)
    }

    public override void OnLoadingFinished(List<Action> actions) {
        int index = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        int[] values = BlocksManager.Blocks[index].GetCreativeValues().ToArray();
        int gunIndex = BlocksManager.GetBlockIndex<ScGunBlock>(true);
        Log.Information($"[ScCsgoKnives] {ModVersion} initialized. block={index}, knives={CsmcKnifeRig.KnifeCount}, creativeValues={values.Length}, gunBlock={gunIndex}, guns={GunSpec.All.Length}.");

        // Every creative item must survive the round trip through the block
        // value and land on its own asset. A stale variant clamp left over from
        // the three-knife build silently mapped everything past the butterfly
        // onto the butterfly's model and animations, and only the inventory
        // icon gave it away.
        for (int variant = 0; variant < CsmcKnifeRig.KnifeCount; variant++) {
            int value = Terrain.MakeBlockValue(index, 0, variant);
            int roundTrip = ScKnifeBlock.GetVariant(value);
            string expected = CsmcKnifeRig.GetAssetName(variant);
            string actual = CsmcKnifeRig.GetAssetName(roundTrip);
            if (roundTrip != variant || actual != expected) {
                Log.Error($"[ScCsgoKnives] variant {variant} ({expected}) round-trips to {roundTrip} ({actual}); knives will share the wrong model.");
            }
        }

    }

    static int s_lastLoggedValue = int.MinValue;

    /// <summary>The vanilla crosshair is a fixed-size quad 50 units ahead, so it grows with the scope's FOV; the scope draws its own.</summary>
    public override void IsCrosshairVisible(ComponentAimingSights componentAimingSights, ref bool isVisible) {
        // Zoomed on the AUG / SG 553 the reticle is the scope's own dot, so the
        // vanilla crosshair goes too (it grew with the FOV in 0.20.0's video).
        if (CsmcFirstPersonRenderer.ScopeOverlayActive || CsmcFirstPersonRenderer.ScopeDotActive) isVisible = false;
    }

    public override void OnFirstPersonModelDrawing(ComponentFirstPersonModel componentFirstPersonModel, Camera camera, int itemValue, ref Matrix matrix, out bool skip) {
        skip = false;
        int variant = KnifeAnimationController.ResolveVariant(itemValue);
        if (variant < 0) {
            KnifeAnimationController.Update(componentFirstPersonModel, itemValue);
            return;
        }

        int raw = Terrain.ExtractData(itemValue);
        KnifeRigPose pose = KnifeAnimationController.Update(componentFirstPersonModel, itemValue);
        // Logged whenever the held value changes, so the hook's view of the item
        // can be compared against what ScKnifeBlock.DrawBlock sees.
        if (itemValue != s_lastLoggedValue) {
            s_lastLoggedValue = itemValue;
            Log.Information(
                $"[ScCsgoKnives] hook: value={itemValue} (0x{itemValue:X}), data={Terrain.ExtractData(itemValue)}, "
                + $"rawVariant={raw}, assetCount={CsmcKnifeRig.KnifeCount}, clamped={variant}, "
                + $"asset={CsmcKnifeRig.GetAssetName(variant)}, poseNull={pose is null}, "
                + $"activeBlockValue={componentFirstPersonModel.m_componentMiner.ActiveBlockValue}, m_value={componentFirstPersonModel.m_value}."
            );
        }
        if (pose is null) return;

        // The complete CSMC renderer owns weapon and arms. Returning skip=true
        // prevents SC from applying block offsets, generic poke/swap animation,
        // or drawing the old approximate model on top.
        skip = CsmcFirstPersonRenderer.Draw(componentFirstPersonModel, camera, variant, pose);
    }
}
