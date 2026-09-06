# Project conventions

- Use CS2 resources and real skinned hands for all first-person weapons. Do not restore the CS:MC / block-hand runtime route or expose a switch that re-enables it. This is the user's standing preference from 2026-09-06.
- Keep gun and knife variant ordering stable: existing worlds store those indices.
- Before removing assets, verify first-person, inventory, dropped-item and shared-effect references. Preserve CS2 source extractions and resources shared with other projects. Record removed paths in a manifest; do not delete by a broad “CS” name match.
- Deliver versioned `.scmod` packages in the project-root `output/` directory. Package only assets present in the current source manifest, so incremental build leftovers do not return to the package.
- Report packaged-DLL checks separately from actual game testing; an offline render is not an in-game screenshot.

- All mod items have no durability. Inherit `ScNoDurabilityBlock`, keep durability metadata at -1, and never store vanilla wear in variant/ammunition data. This is the user’s standing preference from 2026-09-06.

- Release Full and Lite together from the same DLL. Keep original-quality source textures; derive Lite 512px textures only while packaging, renormalizing normal maps. Preserve PackageName, item indices, animations and gameplay across editions. Install only one edition per game. This is the user’s preference from 2026-09-06.

# Two-peer git sync (Windows Codex + VPS Claude)

- The working tree is shared by Syncthing; `.git` is peer-local (`.stignore`) and must stay that way. Commits travel only through `origin`.
- The peer that does the work commits and pushes the current branch (`fix/cs2-only-hands-0.20.4` until it is merged) at the end of every version, before handing over. Uncommitted work is invisible to the other peer's git even though its files are already there.
- Before starting anything, the other peer runs `git fetch origin` and `git reset --mixed origin/<branch>` (VPS: `bash tools/sync_git_from_origin.sh`). That moves HEAD and the index to the pushed commit without touching files, so `git status` shows only what is genuinely uncommitted on the other side. Never `git pull` / `merge` into a tree the other peer has already updated, and never commit the other peer's uncommitted files.
- Never edit the same file on both peers at the same time; check `git status` for the other peer's in-progress files first.
