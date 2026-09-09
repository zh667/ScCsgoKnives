using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using Engine;
using Game;

static class LiveBackupRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Check(string n,bool ok,string d="")=>results.Add(new("live-backup/"+n,ok,d));
        try {
            string directory=Path.Combine(Path.GetTempPath(),"sc-live-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            new XElement("Project",new XElement("Subsystems",new XElement("Values",new XAttribute("Name","ScGunBlockBehavior"),
                new XElement("Values",new XAttribute("Name","GunRegistry"),new XElement("Value",new XAttribute("Name","Schema"),new XAttribute("Type","int"),new XAttribute("Value","4")))))).Save(Path.Combine(directory,"Project.xml"));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            using var serializer=new TerrainSerializer23(directory);
            var storage=(TerrainSerializer23.RegionFileStorage)typeof(TerrainSerializer23).GetField("m_storage",flags).GetValue(serializer);
            var ioLock=typeof(TerrainSerializer23).GetField("m_lock",flags).GetValue(serializer);
            var point=new Point2(0,-1);var live=storage.GetRegionStream(point,true);live.Flush();
            live.Position=0;var hash=SHA256.HashData(live);
            bool blocked=false;
            try{using var other=File.OpenRead(storage.GetRegionPath(point));}catch(IOException){blocked=true;}
            Check("native-region-lock-reproduced",blocked||!OperatingSystem.IsWindows(),"Actual native Region 0,-1.dat; Windows refuses a second File.OpenRead while live");
            using var update=new AutoResetEvent(true);
            var helper=mod.GetType("Game.ScLiveWorldBackup").GetMethod("WithReleasedRegions");
            var snapshot=mod.GetType("Game.ScGunSchemaUpgrade").GetMethod("Snapshot");
            bool paused=false,locked=false;
            string result=(string)helper.Invoke(null,[serializer,update,(Func<string>)(()=> {
                paused=!update.WaitOne(0);locked=Monitor.IsEntered(ioLock);
                return (string)snapshot.Invoke(null,[directory,"live-region",4]);
            })]);
            Check("backup-holds-both-terrain-gates",paused&&locked);
            using(var zip=System.IO.Compression.ZipFile.OpenRead(result)) {
                using var region=zip.GetEntry("Regions/Region 0,-1.dat").Open();
                Check("locked-region-included-byte-for-byte",SHA256.HashData(region).AsSpan().SequenceEqual(hash));
            }
            bool resumed=update.WaitOne(0);if(resumed)update.Set();
            var reopened=storage.GetRegionStream(point,false);
            Check("native-stream-reopens-after-backup",resumed&&reopened.CanRead&&!ReferenceEquals(live,reopened)&&!Monitor.IsEntered(ioLock));
            bool failed=false;
            try{helper.Invoke(null,[serializer,update,(Func<string>)(()=>throw new IOException("injected snapshot failure"))]);}
            catch(TargetInvocationException e)when(e.InnerException is IOException){failed=true;}
            resumed=update.WaitOne(0);if(resumed)update.Set();
            Check("failure-resumes-without-destroying-serializer",failed&&resumed&&!Monitor.IsEntered(ioLock)&&storage.GetRegionStream(point,false).CanRead);
            using var unsupported=new TerrainSerializer23();
            using var memory=new MemoryStream();
            typeof(TerrainSerializer23).GetField("m_storage",flags).SetValue(unsupported,new TerrainSerializer23.SingleFileStorage{Stream=memory});
            bool ran=false,refused=false;
            try{helper.Invoke(null,[unsupported,update,(Func<string>)(()=>{ran=true;return "bad";})]);}catch(TargetInvocationException){refused=true;}
            resumed=update.WaitOne(0);if(resumed)update.Set();
            Check("unsupported-storage-not-disposed",refused&&!ran&&memory.CanRead&&resumed);
        }catch(Exception e){Check("failure",false,e.ToString());}
        return results;
    }
}
