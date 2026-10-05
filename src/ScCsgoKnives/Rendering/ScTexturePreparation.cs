using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Engine.Graphics;
using Image=Engine.Media.Image;
namespace Game;

/// <summary>Owned CPU work only; shared streams, cache publication and GL stay on main thread.</summary>
public static class ScTexturePreparation {
    const long Budget=64L*1024*1024;
    static readonly object budgetLock=new();
    static long reserved;
    static readonly Dictionary<string,Ticket> pending=new();
    static readonly System.Threading.SemaphoreSlim workers=new(2);
    static readonly IDictionary<string,List<object>> caches=
        typeof(ContentManager).GetField("Caches",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null) as IDictionary<string,List<object>>;
    static readonly string[] suffixes=[".astc",".astcsrgb",".webp",".png",".jpg",".jpeg"];
    public static int PendingCount=>pending.Count;
    public static long ReservedBytes{get{lock(budgetLock)return reserved;}}
    public static long Started,Ready,Skipped;
    public static double WaitMilliseconds,UploadMilliseconds;
    sealed class Ticket(string key,long bytes){
        readonly object gate=new();
        Image image;Exception error;bool cancelled,complete,released;
        public readonly string Key=key;
        public Task Work;
        void Release(){if(released)return;released=true;lock(budgetLock)reserved-=bytes;}
        public void Decode(byte[] data){
            Image decoded=null;Exception failed=null;
            workers.Wait();
            try{
                lock(gate)if(cancelled){complete=true;Release();return;}
                using var timing=ScResourceTiming.Measure("texture-decode",Key);
                using var input=new MemoryStream(data,false);decoded=Image.Load(input);
                if((long)decoded.Width*decoded.Height*4>bytes)throw new InvalidDataException("Texture dimensions changed while decoding.");
            }catch(Exception e){failed=e;decoded?.Dispose();decoded=null;}
            finally{workers.Release();}
            lock(gate){
                complete=true;
                if(cancelled){decoded?.Dispose();Release();}
                else{image=decoded;error=failed;}
            }
        }
        public Image Take(){
            Work.GetAwaiter().GetResult();
            lock(gate){
                var result=image;image=null;Release();
                if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                return result;
            }
        }
        public void Cancel(){
            lock(gate){cancelled=true;image?.Dispose();image=null;if(complete)Release();}
        }
        public void Abort(){lock(gate){complete=cancelled=true;Release();}}
    }
    static T Cached<T>(string key)where T:class{
        if(caches!=null&&caches.TryGetValue(key,out var list))foreach(var item in list)if(item?.GetType()==typeof(T))return (T)item;
        return null;
    }
    public static void Request(string material){
        if(caches==null||string.IsNullOrEmpty(material))return;
        string path="Textures/ScCsgoKnives/"+material;
        if(pending.ContainsKey(path)||Cached<Texture2D>(path)!=null)return;
        // Mirrors native suffix precedence, including compressed overrides. Do not
        // reinterpret ASTC, sRGB or an already decoded native resource.
        string file=null;
        foreach(string suffix in suffixes){
            if(Cached<Texture2D>(path+suffix)!=null)return;
            if(file==null&&ContentManager.ContainsKey(path+suffix))file=path+suffix;
        }
        if(file==null||file.EndsWith(".astc")||file.EndsWith(".astcsrgb")||Cached<Image>(file)!=null)return;
        if(pending.Count>=8){Skipped++;return;}
        try{
            var stream=ContentManager.GetStream(file);
            if(stream==null||!stream.CanSeek)return;
            long length=stream.Length-stream.Position;
            if(length<=0||length>64*1024*1024){Skipped++;return;}
            // Header-only dimensions, no resizing or pixel transformation.
            var header=SixLabors.ImageSharp.Image.Identify(stream);stream.Position=0;
            long bytes=checked((long)header.Width*header.Height*4);
            lock(budgetLock){if(bytes<=0||bytes>Budget-reserved){Skipped++;return;}reserved+=bytes;}
            var ticket=new Ticket(file,bytes);
            try{
                var raw=new byte[(int)length];stream.ReadExactly(raw);
                pending.Add(path,ticket);ticket.Work=Task.Run(()=>ticket.Decode(raw));Started++;
            }catch{
                pending.Remove(path);ticket.Abort();throw;
            }
        }catch(Exception e)when(e is not OutOfMemoryException){
            KnifeDiagnostics.WarnOnce("texture-prepare-"+material,"Texture preparation "+material+": "+e.Message);
        }
    }
    public static Texture2D Load(string path){
        if(Cached<Texture2D>(path) is {} cached){
            if(pending.Remove(path,out var redundant))redundant.Cancel();
            return cached;
        }
        Publish(path,true);
        using var timing=ScResourceTiming.Measure("texture-upload",path);
        long tick=Stopwatch.GetTimestamp();
        var texture=ContentManager.Get<Texture2D>(path);
        UploadMilliseconds+=Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
        return texture;
    }
    static void Publish(string path,bool wait){
        if(!pending.TryGetValue(path,out var ticket)||!wait&&!ticket.Work.IsCompleted)return;
        pending.Remove(path);
        if(ticket.Work.IsCompleted)Ready++;
        long tick=Stopwatch.GetTimestamp();var image=ticket.Take();WaitMilliseconds+=Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
        if(image==null)return;
        if(Cached<Image>(ticket.Key)!=null||Cached<Texture2D>(path)!=null||Cached<Texture2D>(ticket.Key)!=null){image.Dispose();return;}
        if(!caches.TryGetValue(ticket.Key,out var list))caches[ticket.Key]=list=new();
        list.Add(image); // Ownership passes to ContentManager, just as ImageReader does.
    }
    public static void Pump(){
        // Publish CPU results only. No unexpected GL uploads from an update/worker.
        foreach(string path in pending.Keys.ToArray())try{Publish(path,false);}
        catch(Exception e)when(e is not OutOfMemoryException){KnifeDiagnostics.WarnOnce("texture-publish-"+path,e.Message);}
    }
    public static void Clear(){
        foreach(var ticket in pending.Values)ticket.Cancel();
        pending.Clear();Started=Ready=Skipped=0;WaitMilliseconds=UploadMilliseconds=0;
    }
}
