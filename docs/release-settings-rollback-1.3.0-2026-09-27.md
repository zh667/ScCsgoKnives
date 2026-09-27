# 1.3.0 settings, HUD gestures, audio rollback and fire trigger repair

The settings redesign and shared audio owner introduced by `efb14b0` caused
reported UI/audio regressions. This release restores the earlier settings order
and native gun/knife/NPC/voice audio callers from `fc023e9`. Public version stays
1.3.0; internal revision is `settings-audio-rollback-20260927`.

The group number 1087216872 and copy button are fixed above the settings scroll
area. The user subsequently asked to preserve the old dim preview for disabled
buttons; that editor behavior is retained, including selection and re-enabling.

HUD editing lives directly inside **编辑按键布局 → 选择按键或弹药 HUD → 弹药 HUD**.
There are no horizontal/vertical sliders. Enable custom position, then collapse
the control panel if it overlaps the HUD. Phone controls support single-pointer
drag and two-finger translation, pinch scaling and rotation. Desktop controls
are drag to move, wheel over the HUD to scale, Shift+wheel to rotate. Save commits
the whole transform; Cancel discards it; reset returns the working HUD to auto.
Scale is bounded to 0.5–2.5 and transformed bounds are kept on screen. Old local
HUD JSON defaults to scale 1 and rotation 0. The gameplay HUD uses the same
transform math; positions remain shared across left/right hand layouts, while
button layouts remain separate. No world or item data format changes.

Fire feedback exposed a second regression: every fire detonation unconditionally
played a ground impact sound and emitted the new airborne burst. The repaired
path classifies the actual `Grounded` state before changing the effect state.
Ground contact retains its own impact sound and supported floor fire and emits
no airborne fireball. Three-second airborne timeout uses the dedicated CS2 air
sound and burst. It may still ignite reachable support within the existing
four-block floor probe; an unsupported explosion produces no floating damage
zone. Repeated detonation of an active ground fire is ignored. Bounded log entries
include `trigger=air-fuse|ground-contact`, kind, flight age, sound and airVisual.
The atlas and all existing audio/resources retain their exact previous bytes.

Other accepted work remains: grenade deploy interruption, fire fuse beginning at
release, smoke one-body-bounce, day-30 natural enemy grace, NEO buffer repair,
resource preparation, compressed models/animations and edition texture quality.
Restoring the old main settings page removes the recent natural-enemy checkbox
from that page; the stored preference is preserved and remains honored.

Deliverables replace only the three current files in `output/`. Use either Full,
or Lite with optional matching agents. Both split files should be updated
together: the agents package requires common protocol 4. Mini, historical
packages, third-party mods, installed Mods and player worlds are unchanged.

| Package | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 35,452,976 | `5cedae8c37a83f4c0f57731fc15925a48f516dda2ea893a19f3d51277a3dd05c` |
| Agents | 37,082,433 | `878bb5940db080215ca7789a27f9d02e52d151fa93ff7761fd24c0bc29ae38f4` |
| Full | 524,453,586 | `38472749cafc2ce6da31946f4263d603cba7cbfaedcf3247f8628d70365256e6` |

Validation uses isolated Windows native runners, not the player's installation:

- Exact candidate load, official/local NMM matrix, native ZIP/resource checks,
  all 191 codec streams with hardware intrinsics enabled and disabled.
- Historical/current/Mini and split/Full compatibility, tactical AI, smoke,
  sampling, NPC/appearance/actor render and original-pixel texture checks.
- Real widget input for two fingers and mouse/keyboard; preview drag, scale,
  rotation, on-screen bounds, Save/Cancel/reset and old-settings defaults.
- Fixed group number while scrolling at 850×479 and 360×640; disabled button
  preview keeps prior dim behavior; gameplay HUD applies the editor transform.
- Both Molotov/incendiary ground routes versus unsupported air detonation,
  nonduplicated audio/visuals, native decoding of the added air sound.
- Seven complete audio-related source files equal the pre-redesign baseline;
  no `ScOwnedAudio` type/callers remain and prior packaged audio is byte-identical.

The evidence JSON contains final package sizes/hashes, gate results, source input
hashes and source audio provenance. Native widget screenshots are detached
renders, not screenshots of a phone game. Android gesture feel, listening and
frame pacing still need device acceptance. Pre-existing dirty Zeus changes were
preserved and are not included in this task's source commit.
Both editions passed all 132 dedicated settings/gesture/fire checks. Every
required final-payload gate listed in the evidence passed before replacement.

Reproduction tools: `prepare_settings_rollback.py`, `package_settings_rollback.py`,
`stage_settings_checks.py`, `check_settings_rollback.py`, `check_audio_rollback.py`,
and `publish_settings_rollback.py`, run through `tools/dev.ps1`.
