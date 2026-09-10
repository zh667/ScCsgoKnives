using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;

static class CombatRegression {
    internal static IEnumerable<MethodBase> Calls(MethodInfo method) {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);
        byte[] il = method.GetMethodBody().GetILAsByteArray();
        for (int p = 0; p < il.Length;) {
            short value = il[p++];
            if (value == 0xfe) value = (short)(0xfe00 | il[p++]);
            OpCode code = codes[value];
            if (code.OperandType == OperandType.InlineMethod) yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, p));
            p += code.OperandType switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, p),
                _ => 4
            };
        }
    }
    internal record Result(string Name, bool Ok, string Detail);
    sealed class Target : ComponentBody {
        public override BoundingBox BoundingBox => new(Position - new Vector3(.4f), Position + new Vector3(.4f));
    }
    internal static List<Result> Run(Assembly mod, string package) {
        List<Result> result = [];
        void Test(string name, Func<bool> action) {
            try { result.Add(new("combat/" + name, action(), name)); }
            catch (Exception e) { result.Add(new("combat/" + name, false, e.ToString())); }
        }
        object Call(string type, string method, params object[] args) => mod.GetType("Game." + type).GetMethod(method).Invoke(null, args);
        foreach (var block in new Block[] { new TallGrassBlock(), new OakLeavesBlock(), new BirchLeavesBlock(), new SpruceLeavesBlock(), new FallenLeavesBlock(), new IvyBlock(), new WaterBlock() })
            Test("foliage-pass/" + block.GetType().Name, () => !(bool)Call("ScGunRange", "StopsBullet", block));
        foreach (var block in new Block[] { new GrassBlock(), new DirtBlock(), new GraniteBlock(), new OakWoodBlock(), new GlassBlock() })
            Test("cover-stops/" + block.GetType().Name, () => (bool)Call("ScGunRange", "StopsBullet", block));
        foreach (var type in typeof(Block).Assembly.GetTypes().Where(t => !t.IsAbstract && (typeof(CrossBlock).IsAssignableFrom(t)
            || typeof(LeavesBlock).IsAssignableFrom(t) || typeof(WaterPlantBlock).IsAssignableFrom(t))))
            Test("all-native-vegetation/" + type.Name, () => !(bool)Call("ScGunRange", "StopsBullet", Activator.CreateInstance(type)));
        Test("runtime-growth-sweep-calls-rule-aware-advance", () => {
            var update = mod.GetType("Game.SubsystemScGunBlockBehavior").GetMethod("UpdateGrowth", BindingFlags.Instance | BindingFlags.NonPublic);
            return Calls(update).Any(c => c.DeclaringType.Name == "ScGunGrowthService" && c.Name == "Advance");
        });
        Test("runtime-fire-calls-shared-bullet-ray", () => {
            var fire = mod.GetType("Game.SubsystemScGunBlockBehavior").GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic);
            return Calls(fire).Any(c => c.DeclaringType.Name == "ScGunRange" && c.Name == "TraceBullet");
        });
        Test("runtime-shot-noise-and-leaf-effect-wired",()=>{
            var fire=mod.GetType("Game.SubsystemScGunBlockBehavior").GetMethod("Fire",BindingFlags.Instance|BindingFlags.NonPublic);
            var calls=Calls(fire).ToArray();
            int commit=Array.FindIndex(calls,c=>c.Name=="Commit"),noise=Array.FindIndex(calls,c=>c.Name=="NotifyNoise"),ray=Array.FindIndex(calls,c=>c.Name=="TraceBullet"),leaves=Array.FindIndex(calls,c=>c.Name=="BreakLeaves");
            return commit>=0&&noise>commit&&leaves>ray&&ray>=0&&calls.Any(c=>c.Name=="PlaySound");
        });
        foreach (bool creative in new[] { false, true }) foreach (bool handling in new[] { false, true })
            foreach (Block plant in new Block[] { new TallGrassBlock(), new RedFlowerBlock(), new PurpleFlowerBlock(), new WhiteFlowerBlock(), new OakLeavesBlock(), new BirchLeavesBlock(), new SpruceLeavesBlock(), new RyeBlock(), new CottonBlock(), new IvyBlock() })
                Test($"live-ray/{(creative ? "creative" : "survival")}/{handling}/{plant.GetType().Name}", () => {
                    var old = new[] { BlocksManager.Blocks[710], BlocksManager.Blocks[711] };
                    using var terrain = new Terrain(); terrain.AllocateChunk(0, 0);
                    var subsystem = new SubsystemTerrain { Terrain = terrain };
                    var enabled = mod.GetType("Game.ScGunplaySettings").GetField("Enabled"); var wasEnabled = enabled.GetValue(null);
                    try {
                        enabled.SetValue(null, handling);
                        BlocksManager.Blocks[710] = plant; BlocksManager.Blocks[711] = new OakWoodBlock();
                        terrain.SetCellValueFast(2, 10, 1, 710); terrain.SetCellValueFast(3, 10, 1, 710); terrain.SetCellValueFast(7, 10, 1, 711);
                        var start = new Vector3(.5f, 10.25f, 1.5f); var dir = Vector3.UnitX;
                        var trace = Activator.CreateInstance(mod.GetType("Game.ScGunRange+BulletTrace"));
                        var hit = (TerrainRaycastResult?)Call("ScGunRange", "TraceBullet", subsystem, start, dir, 12f, trace);
                        var target = new Target { Position = new Vector3(5.5f, 10.25f, 1.5f) }; var bodies = new SubsystemBodies(); bodies.AddBody(target);
                        var behindPlant = bodies.Raycast(start, start + dir * (hit?.Distance ?? 12f), 0, (_, _) => true);
                        if (hit?.CellFace.X != 7 || behindPlant is null) return false;
                        terrain.SetCellValueFast(4, 10, 1, 711); // real cover before the animal
                        hit = (TerrainRaycastResult?)Call("ScGunRange", "TraceBullet", subsystem, start, dir, 12f, trace);
                        return hit?.CellFace.X == 4 && bodies.Raycast(start, start + dir * hit.Value.Distance, 0, (_, _) => true) is null;
                    } finally { enabled.SetValue(null, wasEnabled); for (int i = 0; i < 2; i++) BlocksManager.Blocks[710 + i] = old[i]; }
                });
        Test("real-terrain-ray-passes-grass-leaves-stops-at-trunk", () => {
            var old = new[] { BlocksManager.Blocks[710], BlocksManager.Blocks[711], BlocksManager.Blocks[712] };
            using var terrain = new Terrain(); terrain.AllocateChunk(0, 0);
            try {
                BlocksManager.Blocks[710] = new TallGrassBlock(); BlocksManager.Blocks[711] = new OakLeavesBlock(); BlocksManager.Blocks[712] = new OakWoodBlock();
                terrain.SetCellValueFast(2, 10, 1, 710); terrain.SetCellValueFast(3, 10, 1, 711); terrain.SetCellValueFast(5, 10, 1, 712);
                var start = new Vector3(.5f, 10.5f, 1.5f); var end = new Vector3(9.5f, 10.5f, 1.5f);
                var original = SubsystemTerrain.Raycast(terrain, start, end, false, true, (v,d) => v != 0);
                var corrected = SubsystemTerrain.Raycast(terrain, start, end, false, true, (v,d) => (bool)Call("ScGunRange", "TerrainStopsBullet", v));
                terrain.SetCellValueFast(5, 10, 1, 0);
                var clear = SubsystemTerrain.Raycast(terrain, start, end, false, true, (v,d) => (bool)Call("ScGunRange", "TerrainStopsBullet", v));
                return original is { } a && a.CellFace.X < 5 && corrected is { } b && b.CellFace.X == 5 && clear is null;
            } finally { for (int i = 0; i < 3; i++) BlocksManager.Blocks[710 + i] = old[i]; }
        });
        Test("held-counter-template-wired-before-gun-routing", () => {
            var calls = Calls(mod.GetType("Game.SubsystemScGunBlockBehavior").GetMethod("Update")).ToArray();
            int counter = Array.FindIndex(calls, c => c.DeclaringType.Name == "ScGunCounterTemplateBlock" && c.Name == "Materialize");
            return counter >= 0
                && calls.Any(c => c.DeclaringType.Name == "ScGunCounterTemplateBlock" && c.Name == "IsTemplate")
                && Array.FindIndex(calls, c => c.DeclaringType.Name == "ScGunBlock" && c.Name == "IsKnown") > counter
                && Array.FindIndex(calls, c => c.Name == "UpdateGun") > counter
                && calls.Skip(counter + 1).Any(c => c.Name == "get_ActiveBlockValue");
        });
        var center = new Ray3(new Vector3(0, 1.5f, 0), Vector3.UnitZ);
        var finger = new Ray3(center.Position, Vector3.Normalize(new Vector3(.4f, -.2f, 1)));
        Ray3 Aim(bool touch, Ray3? dig, Ray3? hit) => (Ray3)Call("ScShotAim", "Select", touch, dig, hit, center);
        Test("desktop-retains-mouse-ray", () => Vector3.Distance(Aim(false, finger, null).Direction, finger.Direction) < .000001f);
        Test("touch-camera-ray-even-with-hit-only", () => Aim(true, null, finger).Direction == center.Direction);
        Test("invalid-ray-falls-back", () => Aim(false, new Ray3(center.Position, Vector3.Zero), null).Direction == center.Direction);
        Test("non-unit-ray-normalized", () => Math.Abs(Aim(false, new Ray3(center.Position, Vector3.UnitZ * 4), null).Direction.Length() - 1) < .001);
        foreach (float distance in new[] { 1f, 10, 30, 60, 70 }) Test("touch-api-body-ray/" + distance, () => {
            var bodies = new SubsystemBodies(); var body = new Target { Position = center.Position + Vector3.UnitZ * distance };
            bodies.AddBody(body); var ray = Aim(true, finger, finger);
            var hit = bodies.Raycast(ray.Position, ray.Position + ray.Direction * 64, .35f, (b, d) => true);
            return distance < 64 ? hit?.ComponentBody == body && Math.Abs(hit.Value.Distance - (distance - .75f)) < .002f : hit is null;
        });
        Test("taser-range-preserved", () => {
            var bodies = new SubsystemBodies(); var body = new Target { Position = center.Position + Vector3.UnitZ * 10 }; bodies.AddBody(body);
            return bodies.Raycast(center.Position, center.Position + center.Direction * 3.05f, .35f, (b, d) => true) is null;
        });
        Test("shot-captured-before-unzoom-animation-and-recoil", () => {
            var fire = mod.GetType("Game.SubsystemScGunBlockBehavior").GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic);
            var calls = Calls(fire).ToArray();
            int capture = Array.FindIndex(calls, c => c.DeclaringType.Name == "ScShotAim" && c.Name == "Capture");
            return capture >= 0 && new[] { "LeaveScope", "TriggerShoot", "Kick" }.All(n => Array.FindIndex(calls, c => c.Name == n) > capture)
                && !calls.Any(c => c.DeclaringType.Name == "Cs2Weapons" && c.Name == "SpreadDegrees");
        });
        foreach (string gun in new[] { "awp", "ssg08", "aug", "sg556", "g3sg1", "scar20" }) foreach (bool touch in new[] { false, true }) foreach (float profile in new[] { 0f, 1f })
            Test($"scoped-shot-snapshot/{gun}/{touch}/{profile}", () => {
                var tuning = mod.GetType("Game.KnifeTuning").GetField("GunNumbers"); var old = tuning.GetValue(null);
                try {
                    tuning.SetValue(null, profile);
                    var spec = Call("GunSpec", "ForAsset", gun);
                    float fallback = (float)spec.GetType().GetField("SpreadDegrees").GetValue(spec);
                    object shot = Call("ScShotAim", "Capture", gun, touch, true, false, false, finger, finger, center, 0f, fallback);
                    var type = shot.GetType(); Ray3 ray = (Ray3)type.GetProperty("Ray").GetValue(shot);
                    float spread = (float)type.GetProperty("Spread").GetValue(shot);
                    var numbers = Call("Cs2Weapons", "Get", gun);
                    float expected = (float)numbers.GetType().GetProperty("SpreadDegreesAlternate").GetValue(numbers);
                    if (profile == 0) expected = Math.Min(expected, fallback * .35f);
                    if (ray.Direction != center.Direction || spread != expected || spread > .25f || !(bool)type.GetProperty("Alternate").GetValue(shot)) return false;
                    // Even the edge of this standing scoped cone hits a centred animal at 60m.
                    var bodies = new SubsystemBodies(); var body = new Target { Position = center.Position + Vector3.UnitZ * 60 }; bodies.AddBody(body);
                    float angle = spread * MathF.PI / 180;
                    for (int i = 0; i < 16; i++) {
                        float phi = i * MathF.Tau / 16;
                        Vector3 direction = new(MathF.Sin(angle) * MathF.Cos(phi), MathF.Sin(angle) * MathF.Sin(phi), MathF.Cos(angle));
                        if (bodies.Raycast(ray.Position, ray.Position + direction * 64, .35f, (b,d) => true)?.ComponentBody != body) return false;
                    }
                    return true;
                } finally { tuning.SetValue(null, old); }
            });
        foreach (var c in new[] { (1f,.8f,1), (1f,0f,2), (0f,0f,0), (0f,-1f,0), (.5f,.5f,0), (.5f,.8f,0), (float.NaN,0f,0) })
            Test($"health-result/{c}", () => (int)Call("ScCombatFeedback", "Outcome", c.Item1, c.Item2) == c.Item3);
        Test("kill-feed-bounded-and-captured", () => {
            var type = mod.GetType("Game.ScCombatFeedback"); var state = Activator.CreateInstance(type);
            for (int i = 0; i < 20; i++) type.GetMethod("Record").Invoke(state, [2, "狼\n", "AWP", 25f, (double)i]);
            var kills = (IList)type.GetField("Kills").GetValue(state); var newest = kills[0];
            return kills.Count == 3 && (string)newest.GetType().GetProperty("Target").GetValue(newest) == "狼"
                && (string)newest.GetType().GetProperty("Weapon").GetValue(newest) == "AWP"
                && (float)newest.GetType().GetProperty("Distance").GetValue(newest) == 25;
        });
        Test("magazine-animation-finish-only", () => {
            int data = (int)Call("GunSpec", "MakeData", 0, 8, false), value = Terrain.MakeBlockValue(512, 0, data);
            var inv = new ComponentCreativeInventory { OpenSlotsCount = 10 }; for (int i = 0; i < 10; i++) inv.m_slots.Add(0); inv.AddSlotItems(0, value, 1);
            var type = mod.GetType("Game.ScReloadTransaction"); var tx = Activator.CreateInstance(type, [inv, 0, value, 901, 0, 30]);
            if (!(bool)type.GetMethod("Discard").Invoke(tx, null)) return false;
            foreach (double now in new[] { 0, .8, 1.5, 2.999 })
                if ((bool)type.GetMethod("FinishMagazine").Invoke(tx, [now, 3d]) || inv.GetSlotValue(0) != value) return false;
            return (bool)type.GetMethod("FinishMagazine").Invoke(tx, [3d, 3d])
                && (int)Call("GunSpec", "GetRounds", Terrain.ExtractData(inv.GetSlotValue(0))) == 30
                && !(bool)type.GetMethod("FinishMagazine").Invoke(tx, [4d, 3d]);
        });
        for (int kind = 0; kind < 8; kind++) {  // 0-6 supplies and the bench, 7 the paint tin
            int k = kind;
            Test("supply-ui-colored-with-zero-scene-light/" + k, () => {
                var source = (BlockMesh)Call("ScSurvivalMesh", "Build", k);
                var mesh = (BlockMesh)Call("ScSurvivalMesh", "InventoryMesh", source);
                float saved = LightingManager.LightIntensityByLightValue[15];
                try {
                    LightingManager.LightIntensityByLightValue[15] = 0;
                    var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
                    var renderer = new PrimitivesRenderer3D(); var matrix = Matrix.Identity;
                    BlocksManager.DrawMeshBlock(renderer, mesh, texture, Color.White, 1, ref matrix, new DrawBlockEnvironmentData { Light = 15, DrawBlockMode = DrawBlockMode.UI });
                    var batch = renderer.TexturedBatch(texture, true, 0, null, RasterizerState.CullCounterClockwiseScissor, null, SamplerState.PointClamp);
                    return batch.TriangleVertices.Count == source.Vertices.Count && batch.TriangleVertices.All(v => v.Color.R >= 150 && v.Color.A == 255)
                        && source.Vertices.All(v => !v.IsEmissive) && mesh.Indices.SequenceEqual(source.Indices);
                } finally { LightingManager.LightIntensityByLightValue[15] = saved; }
            });
        }
        using var zip = ZipFile.OpenRead(package);
        Test("supply-texture-explicit-opaque-rgba", () => {
            using var stream = zip.GetEntry("Assets/Textures/ScCsgoKnives/survival_surface.png").Open();
            using var buffer = new MemoryStream(); stream.CopyTo(buffer); var bytes = buffer.ToArray();
            var pixels = Engine.Media.Image.Load(new MemoryStream(bytes));
            try { return bytes[25] == 6 && pixels.Pixels.All(c => c.A == 255 && c.R > 0 && c.G > 0 && c.B > 0); }
            finally { pixels.Dispose(); }
        });
        Test("bf1-original-kill-sound-present", () => zip.GetEntry("Assets/Audio/ScCsgoKnives/bf1_kill_confirm.ogg")?.Length > 10000);
        Test("kill-ding-game-decoder-audible-immediate-and-unclipped", () => {
            string sound = (string)mod.GetType("Game.ScCombatAudio").GetField("KillSound").GetRawConstantValue();
            using var stream = zip.GetEntry("Assets/" + sound + ".wav").Open();
            // ContentInfo.Duplicate supplies a seekable in-memory resource in game.
            using var memory = new MemoryStream(); stream.CopyTo(memory); memory.Position = 0;
            var data = Engine.Media.SoundData.Load(memory);
            double Rms(IEnumerable<short> samples) => Math.Sqrt(samples.Average(x => (double)x * x));
            return data.ChannelsCount == 1 && data.SamplingFrequency == 48000 && data.Data.Length is > 24000 and < 57600
                && Rms(data.Data.Take(4800)) > 1000 && Rms(data.Data) > 2000 && data.Data.Max(x => Math.Abs((int)x)) is > 20000 and < 30000;
        });
        Test("kill-audio-respects-sound-mute", () => {
            float volume = SettingsManager.SoundsVolume;
            try { SettingsManager.SoundsVolume = 0; return !(bool)Call("ScCombatAudio", "PlayKill"); }
            finally { SettingsManager.SoundsVolume = volume; }
        });
        return result;
    }
}
