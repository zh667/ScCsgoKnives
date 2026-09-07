# M4 耐久专项 · 0.36.1 交付记录（对 ada0fc9 审查四项的修正 + D3 持有者身份 + T13）

日期：2026-09-08。状态：**M4 实现中，D1/D2 修正完成，D3 持有者覆盖已换用引擎扫描器，T13 无头部分完成；实机验收未做，其余批次 BLOCKED。**

## 审查项

1. **[P1] 复制隔离**：`ScGunMutation.Commit` 每次都向 `HolderLocator` 询问"这条记录此刻是否还有其他持有者"，不再依赖记录上的 `Holder`（谁最后用过它）；Prepare 的判断只作提示，Commit 现场重判。只要另有持有者，当前这把就先拿到自己的记录再改；先用原枪、读档后先用副本两种顺序都不再共享（自检 `m4-t04` 四段断言）。`SplitDuplicates` 改为对每个多余持有者走同一事务（`Commit(_ => {})` 触发克隆），不再直接 `Clone`+删加物品。
2. **[P1] 新枪登记重入**：Commit 期间设全局 `s_committing`，任何嵌套的枪械提交（不论是否同一记录）返回 `Busy`，"看 ID→改库存→发布"之间不可能插入另一次发布；若仍出现发布 ID 与候选不符，事务失败：撤回替换的物品、退回弹药/材料、把发布出的记录标为 abandoned（保留水位，不复用）。自检 `m4-nested-registration-refused`：在 `AddSlotItems` 回调里嵌套登记另一把新枪 → 内层 `Busy`、外层成功且物品指向自己的记录。
3. **[P1] 扣料异常回滚**：事务保留撤销日志（材料逐槽、弹药、被换下的枪），返回失败和抛异常统一走 `Rollback`；恢复优先原槽，原槽拒收则放入任一可容纳的格；仍放不回的记入 `ScGunMutation.PendingRestore` 并写错误日志，提示改为"有物品未能放回，请查看日志"。自检 `m4-rollback-on-faults`：第二种材料扣除抛异常 → 第一种材料回来；格拒收新枪值 → 枪与弹药回来；全部格拒收 → 记入 PendingRestore 并如实报告。
4. **[P2] 严格解析**：Schema 1 记录必须恰好 7 个字段、逐一解析且在范围内（型号、弹量≤弹匣、消音器 0/1、耐久≤上限、修订号≥0、充能剩余 -1 或 ≤1e6），否则整条隔离为原文，不补默认值（自检 `m4-strict-record-parse`，含审查用的 `0,30,0,1500,badMax,badRevision,badCharge`）。

## D3 持有者身份与覆盖

- 改用原版 `SubsystemItemsScanner.ScanItems()`：子系统库存、全部实体的 `IInventory` 组件（玩家、创造可写格、原版箱、Stash 的 `ComponentStashChest` 等）、掉落物、投射物、移动方块；结果立即复制。
- 持有者键 = 容器对象身份 + 槽位（`类型#哈希:槽`），掉落物各自不同键；同键去重。创造目录格（≥ OpenSlotsCount）不算持有。
- 每帧最多扫描一次，任何发布了新 ID 的提交后失效重扫。
- UI 拖拽：原版拖拽只记源库存和格号，物品仍在源格，不会成为第二个持有者。
- 未实测：Stash 无线终端/批量移动/排序期间"先删后加"是否被审计误判（审计只在两处同时存在时才拆分，删后加的瞬间只剩一处，理论上不误判）。

## T13

- `ScGunRegistry.Save(now)` 在游戏线程生成纯字符串快照，后台写文件只能拿到快照；自检 `m4-t13-save-snapshots`：取快照后继续开枪换弹再取第二份，第一份不变，两份分别重读得到各自状态，内存状态与最新一致。
- 引擎 `SaveProject` 先同步 `Project.Save()` 再后台写文件；保存完成事件成功失败都触发、`OnProjectXmlSaved` 在写文件前——本模组不把它们当写盘成功证据，也不在这里做任何清理。注入写盘失败属于引擎层，无头无法验证，留给实机。

## 验证

PackageCheck（含 Content.zip）6171/6171，运行时自检 5900/5900（`m4-0361-packagecheck.json`）。VPS 不打包。新增自检：`m4-t04`（重写）、`m4-nested-registration-refused`、`m4-rollback-on-faults`、`m4-strict-record-parse`、`m4-t13-save-snapshots`、`m4-holder-keys-unique`。

## 仍未完成

实机（Windows 生存新世界两轮保存重进、原版箱/Stash/掉落/维修/两把电击枪；手机 HUD/维修/转移）；Stash 终端与批量操作实测；引擎写盘失败注入。ID 上限 1022 不回收。
