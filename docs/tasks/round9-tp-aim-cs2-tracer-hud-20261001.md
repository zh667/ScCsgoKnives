# Round 9 (2026-10-01): third-person barrel and tracer alignment, CS2 tracers, one-line ammo HUD

Status: **delivered as r9g (2026-10-01); user acceptance pending** for the tracer look, the wisp's intensity and the HUD.
Base: r8b. Current brief for these three items.

## User requirements (verbatim, 2026-10-01)

> 我好像发现了枪线和枪口对不齐的原因了，这是因为在第三视角下，我们人物模型拿枪的姿势一直都是水平的，我们的枪没有根据当时的第一视角移动视角而变换角度，所以才导致看起来枪线和枪口对不齐，然后这个曳光弹我感觉还是不够还原cs2（注意是cs2而不是csgo），这个应该可以在cs2资源中找到，而且一些枪开枪之后会留下明显的枪线（比如awp，其他的我忘了）。然后还有一个反馈：枪械HUD 可可以改成” 子弹剩余装弹量 /总弹夹数 “的格式，只保留这一行，然后字号大些（也不要太大，略微大一点点）。装填的时候也不用特意加个“装填”文字

1. Tracer and muzzle line up in third person (user's hypothesis: the held gun stays level and does not follow the view).
2. Tracers closer to CS2 (not CS:GO), from CS2's own resources; guns that leave a visible lingering line after the shot in CS2
   (AWP named; others to be found in the data) do so.
3. Ammo HUD: one line "rounds left in the gun / magazines carried", slightly larger font, no "装填" text while reloading.

## 1. Third-person barrel — diagnosis (r8b, isolated 1.9.3.1, `tools/MpM0/sp_tp_pitch.py`)

Requests `r9-tp-pitch-full-nmm-02` (r8b Full + the user's NekoMeko Model 1.1 and Neorxna 1.4) and `r9-tp-pitch-full-02`
(r8b Full alone), third person, look pitch 0 / +0.5 / −0.5 / +0.9 rad:

- The hypothesis is **not** what happens in the third-person camera: ScThirdPerson posed the gun on both models (11-bone
  NekoMeko and 6-bone vanilla) and its forward axis followed the look exactly (pitch +0.5 → forward y 0.479 = sin 0.5;
  −0.5 → −0.479; +0.9 → 0.783).
- What misaligns the line: the shot leaves the eye (r8b) while the gun hangs ≈ 0.45 m below it; with the barrel parallel to
  the look, the tracer (muzzle → impact on the eye's line) crosses the barrel. Looking down 0.5 rad at the ground 2.8 m
  ahead: barrel −28.6°, tracer −17°, **11.7° apart**; far impacts nearly parallel.
- Multiplayer: the platform replicates LookAngles (`PlayerBodyUpdatePacket`), so other players' guns pitch the same way.

Change: the held gun's barrel aims at what the eye's line meets (`ScThirdPerson.AimDistance`: `ScGunRange.TraceBullet` +
bodies, 64 m when nothing; smoothed; at least 1.5 m; at most 0.35 rad off the look; `ScThirdPersonMath.ConvergeBore`
refines the muzzle position six times). Arms unchanged; knife/grenade/C4 unchanged (no pitch follow).
Checks: PackageCheck `aim-ray/third-person barrel…` (5; the line from the muzzle meets the aim point within 1 mm, far
target nearly parallel, bound respected). Runtime re-measure: `sp_tp_pitch.py` now reports `tracerBarrelDeg` (r8b: 11.72°
at −0.5) — to run on the candidate.

## 2. CS2 tracers

Research (read-only agent over the decompiled CS2 data, Windows jobs `54d056c5…`, `ba9053b2…`, `2420a420…`, `480505c5…`,
`896a0db9…`): the lingering line is a **child system of the tracer**, not part of the streak, and only four guns have one:
AWP (`weapon_tracers_rifle` → `_rifle_wisp`), SSG 08 and G3SG1 (`_rifle_ssg` → `_rifle_wisp_ssg`), SCAR-20 (`_rifle_scar` →
`_rifle_wisp_scar`); all 380 exported .vpcf searched. `tools/cs2_effects.py` never read `m_Children`, so none was drawn and
their "Unmodelled" lists were empty. Other gaps found: the streak passes' 1D colour lookups (white core, orange/red
fringe) were not read; the M4A1-S / USP-S vdata `m_nTracerFrequency` [3, 0] / [1, 0] (second value = suppressed mode, the
one with the smaller spread) was read as its first value only; the sniper streak's +20 in start offset was ignored.

Changes (r9a):
- `Cs2Wisp` / `Cs2WispTrail` + `SubsystemScGunBlockBehavior.DrawWisps`: per drawn sniper tracer, the line from the muzzle
  (CP0) to the impact (CP1), 8..36 points by length (SCAR 30), 2 s, radius ×3 → ×0.5 → ×5 (AWP/SSG 3 in, SCAR 1 in),
  alpha per variant (AWP 0.1–0.5, SCAR 0.6–1.0, life curves from the files), born orange then grey, glowing for the first
  ≈ 40 % of life then lit like smoke (cell light), drifting (local velocity/acceleration, gravity, drag, turbulence,
  per-point force) and held near both ends (DampenToCP 500 in → 0.2). Two ropes: core `beam_energy_01` (×0.5 radius,
  additive, screen ≤ 0.03), smoke `beam_smoke_01` (×3 radius, alpha blend, colour ×150/159/165, screen ≤ 0.1), V repeating
  and scrolling as the file says; drawn within 1000 in of the viewer. Local and remote (MP observers) shots alike.
- Streak colour: `cs2_tracer_add_lut` / `cs2_tracer_blend_lut` (colour lookups baked into copies of the streak textures)
  replace the two streak textures for every system using them (assault rifle, rifle family, pistol, shotgun).
- Suppressed M4A1-S / USP-S: no tracer. Sniper streak starts 20 in along the shot.
- Assets: `tools/import_cs2_tracer_round9.py` (Windows job `6c762e44…`) → record
  `round9-tp-aim-cs2-tracer-hud-20261001-assets.json` (Full PNG, Lite lossless WebP), added to both core packages by
  `completion_140` (`TRACER_RECORD`).

Approximations (the user's visual acceptance decides): which channel feeds the colour lookup and the RGB-as-alpha weight;
HALF_BLEND_ADD and overbright 2 drawn as additive core + alpha-blended smoke; the turbulence field (smooth per-point noise,
not Source's Perlin field); drag 0.3 read per 1/30 s (unit not stated; per 1/10 s the line drifted 3.5 m, a cloud); the
local frame of the initial velocity (X along the shot, Y left). Not drawn: the ring sprites (`particle_ring_wave_8` not
extracted), the `beam_generic_2` / `base_rope` layers (not extracted), the crack/breakup layers, view-direction fades,
CP3 (first-person) scaling, the SCAR's CP3 lifetime scale, the ropes' bloom-only passes.
Checks: PackageCheck `cs2-wisp/…` (which guns, point counts, radius/alpha/glow/taper curves, a 30 m line simulated for 2 s
stays finite and drifts < 2 m, more in the middle than at its ends, the four textures in the package).

## 3. Ammo HUD — implemented

`ScAmmoHud.Show`: compact readout shows one line `LoadedText / ReserveCount` (magazines, or shells for shotguns; ∞ in
creative), magazine picture and status line (装填 / 充能) hidden; the Zeus keeps its line (ready `1 / 1`, countdown `3.4 s`).
Font 0.65 → 0.72 (desktop), 0.56 → 0.62 (mobile). Layout editor sample shows `30 / 3`. Check: PackageCheck
`features130/right-hud-one-line-rounds-and-magazines` (replaces `right-hud-compact-magazine-count`, whose two-part layout
the user replaced). Visual acceptance: user.

## Results

- r9a (job `9b618403a0ac4205accc811cd958e867`): `failed steps: []`, main-full 0/14017, main-lite 0/14327, 4 tracer textures
  added to Full and Lite, identity in all four DLLs. Probe on the r9a candidates (1.9.3.1): tracer/barrel angle at most
  **1.36°** (Full + NekoMeko/Neorxna, job `7e8172cb…`) and **1.39°** (Lite + agents, job `ba17e2e5…`), was 11.72° on r8b;
  the AWP shot creates one wisp in third and first person and all four textures load in both editions. Frames (Lite +
  agents) showed the line, but where it ran nearly end-on to the view a segment twisted into a hard-edged bright bow-tie.
  Not delivered.
- r9b: the ropes' view fades from the files (AWP core m_flStartFadeDot 0.995 → 1.0; SSG both ropes 0.9 → 1.0; SCAR none),
  the width direction kept continuous along the rope, exactly end-on joints dropped. Chain `chain_r9b.sh`: pipeline
  `r9-pipeline-r9b-all-01` = job `3e03f44b6f784864b3954a34369fe427`, candidate copies, the same two probes (frames now kept in the run folder).
- r9b (job `3e03f44b6f784864b3954a34369fe427`): `failed steps: []`, main 0/14017 and 0/14327; the r8b baseline fails exactly
  the three new checks (one-line HUD, barrel convergence, wisp). Probes: angle ≤ 1.39° (Full + providers, job `c0d2133f…`),
  ≤ 1.44° (Lite + agents, job `5b904361…`). Frames: no twisted bow-tie any more; a thin line at 0.12 s, a blue-grey twisting
  smoke band at 0.72 s, gone by 1.7 s; but a long sky shot spreads 36 points ≈ 7 m apart, so within the 25 m draw distance
  a few straight spans changed width/fade at once and showed as angular bright blobs (far end in third person, above the
  muzzle in first person). Not delivered.
- r9c: the drawn rope is cut into ≤ ≈ 0.75 m pieces along a Catmull-Rom curve through the points, taper by fractional
  index (PackageCheck `cs2-wisp/the drawn rope is cut…`). Chain `chain_r9c.sh`: pipeline `r9-pipeline-r9c-all-01` = job
  `cba35c273f7542d4bd0352a7491be22b`, candidate copies, the same two probes.
- r9c (job `cba35c273f7542d4bd0352a7491be22b`): `failed steps: []`, main 0/14018 and 0/14328; angles 1.40° / 1.39° (jobs
  `9eb0b008…`, `6fff3cf7…`). Frames: smooth line and smoke band, but a bright spiky "star" where the line is seen nearly
  along its length (vanishing point in first person, far end from behind): many short additive core segments pile up on
  the same pixels. Not delivered.
- r9d: the additive core fades by the whole line's direction against the view ray from 0.985 to 1 (the files say 0.995 /
  0.9 per segment: a deliberate widening, recorded as a deviation); the smoke rope keeps the files' values. The probe adds
  a side view (debug camera 6 m to the side, 8 m ahead). Chain `chain_r9d.sh`: pipeline `r9-pipeline-r9d-all-01` = job
  `658fd39a6ce3430d8872e04e6d53cfd4`, candidate copies, the two probes.
- r9d (job `658fd39a6ce3430d8872e04e6d53cfd4`): `failed steps: []`, main 0/14018 and 0/14328; angles 1.40° / 1.43° (jobs
  `691b19a6…`, `4070cd6f…`). Side view (new): a long thin line at 0.12 s, a soft drifting smoke band with a curled end at
  0.72 s, gone by 1.7 s. From behind and in first person the core still formed a bright zigzag star (the part 3..5 m from
  the eye at 4..7° was only half faded). Not delivered.
- r9e: the core's line-direction fade window 0.95 → 0.985 (≈ 18° → 10°). Pipeline `r9-pipeline-r9e-all-01` = job `35b30f7c02a946eeb635ec87bf3456a0`.
- r9e (job `35b30f7c02a946eeb635ec87bf3456a0`): `failed steps: []`, main 0/14018 and 0/14328; angles 1.44° / 1.58°. Frames
  (random spawns inside trees / a cave) still showed bright slivers end-on. Cause found in code: the smoke rope was drawn with
  the engine's `BlendState.AlphaBlend`, which is premultiplied (One, InverseSourceAlpha); with straight RGBA textures and
  straight vertex colours its colour was added at full strength whatever its alpha, so none of the smoke's fades (size,
  angle, distance, taper, life) dimmed it — the pitfall `ScGrenadeVisuals` already documents. Not delivered.
- r9f: smoke rope drawn `BlendState.NonPremultiplied`; the probe's wisp phase runs 25 blocks up in open air. Pipeline `r9-pipeline-r9f-all-01` = job `bd99036caa48409aa3b5eb569483a203`.
- r9f (job `bd99036caa48409aa3b5eb569483a203`): `failed steps: []`, main 0/14018 and 0/14328; angles 1.37° / 1.39° (jobs
  `9afbf2c4…`, `0931fb1b…`). Frames in open air: no star, blob or bow-tie in any view — the r9a–r9e "stars" were the
  smoke rope at full colour (premultiplied blend), not the core. But the line is now faint: a thin grey line at 0.12 s,
  barely visible grey smoke at 0.72 s, the additive core invisible on the bright sky. Not delivered.
- r9g: smoke colour ×2 (the ropes' overbright 2, left out before); the core's view fade back to the files' per-segment
  values (AWP 0.995 → 1, SSG 0.9 → 1, SCAR none) — the r9d/r9e widening answered a misread cause and is withdrawn. Pipeline `r9-pipeline-r9g-all-01` = job `6373807b56b84a60a181944c60934657`.
- r9g (job `6373807b56b84a60a181944c60934657`): `failed steps: []`, main 0/14018 and 0/14328; the r8b baseline fails exactly
  the three new checks. Probes (jobs `96586cfa…` Full + providers, `4685d18b…` Lite + agents): angle ≤ 1.4°; frames clean
  in every view (no star, blob or twist); the wisp is a thin white line at 0.12 s, light grey smoke after, gone by ≈ 1.7 s
  — subtle against a bright sky, more visible against terrain. Intensity is the user's call (the files' values are used:
  alpha 0.1–0.5, overbright 2; HALF_BLEND_ADD approximated).
- **Delivered** (`r9-deliver-r9g-01` = job `181bea40…`, move, one copy): 全量 `1c9fd7cfaf8fa575d48ab2f8519bd52531649a0594359074b0e401b74a2335d1`
  526,869,360 B; 轻量 `ff673e4e78658425c7083fe611b5afbabc98e432d4687910a360e505349d70e0` 35,716,037 B; 探员
  `bf5208bb9ad279a0b56721c2fa31deb2ca53db31b2f59683599b9fc7eaf6b01e` 38,464,900 B. Against r8b: 全量 resource list, core and
  agents DLLs changed, 4 PNG added; 轻量 resource list and core DLL changed, 4 WebP added; 探员 agents DLL; adapter unchanged.
  Manifest updated; `tools/completion_140.py` BASELINES = r9g.
- Cleanup (`r9-cleanup-01` = job `8ffd3118…`): r9g build trees, r9a–r9f build trees and undelivered candidates, the 21 r9
  probe copies — 21.1 GB; receipt `round9-tp-aim-cs2-tracer-hud-20261001-cleanup.json`; output unchanged.

## Not verified by the agent

- The look in play: tracer colour, wisp intensity and drift against CS2, the HUD line — user acceptance.
- Multiplayer runtime not rerun (no message or state change; observers get wisps through the existing remote-shot path).
- Ring sprites and three extra rope layers not drawn (textures not extracted); other approximations listed in section 2.
