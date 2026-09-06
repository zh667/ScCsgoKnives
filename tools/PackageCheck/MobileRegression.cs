using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Input;
using Game;

static class MobileRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results = [];
        void Test(string name, Func<bool> test) { try { results.Add(new("mobile/" + name, test(), name)); } catch (Exception e) { results.Add(new("mobile/" + name, false, e.ToString())); } }
        var captureType = mod.GetType("Game.ScTouchCapture");
        object Capture() => Activator.CreateInstance(captureType);
        bool Flag(object c, string name) => (bool)captureType.GetProperty(name).GetValue(c);
        void Step(object c, bool enabled, params TouchLocation[] fingers) => captureType.GetMethod("Step").Invoke(c, [fingers, (Func<Vector2, bool>)(p => p.X < 100), enabled]);
        TouchLocation Finger(int id, TouchLocationState state, float x) => new() { Id = id, State = state, Position = new Vector2(x, 20) };
        var down = TouchLocationState.Pressed; var move = TouchLocationState.Moved; var up = TouchLocationState.Released;
        Test("second-finger-cannot-release-button", () => {
            var c = Capture(); Step(c, true, Finger(1, down, 20));
            Step(c, true, Finger(2, down, 300), Finger(1, move, 250));
            if (!Flag(c, "Pressed") || Flag(c, "Clicked")) return false;
            Step(c, true, Finger(2, up, 300), Finger(1, move, 250));
            return Flag(c, "Pressed") && !Flag(c, "Cancelled");
        });
        Test("owner-release-while-movement-still-held", () => {
            var c = Capture(); Step(c, true, Finger(1, down, 20));
            Step(c, true, Finger(2, move, 300), Finger(1, up, 250));
            return !Flag(c, "Pressed") && !Flag(c, "Cancelled") && !Flag(c, "Clicked");
        });
        Test("tap-fires-once", () => {
            var c = Capture(); Step(c, true, Finger(1, down, 20)); Step(c, true, Finger(1, up, 20));
            if (!Flag(c, "Clicked") || Flag(c, "Pressed")) return false;
            Step(c, true); return !Flag(c, "Clicked");
        });
        Test("moving-into-button-does-not-capture", () => {
            var c = Capture(); Step(c, true, Finger(1, down, 300)); Step(c, true, Finger(1, move, 20));
            return !Flag(c, "Pressed") && !Flag(c, "Clicked");
        });
        Test("lost-touch-cancels", () => {
            var c = Capture(); Step(c, true, Finger(1, down, 20)); Step(c, true, Finger(2, move, 300));
            return Flag(c, "Cancelled") && !Flag(c, "Pressed") && !Flag(c, "Clicked");
        });
        Test("menu-cancels-and-old-finger-cannot-recapture", () => {
            var c = Capture(); Step(c, true, Finger(1, down, 20)); Step(c, false, Finger(1, move, 20));
            if (!Flag(c, "Cancelled")) return false;
            Step(c, true, Finger(1, move, 20)); return !Flag(c, "Pressed") && !Flag(c, "Clicked");
        });
        Test("two-buttons-own-different-fingers", () => {
            var first = Capture(); var second = Capture();
            Step(first, true, Finger(1, down, 20)); Step(second, true, Finger(2, down, 20));
            Step(first, true, Finger(1, move, 250), Finger(2, up, 20));
            Step(second, true, Finger(1, move, 250), Finger(2, up, 20));
            return Flag(first, "Pressed") && !Flag(second, "Pressed") && Flag(second, "Clicked");
        });
        var timelineType = mod.GetType("Game.ScGrenadePreparation"); var cs2 = mod.GetType("Game.Cs2Rig");
        foreach (string asset in new[] { "hegrenade", "flashbang", "smokegrenade", "molotov", "incendiary", "decoy" }) foreach (bool low in new[] { false, true }) {
            Test($"multitouch-throw/{asset}/{low}", () => {
                string name = "grenade_" + asset, clip = low ? "throwLow" : "throwHigh";
                float Duration(string alias) => (float)cs2.GetMethod("Duration").Invoke(null, [name, alias]);
                float release = (float)cs2.GetMethod("GrenadeReleaseTime").Invoke(null, [name, clip]);
                var timeline = Activator.CreateInstance(timelineType, [0d, Duration("pullpin"), release, Duration(clip)]);
                void Advance(double now, object c) => timelineType.GetMethod("Step").Invoke(timeline, [now, Flag(c, "Pressed")]);
                var c = Capture(); Step(c, true, Finger(1, down, 20)); Advance(0, c);
                Step(c, true, Finger(2, down, 300), Finger(1, move, 250)); Advance(600, c);
                if ((bool)timelineType.GetProperty("Throwing").GetValue(timeline)) return false;
                Step(c, true, Finger(2, move, 300), Finger(1, up, 250)); Advance(601, c);
                return Math.Abs((double)timelineType.GetProperty("ReleaseAt").GetValue(timeline) - 601 - release) < .0001;
            });
        }
        var savedTypes = new Dictionary<Type, int>(BlocksManager.BlockTypeToIndex);
        var savedNames = new Dictionary<string, int>(BlocksManager.BlockNameToIndex);
        T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        try {
            string[] names = ["ScKnifeBlock", "ScGunBlock", "ScGrenadeBlock"];
            for (int i = 0; i < names.Length; i++) { BlocksManager.BlockTypeToIndex[mod.GetType("Game." + names[i])] = 700 + i; BlocksManager.BlockNameToIndex[names[i]] = 700 + i; }
            for (int kind = 0; kind < 3; kind++) {
                int contents = 700 + kind;
                Test("desktop-touchscreen-preserves-aim-route/" + names[kind], () => {
                    var player = Blank<ComponentPlayer>(); player.ComponentInput = Blank<ComponentInput>(); player.ComponentMiner = Blank<ComponentMiner>();
                    var inventory = new ComponentCreativeInventory { OpenSlotsCount = 10 }; inventory.m_slots.Add(Terrain.MakeBlockValue(contents));
                    player.ComponentMiner.Inventory = inventory; player.ComponentInput.IsControlledByTouch = true;
                    var ray = new Ray3(Vector3.Zero, -Vector3.UnitZ);
                    player.ComponentInput.m_playerInput = new PlayerInput { Dig = ray, Aim = ray };
                    player.m_aim = ray; player.m_aimStartTime = 1;
                    var loader = Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
                    object[] args = [player, false, false, 0f, false, false];
                    loader.GetType().GetMethod("UpdatePlayerInputAim").Invoke(loader, args);
                    return (bool)args[5] == (contents != 701) && !(bool)args[2] && player.m_aim.HasValue && player.m_aimStartTime.HasValue && player.ComponentInput.PlayerInput.Dig.HasValue;
                });
            }
            var mobile = mod.GetType("Game.ScMobileControls");
            foreach (var platform in Enum.GetValues<VersionsManager.Platform>()) {
                Test("device-detection/" + platform, () => (bool)mobile.GetMethod("IsMobilePlatform").Invoke(null, [platform])
                    == (platform is VersionsManager.Platform.Android or VersionsManager.Platform.IOS));
            }
            foreach (bool device in new[] { false, true }) foreach (bool touch in new[] { false, true }) {
                Test($"input-gate/{device}/{touch}", () => (bool)mobile.GetMethod("ShouldUseTouchInput").Invoke(null, [device, touch]) == (device && touch));
            }
            Test("desktop-never-enables-mobile-controls", () => {
                var player = Blank<ComponentPlayer>(); player.ComponentInput = Blank<ComponentInput>(); player.ComponentInput.IsControlledByTouch = true;
                var behavior = RuntimeHelpers.GetUninitializedObject(mod.GetType("Game.SubsystemScKnifeBlockBehavior"));
                // No GUI/player/project exists: the desktop branch must return before creating any controls.
                behavior.GetType().GetMethod("UpdateButtons", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(behavior, [null]);
                return !(bool)mobile.GetProperty("IsMobileDevice").GetValue(null) && !(bool)mobile.GetMethod("UsesTouchInput").Invoke(null, [player]);
            });
        } finally {
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var p in savedTypes) BlocksManager.BlockTypeToIndex[p.Key] = p.Value;
            BlocksManager.BlockNameToIndex.Clear(); foreach (var p in savedNames) BlocksManager.BlockNameToIndex[p.Key] = p.Value;
        }
        return results;
    }
}
