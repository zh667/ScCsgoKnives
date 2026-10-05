// ProcessAudioCapture <pid> <out.wav> <seconds>
//
//   Records only what one process (and its children) plays, through Windows process loopback (VAD\Process_Loopback,
//   Windows 10 20348+/11): neither the user's other applications nor any microphone are captured. 48 kHz 16-bit stereo PCM
//   (the audio engine converts). Should a device period deliver no packet, the gap is filled with silence against the wall
//   clock, so sample n of the file is <startUtc> + n / 48000 (to within one period). Writes <out.wav> and <out.wav>.json.
//   Stops after <seconds>, or earlier when the process exits.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length < 3) { Console.Error.WriteLine("usage: ProcessAudioCapture <pid> <out.wav> <seconds>"); return 2; }
int pid = int.Parse(args[0]); string path = args[1]; double seconds = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
const int Rate = 48000, Channels = 2, Bits = 16, Block = Channels * Bits / 8;

var client = Loopback.Activate(pid);
var format = new Loopback.WaveFormatEx { wFormatTag = 1, nChannels = Channels, nSamplesPerSec = Rate, nAvgBytesPerSec = Rate * Block, nBlockAlign = Block, wBitsPerSample = Bits, cbSize = 0 };
IntPtr pFormat = Marshal.AllocHGlobal(Marshal.SizeOf<Loopback.WaveFormatEx>()); Marshal.StructureToPtr(format, pFormat, false);
Guid session = Guid.Empty;
const uint LOOPBACK = 0x00020000, AUTOCONVERTPCM = 0x80000000, SRC_DEFAULT_QUALITY = 0x08000000;
client.Initialize(0, LOOPBACK | AUTOCONVERTPCM | SRC_DEFAULT_QUALITY, 2_000_000 /* 200 ms */, 0, pFormat, ref session);
Guid captureId = typeof(Loopback.IAudioCaptureClient).GUID;
client.GetService(ref captureId, out object captureObject);
var capture = (Loopback.IAudioCaptureClient)captureObject;

using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
using var output = new BinaryWriter(file);
void Header(long frames) {
    long bytes = frames * Block; file.Position = 0;
    output.Write("RIFF"u8); output.Write((int)(36 + bytes)); output.Write("WAVE"u8); output.Write("fmt "u8); output.Write(16);
    output.Write((short)1); output.Write((short)Channels); output.Write(Rate); output.Write(Rate * Block); output.Write((short)Block); output.Write((short)Bits);
    output.Write("data"u8); output.Write((int)bytes); file.Position = file.Length;
}
Header(0);
var clock = Stopwatch.StartNew(); DateTime startUtc = DateTime.UtcNow;
long written = 0, padded = 0, packets = 0, silentPackets = 0;
var zeros = new byte[Block * 4800];
void Pad(long frames) { while (frames > 0) { int n = (int)Math.Min(frames, zeros.Length / Block); output.Write(zeros, 0, n * Block); frames -= n; written += n; padded += n; } }
var buffer = new byte[Block * Rate];
client.Start();
Process target = null; try { target = Process.GetProcessById(pid); } catch { }
while (clock.Elapsed.TotalSeconds < seconds && !(target?.HasExited ?? true)) {
    capture.GetNextPacketSize(out uint next);
    if (next == 0) {
        // Nothing queued: keep the file on the wall clock once a gap is longer than 30 ms.
        long due = (long)(clock.Elapsed.TotalSeconds * Rate);
        if (due - written > Rate * .03) Pad(due - written - Rate / 100);
        Thread.Sleep(5); continue;
    }
    while (next > 0) {
        capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
        int bytes = (int)frames * Block;
        if ((flags & 2) != 0 || data == IntPtr.Zero) { Pad(frames); padded -= frames; silentPackets++; }   // AUDCLNT_BUFFERFLAGS_SILENT
        else { if (buffer.Length < bytes) buffer = new byte[bytes]; Marshal.Copy(data, buffer, 0, bytes); output.Write(buffer, 0, bytes); written += frames; }
        capture.ReleaseBuffer(frames); packets++;
        capture.GetNextPacketSize(out next);
    }
}
client.Stop();
{ long due = (long)(clock.Elapsed.TotalSeconds * Rate); if (due > written) Pad(due - written); }
Header(written); output.Flush();
Marshal.FreeHGlobal(pFormat);
var info = new { pid, path, startUtc = startUtc.ToString("O"), sampleRate = Rate, channels = Channels, bitsPerSample = Bits, frames = written,
    seconds = written / (double)Rate, paddedFrames = padded, packets, silentPackets };
File.WriteAllText(path + ".json", JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(info));
return 0;

static class Loopback {
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WaveFormatEx { public ushort wFormatTag; public ushort nChannels; public int nSamplesPerSec; public int nAvgBytesPerSec; public ushort nBlockAlign; public ushort wBitsPerSample; public ushort cbSize; }
    [StructLayout(LayoutKind.Sequential)]
    struct ActivationParams { public int ActivationType; public int TargetProcessId; public int ProcessLoopbackMode; }
    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public ushort vt, r1, r2, r3; public int cbSize; public IntPtr pBlobData; }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient {
        void Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, ref Guid audioSessionGuid);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceAsyncOperation {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }
    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceCompletionHandler {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }
    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject { }

    [ComVisible(true)]
    sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject {
        public readonly ManualResetEventSlim Done = new();
        public int Result; public object Interface;
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation) {
            operation.GetActivateResult(out Result, out Interface); Done.Set();
        }
    }
    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid riid, IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation operation);

    public static IAudioClient Activate(int pid) {
        var p = new ActivationParams { ActivationType = 1 /* PROCESS_LOOPBACK */, TargetProcessId = pid, ProcessLoopbackMode = 0 /* include tree */ };
        IntPtr pParams = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>()); Marshal.StructureToPtr(p, pParams, false);
        var blob = new Blob { vt = 65 /* VT_BLOB */, cbSize = Marshal.SizeOf<ActivationParams>(), pBlobData = pParams };
        IntPtr pVariant = Marshal.AllocHGlobal(Marshal.SizeOf<Blob>()); Marshal.StructureToPtr(blob, pVariant, false);
        try {
            var done = new Completion(); Guid iid = typeof(IAudioClient).GUID;
            ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, pVariant, done, out _);
            if (!done.Done.Wait(10000)) throw new TimeoutException("process loopback activation did not complete");
            if (done.Result != 0) Marshal.ThrowExceptionForHR(done.Result);
            return (IAudioClient)done.Interface;
        }
        finally { Marshal.FreeHGlobal(pVariant); Marshal.FreeHGlobal(pParams); }
    }
}
