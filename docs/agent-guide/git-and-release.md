# Git review gates and release identities

Use task branches and PRs into `main`; merge only after the user's task-specific approval in AGENTS.md. No second human reviewer is required for this single-maintainer repository.

## Manual verification; no GitHub CI

The user removed all GitHub build/test CI on 2026-10-21. The build workflow is disabled on GitHub and its YAML and CI-only wrapper are removed from this change. There are no required status checks on main. Do not restore automatic CI without a new user request.

Keep the existing local test projects, including DeathmatchCheck, for manual, change-appropriate verification through tools/dev.ps1. Continue applicable Windows build/release gates in build-and-release.md and record commands, tested source/artifact hashes, failures and pending acceptance in the task record. Removing CI does not waive validation of requested changes or authorize claiming unexecuted tests passed.

Protect `main` with required PRs (zero mandatory reviewer approvals), resolved conversations, admin enforcement, and no force push or branch deletion. GitHub cannot infer approval from a user conversation: the agent must still obey AGENTS.md. No automated CI result is required to merge.

## Release tags

Use annotated, immutable tags for verified official build identities. If several official packages share the same player-visible version, use `v<version>-build.<YYYYMMDD>` (add a unique revision suffix if multiple builds ship that day). Never move an existing release tag to new bytes.

The tag points to the source commit recorded by the build manifest, not an arbitrary later documentation commit. Its annotation records the product version/date, source commit, package SHA-256 values and provenance record. A tracked release manifest can additionally record input identities and acceptance limits. Tags identify existing releases; creating one does not authorize another package build, version bump, public binary upload or installation.

Only backfill a historical tag when its source identity is evidenced. Do not infer a historical build commit from its date alone.
