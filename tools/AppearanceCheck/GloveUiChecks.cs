using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

static class GloveUiChecks {
    public static void Run(string root,string output,System.IO.Compression.ZipArchive content,Action<string,bool> check,IDictionary<string,List<object>> caches){
        foreach(var entry in content.Entries.Where(e=>e.FullName.EndsWith(".xml"))){using var s=entry.Open();caches[entry.FullName.Replace("Assets/","")[..^4]]=[XElement.Load(s)];}
        using(var glyph=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.lst")).Open())using(var texture=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.webp")).Open())LabelWidget.BitmapFont=BitmapFont.Initialize(texture,glyph);
        caches["Fonts/Pericles"]=[LabelWidget.BitmapFont];
        using(var texture=content.Entries.Single(e=>e.FullName.EndsWith("Atlases/AtlasTexture.webp")).Open())using(var reader=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith("Atlases/Atlas.txt")).Open()))TextureAtlasManager.LoadAtlases(Texture2D.Load(texture),reader.ReadToEnd());
        foreach(string file in Directory.GetFiles(Path.Combine(root,"src/ScCsgoTactical/Assets/Textures/ScCsgoTactical/Gloves"),"*.png")){using var s=File.OpenRead(file);caches["Textures/ScCsgoTactical/Gloves/"+Path.GetFileNameWithoutExtension(file)]=[Texture2D.Load(s)];}
        TacticalArms.Register();check("appearance under functions",ScWorkbenchExtension.Actions.Single(a=>a.Key=="tactical-gloves").Category=="功能");
        var entries=TacticalArms.Gloves.Select(g=>new ScWorkbenchAppearance(g.Key,g.Name,"崭新出厂 · 磨损 0.06\n第一人称与 CT／T 第三人称同步。\n免费外观，不消耗材料。","Textures/ScCsgoTactical/Gloves/"+g.Key)).ToArray();
        foreach(var size in new[]{new Vector2(1100,650),new Vector2(850,479),new Vector2(480,850),new Vector2(360,640)}){
            int applied=0,returned=0;var inventory=new ComponentInventory();inventory.m_slots.Add(new(){Value=15,Count=7});
            ScWorkbenchSelectionDialog Make()=>new("人物外观 · 更换手套",entries,64,o=>((ScWorkbenchAppearance)o).Name,_=>applied++,inventory,false){BackAction=()=>returned++,WidgetsHierarchyInput=new WidgetInput()};
            var dialog=Make();dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);
            Widget Part(string name)=>(Widget)typeof(ScWorkbenchSelectionDialog).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
            var list=(ListPanelWidget)Part("m_list");list.ItemClicked(entries[2]);dialog.Update();dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);
            // Native virtual lists create children after measuring; allow the same warm-up as real frames.
            for(int frame=0;frame<4;frame++){dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);}
            var picture=(RectangleWidget)Part("m_picture");var apply=(BevelledButtonWidget)Part("m_apply");
            check("glove all rows measured "+size,list.Children.Count==entries.Length&&list.Children.All(w=>w.ActualSize.Y>0));
            check("glove preview without commit "+size,applied==0&&picture.Subtexture.Texture==caches[entries[2].Preview][0]&&inventory.GetSlotCount(0)==7);
            check("glove no crafting controls "+size,!Part("m_quantity").IsVisible&&!Part("m_craft").IsVisible&&apply.IsVisible);
            bool Inside(Widget w)=>w.GlobalBounds.Min.X>=dialog.GlobalBounds.Min.X&&w.GlobalBounds.Max.X<=dialog.GlobalBounds.Max.X+.1f&&w.GlobalBounds.Max.Y<=dialog.GlobalBounds.Max.Y+.1f;
            check("glove usable fixed controls "+size,Inside(apply)&&Inside(Part("m_cancel"))&&apply.ActualSize.Y>=48&&Part("m_cancel").GlobalBounds.Max.X<=apply.GlobalBounds.Min.X);
            var row=(ContainerWidget)list.ItemWidgetFactory(entries[0]);check("glove row thumbnail "+size,row.Children.OfType<RectangleWidget>().Any(r=>r.Subtexture!=null));
            using(var target=new RenderTarget2D((int)size.X,(int)size.Y,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8)){
                Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,(int)size.X,(int)size.Y);Display.ScissorRectangle=new Rectangle(0,0,(int)size.X,(int)size.Y);Display.Clear(new Color(22,27,34),1,0);Widget.DrawWidgetsHierarchy(dialog);
                using var file=File.Create(Path.Combine(output,$"glove-ui-{size.X}x{size.Y}.png"));RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
            }
            apply.m_clickableWidget.IsClicked=true;dialog.Update();dialog.Update();check("glove explicit apply once "+size,applied==1&&returned==0&&inventory.GetSlotCount(0)==7);
            dialog=Make();dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);list=(ListPanelWidget)Part("m_list");list.ItemClicked(entries[0]);list.ItemClicked(entries[0]);dialog.Update();dialog.Update();check("glove double tap apply once "+size,applied==2);
            dialog=Make();dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);((BevelledButtonWidget)Part("m_cancel")).m_clickableWidget.IsClicked=true;dialog.Update();dialog.Update();check("glove cancel returns without apply "+size,returned==1&&applied==2);
        }
    }
}
