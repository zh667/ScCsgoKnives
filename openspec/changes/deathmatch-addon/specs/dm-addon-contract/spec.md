## ADDED Requirements

### Requirement: N1 Optional addon with scoped core contracts
死亡竞赛 SHALL 作为独立包使用通用核心接口，Full 与 Lite 组合均可工作，探员包可选；不复制核心、不改第三方 APK，不因包已安装就启用竞技。

#### Scenario: Missing optional actors
- **WHEN** Lite + DM 未安装 Agents
- **THEN** 原版角色仍可参加完整竞技流程，缺失外观能力明确处理，不因引用探员 DLL 导致加载失败

#### Scenario: World disposed and reloaded
- **WHEN** 世界反复加载/退出或附属重复注册
- **THEN** 规则/HUD/事件只有一份有效注册，退出释放作用域，不污染下一世界

### Requirement: N2 Negotiated gameplay capabilities
握手 SHALL 校验活动模式需要的附属协议/构建与战斗配置指纹，网络结构变化需明确协商；消息号在完整生产注册表中唯一。

#### Scenario: Incompatible required addon
- **WHEN** 竞技房间一端缺少 DM 或使用不兼容战斗配置/协议
- **THEN** 在授予竞技输入权限前明确拒绝参赛并说明缺项，不让双方按不同结构解析

#### Scenario: Cosmetic layout differences
- **WHEN** 两端只在本地 HUD 位置/大小或非必需能力上不同
- **THEN** 不因此误判玩法版本不兼容

### Requirement: N3 Session and life fenced transactions
客户端命令 SHALL 绑定连接验证身份、会话/局/生命/请求版本；库存和复活提交 SHALL 有完整版本屏障与有界重试，不假设消息有序等于同帧生效。

#### Scenario: Forged identity or old life request
- **WHEN** 客户端伪造其他玩家标识，或旧生命购买/射击消息迟到
- **THEN** 服务端拒绝，不影响当前武器、生命与分数

#### Scenario: Partial snapshot
- **WHEN** 记录、槽位、位置或血甲只到达部分
- **THEN** 客户端等待完整提交或请求快照，不以半状态进入可操作比赛

### Requirement: N4 Bidirectional official save compatibility
从正式 1.4.0 起，所有正式核心发行版本 SHALL 能直接双向加载/保存彼此世界，不因 DM 未知载荷丢数据或拒载；未知模式在安全休眠状态保留，恢复支持后可继续使用。保存兼容不代表网络混版本兼容。

#### Scenario: Old core or addon absent
- **WHEN** 包含竞技配置/军械库/外观偏好的世界在支持范围内旧核心或无 DM 环境打开、保存，再换回
- **THEN** 世界可加载且比赛不错误运行，未知载荷/引用完整保留，换回后恢复；不让竞技装备流入普通生存

#### Scenario: Historical package substitution
- **WHEN** 发布前执行新旧双向兼容矩阵
- **THEN** 使用真实正式包与哈希，不重编旧版/偷换同名文件作为通过证据；1.4.0 前版本不属于本强制矩阵

### Requirement: N5 Crash recovery without resetting player data
世界保存 SHALL 协调竞技状态/租约/库存，崩溃恢复不重复装备或得分；不得重置永久记录或自动生成玩家世界备份来规避恢复设计。

#### Scenario: Shutdown between prepare and commit
- **WHEN** 停服发生于借枪/复活/结算的任一边界
- **THEN** 下次进入安全 Lobby/恢复状态，保留最后确认数据，不重放攻击/奖励或双份装备

### Requirement: N6 Evidence distinguishes platform and gameplay readiness
验收 SHALL 分开记录真实 Windows/Android 构建、离线结果和实机结果；已知 Android 临时路径故障仅阻塞对应端到端场景，不授权平台修补或以 Windows 结果代替 Android。

#### Scenario: Provided Android build cannot join
- **WHEN** `TempNetworkWorld` 故障仍存在
- **THEN** 将 Android 客户端加入标为外部阻塞并保留证据，独立任务可继续，不能宣布 Android 全部通过
