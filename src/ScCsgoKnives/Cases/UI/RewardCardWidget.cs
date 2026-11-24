using Engine;
using Engine.Graphics;

namespace Game;

public sealed class RewardCardWidget : CanvasWidget {
    public RewardCardWidget(RewardDefinition reward) {
        Size = new Vector2(110, 148);
        Color rarityColor = RarityStyle.Color(reward.Rarity);

        RectangleWidget background = new() {
            Size = Size,
            FillColor = new Color(28, 31, 32, 245),
            OutlineColor = rarityColor,
            OutlineThickness = 4
        };
        BlockIconWidget icon = new() {
            Value = reward.GetItemValue(),
            Size = new Vector2(82, 82),
            Scale = 0.85f
        };
        LabelWidget name = new() {
            Text = reward.GetDisplayName(),
            Color = Color.White,
            FontScale = 0.62f,
            Size = new Vector2(104, 32),
            TextAnchor = TextAnchor.Center
        };
        LabelWidget rarity = new() {
            Text = RarityStyle.Name(reward.Rarity),
            Color = rarityColor,
            FontScale = 0.55f,
            Size = new Vector2(104, 22),
            TextAnchor = TextAnchor.Center
        };

        Children.Add(background, icon, name, rarity);
        SetWidgetPosition(icon, new Vector2(14, 10));
        SetWidgetPosition(name, new Vector2(3, 94));
        SetWidgetPosition(rarity, new Vector2(3, 124));
    }
}
