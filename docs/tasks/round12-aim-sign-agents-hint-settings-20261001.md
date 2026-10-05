# Round 12 (2026-10-01): aim pitch direction, agents-package hint, tidy mod settings

Status: **r12c delivered (2026-10-01); user acceptance pending.** Base: r11b. The user tests in game; no agent runtime
test ("你不用启动游戏测试").

## User requirements (verbatim, 2026-10-01, after testing r11b)

> 1.第一人称视角抬头看天，切换第三人称却是人物头埋地2.我只安装了轻量包，没有提示我缺少探员包（当然这只是一个提示，不影响运行，然后不要影响那个模组冲突或者缺少前置的提示，我们的提示放在他们后面，如果有那些异常情况的话，然后探员包提示可以给一个超链接：https://files.zh667.cn）。这个探员包没有提醒的原因可能是我先装了全量，后面我再用的轻量？ 3.模组设置里面太乱了，一些设置下面不需要那么多的文字，精简一些，只保留必要提示，那个受击方向创造模式不用单开一个选更新，和上面那个受到cs武器显示方向共用一个就行了，还有那个编辑按钮布局前面怎么是按键与弹药HUD的位置布局？直接按键位置布局就行了，然后下面怎么还有一个护甲HUD的勾选？这个我记得在编辑按钮布局可以设置显示或者不显示的。先做这些吧，你不用启动游戏测试

1. Looking up in first person, then switching to third person: the character's head points at the ground.
2. Lite without the agents package: show a hint (only a hint, nothing stops). Leave the engine's mod-conflict and
   missing-prerequisite prompts alone, and show ours after them when they appear. Include a hyperlink to
   https://files.zh667.cn. The user also asked whether the missing hint was caused by installing Full first, then Lite.
3. Mod settings:
   - Trim the text: keep only necessary hints.
   - Merge the creative-mode hit-direction switch into "受到CS武器伤害时显示方向".
   - The layout row reads "按键位置布局".
   - No armor HUD checkbox on this page. The user remembers the layout editor having a show/hide switch for it.

## 1. Diagnosis and change

- **Path.** The user's `Bugs/Game.log` shows these sessions on 2026-10-01:

  | Session | Package | CS T/CT appearance | Shots |
  |---|---|---|---|
  | 20:34 | Full | yes | 57; 47 `tracer-from body`, muzzle rising with the look |
  | 21:06 | Full | yes | none |
  | 21:09, 21:11 | Lite only (r11b Lite, 35,716,278 B) | no | none |

  The r11b Lite file in Mods is dated 21:02, so the 20:34 session ran r10a. The head-down observation is the CS
  appearance path (`ScAgentActions.ApplyAimPitch`, agents code: Full and the agents package).
- **Cause.** r11b took the turn axis as `up × barrel` and kept the old sign test, which lifts `up × axis`. That vector is
  −barrel, the barrel's back. So a positive pitch (looking up) turned the gun, the spine and the head **down**. r11a's axis
  (through the clavicles) made the same test lift the front, which is why its probe measured the barrel following upward.
- **Local check** (scratch harness `r12unit`, the built tactical DLL, four skeleton orientations). For +0.5 rad:
  - the r11b formula moves the front by −0.479;
  - the new `ScAgentActions.LiftAxis(up, front)` moves it by +0.479 (= sin 0.5).
- **Change.**
  - `LiftAxis` is a horizontal axis square to the front. Its sign is read on the front itself.
  - `ApplyAimPitch` turns about it with +angle. Proportions are unchanged (spine 3×10 %, arms/mount 70 %, neck 50 %).
  - r12b: "up" is the vertical of the bone space (Y after the root bone, as the crouch squash and lie-down use). It was
    the pelvis-to-neck line of the stance.
    - That line leans. In r12a the Desert Eagle barrel drifted off its yaw at the 1.2 rad clamp (front dot 0.899).
    - With true vertical, the barrel stays in its own vertical plane.
- **Checks.**
  - PackageCheck tactical suite `aim-pitch-lift-axis-lifts-the-front`: seven up/front pairs, ±0.3/±1.2 rad.
  - `ThrowPoseCheck` (the pipeline's native `throw` step), on the packaged CT and T actors with the hold pose then
    `ApplyAimPitch`, as `CsPlayerPose` does, for M4A4, FAMAS (both in the user's log) and Desert Eagle at pitch
    +0.6 / −0.6 / +1.2. AK-47, M4A1-S and AWP ship as OBJ pieces, which this harness has no reader for: r12a's weapon build
    threw a NullReferenceException for AK-47 and AWP, a harness content gap, not the game. The checks:
    - the barrel follows the pitch within 20 %;
    - the muzzle moves with the look;
    - the face's forward axis follows 50–105 % of the pitch, in the same direction;
    - no roll (cant change about the barrel ≤ 0.1 rad) and no yaw (front dot ≥ 0.98).

## 2. Agents-package hint

- **Answer to the user's question.** Install order made no difference: no package had such a hint. Round 6's hint
  (`TacticalPrerequisites`: NekoMeko Model / Neorxna) lives in the agents code, so it never runs with Lite alone.
- **Engine order.** `MainMenuScreen.Enter` queues the engine's disabled/conflicting-mods dialog
  (`ModsManager.ShowDisabledModsDialog`). Mods get `OnScreenEntered` after `Enter`, and dialogs shown later stack on top.
  A hint shown straight from the hook would therefore cover the engine's warnings.
- **Change.** New core `ScAgentsPackageHint` (`src/ScCsgoKnives/World/ScAgentsPackageHint.cs`). It applies only to
  Lite (`ScOptionalAgents.Split`) without a running agents package.
  - Once per game start, `[CS_AGENTS]` is written to the log.
  - Unless muted, it waits on the main menu until no dialog has been open for 30 frames in a row. Any dialog restarts
    the count. Then it opens `ScAgentsPackageHintDialog`.
  - The dialog says what is missing: not installed, or installed but not running.
  - It shows a `LinkWidget` https://files.zh667.cn, which opens the browser through the engine's `WebBrowserManager`.
  - Buttons: "知道了" / "不再提示". Muting is kept in `ModsManager.Configs`.
  - Nothing is disabled. Full never asks.
- **Checks** (main suites, `WeaponHelpLayoutRegression`):
  - `agents-package-hint-lite-only`;
  - `agents-package-hint-after-engine-dialogs`: never opens over a dialog or off the main menu, opens after the quiet
    wait, and a dialog restarts the wait;
  - `agents-package-hint-link/{false,true}`: link URL/text, buttons and wording.

## 3. Mod settings

- **Hints shortened.**
  - Long notes removed: throw controls, the bindings list, the button-mode status line, the kill feedback note, the
    crosshair visibility rules, the enemy density table.
  - Kept as one short line each: view recovery (current values), grenade-preview circles, simple materials, the
    fire-button-only mode, left/right layout, the crosshair parameter scope, the enemy-squad context and that switching
    off keeps existing squads.
  - The "探员语音设置" button sat under the 视角恢复 heading. It now has its own heading, and the grenade preview has one
    too (投掷物).
- **Creative preview merged.** The separate "创造模式：显示命中方向预览" switch is gone.
  - `ScDamageIndicator` gates the creative preview on `ScUiSettings.DamageIndicator`.
  - The page notes "创造模式下被击中也会显示（较淡）".
  - The old file key `CreativeHitPreviewEnabled` is no longer read or written. Older builds reading a new file default
    it to on.
- **Layout row.** It reads "按键位置布局" (button "编辑按键布局").
- **Armor HUD switch moved.**
  - The settings page no longer has the switch, and its copy no longer carries the value, so saving the page cannot undo
    an editor change.
  - The layout editor previously had only the armor HUD's position, with the title saying "显示开关在模组设置". It now
    shows "显示护甲 HUD" whenever the armor HUD is selected.
  - The switch edits the editor's copy; Save writes `ArmorHudEnabled`, with rollback on a failed write.
  - "恢复此键"/"全部默认" show it again.
  - The list marks it "（已隐藏）", and the preview dims it like a switched-off button.
- **Checks** (`WeaponHelpLayoutRegression`):
  - `layout-armor-hud-visibility-switch/*`;
  - `layout-armor-hud-switch-edits-a-copy/*`;
  - `settings-armor-and-creative-switches-moved`;
  - `settings-plain-layout-row-and-short-hints/*`: "按键位置布局" present; no "弹药 HUD", "护甲" or old creative label;
    the creative note present; every text ≤ 40 characters at 850×479 and 360×640.
  - Tactical AI suite `damage-direction-hurt-creative-preview-and-zero-damage-reasons` now switches `DamageIndicator`.

## Follow-up (r12c, 2026-10-01): link caption and release log trim

> 这个链接得说明一下是什么，不然玩家都不知道，然后现在一些日志相关代码只保留必要的，因为要准备发布1.4.0了

- **Link caption.** The agents hint now says what the link is: "探员包下载页：点击下面的链接用浏览器打开，下载与轻量包同版本
  （1.4.0）的探员包。" The version comes from the core's `modInfo`. Check `agents-package-hint-link/*` requires the caption.
- **Logging.** An ordinary Game.log keeps:
  - warnings and errors (all of them mark failures);
  - once per start: version/initialisation, the multiplayer decision, one memory residency summary;
  - once per world load: compatibility, the gun registry summary, spawn rules, T/CT appearance;
  - rare data or decision records: migrations and conversions, recovery grants, real duplicates, workbench refusals,
    counter rule changes, multiplayer handshakes and refusals.
- **New switch: `KnifeLog.Diagnostics`.** It is on only with `SCCS_DIAGNOSTICS=1`, which the test harness `m0.py` sets
  for every game it launches. PackageCheck spawn tests and `TacticalPerformanceCheck` set it themselves. Behind it:
  - the 10-second tactical performance timings (no session, counters or reports in players' games);
  - the periodic, background and resume memory lines and the resource decode timings;
  - the 2-minute spawn status and squad lines;
  - per-shot free-view lines (no per-shot text built);
  - network per-operation and status lines, terrain repairs;
  - workbench success lines, the storage audit, companion/armor maintenance.
- **Deleted.**
  - All 70 `KnifeLog.Trace` calls and the `Trace` method. They compiled only under `SC_CSGO_DIAGNOSTICS`, which no build
    defines.
  - The two methods compiled under the same symbol: `LogPellet`, `LogComposition`.
  - The scaffolding that only fed traces:
    - `LogActionStart`, which still sampled three poses per weapon action;
    - per-frame CS2 skinning timers;
    - "already logged" flags; `LogFirstDraw`; bounds and format helpers;
    - the composition-log reset.
  - The grenade event lines, the inspect-input line, the armor death line, the C4 block line, the NEO bone-buffer line
    and the view-recovery "verified" line.
- **Warning change.** The skin-texture warning is now once per gun and paint.
- **Checks.**
  - Removal used a parser aware of C# strings. Three trace calls were brace-less `if` bodies; they were removed together
    with their side-effect-free `if`. Four inline cases were hand-edited.
  - Analyzers IDE0051/0052/0059 show no new unused members except those removed.
  - Agents suite `enemy/natural-squad…` blocks: with the switch off a squad still appears and no spawn status or squad
    line is written.

## Results

- **r12a** (pipeline `r12-pipeline-r12a-all-01` = job `39d1972a23ca4ca49c13b66e16c3123f`): `failed steps: ['throw']`.
  - Passing:
    - main-full 0/14034, main-lite 0/14344 (all new settings, editor and hint checks included);
    - ai-full and ai-lite 0/66;
    - vf 0/292, c4 0/73;
    - tactical-full-suite: only the 4 known failures;
    - identity stamped in every core and agents DLL.
  - The r11b baseline fails exactly the changed tests:
    - ai: the creative-preview test now switches `DamageIndicator`;
    - main: `weapon-help-layout/setup-or-layout`, because the baseline lacks the new fields.
  - `throw` failures, all in the new aim check:
    - AK-47 and AWP: no weapon in the harness (OBJ pieces, see above).
    - Desert Eagle (CT and T alike) with r12a's fixed sign:

      | Pitch | Barrel | Face | Muzzle | Front dot | Side axis rise |
      |---|---|---|---|---|---|
      | +0.6 | +0.589 | +0.465 | +0.397 m | 0.992 | |
      | −0.6 | −0.589 | −0.469 | −0.431 m | 0.993 | |
      | +1.2 | +1.158 | +0.918 | | 0.899 (fails) | −0.11 |

      At +1.2 the front dot fell to 0.899, which is the leaning "up" fixed in r12b. The side-rise metric also mixed cant
      with pitch; it now measures cant about the barrel.
  - Not delivered.
- **r12b** (pipeline `r12-pipeline-r12b-all-01` = job `59e3b6c6fdad47ca9c5dcd86f587c11b`): `failed steps: []`.
  - main-full 0/14034, main-lite 0/14344, including the 15 round-12 checks per edition (settings, layout editor,
    agents hint).
  - ai-full and ai-lite 0/66; vf 0/292; c4 0/73.
  - tactical-full-suite: the 4 known failures; `aim-pitch-lift-axis-lifts-the-front` passes.
  - identity `cb6cf0e8…` stamped in every core and agents DLL.
  - `throw`: 0 failures in 400 cases. The aim check on the packaged CT and T actors (identical numbers):

    | Gun | Pitch | Barrel | Face | Cant | Front dot | Muzzle |
    |---|---|---|---|---|---|---|
    | M4A4 | +0.6 | +0.600 | +0.468 | 0 | 1.000 | +0.46 m |
    | M4A4 | −0.6 | −0.600 | −0.462 | 0 | 1.000 | −0.50 m |
    | M4A4 | +1.2 | +1.200 | +0.934 | 0 | 1.000 | +0.73 m |
    | FAMAS | +0.6 / −0.6 / +1.2 | ±0.600 / 1.200 | 0.468 / −0.462 / 0.934 | 0 | 1.000 | |
    | Desert Eagle | +0.6 / −0.6 / +1.2 | ±0.600 / 1.200 | 0.476 / −0.474 / 0.951 | 0 | 1.000 | |

- **Delivered** by move, one copy (`r12-deliver-r12b-01` = job `0298d13a5dff41c3a56cc499f4ac9324`):

  | Package | SHA-256 | Size (B) |
  |---|---|---|
  | 全量 | `0817586ebcf58ef98036d6fbc69361c9171425e96b78eb7f218b0be21d367a38` | 526,871,369 |
  | 轻量 | `78af410c73cfb4124637c0f006abc2c21cf9d98c6e4b985275d69f4726d80d28` | 35,716,433 |
  | 探员 | `8ae86da6cf7b5240be685f7421a9732f8c68b66ae00ecf677118ed7967d6774b` | 38,465,869 |

  Files changed against r11b (nothing added or removed):
  - 全量: `ScCsgoKnives.dll`, `ScCsgoTactical.dll`;
  - 轻量: `ScCsgoKnives.dll`;
  - 探员: `ScCsgoTactical.dll`.

  The manifest is updated, and `tools/completion_140.py` BASELINES = r12b.
- **r12c** (pipeline `r12-pipeline-r12c-all-01` = job `2cd25f84b18a4b5c928f4c433d4af649`): `failed steps: []`.
  - main-full 0/14034, main-lite 0/14344, including the link caption.
  - ai 0/66 both editions; vf 0/292; c4 0/73.
  - tactical: the 4 known failures; `throw` 0 failures; identity `10728ed4…` in every core and agents DLL.
  - Baseline runs (old r12b packages): main fails exactly the two link-caption checks.
  - The baseline agents AI suite crashed: the test's setup read the new `KnifeLog.Diagnostics` field, which the old core
    lacks, so a NullReferenceException escaped before any test ran. The setup now tolerates a missing switch.
    - Test-only rerun `r12-retest-r12c-01` = job `d4e11e064c6f45d78eb3ffff417c00f7` on the same candidates: ai 66/66
      both, vf 292/292, tactical the 4 known failures.
- **Delivered** r12c (`r12-deliver-r12c-01` = job `8b862774f9f5491eaf08d0f624a7fc41`):

  | Package | SHA-256 | Size (B) |
  |---|---|---|
  | 全量 | `7d11e59c7884619eaccd0f2d2ce0cb7237549fea62ef9183f323901d26c9d47b` | 526,868,667 |
  | 轻量 | `05947864b94366266132193e2eb14c070ea1cdd2aabeb64a4574243e32b641a5` | 35,713,998 |
  | 探员 | `5eab74d46dabb9e136043702ae00b42605fd1d9c5104cb3c9a3449573d6c1a37` | 38,465,708 |

  Files changed against r12b (nothing added or removed):
  - 全量: `Integrations/ScCsgoAppearance.bin`, `Net/ScCsgoNet.bin`, `ScCsgoKnives.dll`, `ScCsgoTactical.dll`;
  - 轻量: `Net/ScCsgoNet.bin`, `ScCsgoKnives.dll`;
  - 探员: `Integrations/ScCsgoAppearance.bin`, `ScCsgoTactical.dll`.

  BASELINES = r12c. Multiplayer was not rerun: the adapter change is logging only.
- **Cleanup** r12c (`r12-cleanup-r12c-01`): the build trees, 1.89 GB. Receipt
  `round12-aim-sign-agents-hint-settings-20261001-cleanup-r12c.json`.
- **Cleanup** (`r12-cleanup-r12b-01` = job `f9307d89…`) removed 4.39 GB; output hashes were unchanged and E: has 12.9 % free.
  Receipt: `round12-aim-sign-agents-hint-settings-20261001-cleanup.json`. Removed:
  - the r12b build trees;
  - the r12a build trees and its undelivered candidates.

## Not verified by the agent (user tests in game, by request)

- In-game look of the aim with the CS T/CT appearance in every third-person view: looking up should raise the gun and
  the head.
- The agents hint on a real main menu: Lite only, with and without an engine disabled-mods dialog. Opening the link in
  the browser (desktop and Android). "不再提示".
- The settings page and the layout editor's armor HUD switch on screen.
- The link caption on screen; an ordinary Game.log after a normal session (expected: no periodic [CS_MEM]/[CS_PERF]/[CS_SPAWN] status, no [CS_SHOT]).
