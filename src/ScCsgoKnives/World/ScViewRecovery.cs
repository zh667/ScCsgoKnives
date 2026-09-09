using System.Globalization;
using System.Xml.Linq;
using Engine;
namespace Game;

/// <summary>Explicit user recovery only. Never guess whether an unusual preference is intentional.</summary>
public static class ScViewRecovery {
    // Native SettingsManager defaults (ViewAngle=1 => 80 degrees, LookSensitivity=.5).
    public static void ResetPreferences() {
        SettingsManager.ViewAngle = 1f;
        SettingsManager.LookSensitivity = .5f;
    }
    public static bool SavedDefaults(XElement settings) {
        bool Match(string name, float expected) {
            var entries=settings.DescendantsAndSelf("Value").Where(e=>(string)e.Attribute("Name")==name).ToArray();
            return entries.Length==1 && float.TryParse((string)entries[0].Attribute("Value"), NumberStyles.Float,
                CultureInfo.InvariantCulture,out float actual) && actual==expected;
        }
        return Match("ViewAngle",1f) && Match("LookSensitivity",.5f);
    }
    public static string RestoreAndSave() {
        float view=SettingsManager.ViewAngle, sensitivity=SettingsManager.LookSensitivity;
        var project=GameManager.Project;
        var guns=project?.FindSubsystem<SubsystemScGunBlockBehavior>(false);
        var players=project?.FindSubsystem<SubsystemPlayers>(false);
        if(players is not null) foreach(var p in players.ComponentPlayers) guns?.SuspendScope(p);
        CsmcFirstPersonRenderer.SetScope(false,1f);
        ResetPreferences();
        if(players is not null) foreach(var p in players.ComponentPlayers) p.GameWidget.ActiveCamera.PrepareForDrawing(null);
        KnifeLog.Information($"[CS_VIEW_RECOVERY_0417] explicit reset view={view}->1 sensitivity={sensitivity}->0.5");
        try {
            // Use the game's own settings owner/path. SaveSettings can swallow errors or skip a busy lock,
            // so verify the two fields on disk instead of treating a return as proof of persistence.
            SettingsManager.SaveSettings();
            using var stream=Storage.OpenFile(ModsManager.SettingPath,OpenFileMode.Read);
            if(!SavedDefaults(XElement.Load(stream))) throw new System.IO.IOException("saved view values do not match");
            KnifeLog.Information("[CS_VIEW_RECOVERY_0417] saved values verified: "+ModsManager.SettingPath);
            return "视野及灵敏度已恢复并保存。";
        } catch(Exception e) {
            KnifeLog.Warning("[CS_VIEW_RECOVERY_0417] save not verified: "+e.Message);
            return "已恢复；保存未确认，请重试。";
        }
    }
}
