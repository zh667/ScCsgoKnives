# Project conventions

- Execution override, user-directed at 2026-09-08 00:22:15 +08:00 (Asia/Shanghai): only M4 gun durability is ACTIVE. M0 general work, M1, M1b, M2/F07, M3, M5, standalone scope/inspect work and their separate device acceptance are BLOCKED until the user explicitly resumes them. Keep existing features. M4-required identity/transaction/save validation, charge state, box integration and narrow fixes for regressions caused by M4 are in scope. Follow `docs/vps-m4-durability-plan-2026-09-08.md`. Later user authorization on 2026-09-08 adds migration from the only published source version 0.28.2: back up the whole world, preserve model/ammo/silencer and give full durability once. Do not guess internal 0.29–0.36 test formats or reclaim IDs. See `docs/official-0282-compatibility-0370.md`. Do not resume other milestones automatically after M4.

- Use CS2 resources and real skinned hands for all first-person weapons. Do not restore the CS:MC / block-hand runtime route or expose a switch that re-enables it. This is the user's standing preference from 2026-09-06.
- Keep gun and knife variant ordering stable: existing worlds store those indices.
- Before removing assets, verify first-person, inventory, dropped-item and shared-effect references. Preserve CS2 source extractions and resources shared with other projects. Record removed paths in a manifest; do not delete by a broad “CS” name match.
- Deliver versioned `.scmod` packages in the project-root `output/` directory. Package only assets present in the current source manifest, so incremental build leftovers do not return to the package.
- Report packaged-DLL checks separately from actual game testing; an offline render is not an in-game screenshot.

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

# Two-peer git sync (Windows Codex + VPS Claude)

- The working tree is shared by Syncthing; `.git` is peer-local (`.stignore`) and must stay that way. Commits travel only through `origin`.
- The peer that does the work commits and pushes `main` (the fix/cs2-only-hands-0.20.4 branch was fast-forwarded into it at 0.28.2) at the end of every version, before handing over. Uncommitted work is invisible to the other peer's git even though its files are already there.
- Before starting anything, the other peer runs `git fetch origin` and `git reset --mixed origin/<branch>` (VPS: `bash tools/sync_git_from_origin.sh`). That moves HEAD and the index to the pushed commit without touching files, so `git status` shows only what is genuinely uncommitted on the other side. Never `git pull` / `merge` into a tree the other peer has already updated, and never commit the other peer's uncommitted files.
- Never edit the same file on both peers at the same time; check `git status` for the other peer's in-progress files first.

# PackageCheck runs headless

`tools/PackageCheck` needs no window or GPU. Its only host requirement was `Engine.Dispatcher.Initialize()`,
which `Program.cs` calls before loading the package (0.29.0): without it the finalizers of engine objects the
regressions create through `GetUninitializedObject` post to an uninitialized Dispatcher and kill the process
mid-run. On the VPS run
`dotnet tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll --scmod <pkg> --sha256 <sha> --vanilla-content <Content.zip> --json <out>`.
Do not add a "skip GPU tests" switch; nothing in the check set draws.

# Textures and threads

- `Engine.Graphics.Texture2D.Load` creates the GL object on the calling thread with no dispatch and no check, and `ContentManager` caches the result. Any texture a placeable block needs in `GenerateTerrainVertices` (terrain worker thread) must be resolved on the main thread first, in `Block.Initialize()`, and read from a field afterwards. A worker thread that first-touches `ContentManager.Get<Texture2D>` gets a broken texture that then draws black everywhere for the session (0.26.1-0.28.2 supply icons and the placed bench).
