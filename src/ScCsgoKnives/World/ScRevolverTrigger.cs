using Engine;
namespace Game;

/// <summary>R8 Revolver trigger rules and their evidence trail (video-feedback-20260929 R1).
///
/// Primary (cocked shot): Idle -> Cocking on a held primary input once the previous cycle allows it; the shot is
/// committed when the cocking time (the primary cycle time, scaled by growth as before) has passed AND the input
/// was held throughout. Releasing, a menu, a switch or death cancels without a shot and restores the previous
/// ready time, so quick clicks can never ride on an old deadline. The next cocking may begin in the frame after a
/// commit: held continuously the R8 fires once per cycle time, exactly the accepted cadence. Whether the hammer
/// animation can be shown never changes any of this.
/// Alternate (fanned shot): its own input, immediate, on the alternate cycle time; refused while cocking, in a
/// frame that already committed a shot, and for one alternate cycle time after a cocked shot (recovery).
///
/// The trail is bounded (last 256 events per session), costs nothing to read, and goes to the diagnostic trace.</summary>
public static class ScRevolverTrigger {
    public readonly record struct Entry(int Frame,double GameTime,double ActionClock,int Player,string Event,int Rounds,double Deadline,bool Dig,bool Hit,bool Button,long ActionSequence,string Clip);
    const int Capacity=256;
    static readonly Queue<Entry> s_entries=new();
    public static IReadOnlyCollection<Entry> Entries=>s_entries;
    public static void Clear()=>s_entries.Clear();
    public static void Note(ComponentPlayer player,string what,double now,int rounds,double deadline,bool dig,bool hit,bool button){
        var model=player?.Entity?.FindComponent<ComponentFirstPersonModel>();
        var entry=new Entry(Time.FrameIndex,now,KnifeClock.Now,player?.PlayerData?.PlayerIndex??-1,what,rounds,deadline,dig,hit,button,
            KnifeAnimationController.ActionToken(model),KnifeAnimationController.CurrentClip(model));
        if(s_entries.Count>=Capacity)s_entries.Dequeue();
        s_entries.Enqueue(entry);
    }
}
