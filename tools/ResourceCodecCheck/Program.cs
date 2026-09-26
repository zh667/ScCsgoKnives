using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if(args.Length==2&&args[0]=="bench"){CodecBench.Run(Path.GetFullPath(args[1]));return;}
if(args.Length==2&&args[0]=="verify-budgets"){CodecBench.VerifyBudgets(Path.GetFullPath(args[1]));return;}
if(args.Length!=3||args[0]!="extract")throw new ArgumentException("ResourceCodecCheck extract <repo> <stage> | bench <stage> | verify-budgets <stage>");
string root=Path.GetFullPath(args[1]),stage=Path.GetFullPath(args[2]);
Directory.CreateDirectory(stage);
var records=new List<object>();
string Sha(byte[] data)=>Convert.ToHexStringLower(SHA256.HashData(data));
void Put(string name,byte[] data){string p=Path.Combine(stage,name);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllBytes(p,data);}
foreach(var (owner,file,expected) in new[]{
    ("core","[API1.9]CS武器1.3.0-轻量包.scmod","2d4cfc231c7a4e419eb3e7332fbf6b89d1fdcc0601e2c174d0c60b2543c43ea3"),
    ("agents","[API1.9]CS武器1.3.0-探员包.scmod","9c430eecdbfd44ee501cd75352f3086c26ad98e6cda210bccc120ffac6c5d2db")}){
    string package=Path.Combine(root,"output",file);
    if(Sha(File.ReadAllBytes(package))!=expected)throw new Exception("Baseline package changed");
    using var zip=ZipFile.OpenRead(package);
    if(owner=="core"){
        using var stream=zip.GetEntry("ScCsgoResources.dll")!.Open();using var memory=new MemoryStream();stream.CopyTo(memory);
        var assembly=Assembly.Load(memory.ToArray());
        foreach(string resource in assembly.GetManifestResourceNames().Order()){
            const string prefix="Game.AnimationData.";if(!resource.StartsWith(prefix))throw new Exception(resource);
            string name=resource[prefix.Length..];using var input=assembly.GetManifestResourceStream(resource)!;using var buffer=new MemoryStream();input.CopyTo(buffer);byte[] data=buffer.ToArray();
            string local="input/embedded/"+name;Put(local,data);
            string group=name.EndsWith(".parts")||name.EndsWith(".skin")?"embedded-mesh":name.EndsWith(".cs2.animation.json")?"weapon-animation":"embedded-other";
            object? packed=null;
            if(group=="weapon-animation"){
                var original=JsonNode.Parse(data)!;using var values=new MemoryStream();using var writer=new BinaryWriter(values);
                var spans=new List<int[]>();var meta=Pack(original.DeepClone(),writer,spans);
                byte[] metadata=Encoding.UTF8.GetBytes(meta!.ToJsonString()),floats=values.ToArray();
                using var all=new MemoryStream();using var header=new BinaryWriter(all);header.Write("RSCURV01"u8);header.Write(metadata.Length);header.Write(floats.Length);header.Write(metadata);header.Write(floats);
                byte[] result=all.ToArray();var reconstructed=Unpack(meta.DeepClone(),floats);Check(original,reconstructed,false);
                string binary="input/curves/"+name+".bin";Put(binary,result);
                packed=new{path=binary,sha256=Sha(result),bytes=result.Length,floatValues=floats.Length/4,metadataBytes=metadata.Length,spans=spans.Select(s=>new[]{16+metadata.Length+s[0],s[1],s[2]}).ToArray(),nativeFloat32Exact=true};
            }
            records.Add(new{owner,group,name,path=local,sha256=Sha(data),bytes=data.Length,compressedBytes=(long?)null,packed});
        }
    }
    foreach(var entry in zip.Entries){
        string n=entry.FullName;string? group=n.EndsWith(".scanim")?"actor-animation":n.EndsWith(".scmesh")?"npc-mesh":n.EndsWith(".glb")?"glb":null;
        if(group==null)continue;using var input=entry.Open();using var buffer=new MemoryStream();input.CopyTo(buffer);byte[] data=buffer.ToArray();
        string local="input/"+owner+"/"+n;Put(local,data);
        records.Add(new{owner,group,name=n,path=local,sha256=Sha(data),bytes=data.Length,compressedBytes=entry.CompressedLength,packed=(object?)null});
    }
}
File.WriteAllText(Path.Combine(stage,"inventory.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Extracted {records.Count} resources; binary curves preserve current .NET float32 values and metadata.");

static JsonNode? Pack(JsonNode? node,BinaryWriter writer,List<int[]> spans){
    if(node is JsonObject obj){
        foreach(string key in obj.Select(p=>p.Key).ToArray()){
            if((key=="Times"||key=="Values")&&obj[key] is JsonArray array&&array.Count>0){
                bool matrix=array[0] is JsonArray;int width=matrix?((JsonArray)array[0]!).Count:1;int offset=(int)writer.BaseStream.Position;
                foreach(var element in array){
                    if(matrix){var row=(JsonArray)element!;if(row.Count!=width)throw new Exception("ragged curve");foreach(var value in row)writer.Write(value!.GetValue<float>());}
                    else writer.Write(element!.GetValue<float>());
                }
                spans.Add([offset,array.Count,width*4]);
                obj[key]=new JsonObject{["$researchF32"]=new JsonArray(offset,array.Count,width,matrix?1:0)};
            }else {var prior=obj[key];var next=Pack(prior,writer,spans);if(!ReferenceEquals(prior,next))obj[key]=next;}
        }
    }else if(node is JsonArray list){for(int i=0;i<list.Count;i++){var prior=list[i];var next=Pack(prior,writer,spans);if(!ReferenceEquals(prior,next))list[i]=next;}}
    return node;
}
static JsonNode? Unpack(JsonNode? node,byte[] floats){
    if(node is JsonObject obj){
        if(obj["$researchF32"] is JsonArray desc){
            int offset=desc[0]!.GetValue<int>(),count=desc[1]!.GetValue<int>(),width=desc[2]!.GetValue<int>();bool matrix=desc[3]!.GetValue<int>()!=0;var array=new JsonArray();
            for(int i=0;i<count;i++){var row=new JsonArray();for(int j=0;j<width;j++){float value=BitConverter.ToSingle(floats,offset+(i*width+j)*4);if(matrix)row.Add(value);else array.Add(value);}if(matrix)array.Add(row);}return array;
        }
        foreach(string key in obj.Select(p=>p.Key).ToArray()){var prior=obj[key];var next=Unpack(prior,floats);if(!ReferenceEquals(prior,next))obj[key]=next;}
    }else if(node is JsonArray list){for(int i=0;i<list.Count;i++){var prior=list[i];var next=Unpack(prior,floats);if(!ReferenceEquals(prior,next))list[i]=next;}}
    return node;
}
static void Check(JsonNode? a,JsonNode? b,bool curve){
    if(a is JsonObject obj){var other=(JsonObject)b!;if(!obj.Select(p=>p.Key).SequenceEqual(other.Select(p=>p.Key)))throw new Exception("Metadata keys changed");foreach(var pair in obj)Check(pair.Value,other[pair.Key],curve||pair.Key is "Times" or "Values");}
    else if(a is JsonArray array){var other=(JsonArray)b!;if(array.Count!=other.Count)throw new Exception("Array shape changed");for(int i=0;i<array.Count;i++)Check(array[i],other[i],curve);}
    else if(curve&&a!=null&&b!=null){if(BitConverter.SingleToInt32Bits(a.GetValue<float>())!=BitConverter.SingleToInt32Bits(b.GetValue<float>()))throw new Exception("Float bits changed");}
    else if(!JsonNode.DeepEquals(a,b))throw new Exception("Metadata changed");
}
