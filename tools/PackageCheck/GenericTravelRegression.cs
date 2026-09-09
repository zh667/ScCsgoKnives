using System.Reflection;
using System.Xml.Linq;
using System.IO.Compression;
using Engine;
using Game;
using TemplatesDatabase;

static class GenericTravelRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod,string sourceSnapshot) {
        List<Result> results=[];
        void Check(string n,bool ok,string d="")=>results.Add(new("generic-travel/"+n,ok,d));
        var type=mod.GetType("Game.ScGunTravel");
        XElement V(string n,object v)=>new("Value",new XAttribute("Name",n),new XAttribute("Type",v is int?"int":"string"),new XAttribute("Value",v));
        XElement G(string n,params object[] children)=>new("Values",new XAttribute("Name",n),children);
        XElement Group(XElement p,string n)=>p?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")==n);
        string Text(XElement p,string n)=>(string)p?.Elements("Value").SingleOrDefault(e=>(string)e.Attribute("Name")==n)?.Attribute("Value");
        XElement Guns(XElement p)=>Group(p.Element("Subsystems"),"ScGunBlockBehavior");
        void Capture(XElement p,string path)=>type.GetMethod("Capture").Invoke(null,[p,path]);
        object Plan(XElement p,string path)=>type.GetMethod("Prepare").Invoke(null,[p,path]);
        XElement Prepared(XElement p,string path){var plan=Plan(p,path);return plan==null?null:(XElement)plan.GetType().GetProperty("Document").GetValue(plan);}
        XElement Table(XElement p)=>Group(Guns(p),"GunRegistry");
        XElement FreshTarget(XElement source) => new("Project",new XElement("Subsystems",new XElement(Group(source.Element("Subsystems"),"BlocksManager")),
            G("ScGunBlockBehavior",V("GunDataLayout",5),V("GunTravelWorldIdentity",Guid.NewGuid().ToString("N")),
                G("GunRegistry",V("Schema",4),V("Next",1),V("GrowthMode","Unset"),G("Records")))),new XElement("Entities",source.Element("Entities").Elements().Where(e=>Group(e,"ScGunTravel") is not null).Select(e=>new XElement(e))));
        bool Refused(XElement p,string target) {string before=p.ToString();try{Plan(p,target);return false;}catch(TargetInvocationException){return p.ToString()==before;}}
        try {
            const int block=302;const string main="data:/Worlds/Origin",dimension="data:/OtherMod/Dimensions/Moon";
            string row="v=0,r=7,s=0,d=750,m=2250,n=12,c=-1,p=180,ct=1,k=1234,gl=10,gp=-1,gv=1,rc=0,ov=0";
            foreach(bool creative in new[]{false,true}) {
                int value=Terrain.MakeBlockValue(block,0,64);var data=new ValuesDictionary();
                if(creative) {var inv=new ComponentCreativeInventory{OpenSlotsCount=1};inv.m_slots.Add(value);inv.Save(data,null);}
                else {var inv=new ComponentInventory();inv.m_slots.Add(new(){Value=value,Count=1});inv.Save(data,null);}
                var inventory=G(creative?"CreativeInventory":"Inventory");data.Save(inventory);
                Check("native-save-count-shape/"+creative,inventory.Descendants("Value").Any(v=>(string)v.Attribute("Name")=="Count")==!creative);
                var source=new XElement("Project",new XElement("Subsystems",G("BlocksManager",V("302","ScGunBlock")),
                    G("ScGunBlockBehavior",V("GunDataLayout",5),V("GunTravelWorldIdentity","0123456789abcdef0123456789abcdef"),G("GunRegistry",V("Schema",4),V("Next",2),V("GrowthMode","CountAndGrow"),G("Records",V("1",row))))),
                    new XElement("Entities",new XElement("Entity",new XAttribute("Name","CustomModPlayer"),G("Player"),inventory)));
                Capture(source,main);
                var packet=Group(source.Element("Entities").Element("Entity"),"ScGunTravel");
                Check("native-inventory-not-skipped/"+creative,Text(packet,"Version")=="3"&&Group(packet,"Records").Elements().Count()==1);
                var wholeCopy=new XElement(source);Guns(wholeCopy).Add(V("GunTravelSource",main));
                Check("whole-world-copy-keeps-local-records/"+creative,Plan(wholeCopy,"data:/Worlds/RestoredCopy")==null);
                var futureCopy=new XElement(wholeCopy);
                Group(futureCopy.Element("Entities").Element("Entity"),"ScGunTravel").Elements("Value").Single(v=>(string)v.Attribute("Name")=="Version").SetAttributeValue("Value","99");
                Check("unknown-protocol-not-bypassed-by-world-copy/"+creative,Refused(futureCopy,"data:/Worlds/RestoredCopy"));
                type.GetMethod("ValidateCaptured").Invoke(null,[source,main]);
                var target=FreshTarget(source);string before=target.ToString();var imported=Prepared(target,dimension);
                Check("unrelated-dimension-import/"+creative,imported is not null&&target.ToString()==before&&Text(Group(Table(imported),"Records"),"1")==row);
                Capture(imported,dimension);
                source.Element("Entities").ReplaceNodes(imported.Element("Entities").Elements().Select(e=>new XElement(e)));
                var returned=Prepared(source,main);
                Check("return-updates-same-identity/"+creative,Group(Table(returned),"Records").Elements().Count()==1&&Text(Group(Table(returned),"Records"),"1")==row);
                for(int i=0;i<2;i++){returned=XElement.Parse(returned.ToString());Capture(returned,main);Check($"reload-idempotent/{creative}/{i}",Plan(returned,main)==null);}
                var incomplete=new XElement(target);Group(Group(incomplete.Element("Entities").Element("Entity"),"ScGunTravel"),"Records").RemoveNodes();
                Group(Group(incomplete.Element("Entities").Element("Entity"),"ScGunTravel"),"Identities").RemoveNodes();
                Check("old-zero-record-bug-refused/"+creative,Refused(incomplete,dimension));
                if(!creative) {
                    var malformed=new XElement(source);malformed.Descendants("Value").Where(v=>(string)v.Attribute("Name")=="Count").Remove();
                    Capture(malformed,main);Check("missing-survival-count-not-guessed",Refused(FreshTarget(malformed),dimension));
                }
            }
            if(sourceSnapshot is not null) {
                using var zip=ZipFile.OpenRead(sourceSnapshot);using var stream=zip.GetEntry("Project.xml").Open();var original=XElement.Load(stream);
                var source=new XElement(original);string before=original.ToString();string origin=Text(Guns(source),"GunTravelSource");
                int records=Group(Table(source),"Records").Elements().Count();Capture(source,origin);
                type.GetMethod("ValidateCaptured").Invoke(null,[source,origin]);
                int carried=source.Element("Entities").Elements().Sum(e=>Group(Group(e,"ScGunTravel"),"Records")?.Elements().Count()??0);
                Check("user-six-gun-snapshot-captured",carried==6&&records==6&&before==original.ToString(),"Read-only pre-travel snapshot; real CreativeInventory lacks Count");
                var imported=Prepared(FreshTarget(source),dimension);
                var rows=Group(Table(imported),"Records").Elements("Value").Select(v=>(string)v.Attribute("Value")).Order().ToArray();
                var expected=Group(Table(source),"Records").Elements("Value").Select(v=>(string)v.Attribute("Value")).Order().ToArray();
                Check("user-six-guns-all-fields-preserved",rows.SequenceEqual(expected));
                Capture(imported,dimension);source.Element("Entities").ReplaceNodes(imported.Element("Entities").Elements().Select(e=>new XElement(e)));
                var returned=Prepared(source,origin);
                Check("user-six-guns-return-no-duplicates",Group(Table(returned),"Records").Elements().Count()==6
                    &&Group(Table(returned),"Records").Elements("Value").Select(v=>(string)v.Attribute("Value")).Order().SequenceEqual(expected));
            }
        }catch(Exception e){Check("failure",false,e.ToString());}
        return results;
    }
}
