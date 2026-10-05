// The HUD's own arithmetic (DM-07, DM-10): where a placed element is, whether a touch is on it after scaling and turning,
// which wheel sector a point is in, what the settings file keeps. No widget is created: what the engine draws is the
// user's to look at.
using Engine;
using Game;

static partial class Program {
    static void Ui() {
        Vector2 area = new(1280, 720); Vector2 size = DmHudIds.Size(DmHudIds.Buy);            // 104 x 60
        var plain = new DmElementLayout { X = .5f, Y = .5f };
        Test("U01", "an unturned element is hit inside its rectangle and nowhere else", plain.Contains(new Vector2(640, 360), area, size) && plain.Contains(new Vector2(640 + 51, 360 + 29), area, size) && !plain.Contains(new Vector2(640 + 53, 360), area, size) && !plain.Contains(new Vector2(640, 360 + 31), area, size));
        var turned = new DmElementLayout { X = .5f, Y = .5f, Rotation = 90 };
        // turned a quarter: the 104 x 60 button now stands 60 wide and 104 high
        Test("U02", "a button turned a quarter is pressed where it is seen: along its new long side, not its old one", turned.Contains(new Vector2(640, 360 + 50), area, size) && !turned.Contains(new Vector2(640 + 50, 360), area, size) && turned.Contains(new Vector2(640 + 29, 360), area, size) && !turned.Contains(new Vector2(640 + 31, 360), area, size));
        var big = new DmElementLayout { X = .5f, Y = .5f, Scale = 2 };
        Test("U03", "a doubled element is hit across its doubled size", big.Contains(new Vector2(640 + 100, 360 + 58), area, size) && !big.Contains(new Vector2(640 + 105, 360), area, size));
        var slanted = new DmElementLayout { X = .5f, Y = .5f, Rotation = 45, Scale = 1.5f };
        // a point 70 along the turned long axis (104 * 1.5 / 2 = 78 half length): inside; the same distance across it (half width 45): outside
        Vector2 along = new(MathF.Cos(MathF.PI / 4), MathF.Sin(MathF.PI / 4)), across = new(-along.Y, along.X);
        Test("U04", "turned 45 degrees and scaled: hit along the long axis up to its end, not beyond the short side", slanted.Contains(new Vector2(640, 360) + along * 70, area, size) && !slanted.Contains(new Vector2(640, 360) + along * 80, area, size) && slanted.Contains(new Vector2(640, 360) + across * 40, area, size) && !slanted.Contains(new Vector2(640, 360) + across * 50, area, size));
        // the same element through the transform the engine draws with: the corners of the drawn quad are the hit region's corners
        var transform = slanted.Transform(size); Vector2 corner = slanted.Corner(area, size);
        Vector2 Drawn(Vector2 local) => corner + Vector2.Transform(local, transform);
        bool cornersHit = new[] { new Vector2(1, 1), new Vector2(size.X - 1, 1), new Vector2(size.X - 1, size.Y - 1), new Vector2(1, size.Y - 1) }.All(c => slanted.Contains(Drawn(c), area, size));
        bool outsideMissed = new[] { new Vector2(-2, -2), new Vector2(size.X + 2, -2), new Vector2(size.X + 2, size.Y + 2), new Vector2(-2, size.Y + 2) }.All(c => !slanted.Contains(Drawn(c), area, size));
        Test("U05", "drawing and hit testing use one transform: the drawn corners are inside, a hair outside them is not", cornersHit && outsideMissed);
        bool onScreen = true;
        foreach (string id in DmHudIds.All) foreach (bool touch in new[] { false, true }) foreach (var screen in new[] { new Vector2(1280, 720), new Vector2(2400, 1080), new Vector2(800, 600), new Vector2(720, 1280) }) {
            var layout = DmHudIds.Default(id, touch).Normalize(); Vector2 s = DmHudIds.Size(id), c = layout.Corner(screen, s, 4) + s / 2, e = Vector2.Min(layout.Extent(s) / 2, screen / 2);
            onScreen &= c.X - e.X >= -.01f && c.Y - e.Y >= -.01f && c.X + e.X <= screen.X + .01f && c.Y + e.Y <= screen.Y + .01f;
        }
        Test("U06", "every default element is wholly on screen on a desktop, a wide phone, a small window and a portrait screen", onScreen);
        var off = new DmElementLayout { X = 5, Y = -3, Scale = 99, Rotation = 725, Opacity = 0 }.Normalize();
        Vector2 kept = off.Corner(area, size, 4) + size / 2, extent = off.Extent(size) / 2;
        Test("U07", "values from a damaged file are brought back into range and the element stays reachable on screen", off.X == 1 && off.Y == 0 && off.Scale == DmElementLayout.MaxScale && MathF.Abs(off.Rotation - 5) < .01f && off.Opacity == DmElementLayout.MinOpacity
            && kept.X + extent.X <= area.X + .01f && kept.Y - extent.Y >= -.01f, $"{off.X} {off.Y} {off.Scale} {off.Rotation} {off.Opacity} centre {kept}");
        Test("U08", "a button cannot be made smaller than a 48-unit touch target", DmHudIds.MinScale(DmHudIds.Buy) * 60 >= 48 - .01f && DmHudIds.MinScale(DmHudIds.Feed) == DmElementLayout.MinScale);
        Test("U09", "touch and desktop have their own defaults: the three buttons are shown on touch and hidden on a desktop (the keys open the same things)",
            new[] { DmHudIds.Buy, DmHudIds.Board, DmHudIds.Menu }.All(id => DmHudIds.Default(id, true).Visible && !DmHudIds.Default(id, false).Visible) && DmHudIds.Default(DmHudIds.Feed, true).Visible && DmHudIds.Default(DmHudIds.Feed, false).Visible);
        // round 3: the equipment list and the health/armour readout are gone (the hotbar and the CS core's gun HUD, the
        // game's health bar, the CS armour HUD - the user's decisions); a saved layout for them is not read
        Test("U10", "nine elements (no equipment list, no health/armour readout since round 3), each with a name, a size and the full set of settings (place, size, turn, opacity, shown)", DmHudIds.All.Length == 9 && DmHudIds.All.Distinct().Count() == 9 && !DmHudIds.All.Contains("equip") && !DmHudIds.All.Contains("vitals") && DmHudIds.All.All(id => DmHudIds.Label(id) != id && DmHudIds.Size(id).X > 0)
            && typeof(DmElementLayout).GetProperties().Select(p => p.Name).Intersect(["X", "Y", "Scale", "Rotation", "Opacity", "Visible"]).Count() == 6);

        // ---- the settings file
        DmUiSettings.Desktop[DmHudIds.Feed] = new DmElementLayout { X = .2f, Y = .3f, Scale = 1.4f, Rotation = -30, Opacity = .6f, Visible = false };
        DmUiSettings.Touch[DmHudIds.Buy] = new DmElementLayout { X = .9f, Y = .1f, Scale = .6f };
        DmUiSettings.Keys[DmUiSettings.KeyBuy] = "N"; DmUiSettings.BoardHold = false;
        string text = DmUiSettings.Encode();
        DmUiSettings.Desktop.Clear(); DmUiSettings.Touch.Clear(); DmUiSettings.Keys.Clear(); DmUiSettings.BoardHold = true;
        bool decoded = DmUiSettings.Decode(text);
        var equip = DmUiSettings.Desktop.GetValueOrDefault(DmHudIds.Feed); var buy = DmUiSettings.Touch.GetValueOrDefault(DmHudIds.Buy);
        Test("U11", "the settings survive their own file: both device sets, the keys, the scoreboard mode", decoded && equip is { X: .2f, Y: .3f, Scale: 1.4f, Rotation: -30, Opacity: .6f, Visible: false } && DmUiSettings.KeyOf(DmUiSettings.KeyBuy) == "N" && !DmUiSettings.BoardHold && !DmUiSettings.Desktop.ContainsKey(DmHudIds.Buy));
        Test("U12", "a touch button's saved size is raised to the smallest reliable target", buy is not null && buy.Scale >= DmHudIds.MinScale(DmHudIds.Buy) - .001f, $"{buy?.Scale}");
        Test("U13", "a file of another version, a broken file or an unknown key is not taken over", !DmUiSettings.Decode(text.Replace("\"Version\": 2", "\"Version\": 3")) && !DmUiSettings.Decode("{broken") && DmUiSettings.Decode("{\"Version\":1,\"Keys\":{\"buy\":\"NotAKey\",\"other\":\"B\"}}") && DmUiSettings.KeyOf(DmUiSettings.KeyBuy) == "B" && DmUiSettings.Keys.Count == 0);
        // The user's own file (phone, 2026-10-03, settings version 1): every touch element at its old default except the
        // scoreboard, 9 units off it (a tap that selected it in the old editor). All take the new defaults; an element
        // dragged further (the equipment, added here) stays.
        const string phoneV1 = "{\"Version\":1,\"BoardHold\":true,\"Keys\":{},\"Desktop\":{},\"Touch\":{"
            + "\"buy\":{\"Visible\":true,\"X\":0.6,\"Y\":0.07,\"Scale\":0.9,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},\"board\":{\"Visible\":true,\"X\":0.72,\"Y\":0.07,\"Scale\":0.9,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},"
            + "\"menu\":{\"Visible\":true,\"X\":0.84,\"Y\":0.07,\"Scale\":0.9,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},\"timer\":{\"Visible\":true,\"X\":0.5,\"Y\":0.05,\"Scale\":1,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},"
            + "\"vitals\":{\"Visible\":true,\"X\":0.5,\"Y\":0.93,\"Scale\":1,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},\"protect\":{\"Visible\":true,\"X\":0.5,\"Y\":0.22,\"Scale\":1,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},"
            + "\"feed\":{\"Visible\":true,\"X\":0.2,\"Y\":0.2,\"Scale\":0.9,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},\"death\":{\"Visible\":true,\"X\":0.5,\"Y\":0.68,\"Scale\":1,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},"
            + "\"scores\":{\"Visible\":true,\"X\":0.5075,\"Y\":0.4775926,\"Scale\":1,\"Rotation\":0,\"Opacity\":1,\"Radians\":0},\"equip\":{\"Visible\":true,\"X\":0.945,\"Y\":0.44,\"Scale\":1,\"Rotation\":0,\"Opacity\":1,\"Radians\":0}}}";
        string phoneV1Dragged = phoneV1.Replace("\"feed\":{\"Visible\":true,\"X\":0.2", "\"feed\":{\"Visible\":true,\"X\":0.7");
        bool migrated = DmUiSettings.Decode(phoneV1); int leftPlaced = DmUiSettings.Touch.Count;
        bool menuDefault = DmUiSettings.Layout(DmHudIds.Menu, true).X == DmHudIds.Default(DmHudIds.Menu, true).X, written = DmUiSettings.Encode().Contains("\"Version\": 2");
        bool draggedKept = DmUiSettings.Decode(phoneV1Dragged) && DmUiSettings.Touch.Count == 1 && DmUiSettings.Touch.TryGetValue(DmHudIds.Feed, out var dragged) && MathF.Abs(dragged.X - .7f) < 1e-4f;
        Test("U13b", "a version 1 file: the touch layouts never placed (or only nudged by a selecting tap) take the new defaults, one the player dragged stays, and it is written back as version 2",
            migrated && leftPlaced == 0 && menuDefault && written && draggedKept, $"kept {leftPlaced}, dragged {draggedKept}");
        // The touch defaults keep clear of the game's own touch controls, the CS core's own touch HUD at its default
        // places, and of each other (round 3, the user's phone: 2400x1080 px, about 1183x532 GUI units; and a 16:9 phone,
        // about 946 units wide). Rectangles in GUI units: left column, right column, move pad, hotbar with the health bars
        // above it, the multiplayer join code, the platform's player list beside the left column (six players), the CS
        // weapon buttons' column, the agent voice button, the CS ammo display, the CS armour display above the move pad. The death panel is checked against the game's
        // controls only: under the death camera the game hides them and the CS buttons with them. The board is an overlay
        // the player opens: on a narrow screen it may lie under the CS buttons, which are drawn above the HUD and keep
        // their touches.
        foreach (var screen in new[] { DmHudIds.ReferenceArea, new Vector2(946, 532) }) {
            float w = screen.X, h = screen.Y; bool reference = screen == DmHudIds.ReferenceArea;
            var z = DmHudIds.Zones(screen);
            (Vector2 Min, Vector2 Max)[] game = [(new(0, 0), new(64, 210)), (new(w - 64, 0), new(w, 280)), (new(0, h - 224), new(222, h)), (new(w / 2 - 360, h - 96), new(w / 2 + 360, h)),
                (new(w - 202, 10), new(w - 81, 35)), (new(74, 10), new(234, 166))];
            (Vector2 Min, Vector2 Max)[] core = [(z.Weapons.Min, z.Weapons.Max), (z.Voice.Min, z.Voice.Max), (z.Ammo.Min, z.Ammo.Max), (new(12, h - 224 - 6 - 40), new(240, h - 224 - 6))];
            (Vector2 Min, Vector2 Max) Rect(string id) {
                var l = DmHudIds.Default(id, true, screen); var size = DmHudIds.Size(id); var c = l.Corner(screen, size, DmElement.Margin_) + size / 2; var half = l.Extent(size) / 2;
                var r = (c - half, c + half);
                // the equipment's rows are drawn from its top and only they take touches: five held items (primary,
                // secondary, knife, Zeus, one throwable) must stay clear; seven do on the reference phone
                return r;
            }
            bool Overlap((Vector2 Min, Vector2 Max) a, (Vector2 Min, Vector2 Max) b) => a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y;
            string[][] together = [[DmHudIds.Buy, DmHudIds.Board, DmHudIds.Menu, DmHudIds.Timer, DmHudIds.Edit], [DmHudIds.Buy, DmHudIds.Board, DmHudIds.Menu, DmHudIds.Timer, DmHudIds.Feed, DmHudIds.Protect],
                [DmHudIds.Buy, DmHudIds.Board, DmHudIds.Menu, DmHudIds.Timer, DmHudIds.Feed, DmHudIds.Death], [DmHudIds.Buy, DmHudIds.Board, DmHudIds.Menu, DmHudIds.Timer, DmHudIds.Scores]];
            var clashes = new List<string>();
            foreach (string id in DmHudIds.All) {
                for (int g = 0; g < game.Length; g++) if (Overlap(Rect(id), game[g])) clashes.Add($"{id}/game{g}");
                if (id != DmHudIds.Death && (id != DmHudIds.Scores || reference)) for (int k = 0; k < core.Length; k++) if (Overlap(Rect(id), core[k])) clashes.Add($"{id}/core{k}");
            }
            foreach (var set in together) for (int i = 0; i < set.Length; i++) for (int j = i + 1; j < set.Length; j++) if (Overlap(Rect(set[i]), Rect(set[j]))) clashes.Add($"{set[i]}/{set[j]}");
            Test(reference ? "U13c" : "U13d", $"touch defaults on a {w:0}x{h:0} screen: no element over the game's own touch controls, the join code, the player list or the CS weapon buttons, voice button and ammo display at their default places, none over another shown at the same time (the board above the health)", clashes.Count == 0, string.Join(" ", clashes.Distinct()));
        }
        // Desktop (round 3, the user: "电脑手机的UI都要检查是否阻挡了原版的东西"): the shown elements (the three buttons are
        // hidden there) clear of the hotbar with its bars, the platform's player list and join code, the CS armour display
        // above the hotbar at the left and the CS ammo display at the lower right; and of each other when shown together.
        foreach (var desk in new[] { new Vector2(1181, 635), new Vector2(1280, 720) }) {
            float w = desk.X, h = desk.Y;
            (Vector2 Min, Vector2 Max)[] zones = [(new(w / 2 - 360, h - 96), new(w / 2 + 360, h)), (new(74, 10), new(234, 166)), (new(w - 202, 10), new(w - 81, 35)),
                (new(12, h - 120), new(240, h - 80)), (new(w - 128, h - 88), new(w - 12, h - 12))];
            (Vector2 Min, Vector2 Max) Rect(string id) {
                var l = DmHudIds.Default(id, false, desk); var size = DmHudIds.Size(id); var c = l.Corner(desk, size, DmElement.Margin_) + size / 2; var half = l.Extent(size) / 2;
                var r = (c - half, c + half);
                return r;
            }
            bool Overlap((Vector2 Min, Vector2 Max) a, (Vector2 Min, Vector2 Max) b) => a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y;
            var shown = DmHudIds.All.Where(id => DmHudIds.Default(id, false, desk).Visible).ToArray();
            string[][] together = [[DmHudIds.Timer, DmHudIds.Edit], [DmHudIds.Timer, DmHudIds.Feed, DmHudIds.Protect], [DmHudIds.Timer, DmHudIds.Feed, DmHudIds.Death], [DmHudIds.Timer, DmHudIds.Scores]];
            var clashes = new List<string>();
            foreach (string id in shown) for (int z = 0; z < zones.Length; z++) if ((id != DmHudIds.Death || z == 0) && Overlap(Rect(id), zones[z])) clashes.Add($"{id}/zone{z}");
            foreach (var set in together) for (int i = 0; i < set.Length; i++) for (int j = i + 1; j < set.Length; j++) if (Overlap(Rect(set[i]), Rect(set[j]))) clashes.Add($"{set[i]}/{set[j]}");
            Test(desk.X < 1200 ? "U13h" : "U13i", $"desktop defaults on a {w:0}x{h:0} GUI: nothing over the hotbar and its bars, the platform's player list and join code, the CS armour and ammo displays; none over another shown at the same time",
                clashes.Count == 0 && !shown.Contains(DmHudIds.Buy), string.Join(" ", clashes.Distinct()));
        }
        {
            // a default follows the screen from its edges and is not stored as if the player had placed it
            DmUiSettings.Touch.Clear();
            float LeftGap(Vector2 screen) { var l = DmUiSettings.Layout(DmHudIds.Edit, true, screen); return l.X * screen.X - DmHudIds.Size(DmHudIds.Edit).X * l.Scale / 2; }
            float wide = LeftGap(DmHudIds.ReferenceArea), narrow = LeftGap(new Vector2(946, 532));
            Test("U13e", "a touch default keeps its distance from the screen's edge on a narrower phone (the map panel beside the player list) and is not written into the saved layouts",
                MathF.Abs(wide - 240) < .01f && MathF.Abs(narrow - 240) < .01f && DmUiSettings.Touch.Count == 0, $"{wide} {narrow}");
        }
        {
            // the buy wheel and the menu in the free part of the screen (round 3: the wheel's bottom row lay under the
            // hotbar's bars, which took the taps on 入场; its top right lay under the join code)
            var code = new DmFit.Box(new Vector2(981, 10), new Vector2(1102, 35)); var phone = DmHudIds.ReferenceArea;
            var issues = new List<string>();
            foreach (var (screen, panel, bottom, box, name) in new[] { (phone, new Vector2(850, 500), 436f, (DmFit.Box?)code, "wheel/phone"), (phone, new Vector2(780, 452), 436f, code, "menu/phone"),
                (new Vector2(946, 532), new Vector2(850, 500), 436f, new DmFit.Box(new Vector2(744, 10), new Vector2(865, 35)), "wheel/16:9"), (new Vector2(946, 532), new Vector2(780, 452), 458f, new DmFit.Box(new Vector2(744, 10), new Vector2(865, 35)), "menu/16:9") }) {
                var (s, at) = DmFit.Fit(screen, panel, 64, 64, bottom, box); var placed = new DmFit.Box(at, at + panel * s);
                if (placed.Min.X < 64 + DmFit.Gap - .01f || placed.Max.X > screen.X - 64 - DmFit.Gap + .01f || placed.Min.Y < -.01f || placed.Max.Y > bottom - DmFit.Gap + .01f) issues.Add($"{name} outside {placed}");
                if (box is { } b && placed.Overlaps(b)) issues.Add($"{name} under the join code");
                if (s < .75f || s > 1) issues.Add($"{name} scale {s}");
            }
            var (roomy, roomyCorner) = DmFit.Fit(new Vector2(1600, 900), new Vector2(850, 500), 64, 64, 826, null);
            Test("U13f", "the buy wheel and the menu are scaled into the free part of a phone screen (beside the side columns, above the hotbar's bars, clear of the join code) and never scaled up on a roomy screen",
                issues.Count == 0 && roomy == 1 && MathF.Abs(roomyCorner.X - (1600 - 850) / 2f) < .01f, string.Join("; ", issues));
        }
        Test("U14", "default keys: B buys, Tab shows the board, F6 opens the menu; two functions on one key are reported", DmUiSettings.DefaultKey(DmUiSettings.KeyBuy) == "B" && DmUiSettings.DefaultKey(DmUiSettings.KeyBoard) == "Tab" && DmUiSettings.DefaultKey(DmUiSettings.KeyMenu) == "F6"
            && DmUiSettings.Conflicts(DmUiSettings.KeyBuy, "Tab").Contains(DmUiSettings.KeyLabel(DmUiSettings.KeyBoard)) && DmUiSettings.Conflicts(DmUiSettings.KeyBuy, "R").Any(c => c.StartsWith("CS武器")) && DmUiSettings.Conflicts(DmUiSettings.KeyBuy, "B").Count == 0);

        // ---- the wheel
        Vector2 Polar(float degrees, float r) => new(MathF.Sin(degrees * MathF.PI / 180) * r, -MathF.Cos(degrees * MathF.PI / 180) * r);
        Test("U15", "six sectors: the first is centred at the top and the rest follow clockwise", DmWheelWidget.SectorAt(Polar(0, 130), 6) == 0 && DmWheelWidget.SectorAt(Polar(60, 130), 6) == 1 && DmWheelWidget.SectorAt(Polar(180, 130), 6) == 3 && DmWheelWidget.SectorAt(Polar(300, 130), 6) == 5
            && DmWheelWidget.SectorAt(Polar(29, 130), 6) == 0 && DmWheelWidget.SectorAt(Polar(31, 130), 6) == 1 && DmWheelWidget.SectorAt(Polar(-29, 130), 6) == 0);
        Test("U16", "the middle is the centre, beyond the rim is nothing", DmWheelWidget.SectorAt(Polar(77, 30), 6) == DmWheelWidget.Centre && DmWheelWidget.SectorAt(Polar(77, 230), 6) == DmWheelWidget.None && DmWheelWidget.SectorAt(Vector2.Zero, 6) == DmWheelWidget.Centre && DmWheelWidget.SectorAt(Polar(10, 130), 0) == DmWheelWidget.None);
        bool centres = Enumerable.Range(1, 8).All(n => Enumerable.Range(0, n).All(i => DmWheelWidget.SectorAt(DmWheelWidget.SectorCentre(i, n), n) == i));
        Test("U17", "with one to eight sectors each sector's own centre lies in that sector", centres);
        Test("U18", "six categories on the first ring; no ring ever shows more than eight entries", DmCatalogue.Groups.Length == 6 && DmWheelPanel.PerPage == 8 && DmWheelWidget.MaxSectors == 8);

        // ---- the slots a life's items are issued to (shown by the game's hotbar since round 3)
        Test("U20", "fixed places: primary, secondary, knife, Zeus, then one slot per throwable kind", DmSlots.Primary == 0 && DmSlots.Secondary == 1 && DmSlots.Knife == 2 && DmSlots.Zeus == 3 && DmSlots.Grenade(0) == 4 && DmSlots.Grenade(5) == 9 && DmSlots.Count == 10);

        // ---- the feed's words
        var kill = new DmKill { KillerKey = "a", KillerName = "甲", VictimKey = "b", VictimName = "乙", Weapon = "awp", Cause = DmDeathCause.Kill, Headshot = true, NoScope = true, ThroughSmoke = true };
        string line = DmFeedElement.Line(kill);
        Test("U21", "a feed line names killer, weapon, every established mark and the victim", line.StartsWith("甲") && line.EndsWith("乙") && line.Contains("AWP") && line.Contains("爆头") && line.Contains("盲狙") && line.Contains("穿烟") && !line.Contains("穿透") && !line.Contains("致盲"), line);
        Test("U22", "a kill without marks shows none; a death nobody caused names nobody", !DmFeedElement.Line(kill with { Headshot = false, NoScope = false, ThroughSmoke = false }).Contains("·") && DmFeedElement.Line(new DmKill { VictimName = "乙", Cause = DmDeathCause.OutOfBounds }) == "乙 离开了竞技区域");
        Test("U23", "every gun has a player-facing name (no asset names on screen)", GunSpec.All.All(g => DmNames.Weapon(g.Name) != g.Name), string.Join(",", GunSpec.All.Where(g => DmNames.Weapon(g.Name) == g.Name).Select(g => g.Name)));
        string summary = DmWheelPanel.Summary(new DmLoadout { Primary = new(V("ak47"), 0, true), Grenades = [1, 1] }, new DmRules { Grenades = true });
        Test("U24", "the loadout summary says what will be worn, the counter as a property of the gun", summary.Contains("AK-47") && summary.Contains("计数器") && summary.Contains("闪光弹×2") && summary.Contains("副武器：—"), summary.Replace("\n", " / "));
    }
}
