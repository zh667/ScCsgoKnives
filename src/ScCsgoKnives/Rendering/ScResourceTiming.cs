using System.Diagnostics;
namespace Game;

/// <summary>Cold operations only; bounded per-process diagnostics, no frame-path strings.</summary>
public static class ScResourceTiming {
    public const int MobileProtocol=2;
    static readonly HashSet<string> seen=new();
    static int emitted;
    public static Scope Measure(string kind,string resource)=>new(kind,resource);
    public readonly struct Scope:IDisposable {
        readonly string kind,resource;
        readonly long start,bytes;
        internal Scope(string kind,string resource){this.kind=kind;this.resource=resource;start=Stopwatch.GetTimestamp();bytes=GC.GetAllocatedBytesForCurrentThread();}
        public void Dispose(){
            double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if(ms<10)return;
            lock(seen){
                if(emitted>=64||!seen.Add(kind+":"+resource))return;
                emitted++;
            }
            KnifeLog.Information(FormattableString.Invariant($"[CS_RESOURCE] stage={kind} resource={resource} ms={ms:F2} allocKB={(GC.GetAllocatedBytesForCurrentThread()-bytes)/1024d:F1}; inclusive cold operation"));
        }
    }
}
