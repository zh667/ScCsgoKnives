using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Direction of incoming CS damage, adapted from CS2's panorama huddamageindicator: a 300 px box (1080p
/// reference) with four 90-degree wedges at 0/90/180/270 degrees, each with two concentric bands at 86.2–88.9 % and
/// 92.0–99.9 % of a 147 px radius, shifted 5 px outward, washed #cc3300. The CSS "Segment_Bar" child's static clip
/// (45° + 270° sweep) does not overlap its parent wedge, so it is treated as animation-driven and not reproduced.
/// The 0.7 s fade is this project's choice, not CS2 timing.
///
/// Only CS damage delivered through <see cref="AttackBody"/> reports, and only when the target player's health
/// actually dropped: friendly-fire filtering, shields, invulnerability and zero-damage hits never show a mark.
/// The mark keeps the horizontal direction frozen at the hit; it never follows the attacker afterwards.</summary>
public static class ScDamageIndicator {
    public const float Duration=.7f;
    public const int MaxMarks=4;
    public const float ReferenceBox=300,ReferenceHeight=1080,Radius=147,Offset=5;
    static readonly (float Inner,float Outer)[] Bands=[(.862f,.889f),(.920f,.999f)];
    static readonly Color Wash=new(0xcc,0x33,0x00);
    /// <summary>Opacity of a creative hit preview against a real hit.</summary>
    public const float PreviewStrength=.45f;
    public enum Outcome { None, Hurt, CreativePreview }
    sealed class Mark { public Vector2 From; public double Until; public float Strength=1; }
    sealed class Marks { public readonly List<Mark> Items=[]; }
    static readonly ConditionalWeakTable<ComponentPlayer,Marks> s_marks=new();

    /// <summary>Delivers a CS attack exactly as <see cref="ComponentMiner.AttackBody"/> and reports its direction
    /// to a damaged player. The attack itself is not changed.</summary>
    public static void AttackBody(Attackment attack) => Deliver(attack);
    /// <summary>As <see cref="AttackBody"/>, and says what was shown. A hit that hurt shows the full mark. In creative
    /// mode (video-feedback-20260929 R3) a hit that reached the player with damage left after shields and filters,
    /// and was stopped only by creative invulnerability, shows the weaker preview when the player enabled it. Walls
    /// never get here; a shield that stopped everything, disabled friendly fire and invulnerability from any other
    /// source show nothing. Health, aggression and statistics are those of the native attack, untouched.</summary>
    public static Outcome Deliver(Attackment attack) {
        var player=attack?.Target?.FindComponent<ComponentPlayer>();var health=player is null?null:player.ComponentHealth??attack.Target.FindComponent<ComponentHealth>();float before=health?.Health??0;
        // H4: the shot's own feedback sound, chosen from what it did to this target.
        var hits=ScHitSounds.Before(attack);var targetHealth=hits is null?null:attack.Target.FindComponent<ComponentHealth>();float targetBefore=targetHealth?.Health??0;
        var journal=ScAttackJournal.Record?ScAttackJournal.Begin(attack):default;
        ComponentMiner.AttackBody(attack);
        if(ScAttackJournal.Record)ScAttackJournal.End(attack,journal);
        ScHitSounds.After(attack,hits,targetHealth,targetBefore);
        if(player is null||health is null)return Outcome.None;
        double now=player.Project?.FindSubsystem<SubsystemTime>(false)?.GameTime??0;
        if(health.Health<before-1e-6f)return Report(player,-attack.HitDirection,now)?Outcome.Hurt:Outcome.None;
        if(!ScUiSettings.DamageIndicator||!CreativeImmune(player,health)||FriendlyFireOff(attack)||!(attack.AttackPower>0)||!(attack.CalculateInjuryAmount()>0))return Outcome.None;
        return ReportWith(player,-attack.HitDirection,now,PreviewStrength)?Outcome.CreativePreview:Outcome.None;
    }
    static bool CreativeImmune(ComponentPlayer player,ComponentHealth health)=>health.IsInvulnerable&&health.Health>0
        &&player.Project?.FindSubsystem<SubsystemGameInfo>(false)?.WorldSettings.GameMode==GameMode.Creative;
    // The native rule, read without its on-screen message: player against player while the world has friendly fire off.
    static bool FriendlyFireOff(Attackment attack)=>attack.Attacker?.FindComponent<ComponentPlayer>() is not null
        &&attack.Target.Project.FindSubsystem<SubsystemGameInfo>(false)?.WorldSettings.IsFriendlyFireEnabled==false;
    /// <summary>Records a hit coming from <paramref name="towardSource"/> (world space). A direction with no
    /// horizontal component (straight above/below, or standing in the middle of a fire) is not shown.</summary>
    public static bool Report(ComponentPlayer player,Vector3 towardSource,double now) => ReportWith(player,towardSource,now,1);
    // A distinct name: regressions find Report by name.
    public static bool ReportWith(ComponentPlayer player,Vector3 towardSource,double now,float strength) {
        if(player is null||!float.IsFinite(towardSource.X+towardSource.Z))return false;
        // A remote multiplayer client's player: the marks are drawn on that client's screen.
        if(ScNet.IsRemoteDriven(player)){ScNetFeedback.Damage(player,towardSource,strength);return true;}
        var from=towardSource.XZ;if(from.LengthSquared()<.0025f)return false;from=Vector2.Normalize(from);
        var items=s_marks.GetOrCreateValue(player).Items;items.RemoveAll(m=>m.Until<=now);
        // Hits within 45 degrees of an existing mark merge into it (the newest direction wins).
        foreach(var m in items)if(Vector2.Dot(m.From,from)>.7071f){m.From=from;m.Until=now+Duration;m.Strength=Math.Max(m.Strength,strength);return true;}
        if(items.Count>=MaxMarks)items.RemoveAt(0);
        items.Add(new Mark{From=from,Until=now+Duration,Strength=strength});return true;
    }
    public static void Clear(ComponentPlayer player){if(player is not null)s_marks.Remove(player);}
    /// <summary>0 front/top, 1 right, 2 back/bottom, 3 left, for a frozen world direction and the current camera.</summary>
    public static int Sector(Vector2 from,Vector3 cameraForward) {
        var f=cameraForward.XZ;if(f.LengthSquared()<1e-6f)f=new Vector2(0,-1);f=Vector2.Normalize(f);
        var right=new Vector2(-f.Y,f.X);
        float angle=MathF.Atan2(Vector2.Dot(from,right),Vector2.Dot(from,f));
        return ((int)MathF.Round(angle/(MathF.PI/2))%4+4)%4;
    }
    /// <summary>Opacity per sector at <paramref name="now"/>; empty when there is nothing to show.</summary>
    public static float[] Intensities(ComponentPlayer player,Vector3 cameraForward,double now) {
        if(player is null||!s_marks.TryGetValue(player,out var marks)||marks.Items.Count==0)return null;
        marks.Items.RemoveAll(m=>m.Until<=now);if(marks.Items.Count==0)return null;
        var result=new float[4];
        foreach(var m in marks.Items){int s=Sector(m.From,cameraForward);result[s]=Math.Max(result[s],m.Strength*(float)Math.Clamp((m.Until-now)/Duration,0,1));}
        return result;
    }
    public static void Draw(PrimitivesRenderer2D renderer,Camera camera,ComponentPlayer player,double now) {
        if(!ScUiSettings.DamageIndicator||player is null)return;
        if(player.ComponentHealth.Health<=0){Clear(player);return;}
        var alpha=Intensities(player,camera.ViewDirection,now);if(alpha is null)return;
        Vector2 size=camera.ViewportSize,centre=size*.5f;float s=Math.Min(size.X,size.Y)/ReferenceHeight;
        var batch=renderer.FlatBatch(0,DepthStencilState.None,RasterizerState.CullNoneScissor,BlendState.AlphaBlend);
        const int steps=18;
        for(int sector=0;sector<4;sector++){
            if(alpha[sector]<=0)continue;var color=Wash*alpha[sector];
            float axis=sector*MathF.PI/2;var shift=new Vector2(MathF.Sin(axis),-MathF.Cos(axis))*Offset*s;
            foreach(var (inner,outer) in Bands){
                float r0=inner*Radius*s,r1=outer*Radius*s;
                for(int i=0;i<steps;i++){
                    float a0=axis-MathF.PI/4+MathF.PI/2*i/steps,a1=axis-MathF.PI/4+MathF.PI/2*(i+1)/steps;
                    Vector2 d0=new(MathF.Sin(a0),-MathF.Cos(a0)),d1=new(MathF.Sin(a1),-MathF.Cos(a1));
                    var c=centre+shift;batch.QueueQuad(c+d0*r0,c+d0*r1,c+d1*r1,c+d1*r0,0,color);
                }
            }
        }
        batch.TransformTriangles(camera.ViewportMatrix);batch.Flush();
    }
}
