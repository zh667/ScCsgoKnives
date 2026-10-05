# Round 11 (2026-10-01): CS-appearance aim pose like CS2, SSG 08 / G3SG1 smoke

Status: **r11b delivered (2026-10-01); user acceptance pending** (look of the aim pose in every third-person view with the CS
appearance, SSG 08 / G3SG1 smoke). Base: r10a. The user tests in game; no agent runtime test of r11b ("你不用实机测试，这个我来测试就行").

## User requirements (verbatim, 2026-10-01, after testing r10a)

> 那就先不加轻重刀的动画了，但是现在第三人称视角动画有问题，就是你上次做的第一点，就是这个幅度看起来很怪，人物扭身体的样子很怪，这个动画应该能去cs2资源里找，完全模仿就可以了 然后ssg08 g3sg1的枪弹道的那个烟还是不明显，scar-20的倒是很明显

1. No knife light/heavy animations for now (decision).
2. The third-person aim with the CS appearance (round 10 item 1): the amplitude and the body twist look wrong; take it
   from CS2 and imitate it fully.
3. SSG 08 / G3SG1 trail smoke still not noticeable; SCAR-20's is.

## 2. What CS2 has (read from the user's CS2 install, Windows jobs `r11-*`)

- The CT/T exports (2,062 clips each) and the game archive (`pak01_dir.vpk`, 2,735 `.vnmclip_c`) contain **no aim-pose
  clips** (no aim matrix, no up/down poses; world folders hold idle/walk/run/jump/turn/ladder/draw/reload/shoot only).
- The world player graph `animation/graphs/worldmodel/worldmodel.vnmgraph` (decompiled with Source2Viewer-CLI into
  `.tmp/dev-temp/cs2-graphs`) aims with one procedural node, `CNmGraphDocAimCSNode` "Aim IK": inputs Horizontal/Vertical
  Aim Angle (`aim_angle_yaw`, `aim_angle_pitch`), Weapon Category/Type/Action, Weapon Drop, Crouch Weight, Is Defusing;
  blend times action 0.4 s, hand IK 0.3 s, planting 0.2 s. The bone work is compiled game code, not data; the skeleton
  carries its helpers (`scap_L_AIMAT`, `scap_R_AIMAT` under spine_2, `scap_AIMUP` under spine_3).
- So an exact copy of CS2's aim is not available as data. Reproduced instead: its visible result.

## Change (r11a)

- `ScAgentActions.ApplyAimPitch` rewritten. Round 10 rotated spine_0/1/2 about each bone's own local Z by 20/35/45 % of
  the whole pitch (the whole torso bent up to 69°; the hold pose's spine locals are not pure Z rotations, so it also
  twisted). Now every rotation is about one model-space axis, the line through the clavicles (no twist): spine_0/1/2
  lean 10 % each, the arms (clavicle_L/R) and the weapon mount (cs_weapon_mount) turn together about the shoulder midpoint
  by 70 % (gun on the look, hands stay on the gun), the neck adds 50 % (head ≈ 80 %). The rotation's sign is tested each
  call (lifts what faces forward). Skeleton (shipped ct.glb): pelvis → spine_0 → spine_1 → spine_2 → {spine_3 → {neck_0 →
  head_0, clavicle_L/R}, cs_weapon_mount}.
- 3: SSG 08 / G3SG1 smoke rope drawn without the file's 0.9 fade dot (a deliberate deviation for visibility: AWP and
  SCAR-20 smoke have none); the core keeps it.

## Change (r11b, delivered)

- The r11a axis (through the clavicles) is diagonal in CS2's rifle stance, so the barrel rolled and followed only about
  0.66 of the look. r11b turns about one horizontal axis square to the held gun's barrel (taken from the drawn weapon's
  frame in the pose): `axis = up × barrel`, where `up` runs pelvis → neck and the barrel is flattened onto the plane
  perpendicular to `up`. The proportions are unchanged:
  - spine_0/1/2 lean 10 % each (30 % total);
  - the clavicles and the weapon mount turn 70 % about the shoulder midpoint, so the gun lands on the look;
  - the neck adds 50 %, so the head follows about 80 %.
- 3 is unchanged from r11a: the SSG 08 / G3SG1 smoke has no fade dot.

## Results

- r11a was not delivered. Pipeline job `dde97eb1f7554fc1a82c7a86ee198ffe` passed (`failed steps: []`, main 0/14019 and
  0/14329). Probe job `13a79732…` used the CS CT appearance, switched on through NekoMeko's `SetResModel("zh667.cs.ct")`:
  - the tracer left the drawn muzzle (`tracer-from body`) at every pitch;
  - the barrel followed only 0.33 / −0.345 / 0.59 of a 0.5 / −0.5 / 1.0 look;
  - the barrel rolled: its side axis rose to y 0.37 / 0.64.
- r11b pipeline `r11-pipeline-r11b-all-01` (job `2eeb9e986dda4a1f9dc0d1263d397831`):
  - `failed steps: []`;
  - appearance built for both editions;
  - main-full 0/14019, main-lite 0/14329.
- The user said "你不用实机测试，这个我来测试就行", so the chain was stopped before its candidate copies and probe.
- **Delivered** by move, one copy (`r11-deliver-r11b-01` = job `de24bc73c64a40bcb43f377b819edeb5`):

  | Package | SHA-256 | Size (B) |
  |---|---|---|
  | 全量 | `d4b4ef32d939d4253f89cbf8ba16ba0c4044505f909f5d0530b1947d37dc4d12` | 526,871,274 |
  | 轻量 | `867c03c6f9f572a7cbdb68eb0a173c5e29cb443cec33a2a63fc2a7b0c15b3779` | 35,716,278 |
  | 探员 | `9328a9799a42af0e8320486b3cddd9d53fdb7b04950ac80acc3986f446832aa2` | 38,465,868 |

  Files changed against r10a (nothing added or removed):
  - 全量: `Integrations/ScCsgoAppearance.bin`, `ScCsgoKnives.dll`, `ScCsgoTactical.dll`;
  - 轻量: `ScCsgoKnives.dll`;
  - 探员: `Integrations/ScCsgoAppearance.bin`, `ScCsgoTactical.dll`.

  The manifest is updated, and `tools/completion_140.py` BASELINES = r11b.
- Cleanup (`r11-cleanup-r11b-01` = job `aecb46ea…`) removed 4.99 GB; output hashes were unchanged and E: has 12.9 % free.
  Receipt: `round11-cs-aim-pose-ssg-smoke-20261001-cleanup.json`. Removed:
  - the r11b build trees;
  - the r11a build trees and its undelivered candidates;
  - the `dev-r11a-{full,lite,agents}.scmod` probe copies.

## Not verified by the agent (user tests in game, by request)

- r11b's aim pose with the CS T/CT appearance in every third-person view (third person, debug, perspective, orbit):
  - whether the gun follows the look without a twist or roll, and how the amount looks;
  - whether `[CS_SHOT]` lines show `tracer-from body`.
  r11a's measured numbers do not carry over to r11b (different axis).
- The SSG 08 / G3SG1 trail smoke from the user's free cameras.
- Unchanged: the path without the CS appearance (ScThirdPerson), and first person.
