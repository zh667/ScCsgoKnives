# Rendering, animation and resources

Read before changing meshes, poses, materials, audio, resource caches or appearance integrations.

## Persistent requirements

- First-person CS weapons use CS2 resources and real skinned hands. Do not restore the legacy CS:MC/block-hand runtime or a switch to re-enable it. The project's `csmc-reconstruction` skill and early research contain legacy calibration; use them only for relevant provenance, never as permission to regress the current runtime.
- Preserve original-quality sources in `E:/projects/CSMCReverse`. Derive edition-specific resources separately, with provenance and hashes. Do not bake from already stripped marker GLBs or overwrite Full caches with Lite data.
- Check first-person, third-person, inventory, dropped-item, NPC and shared-effect references before removing/replacing an asset. A standalone mesh render is not a game screenshot or proof of all these paths.
- Do not fit screenshots and label assumed transforms/materials “precise.” Distinguish extracted data, measured runtime behavior and approximation.
- Material textures that terrain workers need must be resolved on the graphics/main thread during loading, not first-created in `GenerateTerrainVertices`. Preserve original pixels/upload behavior unless the task explicitly requests quality changes.
- Keep per-player/per-entity clocks, camera transforms and model visibility independent. Multiple cameras must not advance an action twice; presentation must not mutate ammo/inventory state.
- Keep hierarchy/bone order, inverse binds, animations/events, knife hinge weights and cross-joint triangles. Native caches must be baked with the relevant edition's actual loader; the headless OBJ path is not an interchangeable cache source.
- Match GLB and `.scanim` caches as a unit; validate equality and lifecycle. Dispose owned GPU buffers on world exit and recreate after device reset. Separate CPU timing scopes from actual GPU measurements.
- NMM/NEO and other providers remain optional and unchanged. Adapt our integration with narrow identities/guards; do not patch/repack providers or add broad assembly aliases.
- Player role/gloves use actual player-local state. Non-CS empty hands belong to vanilla/NMM under the latest requirement. Preserve eye fixes, smooth lighting and apply-versus-preview semantics.
- Keep original audio duration/pitch unless approved. Correct event timing, silenced branches and bounded own-gun/voice concurrency need actual listening/runtime checks; decoding alone is not listening QA.
- Preserve current HUD editor gestures, stored bindings and disabled-control previews; scoped inspection remains disallowed. Do not steal vanilla inventory/clothing/crouch/hotbar controls.
- Gameplay performance fixes must preserve current rules and save state. Avoid global AI budgets or third-party edits to hide a local issue.

## Read the applicable evidence

| Work area | Starting documents |
|---|---|
| Visual evidence / remote video | [collaboration.md](collaboration.md) |
| Current editor/voice/inspection feedback | [feedback fixes](../release-feedback-fixes-1.3.0-2026-09-27.md), [settings rollback](../release-settings-rollback-1.3.0-2026-09-27.md) |
| Texture/appearance mobile fixes | [mobile resources](../release-mobile-resources-1.3.0-2026-09-27.md), [NMM compatibility](../release-nmm-fix-1.3.0-2026-09-26.md) |
| Actor caches / crowds | [actor freeze](../release-actor-freeze-1.3.0-2026-09-26.md), [crowd smoothing](../release-crowd-smoothing-1.3.0-2026-09-26.md), [crowd optimization](../release-crowd-optimization-1.3.0-2026-09-26.md) |
| Player gloves / third person | [gloves](../release-gloves-1.4.9.md), [third-person contact](../release-thirdperson-1.4.6.md) |
| Grenade/voice gameplay | [gameplay/audio](../release-gameplay-audio-1.3.0-2026-09-27.md), [mobile common](../release-mobile-common-1.3.0-2026-09-27.md) |

Earlier numeric balance statements conflict with later releases. Inspect current source, the accepted user request and matching release evidence; never select a historical number just because it appears in the archive. In particular, preserve existing unrelated Zeus changes.
