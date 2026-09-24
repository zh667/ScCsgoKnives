# Actual 1.0.0 and 1.2.0 serializer fixtures

Generated from the exact two user-supplied `.scmod` packages, using their own `ScGunRegistry.Save` implementations. Each gzip contains XML `ValuesDictionary` records for all 35 frozen variants and a CountOnly sample. No game assets, old assemblies, or player data are included.

1.0.0: 35 × 8 levels × 3 wear states × 3 ammo states × 2 silencer states = 5,040 gun rows. 1.2.0: 35 × 10 levels × the same states = 6,300 rows. Known paints rotate through each weapon's historical catalogue. Zeus records carry in-progress charges; all records carry an overflow reserve, revision and kill count.

These files are immutable. `tools/BalanceCheck/Run.ps1` verifies the source package SHA256s before extracting DLLs to project `.tmp`; `MigrationCheck` regenerates and compares the XML, then tests two current save/load rounds, old-DLL travel capture, backups and failure refusal. A fixture mismatch must be investigated, not fixed by overwriting the baseline.

Package, DLL and fixture fingerprints and test results: `docs/gun-balance-implementation-evidence-2026-09-24.json`. Migration semantics and remaining real-game checks: `docs/gun-balance-implementation-2026-09-24.md`.
