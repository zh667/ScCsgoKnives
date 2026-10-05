## ADDED Requirements

### Requirement: U1 Equivalent desktop and touch controls
Windows SHALL 支持自定义购买/计分/切枪键；Android SHALL 提供可点击轮盘入口、榜单和装备切换，不依赖键盘或悬停。

#### Scenario: Touch buying and scoreboard
- **WHEN** 手机玩家只使用触屏进行选枪、外观设置、查看榜单和切换装备
- **THEN** 可以完成全部操作，榜单可开关滚动，轮盘多级返回可用，不暂停联机比赛

#### Scenario: Bound key conflicts
- **WHEN** 建议默认 B/Tab 与玩家已有绑定冲突
- **THEN** 明示冲突并允许改绑定，不悄悄夺取原版或其他 CS 操作

### Requirement: U2 Owned-equipment-only HUD
（第三轮用户决定，2026-10-04：“用核心CS的枪械HUD就可以了，不用新加”；“血量显示用原版的就好了”；“护甲现在就用核心CS自己的护甲HUD吧”。死亡竞赛不再画自建装备栏和血量/护甲读数：武器由游戏快捷栏与 CS 核心枪械 HUD 显示和切换，血量显示在游戏自己的血条上，护甲显示在 CS 核心护甲 HUD 上。下面的原要求仅作历史保留。）

右侧装备栏 SHALL 只显示当前实际持有的主武器、副武器、刀、Zeus 和允许投掷物；表现类别不等于四个物理库存槽。

#### Scenario: No grenade or several grenade types
- **WHEN** 玩家没有投掷物，或持有多种投掷物
- **THEN** 前者隐藏该项，后者可展开选择，每项映射实际批准槽位

#### Scenario: Switch under network delay
- **WHEN** 客户端点击装备图标切换但服务端尚未批准或拒绝
- **THEN** UI 正确表示待确认/实际选择，不高亮成不存在的物品

### Requirement: U3 Editable layout for all new controls and HUD
所有新增按钮/HUD SHALL 支持位置、大小、旋转、透明度、显示开关，绘制/裁剪/点击区域使用同一变换；Windows 与 Android 有各自默认布局和本地持久配置。

#### Scenario: Rotated and scaled touch element
- **WHEN** 玩家移动、缩放、旋转按钮后点击可见区域
- **THEN** 点击命中与画面一致，触摸目标不小于可用下限，不串到开火/视角控制

#### Scenario: Hidden controls and resolution changes
- **WHEN** 玩家隐藏全部竞技入口，或改变窗口/屏幕比例/UI 缩放
- **THEN** 可从菜单恢复布局，不丢失配置，元素保持在安全区域；隐藏 HUD 不关闭服务端规则

### Requirement: U4 Input ownership and presentation integrity
轮盘、皮肤抽屉、榜单和死亡镜头 SHALL 正确消费所属输入；界面关闭或生命切换时清除旧按住状态，显示不修改库存/计分/伤害。

#### Scenario: Finger release after shopping
- **WHEN** 玩家在面板里滑动并松手关闭，或死亡打断持续开火
- **THEN** 不将该次触点/旧按住状态透传成一次射击，新生命需有效新输入

#### Scenario: Multiple views
- **WHEN** 存在多个玩家视口或多相机渲染
- **THEN** 各自 HUD/镜头/计时独立，重复绘制不多发音效或推进死亡状态
