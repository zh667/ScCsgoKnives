# ScCsgoKnives: project instructions

One shared instruction source for Codex and Claude Code. Updated 2026-09-28.

## Scope and current truth

- Follow the user's active request. Explain/review/diagnose requests do not authorize implementation, installation or release publication by themselves.
- Preserve existing accepted behavior and unrelated dirty work, including Zeus edits. On Windows inspect `git status --short` before editing; never reset, restore, stash or commit someone else's work as routine cleanup.
- Historical dated requests are provenance, not current commands or permission to resume blocked milestones. Specialist documents below resolve superseded rules; consult release evidence relevant to the change.
- Do not install packages into the user's Mods/device, modify original worlds, patch third-party providers or delete source assets/backups without task-specific authorization.

## Windows and VPS

- Windows project: `E:/projects/ScCsgoKnives`; raw source assets: `E:/projects/CSMCReverse`.
- VPS active source tree: `/home/dev/source-sync/ScCsgoKnives`. Old `/home/dev/workspaces/*` trees are legacy and partly cleaned, not current edit/build roots.
- Windows desktop agent handles user media, planning, review and visual acceptance by default. VPS agent verifies causes against code and implements scoped changes. Treat visual diagnoses as hypotheses until verified.
- Syncthing shares source, scripts, small descriptors/docs and handoffs; Git history stays on Windows. No automatic Git pull/reset/restore in the live synced tree, and no Git initialization on the source-only VPS peer.
- One writer per file at a time. Before handing off, stop the previous writer and verify both peers have no sync errors, pending items or conflicts. OWNER/task notes are not locks.
- Keep models, textures, recordings, packages and bulk AnimationData on Windows. Run resource parsing/conversion/full builds via Windows worker; return bounded JSON/logs/previews. Do not copy the resource tree to VPS to satisfy missing Linux paths.
- For pictures/video, remote jobs or cross-agent handoff, read [collaboration](docs/agent-guide/collaboration.md) first. Use [the task template](docs/agent-guide/TASK_TEMPLATE.md) under `docs/tasks/`.
- A media path or base64 dump is not proof an agent saw an image. Video keyframes need timestamps; do not infer timing/audio correctness from sparse stills. Do not assume automatic agent messaging or dispatch exists.
- Follow `.stignore.shared`; `.psh` and `.xdb` are included, large binary assets/output/caches are not. Do not broaden synchronization as a workaround for resource processing.

## Persistent game and data contracts

- Preserve existing gun/knife/skin IDs, ordering, package identities and permanent gun records; never recycle identities or reset player state to solve a bug.
- No automatic player-world backups/snapshots, including compatibility/migration paths. Players back up manually; preserve existing user backups.
- Current documented capacity family uses layout 6 / schema 7, reserves 0/1023 and preserves legacy meanings. Supported updated 1.0/1.2/1.3 revisions must retain state across bidirectional switching; original unmodified packages and Mini are not automatically compatible.
- Before touching saves, IDs, inventory, growth, durability, charge or migration, read [compatibility](docs/agent-guide/compatibility.md). Validate conversions and future-format refusal before writes; never guess missing/corrupt records.
- First-person CS weapons use CS2 resources and real skinned hands. Preserve original-quality source resources and derive editions separately. Do not re-enable the old CS:MC/block-hand route.
- For models, poses, shaders, materials, audio, caches or appearance integrations, read [rendering/resources](docs/agent-guide/rendering-and-resources.md) before changes.

## Build and verification

- Before builds/tests/resource processing or packaging, read [build/release](docs/agent-guide/build-and-release.md).
- On Windows run project tooling through `./tools/dev.ps1 <command> <arguments>`; temporary files stay under `.tmp/dev-temp`. VPS source-only trees cannot perform every resource-dependent build.
- Use targeted meaningful tests plus required release/compatibility gates. Do not run a full release pipeline for documentation-only edits.
- Deliver requested installable packages in root `output/`, using the current manifest; don't ship incremental leftovers. Keep technical revision/schema notes out of player-facing names.
- Do not silently change Full/Lite/Mini scope, resource quality or optional addon ownership. Check actual current package metadata/hashes; old release labels are not current versions.
- Do not add overloads to methods tests resolve using `GetMethod(name)`; use distinct method names.
- Self-tests may run before BlocksManager registration. Graphics resources needed by terrain workers must be prepared on the main/graphics thread.
- Report changed behavior, tests and remaining uncertainty. Distinguish static checks, native loading, offline rendering, real Windows gameplay and Android acceptance.

## Instruction maintenance

- Keep this file short and current. Put new task status in `docs/tasks/`, details in specialist/release documents, not another dated override here.
- [Maintenance and official sources](docs/agent-guide/README.md) explains loading behavior and historical conflict resolution.
- Complete pre-refactor files are in `docs/agent-guide/history/`. Do not load the whole history at startup or treat it as active instructions.
