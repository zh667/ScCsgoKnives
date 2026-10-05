# 通用子世界物品状态迁移：远古世界0.41.16实包分析

状态：分析与接口设计完成，未修改产品、第三方包或玩家世界（Windows 诊断轮）。**实施状态（VPS 第二执行轮）**：§4 的 CS 一侧已实现（`ScItemTravel` / `Game.ScGunTravelApi`，随候选 mpd2），最终接口与字段以 [design §12.6](../../openspec/changes/weapon-state-consistency/design.md) 为准；远古的接入仍只是建议稿（[ancient-world-cs-bridge-suggestion-20261002.md](ancient-world-cs-bridge-suggestion-20261002.md)），未在远古上实施，游戏内旅行未验收。用户要求不是只适配远古世界，而是找到其他子世界模组可复用的解法。主入口仍为 `mp-state-consistency-plan-20261002.md` §10；本文将其“缺实际包”的部分更新为已取得0.41.16并完成真实程序集验证。

## 1. 版本与证据

用户提供 `D:/下载/AncientWorld_v0.41.16.zip`（232084269 bytes），SHA256 `e88b68f6edaf53b419b89db24ed8dd487734bf0aff0830af30824145f0977a66`。内含一个 `Kelly.Survivalcraft.AncientWorld-Debug.scmod`，SHA256 `ccc9b649623025dcf41bc2c4675e4b0151fe96a5af008cc8c7319508d74d6c62`。

包内 Version=0.41.16、ApiVersion=1.9.2.1、PackageName=kelly.survivalcraft.ancientworld。实际 DLL SHA256 `f9fe4ccfde6550ec01c76adb3dce2b9820c736e2cb3d3ad8f53ad7abb01df5b9`。此前玩家日志是0.41.15：本次对0.41.16做了直接验证，不能声称两个版本逐字节相同。

只在内存打开两层包，提取DLL/PDB/元数据，未展开232MB资源。工作目录 `E:/projects/ScCsgoKnives/.tmp/dev-temp/ancient-travel-20261002/`。实际DLL反编译在 `source/Game/`；引用下文行号均为本次反编译输出，不是上游仓库行号。

runner：`./tools/dev.ps1 dotnet run --project .tmp/dev-temp/ancient-travel-20261002/Probe/Probe.csproj -c Release`。
加载实际1.9.3.1 Windows引擎、实际mpc3 CS核心（ca054737…）以及本次Ancient DLL，调用其真实 InventorySnapshot.Capture→Save→Load→Apply，未启动游戏。首次夹具错误将Blocks当字段（该1.9.3.1为属性），修正反射读取后最终exit0并有断言。没有写任何真实世界，也没有触发Ancient的落盘journal函数。

可同步输入/结果：`mp-state-consistency-evidence-20261002/ancient04116-inputs.json`、`ancient04116-provider-probe.json`、`ancient04116-inventory-envelope.xml`。

## 2. 已定位的实际断点

`AncientWorldRuntime.cs` 中的流程：

1. `BeginTravel`（约630行）先调用自有 `TravelerSnapshot.Capture(player)`，抓生命/饥饿等、当前活动库存及衣服；再调用 `GameManager.SaveProject`。
2. `InventorySnapshot.Capture`（136行）每个物品仅保存 Slot、Value、Count、BlockType、Assembly。创造库存无限数量被正规化为1。
3. `TravelerSnapshot.Save`（68行）生成独立 `AncientWorldTravel` XML：Vitals、Inventory、Clothing、AgesTree、Pets。没有CS枪表、稳定枪身份、ScGunTravel快照或通用扩展数据。
4. 写自己的 `AncientWorldTravel.xml` 后切 GameLoading。新建子世界只复制Players子系统等信息，并未把源CS枪表整体搬过去。
5. 目标 `ProjectXmlLoad`（AncientWorldModLoader约298行）只做游戏模式调整。
6. `OnProjectLoaded` 读取pending旅程；`OnPlayerAvailable`（约782行，亦由OnPlayerSpawned及树门更新调用）在玩家可用时 `travelerSnapshot.Apply`，直接清/重填活动库存。该时机晚于CS的ProjectXmlLoad导入。
7. `ResolveValue`（約295行）按完整类名＋程序集找目标方块类型，只做 `Terrain.ReplaceContents`；数据位中“枪实例编号”完全不动。

所以是两处同时缺失：**自有旅行快照不携带状态；加载后直接恢复避开现有CS XML导入流程**。即使源SaveProject触发CS在Project.xml中写了ScGunTravel，它也没进入Ancient自己的TravelerSnapshot；调整钩子优先级或增加路径白名单不解决。

`rg`核对包内没有 ScGunTravel/ScCsgo 的桥接调用。它有针对WildBond的可选版本化反射桥 `AncientWildBondBridge`，证明提供方已有“携带外部状态”的类似设计思路，但不能把宠物桥误当CS桥或已存在的通用接口。

## 3. 实际代码复现的结果

源枪：AK，局部ID1，7发。目标方块注册号从304改为320，以检查物品类型重映射。

| 目标已有记录 | Ancient实际Apply结果 | CS实际读取结果 |
|---|---|---|
| ID1属于不同型号，18发 | 方块号改为320，枪ID仍1 | IsKnown=false，不可用，符合纸片退化路径 |
| ID1同为AK，但23发 | 方块号改为320，枪ID仍1 | IsKnown=true，读取23发，不是来源7发 |

第二项说明单靠“缺记录/型号不符时尝试导入”不够：最危险的同型串号看起来完全正常。额外向 Inventory XML 加 Extensions 节点再调用真实Load→Save，节点被丢弃。单方面把扩展写进XML、但不修改保留/消费逻辑，也不能实现迁移。

这证明0.41.16的实际序列化/恢复契约与CS记录机制不兼容，解释与0.41.15日志一致；完整0.41.16游戏内双向旅行仍未执行。

## 4. 什么才是通用解法

应将 **物品状态迁移引擎** 与 **子世界旅行流程适配器** 分离：

```text
原生玩家XML / 远古自有TravelerSnapshot / 其他模组运行时传送
                         ↓ 统一调用边界
               版本化物品旅行协议
      来源身份 + 携带物品 + 各状态提供方payload
                         ↓
       CS记录校验 / 本地编号映射 / 幂等提交 / 失败恢复
```

每个提供方只接入少量生命周期调用，不重新实现CS编号算法。CS核心不查某个世界目录、不维护几十套枪迁移补丁。将来其他使用外置状态的物品也能注册独立namespace payload。

但“通用”不是“对任意完全不配合的第三方自动正确”：如果它只把整数value写过去，源世界、稳定身份及原状态均丢失，而目标还可能有同号同型记录，则到达端观察到的信息不足，无法区分带来的A与本地B。全盘扫描同数字ID、用纹理猜枪、缓存上一个世界或共享全局表都不能证明正确。

### 4.1 三类接入覆盖

| 提供方路径 | 统一迁移引擎如何复用 | 必要接入 |
|---|---|---|
| 复制完整玩家XML且保留扩展，在目标加载前导入 | 将既有ScGunTravel v3包装成通用payload，沿用现有格式转换及稳定身份 | 现有钩子保留；核验实际保留路径，不笼统称自动支持 |
| 自有旅行快照，目标玩家加载后恢复（Ancient） | 对同一payload调用运行时prepare/apply/commit | 旅行前导出、journal保留扩展、目标恢复前预检/事务提交、成功后确认 |
| 同一Project里的传送/位置变化 | 世界registry不变，通常不需迁移 | 明确识别无需跨表，不因位置变化分配新ID |
| 服务端跨Project/异步多人旅行 | 同一协议，服务器认证操作者/旅程，按玩家隔离 | 提供方真实生命周期及耐久化回执；不能套用Ancient单玩家静态pending |

Ancient0.41.16明确限制玩家数据/实体均只有1个才允许旅行；通用协议须支持多玩家独立transferId，但不能声称这个包已有多人跨维度功能。

### 4.2 统一payload最小信息

这是拟议契约，不是已存在API；最终字段名由VPS在兼容设计中固定：

- 协议版本、provider namespace和能力/保存schema；unknown版本保留原文并拒绝不安全应用。
- transferId；稳定源/目标世界identity；玩家旅程identity（不能仅用可重用PlayerIndex）；单次移动或明确复制语义。
- 每个携带位置的inventory kind/slot、完整value/count、方块类型身份；来源方块ID不假定等于目标。
- CS每把枪的稳定逻辑identity、源局部ID、record revision、完整状态快照、保存布局/schema、成长规则和携带时间基准。
- 导出快照与实际槽位的绑定/摘要；摘要验证一致性，不冒充对不可信客户端的授权。联机端只接受服务器生成/认可的来源包。
- 操作状态/幂等结果（prepared、destination committed、completed/aborted），保留提交ID→本地ID映射以供重试。

继续使用已有GunTravelWorldIdentity/稳定身份含义和ID规则，不为接协议破坏旧枪布局6/schema7。源局部ID只是一个世界内索引，跨世界关联以稳定identity为准。完整状态含弹药、溢出、耐久/上限、皮肤、消音器、计数成长、充能；未结算击杀/补偿/换弹通过已有ReadyForTravel完成/取消或明确拒绝，不能悄悄遗留。

### 4.3 拟议API及事务时序

建议CS公开可选版本化桥（如 `ScGunTravelApi`，名称仅建议），输入输出采用小型DTO/JSON/XML与明确错误码；提供方可经反射发现，避免强引用CS核心。不要把反射私有Ancient类作为永久“通用标准”。

```text
PrepareExport(context, carriedSlots) -> immutable envelope / refusal
PrepareImport(destinationContext, envelope, currentSlots) -> detached plan / refusal
Apply(plan, inventoryTransaction) -> commit receipt
Finalize(receipt) 或 Abort(reason)
```

这是四个职责，不强制四个具体C#方法。现有Capture/Prepare对detached XML的验证/映射可复用；运行时路径不能把更新过的Project.xml写盘后就假定live registry已更新。

顺序要求：

1. 源端在进入旅行流程时固定本次玩家/库存，拒绝或合法结束未完成枪动作。导出后若物品改变，重新预检，不能用过时快照恢复。
2. 将各状态提供方payload随同一旅行journal保存；它是本次迁移的最小事务数据，不是全世界自动备份。现有Ancient journal的.tmp/.bak是第三方既有机制，本轮不改其文件策略或扩大备份。
3. 目标先解析目标方块类型、完整payload、槽容量、registry容量/规则、目标现有占用，完成全部可失败预检后才清/替库存。
4. 一个权威提交应用目标slot＋registry＋identity映射，source==target按noop处理。同一transferId重试返回同一结果，不能二次分配、修满耐久或复制成长。
5. 用户库存恢复成功但还没有目标持久化确认时不能把journal标completed；记录提交成功而衣服/宠物/玩家其他步骤失败时有清楚补偿/重试边界，不能只有库存恢复而枪表残留半提交。
6. 崩溃/重启/断线后根据receipt恢复；不能凭“目的地已有同ID”推定成功。持久化源player保存副本不是实际另一个可访问副本，需要明确旅行者离开/目标接收凭证；不能全忽略source slots，也不能误把旅行留下的休眠player快照当真复制而每趟改身份。
7. 往返用稳定identity找到对应本地映射并应用本次最新状态；目标相同identity仍有其他可访问持有者时按复制/冲突策略处理，不能无条件覆盖。

### 4.4 远古这份包如何接同一协议

- `BeginTravel`：在构造/固定TravelerSnapshot的同一预检阶段调用导出，确保SaveProject前后状态一致；只针对实际携带库存，不能把整个源表塞进journal。
- `TravelerSnapshot`：增加不透明扩展集合，Save/Load保持未知namespace数据；required provider缺失时拒绝相应不安全恢复并保留待处理数据。只给ItemStack多加一个int字段不够。
- `OnPlayerAvailable`：在 `travelerSnapshot.Apply` 清库存之前prepare所有扩展；由统一事务恢复slot与记录，不能先露出旧value供游戏使用一帧再修。
- `s_pendingApplied` / journal Complete：按可恢复receipt而不是仅进程静态bool判断已提交。此处同时涉及Vitals/Clothing/Pets的部分失败，必须有明确跨提供方协调，不宣称一张CS表能保证整个旅行原子化。
- 已有WildBond版本化桥可作为提供方可选桥风格参考，但宠物Export/Import不替代物品记录。

现有包没有通用扩展点调用；仅我们新增API，它不会自动调用。可交维护者一份最小接入补丁建议，或在获得授权后做特定适配器；**可复用的是迁移逻辑，必须接入的是旅行生命周期**。本轮未获第三方改包授权，不修改远古DLL、不默认Harmony全局拦截。

## 5. 不建议的“全自动”替代方案

- 所有世界共用一份全局枪表：世界副本、存档恢复、不同模组组合、独立游戏会话会互相污染；还扩大故障范围。
- 到达后发现缺记录才找上一个世界：同型碰撞不会缺记录，多玩家/重启/回到旧世界无可靠“上一个”。
- 只缓存当前进程源枪表：不能覆盖重启恢复，不能证明是移动还是另一把同值枪；缓存不能替代durable receipt。
- 只监听AddSlotItems/RemoveSlotItems：普通整理、掉落、复制与维度旅行都走它们，无法从这些调用恢复来源语义。
- 把完整状态塞进现有方块数据位：容量不够且破坏兼容；独立记录本身可保留。
- 只把目标模型强制画出来：同型串号更难被发现，不能恢复原弹药和耐久。

## 6. VPS分阶段实现与验收

1. 先建实际Ancient InventorySnapshot复现测试：本次两类碰撞＋方块304→320＋未知扩展Load/Save丢弃；基线故障有断言。
2. 抽出与目录/提供方无关的CS导出、detached导入计划、runtime提交和幂等receipt；保留现有v3/旧保存兼容。
3. 用三种独立旅行夹具验证：完整XML、白名单slot快照＋显式扩展桥、加载后Apply；三个路径调用同一迁移算法，而不是三个枪号补丁。
4. 再拟Ancient最小桥接补丁/接口契约并列权限边界；没有接入修改或维护者新版，不能勾“该实包已兼容”。未来其他mod只需声明/实现同协议或轻量入口适配。
5. 测源7发→目标同型23发必须保7，往返消耗后保新值；异型碰撞安全重映射；完整字段；same transfer重放/错误target/旧generation；崩溃在源保存后/目标slots后/registry后/持久化确认前；无空间、缺模组、schema不支持、未结算义务；两玩家独立旅程。
6. 实际0.41.16单机API1.9.3.1的已有/新建子世界、树门/普通门、游戏重启后pending恢复待用户验收；MP泛化需真正支持MP的提供方，不能用Ancient单玩家门禁绕过来代替。

旧Game(3).log中已经坏掉的枪，源记录/身份不足时只能保留并说明；当前提供包不会自动修复历史存档。本轮只读分析、验证边界与更新规划，未实施游戏改动。
