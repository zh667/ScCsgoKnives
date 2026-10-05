using Engine;
namespace Game;

// Authored adaptation for vanilla's two rigid arms; not a CS2 character skeleton.
public static class ScThirdPersonMotion {
    public static Vector2 Right(ScWeaponAction a) {
        float w=a.Weight, wave=MathF.Sin(a.Progress*MathF.PI)*w;
        return a.Kind switch {
            ScWeaponActionKind.Draw=>new(-.85f*(1-a.Progress)*w,.08f*wave),
            ScWeaponActionKind.Reload=>new(.22f*wave,.12f*wave),
            ScWeaponActionKind.Inspect=>new(.25f*wave,-.2f*wave),
            ScWeaponActionKind.Shoot=>new(.10f*wave,0),
            ScWeaponActionKind.Slash=>new(.75f*wave,.3f*wave),
            ScWeaponActionKind.Grenade or ScWeaponActionKind.Prepare=>new(.85f*wave,0),
            _=>Vector2.Zero
        };
    }
    public static Vector2 Left(ScWeaponAction a) {
        float wave=MathF.Sin(a.Progress*MathF.PI)*a.Weight;
        return a.Kind switch {
            ScWeaponActionKind.Reload=>new(-.8f*wave,.4f*wave),
            ScWeaponActionKind.Attach or ScWeaponActionKind.Detach=>new(.22f*wave,.2f*wave),
            _=>Vector2.Zero
        };
    }
    /// <summary>The throw of a vanilla human's two rigid arms (video-feedback-20260929 R2), by stage of the gameplay
    /// timeline: the hands meet at the chest and the left pulls the pin (or lights the rag) away; the arm is held
    /// cocked - up and back for a strong throw, down and back for a weak one; it swings through and lets go exactly
    /// at the release, follows through and returns. Angles are vanilla hand angles (raise: pi/2 forward, pi up).
    /// Authored for rigid arms (估计), not CS2 data; the CS actors use CS2's own world clips instead.</summary>
    public readonly record struct ThrowPose(bool Active,Vector2 Right,Vector2 Left,bool LeftUsed,bool Direct);
    public const float ChestRaise=1.15f,ChestSwing=.55f,HighCock=3.35f,HighRelease=1.85f,HighEnd=.95f,LowCock=-.75f,LowRelease=1.05f,LowEnd=1.55f;
    static float Ease(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
    /// <summary>The left hand's authored reach for the pin at the chest; the game and the offline checks replace it with
    /// the angles that put this model's left fist on the throwable (<see cref="PullLeft"/>).</summary>
    public static readonly Vector2 ChestLeft=new(ChestRaise,-ChestSwing);
    /// <summary>Left arm while pulling the pin: to <paramref name="reach"/>, the pull in the middle, away with the pin at the end.</summary>
    public static Vector2 PullLeft(ScThrowPhase phase,Vector2 reach){
        float to=Ease(phase.Pull/.35f),away=Ease((phase.Pull-.6f)/.4f);
        return Vector2.Lerp(Vector2.Lerp(new Vector2(.2f,0),reach,to),new Vector2(.75f,.15f),away);
    }
    public static ThrowPose Throw(ScThrowPhase phase,ScThirdPersonStance stance) {
        if(!phase.Active)return default;
        var rest=new Vector2(stance.RightRaise,stance.RightSwing);var chest=new Vector2(ChestRaise,ChestSwing);
        var cocked=new Vector2(phase.Low?LowCock:HighCock,phase.Low?.05f:-.1f);
        if(phase.Stage==0){
            // In to the chest, the pull in the middle, the left hand away with the pin at the end.
            var right=Vector2.Lerp(rest,chest,Ease(phase.Pull/.35f));
            return new(true,right,PullLeft(phase,ChestLeft),true,false);
        }
        if(phase.Stage==1){
            float settle=Ease(phase.Hold/.25f);
            return new(true,Vector2.Lerp(chest,cocked,settle),Vector2.Lerp(new Vector2(.75f,.15f),new Vector2(.35f,.05f),settle),true,false);
        }
        var release=new Vector2(phase.Low?LowRelease:HighRelease,.1f);var end=new Vector2(phase.Low?LowEnd:HighEnd,.2f);
        // A quick throw (the button let go before the pull was through, phase.Pull < 1) swings from where the arm had got
        // to, not from a cocked pose it never reached: no arm raised first, only the throw (quick-throw-20261002).
        var from=phase.Pull<1?Vector2.Lerp(Vector2.Lerp(rest,chest,Ease(phase.Pull/.35f)),cocked,Ease(phase.Pull)):cocked;
        if(!phase.Released&&phase.Wind<1)return new(true,Vector2.Lerp(from,release,Ease(phase.Wind)*Ease(phase.Wind)+phase.Wind*(1-Ease(phase.Wind))),new Vector2(.35f,.05f),true,true);
        // Through the release point and on, then back to the stance in the last third.
        float through=Math.Clamp(phase.Follow/.35f,0,1),back=Ease((phase.Follow-.55f)/.45f);
        var swing=Vector2.Lerp(release,end,1-(1-through)*(1-through));
        return new(true,Vector2.Lerp(swing,rest,back),Vector2.Lerp(new Vector2(.35f,.05f),new Vector2(.1f,0),back),back<1,true);
    }
    /// <summary>Planting a C4 with a vanilla human's two rigid arms (r2-c4-completion-20260929), on the gameplay clock of
    /// the plant: both hands go down in front of the (crouched) body, the right one carrying the bomb, the left one
    /// tapping the keypad while CS2's key presses sound; in the last 0.4 s before the commit the right hand sets the
    /// bomb down, then both return. Authored for rigid arms (估计), not CS2 data; CT/T actors use CS2's own clip.</summary>
    public const float PlantReach=.55f,PlantSwing=.3f,PlantSet=.25f,PlantTap=.07f;
    public static ThrowPose Plant(ScPlantPhase phase,ScThirdPersonStance stance) {
        if(!phase.Active)return default;
        var rest=new Vector2(stance.RightRaise,stance.RightSwing);var restLeft=new Vector2(.1f,0);
        var down=new Vector2(PlantReach,PlantSwing);var keypad=new Vector2(PlantReach+.12f,-PlantSwing);
        if(!phase.Placed){
            float reach=Ease(phase.Seconds/.4f),set=Ease((phase.Seconds-(ScPlantPhase.PlantSeconds-.4f))/.4f);
            float tap=phase.Seconds is > .6f and < 2.3f?PlantTap*MathF.Max(0,MathF.Sin((phase.Seconds-.6f)*MathF.PI*2*3)):0;
            return new(true,Vector2.Lerp(rest,down,reach)-new Vector2(PlantSet*set,0),Vector2.Lerp(restLeft,keypad,reach)-new Vector2(tap,0),true,true);
        }
        float back=Ease(phase.Recovery);
        return new(true,Vector2.Lerp(down-new Vector2(PlantSet,0),rest,back),Vector2.Lerp(keypad,restLeft,back),back<1,true);
    }
    public static Matrix WeaponRotation(ScWeaponAction a) {
        float wave=MathF.Sin(a.Progress*MathF.PI)*a.Weight;
        return a.Kind switch {
            ScWeaponActionKind.Inspect=>Matrix.CreateRotationZ(.65f*wave*MathF.Sin(a.Progress*MathF.PI*2))*Matrix.CreateRotationY(.35f*wave),
            ScWeaponActionKind.Reload=>Matrix.CreateRotationZ(-.18f*wave)*Matrix.CreateRotationX(.2f*wave),
            ScWeaponActionKind.Draw=>Matrix.CreateRotationX(-.6f*(1-a.Progress)*a.Weight),
            _=>Matrix.Identity
        };
    }
}
