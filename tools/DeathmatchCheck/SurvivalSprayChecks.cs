// feedback-1120-160 #3: the core's survival "realistic" spray pattern (ScSprayRecoil) is a copy of the deathmatch's model fed
// from the core's own vdata. Both must give the same pattern for every gun: the same table, and the same rounds of a long
// spray at the gun's own cadence (scale 1); the survival scale then shrinks the whole pattern and nothing else.
using Engine;
using Game;

static partial class Program {
    static void SurvivalSpray() {
        var tables = new List<string>(); int compared = 0;
        foreach (var spec in GunSpec.All) foreach (bool alternate in new[] { false, true }) {
            var dm = DmRecoilModel.TableOf(spec.Name, alternate); var core = ScSprayRecoil.TableOf(spec.Name, alternate);
            compared++;
            if (core is null || dm.Length != core.Length || dm.Zip(core).Any(p => p.First.Angle != p.Second.Angle || p.First.Magnitude != p.Second.Magnitude))
                tables.Add($"{spec.Name}{(alternate ? "/alt" : "")}");
        }
        Test("F01", "every gun's survival spray table (core vdata) is the deathmatch's table, both handling states", tables.Count == 0 && compared == 2 * GunSpec.All.Length,
            $"{compared} tables compared; different: [{string.Join(",", tables)}]");
        var sprays = new List<string>(); float worst = 0;
        foreach (var spec in GunSpec.All) {
            var dm = new DmRecoilState(); var core = new ScSprayRecoil.State(); var half = new ScSprayRecoil.State { Scale = .5f };
            float cycle = DmWeapons.Raw(spec.Name, "m_flCycleTime");
            float gunWorst = 0;
            for (int k = 0; k < 40; k++) {
                double t = 10 + k * (double)cycle;
                dm.Advance(t); core.Advance(t); half.Advance(t);
                var (a, av) = dm.Angles(); var (b, bv) = core.Angles(); var (h, _) = half.Angles();
                float d = Math.Max((a - b).Length(), (av - bv).Length());
                gunWorst = Math.Max(gunWorst, Math.Max(d, (h - b * .5f).Length()));
                dm.Fire(spec.Name, false, t); core.Fire(spec.Name, false, t); half.Fire(spec.Name, false, t);
            }
            worst = Math.Max(worst, gunWorst);
            if (gunWorst > 1e-4f) sprays.Add(spec.Name);
        }
        Test("F02", "40 rounds of every gun at its own cadence: the survival spray leaves exactly where the deathmatch's does; scale 0.5 halves every round",
            sprays.Count == 0, $"largest difference {worst:0.######} deg; guns off: [{string.Join(",", sprays)}]");
    }
}
