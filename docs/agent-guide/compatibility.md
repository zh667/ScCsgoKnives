# Gun saves and release compatibility

Read before changing item IDs/encoding, registry/schema, inventory transactions, durability, growth, charge, migration or cross-world state. This current summary resolves older contradictory policy wording; retain historical documents as evidence.

## Current contract

- No automatic player-world backups/snapshots/restore points in CS runtime, compatibility revisions or migration paths. Users back up manually. Do not delete/move existing user backups. Development instruction-file snapshots are a separate activity.
- Preserve every existing weapon/knife/skin ordering, permanent gun identity and instance reference. Never recycle IDs, clear a registry to regain capacity or guess missing/corrupt records.
- The 2026-09-27 capacity revision uses layout 6 / schema 7, preserving old 1–1022 encodings, reserving 0/1023, extending valid records to 66,558. Do not restore the older blanket v5/schema6 freeze.
- Supported bidirectional switching is between the updated capacity-compatible 1.0.0 / 1.2.0 / 1.3.0 Full/Lite revisions, not arbitrary original historical binaries or Mini. Verify the actual input hashes/readers before claiming compatibility.
- Preserve ammo, overflow, durability, silencer, skins, counters, growth, charge remaining seconds, deferred obligations, inventory holders and dormant newer content across supported switching. A no-op switch must not grant or lose resources or rewards.
- Keep mod version, layout/schema and gameplay rules versions distinct. Parameter changes require explicit preserved-state semantics; do not silently reset existing settings/records.
- Validate detached data and capacity before writing. Conversion must be complete, repeat-safe and support skipped versions. Future/unsupported formats must refuse before normal load/autosave without rewriting their data/stamps. Preserve corrupt raw payloads for recovery.
- Historical compatibility revisions retain their original gameplay/assets except necessary compatibility changes; relabeling a current-rule preset is not a historical revision.

## Required evidence and test coverage

Read [capacity revision](../release-capacity-2026-09-27.md), [current package naming](../release-package-names-2026-09-27.md) and [three-version switching design](../three-version-switching-design-2026-09-25.md).

For older migrations also read [legacy compatibility policy](../gun-save-compatibility-policy-2026-09-08.md), [published 0.28.2 migration](../official-0282-compatibility-0370.md) and the relevant fixture/release documents. Their automatic-backup, only-0.28.2-public and fixed-v5 claims are historical and superseded above.

- Preserve immutable original fixtures and provenance; never rewrite fixtures to make tests pass.
- Data-affecting changes require the relevant six-direction family matrix, previous compatible releases, mutations and holders (inventory/container/drop/transfer), two save/reloads, retry/failure and unknown-format protection checks.
- Unsupported newer entities/items must survive as dormant data when switching to older supported readers and return intact later.
- Preserve custom durability and `ScNoDurabilityBlock` protection against vanilla wear. One-time historical initialization must not be applied again on routine reload/switch.
- Sushi inventory channels require actual provider semantics, including shared-channel de-duplication and creative proxies. Never refund ambiguous items to a recreated channel just because its number matches.
- Exact player exports may only be inspected/restored as authorized independent copies. No installed-world writes or fabricated state recovery.
- Offline/native checks and Android/game acceptance are separate. A successful save request is not evidence of persisted disk data.

Do not resume old milestones, restore old packages or regenerate missing fixtures solely because a historical document says to do so. Resolve the current user scope and actual files first.
