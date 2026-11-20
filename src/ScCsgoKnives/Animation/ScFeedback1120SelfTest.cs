using Engine;
namespace Game;

/// <summary>feedback-1120-160: the recoil setting's curves and their use by the gun numbers, the survival spray pattern,
/// and which clothing counts as unbreakable. Headless; restores the forced recoil mode it found.</summary>
public static class ScFeedback1120SelfTest {
    const int GunBlock = 512;
    static int Template(int variant) => Terrain.MakeBlockValue(GunBlock, 0, GunSpec.WithId(variant, GunSpec.FreshFull));
    static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-5f;

    public static void Run(Action<string, bool, string> check) {
        string detail = "";
        void Test(string name, Func<bool> test) { detail = ""; try { check("feedback-1120/" + name, test(), detail.Length > 0 ? detail : name); } catch (Exception e) { check("feedback-1120/" + name, false, e.ToString()); } }
        var forced = ScRecoilRules.Forced;
        try {
            Test("recoil/standard-is-the-growth-curve", () => Enumerable.Range(0, ScGunGrowth.MaxLevel + 1).All(l =>
                ScRecoilRules.SpreadScale(ScRecoilMode.Standard, l, 1) == ScGunGrowth.AngleScale(l) && ScRecoilRules.SpreadScale(ScRecoilMode.Standard, l, 9) == ScGunGrowth.AngleScale(l)
                && ScRecoilRules.KickScale(ScRecoilMode.Standard, l) == ScGunGrowth.AngleScale(l)));
            Test("recoil/off-straight-rounds-no-kick-shotgun-pellets-kept", () => Enumerable.Range(0, ScGunGrowth.MaxLevel + 1).All(l =>
                ScRecoilRules.KickScale(ScRecoilMode.Off, l) == 0 && ScRecoilRules.SpreadScale(ScRecoilMode.Off, l, 1) == 0
                && ScRecoilRules.SpreadScale(ScRecoilMode.Off, l, 8) == ScGunGrowth.AngleScale(l)) && ScRecoilRules.SpreadScale(ScRecoilMode.Off, 0, 8) == 1);
            Test("recoil/realistic-5-percent-a-level-half-from-lv10", () => Enumerable.Range(0, ScGunGrowth.MaxLevel + 1).All(l =>
                Near(ScRecoilRules.KickScale(ScRecoilMode.Realistic, l), Math.Max(.5f, 1 - .05f * l)) && Near(ScRecoilRules.SpreadScale(ScRecoilMode.Realistic, l, 1), Math.Max(.5f, 1 - .05f * l)))
                && ScRecoilRules.KickScale(ScRecoilMode.Realistic, 0) == 1 && Near(ScRecoilRules.KickScale(ScRecoilMode.Realistic, 10), .5f) && Near(ScRecoilRules.KickScale(ScRecoilMode.Realistic, 50), .5f));
            Test("recoil/gun-numbers-follow-the-setting", () => {
                var parts = new List<string>();
                foreach (var mode in new[] { ScRecoilMode.Standard, ScRecoilMode.Off, ScRecoilMode.Realistic })
                    foreach (string name in new[] { "ak47", "nova", "awp" })
                        foreach (int l in new[] { 0, 5, 10, 30 }) {
                            ScRecoilRules.Forced = mode;
                            var spec = GunSpec.All.First(g => g.Name == name);
                            var e = EffectiveGunStats.ResolveLevel(spec, Template(Array.IndexOf(GunSpec.All, spec)), false, l);
                            float spread = ScRecoilRules.SpreadScale(mode, l, spec.Pellets), kick = ScRecoilRules.KickScale(mode, l);
                            if (e.AngleScale != spread || e.KickScale != kick || (mode == ScRecoilMode.Standard && e.KickScaleOverride is not null)) {
                                detail = $"{mode} {name} Lv{l}: spread {e.AngleScale}/{spread}, kick {e.KickScale}/{kick}, override {e.KickScaleOverride}"; return false;
                            }
                            if (l == 10) parts.Add($"{mode} {name}: {e.AngleScale}/{e.KickScale}");
                        }
                detail = string.Join("; ", parts);
                return true;
            });
            Test("recoil/text-roundtrip", () => Enum.GetValues<ScRecoilMode>().All(m => ScRecoilRules.Parse(ScRecoilRules.Text(m)) == m)
                && ScRecoilRules.Parse(null) == ScRecoilMode.Standard && ScRecoilRules.Parse("sideways") == ScRecoilMode.Standard
                && ScRecoilRules.Next(ScRecoilMode.Standard) == ScRecoilMode.Off && ScRecoilRules.Next(ScRecoilMode.Off) == ScRecoilMode.Realistic && ScRecoilRules.Next(ScRecoilMode.Realistic) == ScRecoilMode.Standard);
            // The AK-47's vdata (seed 223, angle 0 +-70, magnitude 30, full auto) against the independent Python reference of the
            // published algorithm that the deathmatch's own check K01 uses: [(27.3303, 22.5), (-17.8304, 21.6328), (5.8008, 22.9554),
            // (1.6354, 25.1531)] ... #63 (20.377, 30.0).
            Test("spray/ak47-table-from-the-core-vdata", () => {
                var t = ScSprayRecoil.TableOf("ak47", false);
                bool At(int i, float a, float m) => MathF.Abs(t[i].Angle - a) < 2e-3f && MathF.Abs(t[i].Magnitude - m) < 2e-3f;
                detail = string.Join(" ", t.Take(4).Select(x => $"{x.Angle:0.####}/{x.Magnitude:0.####}")) + $" #63 {t[63].Angle:0.####}/{t[63].Magnitude:0.####}";
                return t.Length == ScSprayRecoil.TableSize && At(0, 27.3303f, 22.5f) && At(1, -17.8304f, 21.6328f) && At(2, 5.8008f, 22.9554f) && At(3, 1.6354f, 25.1531f) && At(63, 20.377f, 30f)
                    && ScSprayRecoil.TableOf("ak47", false) == t && ScSprayRecoil.TableOf("taser", false).All(x => x.Magnitude == 0);
            });
            Test("spray/rises-recovers-and-scales", () => {
                var full = new ScSprayRecoil.State(); var half = new ScSprayRecoil.State { Scale = .5f };
                for (int i = 0; i < 10; i++) { full.Fire("ak47", false, i * .1); half.Fire("ak47", false, i * .1); }
                full.Advance(1); half.Advance(1);
                var (bullet, view) = full.Angles(); var (halfBullet, _) = half.Angles();
                var settled = new ScSprayRecoil.State(); settled.Fire("ak47", false, 0); settled.Advance(5);
                detail = $"after 10 rounds: bullet {bullet}, view {view}, half {halfBullet}; settled {settled.Angles().Bullet}";
                return bullet.X > 1 && Near(view.X, bullet.X * ScSprayRecoil.ViewPitch) && Near(view.Y, bullet.Y * ScSprayRecoil.ViewYaw)
                    && Near(halfBullet.X, bullet.X * .5f) && Near(halfBullet.Y, bullet.Y * .5f) && settled.Angles().Bullet == Vector2.Zero;
            });
            Test("armor/unbreakable-means-protection-and-infinite-sturdiness", () =>
                ScUnbreakableArmor.Unbreakable(new ClothingData { ArmorProtection = 1, Sturdiness = float.PositiveInfinity })
                && !ScUnbreakableArmor.Unbreakable(new ClothingData { ArmorProtection = 1, Sturdiness = 200 })
                && !ScUnbreakableArmor.Unbreakable(new ClothingData { ArmorProtection = 0, Sturdiness = float.PositiveInfinity })
                && !ScUnbreakableArmor.Unbreakable(null) && Near(ScUnbreakableArmor.Floor, .3f));
        } finally { ScRecoilRules.Forced = forced; }
    }
}
