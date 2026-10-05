# Round 10 (2026-10-01): third-person gun pitch with the CS player appearance, wisps for all four snipers, HUD icon, knife swing estimate

Status: **r10a delivered (2026-10-01); user acceptance pending** (pitch direction and look of the bend, the sniper lines, the HUD). Knife swing: estimate only. Base: r9g. The user tests in game; no agent
runtime test after the change ("这些你做完不用实机测试，我来测试就可以").

## User requirements (verbatim, 2026-10-01, after testing r9g)

> 1.我刚才去游戏里看了，我没说错，第一人称枪械没问题，会跟着我的视角，枪械倾斜，但是所有的第三人称都不会，所以现在的弹道看着还是奇怪，不是比之前好一些了 2.目前只有awp有这个枪线 3.怎么把枪的图标删了，上面是图标，下面是这个剩余子弹 / 携带弹匣 4.我发现现在第三人称挥刀动作（轻或重刀，动画都不对，先评估加这个会增大多少包的体积，先别做，这些你做完不用实机测试，我来测试就可以

1. Every third-person view: the held gun must pitch with the view (the user was right; round 9's diagnosis was wrong for
   the user's setup), so the shot line no longer looks odd.
2. All CS2 guns with a lingering line show it (the user saw only the AWP's).
3. HUD: the weapon icon on top, the line "rounds left / magazines carried" below.
4. Third-person knife light/heavy swings look wrong: **estimate the package-size cost only; do not implement.**

## 1. Diagnosis

- The user's `Bugs/Game.log` (2026-10-01): 329 `[CS_SHOT]` lines, all from free cameras (DebugCamera 239, BumanCamera 81,
  OrbitCamera 9), all player P2, **all `tracer-from origin`**: the third-person weapon was never posed by ScThirdPerson in
  the user's sessions. Every session logs `CS战术拓展：已启用内置 T/CT 玩家外观。` — the player is drawn as a CS2 CT/T agent,
  a **skinned** model, and `ScThirdPerson.Pose` returns early for skinned models; the held gun then comes from the
  appearance path, which does not follow the look pitch and gives no muzzle.
- Round 9's probes (`sp_tp_pitch.py`) set the look angle on the default (and NekoMeko) player model, so they never
  exercised this path; their "the gun already follows the look" conclusion holds only without the CS appearance.
- `tools/MpM0/sp_tp_user.py` (job `d2a30df4…`): with every package of the user's Mods folder but without the CS
  appearance, ScThirdPerson posed the gun in third person, debug and orbit cameras alike ("ok", muzzle available) —
  consistent with the appearance being the difference.
- 2: all four snipers spawn their wisp (cs2_effects entries and the embedded copy checked). The user's views were free
  cameras right beside the shooter firing up into the sky; the rope fade was measured against the ray from the camera to
  each point, which there runs along the line, so the SSG/G3SG1/SCAR ropes (fade from 0.9) vanished while the AWP's
  (0.995, smoke none) stayed. Change: the fade compares the rope with the camera's forward axis (the usual engine
  reading); the exact-end-on guard stays.
- 3: round 9 hid the icon reading "只保留这一行" as "only this line"; the user wants icon above line. Change: icon shown
  (filled by the loaded fraction), its own count hidden, line below; PackageCheck
  `features130/right-hud-icon-above-one-line-rounds-and-magazines`.

## 1. Change (r10a)

Read-only exploration of the appearance path (agent report): with the CS appearance, `ComponentCsPlayerAppearance` samples
the CS2 actor clips (`CsPlayerPose`), `ScAgentActions.ApplyHeld` overwrites every bone under `spine_0` with the static
`hold_<gun>` pose, `CsPlayerItems.Draw` draws the gun on `cswp_<gun>/weapon` under `cs_weapon_mount` (a child of
`spine_2`). Nothing applied the look pitch (players or NPCs) and no muzzle position existed, so the shot code fell back to
`ScAimRay.GunOrigin` ("tracer-from origin").
- `ScAgentActions.ApplyAimPitch`: after the hold pose, spine_0/1/2 bend about local Z by 20/35/45 % of the look pitch
  (−Z = back/up: inferred from the shot recoil, −0.05 Z on spine_2, and the death slump, +Z forward on spine_1/neck_0);
  only for gun stances (`PitchFollows`), not while throwing, planting, lying or with the shield; clamp ±1.2 rad as
  ScThirdPerson. Remote players' LookAngles are replicated by the platform, so observers see it too.
- `ScThirdPerson.ReportMuzzle` (core): `CsPlayerItems.Draw` reports the drawn right/left muzzles each draw;
  `TryGetMuzzleWorld` uses them when ScThirdPerson has no pose (≤ 2 frames old, same gun) → the tracer leaves the drawn
  muzzle. PackageCheck `aim-ray/a skinned player's drawn muzzle…`.
- Not done: aiming the barrel at the hit point on this path (the CS agent holds the gun near the head; the line is parallel
  to the look). Sign and look of the bend are the user's in-game check (no agent runtime test, by request).

## 4. Knife swing estimate (not implemented)

Current third-person knife with the CS appearance: no world attack clip exists in the shipped data (only `draw_`/`hold_`
per knife); light and heavy both play the same procedural torso twist (`ApplyHeld`: spine_2 RotationY 0.35·wave,
RotationZ −0.18·wave), the arm stays rigid — hence "wrong". CS2's own world knife attacks are in the raw CT/T exports on
Windows (`animation/anims/world/knife/**`, 366 entries per role; exact attack names not yet listed). Cost from the shipped
actor cache format (`.scanim`, ≈ 13.5 B per channel-key; ≈ 119 KB raw / 32–37 KB compressed per clip per role at 170 clips):
- one shared light/heavy set (≈ 4 clips: light ×2, heavy, maybe the hit variants) × CT + T: ≈ 0.7 MB raw, **≈ 0.25–0.3 MB
  per package** compressed (Full 526.9 MB: +0.06 %; agents package 38.5 MB: +0.7 %; Lite core unaffected unless its own
  appearance cache) and ≈ 0.7 MB more resident memory once loaded;
- per-knife sets (22 knives × ≈ 4 × 2): ≈ 15 MB raw, **≈ 5–6 MB per package** compressed.
The default (non-CS-appearance) player's knife swing is procedural arm angles (ScThirdPersonMotion): improving it costs no
package size.

## Results

- Pipeline `r10-pipeline-r10a-all-01` (job `ff8782553d704d5caa92507d6bde9cc8`): `failed steps: []`; appearance built for
  both editions; main-full 0/14019, main-lite 0/14329; the r9g baseline fails exactly the two new checks (HUD icon above the
  line, skinned-player muzzle hand-off); tactical-full-suite the 4 known failures.
- **Delivered** (`r10-deliver-r10a-01` = job `0f46c973…`, move, one copy): 全量 `581eb16cc7d6a0b3e0f4b9f43edd88fa2dd32d6164962057eb261a4ef4c20973`
  526,869,801 B; 轻量 `8f7ba9bd97ac0040eb382f8a1ba367fc686d0ad6021e978f0cdb550b70b24a6c` 35,716,292 B; 探员
  `39c94776efe9240f3ab0932219da4a95ae226c5b1271fd7bf698fd8ddce4fef9` 38,465,116 B. Against r9g: 全量 `Integrations/ScCsgoAppearance.bin`,
  `ScCsgoKnives.dll`, `ScCsgoTactical.dll`; 轻量 `ScCsgoKnives.dll`; 探员 `Integrations/ScCsgoAppearance.bin`, `ScCsgoTactical.dll`;
  nothing added or removed. Manifest updated; `tools/completion_140.py` BASELINES = r10a.
- Cleanup (`r10-cleanup-r10a-01`): r10a build trees, 1.89 GB; receipt `round10-agent-appearance-pitch-wisp-hud-20261001-cleanup.json`.

## Not verified by the agent (user tests in game, by request)

- The bend's direction (−Z = up, inferred from recoil and death-slump code) and amount in every third-person view with the
  CS appearance; whether the tracer now leaves the drawn muzzle (log: `tracer-from body` expected in `[CS_SHOT]` lines).
- The SSG 08 / G3SG1 / SCAR-20 lines from the user's free cameras; the HUD with the icon.
- Without the CS appearance the round-9 path (ScThirdPerson, barrel converging on the aim point) is unchanged.
