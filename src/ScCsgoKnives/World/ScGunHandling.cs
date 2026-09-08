using System.Text.Json;
using Engine;
namespace Game;

/// <summary>Approved P1a handling values. Presentation and real shots share these;
/// stored ammunition, life, skin and charge remain untouched.</summary>
public static class ScGunHandling {
    public sealed class Mode {
        public float BaseCone { get; set; }
        public float MovingExtra { get; set; }
        public float CrouchingCone { get; set; }
        public float JumpExtra { get; set; }
        public float BloomPerShot { get; set; }
        public float BloomMax { get; set; }
        public float BloomRecoverySeconds { get; set; }
        public float KickPitch { get; set; }
        public float KickYaw { get; set; }
        public float CameraRecoveryT90 { get; set; }
    }
    public sealed class Gun {
        public float Range { get; set; }
        public float FalloffStart { get; set; }
        public float FalloffFloor { get; set; }
        public float? MidDistance { get; set; }
        public float? MidMultiplier { get; set; }
        public string Alternate { get; set; }
        public Dictionary<string, Mode> Modes { get; set; }
    }
    sealed class File { public int Version { get; set; } public Dictionary<string,Gun> Guns { get; set; } }
    static readonly Dictionary<string,Gun> s_guns = Load();
    static Dictionary<string,Gun> Load() {
        using var stream=typeof(ScGunHandling).Assembly.GetManifestResourceStream("Game.AnimationData.gun_handling.json");
        var file=JsonSerializer.Deserialize<File>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(file?.Version!=1 || file.Guns?.Count!=GunSpec.All.Length) throw new InvalidOperationException("Invalid gun handling catalog");
        foreach(var spec in GunSpec.All) {
            if(!file.Guns.TryGetValue(spec.Name,out var g) || !(g.Range>0 && g.Range<=128) || g.FalloffStart<0 || g.FalloffStart>g.Range
                || !(g.FalloffFloor>0 && g.FalloffFloor<=1) || g.Modes?.Count!=2) throw new InvalidOperationException("Invalid handling: "+spec.Name);
            foreach(string key in new[]{"0","1"}) {
                if(!g.Modes.TryGetValue(key,out var m) || !float.IsFinite(m.BaseCone+m.MovingExtra+m.CrouchingCone+m.JumpExtra+m.BloomPerShot+m.BloomMax+m.BloomRecoverySeconds+m.KickPitch+m.KickYaw+m.CameraRecoveryT90)
                    || m.BaseCone<0 || m.MovingExtra<0 || m.CrouchingCone<0 || m.CrouchingCone>m.BaseCone || m.JumpExtra<0 || m.BloomPerShot<0 || m.BloomMax<0
                    || (m.BloomMax>0 && m.BloomRecoverySeconds<=0) || m.KickPitch<0 || m.KickYaw<0 || m.CameraRecoveryT90<0) throw new InvalidOperationException("Invalid handling mode: "+spec.Name);
            }
        }
        return file.Guns;
    }
    public static Gun For(string asset)=>asset is not null && s_guns.TryGetValue(asset,out var g)?g:null;
    public static bool Alternate(GunSpec spec,bool scoped,bool silenced,bool burst,bool alternateFire)=>
        spec.ZoomLevels.Length>0 ? scoped : spec.HasSilencer ? silenced : spec.HasBurstMode ? burst : spec.CycleSecondsAlternate>0 && alternateFire;
    public static Mode ForMode(string asset,bool alternate)=>For(asset)?.Modes[alternate && For(asset).Alternate is not null?"1":"0"];
    public static float MoveFactor(float horizontalSpeed)=>float.IsFinite(horizontalSpeed)?Math.Clamp((horizontalSpeed-.5f)/4,0,1):1;
    public static float Falloff(Gun gun,float distance) {
        if(!float.IsFinite(distance) || distance>gun.Range) return 0;
        if(distance<=gun.FalloffStart) return 1;
        if(gun.MidDistance is float mid && gun.MidMultiplier is float middle) {
            if(distance<=mid) return MathUtils.Lerp(1,middle,(distance-gun.FalloffStart)/(mid-gun.FalloffStart));
            return MathUtils.Lerp(middle,gun.FalloffFloor,(distance-mid)/(gun.Range-mid));
        }
        return MathUtils.Lerp(1,gun.FalloffFloor,(distance-gun.FalloffStart)/(gun.Range-gun.FalloffStart));
    }
    public static Vector3 Scatter(Vector3 direction,float coneDegrees,float radiusRandom,float angleRandom) {
        var forward=Vector3.Normalize(direction);
        if(coneDegrees<=0) return forward;
        var side=Vector3.Normalize(Vector3.Cross(forward,Math.Abs(forward.Y)<.9f?Vector3.UnitY:Vector3.UnitX));
        var up=Vector3.Cross(side,forward);
        float a=MathUtils.DegToRad(coneDegrees)*MathF.Sqrt(Math.Clamp(radiusRandom,0,1));
        float phi=2*MathF.PI*angleRandom;
        return Vector3.Normalize(forward*MathF.Cos(a)+(side*MathF.Cos(phi)+up*MathF.Sin(phi))*MathF.Sin(a));
    }
}

/// <summary>One gun instance's numbers as they actually take effect: the survival balance, the approved handling
/// table and this gun's applied growth level combined once, in one place, so combat, the ammunition HUD, the
/// attribute page and the save validation can never disagree about a Negev that holds 225 rounds.
///
/// Growth is read from the record's <c>AppliedGrowthLevel</c>, never from its kill count: a level that has been
/// earned but deliberately not applied yet must not change anything.</summary>
public readonly record struct EffectiveGunStats(float Power,float Range,int Capacity,int MaxDurability,float CycleSeconds,int Pellets,float HeadMultiplier,ScGunHandling.Mode Handling,
                                                int Level,bool UnlimitedRange,float AngleScale,float RechargeSeconds,int Variant) {
    public static EffectiveGunStats Resolve(GunSpec spec,int value,bool alternate) => ResolveLevel(spec,value,alternate,LevelOf(value));
    /// <summary>The applied level of the gun this item value points at; 0 for a fresh template or unreadable data.</summary>
    public static int LevelOf(int value) {
        int data=Terrain.ExtractData(value);
        return GunSpec.TryGetSnapshot(data,out var s) ? s.Level : 0;
    }
    /// <summary>The same numbers at an arbitrary level, for the attribute page's base / current / next columns.
    /// A preview never touches the record.</summary>
    public static EffectiveGunStats ResolveLevel(GunSpec spec,int value,bool alternate,int level) {
        int data=Terrain.ExtractData(value);
        int variant=Array.IndexOf(GunSpec.All,spec);
        if(variant<0) variant=GunSpec.GetVariant(data);
        int L=ScGunGrowth.Clamp(level);
        float baseRange=ScGunplaySettings.Enabled && ScGunHandling.For(spec.Name) is {} g ? g.Range : spec.RangeBlocks;
        // The gun's own ceiling while previewing the level it is actually carrying; the rule's ceiling for any
        // other level, so a "next level" column is not quoting today's number.
        bool known=GunSpec.TryGetSnapshot(data,out var s);
        int maxDurability=known && s.Level==L ? s.MaxDurability : ScGunGrowth.MaxDurability(variant,L);
        return new(ScSurvivalBalance.Power(spec.Name)*ScGunGrowth.DamageMultiplier(L),
            ScGunGrowth.Range(variant,L,baseRange),
            ScGunGrowth.Capacity(variant,L),maxDurability,spec.CycleSeconds,spec.Pellets,ScHeadshot.MultiplierFor(spec),
            ScGunplaySettings.Enabled?ScGunHandling.ForMode(spec.Name,alternate):null,
            L,ScGunGrowth.UnlimitedRange(variant,L),ScGunGrowth.AngleScale(L),ScGunGrowth.RechargeSeconds(spec,L),variant);
    }
    /// <summary>Damage of one pellet at a distance: the survival close-range power, this level's multiplier and
    /// the distance curve, divided across the pellets. At Lv10 a normal bullet gun has no distance falloff left.</summary>
    public float PelletPower(GunSpec spec,float distance) => Power*Falloff(spec,distance)/Math.Max(1,Pellets);
    /// <summary>The distance multiplier in force. Growth stretches the curve's own nodes by the same factor the
    /// range grew by, so a level never both extends the reach and leaves the damage collapsing at the old node.</summary>
    public float Falloff(GunSpec spec,float distance) {
        if(UnlimitedRange) return 1;
        float scale=ScGunGrowth.RangeScale(Variant,Level);
        float atBase=scale>0?distance/scale:distance;
        return ScGunplaySettings.Enabled && ScGunHandling.For(spec.Name) is {} g ? ScGunHandling.Falloff(g,atBase) : ScSurvivalBalance.Falloff(spec,atBase);
    }
}

/// <summary>One gun's transient shot bloom. Time is game time, never render-camera time.</summary>
public sealed class ScGunBloom {
    public float Value { get; private set; }
    double m_lastShot=double.NegativeInfinity,m_lastUpdate=double.NaN;
    float m_rate;
    public float At(double now) {
        if(!double.IsFinite(now)) return Value;
        if(double.IsFinite(m_lastUpdate) && now<m_lastUpdate) { Value=0;m_lastShot=double.NegativeInfinity; }
        double from=Math.Max(double.IsNaN(m_lastUpdate)?now:m_lastUpdate,m_lastShot+.12);
        if(now>from) Value=Math.Max(0,Value-(float)(now-from)*m_rate);
        m_lastUpdate=now;return Value;
    }
    public void Fired(ScGunHandling.Mode mode,double now) {
        At(now);Value=Math.Min(mode.BloomMax,Value+mode.BloomPerShot);
        m_rate=mode.BloomRecoverySeconds>0?mode.BloomMax/mode.BloomRecoverySeconds:0;
        m_lastShot=m_lastUpdate=now;
    }
}

/// <summary>One player's physical stance. Retained across weapon switches to prevent
/// switching while airborne from receiving another landing grace period.</summary>
public sealed class ScGunStance {
    double m_leaveAt=double.NaN,m_landAt=double.NegativeInfinity,m_last=double.NaN;
    public bool Airborne { get; private set; }
    public float LandingFactor { get; private set; }
    public float AimBlend { get; private set; }
    float m_aimFrom,m_aimTarget; double m_aimAt;
    public void Update(double now,bool grounded,bool jump,bool scoped) {
        if(!double.IsFinite(now)) return;
        if(double.IsFinite(m_last) && now<m_last) { m_leaveAt=double.NaN;m_landAt=double.NegativeInfinity;Airborne=false;AimBlend=0;m_aimTarget=0; }
        if(grounded && !jump) {
            if(Airborne) m_landAt=now;
            m_leaveAt=double.NaN;Airborne=false;
        } else {
            if(double.IsNaN(m_leaveAt)) m_leaveAt=now;
            Airborne=jump || now-m_leaveAt>=.1;
        }
        LandingFactor=Airborne?0:.4f*Math.Clamp(1-(float)(now-m_landAt)/.18f,0,1);
        AimBlend=MathUtils.Lerp(m_aimFrom,m_aimTarget,Math.Clamp((float)(now-m_aimAt)/.15f,0,1));
        float target=scoped?1:0;
        if(target!=m_aimTarget) {m_aimFrom=AimBlend;m_aimTarget=target;m_aimAt=now;}
        m_last=now;
    }
    public float Cone(ScGunHandling.Mode mode,ScGunHandling.Mode hip, bool hasScope,float horizontalSpeed,float crouch,bool waterOrLadder,float bloom) {
        return ExplainCone(mode,hip,hasScope,horizontalSpeed,crouch,waterOrLadder,bloom).Total;
    }
    public readonly record struct ConeParts(float Base,float Move,float Air,float Bloom) { public float Total=>Base+Move+Air+Bloom; }
    public ConeParts ExplainCone(ScGunHandling.Mode mode,ScGunHandling.Mode hip, bool hasScope,float horizontalSpeed,float crouch,bool waterOrLadder,float bloom) {
        float b=MathUtils.Lerp(mode.BaseCone,mode.CrouchingCone,Math.Clamp(crouch,0,1));
        float movement=mode.MovingExtra,air=mode.JumpExtra;
        if(hasScope) {
            float h=MathUtils.Lerp(hip.BaseCone,hip.CrouchingCone,Math.Clamp(crouch,0,1));
            b=MathUtils.Lerp(h,b,AimBlend);movement=MathUtils.Lerp(hip.MovingExtra,movement,AimBlend);air=MathUtils.Lerp(hip.JumpExtra,air,AimBlend);
        }
        return new(b,movement*(waterOrLadder?1:ScGunHandling.MoveFactor(horizontalSpeed)),waterOrLadder?0:air*(Airborne?1:LandingFactor),bloom);
    }
}
