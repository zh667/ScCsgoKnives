# Repository instructions

Read and follow [AGENTS.md](AGENTS.md) for project conventions, current M4-only scope, peer synchronization and release requirements. Other milestones remain BLOCKED until the user resumes them.

# Gun save compatibility

Before changing gun IDs/encoding, registry/schema, load/save, durability/charge semantics or related inventory transactions, read [the complete compatibility policy](docs/gun-save-compatibility-policy-2026-09-08.md). This user-directed policy dated 2026-09-08 supersedes historical new-world-only/no-cross-version instructions for guns.

- Only 0.28.2 has been publicly released. Retain its verified backup migration; treat 0.37.0 as the protected forward baseline. Do not guess internal 0.29-0.36 test formats.
- Freeze variant IDs and v5 item meanings; preserve instance references. Prefer registry extensions and keep mod version, layout and schema separate. Actual format changes require explicit, repeat-safe converters that support skipped versions.
- Preserve gun identity, ammo, silencer, durability, charge and pending recovery state. Full durability is granted only once when initializing the authorized 0.28.2 migration. Parameter changes need explicit old-state rules, including fresh templates and excess ammunition.
- Validate detached conversions and capacity, verify a complete source-world backup before activation, and save items/records/markers consistently. Failure must not partially convert or overwrite the source; a save request is not proof of successful disk writing.
- Reject unknown future layouts/schemas before normal loading/autosave without rewriting data or stamps. Disabling guns or showing question marks is not sufficient protection. Preserve corrupt data and do not guess replacement guns.
- Data-affecting releases require immutable baseline fixtures, applicable inventory/container/drop/state coverage, two save/reload rounds, retry/failure and future-format checks. Report implemented versus pending work and offline versus device evidence honestly. This documentation task does not implement the pending protections or authorize a new data layout.

# Build deliverables

Use the project-root `output/` directory for final deliverables that the user should receive on Windows.

After a successful release build, copy the final installable `.scmod` package from the build directory into `output/`. Use a clear, versioned filename when a version is available. Build intermediates in `bin/`, `obj/`, `.vs/`, or `.tmp-*` are peer-local and must not be treated as delivered artifacts.
