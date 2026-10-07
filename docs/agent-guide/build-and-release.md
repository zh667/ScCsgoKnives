# Builds, verification and delivery

Read for executable checks, resource work, packaging or release changes.

## Environment

- Full resources, native API and build stages reside on Windows at `E:/projects/ScCsgoKnives`. VPS source-sync intentionally lacks large assets and bulk AnimationData; a failed local build there is not proof the sources are broken.
- Check actual dependencies/paths before running a command. Some historical tools still reference former roots or deleted intermediate stages. Do not blindly replay old pipelines or use placeholders.
- Run Windows build/test/Python tooling from the project through `./tools/dev.ps1 <command> <arguments>`. It scopes temporary files to `.tmp/dev-temp` and restores process environment afterward. Do not repurpose HOME/CODEX_HOME or clean system Temp.
- Example for a source-only DLL build when its Windows inputs are present: `./tools/dev.ps1 dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release -p:SkipScmodPackaging=true`. This does not create all required edition artifacts or replace release validation.
- `tools/PackageCheck` is headless; keep `Engine.Dispatcher.Initialize()` and its native dependencies. Do not invent a GPU skip switch. Run only gates relevant to changed behavior, then required release/compatibility gates when packaging.
- The 2026-09-27 capacity pipeline is documented in [release-capacity-2026-09-27.md](../release-capacity-2026-09-27.md); it requires specific historical packages and staging inputs. Audit their current locations before reuse. Missing baseline artifacts must be reported, not silently substituted.

## Packaging invariants

- Preserve accepted gameplay and resource bytes unless the current task changes them. Do not silently lower resolution, simplify geometry, quantize curves or remove cosmetics in the name of optimization.
- Current release snapshot: public 1.3.0 Full / split Lite / optional agents, plus 1.0.0 and 1.2.0 compatibility revisions; Mini is separate. Verify actual package metadata/hash instead of assuming historical version names are current.
- Full and split Lite share intended gameplay, but conditional type ownership/optional components mean their DLL bytes need not be identical. Keep `SC_SPLIT` ownership and dormant optional state intact. Enable only one core edition at a time.
- Mini changes and optional addon changes need explicit task scope; do not propagate Mini cuts into ordinary Full/Lite. Preserve the existing 35-gun/22-knife catalogue and relevant skin IDs.
- Split resource Zstd/Deflate optimization must preserve the decoded original member bytes and be validated with actual loaders. Isolate codec identities, bound reads, check integrity and ownership. Preserve Full's applicable resource format.
- Pack from the current manifest, excluding incremental leftovers. Validate native XML encoding (`utf-8`), required entries, packaged DLL identity, package/version metadata, member hashes, dependencies and resource loading.
- Final installable `.scmod` files go in root `output/`; technical hashes, revisions and compatibility notes go in release records. Player-facing names/descriptions remain generic product text, without schema/debug revision notes.
- Use the requested filenames and edition scope. [Naming correction](../release-package-names-2026-09-27.md) supersedes the initial capacity-suffixed delivery names.
- User standing authorization 2026-09-29: after the applicable gates pass, replace the latest matching `output/` packages directly under their ordinary filenames, update the manifest/hashes, and read back final archives. Do not ask again just to move a validated result into output. Keep edition/version scope unchanged unless authorized; this is not permission to install, publish remotely or modify player worlds. Required visual acceptance and known incomplete scope must still be reported honestly; failed gates do not become optional.
- Resource work, compilation, packing and installation are separate actions. No installation to Mods/device or original-world writes merely because a package built successfully.

## Package names, versions and official releases (2026-10-06)

- Candidates are named from `src/ScCsgoKnives/modinfo.json`'s Version: `[API1.9]CS武器<version>-{全量|轻量|探员}包.scmod` and `[API1.9]CS武器<version>-死亡竞赛.scmod` (no 包). The package step writes that version into every modinfo member and the install/bundle texts. A version bump is a release decision, not a side effect.
- The stage builds on the delivered packages of `followup_140.BASELINE_VERSION` in `BASELINE_DIR` (`completion_140.BASELINES` pins their hashes). On delivery the superseded family moves to `output/history-<version>/` (or `history-<version>/<sha256>/` for a single superseded package) and stays there: `completion_140.OFFICIAL_RELEASES` lists every official release from 1.4.0 on, and the compat gate switches saves against each of their cores (`switching-*`), with every assembly of a package beside its core.
- New tactical assets ship through a record (`docs/tasks/airdrop-package-members-20261006.json` pattern): `followup_140.TACTICAL_ASSET_RECORDS` puts them into Full and agents, and `core_members` lists them in the Full core's resource marker; `main`'s `standalone/resource-manifest-complete` fails otherwise.

## Intermediate cleanup (standing authorization, 2026-09-29)

- Once final output replacement and readback succeed, remove obsolete/reproducible intermediate packages, copied source snapshots, build/obj trees and derived test assets of that task without another permission question. Avoid accumulating one multi-GB copy per test round. Keep at most the working set actually needed by an active follow-up, with an explicit reason.
- First enumerate exact absolute targets and sizes, verify they stay inside the intended task-stage directories, and check no worker/build/agent process needs them. Do not follow junctions/symlinks outside the scope or delete a broad root/glob such as all `.tmp`. Use one native shell for Windows file operations.
- Before cleanup, inspect scripts for references to older staging paths. A temporary-looking directory used as the only copy of embedded AnimationData, references, split shims or source GLBs is a build input, not yet disposable. Move/reconstruct such inputs in a documented reusable location and validate the replacement before removing them; never silently break the next build to reclaim space.
- Preserve raw CS2/source assets, current product sources, third-party packages, immutable compatibility fixtures, player worlds/backups, credentials and unrelated projects. Preserve final package hashes/member manifests, source hashes, executed test results, minimal repro fixtures/logs and the visual evidence still needed for acceptance. Detailed superseded diagnostics may be compacted after retaining the relevant evidence.
- Write a cleanup receipt listing exact removed paths, bytes, retained evidence/dependencies and verification of unchanged delivered packages. State whether deletion was permanent/rebuildable or recoverable; do not claim deleted intermediate builds are in the recycle bin.
- Check disk headroom before starting another full round. Never disable Syncthing's disk-space safety threshold or discard required source/fixture data to make a build fit.

## Regression traps and evidence

- Use change-scoped iteration: reproduce and run focused checks first; reserve the full affected release/compatibility gates for a concrete candidate. Do not run the whole multi-edition bake/package/render pipeline for each fixture or text change. Reuse immutable derived assets and baseline results only with matching source/tool/config/dependency hashes; changes invalidate their dependents. Stop dependent stages on build failure; independent diagnostics may continue deliberately. Preserve full final coverage while removing redundant work.

- Never add a same-name overload to a method a regression locates with `GetMethod(name)`; use a distinct name such as `RequiredFor`.
- Self-tests can run before BlocksManager registration. Build constants/data halves directly; do not assume block indices are initialized.
- Preserve immutable fixtures; don't weaken a test to bypass invalid packages or dependency failures.
- Track every requested actor/posture/input/environment case as implemented + executed + visually reviewed, or as a named unresolved case. A correct refusal/fallback is not proof of the requested animation/feature. Never call a test passed merely because it was added to a source file: confirm the active runner reaches it, and retain the result and tested artifact hash. A known failing/skipped suite cannot supply passing evidence for nested tests.
- Report source/build identity, commands, pass/fail/not-run checks, package path/hash and limitations. Separate static checks, native loading, offline renders, Windows gameplay and Android acceptance. A user-reported mobile stall remains unresolved until relevant device evidence supports the fix.
