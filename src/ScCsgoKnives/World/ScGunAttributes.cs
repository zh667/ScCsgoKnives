using Engine;
namespace Game;

/// <summary>The numbers the attribute page shows, taken from the same place combat takes them.
///
/// Nothing here is a reference table or a CS2 export: every row is what the gun would actually do at the given
/// level, through <see cref="EffectiveGunStats"/>. Deliberately absent: current and maximum durability, and any
/// crafting materials - those belong to the repair page and the trade confirmation, not to an attribute card.
/// Nothing invented either: no rarity, no weight, no armour penetration and no attachments, because the mod has
/// none of them.</summary>
public static class ScGunAttributes {
    public enum Kind { Damage, Headshot, Range, RateOfFire, Capacity, ReloadOrCharge, Spread, Recoil }

    public sealed record Row(Kind Kind, string Label, string Text, string Unit, float Fraction, bool LowerIsBetter, bool Unlimited, string Detail);

    /// <summary>A gun's item value at a level, for previewing without touching any record. Every number below
    /// reads only the data half, so a host with no registered blocks (a headless check) still gets real values.</summary>
    public static int TemplateValue(int variant) {
        int block = 0;
        try { block = BlocksManager.GetBlockIndex<ScGunBlock>(true); } catch (Exception) { }
        return Terrain.MakeBlockValue(block, 0, GunSpec.WithId(variant, GunSpec.FreshFull));
    }

    static float HipCone(GunSpec spec) =>
        ScGunplaySettings.Enabled && ScGunHandling.ForMode(spec.Name, false) is { } mode ? mode.BaseCone : spec.SpreadDegrees;
    static float HipKick(GunSpec spec) =>
        ScGunplaySettings.Enabled && ScGunHandling.ForMode(spec.Name, false) is { } mode ? mode.KickPitch : spec.KickPitchDegrees;
    /// <summary>An empty-magazine reload, or 0 when the animation data cannot be read on this host.</summary>
    public static float ReloadSeconds(GunSpec spec, int capacity) {
        if (spec.RechargeSeconds > 0) return 0;
        try {
            int asset = ScGunBlock.AssetIndex(Array.IndexOf(GunSpec.All, spec));
            return asset < 0 ? 0 : KnifeAnimationController.ReloadSeconds(asset, true, ScReloadTransaction.IsTube(spec.Name) ? capacity : 0);
        }
        catch { return 0; }
    }

    /// <summary>Fixed display ranges. They come from the 35-gun table plus this rule set's own growth ceiling, so
    /// a bar means the same thing whichever gun is selected; nothing is renormalised around the current pick.</summary>
    public sealed record Scales(float Damage, float Headshot, float Range, float RateOfFire, float Capacity, float Reload, float Charge, float Spread, float Recoil);
    static Scales s_scales;
    public static Scales Ranges => s_scales ??= Build();
    static Scales Build() {
        float damage = 0, head = 0, range = 0, rate = 0, capacity = 0, reload = 0, charge = 0, spread = 0, recoil = 0;
        for (int v = 0; v < GunSpec.All.Length; v++) {
            var spec = GunSpec.All[v];
            var top = EffectiveGunStats.ResolveLevel(spec, TemplateValue(v), false, ScGunGrowth.MaxLevel);
            float maxPower = ScSurvivalBalance.Power(spec.Name) * ScGunGrowth.DamageMultiplier(ScGunGrowth.MaxLevel)
                * (ScGunSkinCatalog.For(v).Any() ? 1.5f : 1f);
            damage = Math.Max(damage, maxPower);
            head = Math.Max(head, maxPower * top.HeadMultiplier);
            // Exclude the unlimited sentinel from the finite-range bar scale.
            float finiteRange = EffectiveGunStats.ResolveLevel(spec,TemplateValue(v),false,ScGunGrowth.PrecisionLevel-1).Range;
            range = Math.Max(range, top.UnlimitedRange ? finiteRange : top.Range);
            rate = Math.Max(rate, top.CycleSeconds > 0 ? 60f / top.CycleSeconds : 0);
            capacity = Math.Max(capacity, ScGunGrowth.Capacity(v, ScGunGrowth.MaxLevel));
            reload = Math.Max(reload, ReloadSeconds(spec, ScGunGrowth.Capacity(v, ScGunGrowth.MaxLevel)));
            charge = Math.Max(charge, spec.RechargeSeconds);
            spread = Math.Max(spread, HipCone(spec));
            recoil = Math.Max(recoil, HipKick(spec));
        }
        return new Scales(Math.Max(1, damage), Math.Max(1, head), Math.Max(1, range), Math.Max(1, rate),
                          Math.Max(1, capacity), Math.Max(.5f, reload), Math.Max(.5f, charge), Math.Max(.01f, spread), Math.Max(.01f, recoil));
    }

    static string Number(float value, int decimals) => value.ToString("0." + new string('#', Math.Max(0, decimals)), System.Globalization.CultureInfo.InvariantCulture);
    static float Fraction(float value, float scale) => scale > 0 ? Math.Clamp(value / scale, 0, 1) : 0;

    /// <summary>The eight rows for one gun at one level. The Zeus swaps its reload row for its charge cycle.</summary>
    public static List<Row> Rows(GunSpec spec, int value, int level) {
        var s = EffectiveGunStats.ResolveLevel(spec, value, false, level);
        return RowsFromEffective(spec, s);
    }

    /// <summary>Consumes the final combat snapshot, not a second UI-only level formula.</summary>
    public static List<Row> RowsFromEffective(GunSpec spec, EffectiveGunStats s) {
        int level = s.Level;
        var r = Ranges;
        int variant = Array.IndexOf(GunSpec.All, spec);
        bool zeus = spec.RechargeSeconds > 0;
        float cone = (s.Handling?.BaseCone ?? spec.SpreadDegrees) * s.AngleScale,
              kick = (s.Handling?.KickPitch ?? spec.KickPitchDegrees) * s.AngleScale;
        var rows = new List<Row>();
        string pelletDetail = spec.Pellets > 1 ? $"单次扣扳机 {spec.Pellets} 颗，每颗 {Number(s.Power / spec.Pellets, 1)}" : null;
        rows.Add(new(Kind.Damage, spec.Pellets > 1 ? "单次总伤害" : "普通伤害", Number(s.Power, 1), "攻击力", Fraction(s.Power, r.Damage), false, false, pelletDetail));
        rows.Add(new(Kind.Headshot, "爆头伤害", Number(s.Power * s.HeadMultiplier, 1), "攻击力",
            Fraction(s.Power * s.HeadMultiplier, r.Headshot), false, false,
            spec.Pellets > 1 ? "全部弹丸命中头部时的总伤害" : s.HeadMultiplier <= 1 ? "此武器没有额外爆头倍率" : null));
        rows.Add(new(Kind.Range, "射程", s.UnlimitedRange ? "无限*" : Number(s.Range, 1), "格",
            s.UnlimitedRange ? 1f : Fraction(s.Range, r.Range), false, s.UnlimitedRange,
            s.UnlimitedRange ? "* 仅沿射击方向的连续已加载区域，不穿墙、不自动瞄准、不强制加载地形"
                             : $"满伤害至 {Number(FullDamageRange(spec, variant, level), 1)} 格，末端 {Number(EndMultiplier(spec, variant, level) * 100, 0)}%"));
        rows.Add(new(Kind.RateOfFire, "射速", s.CycleSeconds > 0 ? Number(60f / s.CycleSeconds, 0) : "—", "发/分",
            Fraction(s.CycleSeconds > 0 ? 60f / s.CycleSeconds : 0, r.RateOfFire), false, false, "理论射速；实际受帧率、操作及充能限制"));
        rows.Add(new(Kind.Capacity, zeus ? "充能容量" : "弹匣容量", s.Capacity.ToString(), "发",
            Fraction(s.Capacity, r.Capacity), false, false, zeus ? "电击枪恒为 1 次放电" : null));
        if (zeus) rows.Add(new(Kind.ReloadOrCharge, "充能时间（越低越好）", Number(s.RechargeSeconds, 2), "秒",
            Fraction(s.RechargeSeconds, r.Charge), true, false, null));
        else {
            float reload = ReloadSeconds(spec, s.Capacity);
            rows.Add(new(Kind.ReloadOrCharge, "换弹时间（越低越好）", reload > 0 ? Number(reload, 2) : "—", "秒",
                Fraction(reload, r.Reload), true, false, reload > 0 ? null : "本机缺少该枪的换弹动画数据"));
        }
        rows.Add(new(Kind.Spread, "散布（越低越好）", Number(cone, 2), "度", Fraction(cone, r.Spread), true, false,
            cone <= 0 ? "Lv10 起：所有姿态与模式下子弹不再偏离" : "静止、不开镜、首发"));
        rows.Add(new(Kind.Recoil, "后坐力（越低越好）", Number(kick, 2), "度", Fraction(kick, r.Recoil), true, false,
            kick <= 0 ? "Lv10 起：射击不再产生镜头上跳与横摆" : "每发镜头上跳基准"));
        return rows;
    }

    static float FullDamageRange(GunSpec spec, int variant, int level) =>
        (ScGunplaySettings.Enabled && ScGunHandling.For(spec.Name) is { } g ? g.FalloffStart : 10f) * ScGunGrowth.RangeScale(variant, level);
    static float EndMultiplier(GunSpec spec, int variant, int level) =>
        ScGunplaySettings.Enabled && ScGunHandling.For(spec.Name) is { } g ? g.FalloffFloor : .6f;

    /// <summary>The growth line: counter state, kills, progress and what the next level is worth.</summary>
    public static string GrowthText(int value, ScGunGrowthMode mode) {
        if (!EffectiveGunStats.TrySnapshotValue(value, out var s)) return "枪械记录不可读，无法显示计数。";
        if (!s.CounterInstalled) return CounterUnlockNotice + " 可在武器装配台原地安装；不更换枪械，不清空弹量与耐久。";
        if (mode == ScGunGrowthMode.CountOnly) return $"有效击杀 {s.KillCount}。本世界规则：仅计数，不提供等级成长或属性加成。";
        string rule = "";
        if (s.EarnedLevel >= ScGunGrowth.MaxLevel)
            return s.Level >= ScGunGrowth.MaxLevel ? $"有效击杀 {s.KillCount}（已满级 Lv{ScGunGrowth.MaxLevel}，计数继续累计）。"
                : $"有效击杀 {s.KillCount}，已解锁 Lv{ScGunGrowth.MaxLevel}，当前已应用 Lv{s.Level}；动作结束后应用升级，计数继续累计。";
        long next = ScGunGrowth.ToNextLevel(s.KillCount);
        string pending = s.PendingGrowthLevel != ScGunGrowth.NoPending && s.PendingGrowthLevel > s.AppliedGrowthLevel
            ? $" 已解锁 Lv{s.EarnedLevel}，动作结束后升级至 Lv{s.PendingGrowthLevel}。" : "";
        return $"有效击杀 {s.KillCount}，已应用 Lv{s.Level}，距 Lv{s.EarnedLevel + 1} 还需 {next} 次（每级 {ScGunGrowth.KillsPerLevel} 次，累计 {ScGunGrowth.KillsFor(ScGunGrowth.MaxLevel)} 次满级）。{pending}{rule}";
    }
    /// <summary>What reaching the cap is worth, in the terms the card is allowed to use.</summary>
    public const string CounterUnlockNotice = "安装击杀计数器后才解锁等级机制；从安装时开始计数，安装前的击杀不计入。换肤、去皮和维修保留已有击杀与等级。";
    public const string MaxLevelSummary = "Lv30 收益：基础伤害 ×10、弹匣 ×6.5（取整）、射速 ×3.5、耐久上限 ×2.5；皮肤枪先获得基础伤害 ×1.5。电击枪仍为单次充能，10 秒 → 1 秒。Lv10 起普通子弹枪已零散布、无射击后坐力、无距离衰减且无武器自身距离上限。换弹动画不加速。";
}
