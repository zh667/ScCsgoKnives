using System.Xml.Linq;
namespace Game;

/// <summary>Transport unclaimed choices through older writers that know only GrantedPlayers.
/// The live starter subsystem owns the state; the capsule is only its cross-version save carrier.</summary>
internal static class ScStarterCompatibility {
    const string Key="StarterChoices", GroupName="ScStarterEquipment", Pending="PendingPlayers";
    internal static void Restore(XElement project,XElement capsule){
        if(capsule.Element(Key)?.Attribute(Pending) is not {} pending)return;
        var subs=project.Element("Subsystems");var group=ScCompatibility.Group(subs,GroupName);
        if(group is null){group=new XElement("Values",new XAttribute("Name",GroupName));subs.Add(group);}
        // A present field, including an empty one after a choice, is newer authoritative state.
        if(ScCompatibility.Text(group,Pending) is null)group.Add(ScCompatibility.Field(Pending,pending.Value));
    }
    internal static void Preserve(XElement project,XElement capsule){
        var pending=ScCompatibility.Text(ScCompatibility.Group(project.Element("Subsystems"),GroupName),Pending);
        if(pending is null)return;
        capsule.Element(Key)?.Remove();
        if(pending.Length>0)capsule.Add(new XElement(Key,new XAttribute(Pending,pending)));
        var state=ScCompatibility.Group(project.Element("Subsystems"),ScCompatibility.Key);
        if(state is null)return;
        state.Elements("Value").Where(v=>(string)v.Attribute("Name")=="Capsule").Remove();
        state.Add(ScCompatibility.Field("Capsule",capsule.ToString(SaveOptions.DisableFormatting)));
    }
}
