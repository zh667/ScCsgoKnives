# Project conventions

- Use CS2 resources and real skinned hands for all first-person weapons. Do not restore the CS:MC / block-hand runtime route or expose a switch that re-enables it. This is the user's standing preference from 2026-09-06.
- Keep gun and knife variant ordering stable: existing worlds store those indices.
- Before removing assets, verify first-person, inventory, dropped-item and shared-effect references. Preserve CS2 source extractions and resources shared with other projects. Record removed paths in a manifest; do not delete by a broad “CS” name match.
- Deliver versioned `.scmod` packages in the project-root `output/` directory. Package only assets present in the current source manifest, so incremental build leftovers do not return to the package.
- Report packaged-DLL checks separately from actual game testing; an offline render is not an in-game screenshot.

- Current runtime remains no-durability until the planned feature is implemented. The user's 2026-09-07 instructions supersede the prior all-items-no-durability preference for guns only: implement gun wear, broken-but-retained guns and workbench repair per `docs/community-feedback-plan-2026-09-07.md`. Other items remain on `ScNoDurabilityBlock`. Never write vanilla wear into variant/ammunition bits. Testing uses new worlds per version; old-save migration and cross-version compatibility are out of scope, while same-version persistence and item transfers remain required.
- Follow the current community-feedback plan one milestone at a time. Third-person character and throwing poses are authored for vanilla limbs; only weapon-part animation and event timing reuse CS2 clips. Do not claim full CS2 character animation reproduction. Do not add distant-audio variants. Guns use the planned 1.5x survival damage; knife reach is planned as 2.2 light / 1.8 heavy. Headshot non-kills are yellow; all kills are red. These are implementation requirements, not claims that runtime changes already exist.

- Release Full and Lite together from the same DLL. Keep original-quality source textures; derive Lite 512px textures only while packaging, renormalizing normal maps. Preserve PackageName, item indices, animations and gameplay across editions. Install only one edition per game. This is the user’s preference from 2026-09-06.

# Two-peer git sync (Windows Codex + VPS Claude)

- The working tree is shared by Syncthing; `.git` is peer-local (`.stignore`) and must stay that way. Commits travel only through `origin`.
- The peer that does the work commits and pushes `main` (the fix/cs2-only-hands-0.20.4 branch was fast-forwarded into it at 0.28.2) at the end of every version, before handing over. Uncommitted work is invisible to the other peer's git even though its files are already there.
- Before starting anything, the other peer runs `git fetch origin` and `git reset --mixed origin/<branch>` (VPS: `bash tools/sync_git_from_origin.sh`). That moves HEAD and the index to the pushed commit without touching files, so `git status` shows only what is genuinely uncommitted on the other side. Never `git pull` / `merge` into a tree the other peer has already updated, and never commit the other peer's uncommitted files.
- Never edit the same file on both peers at the same time; check `git status` for the other peer's in-progress files first.

# Textures and threads

- `Engine.Graphics.Texture2D.Load` creates the GL object on the calling thread with no dispatch and no check, and `ContentManager` caches the result. Any texture a placeable block needs in `GenerateTerrainVertices` (terrain worker thread) must be resolved on the main thread first, in `Block.Initialize()`, and read from a field afterwards. A worker thread that first-touches `ContentManager.Get<Texture2D>` gets a broken texture that then draws black everywhere for the session (0.26.1-0.28.2 supply icons and the placed bench).
