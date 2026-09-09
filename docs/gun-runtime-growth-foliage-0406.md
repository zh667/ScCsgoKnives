# 0.40.6 — 实际成长入口与植被射线复查

## 用户实测证据

Game.log 确认实际加载 0.40.5，安装包 DLL SHA256 与交付一致：`140d940e9cbe546f622dae921faeb8b0957fce1d497289bff962cda00e601929`。

13:05:11，AK 记录 4 到达 100 杀，日志显示 earned=1 / applied=0；13:05:28 实际已 104 杀仍 Lv0。该世界加载日志明确 `counter rule Unset`。所以这是**规则初始化和应用入口漏接**，不是击杀未记、不是物品名缓存。

原版仅在装配台安装计数器时选择 CountAndGrow/CountOnly，创造模板路线绕开了对话框。运行 UpdateGrowth 却仅在 CountAndGrow 时应用等级，造成无提示地停在 Lv0。

## 成长修复（创造／生存共用）

- 新增 `ScGunGrowthService.Advance`，实际子系统 UpdateGrowth 统一调用。若已有计数器而规则为 Unset，默认 CountAndGrow；明确选定的 CountOnly 不被覆盖。
- 规则正常化后，依据实际保存击杀数补应用 earned level，即使旧记录没有 PendingGrowthLevel，也不需要再击杀一个目标。104 杀补到 Lv1；没有伪造或补加击杀。
- 继续等待换弹/动作结束、记录可写时，通过原有事务应用成长。保持当前弹量；耐久按既有比例换算，充能按剩余比例处理；不补满弹、不修满、不充满。
- 升级通知改为遍历 SubsystemPlayers，而非尚未建立的 m_states。修复首次世界更新时已补升级却漏提示的问题。
- 实际规则、待应用等级加入 shot 诊断；升级事务拒绝只做一次性原因日志。默认规则变化有 `[GUN_GROWTH]` 日志。
- 世界物品布局仍 5、记录 schema 仍 3，不重编 ID、不改计数上限和每级击杀要求。仅将“未设置”解析为用户要求的默认成长，并补应用已有 earned level；不迁移任何内部测试格式、不编辑真实玩家存档。

## 植被

- 上轮漏了花朵、作物等 CrossBlock，以及水生植物。现在 CrossBlock（草、三种花、作物、枯灌木、树苗）、LeavesBlock 的全部原生派生种类、落叶、藤蔓、水生植物通过枪弹射线；实心草皮土方 GrassBlock、树干、墙、玻璃继续阻挡。
- 运行 Fire 统一调用 `ScGunRange.TraceBullet`，该方法调用实际 SubsystemTerrain 实例的 Raycast 并保留原有分段长射程限制；不是只改静态测试用重载。创造、生存、classic/survival 手感、每颗霰弹都走同一段代码。
- **尚未从旧日志复现“原生草／树叶依然挡弹”的具体地形条件**：旧日志只记录 terrain 次数，没有方块类型。0.40.5 的原生草/叶类型谓词本来已放行，不能将花的遗漏冒充草叶的确切原因。本轮实测引擎离线射线可通过这些类型，不宣称已看过玩家同一现场的游戏截图。
- 新增 `[GUN_FOLIAGE]` 输出实际加载的放行方块类型及编号；采样 shot 的 `vegetation` 输出 IgnoredVegetation、PassedTypes、LastBlocker（地形射线的最终阻挡类型）。不另发射射线、不取随机数、不修改方块、不记玩家位置，受既有日志预算限制。

## 验证

1. 35 枪 × 实际 ComponentInventory/ComponentCreativeInventory：从计数器模板取出，以 Unset 开始，99→100、999→1000，动作忙时延后、空闲时应用，回调只一次，ID 与弹量不变；保存读取两轮后不重复升级。
2. 35 枪 × 两库存：已有 104 杀 / 无 pending / Unset，无需新击杀就应用 Lv1；半损耐久与空弹状态不被补满。
3. 35 枪 × 两库存：明确 CountOnly 不被覆盖，只继续累计计数，不触发升级或提示。
4. 直接调用真实 SubsystemScGunBlockBehavior.UpdateGrowth，配置玩家及可观测 ComponentGui：生存/创造、99 杀加一次队列、旧 104 杀直接补升，各场景 GetDisplayName 都变为 Lv1，升级提示/提示音回调恰好一次（包含首轮 m_states 为空）。提示内容调用已验证，像素/音频设备播放效果待实机。
5. 每个原生 Cross/Leaves/WaterPlant 类型均检查。40 组共用射线测试（10 类植被 × 两种游戏上下文 × classic/survival）：实际 SubsystemTerrain 实例穿植被命中后方实体，前方补树干后阻断实体；运行 Fire IL 检查确认调用的是该入口。射线算法本身不读取游戏模式，库存/成长的双模式行为另由前述实库存测试覆盖。
6. Full/Lite 带公开版 0.28.2 DLL 和原始备份检查，各 **7399 项、0 失败**，包内 DLL 一致。原有 1960 个 0.28.2 状态与备份迁移检查继续保留。
7. 额外把用户游戏目录的实际 Survivalcraft.dll / Engine.dll / EntitySystem.dll 及依赖复制到独立临时测试目录，运行同一测试宿主。**7362 项、0 失败**（未额外带 0.28.2 参数）。报告 `output/0406-installed-engine-check.json`。实际 Survivalcraft.dll SHA256 `51f9b4734a33193347670e8f3bfdac844c943348dda17dcc38ace4568af4c7e1`，与 NuGet 宿主不同，故特地复查。初次宿主缺 OpenXR 依赖无法完整初始化，补齐同目录依赖并增加宿主本地解析后通过；没有修改游戏目录文件。

本轮不会启动玩家世界或改写现有 Project.xml。测试是实际引擎对象的离线运行，不是游戏画面验收；特别是用户现场草叶现象仍需带新增诊断的实机复测。

保留而不代提交开始时已有的属性文案、共享 UI 断点及未跟踪皮肤规划文件。
