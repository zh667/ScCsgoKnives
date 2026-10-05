using System.Collections;
using System.IO.Compression;
using System.Reflection;
using Engine;

/// <summary>Deathmatch round 6, R6-5 (docs/tasks/deathmatch-addon-round6-20261005.md §4): CS2's muzzle particle systems for every
/// gun, run by the core's ScMuzzleParticles from AnimationData/cs2_muzzle_fx.json. Checked here: the data loads; every gun with
/// muzzle effects has its CS2 root (the suppressed ones theirs), the Zeus none; every child system is there; every texture a
/// renderer draws is in the package; the view's control points are CS2's code sets them (CP1 1, CP3 how first-person); one shot of every gun, first
/// person and third, spawns particles that stay finite and within 5 m of the muzzle (the sparks fly about 3) and are all gone within three seconds. The look is
/// the user's acceptance.</summary>
static class MuzzleFxRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("muzzle-fx/" + name, ok, detail));
        var fx = mod.GetType("Game.Cs2MuzzleFx", false);
        var runner = mod.GetType("Game.ScMuzzleParticles", false);
        if (fx is null || runner is null) { Check("the core carries CS2's muzzle systems (Cs2MuzzleFx, ScMuzzleParticles)", false, "type missing"); return results; }
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        string error = (string)fx.GetProperty("LoadError").GetValue(null);
        Check("AnimationData/cs2_muzzle_fx.json loads (format 2)", error is null, error ?? "");
        if (error is not null) return results;
        string Root(string gun, bool silenced) => (string)fx.GetMethod("RootFor").Invoke(null, [gun, silenced]);
        object System(string name) => fx.GetMethod("System").Invoke(null, [name]);
        object Sheet(string texture) => fx.GetMethod("SheetOf").Invoke(null, [texture]);
        T Get<T>(object o, string member) => (T)(o.GetType().GetField(member, Any)?.GetValue(o) ?? o.GetType().GetProperty(member, Any)?.GetValue(o));

        // the roots CS2's weapon models play (02_models/events_full), a sample of each family
        var expected = new Dictionary<(string, bool), string> {
            [("ak47", false)] = "uweapon_muzflsh_ak47", [("m4a4", false)] = "uweapon_muzflsh_riffle", [("m4a1s", true)] = "uweapon_muzsilenced_rif", [("m4a1s", false)] = "uweapon_muzflsh_ak47",
            [("usp_silencer", true)] = "uweapon_muzsilenced_subm", [("usp_silencer", false)] = "uweapon_muzzleflash_pist", [("glock18", false)] = "uweapon_muzzleflash_pist",
            [("deagle", false)] = "uweapon_muzflsh_deagle", [("revolver", false)] = "uweapon_muzzleflash_pist_revolver", [("awp", false)] = "weapon_muzzleflash_snip",
            [("scar20", false)] = "weapon_muzzleflash_snip_ar", [("nova", false)] = "uweapon_muzflsh_shot", [("negev", false)] = "uweapon_muzflsh_mach",
            [("p90", false)] = "uweapon_muzzleflash_subm", [("mp5sd", true)] = "uweapon_muzsilenced_subm", [("aug", false)] = "uweapon_muzflsh_aug", [("galilar", false)] = "uweapon_muzflsh_riffle_lrg",
        };
        foreach (var ((gun, silenced), want) in expected) Check($"{gun}{(silenced ? " suppressed" : "")}: {want}", Root(gun, silenced) == want, Root(gun, silenced) ?? "none");
        Check("the Zeus has no muzzle system", Root("taser", false) is null);
        var specs = ((Array)mod.GetType("Game.GunSpec", true).GetField("All", Any).GetValue(null)).Cast<object>();
        var guns = specs.Where(g => Get<bool>(g, "MuzzleEffects")).Select(g => Get<string>(g, "Name")).ToList();
        var without = guns.Where(g => Root(g, false) is null).ToList();
        Check($"every gun with muzzle effects has a CS2 root ({guns.Count})", guns.Count >= 34 && without.Count == 0, string.Join(",", without));

        // every system reachable from a root is in the file, and every texture a renderer draws has its sheet and its package member
        var seen = new HashSet<string>(); var missing = new List<string>(); var textures = new HashSet<string>(); int lights = 0;
        void Walk(string name) {
            if (!seen.Add(name)) return;
            var s = System(name);
            if (s is null || Get<string>(s, "Missing") is not null) { missing.Add(name); return; }
            foreach (var r in Get<IEnumerable>(s, "Renderers")) {
                if (Get<bool>(r, "Light")) { lights++; continue; }
                textures.Add(Get<string>(r, "Texture")); if (Get<string>(r, "Texture2") is { } second) textures.Add(second);
            }
            foreach (var c in Get<IEnumerable>(s, "Children")) Walk(Get<string>(c, "System"));
        }
        foreach (var g in guns) { Walk(Root(g, false)); if (Root(g, true) is { } s) Walk(s); }
        Check($"every child system is there ({seen.Count}), the muzzle lights among them ({lights})", missing.Count == 0 && seen.Count >= 60 && lights >= 4, string.Join(",", missing));
        using var zip = ZipFile.OpenRead(package);
        var members = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Packaged(string asset) => members.Contains($"Assets/Textures/ScCsgoKnives/{asset}.png") || members.Contains($"Assets/Textures/ScCsgoKnives/{asset}.webp");
        var lost = textures.Where(t => Sheet(t) is not { } sheet || new[] { Get<string>(sheet, "Asset"), Get<string>(sheet, "LinearAsset") }.Where(a => a is not null).DefaultIfEmpty(null).Any(a => a is null || !Packaged(a))).ToList();
        Check($"every texture a renderer draws is packaged ({textures.Count})", textures.Count >= 5 && lost.Count == 0, string.Join(",", lost));

        // the random choice among a unified root's children: one flash variant (group 1), two of the extras (group 2)
        var ak = System("uweapon_muzflsh_ak47");
        var choose = Get<IEnumerable>(ak, "Choose").Cast<object>().Select(c => ((int, int))c).ToList();
        Check("AK root: two of group 2, one of group 1", choose.Contains((2, 2)) && choose.Contains((1, 1)), string.Join(" ", choose));

        // control points: 1 at 1 in both views (the named preview configurations are not taken), 3 first-person-ness (1 / 0;
        // pistols 0.75 / 0.5, pump guns 1 / 0.5 from their fps_view / thirdperson configurations), global scale 1
        var cpOf = runner.GetMethod("ControlPoints", Any);
        Vector3 Cp(string gun, bool firstPerson, int i) => ((Vector3[])cpOf.Invoke(null, [System(Root(gun, false)), gun, firstPerson]))[i];
        var guns1 = new[] { "nova", "glock18", "ak47", "awp", "ssg08", "scar20", "p90" };
        Check("CP1 1 and global scale 1 for every family, both views",
            guns1.All(g => Cp(g, true, 1).X == 1 && Cp(g, false, 1).X == 1 && Cp(g, true, 5).X == 1 && Cp(g, false, 5).X == 1),
            string.Join(" ", guns1.Select(g => $"{g}:{Cp(g, true, 1).X}/{Cp(g, false, 1).X}/{Cp(g, true, 5).X}")));
        Check("CP3: rifle 1 / 0, pistol 0.75 / 0.5, pump gun 1 / 0.5, sniper 1 / 0",
            Cp("ak47", true, 3).X == 1 && Cp("ak47", false, 3).X == 0 && Cp("glock18", true, 3).X == .75f && Cp("glock18", false, 3).X == .5f
            && Cp("nova", true, 3).X == 1 && Cp("nova", false, 3).X == .5f && Cp("awp", true, 3).X == 1 && Cp("awp", false, 3).X == 0,
            $"{Cp("ak47", true, 3).X} {Cp("ak47", false, 3).X} {Cp("glock18", true, 3).X} {Cp("glock18", false, 3).X} {Cp("nova", true, 3).X} {Cp("nova", false, 3).X} {Cp("awp", true, 3).X} {Cp("awp", false, 3).X}");

        // one shot of every gun, both views, at 60 Hz: something is drawn, everything stays finite and within 5 m of the muzzle,
        // and nothing is left after three seconds
        var bad = new List<string>(); int shots = 0, minimum = int.MaxValue;
        var systemsField = runner.GetField("m_systems", Any);
        foreach (var gun in guns)
            foreach (bool firstPerson in new[] { true, false })
                foreach (bool silenced in Root(gun, true) != Root(gun, false) ? new[] { false, true } : new[] { false }) {
                    var run = Activator.CreateInstance(runner);
                    Vector3 muzzle = new(10, 70, 10);
                    runner.GetMethod("Shot").Invoke(run, [gun, silenced, muzzle, new Vector3(0, 0, -1), firstPerson, null, null, Vector3.Zero, 0f, 0f]);
                    runner.GetMethod("Update").Invoke(run, [1 / 60f]);
                    int count = (int)runner.GetProperty("Count").GetValue(run); minimum = Math.Min(minimum, count); shots++;
                    float far = 0; bool finite = true;
                    for (int step = 0; step < 180; step++) {
                        foreach (var s in (IEnumerable)systemsField.GetValue(run))
                            foreach (var p in (IEnumerable)s.GetType().GetField("Live").GetValue(s)) {
                                var at = (Vector3)p.GetType().GetField("Position").GetValue(p);
                                finite &= float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z);
                                far = MathF.Max(far, Vector3.Distance(at, muzzle));
                            }
                        runner.GetMethod("Update").Invoke(run, [1 / 60f]);
                    }
                    int left = (int)runner.GetProperty("Count").GetValue(run);
                    if (count == 0 || !finite || far > 5 || left != 0) bad.Add($"{gun}{(silenced ? "-s" : "")}-{(firstPerson ? "fp" : "tp")}: {count} at first, farthest {far:0.00} m, {left} left");
                }
        Check($"every gun's shot, both views, draws particles that stay finite, within 5 m (sparks fly about 3) and are gone in 3 s ({shots} shots)", bad.Count == 0 && shots >= 68, bad.Count == 0 ? $"fewest at first {minimum}" : string.Join("; ", bad.Take(8)));
        return results;
    }
}
