using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engine.Animation;
using Engine.Graphics;
using Engine.Media;
using Game;

if (args.Length != 4 || args[0] is not ("bake" or "verify"))
    throw new ArgumentException("ActorLoadCheck <bake|verify> <model.glb> <cache.scanim> <report.json>");
Engine.Dispatcher.Initialize();
var checks = new List<string>();
void Check(string name, bool ok) { if (!ok) throw new Exception(name); checks.Add(name); }
string Sha(string path) { using var s=File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(s)); }
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
long allocation = GC.GetTotalAllocatedBytes(true);
var watch = Stopwatch.StartNew();
using var input = File.OpenRead(args[1]);
var data = GltfLoader.Load(input);
long gltfMs = watch.ElapsedMilliseconds;
long gltfAllocatedBytes = GC.GetTotalAllocatedBytes(true)-allocation;
Console.WriteLine($"glTF native parse: {gltfMs} ms, {gltfAllocatedBytes} allocated bytes");
var boneNames = data.Bones.Select(b => b.Name).ToArray();
if (args[0] == "bake") {
    Check("source is an original animated GLB, not a cache marker",
        data.Animations.Count>0&&data.Animations.All(a=>!a.Name.StartsWith(ScActorAnimations.MarkerPrefix)));
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2])));
    using (var output = File.Create(args[2])) using (var writer = new BinaryWriter(output,Encoding.UTF8)) {
        writer.Write("SCACT001"u8); writer.Write(boneNames.Length);
        foreach (string name in boneNames) writer.Write(name);
        var bones = boneNames.Select((name,index)=>(name,index)).ToDictionary(p=>p.name,p=>p.index);
        writer.Write(data.Animations.Count);
        foreach (var clip in data.Animations) {
            Check(clip.Name+" has no unsupported targets", clip.PointerTargets.Count==0&&clip.NodeVisibilityTargets.Count==0&&clip.Channels.Count>0);
            writer.Write(clip.Name); writer.Write(clip.Duration);
            var times=clip.Channels[0].Sampler.KeyTimes; writer.Write(times.Length);
            foreach(float time in times)writer.Write(time);
            writer.Write(clip.Channels.Count);
            foreach(var channel in clip.Channels) {
                var sampler=channel.Sampler;
                Check(clip.Name+"/"+channel.TargetBoneName+"/"+channel.Property+" native uniform samples",
                    sampler.Interpolation==ModelAnimation.InterpolationType.Linear&&sampler.KeyTimes.SequenceEqual(times)&&
                    channel.Property!=ModelAnimation.AnimationProperty.Weights&&times.Length>0);
                writer.Write(bones[channel.TargetBoneName]);writer.Write((byte)channel.Property);
                if(channel.Property==ModelAnimation.AnimationProperty.Rotation) {
                    Check("rotation count",sampler.Rotations.Length==times.Length);
                    foreach(var v in sampler.Rotations){writer.Write(v.X);writer.Write(v.Y);writer.Write(v.Z);writer.Write(v.W);}
                } else {
                    var values=channel.Property==ModelAnimation.AnimationProperty.Translation?sampler.Translations:sampler.Scales;
                    Check("vector count",values.Length==times.Length);
                    foreach(var v in values){writer.Write(v.X);writer.Write(v.Y);writer.Write(v.Z);}
                }
            }
        }
    }
}
long beforeRead = watch.ElapsedMilliseconds;
using var cache=File.OpenRead(args[2]);
var decoded=ScActorAnimations.Read(cache,boneNames);
long readMs=watch.ElapsedMilliseconds-beforeRead;
if(args[0]=="bake") {
    Check("clip count exact",data.Animations.Count==decoded.Count);
    for(int a=0;a<decoded.Count;a++){
        var expected=data.Animations[a];var actual=decoded[a];
        Check("clip identity, duration and channel count",expected.Name==actual.Name&&expected.Duration==actual.Duration&&expected.Channels.Count==actual.Channels.Count);
        for(int c=0;c<actual.Channels.Count;c++){
            var e=expected.Channels[c];var v=actual.Channels[c];var x=e.Sampler;var y=v.Sampler;
            Check("all native samples preserved",e.TargetBoneName==v.TargetBoneName&&e.Property==v.Property&&x.Interpolation==y.Interpolation&&
                x.KeyTimes.SequenceEqual(y.KeyTimes)&&x.Translations.SequenceEqual(y.Translations)&&x.Rotations.SequenceEqual(y.Rotations)&&x.Scales.SequenceEqual(y.Scales));
        }
    }
} else Check("derived GLB contains only cache marker",data.Animations.Count==1&&data.Animations[0].Name.StartsWith(ScActorAnimations.MarkerPrefix));
long benchmarkAllocatedBytes=GC.GetTotalAllocatedBytes(true)-allocation;
long benchmarkPeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64;
if(args[0]=="verify"){
    string marker=data.Animations[0].Name;
    string role=marker=="__sc_prebaked_ct_v1"?"ct":"t";
    string name="Animations/ScCsgoTactical/"+role+".scanim";
    var bytes=File.ReadAllBytes(args[2]);
    MemoryStream Register(byte[] raw){
        var stream=new MemoryStream(raw);var info=new ContentInfo(name);info.SetContentStream(stream);ContentManager.Add(info);return stream;
    }
    Model Fresh(){
        var model=new Model{ModelData=new ModelData(),Animations=[new ModelAnimation{Name=marker}]};
        foreach(var bone in boneNames)model.m_bones.Add(new ModelBone{Name=bone,Index=model.m_bones.Count,Model=model});
        return model;
    }
    var ownedStream=Register(bytes);
    // Fresh models model cold reopen/reentry; repeated instances reuse the cached model's clips.
    for(int round=0;round<2;round++){
        using var model=Fresh();ScActorAnimations.Ensure(model);var loaded=model.Animations;long position=ownedStream.Position;
        Check("cold/reentry restores all clips "+round,loaded.Count==decoded.Count&&ReferenceEquals(loaded,model.ModelData.Animations));
        for(int i=0;i<5;i++)ScActorAnimations.Ensure(model);
        Check("repeated spawn/preview reuses samples "+round,ReferenceEquals(loaded,model.Animations)&&ownedStream.Position==position);
        Check("ContentManager stream stays owned and open "+round,ownedStream.CanRead&&ownedStream.CanWrite);
    }
    using(var unrelated=new Model{Animations=[new ModelAnimation{Name="idle"}]}){
        var clips=unrelated.Animations;ScActorAnimations.Ensure(unrelated);Check("non-CS models untouched",ReferenceEquals(clips,unrelated.Animations));
    }
    foreach(var damaged in new[]{bytes[..12],new byte[8]}){
        Register(damaged);using var model=Fresh();var original=model.Animations;bool rejected=false;
        try{ScActorAnimations.Ensure(model);}catch(Exception e) when(e is IOException or InvalidDataException){rejected=true;}
        Check("damaged cache fails without publishing partial clips",rejected&&ReferenceEquals(original,model.Animations));
        Register(bytes);ScActorAnimations.Ensure(model);Check("failed load can retry valid cache",model.Animations.Count==decoded.Count);
    }
    using(var stream=new MemoryStream(bytes)){
        bool refused=false;try{ScActorAnimations.Read(stream,["wrong bone"]);}catch(InvalidDataException){refused=true;}
        Check("wrong skeleton rejected",refused);
    }
}
var report=new {mode=args[0],gltfSha256=Sha(args[1]),cacheSha256=Sha(args[2]),gltfMs,gltfAllocatedBytes,cacheReadMs=readMs,
    totalAllocatedBytes=benchmarkAllocatedBytes,peakWorkingSetBytes=benchmarkPeakWorkingSetBytes,
    bones=boneNames.Length,clips=decoded.Count,channels=decoded.Sum(a=>a.Channels.Count),samples=decoded.Sum(a=>a.Channels.Sum(c=>c.Sampler.KeyTimes.Length)),
    checks=checks.Count,failed=0,host="Windows native API 1.9.3.1; headless CPU benchmark, not Android"};
File.WriteAllText(args[3],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));
