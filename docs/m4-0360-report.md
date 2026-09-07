# M4 耐久专项 · 0.36.0（D0–D2 + D3 部分）交付记录

日期：2026-09-08。基线 `0910397`，按 `docs/vps-m4-durability-plan-2026-09-08.md` 只推进 M4。其余批次 BLOCKED，本版没有碰烟雾、第三人称、爆头、开镜以外的任何功能；第三人称/HUD 只改了状态读取。

## D0 复现与调用方清单

| 复现 | 0.35.4 基线 | 0.36.0 |
|---|---|---|
| 表满：新空 AK 付费换弹 | 返回成功、弹匣 2→1、枪内仍 0（`SetRounds` 返回同值当成功） | `RegistryFull` 明确失败，弹匣不扣，枪保持新枪（自检 `m4-t05`） |
| 复制共享：同值枪两处持有 | 只有 0.5 s 轮询扫玩家背包 | 首次使用前 `HolderLocator` 查另一持有者 → 当场克隆记录；审计覆盖背包、已加载方块实体库存、掉落物（`m4-t04`） |
| 电击枪充能共享 | 充能时间存在玩家 `GunState`，按玩家保存 | 存在枪记录 `RechargeReadyAt`，存档保存剩余秒数（`m4-t10`） |

写入入口（全部走 `ScGunMutation`）：开火 `SubsystemScGunBlockBehavior.Fire`、换弹 `ScReloadTransaction.Write`（整匣/逐发）、消音器提交（BusyUntil 到点）、电击枪充能开始/恢复、维修 `ScWeaponRepair.TryRepair`、复制拆分 `SplitDuplicates`（`Clone`）。
只读入口：`GunSpec.TryGetSnapshot/GetRounds/GetSilencerOff/GetDurability/GetMaxDurability/IsUsable`（HUD、描述、第一/第三人称、动画控制器、维修候选、装配台）。
新枪创建：`GunSpec.MakeData`（创造栏、装配、开局赠枪、开箱 `block+data` 配置）——满/空弹匣不占 ID，只有部分弹量才建记录。
持有者：玩家背包（含创造可写格）、`SubsystemBlockEntities` 中所有带 `IInventory` 组件的方块实体（原版箱子；Stash 若走方块实体也在内，未单独实测）、`SubsystemPickables` 掉落物。**未覆盖**：投射物/移动方块携带的物品、转移中的 UI 暂存、非方块实体容器。

## D1 状态模型

- `ScGunRecord`：Variant/Rounds/SilencerOff/Durability/MaxDurability/Revision/RechargeReadyAt(+Holder 暂态)；外部只拿 `ScGunSnapshot` 只读副本。
- `ScGunRegistry`：`Schema=1` 写入存档；未知 schema 原样保留并停用枪械；坏记录（型号越界、弹量>弹匣、耐久>上限、非法字段）隔离为原文、原样写回、不可用、不重建；`Next` 水位取 max(保存值, 最大已见 ID+1)；`PeekNextId/Publish` 不预留、失败不占号。
- `ScGunMutation.Prepare/Commit`：只读快照（库存修订号、格值、记录修订号、持有者）→ 提交时复核 → 预检弹药/材料/额度 → 扣料 → 替换格（仅在需要新 ID 时改物品值，且是完整 block value）→ 发布/改写记录 → 修订号+1；任一步失败精确回滚，返回 `Success/Foreign/MissingRecord/StateChanged/InsufficientMaterials/RegistryFull/InventoryRejected/Invalid/DuplicateUnresolved/Busy`。重入同一记录返回 `Busy`。
- `GunSpec` 不再有带副作用的 Set*。

## D2 接入

- 开火：事务成功后才设射速/连发状态、播动画、出弹丸；失败只提示（2 s 限流）。霰弹每次扣扳机 1 点；创造不磨损；最后 1 点可打出后损坏（`m4-t09`）。
- 换弹：预检弹药 → 事务；被拒绝的付费换弹不填弹、不扣弹匣；记录在准备后被改动 → `StateChanged`（`m4-t07`）。
- 维修：报价冻结记录 ID/修订号/耐久/材料；点击复核，记录变化或同款枪交换 → `StateChanged` 作废报价；成功至多一次；缺料不扣（`m4-t08`）。
- 电击枪：按实例计时，存档存剩余秒数（本 API 的 `SubsystemTime.GameTime` 每次会话从 0 起，已核实不持久化），两把各自计时，维修不动时钟（`m4-t10`）。
- 布局：0.35.4 的显式戳优先保留；未知 schema 记录原样保留（`m4-t11`、`world-layout-status`）。

## 验收矩阵对照（无头）

T01 ✅ `m4-t01` · T02 ✅ `m4-t02` · T03 ◐ `m4-t03`（背包→箱子→掉落值→另一玩家→存读，用测试库存桩；原版箱/Stash 实机待验） · T04 ◐ `m4-t04`（首次使用前拆分；创造复制、箱子/掉落/两玩家的真实路径实机待验） · T05 ✅ `m4-t05` · T06 ✅ `m4-t06` · T07 ✅ `m4-t07`（缺料/格拒绝/记录变化/重复提交/重入） · T08 ✅ `m4-t08` · T09 ✅ `m4-t09` · T10 ✅ `m4-t10` · T11 ◐ `m4-t11`（注册表两轮往返；子系统真实 Save→Load 实机待验） · T12 ✅ `m4-t12` · T13 ✗（保存中转移/失败重试，未做） · T14 ◐（百分比文本 ✅；35 枪配置 ✅；HUD 实机待验） · T15 ✅ 开箱：`ScCsgoBox` 现有奖励池无枪械条目，其 `block+data` 配置若指向枪块会得到满弹新枪（不占 ID），无需改奖池。

PackageCheck（含 Content.zip）6166/6166，运行时自检 5895/5895（`m4-0360-packagecheck.json`）。VPS 不打包。

## 未解决

- D3：持有者清单里的未覆盖项（投射物/移动方块、转移暂存、非方块实体容器）；Stash 实测；创造复制的真实入口验证；T13 保存并发。
- ID 上限 1022、不回收（方案范围内）。
- 实机：Windows 生存新世界两轮保存重进、原版箱/Stash/掉落/维修/两把电击枪；手机 HUD/维修/转移。完成前状态为"实现完成，M4 待实机验收"。
