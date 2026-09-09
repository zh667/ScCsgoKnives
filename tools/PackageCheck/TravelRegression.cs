using System.Reflection;
using System.Xml.Linq;
using Game;
using TemplatesDatabase;

static class TravelRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod) {
        var results=new List<Result>();
        void Check(string name,bool ok,string detail="")=>results.Add(new("travel/"+name,ok,detail));
        var type=mod.GetType("Game.ScGunTravel",true);var registryType=mod.GetType("Game.ScGunRegistry");var current=registryType.GetField("Current");var previous=current.GetValue(null);
        XElement Value(string n,object v)=>new("Value",new XAttribute("Name",n),new XAttribute("Type",v is int?"int":"string"),new XAttribute("Value",v));
        XElement Group(string n,params object[] children)=>new("Values",new XAttribute("Name",n),children);
        XElement G(XElement p,string n)=>p?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")==n);
        string Text(XElement p,string n)=>(string)p.Elements("Value").Single(e=>(string)e.Attribute("Name")==n).Attribute("Value");
        const int block=302;
        XElement World(string row,bool player=true) {
            var records=Group("Records");if(row!=null)records.Add(Value("1",row));
            var table=Group("GunRegistry",Value("Schema",3),Value("Next",row==null?1:2),Value("GrowthMode","CountAndGrow"),records);
            return new XElement("Project",new XElement("Subsystems",Group("BlocksManager",Value("302","ScGunBlock")),Group("ScGunBlockBehavior",Value("GunDataLayout",5),table)),
                new XElement("Entities",player?new XElement("Entity",new XAttribute("Name","MalePlayer"),Group("Inventory",Group("Slots",Group("0",Value("Contents",Terrain.MakeBlockValue(block,0,66)),Value("Count",1))))):null));
        }
        void Capture(XElement p,string path)=>type.GetMethod("Capture").Invoke(null,[p,path]);
        XElement Prepare(XElement p,string path){var plan=type.GetMethod("Prepare").Invoke(null,[p,path]);return plan==null?null:(XElement)plan.GetType().GetProperty("Document").GetValue(plan);}
        void Transfer(XElement from,XElement to){to.Element("Entities").Elements("Entity").Where(e=>(string)e.Attribute("Name")=="MalePlayer").Remove();to.Element("Entities").Add(new XElement(from.Element("Entities").Elements("Entity").First()));}
        try {
            current.SetValue(null,null);
            // Use a real valid current row to avoid hardcoding evolving validator requirements.
            var reg=Activator.CreateInstance(registryType);registryType.GetMethod("Allocate").Invoke(reg,[2,4,false,400,1500,0]);
            var real=(ValuesDictionary)registryType.GetMethod("Save").Invoke(reg,[0d]);string row=real.GetValue<ValuesDictionary>("Records").GetValue<string>("1");
            row=row.Replace("ct=0,","ct=1,").Replace("k=0,","k=345,").Replace("gl=0,","gl=3,").Replace("gv=0,","gv=1,").Replace("p=0,","p=51,");
            var main=World(row);Capture(main,"data:/Worlds/Test");var child=World(row,false);Transfer(main,child);
            string before=child.ToString();var imported=Prepare(child,"data:/Worlds/Test/Tartareosity");
            Check("preparation-does-not-mutate-source",before==child.ToString());
            var gun=G(imported.Element("Subsystems"),"ScGunBlockBehavior");var rows=G(G(gun,"GunRegistry"),"Records");
            Check("conflicting-id-allocates-new",Text(rows,"1")==row&&Text(rows,"2")==row&&imported.Descendants("Value").Any(e=>(string)e.Attribute("Name")=="Contents"&&(string)e.Attribute("Value")==Terrain.MakeBlockValue(block,0,130).ToString()));
            // Mutate imported gun, save/copy the player back: update same identity rather than cloning growth.
            rows.Elements("Value").Single(e=>(string)e.Attribute("Name")=="2").SetAttributeValue("Value",row.Replace("r=4,","r=3,").Replace("d=400,","d=390,"));
            Capture(imported,"data:/Worlds/Test/Tartareosity");Transfer(imported,main);var returned=Prepare(main,"data:/Worlds/Test");
            var backRows=G(G(G(returned.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry"),"Records");
            Check("return-preserves-identity-and-updates-state",Text(backRows,"1").Contains("r=3,")&&Text(backRows,"1").Contains("d=390,")&&backRows.Elements().Count()==1);
            for(int i=0;i<2;i++){returned=XElement.Parse(returned.ToString());Capture(returned,"data:/Worlds/Test");Check("same-world-reload-idempotent/"+i,Prepare(returned,"data:/Worlds/Test")==null);}
            var broken=World(row);Capture(broken,"data:/Worlds/Test");var packet=G(broken.Element("Entities").Element("Entity"),"ScGunTravel");G(packet,"Records").Elements("Value").First().SetAttributeValue("Value","corrupt");
            bool refused=false;try{Prepare(broken,"data:/Worlds/Test/Tartareosity");}catch(TargetInvocationException){refused=true;}Check("corrupt-packet-refused",refused);
            var full=World(row,false);G(G(full.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry").Elements("Value").Single(e=>(string)e.Attribute("Name")=="Next").SetAttributeValue("Value",1023);
            var source=World(row);Capture(source,"data:/Worlds/Test");Transfer(source,full);before=full.ToString();refused=false;try{Prepare(full,"data:/Worlds/Test/Tartareosity");}catch(TargetInvocationException){refused=true;}
            Check("full-target-refused-without-changes",refused&&before==full.ToString());
            foreach(var fault in new[]{"unknown-schema","duplicate-identity","foreign-world","target-holder"}) {
                var src=World(row);Capture(src,"data:/Worlds/Test");var dst=World(null,false);Transfer(src,dst);
                if(fault=="unknown-schema")G(G(dst.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry").Elements("Value").Single(e=>(string)e.Attribute("Name")=="Schema").SetAttributeValue("Value",99);
                if(fault=="duplicate-identity")dst.Element("Entities").Add(new XElement(dst.Element("Entities").Element("Entity")));
                if(fault=="foreign-world")G(dst.Element("Entities").Element("Entity"),"ScGunTravel").Elements("Value").Single(e=>(string)e.Attribute("Name")=="Source").SetAttributeValue("Value","data:/Worlds/Other");
                if(fault=="target-holder"){
                    dst=new XElement(main);dst.Element("Entities").Add(new XElement("Entity",new XAttribute("Name","Chest"),Group("Inventory",Group("Slots",Group("0",Value("Contents",Terrain.MakeBlockValue(block,0,66)),Value("Count",1))))));
                }
                before=dst.ToString();refused=false;try{Prepare(dst,fault=="target-holder"?"data:/Worlds/Test":"data:/Worlds/Test/Tartareosity");}catch(TargetInvocationException){refused=true;}
                Check(fault+"-refused",refused&&before==dst.ToString());
            }
            var emptyTarget=World(null,false);var charged=World(row.Replace("v=2,","v=34,").Replace("r=4,","r=0,").Replace("p=51,","p=0,").Replace("c=-1,","c=4.125,").Replace("rc=0,","rc=8.5,"));
            charged.Descendants("Value").Single(e=>(string)e.Attribute("Name")=="Contents").SetAttributeValue("Value",Terrain.MakeBlockValue(block,0,98));
            Capture(charged,"data:/Worlds/Test");Transfer(charged,emptyTarget);var chargedResult=Prepare(emptyTarget,"data:/Worlds/Test/Tartareosity");
            string charge=Text(G(G(G(chargedResult.Element("Subsystems"),"ScGunBlockBehavior"),"GunRegistry"),"Records"),"1");
            Check("zeus-remaining-charge-and-growth",charge.Contains("c=4.125,")&&charge.Contains("k=345,")&&charge.Contains("gl=3,"));
            var visibility=mod.GetType("Game.ScGunPartVisibility").GetMethod("Visible");
            foreach(string clip in new[]{"idle_sawedoff","lookat01_sawedoff","draw_sawedoff","shoot1_sawedoff"})Check("shell-hidden/"+clip,!(bool)visibility.Invoke(null,["sawedoff","shell",clip,.5f,false]));
            Check("shell-shown-during-reload",(bool)visibility.Invoke(null,["sawedoff","shell","reload_sawedoff",.6f,false]));
            Check("cz-front-hidden-only-after-transfer",!(bool)visibility.Invoke(null,["cz75a","magazine2","idle_cz75a",0f,true])&&(bool)visibility.Invoke(null,["cz75a","magazine","idle_cz75a",0f,true]));
            foreach(string blockName in new[]{"ScGunBlock","ScKnifeBlock"})Check("edit-unbound/"+blockName,!((Block)Activator.CreateInstance(mod.GetType("Game."+blockName))).IsEditable_(0));
            var functions=mod.GetType("Game.ScGunFunctions");var all=(string[])functions.GetField("All").GetValue(null);Check("all-controls-includes-fire",all.Length==10&&all.Distinct().Count()==10&&all.Contains("fire"));
            var fire=functions.GetMethod("Default").Invoke(null,["fire",false]);Check("old-layout-new-fire-default-off",!(bool)fire.GetType().GetProperty("Enabled").GetValue(fire));
        }catch(Exception e){Check("setup",false,e.ToString());}
        finally{current.SetValue(null,previous);}
        return results;
    }
}
