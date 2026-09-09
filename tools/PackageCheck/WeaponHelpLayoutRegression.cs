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
            foreach (string name in new[] { "ScGunBlock", "ScKnifeBlock", "ScWeaponMaterialBlock", "ScAmmoBlock", "ScWeaponWorkbenchBlock", "ScGunSkinTemplateBlock", "ScGunCounterTemplateBlock" }) {
                var type = mod.GetType("Game." + name, true);
                var block = (Block)Activator.CreateInstance(type);
                block.BlockIndex = index;
                BlocksManager.Blocks[index] = block;
                BlocksManager.BlockTypeToIndex[type] = index;
                BlocksManager.BlockNameToIndex[name] = index++;
            }
            int template = (int)mod.GetType("Game.ScGunAttributes").GetMethod("TemplateValue").Invoke(null, [0]);
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
            var attributes = (Screen)Activator.CreateInstance(mod.GetType("Game.ScGunAttributesScreen"), [template]);
            var recipe = (Screen)Activator.CreateInstance(mod.GetType("Game.ScAssemblyRecipesScreen"));
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
                    int count = ((Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null)).Length;
                    var select = screen.GetType().GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Instance);
                    for (int v = 0; v < count; v++) {
                        select.Invoke(screen, [v]);
                        screen.Measure(size); screen.Arrange(Vector2.Zero, size);
                        var header = screen.Children.Find<StackPanelWidget>("ScGunAttributes.Header");
                        var allLabels = header.AllChildren.Concat(bars.AllChildren).OfType<LabelWidget>().Where(w => w.IsVisibleGlobal);
                        Check(tag + $"/weapon-{v}", allLabels.All(w => w.ActualSize.X > 0 && float.IsFinite(w.ActualSize.Y)
                            && w.GlobalBounds.Min.X >= right.GlobalBounds.Min.X - .1f && w.GlobalBounds.Max.X <= right.GlobalBounds.Max.X + .1f),
                            "real header + eight stat rows, including long names and Zeus charge, stay inside card");
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
