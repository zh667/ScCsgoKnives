using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

static class BalanceUiCheck {
    public static int Run(string contentPath,string output) {
        using var content=ZipFile.OpenRead(contentPath);
        var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        foreach(var entry in content.Entries.Where(e=>e.FullName.EndsWith(".xml"))){using var s=entry.Open();caches[entry.FullName.Replace("Assets/","")[..^4]]=[XElement.Load(s)];}
        var checks=new List<Result>();void Check(string name,bool ok)=>checks.Add(new(name,ok,"native widget layout/click, no player world"));
        bool done=false;int exit=0;
        Window.Frame+=()=>{if(done)return;done=true;try{
            using(var glyph=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.lst")).Open())using(var texture=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.webp")).Open())LabelWidget.BitmapFont=BitmapFont.Initialize(texture,glyph);
            caches["Fonts/Pericles"]=[LabelWidget.BitmapFont];
            using(var s=content.Entries.Single(e=>e.FullName.EndsWith("Textures/Gui/Panorama.webp")).Open())caches[PanoramaWidget.TexturePath]=[Texture2D.Load(s)];
            using(var s=content.Entries.Single(e=>e.FullName.EndsWith("Textures/FireParticle.webp")).Open())caches["Textures/FireParticle"]=[Texture2D.Load(s)];
            using(var texture=content.Entries.Single(e=>e.FullName.EndsWith("Atlases/AtlasTexture.webp")).Open())using(var reader=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith("Atlases/Atlas.txt")).Open()))TextureAtlasManager.LoadAtlases(Texture2D.Load(texture),reader.ReadToEnd());
            foreach(var (type,index) in new[]{(typeof(AirBlock),0),(typeof(ScGunBlock),512),(typeof(ScGunSkinTemplateBlock),513),(typeof(ScGunCounterTemplateBlock),514)}) {
                var b=(Block)Activator.CreateInstance(type);b.BlockIndex=index;BlocksManager.Blocks[index]=b;BlocksManager.BlockTypeToIndex[type]=index;
            }
            var reg=ScGunRegistry.Current=new(){GrowthMode=ScGunGrowthMode.CountAndGrow};int id=reg.Allocate(0,7,false,750,1500);
            int value=Terrain.MakeBlockValue(512,0,GunSpec.WithId(0,id));var before=new XElement("Values");reg.Save(0).Save(before);
            foreach(var size in new[]{new Vector2(1100,650),new Vector2(850,479),new Vector2(480,850),new Vector2(360,640)}) {
                var screen=new ScGunAttributesScreen(value){WidgetsHierarchyInput=new WidgetInput()};screen.Enter([value]);
                for(int n=0;n<4;n++){screen.Measure(size);screen.Arrange(Vector2.Zero,size);}
                BevelledButtonWidget Button(string name)=>(BevelledButtonWidget)typeof(ScGunAttributesScreen).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(screen);
                int Level()=>(int)typeof(ScGunAttributesScreen).GetMethod("Level",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(screen,null);
                void Click(string name){var b=Button(name);b.m_clickableWidget.IsClicked=true;screen.Update();b.m_clickableWidget.IsClicked=false;}
                var up=Button("m_levelUp10");var down=Button("m_levelDown10");
                bool Inside(Widget w)=>w.GlobalBounds.Min.X>=0&&w.GlobalBounds.Max.X<=size.X+.1f&&w.GlobalBounds.Min.Y>=0&&w.GlobalBounds.Max.Y<=size.Y+.1f&&w.ActualSize.X>=48&&w.ActualSize.Y>=40;
                Check("quick-buttons-visible/"+size,Inside(up)&&Inside(down));
                for(int n=0;n<5;n++)Click("m_levelUp10");Check("five-clicks-to-50/"+size,Level()==50&&!up.IsEnabled&&down.IsEnabled);
                Click("m_levelDown");Click("m_levelUp10");Check("49-clamps-to-50/"+size,Level()==50);
                for(int n=0;n<5;n++)Click("m_levelDown10");Check("five-clicks-to-zero/"+size,Level()==0&&!down.IsEnabled);
                Click("m_levelUp");Click("m_levelDown10");Check("1-clamps-to-zero/"+size,Level()==0);
                Click("m_levelUp10");Click("m_level");Check("reset-to-actual/"+size,Level()==0);
                var after=new XElement("Values");reg.Save(0).Save(after);Check("preview-read-only/"+size,XNode.DeepEquals(before,after));
            }
        }catch(Exception e){checks.Add(new("native-ui/exception",false,e.ToString()));exit=1;}finally{Window.Close();}};
        Window.Run(1100,700,WindowMode.Resizable,"Balance UI regression");
        File.WriteAllText(output,JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var c in checks.Where(c=>!c.Ok))Console.WriteLine($"FAIL {c.Name}: {c.Detail}");
        Console.WriteLine($"BalanceUiCheck: {checks.Count(c=>c.Ok)}/{checks.Count}");return checks.All(c=>c.Ok)?exit:1;
    }
}
