using System.Reflection;
using System.Xml.Linq;
using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>Session-only discoverable test switch: seven title taps, never a saved preference.</summary>
public sealed class ScTestEntryGate {
    int m_taps; double m_last=double.NegativeInfinity;
    public bool Unlocked { get; private set; }
    public bool Tap(double now) {
        if(!double.IsFinite(now))return false;
        m_taps=now>=m_last && now-m_last<=3 ? m_taps+1 : 1;m_last=now;
        if(m_taps>=7)Unlocked=true;
        return Unlocked;
    }
    public void Reset(){m_taps=0;m_last=double.NegativeInfinity;Unlocked=false;}
}

/// <summary>Optional bridge, no dependency on Ghoul/NUI and no alternate world-transfer implementation.</summary>
public static class ScGhoulTestBridge {
    static bool s_busy;
    static XElement Group(XElement p,string name)=>p?.Elements("Values").SingleOrDefault(x=>(string)x.Attribute("Name")==name);
    static string Text(XElement p,string name)=>(string)p?.Elements("Value").SingleOrDefault(x=>(string)x.Attribute("Name")==name)?.Attribute("Value");
    public static MethodInfo TransferMethod(Type type) => type?.FullName=="SAGhoul.Tartareosity.SubsystemTartareosity"
        ? type.GetMethod("TransPortal",BindingFlags.Public|BindingFlags.Instance,null,Type.EmptyTypes,null) : null;
    public static void VerifyCheckpoint(XElement xml,ValuesDictionary expected,string world) {
        var gun=Group(xml.Element("Subsystems"),"ScGunBlockBehavior");
        var wanted=new XElement("Values",new XAttribute("Name","GunRegistry"));expected.Save(wanted);
        if(!XNode.DeepEquals(wanted,Group(gun,"GunRegistry")))throw new InvalidOperationException("枪械记录写盘核对失败；未开始穿越");
        ScGunTravel.ValidateCaptured(xml,world);
    }
    public static string Request() {
        if(s_busy || ScreensManager.IsAnimating)return "正在切换界面，请稍后再试。";
        var project=GameManager.Project;if(project is null)return "请先进入用于测试的世界副本。";
        var target=project.Subsystems.FirstOrDefault(s=>TransferMethod(s.GetType()) is not null);
        if(target is null)return "未找到尸鬼 2.0 的穿越接口；不需要安装 NUI。";
        var guns=project.FindSubsystem<SubsystemScGunBlockBehavior>(false);
        if(guns?.ReadyForTravel!=true)return "枪械数据或动作尚未就绪，请返回游戏等待结算。";
        s_busy=true;
        bool dispatched=false;
        try {
            string world=project.FindSubsystem<SubsystemGameInfo>(true).DirectoryName;
            var expected=ScGunRegistry.Current.Save(project.FindSubsystem<SubsystemTime>(true).GameTime);
            GameManager.SaveProject(true,true);
            using(var stream=Storage.OpenFile(Storage.CombinePaths(world,"Project.xml"),OpenFileMode.Read))
                VerifyCheckpoint(XElement.Load(stream),expected,world);
            string backup=ScLiveWorldBackup.Snapshot(project.FindSubsystem<SubsystemTerrain>(true),world,"ScCsgoKnives-before-ghoul-test-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8],ScGunRegistry.Schema);
            KnifeLog.Information("[GHOUL_TEST_0419] verified backup="+backup+"; invoking original TransPortal");
            dispatched=true;
            TransferMethod(target.GetType()).Invoke(target,null);
            return "已调用尸鬼穿越流程；未切换时请查看日志。";
        } catch(Exception e) {
            Exception reason=e is TargetInvocationException {InnerException:{} inner}?inner:e;
            KnifeLog.Warning("[GHOUL_TEST_0419] stage="+(dispatched?"original-transfer":"preflight/backup")+" "+reason);
            if(dispatched)return "已调用尸鬼，但穿越未完成。请查看游戏日志中的 GHOUL_TEST_0419。";
            if(reason is System.IO.IOException)return "备份失败：世界文件被占用或无法读取。未开始穿越，详情见日志。";
            return "未开始穿越。"+(reason.Message.Length<=65?reason.Message:"预检或备份失败，详情见游戏日志。");
        }finally{s_busy=false;}
    }
}
