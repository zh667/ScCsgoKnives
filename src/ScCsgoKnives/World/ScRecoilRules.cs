namespace Game;

/// <summary>The survival "后坐与弹道" setting (feedback-1120-160 #3; player: "CS武器或许可以加后坐力和随机弹道，模组设置里面可以
/// 选择关闭"). Numbers are the stored values: never renumber.</summary>
public enum ScRecoilMode : byte { Standard = 0, Off = 1, Realistic = 2 }

/// <summary>Which recoil and spread survival guns have. Standard is exactly what every earlier version did. A world mode that
/// governs a player's weapons (deathmatch) keeps its own rules and is never asked about this.
///
/// Owner: this device's <see cref="ScUiSettings.GunRecoil"/>. In multiplayer the host decides: a client asks for it once
/// its handshake is accepted (<see cref="OpAsk"/>; the server's own "peer accepted" moment comes before the handshake answer,
/// so a message sent then would reach a client that does not take game messages yet), the host answers and tells every
/// client again whenever it saves its settings (<see cref="OpRules"/>). A client plays by the last value its host sent
/// (Standard until one arrives) and its settings page shows that, read-only.</summary>
public static class ScRecoilRules {
    /// <summary>Server → client: the host's mode, one byte. Client → server: please send it (empty).</summary>
    public const ushort OpRules = 77, OpAsk = 78;
    /// <summary>Realistic: growth takes this much off per level and stops at <see cref="RealisticFloor"/> (Lv10 on).</summary>
    public const float RealisticStep = .05f, RealisticFloor = .5f;
    static ScRecoilMode? s_host;
    /// <summary>Self-tests only: the mode every caller sees while set (the growth self-tests pin Standard, whatever this
    /// device's setting is).</summary>
    internal static ScRecoilMode? Forced;

    public static ScRecoilMode Local => ScUiSettings.GunRecoil;
    /// <summary>The mode this process plays survival guns by.</summary>
    public static ScRecoilMode Current => Forced ?? (ScNet.IsRemoteClient ? s_host ?? ScRecoilMode.Standard : Local);
    /// <summary>What a client last heard from its host (null: nothing yet). Offline checks read it.</summary>
    public static ScRecoilMode? FromHost => s_host;

    /// <summary>How much of the spread angle is left at <paramref name="level"/>. Off keeps a shotgun's pellets apart
    /// (otherwise it would fire one slug); its single rounds fly straight.</summary>
    public static float SpreadScale(ScRecoilMode mode, int level, int pellets) => mode switch {
        ScRecoilMode.Off => pellets > 1 ? ScGunGrowth.AngleScale(level) : 0f,
        ScRecoilMode.Realistic => RealisticScale(level),
        _ => ScGunGrowth.AngleScale(level)
    };
    /// <summary>How much of the camera recoil (the survival kick, or the realistic spray pattern) is left at <paramref name="level"/>.</summary>
    public static float KickScale(ScRecoilMode mode, int level) => mode switch {
        ScRecoilMode.Off => 0f,
        ScRecoilMode.Realistic => RealisticScale(level),
        _ => ScGunGrowth.AngleScale(level)
    };
    public static float RealisticScale(int level) => Math.Max(RealisticFloor, 1f - RealisticStep * ScGunGrowth.Clamp(level));

    public static string Label(ScRecoilMode mode) => mode switch { ScRecoilMode.Off => "关闭", ScRecoilMode.Realistic => "拟真", _ => "标准" };
    public static string Explain(ScRecoilMode mode) => mode switch {
        ScRecoilMode.Off => "开枪不上跳，子弹直线飞出（霰弹枪弹丸照常散开）。",
        ScRecoilMode.Realistic => "CS2 固定扫射图案，可压枪；成长减到一半为止。",
        _ => "小幅上跳、随机散布；成长逐级减小，Lv10 起归零。"
    };
    public static ScRecoilMode Next(ScRecoilMode mode) => mode switch {
        ScRecoilMode.Standard => ScRecoilMode.Off, ScRecoilMode.Off => ScRecoilMode.Realistic, _ => ScRecoilMode.Standard
    };
    public static string Text(ScRecoilMode mode) => mode switch { ScRecoilMode.Off => "off", ScRecoilMode.Realistic => "realistic", _ => "standard" };
    public static ScRecoilMode Parse(string text) => text switch { "off" => ScRecoilMode.Off, "realistic" => ScRecoilMode.Realistic, _ => ScRecoilMode.Standard };

    // ---- multiplayer -----------------------------------------------------------------------------------------------------

    static bool s_registered;
    public static void Register() {
        if (s_registered) return;
        s_registered = true;
        ScNet.OnClient(OpRules, r => {
            byte value = r.Byte();
            if (!r.End || !Enum.IsDefined(typeof(ScRecoilMode), value)) return;
            s_host = (ScRecoilMode)value;
        });
        ScNet.OnServer(OpAsk, (peer, player, r) => { if (r.End) ScNet.TrySendTo(peer, OpRules, w => w.Byte((byte)Local)); });
        ScNet.ClientAccepted += () => { s_host = null; ScNet.Send(OpAsk, _ => { }); };
    }
    /// <summary>The host saved its settings: every client plays by the new value from now on.</summary>
    public static void Announce() {
        if (ScNet.IsHost) ScNet.TryBroadcast(OpRules, w => w.Byte((byte)Local));
    }
    /// <summary>Offline checks: forget what a host said.</summary>
    internal static void ResetHostForTests() => s_host = null;
}
