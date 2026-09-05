using System.IO.Compression;
using ZipArchive=System.IO.Compression.ZipArchive;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Game;
using TemplatesDatabase;

/// <summary>Uses the actual API database merge and recipe matcher; no game world or graphics.</summary>
static class SurvivalPackageIntegration {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod,string package,string vanillaContent) {
        var results=new List<Result>();
        void Check(string name,bool ok,string detail)=>results.Add(new("integration/"+name,ok,detail));
        using var original=ZipFile.OpenRead(vanillaContent);using var zip=ZipFile.OpenRead(package);
        XElement Read(ZipArchive archive,string suffix) {
            using var stream=archive.Entries.Single(e=>e.FullName.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)).Open();return XElement.Load(stream);
        }
        try {
            var database=Read(original,"Assets/Database.xml");
            using var xdb=zip.GetEntry("Assets/ScCsgoKnivesDatabase.xdb").Open();
            ModsManager.CombineDataBase(database,xdb,"zh667.ScCsgoKnives");
            DatabaseManager.LoadDataBaseFromXml(database);
            foreach (string name in new[] {"Wolf_Gray","Bear_Brown","Wildboar","Bull_Brown","Rhino"}) {
                var entity=DatabaseManager.FindEntityValuesDictionary(name,true);
                var behavior=entity.GetValue<ValuesDictionary>("ScDecoyBehavior",null);
                Check("database/"+name,behavior?.GetValue<string>("Class")=="Game.ComponentScDecoyBehavior","actual database merge and inherited component values");
            }
            var project=database.Descendants("ProjectTemplate").Single(e=>(string)e.Attribute("Name")=="Project");
            foreach (string name in new[] {"ScKnifeBlockBehavior","ScGunBlockBehavior","ScWeaponWorkbench","ScGrenades"})
                Check("subsystem/"+name,project.Elements("MemberSubsystemTemplate").Count(e=>(string)e.Attribute("Name")==name)==1,"exactly one registration after engine merge");
        } catch (Exception e) {Check("database/load",false,e.ToString());}
        string[] types=["ScKnifeBlock","ScGunBlock","ScAmmoBlock","ScWeaponMaterialBlock","ScWeaponWorkbenchBlock","ScGrenadeBlock"];
        var blocks=types.Select(name=>(Block)Activator.CreateInstance(mod.GetType("Game."+name,true))).ToArray();
        var namesSnapshot=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
        var typesSnapshot=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);
        try {
            List<(string Name,CraftingRecipe Recipe)> recipes=[];
            for (int pass=0;pass<2;pass++) {
                for (int i=0;i<blocks.Length;i++) {
                    var b=blocks[i];b.BlockIndex=pass==0?700+i:880-i*7;
                    BlocksManager.BlockNameToIndex[b.GetType().Name]=b.BlockIndex;BlocksManager.BlockTypeToIndex[b.GetType()]=b.BlockIndex;
                }
                foreach (var b in blocks) {
                    int[] values=b.GetCreativeValues().ToArray();
                    Check($"dynamic-index/{b.GetType().Name}/{pass}",b.IsIndexDynamic && values.All(v=>Terrain.ExtractContents(v)==b.BlockIndex),"two simulated mod loading orders; no fixed runtime contents");
                    foreach (var recipe in b.GetProceduralCraftingRecipes()) {
                        Check($"recipe/{b.GetType().Name}/{Terrain.ExtractData(recipe.ResultValue)}/{pass}",Terrain.ExtractContents(recipe.ResultValue)==b.BlockIndex
                            && recipe.Ingredients.Length==9 && recipe.Ingredients.Any(s=>!string.IsNullOrEmpty(s)) && recipe.RequiredPlayerLevel is >=1 and <=3 && recipe.ResultCount>0,
                            recipe.Description+"; level "+recipe.RequiredPlayerLevel);
                        if (pass==0) recipes.Add((recipe.Description,recipe));
                    }
                }
            }
            Check("recipe/count",recipes.Count==13,"2 ammo + 4 parts + workbench + 6 grenades");
            var vanilla=Read(original,"Assets/CraftingRecipes.xml");
            var layouts=new List<(string Name,string[] Ingredients)>();
            foreach (var recipe in vanilla.DescendantsAndSelf("Recipe")) {
                var rows=Regex.Matches(recipe.Value,"\"([^\"]*)\"").Select(m=>m.Groups[1].Value).ToArray();
                if (rows.Length==0 || rows.Length>3 || rows.Any(s=>s.Length>3)) continue;
                var ingredients=new string[9];
                for (int y=0;y<rows.Length;y++) for (int x=0;x<rows[y].Length;x++) {
                    char c=rows[y][x];if (c!=' ') ingredients[y*3+x]=(string)recipe.Attribute(c.ToString());
                }
                layouts.Add(((string)recipe.Attribute("Result"),ingredients));
            }
            Check("vanilla-recipe-coverage",layouts.Count>=250,$"{layouts.Count} original layouts decoded");
            string[] Actual(string[] ingredients)=>ingredients.Select(s=>string.IsNullOrEmpty(s)?null:s.Contains(':')?s:s+":0").ToArray();
            foreach (var (name,recipe) in recipes) {
                var collisions=layouts.Where(v=>CraftingRecipesManager.MatchRecipe(recipe.Ingredients,Actual(v.Ingredients)) || CraftingRecipesManager.MatchRecipe(v.Ingredients,Actual(recipe.Ingredients))).Select(v=>v.Name).ToList();
                collisions.AddRange(recipes.Where(r=>r.Recipe!=recipe && (CraftingRecipesManager.MatchRecipe(recipe.Ingredients,Actual(r.Recipe.Ingredients)) || CraftingRecipesManager.MatchRecipe(r.Recipe.Ingredients,Actual(recipe.Ingredients)))).Select(r=>r.Name));
                Check("recipe-collisions/"+name,collisions.Count==0,collisions.Count==0?"engine matcher: no shifted/mirrored vanilla or mod collision":string.Join(",",collisions));
            }
        } catch (Exception e) {Check("recipes/load",false,e.ToString());}
        finally {
            BlocksManager.BlockNameToIndex.Clear();foreach (var p in namesSnapshot) BlocksManager.BlockNameToIndex.Add(p.Key,p.Value);
            BlocksManager.BlockTypeToIndex.Clear();foreach (var p in typesSnapshot) BlocksManager.BlockTypeToIndex.Add(p.Key,p.Value);
        }
        foreach (string kind in new[] {"hegrenade","flashbang","smokegrenade","molotov","incendiary","decoy"}) {
            foreach (string suffix in new[] {"_draw.wav","_pin.wav","_throw.wav"})
                Check("grenade-audio/"+kind+suffix,zip.GetEntry("Assets/Audio/ScCsgoKnives/grenade_"+kind+suffix) is not null,"referenced by grenade action controller");
            foreach (string suffix in new[] {".png","_normal.png","_orm.png"})
                Check("grenade-texture/"+kind+suffix,zip.GetEntry("Assets/Textures/ScCsgoKnives/grenade_"+kind+"_cs2"+suffix) is not null,"base/normal/ORM exists in package");
        }
        return results;
    }
}
