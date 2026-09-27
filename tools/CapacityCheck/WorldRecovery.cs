using System.Xml.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using TemplatesDatabase;

static class WorldRecovery {
    // Deliberately scoped to the two consented immutable exports, not a generic repair-by-guessing tool.
    static readonly Dictionary<string,string> Sources=new(){
        ["安(3).scworld"]="7B254FBA432B4A057C71F4D4C37DDCC5884BA8EC0C13EEAEFA5C443A87003B85",
        ["安1(3).scworld"]="5437F882677246A672706DCF504D14E187B8CB988E406422DBBAAB31C1FECE48"};
    static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    static XElement Group(XElement p,string n)=>p?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")==n);
    static string Text(XElement p,string n)=>(string)p?.Elements("Value").SingleOrDefault(e=>(string)e.Attribute("Name")==n)?.Attribute("Value");
    static ValuesDictionary Read(XElement x){var d=new ValuesDictionary();d.ApplyOverrides(new XElement(x));return d;}
    static byte[] XmlBytes(XElement x){using var s=new MemoryStream();x.Save(s);return s.ToArray();}
    internal static int Run(string[] args) {
        // dll, source-directory, output-directory; never overwrites an existing output.
        var mod=new Module(args[0]);var output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
        var inputs=Sources.ToDictionary(e=>e.Key,e=>File.ReadAllBytes(Path.Combine(args[1],e.Key)));
        foreach(var pair in inputs)if(Hash(pair.Value)!=Sources[pair.Key])throw new Exception("Source hash mismatch");
        using var donorZip=new System.IO.Compression.ZipArchive(new MemoryStream(inputs["安(3).scworld"]));
        XElement ReadEntry(System.IO.Compression.ZipArchive z,string name){using var s=z.GetEntry(name).Open();return XElement.Load(s);}
        var donor=ReadEntry(donorZip,"Project.xml");var donorGun=Group(donor.Element("Subsystems"),"ScGunBlockBehavior");
        var expectedRows=Group(Group(donorGun,"GunRegistry"),"Records").ToString();
        var results=new List<object>();var staged=new Dictionary<string,byte[]>();
        foreach(var pair in inputs) {
            using var sourceZip=new System.IO.Compression.ZipArchive(new MemoryStream(pair.Value));
            if(sourceZip.Entries.Select(e=>e.FullName).Distinct().Count()!=sourceZip.Entries.Count)throw new Exception("Duplicate zip entries");
            var changed=new Dictionary<string,byte[]>();var inspections=new List<object>();
            foreach(string entry in new[]{"Project.xml","Project.bak"}) {
                var original=ReadEntry(sourceZip,entry);var doc=new XElement(original);
                var info=Group(doc.Element("Subsystems"),"GameInfo");var donorInfo=Group(donor.Element("Subsystems"),"GameInfo");
                if(Text(info,"WorldSeed")!=Text(donorInfo,"WorldSeed")||Text(info,"WorldName")!=Text(donorInfo,"WorldName"))throw new Exception("World lineage mismatch");
                var gun=Group(doc.Element("Subsystems"),"ScGunBlockBehavior");
                // Restore complete verified gun state (including identity map and queue watermarks).
                // Every unrelated target subsystem/entity and the target's world progression remain untouched.
                if(pair.Key=="安1(3).scworld") {
                    if(Group(Group(gun,"GunRegistry"),"Records")?.HasElements==true)throw new Exception("Target acquired records; refusing replacement");
                    var copy=new XElement(donorGun);if(gun is null)doc.Element("Subsystems").Add(copy);else gun.ReplaceWith(copy);gun=copy;
                }
                var table=Group(gun,"GunRegistry");var reg=mod.Call("ScGunRegistry",null,"Load",Read(table),0d);
                if((bool)mod.Get(reg,"Disabled")||(int)mod.Get(reg,"QuarantinedCount")!=0||(int)mod.Get(reg,"Count")!=1022)throw new Exception("Invalid source table");
                var saved=new XElement("Values",new XAttribute("Name","GunRegistry"));((ValuesDictionary)mod.Call("ScGunRegistry",reg,"Save",0d)).Save(saved);
                if(Group(saved,"Records").ToString()!=expectedRows)throw new Exception("Original gun attributes changed");
                table.ReplaceWith(saved);gun.Elements("Value").Single(e=>(string)e.Attribute("Name")=="GunDataLayout").SetAttributeValue("Value",6);
                mod.Call("ScGunTravel",null,"CaptureSaved",doc);
                mod.Call("ScGunTravel",null,"ValidateCaptured",doc,Text(gun,"GunTravelSource"));
                int count=(int)mod.Call("ScGunLoadIntegrity",null,"ValidateReferences",doc);if(count!=2)throw new Exception("Unexpected live gun references");
                // Importing this whole archive under a new folder must not clone/renumber carried guns.
                if(mod.Call("ScGunTravel",null,"Prepare",doc,"data:/RecoveredCopy")!=null)throw new Exception("Whole-world copy incorrectly needs gun import");
                for(int n=0;n<2;n++) {
                    reg=mod.Call("ScGunRegistry",null,"Load",Read(saved),0d);var after=new XElement("Values",new XAttribute("Name","GunRegistry"));
                    ((ValuesDictionary)mod.Call("ScGunRegistry",reg,"Save",0d)).Save(after);if(!XNode.DeepEquals(saved,after))throw new Exception("Roundtrip changed state");saved=after;
                }
                int first=(int)mod.Call("ScGunRegistry",reg,"Allocate",0,0,false,1500,1500,0);if(first!=1024)throw new Exception("No new gun capacity");
                // New gun is a test-only in-memory operation, not an item gifted to the exported world.
                XElement WithoutGunData(XElement x) {var clone=new XElement(x);Group(clone.Element("Subsystems"),"ScGunBlockBehavior")?.Remove();foreach(var e in clone.Descendants("Values").Where(e=>(string)e.Attribute("Name")=="ScGunTravel").ToArray())e.Remove();return clone;}
                if(!XNode.DeepEquals(WithoutGunData(original),WithoutGunData(doc)))throw new Exception("Unrelated world content changed");
                changed[entry]=XmlBytes(doc);inspections.Add(new{entry,checkedItems=count,records=1022,next=1024,freeSlots=65536,twoRoundtrips=true,testAllocation=first});
            }
            using var buffer=new MemoryStream();
            using(var dest=new System.IO.Compression.ZipArchive(buffer,System.IO.Compression.ZipArchiveMode.Create,true))foreach(var entry in sourceZip.Entries) {
                var target=dest.CreateEntry(entry.FullName,System.IO.Compression.CompressionLevel.Optimal);target.LastWriteTime=entry.LastWriteTime;
                using var write=target.Open();if(changed.TryGetValue(entry.FullName,out var bytes))write.Write(bytes);else {using var read=entry.Open();read.CopyTo(write);}
            }
            byte[] result=buffer.ToArray();
            using(var native=Game.ZipArchive.Open(new MemoryStream(result)))foreach(var entry in native.ReadCentralDir()) {
                using var extracted=new MemoryStream();native.ExtractFile(entry,extracted);
                if(changed.TryGetValue(entry.FilenameInZip,out var expected)) {if(!extracted.ToArray().SequenceEqual(expected))throw new Exception("Native XML readback failed");}
                else {using var s=sourceZip.GetEntry(entry.FilenameInZip).Open();using var b=new MemoryStream();s.CopyTo(b);if(!b.ToArray().SequenceEqual(extracted.ToArray()))throw new Exception("Non-project member changed");}
            }
            string name=Path.GetFileNameWithoutExtension(pair.Key)+"-扩容恢复副本.scworld";
            if(File.Exists(Path.Combine(output,name)))throw new Exception("Output already exists; never overwrite");
            staged.Add(name,result);results.Add(new{file=name,sha256=Hash(result),source=pair.Key,sourceSha256=Sources[pair.Key],changedEntries=changed.Keys,inspections});
        }
        foreach(var pair in inputs)if(Hash(File.ReadAllBytes(Path.Combine(args[1],pair.Key)))!=Sources[pair.Key])throw new Exception("Source changed during recovery");
        foreach(var pair in staged){using var f=new FileStream(Path.Combine(output,pair.Key),FileMode.CreateNew);f.Write(pair.Value);}
        File.WriteAllText(Path.Combine(output,"world-recovery.json"),JsonSerializer.Serialize(new{coreSha256=Hash(File.ReadAllBytes(mod.Path)),originalsUnchanged=true,worlds=results,androidAcceptance=false},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        Console.WriteLine("Recovered both archives with original IDs and 65536 free slots; native readback and detached roundtrips passed.");return 0;
    }
}
