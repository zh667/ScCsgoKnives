using System.IO;
using System.Text;
using Engine;
namespace Game;

/// <summary>Payload writer for CS network messages: little-endian binary, strings as UTF-8. Every message is small and
/// self-contained; readers validate ranges themselves (a peer's bytes are never trusted).</summary>
public sealed class ScNetWriter {
    readonly MemoryStream m_stream = new();
    readonly BinaryWriter m_writer;
    public ScNetWriter() => m_writer = new BinaryWriter(m_stream, Encoding.UTF8, true);
    public ScNetWriter Byte(byte v) { m_writer.Write(v); return this; }
    public ScNetWriter Bool(bool v) { m_writer.Write(v); return this; }
    public ScNetWriter Int(int v) { m_writer.Write(v); return this; }
    public ScNetWriter Long(long v) { m_writer.Write(v); return this; }
    public ScNetWriter Float(float v) { m_writer.Write(v); return this; }
    public ScNetWriter Double(double v) { m_writer.Write(v); return this; }
    public ScNetWriter String(string v) { m_writer.Write(v ?? ""); return this; }
    public ScNetWriter Vector3(Vector3 v) { m_writer.Write(v.X); m_writer.Write(v.Y); m_writer.Write(v.Z); return this; }
    public ScNetWriter Ray(Ray3 r) => Vector3(r.Position).Vector3(r.Direction);
    /// <summary>Bytes of a message already validated by its reader (a relay).</summary>
    public ScNetWriter Raw(byte[] bytes) { m_writer.Write(bytes ?? []); return this; }
    public int Length { get { m_writer.Flush(); return (int)m_stream.Length; } }
    public byte[] ToArray() { m_writer.Flush(); return m_stream.ToArray(); }
}

/// <summary>Payload reader; any malformed payload throws <see cref="InvalidDataException"/> (the dispatcher drops the message).</summary>
public sealed class ScNetReader {
    readonly BinaryReader m_reader;
    public ScNetReader(byte[] payload) => m_reader = new BinaryReader(new MemoryStream(payload ?? [], false), Encoding.UTF8);
    public bool End => m_reader.BaseStream.Position >= m_reader.BaseStream.Length;
    /// <summary>Everything not read yet.</summary>
    public byte[] Rest() => m_reader.ReadBytes((int)(m_reader.BaseStream.Length - m_reader.BaseStream.Position));
    T Guard<T>(Func<T> read) {
        try { return read(); }
        catch (EndOfStreamException e) { throw new InvalidDataException("truncated CS network message", e); }
    }
    public byte Byte() => Guard(m_reader.ReadByte);
    public bool Bool() => Guard(m_reader.ReadBoolean);
    public int Int() => Guard(m_reader.ReadInt32);
    public long Long() => Guard(m_reader.ReadInt64);
    public float Float() {
        float v = Guard(m_reader.ReadSingle);
        return float.IsFinite(v) ? v : throw new InvalidDataException("non-finite number");
    }
    public double Double() {
        double v = Guard(m_reader.ReadDouble);
        return double.IsFinite(v) ? v : throw new InvalidDataException("non-finite number");
    }
    public string String(int maxLength = 4096) {
        string v = Guard(m_reader.ReadString);
        return v.Length <= maxLength ? v : throw new InvalidDataException("string too long");
    }
    public Vector3 Vector3() => new(Float(), Float(), Float());
    public Ray3 Ray() {
        Vector3 position = Vector3(), direction = Vector3();
        float length = direction.Length();
        if (length < 1e-4f) throw new InvalidDataException("zero ray direction");
        return new Ray3(position, direction / length);
    }
    /// <summary>Requires that nothing is left: a message of another kind (or a longer one) must not pass as this one.</summary>
    public void Finish() { if (!End) throw new InvalidDataException("unexpected bytes after the message"); }
    public int Count(int max) {
        int n = Int();
        return n >= 0 && n <= max ? n : throw new InvalidDataException($"count {n} outside 0..{max}");
    }
}
