## ADDED Requirements

### Requirement: L1 Free layered catalogue and open cosmetics
系统 SHALL 免费提供竞技支持目录，通过 CS 风格轮盘分类/型号选择及外观详情分层操作；原皮、皮肤、计数器及带计数器皮肤不作为四份独立商品，刀型/涂装无需生存拥有条件。

#### Scenario: Choosing a skinned counter weapon
- **WHEN** 玩家选择某型号、一个适配皮肤并开启支持的计数器
- **THEN** 提交一个带这些属性的配装项，不扫描生存背包、不扣材料、不增加性能

#### Scenario: Catalogue exceeds one screen
- **WHEN** 手机浏览所有型号或大量皮肤
- **THEN** 可用轮盘分层、分页/滚动/筛选完成选择，不要求缩小到无法点击的一页

### Requirement: L2 Explicit initial loadout and phase-dependent changes
首次入场 SHALL 让玩家自行配装，不强发默认四格装备；随时可打开面板，存活交战时的修改排入下一命，准备/死亡阶段可设置，保护期内服务端批准后才立即应用。

#### Scenario: No weapon selected yet
- **WHEN** 新玩家进入准备页，尚未确认任何配装
- **THEN** 不自动塞入默认手枪/刀，保持准备状态或按明确空装确认进入，不影响其他人计时

#### Scenario: Protection expires while menu is open
- **WHEN** 玩家保护期打开轮盘，服务端处理确认时保护已结束
- **THEN** 更新下一命计划，不立即换成满弹武器，UI 告知生效时机

#### Scenario: Repeated same selection
- **WHEN** 同一购买请求重复到达，或保护期内重复选择同一装备
- **THEN** 不复制武器、不刷新保护时间、不绕过投掷物每命数量上限

### Requirement: L3 Complete loadout restoration
复活 SHALL 恢复服务端冻结的完整批准配装与选中武器，以新生命规则恢复弹量/血甲/可用数量；不得恢复旧命的未完成攻击、换弹或预测。

#### Scenario: Selection races with respawn
- **WHEN** 配装正在准备提交时又收到新版本选择
- **THEN** 本次提交只使用一个完整版本，后来请求按合法窗口处理，不混用两套主副武器和外观

#### Scenario: Capacity or validation failure
- **WHEN** 记录配额、槽位或外观校验失败
- **THEN** 保留旧批准状态或待复活，不出现半套装备、额外扣除或静默替换

### Requirement: L4 Fixed-identity bounded armoury
竞技枪池 SHALL 保持永久 RecordId/Variant 不变并以租约转移同一实例；Zeus SHALL 纳入有记录武器管理。记录增长 SHALL 由支持型号数、并发参赛人数及有界准备容量决定，不能与死亡次数成正比。

#### Scenario: Many lives and all weapon types
- **WHEN** 玩家反复死亡、换型号、换皮肤、重连并完成多局
- **THEN** 重复型号复用已有可借实例，不重写型号/回收身份；记录数量符合声明上界

#### Scenario: Late action from a previous lease
- **WHEN** 枪归还后再次借出，旧 LifeId/LeaseGeneration 的射击或换弹消息到达
- **THEN** 消息被丢弃，不改变新借用者的弹量、耐久或外观

#### Scenario: Save and load with parked weapons
- **WHEN** 未借出的枪在军械库中，部分枪在活动库存中，世界保存并重载
- **THEN** 每把枪只有一个实际持有者，完整性扫描不将账本副本当复制枪；损坏租约保留原文且不猜测恢复

### Requirement: L5 Competitive counters have separate state
竞技计数器 SHALL 来源于本局该玩家该型号的确认击杀，与生存 KillCount/成长分离；复活/换皮肤保留本局次数，下局重置。

#### Scenario: Reborrow another physical record
- **WHEN** 同一玩家同一型号取得另一条池记录，或另一个玩家借到上一把枪
- **THEN** 显示各自竞技统计，不转移前一持有者计数，不触发生存升级
