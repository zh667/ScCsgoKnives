using System.IO.Compression;
using ZipArchive=System.IO.Compression.ZipArchive;
using System.Xml.Linq;
using Game;

// Static inheritance audit of actual installed XDB bytes, not a claim of running every creature's AI.
static class CreatureTemplateAudit {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> CheckEngineInheritance(string content,string extractedRoot,string package) {
        List<Result> results=[];
        using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(extractedRoot,"manifest.json")));
        using var vanilla=ZipFile.OpenRead(content);using var own=ZipFile.OpenRead(package);
        foreach(var row in manifest.RootElement.EnumerateArray()) {
            string owner=row.GetProperty("file").GetString();
            string[] names=owner.Contains("Ghoul")?["SAGhoul_Weak_01"]:owner.Contains("深海迷航")?["BlueWhale","GiantTurtle","Kraken"]:[];
            if(names.Length==0)continue;
            try {
                using var original=vanilla.GetEntry("Assets/Database.xml").Open();var database=XElement.Load(original);
                foreach(string path in Directory.EnumerateFiles(row.GetProperty("folder").GetString(),"*.xdb",SearchOption.AllDirectories)) {
                    using var input=File.OpenRead(path);ModsManager.CombineDataBase(database,input,owner);
                }
                using var xdb=own.GetEntry("Assets/ScCsgoKnivesDatabase.xdb").Open();ModsManager.CombineDataBase(database,xdb,"zh667.ScCsgoKnives");
                DatabaseManager.LoadDataBaseFromXml(database);
                foreach(string name in names) {
                    var values=DatabaseManager.FindEntityValuesDictionary(name,true);
                    var behavior=values.GetValue<TemplatesDatabase.ValuesDictionary>("ScDecoyBehavior",null);
                    results.Add(new("decoy-mod-engine-inheritance/"+name,behavior?.GetValue<string>("Class")=="Game.ComponentScDecoyBehavior","actual engine XDB merge and resolved component inheritance; no spawned creature"));
                }
            }catch(Exception e){results.Add(new("decoy-mod-engine-inheritance/"+owner,false,e.ToString()));}
        }
        return results;
    }
    internal static List<Result> Run(string mods,string content,string extractedRoot=null) {
        List<Result> results=[];var nodes=new Dictionary<string,XElement>(StringComparer.OrdinalIgnoreCase);var owners=new Dictionary<string,string>();
        void Read(XElement root,string owner) {
            foreach(var e in root.Descendants().Where(e=>e.Attribute("Guid") is not null)) {
                string id=(string)e.Attribute("Guid");
                if(nodes.TryGetValue(id,out var prior)) {
                    foreach(var a in e.Attributes())prior.SetAttributeValue(a.Name,a.Value);
                    foreach(var child in e.Elements())if(!prior.Elements().Any(p=>(string)p.Attribute("Guid")== (string)child.Attribute("Guid") && child.Attribute("Guid") is not null))prior.Add(new XElement(child));
                }else nodes[id]=new XElement(e);
                if(e.Name.LocalName=="EntityTemplate"&&owner!="vanilla")owners[id]=owner;
            }
        }
        using(var vanilla=ZipFile.OpenRead(content)) {using var input=vanilla.GetEntry("Assets/Database.xml").Open();Read(XElement.Load(input),"vanilla");}
        foreach(var path in Directory.EnumerateFiles(mods,"*.scmod").OrderBy(p=>p,StringComparer.Ordinal)) {
            if(Path.GetFileName(path).Contains("CS武器"))continue;
            ZipArchive zip;try{zip=ZipFile.OpenRead(path);}catch(InvalidDataException){zip=new(ModsManager.GetDecipherStream(File.OpenRead(path)));}
            using(zip)foreach(var e in zip.Entries.Where(e=>e.FullName.EndsWith(".xdb",StringComparison.OrdinalIgnoreCase))) {
                using var stream=e.Open();var root=XElement.Load(stream);Read(root,Path.GetFileName(path));
                results.Add(new("creature-source/"+Path.GetFileName(path)+"/"+e.FullName,true,$"entities={root.Descendants().Count(n=>n.Name.LocalName=="EntityTemplate")}; elements={string.Join(',',root.Descendants().Select(n=>n.Name.LocalName).Distinct())}"));
            }
        }
        if(extractedRoot is not null) {
            using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(extractedRoot,"manifest.json")));
            foreach(var row in manifest.RootElement.EnumerateArray()) {
                string owner=row.GetProperty("file").GetString();if(owner.Contains("CS武器"))continue;
                string source=Path.Combine(mods,owner);
                string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();
                if(hash!=row.GetProperty("sha256").GetString())throw new InvalidDataException("Stale extracted mod: "+owner);
                string folder=row.GetProperty("folder").GetString();
                foreach(string path in Directory.EnumerateFiles(folder,"*.xdb",SearchOption.AllDirectories)) {
                    // Outer XDBs were already read above; data/ holds the packaged-asset extractions.
                    if(!Path.GetRelativePath(folder,path).Replace('\\','/').StartsWith("data/"))continue;
                    var root=XElement.Load(path);Read(root,owner);
                    results.Add(new("creature-source/"+owner+"/"+Path.GetRelativePath(folder,path),true,"verified source sha256="+hash+"; extracted asset pack"));
                }
            }
        }
        IEnumerable<XElement> Chain(XElement node) {
            HashSet<string> visited=[];
            while(node is not null){yield return node;string parent=(string)node.Attribute("InheritanceParent");if(parent is null||!visited.Add(parent)||!nodes.TryGetValue(parent,out node))break;}
        }
        foreach(var (id,owner) in owners.OrderBy(p=>p.Value)) {
            var entity=nodes[id];var chain=Chain(entity).ToArray();
            var members=chain.SelectMany(e=>e.Elements().Where(c=>c.Name.LocalName=="MemberComponentTemplate"))
                .GroupBy(e=>(string)e.Attribute("Name")).ToDictionary(g=>g.Key,g=>g.ToArray());
            string Param(IEnumerable<XElement> definitions,string name)=>definitions.SelectMany(Chain).SelectMany(n=>n.Elements()).FirstOrDefault(p=>(string)p.Attribute("Name")==name)?.Attribute("Value")?.Value;
            var classes=members.Values.Select(e=>Param(e,"Class")).Where(s=>s is not null).ToArray();
            if(!classes.Any(c=>c.Contains("Creature")||c.Contains("Health")))continue;
            bool inherited=chain.Any(e=>(string)e.Attribute("Guid")=="3f077159-f492-419b-859a-bb051de6339f");
            string category=members.Values.Select(e=>Param(e,"Category")).FirstOrDefault(c=>c is not null)??"unknown";
            string flySpeed=members.Values.Select(e=>Param(e,"FlySpeed")).FirstOrDefault(c=>c is not null)??"unknown";
            results.Add(new("creature-audit/"+owner+"/"+(string)entity.Attribute("Name"),true,
                $"AICreature={inherited}; category={category}; flySpeed={flySpeed}; nativePath={classes.Contains("Game.ComponentPathfinding")}; nativeSelector={classes.Contains("Game.ComponentBehaviorSelector")}; classes={string.Join(',',classes)}; template audit only"));
        }
        return results;
    }
}
