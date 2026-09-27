using System.Xml.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using Game;
using TemplatesDatabase;

static class CapacityMatrix {
    internal static int Run(string[] args) {
        // donor.scworld report.json updated-1.0.dll updated-1.2.dll updated-full.dll updated-lite.dll
        var mods=args.Skip(2).Select(p=>new Module(p)).ToArray();var checks=new List<object>();int failed=0;
        void Require(bool ok,string message="assertion failed"){if(!ok)throw new Exception(message);}
        ValuesDictionary Read(XElement x){var d=new ValuesDictionary();d.ApplyOverrides(new XElement(x));return d;}
        XElement Xml(ValuesDictionary d){var x=new XElement("Values");d.Save(x);return x;}
        using var zip=System.IO.Compression.ZipFile.OpenRead(args[0]);using var input=zip.GetEntry("Project.xml").Open();var w=XElement.Load(input);
        var donor=w.Descendants("Values").Single(x=>(string)x.Attribute("Name")=="GunRegistry");
        for(int i=0;i<mods.Length;i++)for(int j=0;j<mods.Length;j++)if(i!=j) {
            var source=mods[i];var target=mods[j];string name=$"{i}->{j}/extended-state-play-return-two-rounds";
            try {
                object reg=source.Call("ScGunRegistry",null,"Load",Read(donor),0d);
                int id=(int)source.Call("ScGunRegistry",reg,"Allocate",0,7,false,700,1500,0);Require(id==1024);
                int data=(int)source.Call("GunSpec",null,"WithId",0,id);int value=Terrain.MakeBlockValue(512,0,data);
                var saved=(ValuesDictionary)source.Call("ScGunRegistry",reg,"Save",0d);string baseline=Xml(saved).ToString();
                for(int n=0;n<2;n++) {
                    reg=target.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),0d);target.Type("ScGunRegistry").GetField("Current").SetValue(null,reg);
                    Require((int)target.Call("GunSpec",null,"GetId",data)==id&&(int)target.Call("GunSpec",null,"GetVariant",data)==0);
                    saved=(ValuesDictionary)target.Call("ScGunRegistry",reg,"Save",0d);Require(Xml(saved).ToString()==baseline,"no-op target state changed");
                    reg=source.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),0d);saved=(ValuesDictionary)source.Call("ScGunRegistry",reg,"Save",0d);Require(Xml(saved).ToString()==baseline,"no-op return state changed");
                }
                reg=target.Call("ScGunRegistry",null,"Load",saved,0d);target.Type("ScGunRegistry").GetField("Current").SetValue(null,reg);
                var inventory=new Inventory();inventory.values[0]=value;inventory.counts[0]=1;object[] a={inventory,0,"capacity-matrix",null};
                var tx=target.Type("ScGunMutation").GetMethod("Prepare").Invoke(null,a);Require(tx!=null);
                var action=typeof(Mutations).GetMethod("Shot").MakeGenericMethod(target.Type("ScGunRecord")).Invoke(null,null);
                Require(target.Call("ScGunMutation",tx,"Commit",action,0,0,null).ToString()=="Success");
                saved=(ValuesDictionary)target.Call("ScGunRegistry",reg,"Save",0d);reg=source.Call("ScGunRegistry",null,"Load",Read(Xml(saved)),0d);
                var record=source.Call("ScGunRegistry",reg,"Get",id);Require((int)source.Get(record,"Rounds")==6&&(int)source.Get(record,"Durability")==699);
                Require((int)source.Get(reg,"Next")==1025&&inventory.values[0]==value);
                checks.Add(new{name,ok=true});
            }catch(Exception e){failed++;checks.Add(new{name,ok=false,error=e.GetBaseException().ToString()});Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{failed,count=checks.Count,modules=mods.Select(m=>new{path=m.Path,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(m.Path)))}),checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"CapacityMatrix {checks.Count-failed}/{checks.Count}, failed={failed}");return failed==0?0:1;
    }
}
