# Full 1.3.0 NPC crowd optimization — 2026-09-26

The supplied game log identifies NPC weapon rendering as the main measured CS
crowd cost, rather than squad entity creation. This release replaces repeated
CPU vertex transformation/copying with deferred GPU draws from immutable buffers
and ships lossless native world-geometry caches. No gun/actor precision, movable
parts, animations, population, combat behavior, IDs or saved state are reduced.

## Artifact and scope

- `output/[API1.9]CS武器1.3.0-全量包.scmod`: 522,864,645 bytes.
- SHA256: `0c4f2fddc79b56b90a701e892fe9e52195bb02cbecfc1b2ee67aeb1cc2926929`.
- Internal diagnostic revision: `crowd-gpu-20260926`; public metadata stays 1.3.0.
- Replaces full diagnostic SHA `3760e94fde9e3b9dff4e96a9a47240b39c677d13ffc791d9d213b72cce8721a9`.
- Only `ScCsgoTactical.dll` changes among existing archive entries; 63 new
  `Assets/Models/ScCsgoTactical/Weapons/*.scmesh` caches add about 29.8 MB compressed.
  All other 1,633 entries retain their original compressed bytes.
- Core DLL remains `dd8120ff30f07e4ec32c474304bd98077c424888262def3261d0d545126e44fc`.
  Existing uncommitted Zeus source changes were not rebuilt or included.
- Lite remains `bfa85bcf02cd40246b883099367ee7a3a8bd7bac8c1e8b1602f5f8a8d596c9d7`.
  Installed Mods, third-party packages and player worlds were not modified.
  v5/schema6/rules7, compatibility family 1 and manual-backup policy are unchanged.

## Evidence and implementation

Input: the user's Windows API 1.9.3.1 / RTX 3060 Laptop session, copied to
`.tmp/crowd-perf-20260926/session-2105/Game.log`, SHA256
`c6af2e1a3ba126e8aff1e6b84702b50a23a8a1c809e0e800c1b81e0df87fa459`.
Three/five entity creation took 6.24–8.45 ms. In a stable 28-actor window,
weapon rendering averaged 6.70 ms/frame against enemy AI's 0.25 ms/frame.
The spawn-heavy window recorded about 3 GB of cumulative weapon draw allocations
(not simultaneously resident memory), with a 298.15 ms individual weapon draw.
The engine's DrawMeshBlock exact-size batch expansion explains this scaling.

`ScNpcWeaponGeometry` reads bounded lossless binary caches for 35 standard and
28 legacy gun geometries. They are built using native `ScThirdPersonWeapon.For`
with the actual OBJ content reader and native lighting initialization. The
headless OBJ helper is unsuitable: it omits native directional vertex colors and
double-sided triangle expansion. Every stored vertex, index, bind/world matrix,
visibility flag and texture key is compared to the native source. Invalid caches
fall back to the original build path; unsupported legacy geometry also restores
its matching factory material. Legacy NPC skins no longer decode duplicate OBJ
GPU models when a valid cache already supplies their geometry.

`ScNpcWeaponRenderer` attaches a custom layer-0 batch to each native primitives
renderer. The engine retains control of flush timing, camera projection and
clearing. Commands contain transforms, texture, light and sampler; the unchanged
mesh is uploaded once. The shader preserves emissive vertices, byte-truncated
lighting, cutout alpha, native depth/blend/cull state and PointClamp/LinearWrap.
Index buffers use 16 bits where lossless. Temporary upload arrays are discarded;
device reset regenerates them from the retained source. World disposal explicitly
removes batches and disposes GPU resources, including the shader. Commands for
different renderers/cameras remain independent. Driver rejection uses the native
CPU path with geometric capacity growth instead of exact-size reallocations.

`WeaponDraw` now includes queue preparation; new `WeaponUpload` and `WeaponSubmit`
scopes expose upload and deferred submission costs. Scopes remain inclusive CPU
wall timings, not GPU timers. Use submission alongside draw when reading logs.

## Controlled measurements

Windows / NVIDIA OpenGL ES 3.2 isolated native fixture, five alternating gun types,
384×384 target, existing textures, 30 repeated frames. Includes primitive flush;
completed timings add a readback fence. These are weapon-only probes, not game FPS
or a direct replay of the user's 28-NPC scene.

| Weapons | Old/new cold draw ms | Old/new cumulative cold allocation | Old/new warm completed ms/frame |
|---|---:|---:|---:|
| 1 | 4.05 / 7.79 | 10.38 / 2.61 MB | 2.85 / 0.16 |
| 3 | 8.97 / 5.41 | 34.75 / 7.30 MB | 5.67 / 0.06 |
| 5 | 15.67 / 11.24 | 88.96 / 9.73 MB | 6.88 / 0.10 |
| 28 | 402.48 / 9.57 | 1,842.02 / 9.81 MB | 43.38 / 0.34 |

Cold GPU rows include shader creation and buffer upload; geometry and texture
loading are separate. The single-weapon cold result is slower because of initial
shader setup. Warm queue/flush loops allocated zero managed bytes in this fixture;
the rest of NPC gameplay/animation and the integration's presentation code still
allocate. Geometry read vs original native build: AK 17.44 vs 248.02 ms, AWP 7.40
vs 117.72 ms, MP9 0.91 vs 32.13 ms. These exclude ZIP decompression and texture
decoding; first JIT and resource warming affect absolute timings.

## Validation

All checks passed on the delivered DLLs and candidate archive:

- 3,211 native weapon checks: 63 exact geometry round trips and package matches,
  malformed/truncated input rejection, 189 old/new gun images at three light
  levels, emissive/alpha/sampling probes, device loss/reset, independent renderer
  queues, world disposal and reconstruction. Image thresholds allow rare edge
  rasterization differences from moving transforms to the GPU; they do not require
  every rendered pixel to be bit-identical.
- 15 tactical AI/squad/rollback/state checks; existing diagnostics lifecycle probe.
- 7,892 appearance checks, including action contacts; 12 native actor frames.
- 12 resource gate and 6 native compatibility checks; 243 family compatibility
  checks with the 1.0/1.2 compatibility revisions and unchanged latest core.
- Official NekoMeko 1.1: both/reversed/none/NMM-only/Neo-only/disabled/outdated
  dependency cases all pass (20/20/14/14/14/14/14 checks).
- Package CRC, preserved compressed members, public version, core hash and
  unchanged Lite hash verified before publication. Full evidence is in the
  adjacent `-evidence.json`.

Reproduce using `tools/dev.ps1`: build tactical and native tools with
`-p:BuildProjectReferences=false -p:SkipScmodPackaging=true`, run
`NpcWeaponCheck` to generate/validate native caches, stage with
`tools/package_tactical_optimization.py`, run
`pwsh -NoProfile -File tools/check_tactical_optimization.ps1`, then publish with
the packaging script's `--publish`. The source package SHA is intentionally pinned.
The previous full delivery remains in `.tmp/crowd-opt-20260926/previous-full.scmod`.

Android acceptance remains pending. Next device run should cover first and repeated
three/five-person beacon summons, accumulated crowds, companion skin changes,
CT/T model selection, leaving/reentering that world and background/resume. Collect
the new `[CS_PERF] build=crowd-gpu-20260926` logs; texture decoding, actor animation,
terrain, other mods and mobile GPU throughput remain separate possible costs.
