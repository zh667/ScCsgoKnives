using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Engine;
namespace Game;

/// <summary>Local, bounded observation only: never draws random numbers, queries
/// targets again, mutates guns, or lets an output failure interrupt a real shot.</summary>
public sealed class ScGunDiagnostics {
    public const int DefaultByteBudget=8*1024*1024;
    public const double DetailInterval=.5, SummaryInterval=10;
    public sealed class Context {
        public string Gun, Preset;
        public int Player, Instance, Frame, Pellets, AmmoBefore, AmmoAfter, DurabilityAfter, GunNumbers;
        public double Time;
        public bool Touch, Creative, Scoped, Silenced, Burst, Alternate, Airborne, FluidOrLadder;
        public float SpeedXZ, Crouch, AimBlend, LandingFactor, Cone, Range, NearPower, BloomBefore, BloomAfter, FrameMs;
        public float KickPitchDegrees, KickYawDegrees;
        /// <summary>Counter and growth state of the gun that fired, so a log can tell a level's effect from a preset's.</summary>
        public bool Counter, UnlimitedRange;
        public int Level;
        public int PendingLevel;
        public string GrowthRule;
        public long Kills;
        public ScGunStance.ConeParts? Components;
    }
    public sealed class Shot {
        public Context State;
        public long Sequence;
        public readonly ScGunHitTest.Trace Trace=new();
        public readonly ScGunRange.BulletTrace VegetationTrace = new();
        public int PelletsSeen, Head, Body, Terrain, RangeEnd, FallbackHits, LogicalHits;
        public float MinDistance=float.PositiveInfinity, MaxDistance;
        public double MinAngle=double.PositiveInfinity, MaxAngle, AngleSquares, TraceMs, SubmittedPower;
        public int ConeViolations, RangeViolations, DamagedTargets, KilledTargets;
        public double ObservedHealthLoss;
        internal bool Finished;
        public void Pellet(Vector3 aim,Vector3 actual,int outcome,float distance,bool fallback,bool logical,double traceMs,double submittedPower) {
            // No extra sample/RNG and no second raycast; observe the actual returned ray.
            double dot=(double)aim.X*actual.X+(double)aim.Y*actual.Y+(double)aim.Z*actual.Z;
            double la=Math.Sqrt((double)aim.X*aim.X+(double)aim.Y*aim.Y+(double)aim.Z*aim.Z);
            double lb=Math.Sqrt((double)actual.X*actual.X+(double)actual.Y*actual.Y+(double)actual.Z*actual.Z);
            double angle=Math.Acos(Math.Clamp(dot/Math.Max(la*lb,1e-20),-1,1))*180/Math.PI;
            PelletsSeen++;MinAngle=Math.Min(MinAngle,angle);MaxAngle=Math.Max(MaxAngle,angle);AngleSquares+=angle*angle;
            if(!double.IsFinite(angle) || angle>State.Cone+.003) ConeViolations++;
            if(!float.IsFinite(distance) || distance<0 || distance>State.Range+.001f) RangeViolations++;
            switch(outcome) { case 0:Head++;break;case 1:Body++;break;case 2:Terrain++;break;default:RangeEnd++;break; }
            if(outcome<=1) {MinDistance=Math.Min(MinDistance,distance);MaxDistance=Math.Max(MaxDistance,distance);}
            if(fallback)FallbackHits++;if(logical)LogicalHits++;
            TraceMs+=traceMs;SubmittedPower+=submittedPower;
        }
        public void Health(float before,float after) {
            if(!float.IsFinite(before+after))return;
            if(after<before){DamagedTargets++;ObservedHealthLoss+=before-after;}
            if(before>0 && after<=0)KilledTargets++;
        }
    }
    sealed class Batch {
        public string Gun, Preset;
        public long First, Last, Shots, WithGeometryHit, Pellets, Head, Body, Terrain, RangeEnd, Fallback, Logical, Suppressed;
        public long Ads, Moving, Air, Crouching, Touch, Creative, ConeErrors, RangeErrors, PelletErrors, DamagedTargets, KilledTargets;
        public long Candidates, PreciseTests, PreciseMisses, FallbackCandidates;
        public double Begin, End, ConeSum, ConeMin=double.PositiveInfinity,ConeMax,AngleSquares,AngleMax,TraceMs,TraceMax,SubmittedPower,HealthLoss;
        public double DistanceMin=double.PositiveInfinity,DistanceMax;
        public void Add(Shot s,bool detail) {
            var c=s.State;if(Shots==0){First=s.Sequence;Begin=c.Time;Gun=c.Gun;Preset=c.Preset;}
            Last=s.Sequence;End=c.Time;Shots++;Pellets+=s.PelletsSeen;
            if(s.Head+s.Body>0)WithGeometryHit++;
            Head+=s.Head;Body+=s.Body;Terrain+=s.Terrain;RangeEnd+=s.RangeEnd;Fallback+=s.FallbackHits;Logical+=s.LogicalHits;
            if(!detail)Suppressed++;if(c.Scoped)Ads++;if(c.SpeedXZ>.5)Moving++;if(c.Airborne)Air++;if(c.Crouch>0)Crouching++;if(c.Touch)Touch++;if(c.Creative)Creative++;
            ConeSum+=c.Cone;ConeMin=Math.Min(ConeMin,c.Cone);ConeMax=Math.Max(ConeMax,c.Cone);
            AngleSquares+=s.AngleSquares;AngleMax=Math.Max(AngleMax,s.MaxAngle);TraceMs+=s.TraceMs;TraceMax=Math.Max(TraceMax,s.TraceMs);
            SubmittedPower+=s.SubmittedPower;HealthLoss+=s.ObservedHealthLoss;DamagedTargets+=s.DamagedTargets;KilledTargets+=s.KilledTargets;
            DistanceMin=Math.Min(DistanceMin,s.MinDistance);DistanceMax=Math.Max(DistanceMax,s.MaxDistance);
            Candidates+=s.Trace.BroadCandidates;PreciseTests+=s.Trace.PreciseTests;PreciseMisses+=s.Trace.PreciseMisses;FallbackCandidates+=s.Trace.FallbackCandidates;
            ConeErrors+=s.ConeViolations;RangeErrors+=s.RangeViolations;if(c.Pellets!=s.PelletsSeen)PelletErrors++;
        }
    }
    static readonly JsonSerializerOptions JsonOptions=new(){IncludeFields=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    readonly Action<string> m_write;
    readonly string m_mode,m_session=Guid.NewGuid().ToString("N")[..8];
    readonly int m_budget;
    readonly Dictionary<string,Batch> m_batches=[];
    double m_nextDetail=double.NegativeInfinity,m_nextSummary=double.NaN;
    long m_sequence;
    int m_bytes;bool m_stopped;
    public bool Active=>m_mode!="off"&&!m_stopped;
    public ScGunDiagnostics(string mode,Action<string> write=null,int byteBudget=DefaultByteBudget) {
        m_mode=ScGunplaySettings.DiagnosticMode(mode);m_write=write ?? KnifeLog.Information;m_budget=Math.Max(0,byteBudget);
        if(Active)Emit(new {type="session",session=m_session,version="0.39.1",mode=m_mode,detailInterval=DetailInterval,summaryInterval=SummaryInterval,
            byteBudget=m_budget,scope="local world / gun; all completed shots in summaries; detailed shots are sampled; no world coordinates or player names"});
    }
    static double R(double x)=>double.IsFinite(x)?Math.Round(x,5):0;
    static double? Optional(double x)=>double.IsFinite(x)?R(x):null;
    bool Emit(object value) {
        if(!Active)return false;
        try {
            string line="[GUN_DIAG] "+JsonSerializer.Serialize(value,JsonOptions);
            int bytes=Encoding.UTF8.GetByteCount(line)+2;
            if(m_bytes+bytes>m_budget){m_stopped=true;m_write("[GUN_DIAG] {\"type\":\"budget_exhausted\",\"message\":\"diagnostics stopped for this world session; gameplay unchanged\"}");return false;}
            m_bytes+=bytes;m_write(line);return true;
        } catch {m_stopped=true;return false;}
    }
    public Shot Begin(Context state) {
        if(!Active)return null;
        try {if(double.IsNaN(m_nextSummary))m_nextSummary=state.Time+SummaryInterval;return new Shot{State=state,Sequence=++m_sequence};}
        catch {m_stopped=true;return null;}
    }
    public static long Timestamp()=>Stopwatch.GetTimestamp();
    public static double ElapsedMs(long started)=>(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
    public void Complete(Shot shot) {
        if(shot is null || shot.Finished || !Active)return;
        try {
            shot.Finished=true;var c=shot.State;
            bool sampled=m_mode=="sampled" && c.Time>=m_nextDetail;
            if(sampled) {
                m_nextDetail=c.Time+DetailInterval;
                sampled=Emit(new {type="shot",session=m_session,sequence=shot.Sequence,state=c,
                    outcomes=new {shot.PelletsSeen,shot.Head,shot.Body,shot.Terrain,shot.RangeEnd,shot.LogicalHits,shot.FallbackHits},
                    angleDeg=new {min=Optional(shot.MinAngle),max=R(shot.MaxAngle),rms=R(Math.Sqrt(shot.AngleSquares/Math.Max(1,shot.PelletsSeen)))},
                    geometryDistance=new {min=Optional(shot.MinDistance),max=shot.Head+shot.Body>0?R(shot.MaxDistance):(double?)null},
                    trace=shot.Trace,vegetation=shot.VegetationTrace,traceMs=R(shot.TraceMs),submittedPower=R(shot.SubmittedPower),healthLoss=R(shot.ObservedHealthLoss),shot.DamagedTargets,shot.KilledTargets,
                    checks=new {shot.ConeViolations,shot.RangeViolations,pelletMismatch=shot.PelletsSeen!=c.Pellets}});
            }
            if(!Active)return;
            if(!m_batches.TryGetValue(c.Gun,out var batch)) {
                if(m_batches.Count>=35)Flush("bucket_limit");
                m_batches[c.Gun]=batch=new Batch();
            }
            batch.Add(shot,sampled);
            Tick(c.Time);
        } catch {m_stopped=true;}
    }
    public void Tick(double now) {
        if(Active && double.IsFinite(m_nextSummary) && now>=m_nextSummary) {Flush("interval");m_nextSummary=now+SummaryInterval;}
    }
    public void Flush(string reason) {
        if(!Active)return;
        try {
            foreach(var b in m_batches.Values) if(b.Shots>0) Emit(new {
                type="summary",session=m_session,reason,scope="world/by-gun; includes all completed shots, not just sampled details",gun=b.Gun,preset=b.Preset,
                firstSequence=b.First,lastSequence=b.Last,from=R(b.Begin),to=R(b.End),shots=b.Shots,pellets=b.Pellets,
                detailSuppressed=b.Suppressed,shotsWithGeometryHit=b.WithGeometryHit,
                outcomes=new {head=b.Head,body=b.Body,terrain=b.Terrain,rangeEnd=b.RangeEnd,logical=b.Logical,fallback=b.Fallback},
                states=new {ads=b.Ads,hip=b.Shots-b.Ads,moving=b.Moving,air=b.Air,crouching=b.Crouching,touch=b.Touch,creative=b.Creative},
                coneDeg=new {min=R(b.ConeMin),mean=R(b.ConeSum/b.Shots),max=R(b.ConeMax)},
                actualAngleDeg=new {rms=R(Math.Sqrt(b.AngleSquares/Math.Max(1,b.Pellets))),max=R(b.AngleMax)},
                geometryDistance=new {min=Optional(b.DistanceMin),max=b.Head+b.Body>0?R(b.DistanceMax):(double?)null},
                trace=new {candidates=b.Candidates,preciseTests=b.PreciseTests,preciseMisses=b.PreciseMisses,fallbackCandidates=b.FallbackCandidates},
                timing=new {traceMsPerShot=R(b.TraceMs/b.Shots),maxShotTraceMs=R(b.TraceMax)},
                submittedPower=R(b.SubmittedPower),observedHealthLoss=R(b.HealthLoss),damagedTargets=b.DamagedTargets,killedTargets=b.KilledTargets,
                checks=new {coneViolations=b.ConeErrors,rangeViolations=b.RangeErrors,pelletMismatches=b.PelletErrors}});
            m_batches.Clear();
        } catch {m_stopped=true;m_batches.Clear();}
    }
}
