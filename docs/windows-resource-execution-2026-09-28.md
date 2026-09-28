# Resource execution on Windows

VPS code editing uses `/home/dev/source-sync/ScCsgoKnives`. Windows source and complete resources reside at `E:/projects/ScCsgoKnives` and `E:/projects/CSMCReverse`.

Large models, textures, audio, exported raw resources and full build outputs remain on Windows. Submit their parsing/conversion/build through `win-worker` with Windows absolute paths and a unique request ID; receive small logs, JSON, hashes and optional preview images. Do not copy the resource tree back to the VPS to satisfy local relative paths.

The VPS source checkout is not a standalone complete build tree. Source-only checks can run locally if their dependencies are present; resource-dependent work runs on Windows. Check worker health first. Windows offline means resource work must wait, not that missing assets can be replaced with placeholders.

Remote execution was verified on 2026-09-28 with job `3c02b335fdd549a98e5cf0a58cafd4be`: Windows parsed a real local GLB, returning 461 bytes of structured output without transferring its 605,112-byte payload to VPS storage.

Cleanup/archival manifests for old VPS resource and output copies: `E:/projects/vps-offload-20260928`. Current source synchronization and single-writer handoff rules remain in effect.
