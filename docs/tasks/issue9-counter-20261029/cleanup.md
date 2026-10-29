# Cleanup receipt — 2026-10-29

The automatic approval layer rejected the cleanup command before process creation with `blocked by policy`. No deletion ran. The following verified, task-local reproducible outputs remain intact:

| Absolute directory | Files | Bytes |
|---|---:|---:|
| E:/projects/ScCsgoKnives-issue9-counter/src/ScCsgoKnives/bin | 1353 | 524307338 |
| E:/projects/ScCsgoKnives-issue9-counter/src/ScCsgoTactical/bin | 1497 | 761702365 |
| E:/projects/ScCsgoKnives-issue9-counter/src/ScCsgoResources/bin | 2 | 134709312 |
| E:/projects/ScCsgoKnives-issue9-counter/tools/WeaponPresentationCheck/bin/Release/net10.0/Assets | 1488 | 618630139 |

Total: 2,039,349,154 bytes retained, none freed. No attempt was made to bypass the rejection. Source assets, the review packages in output, compact evidence, full raw diagnostic results and the isolated candidate stage remain available. The original synchronized checkout and its packages were not cleanup targets.
