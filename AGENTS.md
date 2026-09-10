# Project conventions

- User-directed 2026-09-10 (0.41.10; 0.41.8/9 were test candidates): extend counter growth to Lv30, still 100 kills/level (3000 total).
  Lv0-10 stays unchanged, including zero spread/recoil and unlimited loaded-world bullet range from Lv10.
  Lv11-20 add base damage/capacity/rate +30%/+20%/+10% per level; Lv21-30 +50%/+30%/+15%.
  Durability stays +5% of base per level. Lv30: damage x10, capacity x6.5 (floor), fire rate x3.5, life x2.5.
  Skin base multiplier stays x1.5; Zeus remains one charge, 5s/2.5s/1s at Lv10/20/30. Reload clips are not sped up.
  Record schema 4 expands valid levels; keep schema 1/2/3 readers and verified world backup before conversion.
  Never bump item layout v5 or change weapon IDs. See docs/growth30-ghoul-test-0418.md.
  Cross-world gun snapshots are generic for providers retaining player XML: native creative slots omit Count,
  survival slots require it. Packet v3 supports arbitrary world paths; no guessing missing records or overwriting
  another holder. New identity seeds use a saved world UUID; keep older gun identities unchanged.

- User-directed 2026-09-09 (0.40.7): damage grows +10% of the applicable base per level, +100% at Lv10.
  A valid skin supplies a separate 1.5x base-damage multiplier (skin Lv10 = 3x factory Lv0). Other growth
  values remain unchanged. Counter installation unlocks leveling and starts counting at zero; no prior kills
  are backfilled. Applying, replacing or stripping a skin must preserve counter/kill/level/instance state.
  This supersedes old +50% damage and Zeus 225-at-max text, not the +50% durability/capacity rules.

- Narrow user authorization on 2026-09-08: fix the reported survival weapon-workbench missing drop and first-person muzzle particles being overwritten over water. This does not resume other blocked milestones or permit gun save-format changes. See `docs/workbench-muzzle-fixes-0371.md`.

- User-directed handover of 2026-09-09: the counter/growth/attribute/touch work line is ACTIVE and 0.40.0 implements it
  (attribute page, mobile mod settings and touch layout, kill-feed and gun-crosshair switches, gun-only StatTrak counter,
  ten-level growth at 100 kills a level to 1000, and the Zeus rebuild). Record schema is 3, with the schema 1 and 2
  converters kept; the item layout stamp is still 5 and gun variant order is still frozen. A world saved by 0.40.0 is
  refused by 0.39.1 and earlier, which is the required forward protection, not damage. Windows review 0.40.1 imports the
  official CS2 StatTrak module mesh/digit atlas and adds actual first-person/third-person/item rendering; see
  `docs/gun-stattrak-attachments-2026-09-09.md` for the exact missing paths and
  `docs/gun-counter-review-0401.md` for review fixes and remaining device acceptance. Historical five-level
  XP, 100-kill or 1600-kill caps, +10% damage, +100% durability and the Zeus 54/81 or 3-second charge are obsolete.

- Execution override, user-directed at 2026-09-08 00:22:15 +08:00 (Asia/Shanghai): only M4 gun durability is ACTIVE. M0 general work, M1, M1b, M2/F07, M3, M5, standalone scope/inspect work and their separate device acceptance are BLOCKED until the user explicitly resumes them. Keep existing features. M4-required identity/transaction/save validation, charge state, box integration and narrow fixes for regressions caused by M4 are in scope. Follow `docs/vps-m4-durability-plan-2026-09-08.md`. Later user authorization on 2026-09-08 adds migration from the only published source version 0.28.2: back up the whole world, preserve model/ammo/silencer and give full durability once. Do not guess internal 0.29–0.36 test formats or reclaim IDs. See `docs/official-0282-compatibility-0370.md`. Do not resume other milestones automatically after M4.

- Use CS2 resources and real skinned hands for all first-person weapons. Do not restore the CS:MC / block-hand runtime route or expose a switch that re-enables it. This is the user's standing preference from 2026-09-06.
- Keep gun and knife variant ordering stable: existing worlds store those indices.
- Before removing assets, verify first-person, inventory, dropped-item and shared-effect references. Preserve CS2 source extractions and resources shared with other projects. Record removed paths in a manifest; do not delete by a broad “CS” name match.
- Deliver versioned `.scmod` packages in the project-root `output/` directory. Package only assets present in the current source manifest, so incremental build leftovers do not return to the package.
- Report packaged-DLL checks separately from actual game testing; an offline render is not an in-game screenshot.

- Do not add an overload to a method a tool or regression binds to by name alone: `Type.GetMethod("Required")` throws
  AmbiguousMatchException the moment a second `Required` exists. Give the new shape its own name (`RequiredFor`).
- Self-tests run before BlocksManager has any blocks. A check that needs an item value builds it from a constant, never
  from `BlocksManager.GetBlockIndex`; production helpers that only need the data half tolerate a missing registry.

- Guns already have custom durability and a v5 instance registry since 0.35.0; M4 is still under correction and acceptance. The user's 2026-09-07 instructions supersede the prior all-items-no-durability preference for guns only. Complete gun wear, broken-but-retained guns and workbench repair per the current M4 plan. Keep `ScNoDurabilityBlock` protection against vanilla wear, including on guns; custom wear must never use vanilla damage bits. Test normal gameplay in new worlds, authorized migration in untouched 0.28.2 world copies, and persistence/upgrade protection from the 0.37.0 baseline per the policy below. Unsupported internal test formats remain out of scope; item transfers remain required.
- Follow the current community-feedback plan one milestone at a time. Third-person character and throwing poses are authored for vanilla limbs; only weapon-part animation and event timing reuse CS2 clips. Do not claim full CS2 character animation reproduction. Do not add distant-audio variants. Guns use the planned 1.5x survival damage; knife reach is planned as 2.2 light / 1.8 heavy. Headshot non-kills are yellow; all kills are red. These are implementation requirements, not claims that runtime changes already exist.

- Release Full and Lite together from the same DLL. Keep original-quality source textures; derive Lite 512px textures only while packaging, renormalizing normal maps. Preserve PackageName, item indices, animations and gameplay across editions. Install only one edition per game. This is the user’s preference from 2026-09-06.

# Gun save compatibility (user-directed 2026-09-08)

- Before changing gun identifiers, item encoding, registry/schema, load/save, durability/charge semantics or related inventory transactions, read [the complete compatibility policy](docs/gun-save-compatibility-policy-2026-09-08.md). It supersedes historical new-world-only/no-cross-version rules for guns. Only 0.28.2 has been publicly released; 0.37.0 is the protected forward baseline, not a claim of public release. Other milestones remain BLOCKED.
- Freeze existing gun variant IDs and v5 item meanings; do not reorder/reuse IDs or renumber instance references. Prefer registry extensions. Keep mod version, item layout (5) and record schema (1) separate; change data versions only when their structure/meaning changes, with explicit converters.
- Preserve supported old worlds' gun identity, ammo, silencer, durability, charge and recovery state. Full durability is a one-time initialization for the authorized 0.28.2 migration only. Never guess internal test formats, silently reset state, or treat question marks/new-world instructions as normal release compatibility.
- For required conversions: validate detached data and capacity, verify a full source-world backup before activation, convert completely, save items/records/version markers consistently, and make retry idempotent. Keep prior converters for skipped-version upgrades. Explicitly define parameter-change treatment, including fresh templates; do not discard excess ammunition.
- Unknown future layout/schema must be rejected before normal loading/autosave without rewriting source data or stamps. Existing gun disabling alone is not proof of this protection. Preserve corrupt raw data; only show a model when its identity is reliable. Do not promise arbitrary old DLLs can safely downgrade.
- Data-affecting releases require immutable 0.28.2/0.37.0 and later format fixtures, applicable holder/state checks, two save/reload rounds, retry/failure/unknown-format checks and documented evidence. Keep implemented behavior, planned protection, offline results and device acceptance distinct. Do not bump format or mark pending policy requirements complete merely because documentation was added.

# Two-peer Git workflow (user-directed 2026-09-08)

- Source, committed documentation and packaged assets travel through Git only. Each peer owns an independent checkout; do not synchronize a live repository or `.git` with Syncthing. External CS2 extractions, videos and logs may use a separate resource transfer directory. This rule does not itself stop an existing Syncthing service.
- Before work, run `git status` and preserve existing uncommitted work. Do not overwrite it, silently stash it, or commit another peer's work. Local status cannot tell you whether the other peer is working; coordinate ownership before touching the same files.
- Once the working tree is clean, run `git pull --ff-only origin main`. On divergence, inspect both histories and resolve deliberately; never force-reset to hide it. With unrelated uncommitted work, preserve it and use an isolated clean checkout if necessary.
- Complete, validate, commit and push the work before handing over. The receiving peer then pulls the commit. Include required plans/resources in explicit commits; untracked files do not travel through Git.
- Retire the old `tools/sync_git_from_origin.sh` reset-based handoff. Neither `git reset --mixed` nor `git restore` is a routine synchronization command. Restore/reset is reserved for explicit, backed-up recovery.

# PackageCheck runs headless

- User-directed 2026-09-10: keep development temporary files off the Windows system drive.
  Run shell build/test/Python tools through `./tools/dev.ps1 <command> <args>` on Windows.
  This scopes TEMP/TMP/TMPDIR to the project `.tmp/dev-temp` (E: in this checkout), not the whole OS.
  PackageCheck also selects this root automatically even when invoked directly, isolates each run,
  loads the package DLL from a stream and cleans up test fixtures on normal completion.
  `SC_CSGO_DEV_TEMP` may override the root; `SC_CSGO_KEEP_TEST_TEMP=1` retains a check's fixtures for diagnosis.
  Never clean the user's global Temp, game worlds or backups as part of this workflow.

`tools/PackageCheck` needs no window or GPU. Its only host requirement was `Engine.Dispatcher.Initialize()`,
which `Program.cs` calls before loading the package (0.29.0): without it the finalizers of engine objects the
regressions create through `GetUninitializedObject` post to an uninitialized Dispatcher and kill the process
mid-run. On the VPS run
`dotnet tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll --scmod <pkg> --sha256 <sha> --vanilla-content <Content.zip> --json <out>`.
Do not add a "skip GPU tests" switch; nothing in the check set draws.

# Textures and threads

- `Engine.Graphics.Texture2D.Load` creates the GL object on the calling thread with no dispatch and no check, and `ContentManager` caches the result. Any texture a placeable block needs in `GenerateTerrainVertices` (terrain worker thread) must be resolved on the main thread first, in `Block.Initialize()`, and read from a field afterwards. A worker thread that first-touches `ContentManager.Get<Texture2D>` gets a broken texture that then draws black everywhere for the session (0.26.1-0.28.2 supply icons and the placed bench).
