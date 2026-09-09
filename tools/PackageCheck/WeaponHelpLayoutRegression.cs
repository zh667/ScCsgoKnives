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
    internal static List<Result> Run(Assembly mod, string contentPath) {
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
            if (LabelWidget.BitmapFont is null) throw new Exception("Failed to read native font metrics");
            for (int i = 0; i < BlocksManager.Blocks.Length; i++) BlocksManager.Blocks[i] = new AirBlock { BlockIndex = i };
            int index = 700;
            foreach (Block material in new Block[] { new DiamondChunkBlock(), new GermaniumChunkBlock() }) {
                int materialIndex = material is DiamondChunkBlock ? 730 : 731;
                material.BlockIndex = materialIndex; BlocksManager.Blocks[materialIndex] = material;
                BlocksManager.BlockTypeToIndex[material.GetType()] = materialIndex; BlocksManager.BlockNameToIndex[material.GetType().Name] = materialIndex;
            }
            foreach (string name in new[] { "ScGunBlock", "ScKnifeBlock", "ScWeaponMaterialBlock", "ScAmmoBlock", "ScWeaponWorkbenchBlock", "ScGunSkinTemplateBlock", "ScGunCounterTemplateBlock" }) {
                var type = mod.GetType("Game." + name, true);
                var block = (Block)Activator.CreateInstance(type);
                block.BlockIndex = index;
                BlocksManager.Blocks[index] = block;
                BlocksManager.BlockTypeToIndex[type] = index;
                BlocksManager.BlockNameToIndex[name] = index++;
            }
            int template = (int)mod.GetType("Game.ScGunAttributes").GetMethod("TemplateValue").Invoke(null, [0]);
            var craftItems=((Array)mod.GetType("Game.ScWeaponCrafting").GetField("All").GetValue(null)).Cast<object>().ToArray();
            foreach(bool creative in new[]{false,true}) {
                var menu=(object[])mod.GetType("Game.SubsystemScWeaponWorkbench").GetMethod("MainMenuItems",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                var inv=new ComponentInventory();inv.m_slots.Add(new());
                var dialog=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchSelectionDialog"),["功能菜单",menu,56f,
                    (Func<object,string>)(o=>o.GetType().Name),(Action<object>)(_=>{}),inv,creative]);
                dialog.GetType().GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,["功能"]);
                dialog.Measure(new(850,479));dialog.Arrange(Vector2.Zero,new(850,479));
                var list=(ListPanelWidget)dialog.GetType().GetField("m_list",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                Check($"workbench-all-functions-visible/{creative}",list.Items.Count==4&&list.Items.Any(o=>o.GetType().Name=="SkinMenu")
                    && menu.Length==craftItems.Length+4,"actual runtime menu -> 功能 filter contains repair, skins, counter and attributes in both modes");
            }
            foreach(bool creative in new[]{false,true})foreach(var available in new[]{new Vector2(1100,650),new Vector2(850,479),new Vector2(708,399),new Vector2(480,850),new Vector2(360,640),new Vector2(850,270)}){
                var inv=new ComponentInventory();inv.m_slots.Add(new()); int choices=0;
                var browse=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchSelectionDialog"),["武器装配台 · 组装 / 维修 / 涂装 / 计数器",craftItems,56f,
                    (Func<object,string>)(o=>(string)o.GetType().GetProperty("Name").GetValue(o)),(Action<object>)(_=>choices++),inv,creative]);
                browse.WidgetsHierarchyInput=new WidgetInput();browse.Measure(available);browse.Arrange(Vector2.Zero,available);
                Widget Part(string n)=>(Widget)browse.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(browse);
                bool Inside(Widget w)=>w.GlobalBounds.Min.X>=browse.GlobalBounds.Min.X-.1f && w.GlobalBounds.Max.X<=browse.GlobalBounds.Max.X+.1f
                    && w.GlobalBounds.Min.Y>=browse.GlobalBounds.Min.Y-.1f && w.GlobalBounds.Max.Y<=browse.GlobalBounds.Max.Y+.1f;
                bool ok=Inside(Part("m_listHost"))&&(!Part("m_previewHost").IsVisible||Inside(Part("m_previewHost")))&&Inside(Part("m_detailHost"))&&Inside(Part("m_confirm"))&&Part("m_confirm").ActualSize.Y>=48;
                var select=browse.GetType().GetMethod("Select",BindingFlags.NonPublic|BindingFlags.Instance);
                foreach(var item in craftItems){select.Invoke(browse,[item]);browse.Measure(available);browse.Arrange(Vector2.Zero,available);ok &= Inside(Part("m_confirm"))&&Part("m_detailScroll").ActualSize.Y>40;}
                Check($"workbench-browse/{creative}/{available}",ok&&choices==0,"all 57 weapon recipes: list, preview, scrollable material area and fixed footer; browsing never commits");
                browse.GetType().GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(browse,["步枪"]);
                Check($"workbench-category/{creative}/{available}",((ListPanelWidget)Part("m_list")).Items.Count==7,"category filters rifles without losing available recipes");
                var confirm=(Dialog)Activator.CreateInstance(mod.GetType("Game.ScWorkbenchConfirmDialog"),["长名称 · 计数器皮肤枪械",string.Join("\n",Enumerable.Repeat("材料需要 6 件，当前持有 12 件；枪械状态保持不变。",80)),"确认组装","返回",(Action<MessageDialogButton>)(_=>choices++)]);
                confirm.WidgetsHierarchyInput=new WidgetInput();confirm.Measure(available);confirm.Arrange(Vector2.Zero,available);
                var yes=(ButtonWidget)confirm.GetType().GetField("m_yes",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(confirm);
                var scroll=(ScrollPanelWidget)confirm.GetType().GetField("m_scroll",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(confirm);
                scroll.ScrollPosition=Math.Max(0,scroll.CalculateScrollAreaLength()-scroll.ActualSize.Y);confirm.Measure(available);confirm.Arrange(Vector2.Zero,available);
                Check($"workbench-long-quote/{creative}/{available}",yes.ActualSize.Y>=48&&yes.GlobalBounds.Max.Y<=confirm.GlobalBounds.Max.Y+.1f&&scroll.GlobalBounds.Max.Y<yes.GlobalBounds.Min.Y&&choices==0,
                    "80-line quote scrolls; confirmation remains visible and cannot be executed by simply selecting a row");
                ((BevelledButtonWidget)yes).m_clickableWidget.IsClicked=true;confirm.Update();confirm.Update();
                Check($"workbench-confirm-once/{creative}/{available}",choices==1,"explicit confirmation dispatches the existing transaction callback once, not once per held frame");
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
                foreach (bool creative in new[] { false, true }) foreach (int initialKills in new[] { 99, 104 }) {
                    object registry = Activator.CreateInstance(registryType); registryField.SetValue(null, registry);
                    IInventory inv;
                    if (creative) { var ci = new ComponentCreativeInventory { OpenSlotsCount = 10 }; for (int i = 0; i < 10; i++) ci.m_slots.Add(0); inv = ci; }
                    else { var si = new ComponentInventory(); si.m_slots.Add(new()); inv = si; }
                    inv.AddSlotItems(0, counterBlock.GetCreativeValues().First(), 1);
                    counterType.GetMethod("Materialize").Invoke(null, [inv, 0, "notify-test"]);
                    int held = inv.GetSlotValue(0);
                    var specType = mod.GetType("Game.GunSpec"); int id = (int)specType.GetMethod("GetId").Invoke(null, [Terrain.ExtractData(held)]);
                    object record = registryType.GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(registry, [id]); record.GetType().GetField("KillCount").SetValue(record, (long)initialKills);
                    if (initialKills == 99) { object queue = registryType.GetField("Kills").GetValue(registry); queue.GetType().GetMethod("Enqueue").Invoke(queue, [id, 0]); }
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
                        && realGun.GetDisplayName(null, held).Contains("Lv1") && (long)record.GetType().GetField("KillCount").GetValue(record) == Math.Max(initialKills, 100),
                        "actual subsystem UpdateGrowth -> rule -> transaction -> GetDisplayName -> player ComponentGui callback; first sweep and repeat; both real inventory classes");
                }
            } finally { registryField.SetValue(null, previousRegistry); }
            var editorType = mod.GetType("Game.ScGunLayoutScreen");
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
                            && proxies.Length is >=2 and <=3 && proxies.All(w=>!w.IsUpdateEnabled && !w.IsHitTestVisible)
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
                void Finish() { for (int i = 0; i < 10 && ScreensManager.IsAnimating; i++) ScreensManager.UpdateAnimation(); if (ScreensManager.IsAnimating) throw new Exception("Navigation animation did not finish"); }
                void Switch(Screen screen, params object[] args) { ScreensManager.SwitchScreen(screen, args); Finish(); }
                void Back(Screen screen) { mod.GetType("Game.ScWeaponHelpScreen").GetMethod("GoBack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(screen, null); Finish(); }
                Switch(game);
                for (int cycle = 0; cycle < 4; cycle++) {
                    Switch(help); Switch(catalogue);
                    var a = (Screen)Activator.CreateInstance(attributes.GetType(), [template]); ScreensManager.m_screens["RecipaediaRecipes"] = a; Switch(a, template);
                    var b = (Screen)Activator.CreateInstance(recipe.GetType()); ScreensManager.m_screens["RecipaediaRecipes"] = b; Switch(b, template);
                    var c = (Screen)Activator.CreateInstance(attributes.GetType(), [template]); ScreensManager.m_screens["RecipaediaRecipes"] = c; Switch(c, template);
                    // Simulate a registered recipe instance changing after entry, as in the reported loop.
                    ScreensManager.m_screens["RecipaediaRecipes"] = b;
                    Back(c);
                    bool atCatalogue = ScreensManager.CurrentScreen == catalogue && catalogue.m_previousScreen == help && ScreensManager.TopOfHistoryScreen == help;
                    catalogue.m_listCategoryIndex = catalogue.m_categoryIndex;
                    var backButton = catalogue.Children.Find<BevelledButtonWidget>("TopBar.Back"); backButton.m_clickableWidget.IsClicked = true;
                    catalogue.Update(); Finish(); help.Update(); Finish();
                    Check($"navigation/catalogue-help-exit/{cycle}", atCatalogue && ScreensManager.CurrentScreen == game && ScreensManager.HistoryStack.Count == 0,
                        "real SwitchScreen/animation/Recipaedia.Enter/Recipaedia.Update/Help GoBack exits; no history loop");
                }
                var stationPage = (Screen)Activator.CreateInstance(attributes.GetType(), [template]); Switch(stationPage, template);
                var stationRecipe = (Screen)Activator.CreateInstance(recipe.GetType()); Switch(stationRecipe, template); Back(stationRecipe);
                Check("navigation/workbench-returns-to-game", ScreensManager.CurrentScreen == game && ScreensManager.HistoryStack.Count == 0, "direct game entry does not return through catalogue/help");
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
                    Check(tag + "/catalogue-includes-skins", count == 46, "35 factory entries plus all 11 supported finishes");
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
                                correct &= Math.Abs(shown - basePower * (level == 0 ? 1.5f : 3f)) < .051f;
                            }
                            recipe.Enter([selectedValue]); // skin catalogue -> recipe uses correct factory model and keeps source value
                            Check(tag + $"/skin-preview-{paint}", correct && (int)registryType.GetProperty("Count").GetValue(registryField.GetValue(null)) == recordCount,
                                "skin icon/name/1.5x Lv0 and 3x Lv10; recipe reachable; no registry allocations while browsing");
                        }
                    }
                    select.Invoke(screen, [0]);
                    var preview = screen.GetType().GetMethod("PreviewLevel", BindingFlags.NonPublic | BindingFlags.Instance);
                    var levelField = screen.GetType().GetField("m_previewLevel", BindingFlags.NonPublic | BindingFlags.Instance);
                    int originalValue = (int)screen.GetType().GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen);
                    for (int lv = 0; lv <= 10; lv++) {
                        preview.Invoke(screen, [lv]); screen.Measure(size); screen.Arrange(Vector2.Zero, size);
                        var down = (ButtonWidget)Field("m_levelDown"); var up = (ButtonWidget)Field("m_levelUp"); var levelButton = (ButtonWidget)Field("m_level");
                        var future = (System.Collections.IList)screen.GetType().GetField("m_futureRows", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen);
                        Check(tag + $"/preview-{lv}", (int)levelField.GetValue(screen) == lv && down.IsEnabled == (lv > 0) && up.IsEnabled == (lv < 10)
                            && levelButton.Text.Contains($"Lv{lv} / 10") && (lv == 0 ? future.Count == 0 : future.Count > 0)
                            && up.GlobalBounds.Max.X <= right.GlobalBounds.Max.X + .1f
                            && (int)screen.GetType().GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(screen) == originalValue,
                            "read-only level selector, boundary buttons, future highlight targets and narrow layout");
                    }
                    preview.Invoke(screen, [-100]); Check(tag + "/preview-clamp-low", (int)levelField.GetValue(screen) == 0, "Lv0 lower bound");
                    preview.Invoke(screen, [100]); Check(tag + "/preview-clamp-high", (int)levelField.GetValue(screen) == 10, "Lv10 upper bound");
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
