#!/usr/bin/env bash
# Align this peer's git (HEAD + index) with what the other peer pushed, without
# touching the Syncthing-synchronised working tree. Afterwards `git status`
# shows only the other peer's still-uncommitted work.
#
#   bash tools/sync_git_from_origin.sh [branch]   (default: the current branch)
set -euo pipefail
cd "$(dirname "$0")/.."
branch="${1:-$(git rev-parse --abbrev-ref HEAD)}"
git fetch -q origin
if ! git show-ref -q --verify "refs/remotes/origin/$branch"; then
    echo "origin has no branch '$branch'" >&2; exit 1
fi
current="$(git rev-parse --abbrev-ref HEAD)"
if [ "$current" != "$branch" ]; then
    git branch -f "$branch" "origin/$branch"
    git symbolic-ref HEAD "refs/heads/$branch"
fi
git reset -q --mixed "origin/$branch"
git branch -q --set-upstream-to="origin/$branch" "$branch" 2>/dev/null || true
echo "HEAD: $(git log --oneline -1)"
echo "left uncommitted by the other peer: $(git status --short | grep -vc '^?? output/' || true) file(s)"
git status --short | grep -v '^?? output/' | head -30
