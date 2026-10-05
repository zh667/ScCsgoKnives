# Shot line from the eye in every view; Lite and Full in one multiplayer session (2026-10-01)

Status: **r8b delivered to `output/` (2026-10-01): every view shoots the first-person crosshair's line from the eye, and
Full / Lite (+ agents) of one release play together in multiplayer.** Release gates and the mixed-edition multiplayer
matrix (1.9.3.2_MP, Windows) passed; see Results. View acceptance in game by the user ("你改好我自己测试就行了，不用你开游戏测试").
Not done: phone + computer sessions (no Android 1.9.3.2_MP build exists). This brief is the current one for the shot line
and for mixed-edition multiplayer.

## User requirements (verbatim, 2026-10-01)

1. r8a, first person:
   > 就修一下上面那个1. （我推荐）第一人称下判伤射线从眼睛出发，沿准星方向走。 准星对准哪里就打哪里，曳光和枪口火焰仍然从枪口出来，画面不变。调试视角、透视视角、第三人称继续按原版火枪规则，第 4、5 轮"弹道不随摄像机变化"的要求照样满足，因为第一人称下摄像机本来就在眼睛上 .做这个就行。然后你改好我自己测试就行了，不用你开游戏测试
2. r8b, all other views (**supersedes** the "调试视角、透视视角、第三人称继续按原版火枪规则" part of 1):
   > 然后那个第三视角（或者透视视角）开枪的位置有点怪了，虽然是满足了我之前的需求，不如就按这个标准，调试视角和透视视角（或者说所有的第三视角），开枪命中的位置就保持第一视角的准星所在位置就可以（也就是说切换视角不影响这个枪开火命中的位置，不影响枪线，现在的第三视角枪线就是位置奇怪）
3. r8b, multiplayer: "这游戏肯定是要手机和电脑一起联机（手机玩家基本都用轻量版，电脑玩家用全量版）这个问题怎么解决呢？" and
   "API_1.9.3.2_MP 只有zip和tar.gz 应该让lite和full可以相互连接".

Also decided: the three remaining memory options (CT/T warm-up, Full textures, gun warm-up) are **not** to be done
("感觉都不用做了"). Not in scope: building an Android 1.9.3.2_MP platform (the user has only the source archives; phone +
computer sessions need it, see "Not verified").

History: round 4/5 (`round4-smoke-facing-camera-shots-20261001.md`, `round5-free-camera-aim-smoke-look-20261001.md`) shot
from the vanilla musket origin (eye + body right 0.3 − up 0.2) along the camera's direction (free cameras: the character's
look). That rule is superseded by 1 and 2; their "a camera never moves or bends a shot" requirement still holds.

## Why (diagnosis, r7b, isolated 1.9.3.1, before the change)

User report: CT/T head hits lost their headshot sound, damage and crosshair colour. `tools/MpM0/sp_agent_headshot.py`
(T enemies in the camera-fire arena, AK-47, first person): the precise hit test works (a ray from the eye to the head
joint reports `Head logical mesh pose`), but the shot left the round-4 musket origin (eye + body right 0.3 − up 0.2,
parallel to the crosshair). Aimed at the head, that line passed the head joint at 0.35–0.36 m (offset ≈ 0.29 m sideways,
0.20 m down) and hit the arm at ≈ 8.9 m: real shots gave `lastKind 1` (body), 0.124 damage each, no head outcome.
Scratch logs: `hs-r7b-enemy-03.out`, `hs-r7b-la-01.out`.

## Requirement → implementation → check

| Requirement | Implementation | Check |
|---|---|---|
| First person: the damage ray leaves the eye along the crosshair | `ScAimRay.Resolve`: origin `Eye` always; first person (`FirstPerson`: engine `FppCamera`, which sits on `EyePosition` along `EyeRotation`) uses the aim's direction | PackageCheck `aim-ray/first person…` (eye, crosshair direction, 0.36 m from the musket origin) |
| Every other view (third person, debug, perspective/BumanCamera, orbit): the first-person crosshair's line; switching views moves neither the line nor the impact | `Resolve`: not first person → the character's own look (`LookDirection`, the eye's forward = the first-person crosshair centre), whatever the camera's position or direction | PackageCheck `aim-ray/third person…`, `debug camera…`, `orbit camera…`: the same ray as first person's crosshair for the same character, with the camera elsewhere and pointing elsewhere |
| Tracer and muzzle flash still leave the muzzle; picture unchanged | Tracer: drawn viewmodel muzzle, else body muzzle, else the vanilla gun origin (`visualOrigin`); Zeus fallback and the shot broadcast use `visualOrigin`; muzzle flash code untouched | Code review; `sp_camera_fire.py` expects `tracer-from viewmodel` in first person and the body's weapon in third person (runtime, not run) |
| Knife unchanged | Strike: eye along the aim; under a free camera along the character's look (as before) | PackageCheck `aim-ray/a strike…` ×2 |
| Multiplayer shots: same rule on the server | The client's own `Resolve` chooses the direction (its view); the server starts every client shot at its own copy of that player's eye (`ScNetGuns.FromEye`), never consulting a camera of its own; the r8a first-person flag is removed | PackageCheck `aim-ray/multiplayer…`: real `SendInput` → payload → real `ReceiveInput` on a fake host transport, remote player without a game widget and looking elsewhere; MP M1/M2 runtime |
| Full and Lite (+ agents) of one release connect | `Game.ScNetIdentity`: the release pipeline (`followup_140.prepare`) stamps one gameplay identity (hash of the shared gameplay sources) into the Full and Lite cores and both agents builds; the adapter's hello (protocol 3) sends it instead of the exact module, plus the agents identity and edition; `completion_140.identity` gate checks all three candidates carry it | PackageCheck `net-identity/…` (9); pipeline `identity` gate; MP M4 (other edition accepted), M1/M3t/M2 mixed-edition runs |
| A different release is still refused; agents on one side only are refused with a reason players can act on | `ScNetIdentity.Incompatibility`: build differs → "不是同一次发布"; server has agents, client not → "请安装同版本探员包"; client has agents, server not → "服务器没有探员…"; development (unstamped) builds by exact module as before | PackageCheck `net-identity/…`; MP M4: identity hellos from an accepted client refused and re-accepted; a real Lite-only client on a Full server refused with the agents notice |

The engine maps dynamic block indices by class name and saves the map with the world; a joining client builds its world
from the server's project data (1.9.3.2_MP `GameManager` → `ProjectData`), so the agents' item types (Lite: core DLL,
Full: agents DLL, same class names) get the server's indices. The platform itself compares no mod lists on join
(searched `Survivalcraft.Multiplayer`/`Survivalcraft.CompatNet`).

Negative controls: r8a's rule restored in a scratch copy fails exactly the first-person checks; an unstamped build fails
only `net-identity/this core carries a release gameplay identity`; the pipeline's baseline (r8a output) is expected to fail
the third-person/debug/orbit aim-ray checks and the identity checks.

Test tooling: `sp_camera_fire.py` (every view expects the eye along the character's look), `mp_m1.py`/`mp_m2.py` aims from
the eye; `m0.py` `cs_packages` run specs `full`, `lite`, `lite+agents`, `<server>/<clients>`; `candpkg` also copies the
agents package; `mp_m4.py` other-edition section rewritten.

## Results

- VPS type-check: core (Full constants and `SC_SPLIT`) and PackageCheck build; local fixture harness `AimRayRegression`
  9/9 with the new DLL.
- Release pipeline `completion_140 r8a all` (request `r8-pipeline-r8a-all-01`, job `bafd83e1199241dbb2660fe6702027a9`):
  `failed steps: []`; main-full 0 failed of 13978, main-lite 0 of 14284 (aim-ray 9/9 in both); the r7b output packages
  under the same PackageCheck fail exactly `aim-ray/first person…` and the multiplayer fixture (old `SendInput`
  signature) — the Windows-side negative control; vf 0/292 ×2, ai 0/66 ×2, c4 0/73 ×2, tactical-full-suite the 4 known
  failures, motion/throw/hotspots/ui pass. Candidate copies `r8-candpkg-r8a-01` (job `57a7a1eb…`).
- Delivered (job `r8-deliver-r8a-01` = `49bb664978f244a09416fb96a17c0c9f`, move on the same volume, one copy):
  全量 `25cb7224194a793d848cf4927fba70012047a7fe3c2ed7467fa0b800e536d58b` 526,743,495 B, 轻量
  `7d8e690592725471affa5f223b675bfb72442346f8d2043c054c1f3770267fcf` 35,617,502 B, 探员 unchanged
  `0dea4191c1ee4a5c2ce07a11ac75a8afd24982ae9277caeccd10fa1e45179798`. Against r7b only `ScCsgoKnives.dll` changed in
  全量/轻量 (1708 / 1342 members unchanged); the agents package is byte-identical; adapter `Net/ScCsgoNet.bin` `93415266…`
  unchanged. Manifest updated (r7b moved to `replaced.previous`/`history`); `tools/completion_140.py` BASELINES = r8a.
- Cleanup (job `r8-cleanup-r8a-01` = `5dc18b42…`): stage r8a build trees and the two r8a test copies, 2.46 GB; receipt
  `first-person-eye-shot-20261001-cleanup.json`; output unchanged after cleanup.

- r8b release pipeline `r8-pipeline-r8b-all-01` (job `ed1d13c99ab54b24ab4512dfd6b89ae0`): `failed steps: []`; main-full
  0 failed of 13986, main-lite 0 of 14292 (aim-ray 8/8, net-identity 9/9); identity gate: `a0d2d637ef517fac792715e1999d45d8`
  in 全量 `ScCsgoKnives.dll` + `ScCsgoTactical.dll`, 轻量 `ScCsgoKnives.dll`, 探员 `ScCsgoTactical.dll`; vf 0/292 ×2, ai 0/66 ×2,
  c4 0/73 ×2, tactical-full-suite the 4 known failures. Baseline (r8a output) main runs stopped with `TypeLoadException:
  Game.ScNetIdentity` (the old core has no identity); NetIdentityRegression now reports that as a failed check instead
  (tool-only fix after the run, not in any package).
- Candidates `r8-candpkg-r8b-01` (job `aa1ea3b0…`), platform gate `r8-platform-r8b-lite-01` (job `163db3c7…`) 17/17.
- Mixed-edition MP matrix on the r8b candidates (isolated 1.9.3.2_MP copies, loopback, every process 0 errors; the mods
  lines and the server's hello log confirm each process's edition):

  | Run (request) | Job | Result |
  |---|---|---|
  | M4 Full (`r8-m4-full-r8b-01`) | `de81cfa4…` | 30/30: latency/loss, disconnect, rejoin; Lite + agents client accepted on the Full server; hellos claiming another release ("不是同一次发布") and no agents ("请安装同版本探员包") refused, own hello re-accepted; a real Lite-only client refused with the agents notice |
  | M1 Full host, Lite + agents clients (`r8-m1-full-host-r8b-01`) | `ee0e802c…` | 28/28 |
  | M1 Lite + agents host, Full clients (`r8-m1-lite-host-r8b-01`) | `77f85615…` | 28/28 |
  | M3t agents, Full host / Lite + agents clients (`r8-m3t-full-host-r8b-01`) | `1a53acc6…` | 48/48 |
  | M3t agents, Lite + agents host / Full clients (`r8-m3t-lite-host-r8b-01`) | `de10fb61…` | 48/48 |
  | M2 Full host, Lite + agents clients (`r8-m2-full-host-r8b-01`) | `4dbfb3f1…` | 31/31 (body → vest, head → helmet + dink, shots from the eye) |

- Delivered (`r8-deliver-r8b-01` = job `284c90dc…`, move, one copy): 全量 `c9506d59859c7f46d715930e29b006955d1e7479dd3eb31d2d3152fadd61976d`
  526,744,692 B; 轻量 `b4d5fa52169fa01646eddb66d11fc6304c4a59fc0c3d4ebfca2e8b79fd3349bc` 35,618,802 B; 探员
  `522b78296abbae87cc5197d126925f5726da6b9ff4043a86d7cb5be2bf066668` 38,464,900 B. Against r8a: 全量 `Net/ScCsgoNet.bin`,
  `ScCsgoKnives.dll`, `ScCsgoTactical.dll`; 轻量 `Net/ScCsgoNet.bin`, `ScCsgoKnives.dll`; 探员 `ScCsgoTactical.dll`; no members
  added or removed, resources unchanged. Adapter `d00107b9…`. Manifest updated; `tools/completion_140.py` BASELINES = r8b.
- Cleanup `r8-cleanup-r8b-01` (job `2f0d1815…`): stage r8b build trees and the three r8b test copies, 2.51 GB; receipt
  `first-person-eye-shot-20261001-r8b-cleanup.json`; output unchanged.

Time: r8b pipeline ≈ 6 min, candidate copies + platform gate ≈ 3 min, six MP runs ≈ 13 min in total (one chain, no rework).

## Not verified by the agent

- In-game views (first person, third person, debug, perspective view) on 1.9.3.1 — user acceptance; `sp_camera_fire.py`
  is updated for the rule but was not run.
- Phone + computer in one session: needs an Android build of the 1.9.3.2_MP platform, which does not exist (the user has
  only the source archives); whether the adapter (built against the Windows platform references) loads there is unknown.
- Real LAN / several machines / human play: the MP matrix runs on one Windows machine over loopback.
- Mixed editions not run: M3 (grenades/C4) and M2 part 2 (gun mechanics) with mixed editions (same code paths as M1/M2;
  covered single-edition in earlier rounds).
