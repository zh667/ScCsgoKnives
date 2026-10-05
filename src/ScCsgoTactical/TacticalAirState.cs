using Engine;
namespace Game;

/// <summary>Jump / in-air animation state of one actor (video-feedback-20260929 R1), shared by the NPC model and the
/// player pose. Advanced at most once per game frame however many cameras or previews animate the actor.
/// Airborne only after a short genuine time off the ground. The standing/moving variant is chosen once, when the
/// actor leaves the ground, and kept until it lands: crossing a speed threshold in mid-air must not restart a clip.
/// Rising has a wide hysteresis band, so a vertical speed hovering around zero cannot flip jump and fall clips.</summary>
public sealed class TacticalAirState {
    public const float Debounce=.12f,MovingSpeed=2,RisingAtTakeoff=.5f,RisingOff=.2f,RisingOn=2;
    float airTime;int frame=int.MinValue;
    public bool Airborne {get;private set;}
    public bool Rising {get;private set;}
    public bool Moving {get;private set;}
    /// <summary>Seconds off the ground, for diagnostics.</summary>
    public float AirTime=>airTime;
    public void Advance(int frameIndex,float dt,bool grounded,Vector3 velocity){
        if(frame==frameIndex)return;
        frame=frameIndex;
        airTime=grounded?0:airTime+Math.Clamp(float.IsFinite(dt)?dt:0,0,.25f);
        bool airborne=airTime>Debounce;float vertical=velocity.Y;
        if(!airborne)Rising=Moving=false;
        else if(!Airborne){Moving=velocity.XZ.Length()>MovingSpeed;Rising=vertical>RisingAtTakeoff;}
        else if(Rising){if(vertical<RisingOff)Rising=false;}
        else if(vertical>RisingOn)Rising=true;
        Airborne=airborne;
    }
    public void Apply(Engine.Animation.AnimationParameters parameters,bool crouch){
        parameters.SetBool("Airborne",Airborne);parameters.SetBool("AirRising",Rising);parameters.SetBool("AirMoving",Moving);parameters.SetBool("Crouch",crouch);
    }
}
