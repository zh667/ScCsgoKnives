## ADDED Requirements

### Requirement: A1 Dedicated world activation and survival isolation
系统 SHALL 仅在房主显式配置的专用竞技世界启用死亡竞赛；安装附属包不得修改普通生存规则、背包、成长、皮肤或存档。

#### Scenario: Installed addon in an ordinary world
- **WHEN** 玩家装着 DM 附属包进入普通生存世界或从竞技世界切回生存
- **THEN** 生存血甲/伤害/耐久/库存按原规则工作，竞技输入、HUD 和事件监听不残留

#### Scenario: Arena equipment attempts to escape
- **WHEN** 参赛者试图丢弃装备、放入箱子、拾取外来装备或携带竞技库存跨世界旅行
- **THEN** 服务端拒绝不允许的转移，保持原有效状态，不复制装备、不写入生存收藏

### Requirement: A2 Host-owned three-dimensional arena definition
房主 SHALL 能设置三维区域、准备/观战位置、候选复活点及朝向并随地图保存；开始比赛前 SHALL 验证至少两个不同合法点，普通参赛者不能更改房主规则。

#### Scenario: Invalid or unauthorized setup
- **WHEN** 点位在界外/墙内，或非房主客户端提交修改地图/局长请求
- **THEN** 不采纳无效设置，房主收到可定位点位的问题说明，当前有效配置保持不变

#### Scenario: Author edits during a match
- **WHEN** 房主在 Running 阶段修改时长或点位草稿
- **THEN** 草稿仅在后续合法阶段验证并生效，不瞬间改变当前比赛规则

### Requirement: A3 Free-for-all lifecycle and configurable duration
本版 SHALL 支持个人混战及房主局长设置，采用 Editing/Lobby/Countdown/Running/Results 生命周期；不得按 CT/T 外观组队，不包含机器人补位、奖励武器或团队胜利条件。

#### Scenario: Timer expires
- **WHEN** 服务端当前局截止时间到达
- **THEN** 停止该局得分并发布唯一结算，同分者并列，后续迟到攻击不会更改本局结果

#### Scenario: Too few players or everyone leaves
- **WHEN** 仅一人准备练习，或正式比赛所有玩家断开
- **THEN** 前者可练习但不伪造正式成绩，后者明确中止并保存原因，重连不重复结算

### Requirement: A4 Fair arena participation
系统 SHALL 对参赛者、准备者、观战者、编辑者分别控制攻击/伤害/碰撞/地形权限；参赛者使用竞技互伤规则，原版创造无敌与世界友伤开关不能绕过比赛契约。

#### Scenario: Preparing or spectating player
- **WHEN** 玩家尚在选装或观战，其他人正在交战
- **THEN** 该玩家不能攻击、挡枪或成为得分目标，不拖停其他人的比赛

#### Scenario: Terrain destruction or unsupported modifiers
- **WHEN** 竞技爆炸/建造动作试图修改地图，或已知第三方组合绕过竞技伤害控制
- **THEN** 比赛按地形保护规则处理；不兼容组合给出明确开赛限制，世界仍可安全加载，不擅改第三方文件
