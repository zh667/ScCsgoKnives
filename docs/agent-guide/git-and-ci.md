# Git review gates and release identities

Use task branches and PRs into `main`; merge only after the user's task-specific approval in AGENTS.md. No second human reviewer is required for this single-maintainer repository.

## Required checks

- `build`: build the solution and require the expected CI package to exist.
- `behavior-checks`: execute the maintained DeathmatchCheck runner against freshly built production code and SurvivalcraftAPI 1.9.3.1. It covers arena validation, spawn selection, armoury, combat, weapons, match state and related offline rules. Failed execution, missing/empty reports and failing cases fail the job. JSON results and input DLL hashes are uploaded as artifacts.
- Run the same behavior entrypoint locally: `./tools/dev.ps1 pwsh -NoProfile -File tools/ci-checks.ps1`.

These checks are a quick PR gate, not full release acceptance. They do not cover the complete core regression suite, actual engine gameplay, networking, Android or the historical save-switching matrix. Continue the applicable Windows release gates in build-and-release.md and record artifact hashes, executed checks, known failures and pending acceptance in the task record. The ordinary CI build artifact is not automatically an official Full/Lite/addon release family.

Protect `main` with required PRs (zero mandatory reviewer approvals), both checks passing against an up-to-date base, resolved conversations, admin enforcement, and no force push or branch deletion. GitHub cannot infer approval from a user conversation: the agent must still obey AGENTS.md. These stable job names must stay synchronized with the server's required checks.

## Release tags

Use annotated, immutable tags for verified official build identities. If several official packages share the same player-visible version, use `v<version>-build.<YYYYMMDD>` (add a unique revision suffix if multiple builds ship that day). Never move an existing release tag to new bytes.

The tag points to the source commit recorded by the build manifest, not an arbitrary later documentation commit. Its annotation records the product version/date, source commit, package SHA-256 values and provenance record. A tracked release manifest can additionally record input identities and acceptance limits. Tags identify existing releases; creating one does not authorize another package build, version bump, public binary upload or installation.

Only backfill a historical tag when its source identity is evidenced. Do not infer a historical build commit from its date alone.
