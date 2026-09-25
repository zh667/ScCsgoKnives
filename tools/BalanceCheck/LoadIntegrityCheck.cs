using Game;
using System.Xml.Linq;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

static class LoadIntegrityCheck {
    static XElement G(string n, params object[] children)=>new("Values",new XAttribute("Name",n),children);
    static XElement V(string n, object value)=>new("Value",new XAttribute("Name",n),new XAttribute("Type",value is int?"int":"string"),new XAttribute("Value",value));
    static XElement Fixture(int id=7, bool records=false) {
        var registry=new ScGunRegistry();for(int i=0;i<7;i++)registry.Allocate(0,13,false,999);
        var table=G("GunRegistry");registry.Save(0).Save(table);
        return new XElement("Project",new XElement("Subsystems",G("BlocksManager",V("315","ScGunBlock")),
            records?G("ScGunBlockBehavior",V("GunDataLayout",5),table):G("ScGunBlockBehavior")),
            new XElement("Entities",new XElement("Entity",new XAttribute("Name","MalePlayer"),
                G("CreativeInventory",G("Slots",G("Slot0",V("Contents",Terrain.MakeBlockValue(315,0,GunSpec.WithId(0,id)))))))));
    }
    public static void Run(Action<string,bool,string> check) {
        void T(string n,Action test){try{test();check("integrity/"+n,true,n);}catch(Exception e){check("integrity/"+n,false,e.ToString());}}
        void Require(bool ok){if(!ok)throw new Exception("assertion failed");}
        void Refused(XElement doc) {
            string before=doc.ToString();bool refused=false;
            try{ScGunLoadIntegrity.ValidateReferences(doc);}catch(InvalidOperationException){refused=true;}
            Require(refused&&doc.ToString()==before);
        }
        T("lost-table-is-not-a-new-world",()=>Refused(Fixture()));
        T("same-schema-missing-record",()=>Refused(Fixture(8,true)));
        T("fresh-template-does-not-need-a-record",()=>Require(ScGunLoadIntegrity.ValidateReferences(Fixture(0))==0));
        T("valid-old-reference-uses-saved-block-index",()=>Require(ScGunLoadIntegrity.ValidateReferences(Fixture(7,true))==1));
        foreach(string holder in new[]{"Inventory","Stash","SushiChannel"})T(holder,()=>{
            var doc=Fixture();var inventory=doc.Descendants("Values").Single(e=>(string)e.Attribute("Name")=="CreativeInventory");inventory.SetAttributeValue("Name",holder);
            inventory.Descendants("Values").Last().Add(V("Count",1));Refused(doc);
        });
        foreach(string holder in new[]{"Pickables","Projectiles","MovingBlocks"})T(holder,()=>{
            var doc=Fixture();doc.Element("Entities").Remove();int value=Terrain.MakeBlockValue(315,0,GunSpec.WithId(0,7));
            doc.Element("Subsystems").Add(holder=="MovingBlocks"?G(holder,G("MovingBlockSets",G("0",V("Blocks",$"{value},0,0,0")))):G(holder,G(holder,G("0",V("Value",value),V("Count",1)))));Refused(doc);
        });
        T("missing-table-with-layout",()=>{var d=Fixture(0);d.Element("Subsystems").Elements().Last().Add(V("GunDataLayout",5));Refused(d);});
        T("mismatched-model",()=>{var d=Fixture(7,true);d.Descendants("Value").Single(e=>(string)e.Attribute("Name")=="Contents").SetAttributeValue("Value",Terrain.MakeBlockValue(315,0,GunSpec.WithId(1,7)));Refused(d);});
        foreach(string version in new[]{"1.0.0","1.2.0","1.5.1"})T("release-backup/"+version,()=>{
            var doc=Fixture(7,true);doc.Element("Subsystems").Add(G("UsedMods",G("Mods",G("0",V("PackageName",ScGun0282Migration.Package),V("Version",version)))));
            var dir=Directory.CreateTempSubdirectory("release-backup-");string path=Path.Combine(dir.FullName,"Project.xml");doc.Save(path);
            File.WriteAllBytes(Path.Combine(dir.FullName,"Chunks.dat"),[1,2,3]);Directory.CreateDirectory(Path.Combine(dir.FullName,"OtherMod"));File.WriteAllText(Path.Combine(dir.FullName,"OtherMod/state"),"keep");
            byte[] before=File.ReadAllBytes(path);
            var world=(WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo));world.DirectoryName=dir.FullName;
            var broken=new XElement(doc);world.DirectoryName=Path.Combine(dir.FullName,"missing");bool refused=false;
            try{ScGunSchemaUpgrade.BeforeReleaseLoad(broken,world);}catch{refused=true;}
            Require(refused&&XNode.DeepEquals(doc,broken));world.DirectoryName=dir.FullName;
            string backup=ScGunSchemaUpgrade.BeforeReleaseLoad(doc,world);using var zip=ZipFile.OpenRead(backup);
            Require(zip.GetEntry("OtherMod/state")!=null&&zip.GetEntry("Chunks.dat")!=null&&before.SequenceEqual(File.ReadAllBytes(path)));
            Require(ScGunSchemaUpgrade.BeforeReleaseLoad(doc,world)==null);
            string retry=ScGunSchemaUpgrade.BeforeReleaseLoad(XElement.Load(path),world);Require(retry!=backup&&File.Exists(backup));
        });
        using var cs2=JsonDocument.Parse(File.ReadAllText("src/ScCsgoKnives/AnimationData/cs2_weapons.json"));
        foreach(var spec in GunSpec.All)T("cs2-lv0/"+spec.Name,()=>{
            int variant=Array.IndexOf(GunSpec.All,spec);var row=cs2.RootElement.GetProperty("Guns").GetProperty(spec.Name);
            foreach(var pair in new[]{("CycleSeconds",spec.CycleSeconds),("CycleSecondsAlternate",spec.CycleSecondsAlternate),("BurstCycleSeconds",spec.BurstCycleSeconds),("BurstShotSeconds",spec.BurstShotSeconds)})
                // The second vdata cycle on pistols without a second firing mode is dormant metadata.
                if(pair.Item1!="CycleSecondsAlternate" || spec.CycleSecondsAlternate>0)
                    Require(Math.Abs(ScGunGrowth.ShotInterval(variant,pair.Item2,0)-row.GetProperty(pair.Item1).GetDouble())<.00001);
            if(spec.Name=="taser")Require(Math.Abs(ScGunGrowth.RechargeSeconds(spec,0)-30)<.00001);
        });
        string local=".tmp/migration-20260925/World7-Project.bak";
        if(File.Exists(local))T("actual-world7-copy-rejected-unchanged",()=>{
            byte[] before=File.ReadAllBytes(local);Refused(XElement.Load(local));Require(before.SequenceEqual(File.ReadAllBytes(local)));
        });
        string oldWorld=".tmp/migration-20260925/World-Project.xml";
        if(File.Exists(oldWorld))T("actual-old-sushibox-model-conflict-rejected",()=>Refused(XElement.Load(oldWorld)));
        for(int i=1;i<7;i++){
            string path=$".tmp/migration-20260925/World{(i==0?"":i.ToString())}-Project.xml";
            if(File.Exists(path))T("actual-intact-world-copy/"+i,()=>{
                var doc=XElement.Load(path);string before=doc.ToString();
                ScGunLoadIntegrity.ValidateReferences(doc);Require(doc.ToString()==before);
            });
        }
        T("old-zeus-running-cycles-survive-new-base",()=>{
            foreach(float cycle in new[]{10f,5f,1f,10f/.65f,1f/.65f}){
                var rows=G("Records",V("7",$"v=34,r=0,s=0,d=71,m=100,n=19,c=0.4,p=0,ct=0,k=0,gl=0,gp=-1,gv=0,rc={cycle.ToString(System.Globalization.CultureInfo.InvariantCulture)},ov=0,kc=0"));
                var data=new TemplatesDatabase.ValuesDictionary();data.ApplyOverrides(G("GunRegistry",V("Schema",6),V("Next",8),rows));
                for(int pass=0;pass<2;pass++){
                    var reg=ScGunRegistry.Load(data,100);Require(reg.TryGetSnapshot(7,out var s)&&Math.Abs(s.RechargeReadyAt-100.4)<.001&&Math.Abs(s.RechargeCycleSeconds-cycle)<.001&&s.Rounds==0&&s.Durability==71);
                    data=reg.Save(100);
                }
            }
        });
    }
}
