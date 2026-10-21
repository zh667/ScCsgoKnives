# Git quality gates (2026-10-21)

Status: implementing
Current writer: Windows Codex, isolated worktree outside the Syncthing source tree.
User-authorized scope: protect main, add appropriate real CI checks, and establish official version tags. Merging this task is still subject to the specific approval rule in AGENTS.md.
Baseline: main 4cd1560e734c4f1c6527e2bb8111c04e57b541e6.
Branch: chore/git-quality-gates-20261021.

## Acceptance

- Server-side main protection requires PRs, build and behavior checks, current base, resolved review conversations, and blocks force push/deletion, including administrators. Zero mandatory reviewer approvals allows the sole maintainer to merge approved work.
- CI executes existing DeathmatchCheck rules against production assemblies, rejects failures/missing or empty results and retains report/input identities.
- The 2026-10-21 official 1.5.0 package family is identified by an annotated tag at its evidenced build source, with verified package hashes. Historical versions without a verified source commit are not guessed.
- No gameplay/resource/package changes, installation, merging main or alteration of the original checkout's four pre-existing untracked files.

## Release provenance

The existing output/release-1.5.0/manifest.json records source commit 13171aa1cdf68d552fde813d3db2462112d1979e. All four current output package sizes and SHA-256 values were independently read and matched that manifest. The committed release report is docs/tasks/release-1.5.0-20261021.md at 4cd1560. Changes between 13171aa and 4cd1560 only touch release documentation and tooling, not production src/ files.

## Validation and remote configuration

- Local exact entrypoint `./tools/dev.ps1 pwsh -NoProfile -File tools/ci-checks.ps1`: 254/254 checks passed; report and DLL hashes retained in .tmp/ci-results. Existing dependency/compiler warnings remain visible.
- `git diff --check` passed. The tracked release identity manifest matches the existing four output packages.
- Pending GitHub PR checks, main protection readback and remote tag verification.
