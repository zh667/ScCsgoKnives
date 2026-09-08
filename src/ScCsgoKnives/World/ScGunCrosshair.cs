using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>The gun crosshair: shown only while a usable gun is actually in hand.
///
/// Vanilla's own crosshair is a quad fifty units ahead, so it grows with the scope's field of view, and on a
/// touch device outside split-touch control it is not drawn at all. This one is screen space, a fixed size, and
/// colourable per player without touching a shared texture or a global colour, so nobody else's crosshair,
/// vanilla tool aiming, scope reticle, hit marker or tracer changes with it.
///
/// While it is drawn the vanilla crosshair is suppressed through the engine's own hook, so there is never a
/// second layer; the moment the gun leaves the hand vanilla decides again, exactly as before.</summary>
public static class ScGunCrosshair {
    /// <summary>A gun this build can read and fire is in the player's hand.</summary>
    public static bool HoldingGun(ComponentPlayer player) {
        if (player?.ComponentMiner is null) return false;
        int value = player.ComponentMiner.ActiveBlockValue;
        return Terrain.ExtractContents(value) == BlocksManager.GetBlockIndex<ScGunBlock>(true) && ScGunBlock.IsKnown(value);
    }
    /// <summary>Whether this mod owns the crosshair right now. False for an empty hand, a knife, a grenade, any
    /// vanilla tool, a gun whose record cannot be read, a scoped shot, death, a dialog and third person.</summary>
    public static bool Active(ComponentPlayer player, Camera camera, bool scoped) {
        if (!ScUiSettings.GunCrosshair || player is null) return false;
        if (!HoldingGun(player)) return false;
        if (scoped || CsmcFirstPersonRenderer.ScopeOverlayActive) return false;
        if (player.ComponentHealth.Health <= 0) return false;
        if (player.ComponentGui.ModalPanelWidget is not null || DialogsManager.HasDialogs(player.GuiWidget)) return false;
        if (!player.ComponentGui.ControlsContainerWidget.IsVisible) return false;
        // Third person and the death camera draw the world from somewhere else; a combat reticle does not belong there.
        return camera is null || (!camera.Eye.HasValue && !camera.UsesMovementControls);
    }

    /// <summary>Screen-space size in the 960x540 reference the rest of the mod's HUD uses.</summary>
    public static float Scale(Vector2 viewport) => Math.Clamp(Math.Min(viewport.X / 960f, viewport.Y / 540f), .55f, 2.5f);

    public static void Draw(PrimitivesRenderer2D renderer, Camera camera, Color color, string style) {
        Vector2 size = camera.ViewportSize, centre = size * .5f;
        float scale = Scale(size);
        if (style == ScUiSettings.StyleVanilla) {
            // The vanilla artwork, tinted. The subtexture is sampled, never modified, so the shared atlas and
            // every other player's crosshair stay exactly as they were.
            Subtexture sub;
            try { sub = ContentManager.Get<Subtexture>("Textures/Atlas/Crosshair"); }
            catch (Exception e) { KnifeDiagnostics.WarnOnce("gun-crosshair-atlas", "Vanilla crosshair artwork unavailable; drawing the cross style: " + e.Message); DrawCross(renderer, centre, scale, color, camera); return; }
            float half = 11f * scale;
            var batch = renderer.TexturedBatch(sub.Texture, false, 0, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.AlphaBlend, SamplerState.PointClamp);
            batch.QueueQuad(centre - new Vector2(half), centre + new Vector2(half),
                0, sub.TopLeft, sub.BottomRight, color);
            batch.TransformTriangles(camera.ViewportMatrix);
            batch.Flush();
            return;
        }
        if (style == ScUiSettings.StyleDot) { DrawDot(renderer, centre, scale, color, camera); return; }
        DrawCross(renderer, centre, scale, color, camera);
    }

    static void DrawCross(PrimitivesRenderer2D renderer, Vector2 centre, float scale, Color color, Camera camera) {
        var batch = renderer.FlatBatch(0, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.AlphaBlend);
        float gap = 3f * scale, length = 8f * scale, half = 1f * scale;
        foreach (Vector2 direction in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) }) {
            Vector2 side = new Vector2(-direction.Y, direction.X) * half;
            Vector2 a = centre + direction * gap, b = centre + direction * (gap + length);
            batch.QueueQuad(a - side, b - side, b + side, a + side, 0, color);
        }
        batch.TransformTriangles(camera.ViewportMatrix);
        batch.Flush();
    }
    static void DrawDot(PrimitivesRenderer2D renderer, Vector2 centre, float scale, Color color, Camera camera) {
        var batch = renderer.FlatBatch(0, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.AlphaBlend);
        float half = 1.6f * scale;
        batch.QueueQuad(centre - new Vector2(half), centre + new Vector2(half, -half), centre + new Vector2(half), centre + new Vector2(-half, half), 0, color);
        batch.TransformTriangles(camera.ViewportMatrix);
        batch.Flush();
    }
}
