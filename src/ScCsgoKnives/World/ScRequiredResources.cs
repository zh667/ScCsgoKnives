using System.Xml.Linq;
namespace Game;

public static class ScRequiredResources {
    public const string Version="1.0.0";
    public static void Validate() {
        // Validate before world parsing; do not let missing resources masquerade as missing skins.
        var marker=ContentManager.Get<XElement>("ScCsgoResources");
        if((string)marker.Attribute("Version")!=Version || (string)marker.Attribute("Edition")!="Full"
            || (string)marker.Attribute("Format")!="1" || ScAnimationResources.Assembly.GetName().Version?.Major!=1)
            throw new InvalidOperationException("请安装匹配的 CS 武器完整资源前置包 "+Version+"，不要只安装主包。");
    }
}
