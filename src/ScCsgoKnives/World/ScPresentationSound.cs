using Engine.Audio;
namespace Game;

/// <summary>Visible first-person mechanics use the animation clock, not simulation-speed filtered world audio.</summary>
public static class ScPresentationSound {
    static readonly Engine.Random s_random=new();
    public static void Play(string name,float volume=1,float pitch=0) {
        if(!Engine.Window.IsActive)return;
        float gain=Math.Clamp(SettingsManager.SoundsVolume,0,1)*volume;
        if(gain<=AudioManager.MinAudibleVolume)return;
        if(Cs2SoundVariants.All.TryGetValue(name,out int count))name+="_"+s_random.Int(1,count);
        try {new Sound(ContentManager.Get<SoundBuffer>("Audio/ScCsgoKnives/"+name),gain,MathF.Pow(2,pitch),0,false,true).Play();}
        catch(Exception e){KnifeDiagnostics.WarnOnce("presentation-sound/"+name,"Animation sound "+name+": "+e.Message);}
    }
}
