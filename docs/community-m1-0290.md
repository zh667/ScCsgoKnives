# 0.29.0 社区反馈 M1：灰烟、投后第三格、投掷力度、落稳起烟、全枪 ×1.5、刀距 2.2/1.8

日期：2026-09-07。对应 `docs/community-feedback-plan-2026-09-07.md` 的 M1 批次（F01/F02/F03/F04/F13/F14），
以及交接文档里"PackageCheck 在 VPS 跑不通"的无头化。这版是可实机测试的第一版；数值允许按实测再调，
调参入口全部集中在下文列出的文件。

## 改动

| 项 | 文件 | 0.28.x | 0.29.0 | 依据 |
|---|---|---|---|---|
| F13 全枪伤害 | `World/ScSurvivalBalance.cs` | 每枪一张表 | `GunPowerMultiplier = 1.5`，`Power = BasePower × 1.5`，只乘一次 | 用户确认；表值不变，AK-47 10→15、AWP 38→57、电击枪 18→27、手枪 7→10.5 |
| F14 刀距 | `World/ScKnifeStrike.cs` | 轻 1.6 / 重 1.3 | 轻 2.2 / 重 1.8 | 用户确认；命中仍在挥击落点时刻（`TakeHit`）从相机原点做射线 |
| F01 灰烟 | `World/ScGrenadeVisuals.cs`、`SubsystemScGrenades.cs` | 粒子 `(L, L+3, L+5)` 偏蓝，烟内遮罩 `(125,130,133)` | 粒子 `(L, L, L)`，遮罩 `(128,128,128)`；明暗层次保留在亮度 L 里 | 图集 `grenade_smoke_atlas.png` 已核对：RGB 全白、只带 alpha，色偏只来自 tint |
| F03 投掷 | 新文件 `World/ScGrenadeBallistics.cs` | 强 14 / 弱 6 / 上抬 .18 / .08 / 玩家速度 ×0.5 | 强 **20** / 弱 **10** / 上抬不变 / 玩家速度 **×1.25**，出手瞬间采样一次 | 强投 20 为估计（用户反馈"慢"）；弱投 = 强投 × 0.5、×1.25 取自 CS:GO SDK `CBaseCSGrenade::ThrowGrenade` |
| F03 时间步 | `SubsystemScGrenades.Update` | 物理每帧最多推进 0.5 s，引信/年龄减完整 dt | 物理、年龄、引信同用 `ScGrenadeBallistics.Step(dt)`（≤0.5 s） | 规划要求；卡顿时不会"飞得少却提前起烟" |
| F04 落稳起烟 | `ScGrenadeState.Rested`、`Move`、`Update` | 引信到点后：着地即起烟，或空中满 4 s 强制起烟 | 引信到点后需 `Grounded && Rested ≥ 0.15 s`；再次腾空 `Rested` 清零；12 s 仍未落稳才原地起烟并写一条 `KnifeLog.Warning` | 0.15 s 为估计；4 s 空爆路径已删除；`Rested` 随存档保存/读取，负值或非有限值判为坏档 |
| F02 投后第三格 | `ScGrenadeBallistics.FollowUpSlot`、`SubsystemScGrenades.Update` | 回"上次持枪/刀的槽" | 第三格（索引 2）有物品就切过去；第三格空则回仍有效的上次武器槽；都不行留在原槽 | 规划 F02；未投出、取消、生成失败、达到上限、手动切槽/开界面/改背包时不切（这些路径在切槽前已 `Cancel`） |

只改烟雾和诱饵的起效条件；燃烧弹仍是着地即燃、落水熄灭，高爆和闪光仍按引信起爆。

## 验证（VPS 无头）

- `tools/PackageCheck` 在 VPS 崩溃的原因是没有任何代码调用过 `Engine.Dispatcher.Initialize()`：
  回归测试里用 `GetUninitializedObject` 造出的引擎对象在终结器里往 Dispatcher 投递，抛
  "Dispatcher is not initialized" 把进程打死。现在 `Program.cs` 在解析参数后直接初始化 Dispatcher，
  不需要 `--no-gpu` 之类的开关，也没有跳过任何检查。
- `dotnet tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll --scmod output/ScCsgoKnives-0.29.0-Lite.scmod --sha256 396457b0… --vanilla-content …/Content.zip`
  → 6087 项全部通过（`docs/community-m1-0290-packagecheck.json`），27 s。0.28.3 包同法重跑也是 6080/6080。
- `python3 -X utf8 tools/cs2_runtime_selftest.py --scmod … --sha256 …` → 5850/5850。
- 新增自检：`gun-power-x1.5`、`knife-range-2.2-1.8`、`throw-speed-inherits-velocity`、`throw-step-clamped`、
  `smoke-settles-before-pop`、`after-throw-third-slot`（8 种第三格/上次武器组合）、`smoke-neutral-grey`；
  `animal-shot-targets` 的 AK-47 击杀发数从 7 改成 5。

包：`output/ScCsgoKnives-0.29.0-Lite.scmod`，111.8 MB，SHA-256 `396457b01a76cce7138b01133db5bb80c040af07b9c8f34f4533a862f61441e2`。
Full/Mini 在 Windows 侧打（同一 DLL）。

## 实机要看的点

1. 烟雾白天、夜间、烟内视角都不再偏蓝；仍有明暗层次。
2. 投后切槽：第三格放刀 / 枪 / 其他物品 / 空；最后一枚投完；同堆还有剩；第三格就是投掷槽。鼠标与触屏一致。
3. 站投、跑投、后退投、横移投的落点：跑投应明显更远，后退更近；弱投仍能近距离控制。强投 20 若过远，只改 `ScGrenadeBallistics.StrongSpeed`。
4. 高抛、撞墙反弹、滚下台阶、斜坡：烟雾不再在明显弹跳中起烟；落稳约 0.15 s 后起烟。
5. 存档时正在飞的烟雾弹，读档后继续飞而不是立即起烟。
6. 全枪伤害：AK-47 打 70 血动物 5 发；刀在 2.2 / 1.8 格边界内外的命中。

## 未做（属于后续批次）

爆头（M1b）、烟雾遮挡和受热/炸烟（M2）、第三人称与耐久（M3–M5）。规划文件 `community-feedback-plan-2026-09-07.md`
的勾选框没有动，避免与 Windows 侧同时编辑同一文件。
