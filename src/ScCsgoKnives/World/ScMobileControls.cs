namespace Game;

/// <summary>Device detection is separate from the current input method.</summary>
public static class ScMobileControls {
    public static bool IsMobilePlatform(VersionsManager.Platform platform) =>
        platform is VersionsManager.Platform.Android or VersionsManager.Platform.IOS;
    public static bool IsMobileDevice => IsMobilePlatform(VersionsManager.CurrentPlatform)
        || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    public static bool ShouldUseTouchInput(bool mobileDevice, bool touchInput) => mobileDevice && touchInput;
    public static bool UsesTouchInput(ComponentPlayer player) =>
        ShouldUseTouchInput(IsMobileDevice, player.ComponentInput.IsControlledByTouch);
}
