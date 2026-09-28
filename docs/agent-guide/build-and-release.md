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
- Before replacing accepted outputs, retain only the rollback/baseline artifacts required by the current task. The user's 2026-09-28 cleanup permits discarding scoped ordinary old packages/intermediates; it is not blanket permission to remove source assets, original compatibility fixtures, user worlds or backups.
- Resource work, compilation, packing and installation are separate actions. No installation to Mods/device or original-world writes merely because a package built successfully.

## Regression traps and evidence

- Never add a same-name overload to a method a regression locates with `GetMethod(name)`; use a distinct name such as `RequiredFor`.
- Self-tests can run before BlocksManager registration. Build constants/data halves directly; do not assume block indices are initialized.
- Preserve immutable fixtures; don't weaken a test to bypass invalid packages or dependency failures.
- Report source/build identity, commands, pass/fail/not-run checks, package path/hash and limitations. Separate static checks, native loading, offline renders, Windows gameplay and Android acceptance. A user-reported mobile stall remains unresolved until relevant device evidence supports the fix.
