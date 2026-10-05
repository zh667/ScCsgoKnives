# Task memory-residency-20261001: resource residency of the CS packages

Status: r7b delivered (2026-10-01): desktop-verified on isolated 1.9.3.1 and measured on the user's phone (user-authorized install); r7a superseded (Android install exception, decoded pixel memory not freed)
Current writer: VPS Claude (src/ScCsgoKnives/Rendering/ScLazyContent.cs, ScTextureResidency.cs, ScMemoryReport.cs,
Mod/ScCsgoKnivesModLoader.cs, tools/PackageCheck/ResidencyRegression.cs + Program.cs, tools/MpM0/sp_memory.py,
sp_residency_check.py, this file)
User-authorized scope: the 2026-10-01 memory brief (ad27): lower startup residency, cache growth after many guns/skins and
load peaks without removing accepted features, lowering Full quality, changing package identity or breaking bidirectional
compatibility; no platform/third-party patching, no global ContentManager Clear/Dispose, no frequent forced GC, no black
textures after background; standalone tests on isolated SurvivalcraftAPI 1.9.3.1 only (no 1.9.3.2_MP); after the gates the
standing output-replacement authorization applies; no install, no public release, no player world writes.
Out of scope: package-size compression for its own sake, Mini scope, new player settings, multiplayer.
Background review: [memory-review-20261001.md](memory-review-20261001.md) (Windows Codex, static).

## User requirements (from the brief, kept verbatim in meaning)

1. Verify the actual latest output/source identity first; single writer and sync state.
2. Step 1: minimal baseline on isolated 1.9.3.1: resource streams, decoded images, textures, animations, geometry,
   background tasks; exact vs estimated bytes; no double counting; scenario menu → world → first gun → fixed gun/skin set
   → repeat → CT/T → exit → re-enter; memory, cache sizes, load counts, first-use / repeat switch times.
3. Step 2: a controlled prototype on one representative own resource category (load on demand, hold, release, reload);
   do not assume ContentInfo accepts an arbitrary lazy Stream; no platform/third-party rewrite; if the native contract
   blocks it, state the limit and evaluate alternatives (inner lossless compression), with what each reduces.
4. Step 3: byte- and lifetime-based cache management: unified CPU/GPU/encoded/temporary accounting, shared ownership and
   in-use protection, evict only confirmed idle rebuildable data, working set / delay / watermarks from measurements,
   bounded scheduling with logged reasons, check every reference before clearing; no global Clear/Dispose, no frequent GC;
   CPU image release only according to the device-restore path, keep a re-readable source, no black textures.
5. Step 4: verify first/repeat use, fast switching, shared resources in use, exit/re-enter, exit during loading,
   background resume / graphics rebuild, decode failure fallback; pixels/models/animations/sounds not degraded;
   damage/ammo/saves unchanged; before/after table on the same input; Android needs device evidence (untested here).
6. User addition (2026-10-01): "可以打日志辅助判断" — logs that help judge memory ([CS_MEM]).

## Identity at start (2026-10-01 13:20 JST)

- Output = r6x (manifest revision round6-smoke-surface-duration-settings-agents-hint-20261001): 全量 e0ccfff98b57…
  526,731,361 B; 轻量 4c0d81d0a69a… 35,604,693 B; 探员 0dea4191c1ee… 38,464,758 B (Windows job a26389fead664bb289a179d2a7a06507).
- Source = r6x source-hashes except tools/completion_140.py (BASELINES update after delivery). Syncthing idle, 0 errors.

## Engine facts (decompiled, not inferred)

Desktop 1.9.3.1 (NuGet Survivalcraft/Engine 1.9.3.1) and Android 1.9.2.2 (Engine/Survivalcraft extracted from
SurvivalcraftApi-Android-arm64-1.9.2.2-Signed.Apk) share all of these:
- ModsManager.GetDecipherStream reads the whole .scmod into a byte[] and copies it into a MemoryStream that stays
  ModEntity.ModArchive.ZipFileStream for the life of the process (MemoryStream growth: 512 MiB for the 502 MiB Full file,
  64 MiB each for Lite 34 MiB / agents 37 MiB).
- ModEntity.CombineContent extracts every Assets/ member into its own MemoryStream (ContentInfo.ContentStream), before any
  mod assembly runs (load order: CombineContent → ContentLoaded → HandleAssembly). Our code cannot change the loading
  peak while the members sit under Assets/.
- ContentInfo.SetContentStream requires a MemoryStream; Duplicate() hands out that same instance at position 0 and needs
  CanRead && CanWrite. Readers share its position. Engine readers close some streams (glTF/OBJ models, Ogg): a closed
  MemoryStream keeps its buffer.
- Texture2DReader decodes through ContentManager.Get<Image> (cached under the file name) and Texture2D.Load(image) sets
  Tag = image; nothing reads that Tag for these textures. Texture2D.HandleDeviceReset only allocates an empty texture;
  GLWrapper.HandleContextLost has no caller in either engine (Android: Engine, Survivalcraft and two other app assemblies
  searched). ContentManager.Display_DeviceReset re-gets the cached object (a no-op).

## Design (implemented)

- ScLazyContent (core): on the first main-menu entry (after every mod's loading actions) each enabled CS package
  (zh667.ScCsgoKnives/Tactical/Resources/Appearance/Voice) gets a ScPackageSource over its file (own read handle, shared
  read/write/delete). The engine copy is verified to be the file's bytes (size + first/last 64 KiB + 16 blocks); an
  enciphered package is left as loaded. Every Assets member of ≥ 4 KiB whose engine copy came from this package
  (ModList order, as CombineContent) gets a ScLazyContentStream (a MemoryStream subclass overriding every data member) in
  its ContentInfo; the package copy (ModArchive.ZipFileStream) becomes a file stream. A member is read when a reader asks
  (zip local header check, inflate, CRC-32 check), kept while read (2 s), released above 64 MiB down to 32 MiB or after
  20 s idle; GetBuffer/TryGetBuffer pin it; a write makes an owned copy; Dispose closes it like a MemoryStream and drops
  the bytes. A package changed on disk while running fails its CRC and is reported, never returned.
- ScTextureResidency: once a CS texture (Textures/ScCsgoKnives, Textures/ScCsgoTactical) is on the GPU, its decoded Image
  is removed from ContentManager.Caches and the texture's Tag and disposed; on Display.DeviceReset every CS texture is
  uploaded again (from Tag or re-decoded from the package member).
- ScMemoryReport: [CS_MEM] lines (install, world, exit, background, resume, periodic on change) with lazy/engine/texture/
  GC/process (VmRSS/RssAnon/RssFile on Android) figures.
- SCCS_RESIDENCY=off|textures|full (environment, default full): A/B diagnostics only, not a player setting.

## Evidence

Measurement: tools/MpM0/sp_memory.py on the isolated 1.9.3.1 copy (engine 2.4.0.0 / API 1.9.3.1, Windows 11 desktop,
RTX 3060 Laptop), one session per package set, fixed scenario (menu → new world → first gun → 19 guns/skins/knives →
same 19 again → CT and T in view → exit → re-enter → first gun). Every stage is measured as it is and after a
measurement-only full GC (the product never forces one during play). Exact: package-copy and stream capacities (bytes the
MemoryStreams hold), decoded Image bytes (W×H×4 Rgba32), GC live/heap/committed/fragmented, process private bytes and
working set. Estimated: GPU bytes (the engine's own size formula; driver overhead, mipmaps beyond level 0 of other
resources and GPU-side allocation granularity not included).

### Baseline r6x (2026-10-01, results/memory-r6x-full-b.json, memory-r6x-lite-agents.json)

| r6x, after GC | Full: menu | Full: after 2 rounds | Lite+agents: menu | Lite+agents: after 2 rounds |
|---|---:|---:|---:|---:|
| CS package copies (ModArchive.ZipFileStream) | 512 MiB | 512 MiB | 128 MiB | 128 MiB |
| CS Assets streams held (logical 593.5 / 94 MiB) | 874 MiB | 874 MiB | 135 MiB | 135 MiB |
| decoded CPU images of CS textures | 3 MiB | 269 MiB | 3 MiB | 27 MiB |
| CS textures on the GPU (estimate) | 3 MiB | 269 MiB | 3 MiB | 27 MiB |
| GC live bytes | 1549 MiB | 1708 MiB | 425 MiB | 566 MiB |
| process private bytes | 2354 MiB | 3064 MiB | 998 MiB | 1159 MiB |

Other facts: model streams are closed by the engine's readers after use but their buffers stay (34 after round 1);
the full package also loads ScCsgoResources.dll (134.7 MB, embedded AnimationData) as an assembly image that no mod can
release; own caches are count-bounded (animations 40, rigid 40, skinned 40, NPC weapons 80, third-person/drop 12) and held
10/9/10/5 entries in this scenario. Switches (longest frame in the 1.5 s after a switch, Full): first use mean 38–45 ms,
median 20–26 ms, worst 189–211 ms; repeat mean 12–19 ms, median ≈ 10 ms.

### Prototype and first full build (stage m1, dev packages sp-m1-*, not delivered)

- Texture members only (SCCS_RESIDENCY=textures; the package copy is released in this mode too): Full streams held 874 →
  322 MiB (models, animations, audio left), GC live bytes at the menu 1549 → 485 MiB (−1064 MiB = 552 + 512), but heap only
  1801 → 1252 MiB and private 2354 → 1783 MiB: the freed space stayed committed as LOH fragmentation (768 MiB).
- Full mode: streams 0 MiB, decoded 0 MiB (269 MiB released after upload, textures stay on the GPU), private bytes after two
  rounds 3064 → 2191 MiB; switch times unchanged within noise (first use mean 31 ms, median 18 ms, worst 255 ms; repeat
  mean 13 ms); lazy loads 124 members / 62.5 MiB / 272 ms in total, peak held 37 MiB (budget 64).
- Lite+agents full mode: streams 135 → 0 MiB, decoded 27 → 0 MiB, private after two rounds 1173 → 1056 MiB; repeat switches
  showed 4 isolated 60–90 ms frames in 19 windows (r6x 0); to be re-measured back to back (r7a, twice each).
- Residency acceptance (sp_residency_check): install, decoded-copy release, background/resume lines, decode-failure
  fallback, fast switching, exit during preparation all passed on Full and Lite+agents; the device-reset check's negative
  control was wrong (it emptied one texture key, the AK skin draws from another) and was changed to empty every CS texture.
- Fragmentation answer: one compacting collection after the install (main menu, once per session, logged).
- Review fix before r7a: lock order (a trim took a stream's lock under the budget lock while a reader takes them the other
  way round) → the budget list keeps each stream's bytes itself; concurrent-reader regression added (2 M reads, 0 wrong).

### Release candidate r7a (delivered)

Pipeline r7-pipeline-r7a-all-01 (Windows job, 7 min): failed steps []; main-full 13968 / main-lite 14274 checks, 0 failed
(12 new residency checks each: members read again = the engine's bytes for this package, decoded pixels identical,
ContentInfo contract, seeks, pin, owned copy, dispose, budget, concurrent readers 2 M reads / 0 wrong, changed-on-disk
report, enciphered copy refused); tactical-full-suite the same 4 known failures as r6x, no others. Baseline runs of the
current PackageCheck against the old r6x packages crash without a report, as they did in r6x (not a gate).

Before/after on the same scenario, r6x vs r7a back to back (results/memory-cmp-*.json), after the measurement GC:

| | Full r6x | Full r7a | Δ | Lite+agents r6x | Lite+agents r7a | Δ |
|---|---:|---:|---:|---:|---:|---:|
| menu: CS package copies / streams held | 512 / 875 MiB | 0 / 0 | −1387 MiB | 128 / 135 MiB | 0 / 0 | −263 MiB |
| menu: GC live / heap | 1550 / 1845 MiB | 163 / 459 | −1386 heap | 425 / 543 | 163 / 407 | −136 heap |
| menu: private bytes | 2382 MiB | 975 MiB | **−1407 MiB** | 998 MiB | 849 MiB | **−149 MiB** |
| after 2 rounds: decoded CPU images | 273 MiB | 0 | −273 | 27 MiB | 0 | −27 |
| after 2 rounds: CS GPU textures (estimate) | 273 MiB | 269 MiB | ≈ 0 | 27 MiB | 27 MiB | 0 |
| after 2 rounds: private bytes | 3152 MiB | 1711 MiB | **−1441 MiB** | 1159 MiB | 1020 MiB | **−139 MiB** |
| exit → re-enter: private bytes | 3122 → 3217 | 1652 → 1738 | −1470 / −1479 | 1147 → 1188 | 998 → 1049 | −149 / −139 |
| process peak working set (incl. loading) | 2396 MiB | 2154 MiB | −242 | 1234 MiB | 1048 MiB | −186 |
| first-use switch: mean / median / worst | 30.9 / 18.6 / 245 ms | 37.9 / 19.5 / 225 ms | noise | 37.8–32.9 / 22.3–22.5 / 111–93 | 29.8–37.6 / 22.2–21.8 / 83–138 | noise |
| repeat switch: mean / worst, frames > 50 ms | 10.3 / 18 ms, 0 | 17.9 / 71 ms, 2 | see note | 12.4–11.0 / 16–15, 0 | 12.3–13.2 / 31–24, 0 | noise |
| process start → main menu | 10.2–11.7 s (3 runs) | 10.8–11.7 s | overlap | 5.4–6.9 s | 5.1–8.1 s | overlap |

Install on the main menu (measured in Game.log): Full 184 ms (of it the one compacting collection 169 ms, heap 1926 → 449
MiB), Lite+agents 96 ms (81 ms, 532 → 397 MiB). Lazy reads over a whole Full session: 124 members, 62.5 MiB, 330 ms in
total, peak 37 MiB held (budget 64); none during exit/re-enter. Own caches (deep size): animations 10 entries ≈ 1.5 MiB,
third-person weapons 4–7 ≈ 7–18 MiB, skinned 10 ≈ 2–3.5 MiB, rigid 9 ≈ 1.7–3 MiB → the count limits stay; a byte budget
would not change anything measurable. Repeat-switch note: r7a Full had two frames of 55/71 ms in round 2 with 6 small
lazy reads in that round and one 70 ms frame after re-entering with no lazy read at all; r6x showed 66–83 ms repeat frames
in its first baseline run and none in two others, Lite+agents (two pairs) shows none on either side: no systematic
difference attributable to the change, single-run noise in this range.

Acceptance on the candidates (isolated 1.9.3.1): residency 7/7 on Full and on Lite+agents (device-reset check: all CS
textures emptied → frame differs strongly A/C 21.1 / 36.7; restored → A/D 0.30 / 2.41 vs frame noise 0.32 / 2.19; previews
run/residency-r7a-full-*/preview-ACDE.jpg viewed: emptied gun and hands black, restored identical, resumed identical);
agents hint 6/6; camera-fire 27/27 twice (default, and SCCS_RESIDENCY=off on the same package). MP compatibility probe
(1.9.3.2_MP, full candidate) 6/6, residency installed there, no package kept as loaded.

Camera-fire run 1 on r7a: 9/27 — every check that needs the injected fire button to shoot failed for the whole session,
the key binding fired. Cause (sp_button_order.py, 4/4): the harness calls SetFireButton directly, while
SubsystemScKnifeBlockBehavior writes SetFireButton(player, touch panel Fire pressed) on every update; both subsystems are
UpdateOrder.Default and SubsystemUpdate.Comparer breaks the tie by GetHashCode(), so the order is random per session; with
the knife subsystem first the injected press is overwritten before the gun reads it (direct button: no shot; key: shot),
with the gun first it fires. A real touch keeps the panel pressed, so players are not affected. Follow-up: make the
harness press the real on-screen Fire widget (MOUSEDOWN/UP at its bounds) instead of SetFireButton.

Delivered (output/, manifest release-1.4.0, stage .tmp/completion-140-20260929/r7a): 全量 9f1df6c9… 526,742,630 B;
轻量 a96805ec… 35,616,882 B; 探员 0dea4191… 38,464,758 B (unchanged). Members vs r6x: only ScCsgoKnives.dll changed in
Full and Lite (1708 / 1342 members identical: no resource byte, quality or identity change). Cleanup receipt:
memory-residency-20261001-cleanup.json (3.9 GB rebuildable intermediates removed, output unchanged).

## Requirement coverage

| Requirement | Implementation | Distinguishing check | Result |
|---|---|---|---|
| identity, single writer, sync | — | Windows job a26389fe; Syncthing idle | done |
| baseline with byte accounting, no double count | sp_memory.py (reflection only; same code before/after) | before/after table | done (desktop) |
| prototype on one category, contract limits | SCCS_RESIDENCY=textures run; MemoryStream subclass because ContentInfo requires MemoryStream + CanWrite | m1 textures run; residency regression | done |
| startup residency | ScLazyContent (package copies + Assets streams) | menu private −1407 / −149 MiB | done after loading; the loading peak itself is a verified limit (below) |
| cache growth after many guns/skins | decoded copies released (ScTextureResidency) | decoded 273 → 0 MiB | done for CPU; GPU textures: limit (below) |
| byte/lifetime management, in-use protection, watermarks, logged | lazy budget 64/32 MiB, 2 s in use, 20 s idle, pins, [CS_MEM] | budget + concurrency regression | done for encoded data |
| no black textures after background, re-readable source | restore on Display.DeviceReset from the package member | residency check 3 with negative control | desktop-verified; Android untested |
| decode failure fallback | lazy read failure → IOException → factory finish | residency check 5 | done |
| first/repeat use, fast switch, exit/re-enter, exit during loading | — | sp_memory switches, residency checks 6/7 | done |
| quality, damage, ammo, saves unchanged | only the core DLL changed | member comparison, main suites 0 failures | done |
| logs for judging (user) | [CS_MEM] lines | seen in every run's Game.log | done |
| Android evidence | — | — | **not tested** (no device/installation authorization) |

## Verified limits and next steps (not done this round)

1. Loading peak: every Assets member is extracted and the whole package is read into memory (byte[] + MemoryStream) by the
   engine before any mod code runs; Full's process peak working set stays ≈ 2.15 GiB on desktop during loading. Only a
   package-layout change (large members outside Assets/, registered lazily by the loader) or smaller packages can lower it;
   the whole-file copy in GetDecipherStream remains in any case. Inner lossless compression would shrink models/animations
   (≈ 163 + 45 MB raw) but not PNG textures, and only that part of the peak.
2. GPU textures used so far stay resident (Full: 269 MiB after 19 items). Evicting idle ones needs every texture holder
   (≈ 40 static caches across first/third person, drops, PBR, arms, items) to resolve through one owner with in-use stamps;
   disposing a texture still referenced would draw black or crash, so it was not done piecemeal.
3. ScCsgoResources.dll (Full, 134.7 MB embedded AnimationData) is a loaded assembly image; no mod can release it.
4. Android: same engine code paths confirmed on 1.9.2.2 (APK) only statically; phone RSS/PSS, background kill behaviour
   and the actual saving need a device run. The [CS_MEM] lines report VmRSS/RssAnon/RssFile on Android.
5. Harness: camera-fire's direct SetFireButton injection is order-dependent (see above).

## Phone test (user-authorized install, Redmi Note 9 Pro M2007J17C, Android 12, 7.2 GiB, SurvivalcraftAPI 1.9.3.1 Android)

Connection: USB ADB kept dropping (offline) during transfers; wireless debugging (paired by the user) was stable.
Backups before any change: the phone's own 1.3.0 Lite/agents packages (MD5-verified) and settings files, on Windows
.tmp/phone-backup-20261001 and on the phone in Survivalcraft2.4_API1.9/CS-backup-20261001 (outside Mods). The player's
worlds (World, World1-3) were never opened; one test world (World4, seed 20261001, creative) was created for the
protocol. Protocol (dumpsys meminfo TOTAL PSS): cold start → main menu (+20 s) → load World4 → 2 knives + 8 guns in the
hotbar selected twice → Home 10 s → back → exit.

| TOTAL PSS MiB | 1.3.0 Lite+agents (user's) | r7a Lite+agents | r7a Full |
|---|---:|---:|---:|
| main menu | 849 | 872 * | 2483 * |
| world loaded | 979 | 833 | 1546 |
| after round 1 / 2 | 1039 / 1042 | 929 / 895 | 1855 / 1858 |
| background (Home) | 980 | 840 | killed |
| resumed | 1044 | 895 | (fresh start) |
| after exit | 1017 | 876 | — |

\* r7a on Android: GCSettings.LargeObjectHeapCompactionMode throws PlatformNotSupportedException on the Android runtime
(Mono); the exception aborted the install after the members were replaced, so the one collection never ran and the freed
copies stayed until the world loaded (Full menu 2.2 GiB managed). Fixed in r7b (setting attempted, exception tolerated,
install never throws into the hook).

Full on this phone: MIUI force-stopped the game 10 s after Home ("stop ... due to MiuiMemoryService", kill_bg_proc, PSS
1.73 GiB); during Full's loading lmkd killed other apps under critical pressure. Lite+agents survived background/resume
with intact textures (screenshot checked).

Correction to the desktop table above: "decoded CPU images 273 → 0 MiB" counts Image objects; their pixel memory was NOT
freed in r7a. The engine's Texture2D.Load(Image) → SetData(Image<Rgba32>) pins the ImageSharp buffer and never disposes
the handle; ImageSharp 3.1.12 then keeps 1024²/2048² buffers for good (Dispose suppresses the finalizer). Proven with
ImageSharp's own undisposed-allocation count; on the phone native heap grew +110 MiB in Full's first round while 154 MiB
of decoded copies were "released". r7b balances that one pin before disposing (new PackageCheck check) and returns
ImageSharp's pooled blocks after ≥ 8 MiB released (rate-limited). The desktop private-byte savings measured for r7a
come from the package copies and streams only.

## r7b (delivered) — phone and desktop results

Release r7-pipeline-r7b-all-01: failed steps []; main-full 13969 / main-lite 14275 checks, 0 failed (incl. the pixel-buffer
check). Residency acceptance 7/7 on Full (after making the test player invulnerable and still; run 1 failed only its
frame-noise precondition: damage tint and knock-back between A and A2, restore itself correct in the captures) and on
Lite+agents. Delivered: 全量 a710dd19… 526,743,339 B; 轻量 206f9298… 35,617,431 B; 探员 0dea4191… unchanged. Members vs
r7a: only ScCsgoKnives.dll. Cleanup receipt: memory-residency-20261001-r7b-cleanup.json (3.6 GiB).

Phone (same protocol, TOTAL PSS MiB, Android 1.9.3.1, Redmi Note 9 Pro):

| | 1.3.0 Lite+agents | r7b Lite+agents | Δ | r7a Full | r7b Full |
|---|---:|---:|---:|---:|---:|
| main menu | 849 | 676 | −173 (−20 %) | 2483 | 1315 |
| world loaded | 979 | 795 | −184 (−19 %) | 1546 | 1504 |
| after round 1 / 2 | 1039 / 1042 | 849 / 852 | −190 (−18 %) | 1855 / 1858 | 1712 / 1707 |
| background (Home) | 980 | 798 | −182 | killed | killed |
| resumed / after exit | 1044 / 1017 | 841 / 826 | −203 / −191 | — | — |

r7b on the phone: no error in Game.log; install 108 ms (Lite+agents, one collection 72 ms, heap 488 → 307 MiB) / 206 ms
(Full, 177 ms, heap 2192 → 764 MiB); 36 texture pins balanced, 2 pool trims; textures intact after background/resume
(Lite+agents). Full in a world (≈1.7 GiB) is still force-stopped by MIUI within 10 s of Home.

Desktop (isolated 1.9.3.1, private bytes after the measurement GC): Full r6x → r7b after two rounds 3152 → 1472 MiB
(−53 %), exit 3122 → 1425, re-enter 3217 → 1494; r7a → r7b −227…−244 MiB from the really freed pixel buffers. Main-menu
private bytes right after the install depend on when the GC hands committed memory back (r7b measured 8 s earlier: 1218
MiB committed at the menu, 531 by round 2); live/heap bytes are the stable figure there (Full menu heap 1845 → 360 MiB).
Lite+agents r6x → r7b after two rounds 1159–1173 → 1075 MiB.

## Where the remaining memory is (r7b) and what could still be saved

Ownership walk (desktop, every static field of the CS assemblies, deep size): after this round the CS mod's own retained
data is small — third-person weapon cache 4–7 MiB, rigid/skinned meshes 1–3 MiB, weapon textures referenced by the
renderers (GPU) — no single CS-owned CPU structure above 10 MiB. What remains is mostly:
1. GPU textures of weapons used this session (Full: ≈270 MiB after 19 items on desktop, phone Graphics 297 MiB after 10
   weapons; Lite+agents 27 MiB). Freeing idle ones needs one owner for the ≈40 static texture holders (first/third
   person, drops, PBR, arms, items) so a texture can be disposed only when nothing draws it — a structural change, the
   largest remaining lever for Full.
2. The Full loading peak (phone: 2.2 GiB managed before the install, critical memory pressure while loading): the engine
   reads the whole package and extracts every Assets member before any mod code; only a package-layout change (large
   members outside Assets/, registered lazily) can lower it.
3. CT/T preparation at startup (warmup allocates ≈ 85 MB: geometry ≈ 21 MB, animations ≈ 64 MB, for players who may not
   meet agents in a session). Deferring it would save that much but reintroduce a first-spawn hitch — a trade-off for
   the user to decide.
4. Not CS-owned (left alone by rule): the base game and other mods keep ≈ 27 MB of streams, ≈ 73 MB of decoded images and
   ≈ 94 MB of GPU textures (desktop measure); ScCsgoResources.dll (Full) is a 134.7 MB loaded assembly image.
Recommendation for phones from the measurement: Lite+agents (≈ 0.85 GiB in a world) survives background; Full (≈ 1.7 GiB)
is killed by MIUI in background on this 7.2 GiB device.

Phone state left: r7b Lite+agents installed (MD5-verified); the player's 1.3.0 packages in
Survivalcraft2.4_API1.9/CS-backup-20261001 (and on Windows .tmp/phone-backup-20261001); the test world removed; the
player's four worlds untouched (timestamps unchanged). Settings files were rewritten by the 1.4.0 game run as usual
(originals backed up). Wireless debugging was paired by the user (can be switched off on the phone).
