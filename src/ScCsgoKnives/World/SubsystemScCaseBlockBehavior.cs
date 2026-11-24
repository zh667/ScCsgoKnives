using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

public sealed class SubsystemScCaseBlockBehavior : SubsystemBlockBehavior {
    SubsystemPickables m_subsystemPickables;
    SubsystemGameInfo m_subsystemGameInfo;
    readonly HashSet<ComponentPlayer> m_playersWithPendingResult = [];

    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScCaseBlock>()];

    public override void Load(ValuesDictionary valuesDictionary) {
        base.Load(valuesDictionary);
        m_subsystemPickables = Project.FindSubsystem<SubsystemPickables>(true);
        m_subsystemGameInfo = Project.FindSubsystem<SubsystemGameInfo>(true);
    }

    public override bool OnUse(Ray3 ray, ComponentMiner componentMiner) {
        ComponentPlayer player = componentMiner.ComponentPlayer;
        if (player is null) return true;
        // 1.6.0 (feedback-1124-170 Q5): opening is single-player only for now; cases still drop, stack and travel in multiplayer.
        if (ScNet.Role is ScNetRole.Host or ScNetRole.Client) {
            player.ComponentGui.DisplaySmallMessage(LanguageControl.TryGet(out string mp, "ScCsgoBox", "Message", "MultiplayerUnsupported") ? mp : "联机暂不支持开箱。", Color.LightYellow, true, true);
            return true;
        }

        if (m_playersWithPendingResult.Contains(player)) {
            player.ComponentGui.DisplaySmallMessage(LanguageControl.Get("ScCsgoBox", "Message", "PendingResult"), Color.LightYellow, true, false);
            return true;
        }

        int activeValue = componentMiner.ActiveBlockValue;
        CaseDefinition definition = CaseRegistry.GetByData(Terrain.ExtractData(activeValue));
        if (definition is null) {
            player.ComponentGui.DisplaySmallMessage(LanguageControl.Get("ScCsgoBox", "Message", "InvalidCase"), Color.White, true, true);
            return true;
        }

        int keySlot = FindKeySlot(componentMiner.Inventory, definition.KeyBlockIndex);
        if (keySlot < 0) {
            player.ComponentGui.DisplaySmallMessage(LanguageControl.Get("ScCsgoBox", "Message", "NeedKey"), Color.LightYellow, true, true);
            return true;
        }

        // Decide the reward before opening the UI. The animation can only display this result.
        // 1.6.0 (D17): guns only up to what the opener's level can craft.
        CaseRoll roll = CaseRoller.RollFor(definition, CsCaseRewards.EligibleFor((int)MathF.Floor(player.PlayerData.Level)),
            new System.Random(System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue)));
        CaseOpeningDialog dialog = new(definition, roll, reward => {
            try {
                GrantReward(componentMiner, reward);
            }
            finally {
                m_playersWithPendingResult.Remove(player);
            }
        });
        m_playersWithPendingResult.Add(player);
        if (m_subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
            componentMiner.Inventory.RemoveSlotItems(keySlot, 1);
            componentMiner.RemoveActiveTool(1);
        }

        AudioManager.PlaySound("Audio/ScCsgoBox/cs_open", 1f, 0f, 0f);
        DialogsManager.ShowDialog(player.GuiWidget, dialog);
        return true;
    }

    static int FindKeySlot(IInventory inventory, int keyBlockIndex) {
        for (int slot = 0; slot < inventory.SlotsCount; slot++) {
            if (inventory.GetSlotCount(slot) > 0 && Terrain.ExtractContents(inventory.GetSlotValue(slot)) == keyBlockIndex)
                return slot;
        }
        return -1;
    }

    void GrantReward(ComponentMiner miner, RewardDefinition reward) {
        int itemValue = reward.GetItemValue();
        int remaining = ComponentInventoryBase.AcquireItems(miner.Inventory, itemValue, reward.Count);
        if (remaining > 0) {
            Vector3 position = miner.ComponentCreature.ComponentBody.Position + new Vector3(0, 0.5f, 0);
            Entity owner = miner.Entity;
            m_subsystemPickables.AddPickable(itemValue, remaining, position, null, null, owner);
        }
        miner.ComponentPlayer?.ComponentGui.DisplaySmallMessage(
            string.Format(LanguageControl.Get("ScCsgoBox", "Message", "Received"), reward.GetDisplayName(), reward.Count),
            RarityStyle.Color(reward.Rarity),
            true,
            false
        );
    }
}
