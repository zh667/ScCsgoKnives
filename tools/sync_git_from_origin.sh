#!/usr/bin/env bash
# Deprecated: Git now owns both the commit and working-tree transfer.
set -euo pipefail
printf '%s\n' 'Retired: check git status and preserve uncommitted work, then use git pull --ff-only origin main in a clean checkout.' >&2
exit 2
