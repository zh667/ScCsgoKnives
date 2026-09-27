# Isolated managed resource codec

This project builds the unmodified ZstdSharp.Port 0.8.8 source under the distinct
assembly identity **ScCsgoResourceCodec, Version=0.8.8.0**. Keeping the upstream
namespace is safe because CLR type identity also includes the assembly. There
is no global AssemblyResolve alias for third-party ZstdSharp assemblies.

Source: https://github.com/oleg-st/ZstdSharp/tree/2cd0c019693bc786a5fe5c3be94e107b24e7267e

The commit is the repository commit recorded by the NuGet 0.8.8 package.
Run tools/prepare_codec_release.py through tools/dev.ps1 to fetch and verify
the pinned archive into the project-local .tmp directory. Its SHA-256 is
7c2f6dc348f9ba4017e858f40b2c32f570148a8a4d98e96a74896c9723d41916.
The staging tool checks resource inputs as well; see the release document.

The build retains upstream Fody 6.9.1 / InlineMethod.Fody 0.8.4 inlining. These
are build-only dependencies and are not shipped. The target is the existing
game/mod .NET 10 runtime; no native Android .so is introduced. Upstream MIT
license text is bundled at Licenses/ZstdSharp-MIT.txt in the Lite package.
Original ZstdSharp.dll and the isolated library can coexist; both the accelerated
and disabled-hardware-intrinsics decode paths are exercised on Windows.
Android device/AOT acceptance remains separate.

Production envelopes use SCZSTD01, little-endian decoded/compressed lengths,
32 bytes of SHA-256 of decoded data, then one Zstd frame. Maximum compressed
and decoded resource sizes are each 64 MiB. There is no quantization, schema
conversion, per-frame decode, native library, or persistent raw decoded cache.
The owning reader disposes the temporary decoded stream after constructing the
existing model/animation objects.
