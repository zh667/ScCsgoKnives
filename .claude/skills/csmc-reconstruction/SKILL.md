---
name: csmc-reconstruction
description: Project facts for reconstructing CSMC's first-person weapon/arm chain offline in Survivalcraft. Use when working on ScCsgoKnives pose/animation/matrix work — known classes and call chain, where the extracted .animbin/mesh/skeleton live, M9/butterfly/karambit calibration, the per-frame matrix export format, SC acceptance rules, and the rule that fitted/assumed values are never marked "precise".
---

# CSMC -> Survivalcraft reconstruction

Goal: reproduce CSMC's first-person result (item state -> animation -> skeleton ->
arm correction -> camera -> final matrices) from data we hold, offline, verified
frame by frame — never by screenshot fitting.

## Where the data is
- Extraction package: `~/workspaces/reference/csmc_ctf_20260902/deliverables/`
  - `animations/full_json`, `animations/runtime_json` (our CsmcAnimation/2 format), `anim_bin/`, `obj_bin/knife/`
  - `weapon_configs/` (model/skin only; NO damage/recoil — those are server-side)
  - `first_person_rendering_sources/` + `INDEX.md` (call chain), `source2_arms.geo.json`
- Our rig data (embedded): `src/ScCsgoKnives/AnimationData/*.csmc.animation.json`

## Known chain (verified)
- Client renders; weapon numbers (recoil/spread/damage) are server-delivered — not in the client.
- First-person hooks: `p001m0/a1e` (GameRenderer.renderHand, bobView cancelled, FOV),
  `p001m0/a1l` (reskins vanilla renderLeftArm/renderRightArm — pose stays vanilla),
  `p001m0/a1g` (GeoRenderer.renderRecursively — routes LeftArm/RightArm verts),
  `p007m6/AbstractC0150a0` (GeckoLib AnimationController — slerp transition on LeftArm/RightArm),
  `C0079b` (19 MB obfuscated bodies; Source2 first-person batch/SSR).
- **Weapon rig** bones (root_0, arm_lower_r, hand_r, weapon_hand_r, fingers) ARE in our
  animbin and reconstruct offline. **LeftArm/RightArm** are a separate arm animatable
  (source2_arms.geo.json, 2 rootless bones), NOT in the weapon animbin. The SC port
  synthesises its own arm box and does not need them — do not fit them in.

## Calibration (M9 inspect, from the offline harness)
- weapon_hand_r cumulative roll 729 deg > hand_r 549 deg over the clip -> the arm's roll
  must follow hand_r (the wrist), never weapon_hand_r (0.11.13).
- weapon-relative-to-wrist rests at ~32 deg at the M9 hold.
- Position follows weapon_hand_r; roll follows hand_r; squaring/clearance only after the
  wrist settles (0.11.14-0.11.16).

## The offline harness
- `tools/ArmPreview trace <knife> <clip> <fps> <out.jsonl>` dumps per-frame CSMC bone
  world matrices (KnifeRigPose.Bones) via the validated CsmcKnifeRig sampler.
- `tools/preview.py` draws the shipped C# arm solve; `tools/trace.py` decomposes the
  trace to t/q/s and runs frame-to-frame self-consistency. Use these, NOT the deprecated
  Python replicas (fleet_qa/roll_sweep) for anything dynamic.
- `verify_cs.py` guards the idle photo-fit composition.

## Acceptance (per weapon)
idle hold, deploy-in, inspect start/mid/end, attack, deploy-out. Check grip-in-palm,
tip path, arm rotation, camera shake, clip start/end continuity, keyframe timing,
and diff vs MCCS recording. Pass M9 first, then butterfly/karambit, then the rest.

## Non-negotiable
Fitted or assumed values are marked "approximate / missing server truth" — never
"precise/complete". Server-only numbers need the author or a black-box experiment.
