# Full 1.3.0 animation and loading optimization — 2026-09-26

The GPU-buffer release improved the user's actual desktop session: 41 NPCs had
8.26 ms average frame time and 0.53 ms/frame weapon preparation plus submission,
compared with 18.69 and 6.70 ms at 28 NPCs previously. These are different play
sessions, not controlled FPS benchmarks. The last minute had no frame over 50 ms,
but startup still included a 593.30 ms frame and actor animation allocated roughly
600–850 MB per 10-second window. The user requested the remaining optimizations
in one full build.

Input log copy: `.tmp/crowd-opt-20260926/session-2146/Game.log`, SHA256
`d99acc1c3d1ba37bbd057eebfe3fb152141e2c1630e798ed97bfed1f5482f32a`.
The process is Windows / RTX 3060 Laptop / API 1.9.3.1 with 30 enabled mods, not
an Android device. Its approximately 2.4 GB managed heap belongs to the whole
process. Startup errors in unrelated fertilizer/command/filename resources are
not changed by this release.

## Changes

- `ScActorSampler` compiles channel-to-bone bindings once per model/clip instead
  of allocating and expanding a dictionary on every native sample. It retains
  native vector/quaternion interpolation, forward/reverse loop seams, default
  transform components and each entity's independent playback time.
- The normal `OnAnimateModel` hook only handles our NPC component, after native
  parameter synchronization. Native controller updates still perform state
  transitions, interruption, events and completion. The supported single base
  layer samples into the existing output array during crossfades instead of
  allocating another 580/572-bone array each frame. Blend functions remain native.
  Root-motion configurations, participant-driven controllers and unsupported
  layer/IK/mask/driver features retain native behavior; controllers observed using
  root motion remain on the native path afterward to retain restoration semantics.
- `ScAgentActions` uses the same verified sampler for hold/reload/draw clips and
  caches prop-bone indices, removing repeated string construction during gun draw.
- NPC bone hierarchy traversal uses a cached parent-first array. Every bone is
  still computed, using the same restricted matrix multiply, model scale and
  skin/non-skin rules. Model replacement clears per-model state; assigning the
  same model preserves it. Existing once-per-frame camera guards remain.
- Main-thread loading actions prepare T/CT model geometry, animation caches and
  body textures, followed by the NPC weapon shader. World loading prepares the
  shader again after disposal. ContentManager retains ownership of actor models
  and textures. GPU resources are never created on a worker thread. This moves
  work into loading rather than eliminating its startup time. Gun textures remain
  lazy instead of eagerly loading every full-quality skin onto mobile devices.
- Logs add `AnimationUpdate`, `AnimationSample`, and bounded warmup-stage times.
  The existing weapon upload/submission counters remain. All timing scopes are
  CPU wall time; nesting must not be summed as independent costs.

## Native validation

`ActorSamplingCheck` passed 1,793 checks. It compares all T/CT clip samples against
the native player in forward/reverse loops, with exact nullable-matrix equality.
Unarmed and armed component runs cover gait changes, rapidly interrupted
transitions, reload overlays, death, repeated cameras and disabled animation.
All hierarchy matrices match the native traversal. Cache clearing/reconstruction,
main-thread warmup and shader disposal/recreation also pass, with no swallowed
hook errors. Native interpolation and controller update methods were checked
against the actual Engine assembly, not assumed from API source alone.

Controlled native fixture, 41 armed actors, 180 frames:

| Role | Native sampling path allocation/frame | Optimized allocation/frame | Native/optimized animation + hierarchy ms/frame |
|---|---:|---:|---:|
| CT | 997,284 B | 8,692 B | 2.79 / 1.53 |
| T | 445,887 B | 8,663 B | 1.68 / 1.13 |

The paired fixture toggles the sampling hook while retaining identical gameplay
state, hierarchy implementation and action overlays; this isolates sampling-path
savings, not all changes versus the previous binary. It is not total game frame
time. The raw compiled sampler allocated zero bytes over 1,000 warmed calls;
integration still allocates small native hook/state objects. Native state changes
can still allocate snapshots. No zero-allocation claim applies to the whole game.

The loading fixture measured first T/CT texture preparation at about 156/112 ms
and shader preparation at 11 ms. Geometry/animation were already loaded there;
the separate cold probe records those costs. These measurements explain specific
first-use work moved out of gameplay, but do not establish that the entire logged
593 ms startup frame has been eliminated.

Full delivery checks also passed:

- 3,211 weapon geometry/render/lifecycle checks with all 63 packaged caches.
- 22 performance/log lifecycle checks, now invoking the NPC animation hook.
- 15 AI/atomic squad/rollback/state checks.
- 7,892 appearance checks and 12 actor render frames.
- 12 resource gates, 6 native compatibility checks, 243 save-family checks.
- Official NekoMeko 1.1 in both/reversed/none/NMM-only/Neo-only/disabled/outdated
  dependency configurations, all passing.
- Archive CRC, unchanged compressed members, final DLL hashes and Lite hash.

## Delivery and reproducibility

`output/[API1.9]CS武器1.3.0-全量包.scmod`, 522,867,456 bytes.
SHA256: `a2922a8115fe010b564691115351667dba1c3327329fd1a36870184ffef10f02`.
Diagnostic revision: `crowd-smooth-20260926`; public version stays 1.3.0.

Only `ScCsgoTactical.dll` changes. All other 1,696 compressed archive entries,
including the GPU release's geometry caches, are byte-identical. Core remains
`dd8120ff30f07e4ec32c474304bd98077c424888262def3261d0d545126e44fc`; existing dirty
Zeus source was neither rebuilt nor committed. Lite remains
`bfa85bcf02cd40246b883099367ee7a3a8bd7bac8c1e8b1602f5f8a8d596c9d7`.
No changes to precision, animations, combat balance, spawn counts, IDs,
v5/schema6/rules7, compatibility family 1 or manual-backup policy. Installed Mods,
third-party packages and player worlds are untouched.

Build through `tools/dev.ps1`, with `BuildProjectReferences=false` and
`SkipScmodPackaging=true`. Stage with `tools/package_tactical_smoothing.py`, run
`pwsh -NoProfile -File tools/check_tactical_smoothing.ps1`, and publish with the
packaging script's `--publish`. Its source SHA is pinned to the prior GPU release.
The adjacent evidence JSON binds checks and measurements to the delivered DLLs.
Previous full is retained in `.tmp/crowd-smooth-20260926/previous-full.scmod`.

Android validation remains pending. This is a completed, validated implementation
of the identified CS hotspots, not a guarantee for every phone, terrain workload,
GPU driver or installed-mod combination. New gameplay logs should identify the
`crowd-smooth-20260926` revision and separate update/sample/submission and loading
warmup timings. No new player-world run was performed by the tooling.
