# Quality checks

Run from the repository root on Windows:

```powershell
./tools/dev.ps1 python tools/check_quality.py --group all --out .tmp/dev-temp/quality-check --mp-refs '<API 1.9.3.2_MP DLL directory>' --mods '<API 1.9.3.1 Mods directory>' --appearance-refs '<original NMM/NEO DLL directory>'
```

Groups: `core` (targeted inventory/lifecycle faults), `builds` (all ten maintained product projects), `behavior` (DeathmatchCheck, BalanceCheck, FeedbackCheck, InventoryCheck, AppearanceNetCheck and wrapper argv), `mp` (actual platform transport/CompatNet/state/gunloop/dmloop/quality runners), `mp-quality` (only affected protocol cases), `variants` (Lite Deflate/Zstd core and agents, Mini and Mini+Inspect). `all` runs all groups. Individual test assertions and loaded assembly identities are in their runner reports. No package, install, release or cleanup scripts are invoked.

`execution.json` distinguishes `passed`, `failed`, `missing-dependency` and `not-executed`. Exit codes: 0 means requested runnable checks passed, 1 means failure, 2 means required dependencies missing. Unselected groups, optional historical binary regeneration, real game/multiplayer/Android/user acceptance and release compatibility are explicitly not executed. A source build never substitutes for those gates. The product project inventory is checked so a new product cannot silently disappear from the gate.

Build paths come from evaluated MSBuild `TargetPath`; do not guess between `bin/Release` and `bin/Release/net10.0`. `inputs.json` hashes maintained code, embedded data, fixtures and behavior resources; build rows hash the produced assemblies, and MP/provider directories have separate hash manifests. Original provider binaries are read-only inputs; the runner stages only DLL references. Source-linked variant projects follow the existing split/minimal type ownership without copying resource trees or changing editions.

Full Windows resource inputs are required, including AnimationData, source OBJ models and voice files. The appearance build uses the project's existing NekoMeko reference sources and original NEO DLL; the codec needs its pinned ZstdSharp source. Missing inputs block only their dependent checks. These external inputs are not guaranteed on GitHub-hosted machines. CI is configured to call the same entry and upload text evidence, and will report missing dependencies as non-success until the runner is provisioned. Repository variables `SC_MP_REFS`, `SC_MODS_1931`, `SC_APPEARANCE_REFS` supply paths on that runner; they do not fetch or install third-party inputs. CI configuration is not evidence that GitHub ran it.

For direct wrapper calls, quote whole colon arguments, e.g. `'-p:SkipScmodPackaging=true'` and `'-v:quiet'`, or splat literal strings. `check_dev_arguments.ps1` verifies child-process argv, spaces, Unicode, equals/semicolon characters and scoped temporary environment restoration. The wrapper cannot reconstruct prefixes that PowerShell removed before invoking it.
