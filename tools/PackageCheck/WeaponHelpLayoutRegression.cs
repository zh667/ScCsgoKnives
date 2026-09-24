using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

/// <summary>Real API XML, fonts, Measure/Arrange and screen constructors. No window, GPU or player world.
/// Texture handles are inert stand-ins: these tests check layout, not rendered pixels.</summary>
static class WeaponHelpLayoutRegression {
    sealed class HelpBackScreen : Screen { public override void Update() => ScreensManager.GoBack(); }
    sealed class NoticeGui : ComponentGui {
        public readonly List<string> Messages = [];
        public bool PlayedSound;
        public override void DisplaySmallMessage(string text, Color color, bool blinking, bool playNotificationSound) {
            Messages.Add(text); PlayedSound |= playNotificationSound;
        }
    }
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string contentPath, ThirdPartyDlls thirdPartyDlls = null, string packagePath = null) {
        List<Result> results = [];
        void Check(string name, bool ok, string detail) => results.Add(new("weapon-help-layout/" + name, ok, detail));
        var caches = (IDictionary<string, List<object>>)typeof(ContentManager).GetField("Caches", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var savedCaches = caches.ToArray();
        var savedAtlas = TextureAtlasManager.m_subtextures.ToArray();
        var savedBlocks = BlocksManager.Blocks.ToArray();
        var savedTypes = BlocksManager.BlockTypeToIndex.ToArray();
        var savedNames = BlocksManager.BlockNameToIndex.ToArray();
        var savedFont = LabelWidget.m_bitmapFont;
        try {
            using var zip = ZipFile.OpenRead(contentPath);
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            TextureAtlasManager.m_subtextures["Textures/Atlas/Crosshair"] = new Subtexture(texture,Vector2.Zero,Vector2.One);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("Assets/"))) {
                string key = entry.FullName[7..];
                key = key[..(key.LastIndexOf('.') >= 0 ? key.LastIndexOf('.') : key.Length)];
                if (entry.FullName.EndsWith(".xml")) {
                    using var stream = entry.Open();
                    var xml = XElement.Load(stream);
                    caches[key] = [xml];
                    foreach (var a in xml.DescendantsAndSelf().Attributes().Where(a => a.Value.StartsWith("{Textures/Atlas/") && a.Value.EndsWith('}')))
                        TextureAtlasManager.m_subtextures[a.Value[1..^1]] = new Subtexture(texture, Vector2.Zero, Vector2.One);
                } else if (entry.FullName.EndsWith(".png") || entry.FullName.EndsWith(".webp")) caches[key] = [texture];
            }
            using (var glyphs = zip.Entries.Single(e => e.FullName.EndsWith("Fonts/Pericles.lst")).Open())
                LabelWidget.BitmapFont = BitmapFont.Initialize((Texture2D)null, glyphs);
            caches["Fonts/Pericles"] = [LabelWidget.BitmapFont];
            if (thirdPartyDlls is not null) foreach (var asset in thirdPartyDlls.RecipaediaAssets) {
                using var xmlStream = new MemoryStream(asset.Value); var xml = XElement.Load(xmlStream); caches[asset.Key] = [xml];
                foreach (var attr in xml.DescendantsAndSelf().Attributes().Where(a => a.Value.StartsWith("{Textures/Atlas/") && a.Value.EndsWith('}')))
                    TextureAtlasManager.m_subtextures[attr.Value[1..^1]] = new Subtexture(texture, Vector2.Zero, Vector2.One);
            }
            var captureType=mod.GetType("Game.ScWorldBackgroundCaptureState");
            var capture=Activator.CreateInstance(captureType,true);
            var ensure=captureType.GetMethod("Ensure");var reset=captureType.GetMethod("Reset");
            object projectKey=new();int draws=0;
            bool Capture(int w,int h,Action draw)=>(bool)ensure.Invoke(capture,[projectKey,w,h,draw]);
            for(int frame=0;frame<600;frame++)Capture(1920,1080,()=>draws++);
            Check("frozen-background-600-frames",draws==1,"world draw runs once across 600 paused UI frames");
            Capture(1080,1920,()=>draws++);
            Check("background-resize-recapture",draws==2,"rotation captures once at the new dimensions");
            projectKey=new();Capture(1080,1920,()=>draws++);
            Check("background-new-world-recapture",draws==3,"old world image never reused for a different Project");
            reset.Invoke(capture,null);Capture(1080,1920,()=>draws++);
            Check("background-explicit-recapture",draws==4,"entry/device-reset/view-recovery invalidation");
            reset.Invoke(capture,null);
            try{Capture(1080,1920,()=>{draws++;throw new IOException("injected capture failure");});}
            catch(TargetInvocationException e)when(e.InnerException is IOException){}
            bool failedStable=true;
            for(int frame=0;frame<600;frame++)failedStable&=!Capture(1080,1920,()=>draws++);
            Check("background-failure-no-flicker-loop",failedStable&&draws==5,"failed capture stays fallback; no repeated world draw or warning per frame");
            reset.Invoke(capture,null);Capture(1080,1920,()=>draws++);
            Check("background-failure-retry-after-reopen",draws==6,"reopening explicitly retries after failure");
            foreach(string screenName in new[]{"ScGunSettingsScreen","ScGunBindingsScreen","ScGunLayoutScreen"}) {
                var type=mod.GetType("Game."+screenName);
                Check("background-lifecycle-wired/"+screenName,
                    CombatRegression.Calls(type.GetMethod("Enter")).Any(c=>c.DeclaringType.Name=="ScGunWorldBackground"&&c.Name=="ResetCapture")
                    &&CombatRegression.Calls(type.GetMethod("Leave",Type.EmptyTypes)).Any(c=>c.DeclaringType.Name=="ScGunWorldBackground"&&c.Name=="ReleaseCapture"),
                    "actual screen Enter invalidates; Leave releases capture without retaining GPU texture");
            }
            Check("ghoul-title-taps-removed",!CombatRegression.Calls(mod.GetType("Game.ScGunSettingsScreen").GetMethod("Update"))
                .Any(c=>c.DeclaringType.Name is "ScGhoulTestBridge" or "ScTestEntryGate"),"settings Update has no test gate or transfer call");
            var paths=mod.GetType("Game.ScLocalSettings").GetMethod("Resolve",BindingFlags.NonPublic|BindingFlags.Static);
            foreach(string file in new[]{"ScCsgoUi.json","ScCsgoGunplay.json","ScCsgoKnivesTuning.txt","ScreenCapture"}) {
                string android=(string)paths.Invoke(null,[file,true,"android:/Android/data/game/files"]);
                string desktop=(string)paths.Invoke(null,[file,false,"app:/doc"]);
                Check("android-settings-root/"+file,android=="android:/Android/data/game/files/"+file&&desktop=="app:/"+file,"Android uses game document root; desktop preserves old filename");
            }
            var preserve=mod.GetType("Game.ScGunWorldBackground").GetMethod("WithPreservedDisplay",BindingFlags.NonPublic|BindingFlags.Static);
            var opaque=mod.GetType("Game.ScGunWorldBackground").GetMethod("WithOpaquePreview",BindingFlags.NonPublic|BindingFlags.Static);
            var faded=new CanvasWidget();faded.m_globalColorTransform=new Color(30,30,30,30);
            foreach(bool fail in new[]{false,true}) {
                bool white=false;
                try{opaque.Invoke(null,[faded,(Action)(()=>{white=faded.GlobalColorTransform==Color.White;if(fail)throw new IOException("injected preview failure");})]);}
                catch(TargetInvocationException e)when(e.InnerException is IOException){}
                Check("preview-fade-restored/"+fail,white&&faded.GlobalColorTransform==new Color(30,30,30,30),"preview does not inherit outgoing screen fade or change live game layout");
            }
            var oldVp=Display.Viewport;var oldClip=Display.ScissorRectangle;
            try {
                Display.Viewport=new Viewport(0,0,2400,1080);Display.ScissorRectangle=new Rectangle(20,30,1500,900);
                foreach(bool fail in new[]{false,true}) {
                    try {preserve.Invoke(null,[(Action)(()=>{Display.Viewport=new Viewport(10,15,320,200);Display.ScissorRectangle=new Rectangle(1,2,30,40);if(fail)throw new IOException("injected world draw failure");})]);}catch(TargetInvocationException e) when(e.InnerException is IOException){}
                    Check("background-restores-render-state/"+fail,Display.Viewport.Width==2400&&Display.Viewport.Height==1080&&Display.ScissorRectangle==new Rectangle(20,30,1500,900),"nested world draw cannot leak viewport/scissor on success or exception");
                }
            } finally {Display.Viewport=oldVp;Display.ScissorRectangle=oldClip;}
            var panelType=mod.GetType("Game.ScWeaponTouchPanel");var pausedPanel=Activator.CreateInstance(panelType);
            var host=new CanvasWidget{WidgetsHierarchyInput=new WidgetInput()};panelType.GetMethod("Attach").Invoke(pausedPanel,[host]);
            foreach(var b in host.Children.OfType<BevelledButtonWidget>())b.IsVisible=true;
            panelType.GetMethod("SuppressAll").Invoke(null,[true]);
            Check("settings-immediate-hud-suppression",host.Children.All(w=>!w.IsVisible),"already visible buttons hide without another gameplay Update");
            ((IDisposable)pausedPanel).Dispose();
            {
                using var package = ZipFile.OpenRead(packagePath);
                using var metadata = System.Text.Json.JsonDocument.Parse(package.GetEntry("modinfo.json").Open());
                var pages = ModSettingsParser.ParseSettings(metadata.RootElement.GetProperty("Settings"), "zh667.ScCsgoKnives");
                var descriptor = (ModSettingItem)pages.Single().Items.Single();
                Check("native-mod-settings-parser", descriptor.WidgetType==mod.GetType("Game.ScGunModSettingsWidget"), "packaged modinfo resolves custom widget through real TypeCache");
                var entry = ModSettingItemWidgetFactory.Create(descriptor, false, "枪械与操作", "打开完整设置页");
                Check("native-mod-settings-factory", entry?.GetType()==descriptor.WidgetType && entry.Value.Equals(false), "real API factory instantiates CS editor entry without bool-widget fallback");
                Check("main-settings-no-extra-entry", mod.GetType("Game.ScCsgoKnivesModLoader").GetMethod("OnSettingsScreenCreated").DeclaringType==typeof(ModLoader), "CS entry is registered through modinfo Settings");
                ((Widget)entry).Dispose();
            }
            foreach(var available in new[]{new Vector2(850,383),new Vector2(850,479),new Vector2(360,640),new Vector2(480,850)}) {
                var settings=(Screen)Activator.CreateInstance(mod.GetType("Game.ScGunSettingsScreen"));settings.Enter([]);
                settings.Measure(available);settings.Arrange(Vector2.Zero,available);settings.Measure(available);settings.Arrange(Vector2.Zero,available);
                var texts=settings.AllChildren.OfType<LabelWidget>().Select(w=>w.Text).Where(t=>t is not null).ToArray();
                Check("settings-no-third-party-or-test-entry/"+available,!texts.Any(t=>t.Contains("触控映射")||t.Contains("玲兰")||t.Contains("铃兰")||t.Contains("尸鬼"))
                    &&settings.GetType().GetField("m_testGate",BindingFlags.NonPublic|BindingFlags.Instance) is null,"no hidden title tap gate; ordinary binding entry only");
                Widget Part(string n)=>(Widget)settings.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(settings);
                var scroll=(ScrollPanelWidget)Part("m_scroll");var save=Part("m_save");
                var recovery=Part("m_recoverView");
                Check("view-recovery-entry/"+available,recovery.GlobalBounds.Min.Y>=scroll.GlobalBounds.Min.Y
                    &&recovery.GlobalBounds.Max.Y<=scroll.GlobalBounds.Max.Y&&recovery.GlobalBounds.Max.X<=available.X,
                    "view recovery is visible at the top without scrolling, including narrow phone");
                Check("settings-fixed-footer/"+available,save.GlobalBounds.Max.X<=available.X&&save.GlobalBounds.Max.Y<=available.Y&&scroll.GlobalBounds.Max.Y<=save.GlobalBounds.Min.Y,"scroll region ends above fixed footer, including 20:9 phone");
                scroll.ScrollPosition=10000;settings.Measure(available);settings.Arrange(Vector2.Zero,available);
                Check("settings-scroll-retains-footer/"+available,save.GlobalBounds.Max.Y<=available.Y&&Part("m_cancel").GlobalBounds.Min.X>=0,"Save/Cancel remain visible after scrolling");
                var preview = (CanvasWidget)Part("m_shapePreview");
                var shapeType = mod.GetType("Game.ScCrosshairShape");
                preview.GetType().GetField("Shape").SetValue(preview, Activator.CreateInstance(shapeType, [8f,32f,24f,3f,16f]));
                preview.GetType().GetField("CrossStyle").SetValue(preview, "cross");
                settings.Measure(available); settings.Arrange(Vector2.Zero, available);
                var rgb = Part("m_red");
                Check("crosshair-preview-beside-controls/" + available, rgb.GlobalBounds.Max.X + 8 <= preview.GlobalBounds.Min.X
                    && Math.Abs(rgb.GlobalBounds.Min.Y - preview.GlobalBounds.Min.Y) < 1,
                    $"slider={rgb.GlobalBounds}; preview={preview.GlobalBounds}");
                foreach (var line in preview.Children.OfType<RectangleWidget>().Where(w=>w.IsVisible))
                    Check("crosshair-max-contained/" + available + "/" + line.GlobalBounds.Min,
                        line.GlobalBounds.Min.X >= preview.GlobalBounds.Min.X + 7 && line.GlobalBounds.Max.X <= preview.GlobalBounds.Max.X - 7
                        && line.GlobalBounds.Min.Y >= preview.GlobalBounds.Min.Y + 7 && line.GlobalBounds.Max.Y <= preview.GlobalBounds.Max.Y - 7,
                        "all shape sliders at their maximum; preview stays inside its own panel");
                settings.Leave();settings.Dispose();
            }
            if (LabelWidget.BitmapFont is null) throw new Exception("Failed to read native font metrics");
            for (int i = 0; i < BlocksManager.Blocks.Length; i++) BlocksManager.Blocks[i] = new AirBlock { BlockIndex = i };
            int index = 700;
            foreach (Block material in new Block[] { new DiamondChunkBlock(), new GermaniumChunkBlock() }) {
                int materialIndex = material is DiamondChunkBlock ? 730 : 731;
                material.BlockIndex = materialIndex; BlocksManager.Blocks[materialIndex] = material;
                BlocksManager.BlockTypeToIndex[material.GetType()] = materialIndex; BlocksManager.BlockNameToIndex[material.GetType().Name] = materialIndex;
            }
            foreach (string name in new[] { "ScGunBlock", "ScKnifeBlock", "ScWeaponMaterialBlock", "ScAmmoBlock", "ScWeaponWorkbenchBlock", "ScGunSkinTemplateBlock", "ScGunCounterTemplateBlock", "ScGrenadeBlock", "ScC4Block", "ScChickenEggBlock" }) {
                var type = mod.GetType("Game." + name, true);
                var block = (Block)Activator.CreateInstance(type);
                block.BlockIndex = index;
                BlocksManager.Blocks[index] = block;
                BlocksManager.BlockTypeToIndex[type] = index;
                BlocksManager.BlockNameToIndex[name] = index++;
            }
            mod.GetType("Game.ScWorkbenchExtension").GetMethod("RegisterBaseRecipes").Invoke(null,null);
            int template = (int)mod.GetType("Game.ScGunAttributes").GetMethod("TemplateValue").Invoke(null, [0]);
            void ComponentCheck(string name, bool ok) => Check(name, ok, "real API blocks, vanilla data and inventory transaction");
            // Resolve ingredient IDs against the user's actual vanilla data and engine block classes.
            using (var dataReader = new StreamReader(zip.GetEntry("Assets/BlocksData.txt").Open())) {
                string[] lines=dataReader.ReadToEnd().Split('\n');int craftingColumn=Array.IndexOf(lines[0].Trim().Split(';'),"CraftingId");
                var components=mod.GetType("Game.ScComponentCrafting");var entries=((Array)components.GetField("All").GetValue(null)).Cast<object>().ToArray();
                var ids=entries.SelectMany(e=>((ValueTuple<string,int>[])e.GetType().GetProperty("Ingredients").GetValue(e)).Select(p=>p.Item1.Split(':')[0])).Where(id=>id!="sccsgomaterial").ToHashSet();
                ids.Add("gunpowder");
                int materialIndex=740;
                foreach(string line in lines.Skip(1)) {
                    string[] cells=line.Trim().Split(';');if(cells.Length<=craftingColumn||!ids.Contains(cells[craftingColumn]))continue;
                    var material=(Block)Activator.CreateInstance(typeof(Block).Assembly.GetType("Game."+cells[0],true));
                    material.BlockIndex=materialIndex;material.CraftingId=cells[craftingColumn];material.MaxStacking=40;
                    BlocksManager.Blocks[materialIndex]=material;BlocksManager.BlockTypeToIndex[material.GetType()]=materialIndex;BlocksManager.BlockNameToIndex[cells[0]]=materialIndex++;
                }
                var componentRegistryType=mod.GetType("Game.ScGunRegistry");var current=componentRegistryType.GetField("Current");object previous=current.GetValue(null);
                var registry=Activator.CreateInstance(componentRegistryType);current.SetValue(null,registry);componentRegistryType.GetField("RecoveryOwner").SetValue(registry,(Func<IInventory,string>)(_=>"fixture/components"));
                try {
                    foreach(var entry in entries) {
                        var costs=(Dictionary<int,int>)entry.GetType().GetMethod("Materials").Invoke(entry,null);int result=(int)entry.GetType().GetProperty("Value").GetValue(entry);
                        string name=(string)entry.GetType().GetProperty("Name").GetValue(entry);
                        ComponentCheck("components/actual-vanilla-resolver/"+name,costs.Count==((ValueTuple<string,int>[])entry.GetType().GetProperty("Ingredients").GetValue(entry)).Length);
                        var componentInventory=new ComponentInventory();for(int i=0;i<16;i++)componentInventory.m_slots.Add(new());int slot=0;
                        foreach(var cost in costs){componentInventory.m_slots[slot++]=new(){Value=cost.Key,Count=1};componentInventory.m_slots[slot++]=new(){Value=cost.Key,Count=cost.Value-1};}
                        var craft=mod.GetType("Game.ScWeaponCrafting").GetMethod("TryCraft");
                        bool crafted=(bool)craft.Invoke(null,[componentInventory,result,costs]);
                        ComponentCheck("components/split-stacks-exact-output/"+name,crafted&&Enumerable.Range(0,16).Sum(i=>componentInventory.GetSlotCount(i))==1&&Enumerable.Range(0,16).Any(i=>componentInventory.GetSlotValue(i)==result&&componentInventory.GetSlotCount(i)==1));
                        string Frozen()=>string.Join(";",componentInventory.m_slots.Select(s=>$"{s.Value}:{s.Count}"));string before=Frozen();
                        ComponentCheck("components/repeated-click-no-second-output/"+name,!(bool)craft.Invoke(null,[componentInventory,result,costs])&&Frozen()==before);
                        foreach(var s in componentInventory.m_slots){s.Value=result;s.Count=40;}
                        slot=0;foreach(var cost in costs)componentInventory.m_slots[slot++]=new(){Value=cost.Key,Count=cost.Value+1};
                        before=Frozen();ComponentCheck("components/full-componentInventory-no-deduction/"+name,!(bool)craft.Invoke(null,[componentInventory,result,costs])&&Frozen()==before);
                    }
                    int pigment=(int)components.GetMethod("Resolve").Invoke(null,["pigment:0"]);
                    ComponentCheck("components/white-pigment-real-engine",BlocksManager.Blocks[Terrain.ExtractContents(pigment)] is PigmentBlock&&Terrain.ExtractData(pigment)==0&&WorldPalette.DefaultColors[0]==Color.White);
                }finally{current.SetValue(null,previous);}
            }
            var craftItems=((Array)mod.GetType("Game.ScWeaponCrafting").GetField("All").GetValue(null)).Cast<object>().ToArray();
            foreach(bool creative in new[]{false,true}) {
                var menu=(object[])mod.GetType("Game.SubsystemScWeaponWorkbench").GetMethod("MainMenuItems",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                var inv=new ComponentInventory();inv.m_slots.Add(new());
                var dialog=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchSelectionDialog"),["功能菜单",menu,56f,
                    (Func<object,string>)(o=>o.GetType().Name),(Action<object>)(_=>{}),inv,creative]);
                var menuSelect=dialog.GetType().GetMethod("Select",BindingFlags.NonPublic|BindingFlags.Instance);
                var menuCount=dialog.GetType().GetField("m_count",BindingFlags.NonPublic|BindingFlags.Instance);
                menuSelect.Invoke(dialog,[menu.First(o=>o.GetType().DeclaringType?.Name=="ScComponentCrafting")]);
                menuCount.SetValue(dialog,10);menuSelect.Invoke(dialog,[craftItems[0]]);
                Check($"workbench-components-ten-then-gun-one/{creative}",(int)menuCount.GetValue(dialog)==1,"component batch cannot carry over into a gun recipe");
                dialog.GetType().GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,["功能"]);
                dialog.Measure(new(850,479));dialog.Arrange(Vector2.Zero,new(850,479));
                var list=(ListPanelWidget)dialog.GetType().GetField("m_list",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                Check($"workbench-all-functions-visible/{creative}",list.Items.Count==5&&list.Items.Any(o=>o.GetType().Name=="SkinMenu")&&list.Items.Any(o=>o.GetType().Name=="OwnedAttributesMenu")
                    && menu.Length==craftItems.Length+20,"actual runtime menu: five operations + five components + ten supplies, plus gun/knife assembly");
            }
            foreach(bool creative in new[]{false,true})foreach(var available in new[]{new Vector2(1100,650),new Vector2(850,479),new Vector2(708,399),new Vector2(480,850),new Vector2(360,640),new Vector2(850,270)}){
                var inv=new ComponentInventory();inv.m_slots.Add(new()); int choices=0;
                var browse=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchSelectionDialog"),["武器装配台 · 组装 / 维修 / 涂装 / 计数器",craftItems,56f,
                    (Func<object,string>)(o=>(string)o.GetType().GetProperty("Name").GetValue(o)),(Action<object>)(_=>choices++),inv,creative]);
                browse.WidgetsHierarchyInput=new WidgetInput();browse.Measure(available);browse.Arrange(Vector2.Zero,available);
                Widget Part(string n)=>(Widget)browse.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(browse);
                bool Inside(Widget w)=>w.GlobalBounds.Min.X>=browse.GlobalBounds.Min.X-.1f && w.GlobalBounds.Max.X<=browse.GlobalBounds.Max.X+.1f
                    && w.GlobalBounds.Min.Y>=browse.GlobalBounds.Min.Y-.1f && w.GlobalBounds.Max.Y<=browse.GlobalBounds.Max.Y+.1f;
                bool ok=Inside(Part("m_listHost"))&&(!Part("m_previewHost").IsVisible||Inside(Part("m_previewHost")))&&Inside(Part("m_detailHost"))&&Inside(Part("m_cancel"))&&Part("m_cancel").ActualSize.Y>=48&&Inside(Part("m_hint"));
                var select=browse.GetType().GetMethod("Select",BindingFlags.NonPublic|BindingFlags.Instance);
                foreach(var item in craftItems){select.Invoke(browse,[item]);browse.Measure(available);browse.Arrange(Vector2.Zero,available);ok &= Inside(Part("m_cancel"))&&Part("m_detailScroll").ActualSize.Y>40;}
                Check($"workbench-browse/{creative}/{available}",ok&&choices==0,"all 57 weapon recipes: list, preview, scrollable material area and fixed footer; browsing never commits");
                Check($"workbench-batch-footer/{creative}/{available}",Inside(Part("m_quantity"))&&Inside(Part("m_craft"))
                    &&Part("m_cancel").GlobalBounds.Max.X<=Part("m_quantity").GlobalBounds.Min.X
                    &&Part("m_quantity").GlobalBounds.Max.X<=Part("m_craft").GlobalBounds.Min.X
                    &&Part("m_detailHost").GlobalBounds.Max.Y<=Part("m_craft").GlobalBounds.Min.Y,"quantity and explicit craft stay separated and visible");
                var clickCraft=browse.GetType().GetMethod("ClickItem",BindingFlags.NonPublic|BindingFlags.Instance);
                var countField=browse.GetType().GetField("m_count",BindingFlags.NonPublic|BindingFlags.Instance);
                select.Invoke(browse,[craftItems[0]]);countField.SetValue(browse,10);select.Invoke(browse,[craftItems[0]]);
                Check($"workbench-quantity-refresh-keeps-choice/{creative}/{available}",(int)countField.GetValue(browse)==10,"same selection retains deliberately selected batch");
                select.Invoke(browse,[craftItems[1]]);
                Check($"workbench-quantity-new-recipe-resets/{creative}/{available}",(int)countField.GetValue(browse)==1&&((ButtonWidget)Part("m_craft")).Text=="制作 1 件","changing recipe resets count and quote");
                clickCraft.Invoke(browse,[craftItems[0],1d]);clickCraft.Invoke(browse,[craftItems[0],1.1d]);browse.Update();
                Check($"workbench-recipe-doubleclick-no-navigation/{creative}/{available}",choices==0,"only explicit craft spends materials");
                browse.GetType().GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(browse,["步枪"]);
                Check($"workbench-category/{creative}/{available}",((ListPanelWidget)Part("m_list")).Items.Count==7,"category filters rifles without losing available recipes");
                var nav=browse.GetType().GetMethod("CaptureNavigation").Invoke(browse,null);
                var restored=(Dialog)Activator.CreateInstance(browse.GetType(),["返回",craftItems,56f,(Func<object,string>)(o=>o.GetType().Name),(Action<object>)(_=>{}),inv,creative]);
                restored.GetType().GetMethod("RestoreNavigation").Invoke(restored,[nav]);
                var restoredList=(ListPanelWidget)restored.GetType().GetField("m_list",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(restored);
                Check($"workbench-return-preserves-category/{creative}/{available}",restoredList.Items.Count==7&&Equals(restoredList.SelectedItem,((ListPanelWidget)Part("m_list")).SelectedItem),"recreated page restores rifle tab and selected gun");
                var confirm=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchConfirmDialog"),["长名称 · 计数器皮肤枪械",string.Join("\n",Enumerable.Repeat("材料需要 6 件，当前持有 12 件；枪械状态保持不变。",80)),"确认组装","返回",(Action<MessageDialogButton>)(_=>choices++)]);
                confirm.WidgetsHierarchyInput=new WidgetInput();confirm.Measure(available);confirm.Arrange(Vector2.Zero,available);
                var yes=(ButtonWidget)confirm.GetType().GetField("m_yes",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(confirm);
                var scroll=(ScrollPanelWidget)confirm.GetType().GetField("m_scroll",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(confirm);
                scroll.ScrollPosition=Math.Max(0,scroll.CalculateScrollAreaLength()-scroll.ActualSize.Y);confirm.Measure(available);confirm.Arrange(Vector2.Zero,available);
                Check($"workbench-long-quote/{creative}/{available}",yes.ActualSize.Y>=48&&yes.GlobalBounds.Max.Y<=confirm.GlobalBounds.Max.Y+.1f&&scroll.GlobalBounds.Max.Y<yes.GlobalBounds.Min.Y&&choices==0,
                    "80-line quote scrolls; confirmation remains visible and cannot be executed by simply selecting a row");
                ((BevelledButtonWidget)yes).m_clickableWidget.IsClicked=true;confirm.Update();confirm.Update();
                Check($"workbench-confirm-once/{creative}/{available}",choices==1,"explicit confirmation dispatches the existing transaction callback once, not once per held frame");
                int notices=0;
                var notice=(Dialog)mod.GetType("Game.SubsystemScWeaponWorkbench").GetMethod("NoticeDialog",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,["涂装 · 没有可用枪械","背包里没有支持更换涂装的枪械。\n请将支持涂装的枪械放入玩家背包或快捷栏。",(Action)(()=>notices++)]);
                notice.WidgetsHierarchyInput=new WidgetInput();notice.Measure(available);notice.Arrange(Vector2.Zero,available);notice.Update();
                var noticeYes=(ButtonWidget)notice.GetType().GetField("m_yes",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(notice);
                var noticeNo=(ButtonWidget)notice.GetType().GetField("m_no",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(notice);
                Check($"workbench-visible-refusal/{creative}/{available}",notices==0&&!noticeNo.IsVisible&&noticeYes.Text=="返回"
                    &&noticeYes.GlobalBounds.Max.Y<=notice.GlobalBounds.Max.Y+.1f&&noticeYes.ActualSize.Y>=48,
                    "real workshop notice persists until acknowledgment, with a single centered visible Return; not a hidden HUD toast");
                ((BevelledButtonWidget)noticeYes).m_clickableWidget.IsClicked=true;notice.Update();notice.Update();
                Check($"workbench-notice-return-once/{creative}/{available}",notices==1,"acknowledgment returns to the appropriate list exactly once");
            }
            {
                var levelDialog=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchSelectionDialog"),["创造等级",Enumerable.Range(0,51).Cast<object>().ToArray(),48f,
                    (Func<object,string>)(o=>$"Lv{o}（5250 击杀）"),(Action<object>)(_=>{}),new ComponentInventory(),true]);
                var levelList=(ListPanelWidget)levelDialog.GetType().GetField("m_list",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(levelDialog);
                var row=(ContainerWidget)levelList.ItemWidgetFactory(50);row.Measure(new Vector2(180,52));row.Arrange(Vector2.Zero,new Vector2(180,52));
                var labels=row.AllChildren.OfType<LabelWidget>().ToArray();
                Check("level-row-emphasizes-level",labels.Length==2&&labels[0].Text=="Lv50"&&labels[1].Text=="（5250 击杀）"&&labels[0].FontScale>labels[1].FontScale
                    &&labels.All(l=>l.GlobalBounds.Max.Y<=52.1f),"larger separate level; parenthesized kills below, fits existing row");
            }
            foreach(bool creative in new[]{false,true}) {
                Dialog Browse(Action<object> choose) {
                    var inv=new ComponentInventory(); inv.m_slots.Add(new());
                    var d=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchSelectionDialog"),["功能",new object[]{"维修","涂装","计数器","属性"},56f,
                        (Func<object,string>)(o=>o.ToString()),choose,inv,creative]);
                    d.WidgetsHierarchyInput=new WidgetInput(); d.Measure(new(850,479)); d.Arrange(Vector2.Zero,new(850,479)); return d;
                }
                foreach(int row in Enumerable.Range(0,4)) {
                    int opened=0; object chosen=null;
                    var d=Browse(o=>{opened++;chosen=o;});
                    var list=(ListPanelWidget)d.GetType().GetField("m_list",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(d);
                    list.ItemClicked(list.Items[row]); d.Update();
                    Check($"workbench-first-click-preview/{creative}/{row}",opened==0&&!d.AllChildren.OfType<ButtonWidget>().Any(b=>b.Text=="下一步"),"real ItemClicked previews, including initially selected first row; no Next button");
                    list.ItemClicked(list.Items[row]); d.Update();d.Update();list.ItemClicked(list.Items[row]);d.Update();
                    Check($"workbench-double-click-once/{creative}/{row}",opened==1&&ReferenceEquals(chosen,list.Items[row]),"mouse/touch shared click callback opens exactly once; held/triple input cannot repeat it");
                }
                int calls=0; var delayed=Browse(_=>calls++);
                var click=delayed.GetType().GetMethod("ClickItem",BindingFlags.NonPublic|BindingFlags.Instance);
                var filter=delayed.GetType().GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance);
                var delayedList=(ListPanelWidget)delayed.GetType().GetField("m_list",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(delayed);
                var a=delayedList.Items[0];var b=delayedList.Items[1];
                click.Invoke(delayed,[a,1d]);click.Invoke(delayed,[a,2d]);delayed.Update();
                click.Invoke(delayed,[b,2.1d]);delayed.Update();
                Check($"workbench-timeout-or-different-row/{creative}",calls==0,"slow clicks and different rows are previews, not double-click activation");
                delayedList.ScrollPosition=20;click.Invoke(delayed,[b,2.2d]);delayed.Update();
                Check($"workbench-scroll-breaks-double-click/{creative}",calls==0,"scrolling between taps cannot open a row");
                filter.Invoke(delayed,["全部"]);click.Invoke(delayed,[b,2.3d]);delayed.Update();
                Check($"workbench-category-breaks-double-click/{creative}",calls==0,"changing category resets the click sequence");
                click.Invoke(delayed,[b,2.4d]);delayed.Update();delayed.Update();
                Check($"workbench-fresh-double-click/{creative}",calls==1,"two consecutive clicks in the new category activate once");
            }
            var counterType = mod.GetType("Game.ScGunCounterTemplateBlock");
            var counterBlock = BlocksManager.Blocks[BlocksManager.BlockTypeToIndex[counterType]];
            var inventory = new ComponentInventory(); inventory.m_slots.Add(new());
            inventory.AddSlotItems(0, counterBlock.GetCreativeValues().First(), 1);
            object conversion = counterType.GetMethod("Materialize").Invoke(null, [inventory, 0, "layout-name-test"]);
            int instance = inventory.GetSlotValue(0);
            var realGun = BlocksManager.Blocks[Terrain.ExtractContents(instance)];
            string beforeName = realGun.GetDisplayName(null, instance);
            var chest = new ComponentChest(); chest.m_slots.Add(new());
            inventory.RemoveSlotItems(0, 1); chest.AddSlotItems(0, instance, 1);
            string chestName = realGun.GetDisplayName(null, chest.GetSlotValue(0));
            chest.RemoveSlotItems(0, 1); inventory.AddSlotItems(0, instance, 1);
            Check("counter-name-survives-chest-roundtrip", conversion.ToString() == "Success" && beforeName.Contains("击杀计数器 Lv0")
                && chestName == beforeName && realGun.GetDisplayName(null, inventory.GetSlotValue(0)) == beforeName, "actual GetDisplayName on instance in inventory -> ComponentChest -> inventory");
            var registryType = mod.GetType("Game.ScGunRegistry"); var registryField = registryType.GetField("Current"); var previousRegistry = registryField.GetValue(null);
            try {
                foreach (bool creative in new[] { false, true }) foreach (int initialKills in new[] { 24, 29 }) {
                    object registry = Activator.CreateInstance(registryType); registryField.SetValue(null, registry);
                    IInventory inv;
                    if (creative) { var ci = new ComponentCreativeInventory { OpenSlotsCount = 10 }; for (int i = 0; i < 10; i++) ci.m_slots.Add(0); inv = ci; }
                    else { var si = new ComponentInventory(); si.m_slots.Add(new()); inv = si; }
                    inv.AddSlotItems(0, counterBlock.GetCreativeValues().First(), 1);
                    counterType.GetMethod("Materialize").Invoke(null, [inv, 0, "notify-test"]);
                    int held = inv.GetSlotValue(0);
                    var specType = mod.GetType("Game.GunSpec"); int id = (int)specType.GetMethod("GetId").Invoke(null, [Terrain.ExtractData(held)]);
                    object record = registryType.GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(registry, [id]); record.GetType().GetField("KillCount").SetValue(record, (long)initialKills);
                    if (initialKills == 24) { object queue = registryType.GetField("Kills").GetValue(registry); queue.GetType().GetMethod("Enqueue").Invoke(queue, [id, 0]); }
                    var gui = new NoticeGui(); var player = new ComponentPlayer { ComponentMiner = new ComponentMiner { Inventory = inv }, ComponentGui = gui };
                    var players = new SubsystemPlayers(); players.m_componentPlayers.Add(player);
                    var behaviorType = mod.GetType("Game.SubsystemScGunBlockBehavior"); var behavior = Activator.CreateInstance(behaviorType);
                    void Set(string name, object val) => behaviorType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(behavior, val);
                    Set("m_registry", registry); Set("m_players", players); Set("m_time", new SubsystemTime());
                    var holderType = mod.GetType("Game.ScGunHolders+Holder"); var holderList = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holderType));
                    holderList.Add(Activator.CreateInstance(holderType, [id, "notify-test", inv, 0]));
                    var update = behaviorType.GetMethod("UpdateGrowth", BindingFlags.Instance | BindingFlags.NonPublic);
                    update.Invoke(behavior, [holderList]); update.Invoke(behavior, [holderList]);
                    Check($"runtime-notification/{creative}/{initialKills}", gui.Messages.Count == 1 && gui.Messages[0].Contains("Lv0 → Lv1") && gui.PlayedSound
                        && realGun.GetDisplayName(null, held).Contains("Lv1") && (long)record.GetType().GetField("KillCount").GetValue(record) == Math.Max(initialKills, 25),
                        "actual subsystem UpdateGrowth -> rule -> transaction -> GetDisplayName -> player ComponentGui callback; first sweep and repeat; both real inventory classes");
                }
            } finally { registryField.SetValue(null, previousRegistry); }
            // Owned-item stats are read-only, use the final combat snapshot, and do not confuse same-model guns.
            var owned=mod.GetType("Game.ScOwnedGunAttributes");var ownedDialog=mod.GetType("Game.ScOwnedGunAttributesDialog");
            var nameType=mod.GetType("Game.ScGunNames");var statsType=mod.GetType("Game.EffectiveGunStats");
            var specs=(Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null);
            var skinBlock=BlocksManager.Blocks[BlocksManager.BlockTypeToIndex[mod.GetType("Game.ScGunSkinTemplateBlock")]];
            string[] expectedNames=["AK-47","M4A1-S","AWP","沙漠之鹰","格洛克18","USP-S","M4A4","法玛斯","MP9","P90","SSG 08","FN57","P2000","P250","TEC-9","CZ75","MAC-10","MP7","UMP-45","PP-野牛","MP5-SD","加利尔","SCAR-20","G3SG1","AUG","SG 553","新星","XM1014","截短霰弹枪","MAG-7","M249","内格夫","R8 左轮","双持贝瑞塔","电击枪"];
            for(int v=0;v<expectedNames.Length;v++)Check("canonical-name/"+v,(string)nameType.GetMethod("Variant").Invoke(null,[v])==expectedNames[v],expectedNames[v]);
            foreach(int value in realGun.GetCreativeValues().Concat(counterBlock.GetCreativeValues()).Concat(skinBlock.GetCreativeValues())) {
                var block=BlocksManager.Blocks[Terrain.ExtractContents(value)];
                object[] args=[value,null];bool known=(bool)statsType.GetMethod("TrySnapshotValue").Invoke(null,args);
                int variant=(int)args[1].GetType().GetProperty("Variant").GetValue(args[1]);
                string expected=(string)nameType.GetMethod("Variant").Invoke(null,[variant]);
                string display=block.GetDisplayName(null,value);
                Check("all-template-names/"+value,known&&expected!="未知枪械"&&display.StartsWith(expected),display);
            }
            try {
                var namesRegistry=Activator.CreateInstance(registryType);registryField.SetValue(null,namesRegistry);
                foreach(int templateValue in counterBlock.GetCreativeValues()) {
                    var bag=new ComponentInventory();bag.m_slots.Add(new());bag.AddSlotItems(0,templateValue,1);
                    string expectedName=counterBlock.GetDisplayName(null,templateValue);
                    object result=counterType.GetMethod("Materialize").Invoke(null,[bag,0,"name-audit"]);
                    string actual=realGun.GetDisplayName(null,bag.GetSlotValue(0));
                    Check("counter-instance-name-parity/"+templateValue,result.ToString()=="Success"&&actual==expectedName,actual);
                }
                foreach(bool creative in new[]{false,true}) {
                    object registry=Activator.CreateInstance(registryType);registryField.SetValue(null,registry);
                    IInventory inv;
                    if(creative){var ci=new ComponentCreativeInventory {OpenSlotsCount=3};for(int k=0;k<5;k++)ci.m_slots.Add(0);inv=ci;}
                    else{var si=new ComponentInventory();for(int k=0;k<4;k++)si.m_slots.Add(new());inv=si;}
                    int counter=counterBlock.GetCreativeValues().First();
                    inv.AddSlotItems(0,counter,1);inv.AddSlotItems(1,counter,1);
                    counterType.GetMethod("Materialize").Invoke(null,[inv,0,"owned-test-0"]);
                    counterType.GetMethod("Materialize").Invoke(null,[inv,1,"owned-test-1"]);
                    inv.AddSlotItems(2,skinBlock.GetCreativeValues().First(),1);
                    if(creative)((ComponentCreativeInventory)inv).m_slots[3]=counter;
                    int Id(int value)=>(int)mod.GetType("Game.GunSpec").GetMethod("GetId").Invoke(null,[Terrain.ExtractData(value)]);
                    var record=registryType.GetMethod("Get",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(registry,[Id(inv.GetSlotValue(1))]);
                    record.GetType().GetField("AppliedGrowthLevel").SetValue(record,10);
                    record.GetType().GetField("PendingGrowthLevel").SetValue(record,11);
                    record.GetType().GetField("KillCount").SetValue(record,1100L);
                    Array Candidates()=>(Array)owned.GetMethod("Candidates").Invoke(null,[inv]);
                    var candidates=Candidates();int originalCount=(int)registryType.GetProperty("Count").GetValue(registry);
                    object Resolve(object selected,out bool ok){object[] args=[inv,selected,registry,null,null];ok=(bool)owned.GetMethod("TryResolve").Invoke(null,args);return args[4];}
                    object first=Resolve(candidates.GetValue(0),out bool firstOk),second=Resolve(candidates.GetValue(1),out bool secondOk);
                    float Power(object stats)=>(float)statsType.GetProperty("Power").GetValue(stats);
                    Check("owned-same-model-independent/"+creative,candidates.Length==3&&firstOk&&secondOk&&Math.Abs(Power(second)-2*Power(first))<.001f
                        &&(int)statsType.GetProperty("Level").GetValue(second)==10,"Lv0 and applied Lv10, pending Lv11 does not inflate current stats; infinite creative slots excluded");
                    object spec=specs.GetValue(0);
                    var changed=statsType.GetMethod("Resolve").Invoke(null,[spec,inv.GetSlotValue(1),false]);
                    statsType.GetProperty("Power").SetValue(changed,777f);
                    var rows=(System.Collections.IList)mod.GetType("Game.ScGunAttributes").GetMethod("RowsFromEffective").Invoke(null,[spec,changed]);
                    Check("owned-final-snapshot-no-ui-recalculation/"+creative,rows[0].GetType().GetProperty("Text").GetValue(rows[0]).ToString().StartsWith("777"),"future final-stat changes are not lost by recalculating from the level in UI");
                    foreach(var size in new[]{new Vector2(1187,637),new Vector2(850,383),new Vector2(360,640),new Vector2(480,850),new Vector2(850,270)}) {
                        int backs=0;var dialog=(Dialog)Activator.CreateInstance(ownedDialog,[inv,(Func<bool>)(()=>true),(Action)(()=>backs++)]);
                        dialog.WidgetsHierarchyInput=new WidgetInput();
                        dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);
                        Widget Part(string n)=>(Widget)ownedDialog.GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                        var list=(ListPanelWidget)Part("m_list");var content=(StackPanelWidget)Part("m_content");var scroll=(ScrollPanelWidget)Part("m_scroll");
                        foreach(var gun in list.Items) {list.ItemClicked(gun);dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);}
                        bool Inside(Widget w)=>w.GlobalBounds.Min.X>=dialog.GlobalBounds.Min.X-.1f&&w.GlobalBounds.Max.X<=dialog.GlobalBounds.Max.X+.1f
                            &&w.GlobalBounds.Min.Y>=dialog.GlobalBounds.Min.Y-.1f&&w.GlobalBounds.Max.Y<=dialog.GlobalBounds.Max.Y+.1f;
                        Check($"owned-layout/{creative}/{size}",Inside(Part("m_listHost"))&&Inside(Part("m_detailHost"))&&Inside(Part("m_backButton"))
                            &&scroll.ActualSize.Y>0&&content.AllChildren.OfType<ValueBarWidget>().Count()==8
                            &&!content.AllChildren.OfType<LabelWidget>().Any(l=>l.Text.Contains("最大耐久")||l.Text.Contains("制作材料")||l.Text.Contains("预览 Lv")),"actual dialog layout, eight rows, no level selector or hidden footer");
                        scroll.ScrollPosition=10000;dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);
                        Check($"owned-scroll-footer/{creative}/{size}",Inside(Part("m_backButton")),"footer stays fixed while stats scroll");
                        ((BevelledButtonWidget)Part("m_backButton")).m_clickableWidget.IsClicked=true;dialog.Update();dialog.Update();
                        Check($"owned-back-once/{creative}/{size}",backs==1,"workbench navigation callback, not recipe-screen switching");dialog.Dispose();
                    }
                    Check("owned-no-record-allocation/"+creative,(int)registryType.GetProperty("Count").GetValue(registry)==originalCount,"unmaterialized skin stays a template during inspection");
                    var selected=candidates.GetValue(0);int original=inv.GetSlotValue(0);
                    if(creative)((ComponentCreativeInventory)inv).m_slots[0]=inv.GetSlotValue(1);
                    else{inv.RemoveSlotItems(0,1);inv.AddSlotItems(0,inv.GetSlotValue(1),1);}
                    Resolve(selected,out bool replacement);Check("owned-replaced-slot-invalid/"+creative,!replacement,"cannot silently show a different gun in the same slot");
                    registryField.SetValue(null,Activator.CreateInstance(registryType));Resolve(candidates.GetValue(1),out bool world);
                    Check("owned-other-world-invalid/"+creative,!world,"same numeric IDs in another world must not be read");
                }
            }finally{registryField.SetValue(null,previousRegistry);}
            var editorType = mod.GetType("Game.ScGunLayoutScreen");
            var bindingsScreen=(Screen)Activator.CreateInstance(mod.GetType("Game.ScGunBindingsScreen"));
            bindingsScreen.Enter([]);
            var bindingButtons=(Dictionary<string,ButtonWidget>)bindingsScreen.GetType().GetField("m_buttons",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(bindingsScreen);
            Check("bindings-c4-timer-appended-old-actions-preserved",bindingButtons.Keys.ToHashSet().SetEquals(new[]{"fire","reload","scope","silencer","burst","revolver_alt","inspect","knife_heavy","throw_strong","throw_weak","plant_c4","c4_timer"}),"one row per stable action ID, includes firing and both throw strengths");
            Check("bindings-no-third-party-caption",!bindingsScreen.AllChildren.OfType<LabelWidget>().Any(w=>w.Text is string t&&(t.Contains("玲兰")||t.Contains("铃兰")||t.Contains("触控映射"))),"standalone keyboard binding page");
            foreach(Vector2 size in new[]{new Vector2(850,383),new Vector2(360,640),new Vector2(480,850)}) {
                bindingsScreen.Measure(size);bindingsScreen.Arrange(Vector2.Zero,size);bindingsScreen.Measure(size);bindingsScreen.Arrange(Vector2.Zero,size);
                Widget Part(string n)=>(Widget)bindingsScreen.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(bindingsScreen);
                var scroll=(ScrollPanelWidget)Part("m_scroll");var save=Part("m_save");
                Check("bindings-footer/"+size,save.GlobalBounds.Max.X<=size.X&&save.GlobalBounds.Max.Y<=size.Y&&scroll.GlobalBounds.Max.Y<=save.GlobalBounds.Min.Y,"key list scrolls, fixed save/cancel, phone and desktop");
                scroll.ScrollPosition=Math.Max(0,scroll.CalculateScrollAreaLength()-scroll.ActualSize.Y);
                bindingsScreen.Measure(size);bindingsScreen.Arrange(Vector2.Zero,size);
                var padButtons=(System.Collections.Generic.Dictionary<string,ButtonWidget>)bindingsScreen.GetType().GetField("m_padButtons",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(bindingsScreen);
                Check("bindings-last-action-reachable/"+size,padButtons["fire"].GlobalBounds.Max.Y<=scroll.GlobalBounds.Max.Y+.1f
                    &&padButtons["fire"].GlobalBounds.Min.Y>=scroll.GlobalBounds.Min.Y-.1f,"last gamepad action scrolls fully into view above footer");
                scroll.ScrollPosition=0;
            }
            bindingsScreen.Leave();bindingsScreen.Dispose();
            var editor = (Screen)Activator.CreateInstance(editorType);
            editor.WidgetsHierarchyInput = new WidgetInput(); editor.Enter([]);
            Widget EditorField(string name) => (Widget)editorType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
            foreach (Vector2 size in new[] { new Vector2(850,479), new Vector2(1187,637), new Vector2(480,850), new Vector2(708,399) }) {
                foreach (string id in new[] { "reload", "throw_weak", "knife_heavy", "scope" }) {
                    editor.Measure(size); editor.Arrange(Vector2.Zero, size);
                    editorType.GetField("m_selected", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(editor,id);
                    editorType.GetMethod("LoadSelected", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor,null);
                    foreach (bool collapsed in new[] { false, true, false, true }) {
                        editorType.GetField("m_collapsed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(editor,collapsed);
                        string last = null; bool stable = true;
                        for (int frame = 0; frame < 12; frame++) {
                            editor.Update(); editor.Measure(size); editor.Arrange(Vector2.Zero,size);
                            var preview = (CanvasWidget)EditorField("m_preview");
                            string now = string.Join("|", preview.Children.Where(w=>w.IsVisible).Select(w=>$"{w.ActualSize}:{w.GlobalBounds}"));
                            if (frame > 1 && last != now) stable = false;
                            last = now;
                        }
                        var canvas = (CanvasWidget)EditorField("m_preview"); var panel = EditorField("m_panel"); var save = EditorField("m_save");
                        var proxies = canvas.Children.Where(w=>w.IsVisible).ToArray();
                        Check($"editor-stable/{size}/{id}/{collapsed}/{results.Count}", stable && canvas.ActualSize==size
                            && proxies.Length is >=2 and <=4 && proxies.All(w=>!w.IsUpdateEnabled && !w.IsHitTestVisible)
                            && (collapsed ? !panel.IsVisible : save.GlobalBounds.Max.Y<=panel.GlobalBounds.Max.Y+.1f && save.ActualSize.Y>=48),
                            "12 real Update/Measure/Arrange frames: fixed preview area, no oscillation, only concurrent buttons, footer reachable");
                        if (!collapsed) {
                            var scroll = (ScrollPanelWidget)EditorField("m_panelScroll"); scroll.ScrollPosition=Math.Max(0,scroll.CalculateScrollAreaLength()-scroll.ActualSize.Y);
                            editor.Measure(size); editor.Arrange(Vector2.Zero,size);
                            Check($"editor-scroll/{size}/{id}/{results.Count}", scroll.ActualSize.Y>100 && EditorField("m_resetAll").GlobalBounds.Max.Y<=scroll.GlobalBounds.Max.Y+.1f,
                                "settings below sliders can scroll into view without hiding Save/Cancel");
                            scroll.ScrollPosition=0;
                        }
                    }
                }
            }
            var attributes = (Screen)Activator.CreateInstance(mod.GetType("Game.ScGunAttributesScreen"), [template]);
            var recipe = (Screen)Activator.CreateInstance(mod.GetType("Game.ScAssemblyRecipesScreen"));
            foreach(var supply in ((System.Collections.IEnumerable)mod.GetType("Game.ScWorkbenchExtension").GetProperty("All").GetValue(null)).Cast<object>()) {
                int v=(int)supply.GetType().GetProperty("Value").GetValue(supply);
                int count=(int)supply.GetType().GetProperty("ResultCount").GetValue(supply);
                bool creativeOnly=(bool)supply.GetType().GetProperty("CreativeOnly").GetValue(supply);
                var help=BlocksManager.Blocks[Terrain.ExtractContents(v)].GetBlockRecipeScreen(v);help.Enter([v]);
                string text=string.Join("\n",help.AllChildren.OfType<LabelWidget>().Select(l=>l.Text));
                Check("supply-help-screen/"+v,help.GetType()==recipe.GetType()&&text.Contains(creativeOnly?"仅创造模式":$"每批产出 {count} 件"),text);
                var inv=new ComponentInventory();inv.m_slots.Add(new());
                var dialog=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchSelectionDialog"),["补给制作",new[]{supply},56f,(Func<object,string>)(_=>"补给"),(Action<object>)(_=>{}),inv,false]);
                foreach(var size in new[]{new Vector2(360,640),new Vector2(850,479)}){dialog.Measure(size);dialog.Arrange(Vector2.Zero,size);}
                string reason=(string)dialog.GetType().GetMethod("CraftReason",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,null);
                Check("supply-recipe-refusal/"+v,reason.Contains(creativeOnly?"仅创造模式":"材料不足"),reason);
                help.Leave();help.Dispose();dialog.Dispose();
            }
            // Execute the real screen switcher/history and vanilla catalogue Enter/Back, not only Enter([]).
            var savedRoot = ScreensManager.RootWidget; var savedCurrent = ScreensManager.CurrentScreen; var savedPrevious = ScreensManager.PreviousScreen;
            var savedHistory = ScreensManager.HistoryStack.ToArray(); var savedScreens = ScreensManager.m_screens.ToArray(); var savedAnimation = ScreensManager.m_animationData;
            var frameDuration = typeof(Time).GetProperty("FrameDuration"); float savedDuration = Time.FrameDuration;
            bool edge = SettingsManager.AdaptEdgeToEdgeDisplay;
            try {
                frameDuration.SetValue(null, .1f); SettingsManager.AdaptEdgeToEdgeDisplay = false;
                ScreensManager.RootWidget = new CanvasWidget { WidgetsHierarchyInput = new WidgetInput() };
                ScreensManager.CurrentScreen = null; ScreensManager.PreviousScreen = null; ScreensManager.m_animationData = null; ScreensManager.HistoryStack.Clear();
                var game = new Screen(); var help = new HelpBackScreen(); var catalogue = new RecipaediaScreen();
                ScreensManager.m_screens["Game"] = game; ScreensManager.m_screens["Help"] = help; ScreensManager.m_screens["Recipaedia"] = catalogue;
                void Finish() { for (int i = 0; i < 10 && ScreensManager.IsAnimating; i++) ScreensManager.UpdateAnimation(); if (ScreensManager.IsAnimating) throw new Exception("Navigation animation did not finish"); mod.GetType("Game.ScWeaponHelpScreen").GetMethod("AfterEnter").Invoke(null,[ScreensManager.CurrentScreen]); }
                void Switch(Screen screen, params object[] args) { ScreensManager.SwitchScreen(screen, args); Finish(); }
                void Back(Screen screen) { mod.GetType("Game.ScWeaponHelpScreen").GetMethod("GoBack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(screen, null); Finish(); }
                {
                    const string key="zh667.ScCsgoKnives";
                    ModSettingsManager.ModSettingPages.TryGetValue(key,out var priorPages);
                    ModsManager.PackageNameToModEntity.TryGetValue(key,out var priorEntity);
                    var native = new ModSettingsScreen();
                    try {
                        using var installed=ZipFile.OpenRead(packagePath);
                        using var metadata=System.Text.Json.JsonDocument.Parse(installed.GetEntry("modinfo.json").Open());
                        ModSettingsManager.ModSettingPages[key]=ModSettingsParser.ParseSettings(metadata.RootElement.GetProperty("Settings"),key);
                        var entity=(ModEntity)RuntimeHelpers.GetUninitializedObject(typeof(ModEntity));
                        entity.modInfo=new ModInfo{Name="CS武器",PackageName=key};entity.Icon=texture;ModsManager.PackageNameToModEntity[key]=entity;
                        Switch(native);
                        var navigation=native.AllChildren.OfType<BevelledButtonWidget>().Single(w=>w.Text=="CS 枪械");
                        foreach(var c in navigation.AllChildren.OfType<ClickableWidget>())c.IsClicked=true;
                        native.Update();
                        var openWidget=native.AllChildren.Single(w=>w.GetType()==mod.GetType("Game.ScGunModSettingsWidget"));
                        Check("native-settings-no-second-button",!((ContainerWidget)openWidget).AllChildren.OfType<BevelledButtonWidget>().Any(),"native CS page navigates without another click");

                        openWidget.Update();Finish();
                        var settingsEditor=ScreensManager.CurrentScreen;
                        Check("native-mod-settings-opens-settingsEditor",settingsEditor.GetType()==mod.GetType("Game.ScGunSettingsScreen"),"real root page > CS page > custom entry > settingsEditor");
                        settingsEditor.Measure(new Vector2(850,479));settingsEditor.Arrange(Vector2.Zero,new Vector2(850,479));
                        var cancel=(ButtonWidget)settingsEditor.GetType().GetField("m_cancel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(settingsEditor);
                        foreach(var c in cancel.AllChildren.OfType<ClickableWidget>())c.IsClicked=true;
                        settingsEditor.Update();Finish();
                        Check("native-mod-settings-cancel-returns",ReferenceEquals(ScreensManager.CurrentScreen,native),"cancel returns to API Mod Settings without saving settings");
                        native.Update();Finish();
                        Check("native-settings-back-does-not-reopen",ReferenceEquals(ScreensManager.CurrentScreen,native)&&!native.AllChildren.Any(w=>w.GetType()==mod.GetType("Game.ScGunModSettingsWidget")),"back returns to native root without redirect loop");
                        Switch(game);
                    } finally {
                        native.Leave();native.Dispose();
                        if(priorPages is null)ModSettingsManager.ModSettingPages.Remove(key);else ModSettingsManager.ModSettingPages[key]=priorPages;
                        if(priorEntity is null)ModsManager.PackageNameToModEntity.Remove(key);else ModsManager.PackageNameToModEntity[key]=priorEntity;
                    }
                }
                ScreensManager.HistoryStack.Clear();
                Switch(game);
                var materialBlock=BlocksManager.Blocks[BlocksManager.BlockTypeToIndex[mod.GetType("Game.ScWeaponMaterialBlock")]];
                var materialValues=materialBlock.GetCreativeValues().Concat(((System.Collections.IEnumerable)mod.GetType("Game.ScWorkbenchExtension").GetProperty("All").GetValue(null)).Cast<object>().Select(r=>(int)r.GetType().GetProperty("Value").GetValue(r))).ToArray();
                bool RecognizedRecipe(Screen page,int value)=>page.GetType()==recipe.GetType()&&page.AllChildren.OfType<LabelWidget>().Any(l=>l.Text.Contains("每批产出")||l.Text.Contains("仅创造模式"))&&page.AllChildren.OfType<LabelWidget>().Any(l=>l.Text==BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(null,value));
                foreach(int materialValue in materialValues) {
                    Switch(catalogue);
                    catalogue.m_blocksList.ClearItems();catalogue.m_blocksList.AddItem(materialValue);catalogue.m_blocksList.SelectedItem=materialValue;
                    var materialLoader=(ModLoader)Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
                    materialLoader.AfterWidgetUpdate(catalogue);
                    foreach(var clickable in catalogue.m_recipesButton.AllChildren.OfType<ClickableWidget>())clickable.IsClicked=true;
                    materialLoader.BeforeWidgetUpdate(catalogue);materialLoader.AfterWidgetUpdate(catalogue);Finish();
                    var materialPage=ScreensManager.CurrentScreen;
                    Check("material-native-recipe/"+materialValue,RecognizedRecipe(materialPage,materialValue),"actual native browser hook to component/supply recipe");
                    Back(materialPage);Switch(game);
                }
                for (int cycle = 0; cycle < 4; cycle++) {
                    Switch(help); Switch(catalogue);
                    var foreign = new Screen(); ScreensManager.m_screens["RecipaediaRecipes"] = foreign;
                    var open = mod.GetType("Game.ScWeaponHelpScreen").GetMethod("Open");
                    open.Invoke(null,[true,template]); Finish(); var a=ScreensManager.CurrentScreen;
                    open.Invoke(null,[false,template]); Finish();
                    open.Invoke(null,[true,template]); Finish();
                    Check("navigation/foreign-slot-preserved/"+cycle,ReferenceEquals(ScreensManager.m_screens["RecipaediaRecipes"],foreign)&&ReferenceEquals(ScreensManager.CurrentScreen,a),"independent pages leave third-party recipe instance untouched; toggle returns to parent");
                    Back(a);
                    bool atCatalogue = ScreensManager.CurrentScreen == catalogue && catalogue.m_previousScreen == help && ScreensManager.TopOfHistoryScreen == help;
                    catalogue.m_listCategoryIndex = catalogue.m_categoryIndex;
                    var backButton = catalogue.Children.Find<BevelledButtonWidget>("TopBar.Back"); backButton.m_clickableWidget.IsClicked = true;
                    catalogue.Update(); Finish(); help.Update(); Finish();
                    Check($"navigation/catalogue-help-exit/{cycle}", atCatalogue && ScreensManager.CurrentScreen == game && ScreensManager.HistoryStack.Count == 0,
                        "real SwitchScreen/animation/Recipaedia.Enter/Recipaedia.Update/Help GoBack exits; no history loop");
                }
                var stationPage = (Screen)Activator.CreateInstance(attributes.GetType(), [template]); Switch(stationPage, template);
                var stationRecipe = (Screen)Activator.CreateInstance(recipe.GetType()); Switch(stationRecipe, template); Back(stationRecipe); Back(stationPage);
                Check("navigation/workbench-returns-to-game", ScreensManager.CurrentScreen == game && ScreensManager.HistoryStack.Count == 0, "direct game entry does not return through catalogue/help");
                if (thirdPartyDlls is not null) {
                    var ex = thirdPartyDlls.Load("RecipaediaEX"); var browserType = ex.GetType("RecipaediaEX.UI.RecipaediaEXScreen");
                    var browser = (Screen)Activator.CreateInstance(browserType); var exRecipe = (Screen)Activator.CreateInstance(ex.GetType("RecipaediaEX.UI.RecipaediaEXRecipesScreen"));
                    // Seed one category so Enter exercises real return ownership without requiring a populated game database.
                    browserType.GetField("m_categoriesInitialized").SetValue(browser, true);
                    ((List<string>)browserType.GetField("m_categoriesName").GetValue(browser)).Add("fixture");
                    browserType.GetField("m_selectedCategory").SetValue(browser, "fixture");
                    browserType.GetField("m_listCategory").SetValue(browser, "fixture");
                    var categoryType=ex.GetType("RecipaediaEX.Implementation.BlocksCategory");
                    var category=RuntimeHelpers.GetUninitializedObject(categoryType);categoryType.GetField("m_id").SetValue(category,"fixture");categoryType.GetField("m_displayName").SetValue(category,"fixture");
                    var catalogType=ex.GetType("RecipaediaEX.UI.RecipaediaCategoryCatalog");
                    var categories=(System.Collections.IDictionary)catalogType.GetField("m_categories",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
                    categories.Add("fixture",category);catalogType.GetField("m_loaded",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,true);
                    var list = (ListPanelWidget)browserType.GetField("m_blocksList").GetValue(browser);
                    var button = (ButtonWidget)browserType.GetField("m_recipesButton").GetValue(browser);
                    var item = Activator.CreateInstance(ex.GetType("RecipaediaEX.Implementation.BlockItem"), [realGun, 0, template]);
                    list.AddItem(item); list.SelectedItem = item;
                    ScreensManager.m_screens["Recipaedia"] = browser; ScreensManager.m_screens["RecipaediaRecipes"] = exRecipe;
                    var loader = (ModLoader)Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
                    for (int cycle = 0; cycle < 4; cycle++) {
                        Switch(browser); loader.AfterWidgetUpdate(browser);
                        foreach (var click in button.AllChildren.OfType<ClickableWidget>()) click.IsClicked = true;
                        loader.BeforeWidgetUpdate(browser);
                        Check("recipaedia-dll/click-consumed/" + cycle, !button.IsClicked, "generic empty recipe page cannot receive the same CS click");
                        browser.Update();
                        Check("recipaedia-dll/actual-update-stays-in-browser/"+cycle,ScreensManager.CurrentScreen==browser,"real EX.Update receives the consumed click before CS AfterWidgetUpdate");
                        loader.AfterWidgetUpdate(browser); Finish(); var child = ScreensManager.CurrentScreen;
                        bool childContract = (bool)browserType.GetMethod("IsChildScreen", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [child]);
                        Check("recipaedia-dll/own-page-and-child-contract/" + cycle, child.GetType() == attributes.GetType() && childContract && ScreensManager.m_screens["RecipaediaRecipes"] == exRecipe, thirdPartyDlls.Hash("RecipaediaEX"));
                        mod.GetType("Game.ScWeaponHelpScreen").GetMethod("Open").Invoke(null, [false, template]); Finish(); Back(ScreensManager.CurrentScreen); Back(child);
                        Check("recipaedia-dll/return-owner/" + cycle, ScreensManager.CurrentScreen == browser && ReferenceEquals(browserType.GetField("m_previousScreen").GetValue(browser), game), "real EX.Enter keeps original owner after attributes/assembly roundtrip");
                    }
                    foreach(int materialValue in materialValues) {
                        list.ClearItems();var materialItem=Activator.CreateInstance(ex.GetType("RecipaediaEX.Implementation.BlockItem"),[BlocksManager.Blocks[Terrain.ExtractContents(materialValue)],0,materialValue]);
                        list.AddItem(materialItem);list.SelectedItem=materialItem;Switch(browser);loader.AfterWidgetUpdate(browser);
                        foreach(var clickable in button.AllChildren.OfType<ClickableWidget>())clickable.IsClicked=true;
                        loader.BeforeWidgetUpdate(browser);browser.Update();loader.AfterWidgetUpdate(browser);Finish();
                        var materialPage=ScreensManager.CurrentScreen;
                        Check("material-ex-recipe/"+materialValue,RecognizedRecipe(materialPage,materialValue),"real EX update + component/supply recipe, no shared page replacement");
                        Back(materialPage);Switch(game);
                    }
                    attributes.Enter([template]); attributes.Enter([new object()]);
                    Check("recipaedia-dll/unknown-parameter-no-stale-gun", (bool)attributes.GetType().GetField("m_invalidEntry", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(attributes), "unknown nonempty parameter explicitly rejected even after a valid gun");
                    Switch(game);
                    categories.Remove("fixture");catalogType.GetField("m_loaded",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);
                }
            } finally {
                frameDuration.SetValue(null, savedDuration); SettingsManager.AdaptEdgeToEdgeDisplay = edge;
                ScreensManager.RootWidget = savedRoot; ScreensManager.CurrentScreen = savedCurrent; ScreensManager.PreviousScreen = savedPrevious; ScreensManager.m_animationData = savedAnimation;
                ScreensManager.HistoryStack.Clear(); foreach (var s in savedHistory.Reverse()) ScreensManager.HistoryStack.Push(s);
                ScreensManager.m_screens.Clear(); foreach (var p in savedScreens) ScreensManager.m_screens[p.Key] = p.Value;
            }
            // Includes the user's 1536x825 at the game's 850-unit UI width, maximum UI scale,
            // landscape phones, portrait, wide windows, and resize back through previous modes.
            Vector2[] sizes = [new(850, 479), new(708, 399), new(1000, 479), new(1200, 675), new(480, 850), new(360, 640), new(850, 479)];
            foreach (var screen in new[] { attributes, recipe }) {
                string page = screen == attributes ? "attributes" : "recipe";
                screen.Enter([]); // formerly crashed on recipe -> attributes -> Back (no parameters).
                screen.Enter([template]);
                foreach (var size in sizes) {
                    screen.Measure(size); screen.Arrange(Vector2.Zero, size);
                    screen.Measure(size); screen.Arrange(Vector2.Zero, size);
                    string tag = $"{page}/{size.X}x{size.Y}/{results.Count}";
                    var body = screen.Children.Find<CanvasWidget>("ScWeaponHelp.Body");
                    var back = screen.Children.Find<ButtonWidget>("TopBar.Back");
                    var panorama = screen.Children.OfType<PanoramaWidget>().Single();
                    bool Inside(Widget w, Widget parent) => w.GlobalBounds.Min.X >= parent.GlobalBounds.Min.X - .1f
                        && w.GlobalBounds.Min.Y >= parent.GlobalBounds.Min.Y - .1f && w.GlobalBounds.Max.X <= parent.GlobalBounds.Max.X + .1f
                        && w.GlobalBounds.Max.Y <= parent.GlobalBounds.Max.Y + .1f;
                    Check(tag + "/native-shell", panorama.IsVisibleGlobal && panorama.ActualSize == size && back.IsVisibleGlobal
                        && body.GlobalBounds.Min.X >= 64 && Inside(body, screen), $"native panorama {panorama.ActualSize}; body {body.GlobalBounds}; Back visible");
                    Check(tag + "/no-vanilla-recipes", screen.Children.Find<CraftingRecipeWidget>("CraftingRecipe", false) is null
                        && screen.Children.Find<ButtonWidget>("PreviousRecipe", false) is null, "old recipe content detached, not hidden under the new content");
                    if (screen != attributes) continue;
                    var left = screen.Children.Find<CanvasWidget>("ScGunAttributes.LeftHost");
                    var right = screen.Children.Find<CanvasWidget>("ScGunAttributes.RightHost");
                    bool separated = left.GlobalBounds.Max.X <= right.GlobalBounds.Min.X + .1f || left.GlobalBounds.Max.Y <= right.GlobalBounds.Min.Y + .1f;
                    Check(tag + "/panels", Inside(left, body) && Inside(right, body) && separated && right.ActualSize.X > 250 && right.ActualSize.Y > 150,
                        $"left {left.GlobalBounds}; right {right.GlobalBounds}");
                    Widget Field(string name) => (Widget)screen.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen);
                    var scroll = (ScrollPanelWidget)Field("m_barScroll");
                    var button = Field("m_recipe");
                    Check(tag + "/scroll-and-actions", Inside(scroll, right) && Inside(button, right) && button.ActualSize.Y >= 48
                        && scroll.ActualSize.Y > 100 && scroll.GlobalBounds.Max.Y <= button.GlobalBounds.Min.Y + .1f,
                        $"scroll {scroll.ActualSize}; action {button.GlobalBounds}; content scrolls, buttons stay visible");
                    var bars = (ContainerWidget)Field("m_bars");
                    var labels = bars.AllChildren.OfType<LabelWidget>().Where(w => w.IsVisibleGlobal).ToArray();
                    Check(tag + "/all-stats", labels.Count(w => w.Text.EndsWith("攻击力") || w.Text.EndsWith(" 格") || w.Text.EndsWith(" 秒") || w.Text.EndsWith(" 发/分") || w.Text.EndsWith(" 发") || w.Text.EndsWith(" °")) >= 6
                        && labels.All(w => float.IsFinite(w.ActualSize.X) && w.ActualSize.X > 0 && w.GlobalBounds.Max.X <= right.GlobalBounds.Max.X + .1f),
                        $"{labels.Length} stat labels measured using native font, finite widths inside card");
                    // Scrolling to the end must expose the final growth text above the fixed actions.
                    scroll.ScrollPosition = Math.Max(0, scroll.CalculateScrollAreaLength() - scroll.ActualSize.Y);
                    screen.Measure(size); screen.Arrange(Vector2.Zero, size);
                    Check(tag + "/scroll-bottom", Field("m_growth").GlobalBounds.Max.Y <= scroll.GlobalBounds.Max.Y + .1f, "last description reachable without covering footer");
                    scroll.ScrollPosition = 0;
                    int count = ((ListPanelWidget)Field("m_list")).Items.Count;
                    Check(tag + "/catalogue-includes-skins", count == 79, "35 factory entries plus all 44 supported finishes");
                    int recordCount = (int)registryType.GetProperty("Count").GetValue(registryField.GetValue(null));
                    var select = screen.GetType().GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Instance);
                    for (int v = 0; v < count; v++) {
                        select.Invoke(screen, [v]);
                        screen.Measure(size); screen.Arrange(Vector2.Zero, size);
                        var header = screen.Children.Find<StackPanelWidget>("ScGunAttributes.Header");
                        var allLabels = header.AllChildren.Concat(bars.AllChildren).OfType<LabelWidget>().Where(w => w.IsVisibleGlobal);
                        Check(tag + $"/weapon-{v}", allLabels.All(w => w.ActualSize.X > 0 && float.IsFinite(w.ActualSize.Y)
                            && w.GlobalBounds.Min.X >= right.GlobalBounds.Min.X - .1f && w.GlobalBounds.Max.X <= right.GlobalBounds.Max.X + .1f),
                            "real header + eight stat rows, including long names and Zeus charge, stay inside card");
                        if (v >= 35) {
                            var skin = ((Array)mod.GetType("Game.ScGunSkinCatalog").GetField("All").GetValue(null)).GetValue(v - 35);
                            int paint = (int)skin.GetType().GetProperty("PaintId").GetValue(skin);
                            string skinName = (string)skin.GetType().GetProperty("Name").GetValue(skin);
                            string asset = (string)skin.GetType().GetProperty("Gun").GetValue(skin);
                            int selectedValue = (int)screen.GetType().GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen);
                            float basePower = (float)mod.GetType("Game.ScSurvivalBalance").GetMethod("Power").Invoke(null, [asset]);
                            var levelPreview = screen.GetType().GetMethod("PreviewLevel", BindingFlags.NonPublic | BindingFlags.Instance);
                            bool correct = Terrain.ExtractData(selectedValue) == paint && ((BlockIconWidget)Field("m_preview")).Value == selectedValue
                                && ((LabelWidget)Field("m_name")).Text.Contains(skinName);
                            foreach (int level in new[] { 0, 10 }) {
                                levelPreview.Invoke(screen, [level]);
                                var damageLabels = bars.AllChildren.OfType<LabelWidget>().Where(l => l.Text.EndsWith(" 攻击力")).ToArray();
                                float shown = float.Parse(damageLabels.First().Text.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
                                float bonus = asset switch { "nova"=>21, "xm1014"=>12, "sawedoff"=>24, "mag7"=>18, _=>0 };
                                correct &= Math.Abs(shown - (basePower+bonus*(1-level/20f)) * (level == 0 ? 1.5f : 3f)) < .051f;
                            }
                            recipe.Enter([selectedValue]); // skin catalogue -> recipe uses correct factory model and keeps source value
                            Check(tag + $"/skin-preview-{paint}", correct && (int)registryType.GetProperty("Count").GetValue(registryField.GetValue(null)) == recordCount,
                                "skin icon/name/effective shotgun bonus with 1.5x Lv0 and 3x Lv10; recipe reachable; no registry allocations while browsing");
                        }
                    }
                    select.Invoke(screen, [0]);
                    var preview = screen.GetType().GetMethod("PreviewLevel", BindingFlags.NonPublic | BindingFlags.Instance);
                    var levelField = screen.GetType().GetField("m_previewLevel", BindingFlags.NonPublic | BindingFlags.Instance);
                    int originalValue = (int)screen.GetType().GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen);
                    int maxLevel = (int)mod.GetType("Game.ScGunGrowth").GetField("MaxLevel").GetRawConstantValue();
                    for (int lv = 0; lv <= maxLevel; lv++) {
                        preview.Invoke(screen, [lv]); screen.Measure(size); screen.Arrange(Vector2.Zero, size);
                        var down = (ButtonWidget)Field("m_levelDown"); var up = (ButtonWidget)Field("m_levelUp"); var levelButton = (ButtonWidget)Field("m_level");
                        var future = (System.Collections.IList)screen.GetType().GetField("m_futureRows", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen);
                        Check(tag + $"/preview-{lv}", (int)levelField.GetValue(screen) == lv && down.IsEnabled == (lv > 0) && up.IsEnabled == (lv < maxLevel)
                            && levelButton.Text.Contains($"Lv{lv} / {maxLevel}") && (lv == 0 ? future.Count == 0 : future.Count > 0)
                            && up.GlobalBounds.Max.X <= right.GlobalBounds.Max.X + .1f
                            && (int)screen.GetType().GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen) == originalValue,
                            "read-only level selector, boundary buttons, future highlight targets and narrow layout");
                    }
                    preview.Invoke(screen, [-100]); Check(tag + "/preview-clamp-low", (int)levelField.GetValue(screen) == 0, "Lv0 lower bound");
                    preview.Invoke(screen, [100]); Check(tag + "/preview-clamp-high", (int)levelField.GetValue(screen) == maxLevel, "LvMax upper bound");
                    select.Invoke(screen, [0]);
                }
                screen.Enter([]); // return with empty parameters after a populated page
                screen.Measure(sizes[0]); screen.Arrange(Vector2.Zero, sizes[0]);
                Check(page + "/empty-return", true, "empty Enter before/after browsing does not invoke vanilla parameters[0]");
            }
            // Open an actual skin+counter instance, visit its factory counterpart, then return to that finish.
            var skinCounter = counterBlock.GetCreativeValues().Last();
            var sourceInv = new ComponentInventory(); sourceInv.m_slots.Add(new()); sourceInv.AddSlotItems(0, skinCounter, 1);
            counterType.GetMethod("Materialize").Invoke(null, [sourceInv, 0, "source-instance"]);
            int sourceValue = sourceInv.GetSlotValue(0); var gunSpecType = mod.GetType("Game.GunSpec");
            int sourceId = (int)gunSpecType.GetMethod("GetId").Invoke(null, [Terrain.ExtractData(sourceValue)]);
            var table = registryField.GetValue(null); var sourceRecord = registryType.GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(table, [sourceId]);
            sourceRecord.GetType().GetField("KillCount").SetValue(sourceRecord, 345L); sourceRecord.GetType().GetField("AppliedGrowthLevel").SetValue(sourceRecord, 3);
            var instancePage = (Screen)Activator.CreateInstance(attributes.GetType(), [sourceValue]); instancePage.Enter([sourceValue]); instancePage.Measure(new(850,479)); instancePage.Arrange(Vector2.Zero,new(850,479));
            var indexField = instancePage.GetType().GetField("m_entryIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            var valueField = instancePage.GetType().GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance);
            int sourceEntry = (int)indexField.GetValue(instancePage); var selector = instancePage.GetType().GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Instance);
            bool instanceOk = sourceEntry >= 35 && (int)valueField.GetValue(instancePage) == sourceValue;
            selector.Invoke(instancePage, [1]); instanceOk &= (int)valueField.GetValue(instancePage) != sourceValue;
            selector.Invoke(instancePage, [sourceEntry]);
            var counterLabel = (LabelWidget)instancePage.GetType().GetField("m_counter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instancePage);
            Check("skin-instance-return-preserves-counter", instanceOk && (int)valueField.GetValue(instancePage) == sourceValue
                && counterLabel.Text.Contains("345") && counterLabel.Text.Contains("Lv3"), "factory and painted variants stay distinct; original skin instance restored with actual kills/level");
        } catch (Exception e) { Check("setup-or-layout", false, e.ToString()); }
        finally {
            caches.Clear(); foreach (var p in savedCaches) caches[p.Key] = p.Value;
            TextureAtlasManager.m_subtextures.Clear(); foreach (var p in savedAtlas) TextureAtlasManager.m_subtextures[p.Key] = p.Value;
            for (int i = 0; i < savedBlocks.Length; i++) BlocksManager.Blocks[i] = savedBlocks[i];
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var p in savedTypes) BlocksManager.BlockTypeToIndex[p.Key] = p.Value;
            BlocksManager.BlockNameToIndex.Clear(); foreach (var p in savedNames) BlocksManager.BlockNameToIndex[p.Key] = p.Value;
            LabelWidget.m_bitmapFont = savedFont;
        }
        return results;
    }
}
