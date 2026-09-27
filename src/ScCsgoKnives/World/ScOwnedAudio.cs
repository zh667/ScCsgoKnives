using Engine;
using Engine.Audio;
namespace Game;

/// <summary>Only our one-shots; shared buffers remain ContentManager-owned.</summary>
public static class ScOwnedAudio {
    sealed record Entry(Sound Sound,string Category,object Owner,double Start,double Expires);
    static readonly List<Entry> active=new();
    static double reportAt;
    static double retryAt;
    static int lastFrame=-1,played,dropped,replaced,expired,failures,peak,details;
    public const int Maximum=24,WorldMaximum=12,VoiceMaximum=2;
    public static int Count=>active.Count;
    public static int Limit(string category)=>category=="world"?WorldMaximum:category=="voice"?VoiceMaximum:8;
    public static bool Allowed(int total,int categoryCount,string category)=>total<Maximum&&categoryCount<Limit(category);
    public static void StopOwner(object owner){
        if(owner==null)return;
        for(int i=active.Count-1;i>=0;i--)if(ReferenceEquals(active[i].Owner,owner)){active[i].Sound.Dispose();active.RemoveAt(i);replaced++;}
    }
    public static void Tick(){
        if(lastFrame==Time.FrameIndex)return;lastFrame=Time.FrameIndex;Reap(Time.RealTime);
        if(Time.RealTime-reportAt>=10){
            if(played+dropped+replaced+failures>0)KnifeLog.Information($"[CS_AUDIO] active={active.Count} peak={peak} played={played} dropped={dropped} replaced={replaced} expired={expired} failed={failures}; own sources only");
            played=dropped=replaced=expired=failures=peak=0;details=0;reportAt=Time.RealTime;
        }
    }
    static void Reap(double now){
        for(int i=active.Count-1;i>=0;i--){
            var e=active[i];
            if(e.Sound.State==SoundState.Disposed||now>=e.Expires){e.Sound.Dispose();active.RemoveAt(i);expired++;continue;}
            if(e.Sound.State==SoundState.Stopped&&now>=e.Start)e.Sound.Play();
        }
    }
    public static bool Play(string path,float volume,float pitch=0,string category="presentation",object owner=null,bool replace=false,double delay=0){
        Tick();if(!Window.IsActive)return false;
        if(Time.RealTime<retryAt){dropped++;return false;}
        if(replace)StopOwner(owner);
        float gain=Math.Clamp(SettingsManager.SoundsVolume,0,1)*volume;
        if(gain<=AudioManager.MinAudibleVolume)return false;
        if(active.Count(e=>e.Category==category)>=Limit(category)){dropped++;return false;}
        if(active.Count>=Maximum){
            if(category=="world"||category=="voice"){dropped++;return false;}
            var low=active.FindIndex(e=>e.Category=="world");
            if(low<0){dropped++;return false;}
            active[low].Sound.Dispose();active.RemoveAt(low);replaced++;
        }
        Sound sound=null;
        try{
            var buffer=ContentManager.Get<SoundBuffer>(path);
            float rate=MathF.Pow(2,Math.Clamp(pitch,-1,1));
            sound=new Sound(buffer,gain,rate,0,false,true);
            if(sound.m_source==0){sound.Dispose();failures++;retryAt=Time.RealTime+1;return false;}
            double now=Time.RealTime,start=now+Math.Clamp(delay,0,3);
            double seconds=buffer.SamplesCount/(double)Math.Max(1,buffer.SamplingFrequency)/rate;
            active.Add(new(sound,category,owner,start,start+Math.Clamp(seconds+.25,.25,30)));
            if(delay<=0)sound.Play();
            played++;peak=Math.Max(peak,active.Count);
            if(details++<6&&category=="draw")KnifeLog.Information(FormattableString.Invariant($"[CS_AUDIO] draw={path} pitch={sound.Pitch:F3} rateHz={buffer.SamplingFrequency} channels={buffer.ChannelsCount} seconds={seconds:F3} active={active.Count}"));
            return true;
        }catch(Exception e){sound?.Dispose();failures++;retryAt=Time.RealTime+1;KnifeDiagnostics.WarnOnce("owned-audio-"+path,e.Message);return false;}
    }
    public static bool World(SubsystemAudio audio,string path,float volume,float pitch,Vector3 position,float minDistance,bool delay=false,bool voice=false,bool priority=false){
        float distance=audio.CalculateListenerDistance(position);
        return Play(path,volume*audio.CalculateVolume(distance,minDistance),pitch,voice?"voice":priority?"player":"world",delay:delay?audio.CalculateDelay(distance):0);
    }
    public static void Clear(){foreach(var e in active)e.Sound.Dispose();active.Clear();lastFrame=-1;retryAt=0;}
}
