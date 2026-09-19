namespace Game;

/// <summary>Device detection is separate from the current input method.</summary>
public static class ScMobileControls {
    public static bool NativeGunFireAllowed(ComponentPlayer player) {
        return NativeGunFireAllowedFor(UsesTouchInput(player),player.GameWidget.Input);
    }
    public static bool NativeGunFireAllowedFor(bool touch,WidgetInput input) {
        if (!ScUiSettings.ButtonOnlyFire || !touch) return true;
        // Button visibility is independent of the firing policy. External touch
        // mappers inject keyboard/mouse input and do not need our overlay enabled.
        // Do not erase the merged PlayerInput: keyboard/touch mappers and gamepads still work on Android.
        return input.IsKeyOrMouseDown("Dig") || input.IsKeyOrMouseDown("Hit")
            || input.IsGamepadDown("Dig") || input.IsGamepadDown("Hit");
    }
    public static bool IsMobilePlatform(VersionsManager.Platform platform) =>
        platform is VersionsManager.Platform.Android or VersionsManager.Platform.IOS;
    public static bool IsMobileDevice => IsMobilePlatform(VersionsManager.CurrentPlatform)
        || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    public static bool ShouldUseTouchInput(bool mobileDevice, bool touchInput) => mobileDevice && touchInput;
    public static bool UsesTouchInput(ComponentPlayer player) =>
        ShouldUseTouchInput(IsMobileDevice, player.ComponentInput.IsControlledByTouch);
}
