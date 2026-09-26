using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Engine;
using GameEntitySystem;
namespace Game;

// Diagnostic timings are inclusive CPU wall times, not GPU timers. No world data or files.
public static class ScTacticalPerformance {
    public const string Revision="crowd-gpu-20260926";
    public enum Stage { Placement, EntityCreate, Configure, AddEntity, ModelLoad, ModelSet,
        AnimationCache, ActionsInit, EnemyAI, CompanionAI, Animate, Bones, Extras, WeaponResolve, WeaponBuild, WeaponDraw, WeaponUpload, WeaponSubmit, Director, Count }
    static readonly ConditionalWeakTable<Project,Session> sessions=new();
    static Session For(Project project)=>project==null?null:sessions.GetValue(project,_=>new Session());
    public static Scope Measure(Project project,Stage stage,string resource=null)=>new(For(project),stage,resource);
    public static void Start(Project project){For(project);Log.Information($"[CS_PERF] build={Revision}; tacticalMvid={typeof(ScTacticalPerformance).Module.ModuleVersionId}; interval=10s; timings=inclusive CPU wall ms; engineFrame includes non-CS work/vsync; GPU not measured");}
    public static void Frame(Project project,int enemies,int companions)=>For(project)?.Frame(Time.FrameIndex,Time.FrameDuration*1000,Time.CpuFrameDuration*1000,enemies,companions);
    public static void Finish(Project project){if(project!=null&&sessions.TryGetValue(project,out var s)){s.Report(true);sessions.Remove(project);}}
    public static SpawnTrace Spawn(Project project,string kind,int count)=>new(For(project),kind,count);

    public readonly struct Scope : IDisposable {
        readonly Session session;readonly Stage stage;readonly long started,allocated;readonly string resource;
        internal Scope(Session session,Stage stage,string resource){this.session=session;this.stage=stage;this.resource=resource;started=Stopwatch.GetTimestamp();allocated=session==null?0:GC.GetAllocatedBytesForCurrentThread();}
        public void Dispose(){if(session!=null)session.Add(stage,Stopwatch.GetTimestamp()-started,GC.GetAllocatedBytesForCurrentThread()-allocated,resource);}
    }
    public sealed class SpawnTrace : IDisposable {
        readonly Session session;readonly string kind;readonly int count,id;readonly long start=Stopwatch.GetTimestamp();readonly bool detail;
        readonly long[] before;
        public bool Success;
        internal SpawnTrace(Session session,string kind,int count){this.session=session;this.kind=kind;this.count=count;if(session==null)return;
            session.requests++;id=++session.sequence;detail=session.AllowDetail(start);
            if(detail){before=session.Timings();Log.Information($"[CS_PERF] spawn begin id={id} kind={kind} requested={count}");}else session.suppressed++;
        }
        public void Dispose(){if(session==null)return;if(!Success)session.failed++;
            if(detail){var b=new StringBuilder(FormattableString.Invariant($"[CS_PERF] spawn end id={id} kind={kind} requested={count} success={Success} totalMs={Milliseconds(Stopwatch.GetTimestamp()-start):F2}"));
                session.AppendDifference(b,before);Log.Information(b.ToString());}}
    }
    static double Milliseconds(long ticks)=>ticks*(1000d/Stopwatch.Frequency);
    struct Counter {public long calls,ticks,max,bytes;public string maxResource;}
    internal sealed class Session {
        readonly Counter[] counters=new Counter[(int)Stage.Count];
        long reportAt=Stopwatch.GetTimestamp(),detailAt=long.MinValue;
        int lastFrame=-1,frames,over50,over100,over250,enemies,companions,peak;
        double frameMs,cpuMs,maxFrame;
        int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
        internal int requests,failed,suppressed,sequence;
        internal long[] Timings()=>counters.Select(c=>c.ticks).ToArray();
        internal void AppendDifference(StringBuilder b,long[] before){for(int i=0;i<counters.Length;i++){long d=counters[i].ticks-before[i];if(d>0)b.Append(CultureInfo.InvariantCulture,$" {(Stage)i}Ms={Milliseconds(d):F2}");}}
        internal void Add(Stage stage,long ticks,long bytes,string resource){ref var c=ref counters[(int)stage];c.calls++;c.ticks+=ticks;if(ticks>c.max){c.max=ticks;c.maxResource=resource;}c.bytes+=Math.Max(0,bytes);}
        internal bool AllowDetail(long now){if(detailAt!=long.MinValue&&Milliseconds(now-detailAt)<1000)return false;detailAt=now;return true;}
        internal void Frame(int index,double ms,double cpu,int enemyCount,int companionCount){
            enemies=enemyCount;companions=companionCount;peak=Math.Max(peak,enemies+companions);
            if(index!=lastFrame){lastFrame=index;frames++;frameMs+=ms;cpuMs+=cpu;maxFrame=Math.Max(maxFrame,ms);if(ms>=50)over50++;if(ms>=100)over100++;if(ms>=250)over250++;}
            if(Milliseconds(Stopwatch.GetTimestamp()-reportAt)>=10000)Report(false);
        }
        internal void Report(bool final){
            long now=Stopwatch.GetTimestamp();bool active=peak>0||requests>0||counters[(int)Stage.ModelLoad].calls>0;
            int current0=GC.CollectionCount(0),current1=GC.CollectionCount(1),current2=GC.CollectionCount(2);
            if(active){
                var b=new StringBuilder(1600);
                b.Append(CultureInfo.InvariantCulture,$"[CS_PERF] summary final={final} windowMs={Milliseconds(now-reportAt):F0} enemies={enemies} companions={companions} peakActors={peak} frames={frames} engineAvgMs={frameMs/Math.Max(1,frames):F2} engineMaxMs={maxFrame:F2} engineCpuAvgMs={cpuMs/Math.Max(1,frames):F2} over50/100/250={over50}/{over100}/{over250} spawnRequests={requests} spawnFailed={failed} detailSuppressed={suppressed} managedMB={GC.GetTotalMemory(false)/1048576d:F1} gc={current0-gc0}/{current1-gc1}/{current2-gc2}");
                for(int i=0;i<counters.Length;i++){var c=counters[i];if(c.calls==0)continue;
                    b.Append(CultureInfo.InvariantCulture,$" | {(Stage)i}:n={c.calls},totalMs={Milliseconds(c.ticks):F2},maxMs={Milliseconds(c.max):F2},allocKB={c.bytes/1024d:F1}");if(c.maxResource!=null)b.Append(",maxFor=").Append(c.maxResource);}
                Log.Information(b.ToString());
            }
            Array.Clear(counters);frames=over50=over100=over250=peak=requests=failed=suppressed=0;frameMs=cpuMs=maxFrame=0;
            gc0=current0;gc1=current1;gc2=current2;reportAt=now;
        }
    }
}
