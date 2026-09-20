using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using NekoMeko;
using NekoMeko.Components;
using Neorxna;
using Neorxna.Common;
using GameEntitySystem;

namespace Game;

public sealed class AppearanceModLoader : ModLoader {
    readonly ConditionalWeakTable<PlayerModelWidget, Dictionary<Model, CsPlayerPose>> previews = new();
    public override void __ModInitialize() => ModsManager.RegisterHook("OnPlayerModelWidgetMeasureOverride", this, 100);
    public override void OnXdbLoad(XElement database) {
        var player = database.Descendants("EntityTemplate").Single(e => (string)e.Attribute("Guid") == "4be6c1c5-d65d-4537-8a8b-a391969e6dc2");
        foreach (var pair in new[] { ("NekoMekoModel", typeof(ComponentCsPlayerAppearance)), ("NekoHUD", typeof(ComponentCsAppearanceHud)) }) {
            var member = player.Elements("MemberComponentTemplate").Single(e => (string)e.Attribute("Name") == pair.Item1);
            var parameter = member.Elements("Parameter").SingleOrDefault(e => (string)e.Attribute("Name") == "Class");
            if (parameter == null) member.Add(new XElement("Parameter", new XAttribute("Name", "Class"), new XAttribute("Value", pair.Item2.FullName), new XAttribute("Type", "string")));
            else parameter.SetAttributeValue("Value", pair.Item2.FullName);
        }
    }
    public override void OnPlayerModelWidgetMeasureOverride(PlayerModelWidget widget) {
        string key = widget.ExtraData is ExtraData { Type: "NMM-PlayerModelWidget", Data: string[] keys } && keys.Length == 2 ? keys[0]
            : widget.PlayerData?.PlayerIndex == NekoMekoDataManager.EditingPlayerIndex ? NekoMekoDataManager.CreatingPlayerModelKey
            : widget.PlayerData?.ComponentPlayer?.Entity.FindComponent<ComponentCsPlayerAppearance>()?.ModelKey;
        if (!ComponentCsPlayerAppearance.OwnsKey(key)) return;
        var resource = NekoMekoDataManager.FindModelData(key);
        var selected = resource?.GetRes(resource.ResPath["ModelName"]);
        // NMM has already selected the model. Restrict this hook to our two resource objects.
        foreach (var model in widget.m_modelWidget.Models) {
            if (!ReferenceEquals(selected, model)) continue;
            var poses = previews.GetOrCreateValue(widget);
            if (!poses.TryGetValue(model, out var pose)) poses[model] = pose = new CsPlayerPose(model);
            pose.Sample(Time.FrameIndex, Time.FrameDuration, 0, false, false, 0, 0);
            widget.m_modelWidget.CustomShader = null;
            widget.m_modelWidget.OnSetupShaderParameters = null;
            widget.m_modelWidget.ViewPosition = widget.CameraShot == PlayerModelWidget.Shot.Bust ? new Vector3(0, 1.6f, -1.7f) : new Vector3(0, 1.15f, -3.8f);
            widget.m_modelWidget.ViewTarget = widget.CameraShot == PlayerModelWidget.Shot.Bust ? new Vector3(0, 1.5f, 0) : new Vector3(0, .9f, 0);
            widget.m_modelWidget.ViewFov = .57f;
            widget.m_modelWidget.Textures.Remove(model); // GLB contains several material textures.
            for (int i = 0; i < pose.Local.Length; i++) widget.m_modelWidget.SetBoneTransform(model, i, pose.Local[i]);
        }
    }
}

// NMM 1.1's HUD directly calls its nonvirtual Animate() and replaces the root transform.
// Use a separate ordinary preview for CS models, leaving the upstream HUD intact for other models.
public sealed class ComponentCsAppearanceHud : ComponentNekoHUD, IUpdateable {
    PlayerModelWidget csWidget;
    void IUpdateable.Update(float dt) {
        if (ComponentNekoMekoModel is not ComponentCsPlayerAppearance { IsCs: true }) {
            if (csWidget != null) csWidget.IsVisible = false;
            base.Update(dt);
            return;
        }
        var parent = ComponentPlayer.GameWidget.Children.Find<CanvasWidget>("ControlsContainer");
        foreach (var widget in parent.Children.OfType<NekoMekoPlayerModelWidget>()) widget.IsVisible = false;
        string mode = NeorxnaSettingOptions.Settings.GetValue("NekoMekoDollMode", "None");
        if (mode == "None") { if (csWidget != null) csWidget.IsVisible = false; return; }
        if (csWidget == null) { csWidget = new PlayerModelWidget { PlayerData = ComponentPlayer.PlayerData, CharacterSkinTexture = ContentManager.Get<Texture2D>("Textures/Creatures/HumanMale1") }; parent.AddChildren(csWidget); }
        csWidget.IsVisible = true;
        csWidget.CameraShot = mode == "Bust" ? PlayerModelWidget.Shot.Bust : PlayerModelWidget.Shot.Body;
        float width = 100 * NeorxnaSettingOptions.Settings.GetValue("NekoMekoDollScale", 1f);
        csWidget.Size = new Vector2(width, mode == "Bust" ? width : width * 1.8f);
        string align = NeorxnaSettingOptions.Settings.GetValue("NekoMekoDollAlign", "LeftTop");
        csWidget.HorizontalAlignment = align.StartsWith("Right") ? WidgetAlignment.Far : WidgetAlignment.Near;
        csWidget.VerticalAlignment = align.EndsWith("Bottom") ? WidgetAlignment.Far : WidgetAlignment.Near;
        csWidget.Margin = new Vector2(NeorxnaSettingOptions.Settings.GetValue("NekoMekoDollMarginX", 72f), NeorxnaSettingOptions.Settings.GetValue("NekoMekoDollMarginY", 16f));
    }
    public override void OnEntityRemoved() {
        if (csWidget != null) csWidget.ParentWidget?.Children.Remove(csWidget);
        base.OnEntityRemoved();
    }
}
