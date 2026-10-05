# 外部提供方缺陷：最小复现与补丁建议（玲兰、联机平台）

日期：2026-10-02。配套：[规划入口](mp-state-consistency-plan-20261002.md)、[设计 §10](../../openspec/changes/weapon-state-consistency/design.md)、[验证矩阵](../../openspec/changes/weapon-state-consistency/verification.md)。

本文件只整理**可交给提供方审核**的复现与建议。CS 武器没有修改、重打包或替换任何第三方包；下列建议均未在第三方代码上实施或验证，只基于对指定版本的反编译阅读和真实程序集的离线调用。是否联系维护者由用户决定。

## 0. 输入身份

| 对象 | 来源 | 身份 |
|---|---|---|
| 玲兰辅助 v3.0.3（SushiBase.dll，内部版本 3.0） | 用户 `D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods` | SHA-256 `55d1a7c388bc9d4e96b6bc534428041700cc49418a854c979040d967c35e8355` |
| 玲兰科技 v3.0.4（SushiTool.dll，内部版本 3.0） | 同上 | SHA-256 `78c6984f244ab304c5320156fb74580da0adcba006f429cde2cbd9c502cfcec8` |
| 联机平台 Survivalcraft.dll（1.9.3.2） | 用户 Windows 平台安装 | SHA-256 `7856ec634fedf319af598dc6966ae8301ac8e75e7f53e54bbecdf75a1deae984` |
| Survivalcraft.Multiplayer.dll | 同上 | SHA-256 `aa6136c507cfbf2902ac395dbbe58d55ea816f46b7ea2c68a26acc769a140033` |
| Survivalcraft.CompatNet.dll（仅 Windows 版平台有） | 同上 | SHA-256 `b0330dd8b244e8c8c21f83276a69f81ea716a1956a8b04e510463bce16c86cb6` |

这两份玲兰包是**单机包**。目标“联机版玲兰”尚未取得；下面对多人行为的描述来自这两份单机包的代码加上平台 Windows 版自带的玲兰桥，不能当作未来联机版玲兰的结论。

## 1. 玲兰（提供方：苏世-Sushi）

### 1.1 自带容器对“不可放置且无原版耐久”的物品二次放大堆叠

- 位置：`Sushi.SushiUtils.GetNewMaxStacking(Block block, int? value)`。先取 `block.GetMaxStacking(value)`；当“调整堆叠”与“无耐久非放置物堆叠”两个选项开启、结果为 1、`!block.IsPlaceable`、`block.Durability < 0` 时返回 `MaxStackingNumbers[MaxStackingNumberSmall]`（用户设置下为 4）。`ComponentSushiBox`、`ComponentSushiSyncBox`、`SushiSyncInventory`、`SushiMachine` 的 `GetSlotCapacity` 都直接返回它。加载时 `SushiBaseFunction.ApplyMaxStackingTweaks` 还把每个方块的 `MaxStacking` 字段改写为同一结果。
- 最小复现（真实 DLL，离线）：`tools/PackageCheck/SushiStackingRegression.cs` 的 S01 三条。开启两个选项后 CS 枪方块字段由 1 变 4；CS 方块自己回答 1，原版库存容量为 1；`SushiSyncInventory.GetSlotCapacity(0, 一把已有记录的枪)` 仍为 4。
- 影响：每把已实例化的枪靠物品值里的记录编号区分，一格里放两把值相同的枪意味着两个副本共用一条记录（弹药、耐久、成长）。不同记录的枪值不同，不会被叠；会被叠的是复制品（创造模式里把同一把枪放进去两次）和未使用的新枪。CS 侧现在的处理是：整叠原样保留、不可直接使用并提示分开，分开后的副本首次使用时获得自己的记录（见设计 §10.6）。
- 建议（任选其一，改动都只在 `GetNewMaxStacking`）：
  1. 尊重方块对具体物品值的回答：当调用带 `value` 且方块类型**覆盖了** `GetMaxStacking(int)`（`block.GetType().GetMethod("GetMaxStacking", new[]{typeof(int)}).DeclaringType != typeof(Block)`）时直接返回 `num`，不再套用“无耐久非放置物”规则。
  2. 提供显式排除：`public static readonly HashSet<int> StackingExcludedContents`（或按类型的集合），其他模组在加载时登记自己的方块；函数开头命中即返回 `num`。
  这两种都不改变玲兰对原版物品和未声明模组物品的现有行为。

### 1.2 个人箱绑定进程内唯一的 `ComponentMiner`

- 位置：`Sushi.ComponentSushiPersonBox` 的全部库存方法都转发到 `m_SubsystemSushiTotal.ComponentMiner.Inventory`。`SubsystemSushiTotal.ComponentMiner` 为空时取 `SubsystemPlayers.ComponentPlayers[0].ComponentMiner`（`SubsystemSushiTotal.cs` 约 131–139 行）；`ComponentSushiDrawer` 约 67 行也会把它设为自己所属玩家的 miner。整个进程只有这一个值，世界里所有个人箱共用它。
- 最小复现（真实 DLL，离线）：`tools/PackageCheck/SushiInventoryRegression.cs`：一个 miner、六个个人箱代理与玩家背包得到同一个持有者键（`player-plus-six-person-boxes-one-holder`）；把 miner 的 `Inventory` 换成另一个对象后所有代理立刻改指新对象（`person-box-remap-follows-engine-property`）。
- 多人后果（代码阅读结论，未实机）：在服务器进程里这个 miner 是房主的（或最近一次被 `ComponentSushiDrawer` 写入的那个玩家的）。客户端 B 打开“自己的”个人箱，拖动请求到服务器后操作的是该 miner 的背包。平台的归属检查 `PlayerDragDropPacket.IsOwnedByOtherPlayer` 只看库存组件所在实体上有没有 `ComponentPlayer`，个人箱是方块实体，检查通过。结果是 B 可以经个人箱存取房主（或另一名玩家）的背包。CS 的库存别名映射会如实把个人箱映射到那个背包，不会、也不应该替它猜“请求者是谁”。
- 权限要求：服务器必须从**收到请求的连接**解析操作者，再取该操作者自己的背包；不能用进程玩家列表的第一个，不能信任客户端自报的玩家编号，也不能让客户端借箱子指定别人的库存。
- 建议：个人箱不要作为世界共享的 `IInventory` 组件存在；打开时由服务器按请求连接的玩家创建只对该玩家有效的视图（或在每次库存操作入口传入操作者，`ComponentSushiPersonBox` 的方法改为按操作者解析 `ComponentMiner`）。单机行为不变（操作者恒为本地玩家）。

### 1.3 同步箱频道按“键连续”保存

- 位置：`Sushi.SubsystemSushiSyncBox.Save`：写 `ChannelCount = SushiSyncInventories.Count`，取 `SushiSyncInventories[0]`，再对 `j = 1 .. Count-1` 取 `SushiSyncInventories[j]`；`Load` 对称地读 `Channel1 .. Channel{Count-1}`。`RemoveChannel(key)` 从字典移除任意键，`AddChannel` 取第一个空键。
- 最小复现（真实 DLL，离线，Windows 探针）：频道 0/1/2 → `RemoveChannel(1)` → `Save` 抛 `KeyNotFoundException: The given key '1' was not present in the dictionary.`（`docs/tasks/mp-state-consistency-evidence-20261002/sushi-offline-probe.json`）。删除频道 0 则在 `SushiSyncInventories[0]` 处同样失败。
- 可达性：在这两份程序集的反编译源码里**没有找到 `RemoveChannel` 的调用点**（只有定义），当前界面是否能触发未知；因此这是接口层面的缺陷，不是“用户当前会丢档”的结论。
- 建议的最小存储格式：保存真实键列表（如 `ChannelKeys = "0,2,5"`），每个频道存为 `Channel{key}`；读取时有 `ChannelKeys` 用它，没有则按旧的连续格式读（兼容旧存档）；不要为了连续而重排频道号，否则已放置的同步箱会指向别的库存。
- 关于频道代次：频道号可以删除后重用，存档里没有能区分“原来的频道 3”和“后来新建的频道 3”的标识。CS 侧对未完成的退还只认**同一个仍然存活的底层库存对象**，重载后或同号重建后保留欠条而不向新频道退款（`SushiSyncInventoryRegression`：`recreated-channel-does-not-inherit-old-refund`、`unresolved-refund-survives-two-saves-and-fences-reused-channel`）。若提供方为频道增加持久的唯一标识（创建时生成的 GUID 或递增代次），CS 可以据此在重载后安全归还；在此之前这是保守限制，不是缺陷修复。

### 1.4 整理/平铺里的“第一个玩家”

`SushiUtils.InventoryResort` 在堆叠容量变小后把放不下的物品丢在 `SubsystemPlayers.ComponentPlayers[0]` 的位置，并把提示发给该玩家；`InventoryAverageSet` / `InventoryAverageSetByItem` 的拒绝提示同样发给第一个玩家。多人时操作者不一定是第一个玩家。建议这些函数接收操作者参数。对 CS 枪的直接影响：CS 把枪的堆叠上限固定为 1 之后，旧存档里已叠在一格的新枪在“整理”时会被分到空格子，放不下的由玲兰按上面的规则丢成掉落物（不会消失）；没有掉落物子系统时玲兰放弃整理、保持原样（`SushiStackingRegression`：S02 older stacks）。

## 2. 联机平台（1.9.3.2 Multiplayer / CompatNet）

### 2.1 创造库存的服务器端写入不通知客户端

- 位置：`Game.ComponentCreativeInventory.AddSlotItems / RemoveSlotItems / SetSlotValue` 直接改 `m_slots`，`OnSlotChange` 是空方法；而 `ComponentInventoryBase` 的同名方法都会经 `OnNetworkInventorySlotChanged` 让平台标脏并在帧末发 `InventorySyncPacket`。于是服务器端任何模组对创造快捷栏的改写（CS 给新枪分配记录、模板实例化、复制分离）都不会到达客户端，直到下一次拖动让 `DragDropItemPacket` 广播整份库存。
- 最小复现（真实平台程序集，离线）：`tools/NetLoopCheck --baseline` 对 mpb：服务器提交后平台没有发库存包，客户端槽仍是模板（T1 失败）。本候选的做法是 CS 自己在事务结束后请求平台对该库存发一次同样的包。
- 建议：让 `ComponentCreativeInventory` 的三个写方法调用 `OnSlotChange(slotIndex)`，并像基类一样在 `OnSlotChange` 里触发 `OnNetworkInventorySlotChanged`。这样所有模组的服务器端改写都会同步，不必各自补发。
- 附带观察：`InventorySyncPacket` 是整份库存（每槽 12 字节），创造库存包含全部目录槽，一次约数万字节；没有版本号，防旧完全依赖通道 0 的 ReliableOrdered 顺序。建议创造库存只同步开放槽（`OpenSlotsCount`），或提供按槽的增量包。

### 2.2 平台自带的玲兰桥覆盖范围（Windows 版 CompatNet，适配器 8 “SushiTool”）

`Game.SushiToolCompatNetRuntime` 处理：同步箱频道状态发布（0.25 s 增量、5 s 全量）、频道内库存操作（Move / DragDrop / Drop）、频道元数据（改名、排序）、机器控制与配置、矿车（控制、回收、呼叫、乘坐）。没有任何个人箱相关处理，也没有针对 SushiBase（堆叠调整、整理/一键存取函数）的适配。Android 版平台只带 Multiplayer，没有 CompatNet，因此**没有玲兰桥**。

CS 自有的包 197 只承载 CS 的握手、枪表、输入与表现，不替代也不包含玲兰的频道/机器/个人箱同步。对应的能力状态在游戏日志里每个世界打印一行（`[CS_SUSHI] ...`，仅在装了玲兰时），内容见 `ScSushiCompatibility.Describe`。

### 2.3 仍待定位的两项平台/交互警告

见验证矩阵 X01（命中姿态 body-only fallback）与 X02（四个区块修补 6 次后仍不一致）。两者都没有定位到独立根因，未随本轮改动。

## 3. CS 这一侧的能力边界（按功能分开）

| 功能 | 单机（玲兰两份单机包） | 多人 |
|---|---|---|
| CS 枪记录、槽位、输入、表现 | 不涉及 | CS 自有通道，离线用例通过，实机待用户验收 |
| 玩家背包、原版箱子里的枪 | 一物一格（方块回答 1） | 平台库存包同步；创造库存由 CS 补发 |
| 个人箱 ↔ 玩家背包的别名去重 | 离线验证有效（六代理一持有者） | 归属由提供方/平台决定；现状为进程内唯一 miner，A/B 各自背包的保证**不存在** |
| 同步箱频道库存里的枪 | 映射、扫描、补偿离线验证有效 | Windows 平台桥负责复制，CS 不发；Android 无桥，不同步 |
| 玲兰容器内的堆叠 | 仍可叠到 4（提供方可控）；整叠保留，不可用直到分开 | 同左 |
| 整理 / 一键存取 / 同名存入 / 平铺 | 离线验证：完整物品值、数量守恒、不分配编号 | 这些操作在多人下由谁执行、如何同步取决于玲兰联机包，未验证 |
| 机器吸物、死亡保物 | 未离线运行（需要世界、掉落物与实体） | 未验证，待目标联机玲兰包 |

## 4. 远古世界 0.41.16（提供方：Kelly）——带 CS 枪穿越

输入身份：用户 `D:/下载/AncientWorld_v0.41.16.zip`，SHA-256 `e88b68f6edaf53b419b89db24ed8dd487734bf0aff0830af30824145f0977a66`；内含 DLL `Kelly.Survivalcraft.AncientWorld.dll` SHA-256 `f9fe4ccfde6550ec01c76adb3dce2b9820c736e2cb3d3ad8f53ad7abb01df5b9`。反馈者日志中的 0.41.15 未取得。

- 现状（实际 DLL，离线）：旅行者快照只带物品的槽、数值、数量和方块类型，目标世界在玩家出现后直接恢复库存；枪的实例编号原样带过去，读到的是目标世界同编号的记录（同型→读成那把枪的弹药/耐久/涂装；异型→不可用）。它自己的库存 XML 会丢弃不认识的节点。
- CS 侧（mpd 起）提供 `Game.ScGunTravelApi`（导出 / 目标预检 / 导入并返回数值映射 / 完成确认），提供方只需在三处生命周期调用并原样保存一段文本。远古现有包**不会**自动调用它。
- 最小接入补丁建议（四处改动、失败处理、已做的实际 DLL 验证、未覆盖项）：[ancient-world-cs-bridge-suggestion-20261002.md](ancient-world-cs-bridge-suggestion-20261002.md)。未在远古代码上实施；是否联系维护者或另行授权我们做特定适配由用户决定。
- 其他以“自带快照＋加载后恢复库存”方式做子世界/维度旅行的模组同理：接同一组三个调用即可，不需要各写一套枪编号处理。把玩家整段 XML 带过去并在目标加载前导入的模组，走既有的 `ScGunTravel`（ProjectXmlSaved / ProjectXmlLoad），两条路径对同一把枪使用同一身份和同一条编号映射规则。
