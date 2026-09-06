# Project conventions

- Use CS2 resources and real skinned hands for all first-person weapons. Do not restore the CS:MC / block-hand runtime route or expose a switch that re-enables it. This is the user's standing preference from 2026-09-06.
- Keep gun and knife variant ordering stable: existing worlds store those indices.
- Before removing assets, verify first-person, inventory, dropped-item and shared-effect references. Preserve CS2 source extractions and resources shared with other projects. Record removed paths in a manifest; do not delete by a broad “CS” name match.
- Deliver versioned `.scmod` packages in the project-root `output/` directory. Package only assets present in the current source manifest, so incremental build leftovers do not return to the package.
- Report packaged-DLL checks separately from actual game testing; an offline render is not an in-game screenshot.

- All mod items have no durability. Inherit `ScNoDurabilityBlock`, keep durability metadata at -1, and never store vanilla wear in variant/ammunition data. This is the user’s standing preference from 2026-09-06.
