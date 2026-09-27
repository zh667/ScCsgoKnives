using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Game;

string stage=Path.GetFullPath(args[0]);
var checks=new List<string>();var timing=new List<object>();
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);}
string Sha(byte[] data)=>Convert.ToHexStringLower(SHA256.HashData(data));
byte[] Bytes(ZipArchive z,string name){using var s=z.GetEntry(name)!.Open();using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
byte[] Decoded(byte[] raw){using var s=ScResourceCompression.Open(new MemoryStream(raw),"test");using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
string foreignPath=Path.GetFullPath(Path.Combine(stage,"../../tools/ResourceCodecCheck/bin/Release/net10.0/ZstdSharp.dll"));
var foreign=Assembly.Load(File.ReadAllBytes(foreignPath));
Check("foreign ZstdSharp coexists without alias",foreign.GetName().Name=="ZstdSharp"&&foreign!=typeof(ZstdSharp.Decompressor).Assembly);
using var packages=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stage,"packages.json")));
byte[] example=null;int streams=0,embedded=0;
foreach(var owner in new[]{"core","agents"}){
    var metadata=packages.RootElement.GetProperty(owner);string file=metadata.GetProperty("file").GetString()!;
    string path=Path.Combine(stage,"candidate",file);
    Check(owner+" candidate hash",Sha(File.ReadAllBytes(path))==metadata.GetProperty("sha256").GetString());
    using var candidate=ZipFile.OpenRead(path);using var baseline=ZipFile.OpenRead(Path.Combine(stage,"baseline",file));
    if(owner=="core"){
        Check("executed packaged reader",Sha(File.ReadAllBytes(typeof(ScResourceCompression).Assembly.Location))==Sha(Bytes(candidate,"ScCsgoKnives.dll")));
        Check("executed packaged isolated codec",Sha(File.ReadAllBytes(typeof(ZstdSharp.Decompressor).Assembly.Location))==Sha(Bytes(candidate,"ScCsgoResourceCodec.dll")));
        Check("codec assembly isolation",typeof(ZstdSharp.Decompressor).Assembly.GetName().Name=="ScCsgoResourceCodec");
        Check("codec only framework dependencies",typeof(ZstdSharp.Decompressor).Assembly.GetReferencedAssemblies().All(a=>a.Name!.StartsWith("System")||a.Name=="netstandard"));
        var original=Assembly.Load(Bytes(baseline,"ScCsgoResources.dll"));var current=Assembly.Load(Bytes(candidate,"ScCsgoResources.dll"));
        Check("exact embedded resource names",original.GetManifestResourceNames().Order().SequenceEqual(current.GetManifestResourceNames().Order()));
        foreach(string n in original.GetManifestResourceNames()){
            using var a=original.GetManifestResourceStream(n)!;using var b=current.GetManifestResourceStream(n)!;
            using var expected=new MemoryStream();a.CopyTo(expected);using var compressed=new MemoryStream();b.CopyTo(compressed);
            byte[] raw=compressed.ToArray();example??=raw;
            var sw=Stopwatch.StartNew();var actual=Decoded(raw);sw.Stop();
            Check(n+" decoded bytes",actual.SequenceEqual(expected.ToArray()));embedded++;streams++;
            timing.Add(new{name=n,milliseconds=sw.Elapsed.TotalMilliseconds,rawBytes=actual.Length,encodedBytes=raw.Length});
        }
    }else foreach(var entry in candidate.Entries.Where(e=>e.FullName.EndsWith(".scanim")||e.FullName.EndsWith(".scmesh"))){
        byte[] raw=Bytes(candidate,entry.FullName);var sw=Stopwatch.StartNew();var actual=Decoded(raw);sw.Stop();
        Check(entry.FullName+" decoded bytes",actual.SequenceEqual(Bytes(baseline,entry.FullName)));streams++;
        timing.Add(new{name=entry.FullName,milliseconds=sw.Elapsed.TotalMilliseconds,rawBytes=actual.Length,encodedBytes=raw.Length});
    }
    foreach(var entry in baseline.Entries){
        bool changed=metadata.GetProperty("changes").TryGetProperty(entry.FullName,out _);
        if(!changed)Check(owner+"/"+entry.FullName+" unchanged member",Bytes(candidate,entry.FullName).SequenceEqual(Bytes(baseline,entry.FullName)));
    }
}
byte[] good=example!;
foreach(bool shared in new[]{false,true})foreach(bool compressed in new[]{false,true})foreach(bool seek in new[]{false,true}){
    byte[] bytes=compressed?good:"SCACT001original-data"u8.ToArray();
    using var source=new ProbeStream(bytes,seek);
    using(var result=ScResourceCompression.Open(source,"ownership",shared)){using var sink=new MemoryStream();result.CopyTo(sink);Check("stream bytes "+shared+compressed+seek,sink.ToArray().SequenceEqual(compressed?Decoded(good):bytes));}
    Check("source ownership "+shared+compressed+seek,source.CanRead==shared);
}
var damaged=new Dictionary<string,byte[]>{
    ["truncated-header"]=good[..16],
    ["truncated-frame"]=good[..^1],
    ["trailing-data"]=good.Concat(new byte[]{1}).ToArray(),
};
foreach(string fault in new[]{"checksum","raw-negative","raw-limit","raw-mismatch","packed-limit","corrupt-frame"}){
    var data=(byte[])good.Clone();
    switch(fault){
        case "checksum":data[16]^=1;break;
        case "raw-negative":BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8),-1);break;
        case "raw-limit":BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8),ScResourceCompression.MaximumBytes+1);break;
        case "raw-mismatch":BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8),1);break;
        case "packed-limit":BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12),int.MaxValue);break;
        case "corrupt-frame":data[48]^=255;break;
    }
    damaged[fault]=data;
}
foreach(var fault in damaged)foreach(bool shared in new[]{false,true})foreach(bool seek in new[]{false,true}){
    using var source=new ProbeStream(fault.Value,seek);bool rejected=false;
    try{using var decoded=ScResourceCompression.Open(source,fault.Key,shared);}catch(ScResourceCodecException){rejected=true;}
    Check(fault.Key+" fails bounded "+shared+seek,rejected);
    Check(fault.Key+" failure ownership "+shared+seek,source.CanRead==shared);
}
// Repeated exact calls are thread-independent; no shared decoder or raw cache.
Parallel.For(0,8,_=>{if(!Decoded(good).SequenceEqual(Decoded(good)))throw new Exception("Concurrent decode");});
Check("concurrent reads independent",true);
bool software=Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic")=="0";
if(software)Check("software fallback has no x86 intrinsics",!System.Runtime.Intrinsics.X86.Avx2.IsSupported&&!System.Runtime.Intrinsics.X86.Sse2.IsSupported);
var output=new{failed=0,streams,embedded,checks,timing,softwareFallback=software,
    packages=packages.RootElement.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetProperty("sha256").GetString()),
    scope="Packaged C# reader; exact baseline equality and corrupt/nonseekable/ownership/concurrent reads. Windows only."};
File.WriteAllText(Path.Combine(stage,software?"codec-check-software.json":"codec-check.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{checks.Count} checks, {streams} resource streams, {embedded} embedded; zero failures.");

sealed class ProbeStream(byte[] bytes,bool seek):MemoryStream(bytes){
    public override bool CanSeek=>seek&&base.CanSeek;
    public override int Read(Span<byte> buffer)=>base.Read(buffer[..Math.Min(3,buffer.Length)]);
    public override int Read(byte[] buffer,int offset,int count)=>base.Read(buffer,offset,Math.Min(3,count));
}
