using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

static class CodecBench {
    static string Sha(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static void VerifyBudgets(string stage){
        using var budgets=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stage,"budget-report.json")));
        using var inventory=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stage,"inventory.json")));
        var checks=new List<object>();var timings=new List<object>();int assemblies=0;
        foreach(var budget in budgets.RootElement.EnumerateArray()){
            string codec=budget.GetProperty("codec").GetString()!;
            string dll=Path.Combine(stage,"budgets",codec,"bin/Release/net10.0/ScCsgoResources.dll");
            byte[] dllBytes=File.ReadAllBytes(dll);if(Sha(dllBytes)!=budget.GetProperty("resourceDllSha256").GetString())throw new Exception("Budget DLL changed");
            var assembly=System.Reflection.Assembly.Load(dllBytes);
            var embedded=inventory.RootElement.EnumerateArray().Where(r=>r.GetProperty("owner").GetString()=="core").ToArray();
            if(!assembly.GetManifestResourceNames().Order().SequenceEqual(embedded.Select(r=>"Game.AnimationData."+r.GetProperty("name").GetString()).Order()))throw new Exception("Resource names changed");
            foreach(var row in embedded){
                string name=row.GetProperty("name").GetString()!;using var input=assembly.GetManifestResourceStream("Game.AnimationData."+name)!;using var buffer=new MemoryStream();input.CopyTo(buffer);
                byte[] data=buffer.ToArray();if(data.AsSpan().StartsWith("RSCODE01"u8))data=Decode(data);
                if(Sha(data)!=row.GetProperty("sha256").GetString())throw new Exception("Rebuilt resource mismatch: "+name);
                checks.Add(new {codec,name,resourceDllExact=true});
            }
            assemblies++;
            foreach(var item in budget.GetProperty("selected").EnumerateArray()){
                byte[] blob=File.ReadAllBytes(Path.Combine(stage,item.GetProperty("path").GetString()!));
                byte[] source=File.ReadAllBytes(Path.Combine(stage,item.GetProperty("normalizedPath").GetString()!));
                if(Sha(blob)!=item.GetProperty("sha256").GetString()||!Decode(blob).AsSpan().SequenceEqual(source))throw new Exception("Budget stream mismatch");
                Decode(blob);var times=new List<double>();var allocations=new List<long>();
                for(int i=0;i<5;i++){
                    long before=GC.GetAllocatedBytesForCurrentThread();long tick=Stopwatch.GetTimestamp();byte[] decoded=Decode(blob);
                    times.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);allocations.Add(GC.GetAllocatedBytesForCurrentThread()-before);
                    if(!decoded.AsSpan().SequenceEqual(source))throw new Exception("Budget repeated decode mismatch");
                }
                times.Sort();allocations.Sort();
                timings.Add(new {codec,name=item.GetProperty("name").GetString(),group=item.GetProperty("group").GetString(),
                    medianMilliseconds=times[2],medianAllocatedBytes=allocations[2],exact=true});
            }
        }
        var result=new {failed=0,assemblies,embeddedChecks=checks.Count,streamChecks=timings.Count,checks,timings,
            scope="Exact embedded names and decoded member SHA-256, five warm C# stream-decode samples; no production loader or Android execution."};
        File.WriteAllText(Path.Combine(stage,"budget-validation.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Budget validation: {assemblies} DLLs, {checks.Count} embedded resources, {timings.Count} streams; 0 failures.");
    }
    public static void Run(string stage){
        using var report=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stage,"lossless-report.json")));
        var rows=new List<object>();
        foreach(var resource in report.RootElement.GetProperty("results").EnumerateArray()){
            string name=resource.GetProperty("name").GetString()!,group=resource.GetProperty("group").GetString()!;
            void Measure(string variant,JsonElement candidate,bool originalJson){
                byte[] blob=File.ReadAllBytes(Path.Combine(stage,candidate.GetProperty("path").GetString()!));
                if(Sha(blob)!=candidate.GetProperty("sha256").GetString())throw new Exception("Encoded input changed");
                string path=(originalJson?candidate:resource).GetProperty("normalizedPath").GetString()!;
                byte[] reference=File.ReadAllBytes(Path.Combine(stage,path));
                string expected=(originalJson?candidate:resource).GetProperty("normalizedSha256").GetString()!;
                if(Sha(reference)!=expected)throw new Exception("Reference changed");
                if(!Decode(blob).AsSpan().SequenceEqual(reference))throw new Exception("C# roundtrip mismatch: "+name);
                // Warm the decoder/JIT for this case. Disk I/O and hashing are outside
                // the timed region. Measure allocations, not process/Android peak RAM.
                Decode(blob);var ms=new List<double>();var allocations=new List<long>();
                for(int i=0;i<5;i++){
                    long before=GC.GetAllocatedBytesForCurrentThread();long tick=Stopwatch.GetTimestamp();byte[] decoded=Decode(blob);
                    ms.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);allocations.Add(GC.GetAllocatedBytesForCurrentThread()-before);
                    if(!decoded.AsSpan().SequenceEqual(reference))throw new Exception("Repeated roundtrip mismatch");
                }
                ms.Sort();allocations.Sort();
                rows.Add(new {name,group,variant,mode=candidate.GetProperty("mode").GetInt32(),
                    encodedBytes=blob.Length,decodedBytes=reference.Length,medianMilliseconds=ms[2],minimumMilliseconds=ms[0],
                    medianAllocatedBytes=allocations[2],sha256=expected,exact=true});
            }
            foreach(var candidate in resource.GetProperty("best").EnumerateObject())Measure(candidate.Name,candidate.Value,false);
            foreach(var candidate in resource.GetProperty("controls").EnumerateObject())Measure("original-json-"+candidate.Name,candidate.Value,true);
        }
        string zstd=typeof(ZstdSharp.Decompressor).Assembly.Location;
        var result=new{failed=0,count=rows.Count,rows,framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            zstdSharpAssemblyBytes=new FileInfo(zstd).Length,zstdSharpSha256=Sha(File.ReadAllBytes(zstd)),
            scope="Windows .NET C# decompression and inverse layout, five warm samples, excludes file I/O and parsing/rendering; not Android or total game load/frame timing."};
        File.WriteAllText(Path.Combine(stage,"managed-decode.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Managed codecs: {rows.Count} verified cases, 5 timed roundtrips each, 0 failures. ZstdSharp assembly {new FileInfo(zstd).Length} bytes.");
    }
    static byte[] Decode(byte[] blob){
        var head=blob.AsSpan();if(blob.Length<24||!head[..8].SequenceEqual("RSCODE01"u8))throw new InvalidDataException("Header");
        int mode=blob[8],codec=blob[9],length=BinaryPrimitives.ReadInt32LittleEndian(head[12..]),innerLength=BinaryPrimitives.ReadInt32LittleEndian(head[16..]);
        if(mode>3||codec>2||length<0||innerLength<length+4||innerLength>256_000_000)throw new InvalidDataException("Bounds");
        byte[] inner=new byte[innerLength];
        if(codec==0){using var stream=new MemoryStream(blob,20,blob.Length-20,false);using var decoder=new DeflateStream(stream,CompressionMode.Decompress);decoder.ReadExactly(inner);if(decoder.ReadByte()!=-1)throw new InvalidDataException("Extra data");}
        else if(codec==1){if(!BrotliDecoder.TryDecompress(head[20..],inner,out int written)||written!=innerLength)throw new InvalidDataException("Brotli length");}
        else {using var decoder=new ZstdSharp.Decompressor();if(decoder.Unwrap(head[20..],inner)!=innerLength)throw new InvalidDataException("Zstd length");}
        int count=BinaryPrimitives.ReadInt32LittleEndian(inner);if(count<0||count>100000)throw new InvalidDataException("Span count");
        int table=checked(4+count*12);if(innerLength-table!=length)throw new InvalidDataException("Payload length");
        byte[] data=inner.AsSpan(table).ToArray();int previous=0;
        for(int s=0;s<count;s++){
            var span=inner.AsSpan(4+s*12,12);int offset=BinaryPrimitives.ReadInt32LittleEndian(span),n=BinaryPrimitives.ReadInt32LittleEndian(span[4..]),stride=BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
            if(mode==0||offset<previous||n<0||stride<=0||stride>4096||(long)offset+(long)n*stride>length)throw new InvalidDataException("Span bounds");
            int size=checked(n*stride);byte[] restored=new byte[size];
            if(mode==3){
                for(int start=0;start<n;start+=256){
                    int rows=Math.Min(256,n-start),aligned=rows-rows%8,bytesPerPlane=aligned/8;
                    for(int column=0;column<stride;column++)for(int bit=0;bit<8;bit++){
                        int plane=offset+start*stride+(column*8+bit)*bytesPerPlane;
                        for(int row=0;row<aligned;row++)restored[(start+row)*stride+column]|=(byte)(((data[plane+row/8]>>(row%8))&1)<<bit);
                    }
                    data.AsSpan(offset+(start+aligned)*stride,(rows-aligned)*stride).CopyTo(restored.AsSpan((start+aligned)*stride));
                }
            }else{
                for(int column=0;column<stride;column++)for(int row=0;row<n;row++)restored[row*stride+column]=data[offset+column*n+row];
                if(mode==2)for(int row=1;row<n;row++)for(int column=0;column<stride;column++)restored[row*stride+column]^=restored[(row-1)*stride+column];
            }
            restored.CopyTo(data,offset);previous=offset+size;
        }
        return data;
    }
}
