using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

return ConverterCli.Run(args);

static class ConverterCli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("CsmcAssetConverter --mesh <meshbin> [--anim <animbin>] --out <diagnostic.json> [--obj <model.obj>] [--obj-parts-dir <directory>] [--runtime <animation.json>] [--stride 28]");
            return args.Length == 0 ? 2 : 0;
        }

        string? meshPath = Option(args, "--mesh");
        string? animPath = Option(args, "--anim");
        string? outputPath = Option(args, "--out");
        string? objPath = Option(args, "--obj");
        string? objPartsDirectory = Option(args, "--obj-parts-dir");
        string? runtimePath = Option(args, "--runtime");
        int stride = int.TryParse(Option(args, "--stride"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStride)
            ? parsedStride : 28;
        if (meshPath is null || outputPath is null)
        {
            Console.Error.WriteLine("--mesh and --out are required.");
            return 2;
        }

        try
        {
            var document = new ConversionDocument
            {
                Mesh = MeshParser.Parse(meshPath, stride),
                Animation = animPath is null ? null : AnimationParser.Parse(animPath)
            };
            var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            options.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(document, options), new UTF8Encoding(false));
            Console.WriteLine($"Wrote {outputPath}");
            if (objPath is not null)
            {
                ObjWriter.Write(document.Mesh, objPath);
                Console.WriteLine($"Wrote {objPath}");
            }
            if (objPartsDirectory is not null)
            {
                foreach (string partPath in ObjWriter.WriteParts(document.Mesh, objPartsDirectory))
                    Console.WriteLine($"Wrote {partPath}");
            }
            if (runtimePath is not null)
            {
                if (document.Animation is null) throw new ArgumentException("--runtime requires --anim");
                RuntimeAnimationWriter.Write(document.Animation, document.Mesh, runtimePath, options);
                Console.WriteLine($"Wrote {runtimePath}");
            }
            Console.WriteLine($"Mesh records: {document.Mesh.Records.Count}, vertices: {document.Mesh.Records.Sum(x => x.VertexCount)}, stride: {stride}");
            if (document.Animation is not null)
            {
                Console.WriteLine($"Bones: {document.Animation.Bones.Count}, animations: {document.Animation.Animations.Count}");
                foreach (var selected in document.Animation.Selected)
                    Console.WriteLine($"{selected.Alias}: {selected.SourceName} ({selected.Duration:0.###}s, {selected.CurveCount} curves)");
            }
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            Console.Error.WriteLine($"Conversion failed: {ex.Message}");
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.FindIndex(args, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

sealed class LittleEndianReader
{
    private readonly byte[] data;
    private int offset;
    public LittleEndianReader(byte[] data, string source) { this.data = data; Source = source; }
    public string Source { get; }
    public int Position => offset;
    public int Remaining => data.Length - offset;
    public bool HasRemaining => Remaining > 0;
    public int ReadInt(string field)
    {
        Ensure(4, field);
        int value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        return value;
    }
    public int ReadCount(string field)
    {
        int value = ReadInt(field);
        if (value < 0) throw Error($"{field} is negative: {value}");
        return value;
    }
    public float ReadFloat(string field)
    {
        Ensure(4, field);
        int bits = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        float value = BitConverter.Int32BitsToSingle(bits);
        if (!float.IsFinite(value)) throw Error($"{field} is not finite: {value}");
        return value;
    }
    public string ReadString(string field)
    {
        int length = ReadCount(field + ".length");
        Ensure(length, field);
        string value = Encoding.UTF8.GetString(data, offset, length);
        offset += length;
        return value;
    }
    public byte[] ReadBytes(int length, string field)
    {
        if (length < 0) throw Error($"{field} is negative: {length}");
        Ensure(length, field);
        byte[] result = data.AsSpan(offset, length).ToArray();
        offset += length;
        return result;
    }
    public void RequireEnd() { if (HasRemaining) throw Error($"unexpected trailing bytes: {Remaining} at offset {Position}"); }
    private void Ensure(int length, string field) { if (length > Remaining) throw Error($"truncated {field}: need {length}, remaining {Remaining}"); }
    private InvalidDataException Error(string message) => new($"{Source}: {message}");
}

static class MeshParser
{
    public static MeshDocument Parse(string path, int stride)
    {
        if (stride <= 0) throw new ArgumentException("stride must be positive");
        var reader = new LittleEndianReader(File.ReadAllBytes(path), path);
        var header = Enumerable.Range(0, 5).Select(i => reader.ReadFloat($"header[{i}]")).ToArray();
        int recordCount = reader.ReadCount("recordCount");
        var records = new List<MeshRecord>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            string firstName = reader.ReadString($"record[{i}].name");
            string secondName = reader.ReadString($"record[{i}].material");
            int vertexCount = reader.ReadCount($"record[{i}].vertexCount");
            int byteLength = checked(vertexCount * stride);
            byte[] raw = reader.ReadBytes(byteLength, $"record[{i}].vertices");
            var samples = new List<VertexSample>(Math.Min(vertexCount, 8));
            for (int vertex = 0; vertex < Math.Min(vertexCount, 8); vertex++)
            {
                var span = raw.AsSpan(vertex * stride, stride);
                var floats = new List<float>();
                for (int j = 0; j + 4 <= stride; j += 4)
                    floats.Add(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(j, 4))));
                samples.Add(new VertexSample
                {
                    Index = vertex,
                    Position = floats.Count >= 3 ? floats.Take(3).ToArray() : [],
                    TexCoord = floats.Count >= 5 ? floats.Skip(3).Take(2).ToArray() : [],
                    Packed0 = stride >= 24 ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20, 4)) : null,
                    Packed1 = stride >= 28 ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(24, 4)) : null
                });
            }
            records.Add(new MeshRecord { Name = firstName, Material = secondName, VertexCount = vertexCount, ByteLength = byteLength, RawBase64 = Convert.ToBase64String(raw), FirstVertices = samples });
        }
        reader.RequireEnd();
        return new MeshDocument { Path = Path.GetFullPath(path), Header = header, RecordCount = recordCount, Records = records };
    }
}

static class ObjWriter
{
    public static void Write(MeshDocument mesh, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        WriteHeader(writer, mesh);
        int baseIndex = 1;
        foreach (var record in mesh.Records)
        {
            WriteRecord(writer, mesh, record, baseIndex);
            baseIndex += record.VertexCount;
        }
    }

    /// <summary>Survivalcraft indexes each OBJ object with ushort indices: at most 65535 vertices, 21845 triangles.</summary>
    public const int MaxFacesPerObject = 21845;

    /// <summary>
    /// One OBJ per part. Records sharing a name (the AK-47 and AWP bodies are two records
    /// both called weapon_hand_r) get a "__2", "__3" suffix instead of overwriting each
    /// other, and a record over the index limit is split into "__c1", "__c2", ... chunks.
    /// The bone a part follows is the name before the first "__".
    /// </summary>
    public static IEnumerable<(string Name, MeshRecord Record, int FirstFace, int FaceCount)> PartPlan(MeshDocument mesh)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (MeshRecord record in mesh.Records)
        {
            string name = Sanitize(record.Name);
            seen[name] = seen.TryGetValue(name, out int n) ? n + 1 : 1;
            string unique = seen[name] == 1 ? name : $"{name}__{seen[name]}";
            int faces = record.VertexCount / 3;
            if (faces <= MaxFacesPerObject) { yield return (unique, record, 0, faces); continue; }
            int chunks = (faces + MaxFacesPerObject - 1) / MaxFacesPerObject;
            for (int c = 0; c < chunks; c++)
            {
                int first = c * MaxFacesPerObject;
                yield return ($"{unique}__c{c + 1}", record, first, Math.Min(MaxFacesPerObject, faces - first));
            }
        }
    }

    public static IEnumerable<string> WriteParts(MeshDocument mesh, string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        foreach (var part in PartPlan(mesh))
        {
            string path = Path.Combine(directory, part.Name + ".obj");
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            WriteHeader(writer, mesh);
            WriteRecord(writer, mesh, part.Record, 1, part.Name, part.FirstFace, part.FaceCount);
            yield return path;
        }
    }

    private static void WriteHeader(StreamWriter writer, MeshDocument mesh)
    {
        writer.WriteLine("# Converted from CSMC meshbin by CsmcAssetConverter");
        writer.WriteLine($"# source: {Path.GetFileName(mesh.Path)}");
        writer.WriteLine($"# source center: {Format(mesh.Header[0])} {Format(mesh.Header[1])} {Format(mesh.Header[2])}");
        writer.WriteLine($"# source scale: {Format(mesh.Header[3])}");
        writer.WriteLine("# Coordinates are preserved exactly as stored by CSMC.");
        writer.WriteLine("# Basis conversion and mesh-normalization conjugation happen at runtime.");
    }

    private static void WriteRecord(StreamWriter writer, MeshDocument mesh, MeshRecord record, int baseIndex)
        => WriteRecord(writer, mesh, record, baseIndex, Sanitize(record.Name), 0, record.VertexCount / 3);

    private static void WriteRecord(StreamWriter writer, MeshDocument mesh, MeshRecord record, int baseIndex, string objectName, int firstFace, int faceCount)
    {
        byte[] raw = Convert.FromBase64String(record.RawBase64);
        if (record.VertexCount % 3 != 0) throw new InvalidDataException($"{mesh.Path}: vertex count {record.VertexCount} is not a triangle list");
        int firstVertex = firstFace * 3, vertexCount = faceCount * 3;
        writer.WriteLine($"o {objectName}");
        for (int i = firstVertex; i < firstVertex + vertexCount; i++)
        {
            DecodedVertex vertex = Decode(raw, i);
            writer.WriteLine($"v {Format(vertex.X)} {Format(vertex.Y)} {Format(vertex.Z)}");
        }
        for (int i = firstVertex; i < firstVertex + vertexCount; i++)
        {
            DecodedVertex vertex = Decode(raw, i);
            // V is written as stored: Survivalcraft's OBJ reader samples it top-down like the
            // meshbin. (0.15.2 flipped it on a misread of a washed-out screenshot and mirrored
            // every knife; the knives' correct look in 0.15.1 was the evidence to trust.)
            writer.WriteLine($"vt {Format(vertex.U)} {Format(vertex.V)}");
        }
        for (int i = firstVertex; i < firstVertex + vertexCount; i++)
        {
            DecodedVertex vertex = Decode(raw, i);
            writer.WriteLine($"vn {Format(vertex.Nx)} {Format(vertex.Ny)} {Format(vertex.Nz)}");
        }
        for (int i = 0; i < vertexCount; i += 3)
        {
            int a = baseIndex + i;
            int b = a + 1;
            int c = a + 2;
            writer.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
        }
    }

    private static DecodedVertex Decode(byte[] raw, int index)
    {
        var span = raw.AsSpan(index * 28, 28);
        return new DecodedVertex
        {
            X = Float(span, 0), Y = Float(span, 4), Z = Float(span, 8),
            U = Float(span, 12), V = Float(span, 16),
            Nx = unchecked((sbyte)span[24]) / 127f,
            Ny = unchecked((sbyte)span[25]) / 127f,
            Nz = unchecked((sbyte)span[26]) / 127f
        };
    }

    private static float Float(ReadOnlySpan<byte> span, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4)));

    private static string Format(float value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
    private static string Sanitize(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));

    private sealed class DecodedVertex
    {
        public float X;
        public float Y;
        public float Z;
        public float U;
        public float V;
        public float Nx;
        public float Ny;
        public float Nz;
    }
}

static class AnimationParser
{
    public static AnimationDocument Parse(string path)
    {
        var reader = new LittleEndianReader(File.ReadAllBytes(path), path);
        int boneCount = reader.ReadCount("boneCount");
        var bones = new List<AnimationBone>(boneCount);
        for (int i = 0; i < boneCount; i++)
        {
            int field0 = reader.ReadInt($"bone[{i}].field0");
            int field1 = reader.ReadInt($"bone[{i}].field1");
            int[] links = ReadIntArray(reader, $"bone[{i}].links");
            float[][] arrays = Enumerable.Range(0, 4).Select(j => ReadFloatArray(reader, $"bone[{i}].values[{j}]")).ToArray();
            bones.Add(new AnimationBone { Index = i, Field0 = field0, Field1 = field1, Links = links, Values = arrays });
        }
        int[] boneMap = ReadIntArray(reader, "boneMap");
        int channelGroupCount = reader.ReadCount("channelGroupCount");
        var channelGroups = new List<AnimationChannelGroup>(channelGroupCount);
        for (int group = 0; group < channelGroupCount; group++)
        {
            int channelCount = reader.ReadCount($"channelGroup[{group}].channelCount");
            var channels = new List<AnimationChannel>(channelCount);
            for (int channel = 0; channel < channelCount; channel++)
            {
                int field = reader.ReadInt($"channel[{group},{channel}].field");
                string name = reader.ReadString($"channel[{group},{channel}].name");
                float[] x = ReadFloatArray(reader, $"channel[{group},{channel}].x");
                float[] y = ReadFloatArray(reader, $"channel[{group},{channel}].y");
                float[] z = ReadFloatArray(reader, $"channel[{group},{channel}].z");
                channels.Add(new AnimationChannel { Field = field, Name = name, X = x, Y = y, Z = z });
            }
            channelGroups.Add(new AnimationChannelGroup { Channels = channels });
        }
        int animationCount = reader.ReadCount("animationCount");
        var animations = new List<AnimationClip>(animationCount);
        for (int i = 0; i < animationCount; i++)
        {
            string name = reader.ReadString($"animation[{i}].name");
            float duration = reader.ReadFloat($"animation[{i}].duration");
            int trackCount = reader.ReadCount($"animation[{i}].trackCount");
            var tracks = new List<AnimationTrack>(trackCount);
            for (int track = 0; track < trackCount; track++)
            {
                float[] times = ReadFloatArray(reader, $"animation[{i}].track[{track}].times");
                int keyCount = reader.ReadCount($"animation[{i}].track[{track}].keyCount");
                var keys = new List<float[]>(keyCount);
                for (int key = 0; key < keyCount; key++) keys.Add(ReadFloatArray(reader, $"animation[{i}].track[{track}].key[{key}]"));
                string target = reader.ReadString($"animation[{i}].track[{track}].target");
                tracks.Add(new AnimationTrack { Times = times, Keys = keys, Target = target });
            }
            int eventCount = reader.ReadCount($"animation[{i}].eventCount");
            var events = new List<AnimationEvent>(eventCount);
            for (int e = 0; e < eventCount; e++) events.Add(new AnimationEvent { Field0 = reader.ReadInt($"event[{i},{e}].field0"), Field1 = reader.ReadInt($"event[{i},{e}].field1"), Name = reader.ReadString($"event[{i},{e}].name") });
            animations.Add(new AnimationClip { Name = name, Duration = duration, Tracks = tracks, Events = events });
        }
        reader.RequireEnd();
        var selected = new List<AnimationSelection>();
        AddSelection("deploy", "firstperson_draw");
        AddSelection("inspect", "firstperson_lookat01");
        AddSelection("idle", "firstperson_idle");
        return new AnimationDocument { Path = Path.GetFullPath(path), Bones = bones, BoneMap = boneMap, ChannelGroups = channelGroups, Animations = animations, Selected = selected };

        void AddSelection(string alias, string sourceName)
        {
            var clip = animations.FirstOrDefault(x => string.Equals(x.Name, sourceName, StringComparison.Ordinal));
            if (clip is not null)
                selected.Add(new AnimationSelection { Alias = alias, SourceName = sourceName, Duration = clip.Duration, CurveCount = clip.Tracks.Count, BindingCount = clip.Events.Count });
        }
    }

    private static int[] ReadIntArray(LittleEndianReader reader, string field) { int n = reader.ReadCount(field + ".length"); return Enumerable.Range(0, n).Select(i => reader.ReadInt($"{field}[{i}]")).ToArray(); }
    private static float[] ReadFloatArray(LittleEndianReader reader, string field) { int n = reader.ReadCount(field + ".length"); return Enumerable.Range(0, n).Select(i => reader.ReadFloat($"{field}[{i}]")).ToArray(); }
}

static class RuntimeAnimationWriter
{
    public static void Write(AnimationDocument animation, MeshDocument mesh, string path, JsonSerializerOptions options)
    {
        string[] names = BuildBoneNames(animation);
        var selected = new Dictionary<string, RuntimeClip>(StringComparer.Ordinal);
        Add("deploy", "firstperson_draw");
        Add("deploy2", "firstperson_draw2");
        Add("idle", "firstperson_idle");
        Add("idle2", "firstperson_idle2");
        Add("inspect", "firstperson_lookat01");
        Add("inspect2", "firstperson_lookat02");
        Add("inspect3", "firstperson_lookat03");
        Add("inspectStart", "firstperson_lookat01_start");
        Add("inspectLoop", "firstperson_lookat01_loop");
        Add("inspectEnd", "firstperson_lookat01_end");
        Add("slash1", "firstperson_light_miss1");
        Add("slash2", "firstperson_light_miss2");
        Add("slashBack", "firstperson_light_backstab");
        Add("heavySlash", "firstperson_heavy_miss1");
        // Guns (AK-47, M4A1-S, AWP): every first-person clip CS:MC's action table can start.
        Add("shoot1", "firstperson_shoot1");
        Add("shoot2", "firstperson_shoot2");
        Add("shoot3", "firstperson_shoot3");
        Add("shootSilenced", "firstperson_shoot_silenced");
        Add("shootUnsilenced", "firstperson_shoot_unsilenced");
        Add("reload", "firstperson_reload");
        Add("attach", "firstperson_attach");
        Add("detach", "firstperson_detach");
        Add("inspect2Start", "firstperson_lookat02_start");
        Add("inspect2Loop", "firstperson_lookat02_loop");
        Add("inspect2End", "firstperson_lookat02_end");
        Add("inspect3Start", "firstperson_lookat03_start");
        Add("inspect3Loop", "firstperson_lookat03_loop");
        Add("inspect3End", "firstperson_lookat03_end");
        Add("inventoryIcon", "inventory_icon");
        var document = new RuntimeAnimationDocument
        {
            Format = "ScCsgoKnives.CsmcAnimation/2",
            MeshCenter = mesh.Header.Take(3).ToArray(),
            MeshNormalizationScale = mesh.Header[3],
            SourceReferenceScale = mesh.Header[4],
            MeshParts = ObjWriter.PartPlan(mesh).Select(part => part.Name).ToArray(),
            Bindings = animation.ChannelGroups.SelectMany(group => group.Channels)
                .GroupBy(channel => channel.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(channel => new RuntimeBinding
                {
                    Name = channel.Name,
                    BoneIndex = channel.Field,
                    RightMatrix = channel.X,
                    ReferenceMatrix = channel.Y,
                    LeftMatrix = channel.Z
                }).ToList(),
            Skeleton = animation.Bones.Select((bone, index) => new RuntimeBone
            {
                Index = index,
                Name = names[index],
                Parent = bone.Field1,
                Children = bone.Links,
                Matrix = bone.Values[0],
                Translation = bone.Values[1],
                Rotation = bone.Values[2],
                Scale = bone.Values[3]
            }).ToList(),
            Clips = selected
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, options), new UTF8Encoding(false));

        void Add(string alias, string sourceName)
        {
            AnimationClip? source = animation.Animations.FirstOrDefault(x => string.Equals(x.Name, sourceName, StringComparison.Ordinal));
            if (source is not null) selected[alias] = ConvertClip(source, names);
        }
    }

    private static string[] BuildBoneNames(AnimationDocument animation)
    {
        string[] names = Enumerable.Range(0, animation.Bones.Count).Select(i => $"bone_{i}").ToArray();
        foreach (AnimationChannel channel in animation.ChannelGroups.SelectMany(x => x.Channels))
            if (channel.Field >= 0 && channel.Field < names.Length) names[channel.Field] = channel.Name;
        foreach (int root in animation.BoneMap)
            if (root >= 0 && root < names.Length && names[root].StartsWith("bone_", StringComparison.Ordinal)) names[root] = $"root_{root}";
        return names;
    }

    private static RuntimeClip ConvertClip(AnimationClip clip, string[] names)
    {
        var bones = new Dictionary<string, RuntimeBoneCurves>(StringComparer.Ordinal);
        foreach (AnimationEvent binding in clip.Events)
        {
            if (binding.Field0 < 0 || binding.Field0 >= clip.Tracks.Count) throw new InvalidDataException($"{clip.Name}: curve index {binding.Field0} is invalid");
            if (binding.Field1 < 0 || binding.Field1 >= names.Length) throw new InvalidDataException($"{clip.Name}: bone index {binding.Field1} is invalid");
            AnimationTrack source = clip.Tracks[binding.Field0];
            if (source.Times.Length != source.Keys.Count) throw new InvalidDataException($"{clip.Name}: curve {binding.Field0} has mismatched times and keys");
            var curve = new RuntimeCurve { Interpolation = source.Target, Times = source.Times, Values = source.Keys };
            if (!bones.TryGetValue(names[binding.Field1], out RuntimeBoneCurves? target))
                bones.Add(names[binding.Field1], target = new RuntimeBoneCurves());
            switch (binding.Name)
            {
                case "rotation": target.Rotation = curve; break;
                case "translation": target.Translation = curve; break;
                case "scale": target.Scale = curve; break;
                default: throw new InvalidDataException($"{clip.Name}: unsupported transform '{binding.Name}'");
            }
        }
        return new RuntimeClip { SourceName = clip.Name, Duration = clip.Duration, Bones = bones };
    }
}

sealed class ConversionDocument { public MeshDocument Mesh { get; init; } = null!; public AnimationDocument? Animation { get; init; } }
sealed class MeshDocument { public string Path { get; init; } = ""; public float[] Header { get; init; } = []; public int RecordCount { get; init; } public List<MeshRecord> Records { get; init; } = []; }
sealed class MeshRecord { public string Name { get; init; } = ""; public string Material { get; init; } = ""; public int VertexCount { get; init; } public int ByteLength { get; init; } public string RawBase64 { get; init; } = ""; public List<VertexSample> FirstVertices { get; init; } = []; }
sealed class VertexSample { public int Index { get; init; } public float[] Position { get; init; } = []; public float[] TexCoord { get; init; } = []; public uint? Packed0 { get; init; } public uint? Packed1 { get; init; } }
sealed class AnimationDocument { public string Path { get; init; } = ""; public List<AnimationBone> Bones { get; init; } = []; public int[] BoneMap { get; init; } = []; public List<AnimationChannelGroup> ChannelGroups { get; init; } = []; public List<AnimationClip> Animations { get; init; } = []; public List<AnimationSelection> Selected { get; init; } = []; }
sealed class AnimationSelection { public string Alias { get; init; } = ""; public string SourceName { get; init; } = ""; public float Duration { get; init; } public int CurveCount { get; init; } public int BindingCount { get; init; } }
sealed class AnimationBone { public int Index { get; init; } public int Field0 { get; init; } public int Field1 { get; init; } public int[] Links { get; init; } = []; public float[][] Values { get; init; } = []; }
sealed class AnimationChannelGroup { public List<AnimationChannel> Channels { get; init; } = []; }
sealed class AnimationChannel { public int Field { get; init; } public string Name { get; init; } = ""; public float[] X { get; init; } = []; public float[] Y { get; init; } = []; public float[] Z { get; init; } = []; }
sealed class AnimationClip { public string Name { get; init; } = ""; public float Duration { get; init; } public List<AnimationTrack> Tracks { get; init; } = []; public List<AnimationEvent> Events { get; init; } = []; }
sealed class AnimationTrack { public float[] Times { get; init; } = []; public List<float[]> Keys { get; init; } = []; public string Target { get; init; } = ""; }
sealed class AnimationEvent { public int Field0 { get; init; } public int Field1 { get; init; } public string Name { get; init; } = ""; }
sealed class RuntimeAnimationDocument { public string Format { get; init; } = ""; public float[] MeshCenter { get; init; } = []; public float MeshNormalizationScale { get; init; } public float SourceReferenceScale { get; init; } public string[] MeshParts { get; init; } = []; public List<RuntimeBinding> Bindings { get; init; } = []; public List<RuntimeBone> Skeleton { get; init; } = []; public Dictionary<string, RuntimeClip> Clips { get; init; } = []; }
sealed class RuntimeBinding { public string Name { get; init; } = ""; public int BoneIndex { get; init; } public float[] RightMatrix { get; init; } = []; public float[] ReferenceMatrix { get; init; } = []; public float[] LeftMatrix { get; init; } = []; }
sealed class RuntimeBone { public int Index { get; init; } public string Name { get; init; } = ""; public int Parent { get; init; } public int[] Children { get; init; } = []; public float[] Matrix { get; init; } = []; public float[] Translation { get; init; } = []; public float[] Rotation { get; init; } = []; public float[] Scale { get; init; } = []; }
sealed class RuntimeClip { public string SourceName { get; init; } = ""; public float Duration { get; init; } public Dictionary<string, RuntimeBoneCurves> Bones { get; init; } = []; }
sealed class RuntimeBoneCurves { public RuntimeCurve? Rotation { get; set; } public RuntimeCurve? Translation { get; set; } public RuntimeCurve? Scale { get; set; } }
sealed class RuntimeCurve { public string Interpolation { get; init; } = ""; public float[] Times { get; init; } = []; public List<float[]> Values { get; init; } = []; }
