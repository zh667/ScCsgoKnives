using System.Xml.Linq;
using Engine;
using Engine.Graphics;

namespace Game;

public sealed class CaseOpeningDialog : Dialog {
    const float Duration = 6.5f;
    const float CardStride = 118f;
    const float InitialStart = 12f;
    const float ReelWidth = 700f;

    readonly CaseRoll m_roll;
    readonly Action<RewardDefinition> m_claimHandler;
    readonly CanvasWidget m_reel = new() { Size = new Vector2(ReelWidth, 154), ClampToBounds = true };
    readonly LabelWidget m_status;
    readonly BevelledButtonWidget m_actionButton;
    readonly List<RewardCardWidget> m_cards = [];
    readonly List<Widget> m_rollingWidgets = [];
    readonly CanvasWidget m_resultStage;
    readonly RotatingBlockIconWidget m_resultIcon;
    int m_lastTickIndex = -1;
    float m_elapsed;
    float m_showcaseElapsed;
    bool m_finished;
    bool m_claimed;

    public CaseOpeningDialog(CaseDefinition definition, CaseRoll roll, Action<RewardDefinition> claimHandler) {
        m_roll = roll;
        m_claimHandler = claimHandler;
        Size = new Vector2(760, 390);
        HorizontalAlignment = WidgetAlignment.Center;
        VerticalAlignment = WidgetAlignment.Center;

        BevelledRectangleWidget panel = new() {
            Style = ContentManager.Get<XElement>("Styles/DialogArea"),
            Size = Size
        };
        LabelWidget title = new() {
            Text = definition.GetDisplayName(),
            FontScale = 1.05f,
            Color = new Color(230, 215, 175),
            Size = new Vector2(720, 45),
            TextAnchor = TextAnchor.Center
        };
        RectangleWidget reelBackground = new() {
            Size = new Vector2(ReelWidth, 154),
            FillColor = new Color(13, 16, 17, 245),
            OutlineColor = new Color(95, 90, 76),
            OutlineThickness = 3
        };
        CircleWidget lensShade = new() {
            Size = new Vector2(250, 250),
            FillColor = new Color(18, 21, 24, 92)
        };
        RectangleWidget lensRim = new() {
            Subtexture = new Subtexture(ContentManager.Get<Texture2D>("Textures/ScCsgoBox/lens_vignette"), Vector2.Zero, Vector2.One),
            Size = new Vector2(250, 250),
            FillColor = new Color(255, 255, 255, 140),
            OutlineColor = Color.Transparent
        };
        RectangleWidget marker = new() {
            Size = new Vector2(5, 166),
            FillColor = new Color(255, 200, 60),
            OutlineColor = Color.Black,
            OutlineThickness = 1
        };
        m_status = new LabelWidget {
            Text = LanguageControl.Get("ScCsgoBox", "Dialog", "Rolling"),
            Color = Color.LightGray,
            FontScale = 0.72f,
            Size = new Vector2(700, 38),
            TextAnchor = TextAnchor.Center
        };
        m_actionButton = new BevelledButtonWidget {
            Text = LanguageControl.Get("ScCsgoBox", "Dialog", "Skip"),
            Size = new Vector2(190, 60),
            HorizontalAlignment = WidgetAlignment.Center
        };

        RewardDefinition reward = roll.Reward;
        m_resultStage = new CanvasWidget {
            Size = new Vector2(700, 250),
            ClampToBounds = true,
            IsVisible = false
        };
        LabelWidget resultName = new() {
            Text = reward.GetDisplayName() + (reward.Count > 1 ? $" ×{reward.Count}" : string.Empty),
            Color = Color.White,
            FontScale = 1.05f,
            Size = new Vector2(700, 42),
            TextAnchor = TextAnchor.Center
        };
        RectangleWidget resultAccent = new() {
            Size = new Vector2(420, 4),
            FillColor = RarityStyle.Color(reward.Rarity),
            OutlineColor = Color.Transparent
        };
        m_resultIcon = new RotatingBlockIconWidget {
            Value = reward.GetItemValue(),
            Size = new Vector2(380, 168),
            Scale = 0.65f,
            AutoRotate = true
        };
        LabelWidget resultRarity = new() {
            Text = RarityStyle.Name(reward.Rarity),
            Color = RarityStyle.Color(reward.Rarity),
            FontScale = 0.72f,
            Size = new Vector2(700, 30),
            TextAnchor = TextAnchor.Center
        };
        LabelWidget rotateHint = new() {
            Text = LanguageControl.Get("ScCsgoBox", "Dialog", "RotateHint"),
            Color = new Color(165, 165, 165),
            FontScale = 0.55f,
            Size = new Vector2(700, 24),
            TextAnchor = TextAnchor.Center
        };
        m_resultStage.Children.Add(resultName, resultAccent, m_resultIcon, resultRarity, rotateHint);
        m_resultStage.SetWidgetPosition(resultName, new Vector2(0, 0));
        m_resultStage.SetWidgetPosition(resultAccent, new Vector2(140, 42));
        m_resultStage.SetWidgetPosition(m_resultIcon, new Vector2(160, 48));
        m_resultStage.SetWidgetPosition(resultRarity, new Vector2(0, 208));
        m_resultStage.SetWidgetPosition(rotateHint, new Vector2(0, 230));

        Children.Add(panel, title, reelBackground, m_reel, lensShade, lensRim, marker, m_status, m_resultStage, m_actionButton);
        SetWidgetPosition(title, new Vector2(20, 18));
        SetWidgetPosition(reelBackground, new Vector2(30, 78));
        SetWidgetPosition(m_reel, new Vector2(30, 78));
        SetWidgetPosition(lensShade, new Vector2(255, 30));
        SetWidgetPosition(lensRim, new Vector2(255, 30));
        SetWidgetPosition(marker, new Vector2(378, 72));
        SetWidgetPosition(m_status, new Vector2(30, 248));
        SetWidgetPosition(m_resultStage, new Vector2(30, 40));
        SetWidgetPosition(m_actionButton, new Vector2(285, 305));
        m_rollingWidgets.AddRange([title, reelBackground, m_reel, lensShade, lensRim, marker, m_status]);

        for (int i = 0; i < roll.Reel.Count; i++) {
            RewardCardWidget card = new(roll.Reel[i]);
            m_cards.Add(card);
            m_reel.Children.Add(card);
        }
        UpdateReel(0);
    }

    public override void Update() {
        if (!m_finished) {
            m_elapsed += Time.FrameDuration;
            float t = MathUtils.Saturate(m_elapsed / Duration);
            float eased = 1f - MathF.Pow(1f - t, 3f);
            UpdateReel(eased);
            if (t >= 1f) FinishAnimation();

            if (m_actionButton.IsClicked || Input.Cancel || Input.Ok) {
                m_elapsed = Duration;
                UpdateReel(1f);
                FinishAnimation();
            }
        }
        else {
            UpdateShowcase();
            if (m_actionButton.IsClicked || Input.Cancel || Input.Ok)
                ClaimAndDismiss();
        }
    }

    void UpdateReel(float progress) {
        float finalStart = ReelWidth / 2f - 55f - m_roll.WinningIndex * CardStride;
        float start = MathUtils.Lerp(InitialStart, finalStart, progress);
        for (int i = 0; i < m_cards.Count; i++)
            m_reel.SetWidgetPosition(m_cards[i], new Vector2(start + i * CardStride, 3));

        int tickIndex = (int)MathF.Floor((ReelWidth / 2f - 55f - start) / CardStride);
        if (tickIndex != m_lastTickIndex && tickIndex >= 0 && tickIndex < m_cards.Count) {
            if (m_lastTickIndex >= 0)
                AudioManager.PlaySound("Audio/ScCsgoBox/cs_dita", 0.75f, MathUtils.Lerp(0.15f, -0.12f, progress), 0f);
            m_lastTickIndex = tickIndex;
        }
    }

    void FinishAnimation() {
        if (m_finished) return;
        m_finished = true;
        foreach (Widget widget in m_rollingWidgets) widget.IsVisible = false;
        m_resultStage.IsVisible = true;
        m_resultStage.ColorTransform = Color.Transparent;
        m_showcaseElapsed = 0f;
        m_actionButton.Text = LanguageControl.Get("ScCsgoBox", "Dialog", "Claim");
        AudioManager.PlaySound("Audio/ScCsgoBox/cs_finish", 1f, 0f, 0f);
    }

    void UpdateShowcase() {
        m_showcaseElapsed += Time.FrameDuration;
        float t = MathUtils.Saturate(m_showcaseElapsed / 0.42f);
        float eased = 1f - MathF.Pow(1f - t, 3f);
        m_resultStage.ColorTransform = Color.White * eased;
        m_resultIcon.Scale = MathUtils.Lerp(0.65f, 1.08f, eased);
    }

    void ClaimAndDismiss() {
        if (m_claimed) return;
        m_claimed = true;
        m_claimHandler?.Invoke(m_roll.Reward);
        DialogsManager.HideDialog(this);
    }
}
