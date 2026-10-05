## ADDED Requirements

### Requirement: R1 Exactly one death transition per life
服务端 SHALL 在原版死亡掉落/实体销毁之前接管竞技致命结果，每个 LifeId 只能终结一次；死亡镜头期间玩家不能攻击、承伤、阻挡战斗或操作库存。

#### Scenario: Simultaneous lethal hits
- **WHEN** 同一帧多个弹丸或不同攻击者造成致命伤害
- **THEN** 只生成一次死亡/计分事件，清除未完成攻击且不触发原版背包掉落

#### Scenario: Non-bullet death
- **WHEN** 玩家被刀、Zeus、爆炸、火、坠落、溺水或越界杀死
- **THEN** 同样进入唯一死亡链；无可靠攻击者的只记环境死，不猜最近玩家

### Requirement: R2 Safe selection from authored spawn candidates
随机复活 SHALL 限于房主三维区域中的候选点，服务端先检查硬安全条件，再依据敌人距离/视线/近期使用择点；并发复活要预留位置。

#### Scenario: Everyone can see the candidates
- **WHEN** 所有物理安全点都被敌人看见或距离偏近
- **THEN** 可按记录的策略放宽视线/距离评分，仍不允许墙内、悬空、火区、未加载或界外点

#### Scenario: No physically valid spawn
- **WHEN** 所有点都被堵塞、危险或未加载
- **THEN** 保持待复活/观战并有界重试、提示房主，不传送到随意世界坐标

#### Scenario: Spawn becomes invalid while preparing
- **WHEN** 两人同时复活或已预留点新出现实体/火区
- **THEN** 提交前再次验证并分配不同合法位置，不叠人、不无条件复用旧选择

### Requirement: R3 Death-view preparation and coherent respawn commit
系统 SHALL 在死亡镜头期间准备分离的配装状态，完成后以统一版本提交位置/朝向/速度/血甲/库存/当前武器/生命标识；客户端完整就绪后才允许操作。

#### Scenario: Inventory and position arrive separately
- **WHEN** 网络分批到达或复活请求重试
- **THEN** 客户端不呈现可操作的半套复活状态，不重复发装备，旧命消息不作用于新生命

#### Scenario: Client never becomes ready
- **WHEN** 地形/资源或快照确认在有界期限内未完成
- **THEN** 玩家保持不可战斗且不挡枪并转为观战/故障提示，不能无限占据无敌角色

### Requirement: R4 Three-second authority-controlled protection
复活保护 SHALL 从服务端提交可操作生命时开始持续 3 秒，客户端对齐显示；接受开火/刀击/投掷时 SHALL 先结束保护再执行攻击。保护不会因选装、菜单或重试延长。

#### Scenario: Attack while protected
- **WHEN** 服务端接受保护中的玩家一次有效攻击动作
- **THEN** 同一权威顺序内先清保护，再执行动作；不能无敌输出

#### Scenario: Waiting for resources or reopening menus
- **WHEN** 玩家等待加载，或在保护内反复打开轮盘/重新确认配装
- **THEN** 加载等待不计入已可操作保护窗口，配装不刷新同一生命截止时刻

### Requirement: R5 Verified death and protection presentation
死亡镜头、复活音效及保护外观 SHALL 有可追溯资源或明确重建说明，双方能识别保护结束；没有可定位来源的“白色效果”不得被宣称为已复用 CS2 原效果。

#### Scenario: Unknown model or effect degradation
- **WHEN** 可选外观不能直接使用保护材质，或低性能设备降低装饰质量
- **THEN** 仍提供可辨认且时序正确的保护标记，不永久改色、不隐藏竞技信息、不改变保护规则
