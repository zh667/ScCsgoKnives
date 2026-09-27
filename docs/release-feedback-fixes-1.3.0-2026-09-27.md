# 1.3.0 layout previews, scope inspection and ordinary-character gloves

Revision `feedback-ui-hands-20260927` follows `60a8862`. Public version remains
1.3.0 and only the current Lite, agents and Full output packages are replaced.

## Layout previews

C4 plant/timer and voice were selectable but absent from the editor concurrency
groups. The selected button is now always previewed using the ordinary button
widget/style; plant and timer share a C4 group and voice is visible across groups.
Disabled buttons retain the user's requested old dim-preview behavior.

The previous text-only HUD placeholder was hidden when custom positioning was
off. It is replaced with the real `ScAmmoHud` and `ScMagazineWidget`, displaying
the M4A4 silhouette, 46 / 46 and infinity reserve, as requested. The sample stays
visible in automatic mode, uses the automatic corner, and directly dragging,
pinching, rotating or scrolling it enables custom positioning in the editor's
working copy. Save/Cancel/reset and prior gesture controls remain intact. This
sample never allocates a gun or modifies the actual equipped weapon.

## Gloves and scoped inspection

The tactical empty-hand hook previously drew replacement gloves for any character
with a glove choice, while NMM could independently render that character's native
hands. Empty-hand replacement is now limited to CT/T. Other characters keep
their native empty hands; the chosen gloves still replace CS weapon arms. This
avoids suppressing another mod's model/hook and leaves non-CS tools unchanged.
Workbench descriptions explain the distinction. No NMM/NEO binary is modified.

Inspection is rejected while scoped for all six scope-capable guns. Entering
scope clears queued inspection and returns an active inspection to scoped idle;
touch inspection controls are unavailable during scope. Unscoped inspection,
fire/reload and scope transitions otherwise retain existing paths.

## Supplied log and Sushi capacity

`D:/下载/Game(1) (1).log` SHA-256 and selected line-numbered evidence are in
`feedback-registry-log-2026-09-27.json`. In its 1.3.0 sessions, lines 16752 and
16846 already report 1022 records and Next=1023 during world load. Earlier
1.2.0 sessions report duplicate holders and allocation via Sushi sync boxes.
This supports a historical aliasing problem but does not prove how each of the
1022 records originated, or prove continuing leakage in the latest build.

The current adapter resolves a sync box to its actual channel inventory and
deduplicates by object plus slot. Added a one-time successful-mapping diagnostic.
The installed actual SushiBase/SushiTool DLLs passed all 44 dedicated checks,
including two native XML roundtrips, 210 same-channel operations without new IDs,
six person proxies sharing one holder, retuned/deleted-channel rollback and
compensation, full-table existing-gun mutations and refusal of genuine new copies.
The output JSON records both third-party DLL hashes.

No capacity recovery was performed. A log cannot enumerate unloaded entities,
all persistent containers, dormant compatibility payloads or external storage;
clearing records/reusing old IDs could bind an existing gun to somebody else's
state. Examining an exported affected world is required before designing an
evidence-based repair. New guns still require a free ID. Layout v5, schema6,
rules7, identities, watermark, recovery claims and player-world files are unchanged.

## Validation and delivery

Both editions passed 221 dedicated native widget/gesture/fire/scope/ownership
checks, plus actual-Sushi inventory checks. Every selection was checked in both
hand layouts at landscape and portrait sizes. Saved screenshots show C4/voice,
automatic M4A4 HUD and transformed HUD. Six scope-capable guns were tested for
blocked scoped inspection, unscoped inspection and scope interruption.

Final-payload gates cover official/local NMM load matrices, resource/ZIP checks,
191 codec streams in hardware/software modes, historical/current/Mini and
split/Full compatibility, tactical AI, smoke/sampling/performance, NPC/actor/
appearance renders and original-pixel texture checks. Assets and their compressed
streams are unchanged; only own assemblies and revision metadata change.

Native detached tests are not Android gesture or real-world listening acceptance.
The provided player world is not available and has not been repaired. Pre-existing
dirty Zeus edits remain outside this task's source commit. Source hashes in the
release evidence record the exact build inputs.

Reproduce with `prepare_feedback_fixes.py`, `package_feedback_fixes.py`,
`stage_feedback_checks.py`, `check_feedback_fixes.py` and
`publish_feedback_fixes.py`, through `tools/dev.ps1`. The dedicated `sushi` mode
uses the actual installed third-party DLLs without writing to their packages.
