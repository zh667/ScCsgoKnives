using System.Text.Json;
using Engine;
namespace Game;

/// <summary>Where one HUD element of the deathmatch sits on this device (DM-10): the centre as a fraction of the controls
/// area (so a resolution, a UI scale or a rotation of the phone moves it with the screen, never off it), its size, its
/// turn in degrees, its opacity and whether it is shown at all. Display only: nothing a rule depends on reads these.</summary>
public sealed class DmElementLayout {
    public bool Visible { get; set; } = true;
    public float X { get; set; } = .5f;
    public float Y { get; set; } = .5f;
    public float Scale { get; set; } = 1f;
    /// <summary>Degrees, clockwise on screen.</summary>
    public float Rotation { get; set; }
    public float Opacity { get; set; } = 1f;
    public const float MinScale = .5f, MaxScale = 2.5f, MinOpacity = .2f;
    public DmElementLayout Copy() => (DmElementLayout)MemberwiseClone();
    public DmElementLayout Normalize() {
        X = float.IsFinite(X) ? Math.Clamp(X, 0, 1) : .5f; Y = float.IsFinite(Y) ? Math.Clamp(Y, 0, 1) : .5f;
        Scale = float.IsFinite(Scale) ? Math.Clamp(Scale, MinScale, MaxScale) : 1f;
        Rotation = float.IsFinite(Rotation) ? MathF.IEEERemainder(Rotation, 360f) : 0f;
        Opacity = float.IsFinite(Opacity) ? Math.Clamp(Opacity, MinOpacity, 1f) : 1f;
        return this;
    }
    public float Radians => Rotation * (MathF.PI / 180f);
    /// <summary>The element's turned and scaled outline, as the axis-aligned box it needs.</summary>
    public Vector2 Extent(Vector2 size) { float c = MathF.Abs(MathF.Cos(Radians)), s = MathF.Abs(MathF.Sin(Radians)); return new Vector2(size.X * c + size.Y * s, size.X * s + size.Y * c) * Scale; }
    /// <summary>The top-left corner of the unturned, unscaled element for this layout in an area of this size, kept so
    /// that its turned and scaled outline stays on screen (and inside the safe margin).</summary>
    public Vector2 Corner(Vector2 area, Vector2 size, float margin = 0) {
        Vector2 half = Vector2.Min(Extent(size) / 2 + new Vector2(margin), area / 2);
        return new Vector2(Math.Clamp(X * area.X, half.X, area.X - half.X), Math.Clamp(Y * area.Y, half.Y, area.Y - half.Y)) - size / 2;
    }
    /// <summary>Scale and turn about the element's own centre: used for drawing, clipping and hit testing alike (the
    /// engine tests a touch against the same transform it draws with).</summary>
    public Matrix Transform(Vector2 size) => Matrix.CreateTranslation(-size.X / 2, -size.Y / 2, 0) * Matrix.CreateScale(Scale) * Matrix.CreateRotationZ(Radians) * Matrix.CreateTranslation(size.X / 2, size.Y / 2, 0);
    /// <summary>Whether a point of the area lies on the turned, scaled element (the editor's own test; the same maths).</summary>
    public bool Contains(Vector2 point, Vector2 area, Vector2 size, float margin = 0) {
        Vector2 centre = Corner(area, size, margin) + size / 2, d = point - centre;
        float c = MathF.Cos(-Radians), s = MathF.Sin(-Radians);
        Vector2 local = new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c) / Scale;
        return MathF.Abs(local.X) <= size.X / 2 && MathF.Abs(local.Y) <= size.Y / 2;
    }
}

/// <summary>The deathmatch HUD's elements by stable id (the id is what the settings file stores).</summary>
public static class DmHudIds {
    public const string Buy = "buy", Board = "board", Menu = "menu", Timer = "timer", Protect = "protect", Feed = "feed", Death = "death", Scores = "scores", Edit = "edit";
    /// <summary>Round 3 took out the equipment list ("equip") and the health and armour readout ("vitals"): the weapons are
    /// on the game's hotbar and the CS core's gun HUD, the health on the game's bar, the armour on the CS armour HUD (the
    /// user: "用核心CS的枪械HUD就可以了，不用新加"; "护甲现在就用核心CS自己的护甲HUD吧"; "血量显示用原版的就好了"). A saved
    /// layout for those ids is not read.</summary>
    public static readonly string[] All = [Buy, Board, Menu, Timer, Protect, Feed, Death, Scores, Edit];
    public static string Label(string id) => id switch {
        Buy => "配装按钮", Board => "榜单按钮", Menu => "竞技菜单按钮", Timer => "比赛时间", Protect => "复活保护提示",
        Feed => "击杀播报", Death => "死亡面板", Scores => "计分板", Edit => "地图编辑面板（房主）", _ => id };
    /// <summary>The element's size at scale 1, in the GUI's logical units.</summary>
    public static Vector2 Size(string id) => id switch {
        Buy or Board or Menu => new Vector2(104, 60), Timer => new Vector2(180, 56),
        Protect => new Vector2(260, 40), Feed => new Vector2(470, 186), Death => new Vector2(460, 130), Scores => new Vector2(560, 320), Edit => new Vector2(400, 150), _ => new Vector2(100, 40) };
    /// <summary>The smallest scale at which a button is still a reliable touch target (48 units on its short side).</summary>
    public static float MinScale(string id) => id is Buy or Board or Menu ? Math.Max(DmElementLayout.MinScale, 48f / 60f) : DmElementLayout.MinScale;
    public static bool IsButton(string id) => id is Buy or Board or Menu;
    /// <summary>The controls area the touch defaults were measured on: the user's phone (2400x1080 px, round 3).</summary>
    public static readonly Vector2 ReferenceArea = new(1183, 532);
    /// <summary>The width of the game's own touch button columns at the sides, in GUI units (64-unit buttons).</summary>
    public const float SideColumn = 64;
    /// <summary>The default layout: one set for a keyboard and mouse, one for a touch screen (the buttons are shown on
    /// touch and hidden on a desktop, where the keys open the same things).</summary>
    /// <remarks>Touch, round 3 (seen on the user's phone, 2400x1080, about 1183x532 GUI units): the game's own touch
    /// controls take the left column (back, player, inventory), the right column (menu, crouch, jump, chat), the move pad
    /// at the bottom left and the hotbar (with the health bars above it) at the bottom middle; the multiplayer platform
    /// puts its join code at the top right and, with two or more players, a list of their names at the top left beside
    /// the left column, one row per player; the CS weapon buttons stand by default in a column at three quarters of the
    /// width (and the agent voice button above it), and the CS ammo display keeps the lower right corner. All of these
    /// have fixed sizes in GUI units, so the touch defaults are measured from them for the screen at hand (a 16:9 phone is
    /// about 946 units wide): the three buttons in a row between the timer and the voice button where they fit, otherwise
    /// in the strip between the weapon buttons and the right-hand column; the feed and the host's map panel between the
    /// player list and those; the board between the buttons and the bars. The version 1 defaults put the buy button on the timer, the menu button on the join code, the map
    /// panel and the feed over the left column, the equipment over the right column and the health over the hotbar; the
    /// first test builds of version 2 put the three buttons on the player list and the equipment on the weapon buttons.</remarks>
    public static DmElementLayout Default(string id, bool touch, Vector2? area = null) {
        Vector2 a = area is { X: > 1, Y: > 1 } given ? given : ReferenceArea; float w = a.X, h = a.Y;
        DmElementLayout At(float x, float y, float scale = 1) => new() { X = x / w, Y = y / h, Scale = scale };
        if (!touch) return id switch {
            Buy => new() { X = .93f, Y = .30f, Visible = false, Scale = .9f },
            Board => new() { X = .93f, Y = .40f, Visible = false, Scale = .9f },
            Menu => new() { X = .93f, Y = .50f, Visible = false, Scale = .9f },
            Timer => new() { X = .5f, Y = .05f },
            Protect => new() { X = .5f, Y = .22f },
            // the feed at the right below the join code, the host's map panel at the left below the player list (round 3)
            Feed => At(w - DmElement.Margin_ - Size(Feed).X * .9f / 2, 41 + Size(Feed).Y * .9f / 2, .9f),
            Death => new() { X = .5f, Y = .68f },
            Scores => new() { X = .5f, Y = .46f },
            Edit => At(DmElement.Margin_ + Size(Edit).X * .9f / 2, 172 + Size(Edit).Y * .9f / 2, .9f),
            _ => new() };
        const float gap = 6, top = 64, buttonScale = .8f, names = 240;    // below the timer and the join code; right of the player list
        var z = Zones(a);
        Vector2 button = Size(Buy) * buttonScale;
        float rightInner = w - SideColumn - gap, timerRight = w / 2 + Size(Timer).X / 2;
        bool row = z.Voice.Min.X - gap - (timerRight + gap) >= 3 * button.X + 2 * gap;
        float stripTop = z.Voice.Max.Y + gap;
        Vector2 Button(int i) => row ? new Vector2(z.Voice.Min.X - gap - (2 - i) * (button.X + gap) - button.X / 2, top + button.Y / 2)
                                     : new Vector2(rightInner - button.X / 2, stripTop + i * (button.Y + gap) + button.Y / 2);
        float upperRight = (row ? Button(0).X - button.X / 2 : z.Weapons.Min.X) - gap;
        float feedScale = Math.Clamp((upperRight - names) / Size(Feed).X, .5f, .8f), editScale = Math.Clamp((upperRight - names) / Size(Edit).X, .6f, 1f);
        float boardTop = row ? top + button.Y + gap : top, boardBottom = h - BottomBars - gap;
        // centred, the board also stays inside the CS weapon buttons' column where that leaves it three quarters of its size
        float beside = (2 * (z.Weapons.Min.X - gap) - w) / Size(Scores).X;
        float boardScale = Math.Clamp(Math.Min(Math.Min((boardBottom - boardTop) / Size(Scores).Y, (w - 2 * names) / Size(Scores).X), beside >= .75f ? beside : 1), .6f, 1f);
        return id switch {
            Buy => At(Button(0).X, Button(0).Y, buttonScale),
            Board => At(Button(1).X, Button(1).Y, buttonScale),
            Menu => At(Button(2).X, Button(2).Y, buttonScale),
            Feed => At(names + Size(Feed).X * feedScale / 2, top + Size(Feed).Y * feedScale / 2, feedScale),
            Edit => At(names + Size(Edit).X * editScale / 2, top + Size(Edit).Y * editScale / 2, editScale),
            Timer => At(w / 2, 32),
            Protect => At(w / 2, .45f * h),
            Death => At(w / 2, .68f * h),
            Scores => At(w / 2, boardTop + Size(Scores).Y * boardScale / 2, boardScale),
            _ => new() };
    }
    /// <summary>The hotbar with the health bars above it, from the bottom.</summary>
    public const float BottomBars = 96;
    public readonly record struct Rect(Vector2 Min, Vector2 Max) {
        public bool Overlaps(Rect o) => Min.X < o.Max.X && o.Min.X < Max.X && Min.Y < o.Max.Y && o.Min.Y < Max.Y;
    }
    /// <summary>The CS core's own touch HUD at its default places on a screen of this size (ScGunFunctions.Default,
    /// right-handed: a 104x60 column centred at 0.7506 of the width from row 2 at 0.339 to row 0 at 0.6235 of the height;
    /// the agent voice button at 0.85, 0.18, scaled 0.8; the ammo display's 116x76 box 12 units in from the lower right).</summary>
    public static (Rect Weapons, Rect Voice, Rect Ammo) Zones(Vector2 area) {
        float w = area.X, h = area.Y;
        return (new Rect(new Vector2(.7506f * w - 52, .339f * h - 30), new Vector2(.7506f * w + 52, .6235f * h + 30)),
            new Rect(new Vector2(.85f * w - 41.6f, .18f * h - 24), new Vector2(.85f * w + 41.6f, .18f * h + 24)),
            new Rect(new Vector2(w - 128, h - 88), new Vector2(w - 12, h - 12)));
    }
    /// <summary>The touch defaults of settings version 1. A touch layout saved by version 1 that still equals its default
    /// was never placed by the player (the editor saves every element): it takes the new default. So does one within 2%
    /// of it and otherwise unchanged: the version 1 editor moved an element a few units when it was only tapped to select
    /// it (round 3: the user's board was 9 units off its default, and on its old place it lay over the new buttons). One
    /// the player moved further, scaled, turned, faded or hid is kept.</summary>
    public static DmElementLayout DefaultV1Touch(string id) => id switch {
        Buy => new() { X = .60f, Y = .07f, Scale = .9f }, Board => new() { X = .72f, Y = .07f, Scale = .9f }, Menu => new() { X = .84f, Y = .07f, Scale = .9f },
        Timer => new() { X = .5f, Y = .05f }, Protect => new() { X = .5f, Y = .22f },
        Feed => new() { X = .2f, Y = .2f, Scale = .9f }, Death => new() { X = .5f, Y = .68f }, Scores => new() { X = .5f, Y = .46f }, Edit => new() { X = .2f, Y = .34f },
        _ => new() };
    public static bool Same(DmElementLayout a, DmElementLayout b, float slack = 1e-3f) => a.Visible == b.Visible && MathF.Abs(a.X - b.X) < slack && MathF.Abs(a.Y - b.Y) < slack
        && MathF.Abs(a.Scale - b.Scale) < 1e-3f && MathF.Abs(a.Rotation - b.Rotation) < 1e-2f && MathF.Abs(a.Opacity - b.Opacity) < 1e-3f;
}

/// <summary>The deathmatch's settings of this device: the HUD layouts (one set for keyboard and mouse, one for touch), the
/// three keys, and whether the scoreboard key is held or toggles. Its own file beside the core's: never in a world,
/// never sent to anybody, and a file this build cannot read is left as it is while the session runs on defaults.</summary>
public static class DmUiSettings {
    /// <summary>2: the touch defaults of round 3 (a version 1 file is read and its untouched touch layouts take them).
    /// Version 2 was never delivered before its defaults moved from the player list (round 3, phone test): no file of it
    /// carries the earlier ones.</summary>
    public const int Version = 2;
    public const string KeyBuy = "buy", KeyBoard = "board", KeyMenu = "menu";
    public static string Path => ScLocalSettings.PathFor("ScCsgoDeathmatchUi.json");
    public static readonly Dictionary<string, DmElementLayout> Desktop = new(StringComparer.Ordinal), Touch = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal);
    /// <summary>Desktop: the scoreboard shows while its key is held (false: the key switches it on and off).</summary>
    public static bool BoardHold = true;
    public static bool Writable { get; private set; } = true;
    static bool s_loaded;

    public static string DefaultKey(string id) => id switch { KeyBuy => "B", KeyBoard => "Tab", KeyMenu => "F6", _ => "" };
    public static string KeyOf(string id) => Keys.GetValueOrDefault(id, DefaultKey(id));
    public static string KeyLabel(string id) => id switch { KeyBuy => "打开配装轮盘", KeyBoard => "计分板", KeyMenu => "竞技菜单", _ => id };
    public static readonly string[] KeyIds = [KeyBuy, KeyBoard, KeyMenu];
    /// <summary>The layout the player saved, or the default for a controls area of this size. A default is not stored
    /// as if it had been placed: it follows the screen (touch defaults are measured from its edges).</summary>
    public static DmElementLayout Layout(string id, bool touch, Vector2 area = default) {
        Load();
        if ((touch ? Touch : Desktop).TryGetValue(id, out var saved)) return saved;
        if (!s_defaults.TryGetValue((id, touch), out var known) || known.Area != area) s_defaults[(id, touch)] = known = (area, DmHudIds.Default(id, touch, area));
        return known.Layout;
    }
    static readonly Dictionary<(string, bool), (Vector2 Area, DmElementLayout Layout)> s_defaults = [];
    public static void Reset(string id, bool touch) { Load(); (touch ? Touch : Desktop).Remove(id); }
    public static void ResetAll(bool touch) { foreach (string id in DmHudIds.All) Reset(id, touch); }

    sealed class File {
        public int Version { get; set; }
        public bool BoardHold { get; set; } = true;
        public Dictionary<string, string> Keys { get; set; }
        public Dictionary<string, DmElementLayout> Desktop { get; set; }
        public Dictionary<string, DmElementLayout> Touch { get; set; }
    }
    static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    /// <summary>The settings as text, and back: the file's own format (checked offline without a storage).</summary>
    public static string Encode() => JsonSerializer.Serialize(new File { Version = Version, BoardHold = BoardHold, Keys = new(Keys), Desktop = new(Desktop), Touch = new(Touch) }, s_json);
    public static bool Decode(string text) {
        File file;
        try { file = JsonSerializer.Deserialize<File>(text, s_json); } catch (JsonException) { return false; }
        if (file is null || file.Version is not (1 or Version)) return false;
        Desktop.Clear(); Touch.Clear(); Keys.Clear();
        foreach (var (target, source) in new[] { (Desktop, file.Desktop), (Touch, file.Touch) })
            foreach (var pair in source ?? []) if (pair.Value is not null && DmHudIds.All.Contains(pair.Key)) {
                var layout = pair.Value.Normalize();
                if (file.Version == 1 && ReferenceEquals(target, Touch) && DmHudIds.Same(layout, DmHudIds.DefaultV1Touch(pair.Key), .02f)) continue;   // never placed (or only tapped): the new default
                target[pair.Key] = layout; layout.Scale = Math.Max(layout.Scale, DmHudIds.MinScale(pair.Key));
            }
        foreach (var pair in file.Keys ?? []) if (KeyIds.Contains(pair.Key) && ScGunBindings.Valid(pair.Value ?? "")) Keys[pair.Key] = pair.Value ?? "";
        BoardHold = file.BoardHold;
        return true;
    }
    public static void Load() {
        if (s_loaded) return;
        s_loaded = true;
        try {
            if (!Storage.FileExists(Path)) return;
            using var stream = Storage.OpenFile(Path, OpenFileMode.Read); using var reader = new StreamReader(stream);
            if (!Decode(reader.ReadToEnd())) { Writable = false; KnifeLog.Warning("[CS_DM] the HUD settings file is not this version's: left as it is, defaults in use"); }
        }
        catch (Exception e) { Writable = false; KnifeLog.Warning("[CS_DM] the HUD settings could not be read (left as they are): " + e.Message); }
    }
    public static void Save() {
        Load();
        if (!Writable) return;
        try {
            using var stream = Storage.OpenFile(Path, OpenFileMode.Create); using var writer = new StreamWriter(stream);
            writer.Write(Encode());
        }
        catch (Exception e) { KnifeLog.Warning("[CS_DM] the HUD settings could not be saved: " + e.Message); }
    }
    /// <summary>Other functions that already use this key on this device: the deathmatch's own, the CS weapon keys and the
    /// game's own keyboard mapping. Shown to the player; the key can still be chosen (nothing is taken from anybody).</summary>
    public static List<string> Conflicts(string id, string key) {
        var found = new List<string>();
        if (string.IsNullOrEmpty(key)) return found;
        foreach (string other in KeyIds) if (other != id && KeyOf(other) == key) found.Add(KeyLabel(other));
        foreach (string function in ScGunFunctions.All) if (ScGunBindings.Get(function) == key) found.Add("CS武器：" + ScGunBindings.Label(function));
        try {
            if (SettingsManager.KeyboardMappingSettings is { } native)
                foreach (var pair in native) if (pair.Value is Engine.Input.Key k && k.ToString() == key) found.Add("游戏：" + pair.Key);
        }
        catch (Exception) { /* the game's own mapping is not readable here: nothing more to report */ }
        if (key == "Tab" && PlatformChatOnTab) found.Add("联机平台：聊天窗口（按一次开、再按一次关）");
        return found;
    }
    /// <summary>The 1.9.3.2 multiplayer platform's chat panel toggles on Tab, a key outside the game's own mapping
    /// (round 2, MP r2: holding Tab for the board also opened the chat). Reported as a conflict; nothing of the platform's
    /// is changed.</summary>
    public static bool PlatformChatOnTab => s_chat ??= AppDomain.CurrentDomain.GetAssemblies().Any(a => { try { return a.GetType("Survivalcraft.Multiplayer.ChatWidget", false) is not null; } catch (Exception) { return false; } });
    static bool? s_chat;
}
