namespace Game;

/// <summary>Device detection is separate from the current input method.</summary>
public static class ScMobileControls {
    public static bool NativeGunFireAllowed(ComponentPlayer player) {
        if (!ScUiSettings.ButtonOnlyFire || !UsesTouchInput(player)) return true;
        // If a player hides the button later, explicitly fall back rather than leave them unable to shoot.
        if (!ScUiSettings.CustomButtons || !ScUiSettings.Layout(ScGunFunctions.Fire).Enabled) return true;
        var input = player.GameWidget.Input;
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
