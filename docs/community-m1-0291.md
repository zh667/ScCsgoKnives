# 0.29.1 投后切槽改为"回到上一次拿的物品那个槽"

日期：2026-09-07。用户看过 0.29.0 后把 F02 的规则改成：**投出投掷物之后回到上一个物品槽，即上一次拿的物品所在的槽位**，
不再固定第三格。

## 规则

- 每帧记录玩家的活动槽；一个槽只有连续激活 ≥ 0.3 s（估计）才算"拿过"，滚轮路过的中间槽、误按一下又切回来的槽都不算。
- 投掷收尾到点时，回到最近一次拿过、且不是投掷槽本身的槽，槽里现在是什么都回去；找不到这样的槽就留在原槽
  （比如读档时手里就是投掷物）。留在原槽且还有投掷物时播 deploy。
- 未投出、取消、生成失败、达到上限、手动切槽、开界面、改背包都不切，这些路径在切槽前已经取消了准备状态。
- 代码：`World/ScSlotHistory.cs`（纯逻辑，可自检）、`ScGrenadeBallistics.FollowUpSlot(slotsCount, thrownSlot, previousSlot)`、
  `SubsystemScGrenades.Update` 的收尾段。

## 新增日志（Game.log，方便实机对照）

- `grenade prepare: grenade_smokegrenade slot 3 previous slot 0 low=False button=False`
- `grenade release: grenade_smokegrenade speed 20.0 (player 0.0) low=False at …`
- `grenade follow-up: thrown slot 3 previous slot 0 -> slot 0 holding=True`
- `grenade kind 2 pops: age 2.31 s rested 0.16 s at …`
- 异常兜底仍是 `WARN grenade kind 2 never settled within 12 s …`

## 验证

- PackageCheck（VPS 无头，含 Content.zip）：6088/6088，`docs/community-m1-0291-packagecheck.json`。
- 运行时自检：5851/5851。新增 `after-throw-previous-slot`、`slot-history-dwell`（滚轮路过、误按回切、读档持雷四种情形）。
- 包：`output/ScCsgoKnives-0.29.1-Lite.scmod`，111.8 MB，SHA-256 `5a2e42bda30794a0e126877a5e8d7304df72c17f749ff06fc656d7d5cbd480d7`。

## 实机要看

刀 → 雷 → 投：回刀。枪 → 滚轮滚过两格到雷 → 投：回枪。雷（拿了一会）→ 误按到别的格又立刻切回雷 → 投：回雷之前拿的东西。
同堆还有剩：也回上一个槽（用户要求的是槽，不是"继续拿雷"）。读档时手里是雷：投完留在原槽。
