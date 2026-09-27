using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace Game;

/// <summary>Per-resource lossless envelope. Decoded buffers live only for the reader's scope.</summary>
public static class ScResourceCompression {
#if SC_RESOURCE_ZSTD
    public const int Protocol = 1;
#else
    public const int Protocol = 0;
#endif
    public const int MaximumBytes = 64 * 1024 * 1024;

    // The returned stream is always owned by the caller. A shared ContentManager
    // source must pass leaveOpen:true; it is never retained in a second raw cache.
    public static Stream Open(Stream source, string resource, bool leaveOpen = false) {
        ArgumentNullException.ThrowIfNull(source);
        try {
            byte[] prefix = new byte[8];
            int count = 0;
            while (count < prefix.Length) {
                int n = source.Read(prefix, count, prefix.Length - count);
                if (n == 0) break;
                count += n;
            }
            if (count != 8 || !prefix.AsSpan().SequenceEqual("SCZSTD01"u8))
                return new PrefixStream(source, prefix, count, leaveOpen);
            try {
                Span<byte> header = stackalloc byte[40];
                source.ReadExactly(header);
                int rawLength = BinaryPrimitives.ReadInt32LittleEndian(header);
                int packedLength = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
                if (rawLength is <= 0 or > MaximumBytes || packedLength is <= 0 or > MaximumBytes)
                    throw new InvalidDataException("Resource size limit.");
                if (source.CanSeek && source.Length - source.Position != packedLength)
                    throw new InvalidDataException("Resource payload length.");
                byte[] packed = new byte[packedLength];
                source.ReadExactly(packed);
                if (source.ReadByte() != -1) throw new InvalidDataException("Trailing resource bytes.");
                byte[] decoded = Decode(packed, rawLength);
                Span<byte> hash = stackalloc byte[32];
                SHA256.HashData(decoded, hash);
                if (!CryptographicOperations.FixedTimeEquals(hash, header[8..]))
                    throw new InvalidDataException("Resource checksum.");
                return new MemoryStream(decoded, writable: false);
            } catch (Exception e) when (e is not OutOfMemoryException) {
                throw new ScResourceCodecException(resource, e);
            } finally {
                if (!leaveOpen) source.Dispose();
            }
        } catch {
            if (!leaveOpen) source.Dispose();
            throw;
        }
    }

    static byte[] Decode(byte[] packed, int length) {
#if SC_RESOURCE_ZSTD
        // Destination length is bounded before allocating; no size from an untrusted
        // Zstd frame is allowed to choose an allocation or a native dependency.
        byte[] decoded = new byte[length];
        using var decoder = new ZstdSharp.Decompressor();
        if (decoder.Unwrap(packed, decoded) != length)
            throw new InvalidDataException("Decoded resource length.");
        return decoded;
#else
        throw new InvalidDataException("This resource requires the matching compressed 1.3.0 Lite package.");
#endif
    }

    sealed class PrefixStream(Stream source, byte[] prefix, int count, bool leaveOpen) : Stream {
        int offset;
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int start, int length) => Read(buffer.AsSpan(start, length));
        public override int Read(Span<byte> buffer) {
            int n = Math.Min(buffer.Length, count - offset);
            prefix.AsSpan(offset, n).CopyTo(buffer); offset += n;
            return n != 0 ? n : source.Read(buffer);
        }
        public override int ReadByte() => offset < count ? prefix[offset++] : source.ReadByte();
        protected override void Dispose(bool disposing) {
            if (disposing && !leaveOpen) source.Dispose();
            base.Dispose(disposing);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed class ScResourceCodecException(string resource, Exception inner)
    : IOException("CS资源解码失败：" + resource + "。请重新导入配套的1.3.0轻量包和探员包。", inner);
