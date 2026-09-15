# 1.0.4-preview：综合规划 DLL 核验与补修

日期：2026-09-12。对应 `gun-registry-shared-storage-plan-2026-09-12.md`。

**结论：原先并没有全部做好。已继续补修可复现的代码缺陷，最终包在用户实际 API 与第三方 DLL 下通过 10,194/10,194 项离线检查。历史枪械恢复、整机性能、Android/真实手柄交互及现场配件改名仍不能标为完成。**

## 交付物与证据

本地输出目录 `E:/Obsidian Document/Document1/ScCsgoKnives/output/`：

| 文件 | 结果/用途 |
| --- | --- |
| `ScCsgoKnives-1.0.4-preview.scmod` | 核心包，535,447 字节；必须配合资源包 |
| `ScCsgoResources-1.0.0.scmod` | Full 原分辨率资源包，保持既有字节，不是 Lite/Mini |
| `plan-audit-1.0.4-final.json` | 最终包 10,194 项、0 失败；含真实 1.0.0/schema4 与正式 0.28.2 DLL |
| `plan-audit-1.0.4-final-schema3.json` | 同一最终包 10,158 项、0 失败；真实 0.41.4/schema3 跳版本 |
| `plan-audit-1.0.4-engine-manifest.json` | 实际游戏 DLL 与测试宿主副本的哈希一致性 |

SHA256：

```text
core scmod  a6b1ff72c5b3eae879bb5c12ef1b111ba1913d05dbdf2e0fd2b2bfb6dd270a0d
core DLL    6fac78f740b5295483eabab1ea813a58fbb6ed569452bb67e528ef77ef9d8634
resources   66fe182a3f83d01ff10ceb1dc6d00d0ebf5626f3ac30837eb9426751cbcb73eb
```

资源包与先前 1.0.0 一致；未修改游戏 Mods、玩家世界或第三方包。检查加载的是 scmod 内的 DLL，不是直接引用源码项目。核心构建 0 错误；有既有 NCalc/NCalcSync NU1902 依赖警告。实际 API 测试宿主构建 0 警告、0 错误。

## 此次发现并修复的遗漏

1. **Sushi 的具体根因**：`ComponentMiner.Inventory` 是实际引擎属性，原适配用 `GetField` 读取，导致 PersonBox 去重失败。已改为读取属性；玩家与六个真实代理共用槽位身份和库存修订。进一步修正补偿归属，让代理事务记到玩家的持久归属，避免代理实体消失后补偿找不到玩家。此前专项证据见 `sushi-person-box-root-cause-2026-09-12.md`。
2. **RecipaediaEX 入口仍漏接**：EX 浏览器不是原版 RecipaediaScreen，BlockItem 固定进入它的通用配方页，不调用原版 Block.GetBlockRecipeScreen。新增明确类型/Value 适配；在原浏览器 Update 前消费 CS 点击，随后转到独立 CS 页面。保留 EX 全局 `RecipaediaRecipes`；未知非空参数不再回退到上次枪械。
3. **换弹取消仍能抹掉新刀入场**：新增两种调度顺序测试后，先画新刀再取消旧枪的 4 个时点全部失败。现在用原换弹动作序号取消，仅当该序号仍处于 Reload 才有效。新刀 deploy、后续新换弹不受旧取消影响。取消事务不补弹。
4. **同型号实例切换被过度忽略**：此前将同型号不同 owned ID 一律当作同一次装备，使 CZ 前弹匣状态串到另一把枪。现在仅新枪模板实例化不触发重复 deploy；真正更换 owned 实例仍重置该实例动画状态。
5. **重复枪审计仍逐把重扫世界**：新增“正向冲突证据”路径，提交当下重读两个物理槽位、内容、ID 与底层映射。证据移走/合并即拒绝，不把陈旧快照当成“没有重复”的证明。自动拆分预算每轮 4；不变失败退避 5 秒，持有关系/记录/库存状态变化可提前重试；满表不再尝试分配。常规开枪/维修等仍重新查重；无法直接重读的掉落物/投射物证据仍走完整扫描。
6. **缺记录显示仍要求新世界**：可靠 v5 型号现在保留名称与基础图标并显示“状态待恢复”；缺失/隔离、型号不符、未知格式分开诊断。仍拒绝开火、维修、涂装等状态操作；不推测皮肤、耐久、弹药或等级。图标分支已实现，本轮未运行 GPU 像素验收。
7. **手柄边沿与投掷来源**：实际引擎肩键 `DownOnce` 是松开沿，不能直接用于 CS 按下动作。现统一从 held 状态生成 CS 按下沿，扳机保留 0.1 滞回。投掷新增按玩家、来源的释放门及发起来源跟踪，来源为原版合并输入/自定义触摸/额外键盘/额外手柄；独立来源松开不被另一个按住来源代替。暂停/失焦/设备连接变化阻止旧按压，准备阶段设备断开取消事务。原版自身已合并的 PlayerInput 仍作为一个来源，未声称可追溯任何第三方模拟输入的原始物理设备。
8. **数值和夹具过期**：成长界面仍有旧 3000 杀、×10 等说明，自测也保留旧参数和不合法的当前 schema 行。按规划中的独立目标更新数值预期和夹具；合法 schema5 行保留 `kc`，旧 schema3/4 使用真实旧行，不放宽解析器。另修复 `GrowthMode="99"/-1` 被枚举解析接受的问题。

## 分项验证与未完成项

| 规划项 | 已验证内容 | 仍未验证/限制 |
| --- | --- | --- |
| P0/P1 | 真实 Sushi 玩家+6代理、210次事务不涨编号；满表旧枪涂装/计数器可用；真实复制满表拒绝；两轮XML。真实 Logistics 7入口只扫描一个底层库、GUID切换合并/拆分即时重映射、原生 Vault Write/Read 两轮 | 不自动支持所有未知代理；匿名现场 holder 不能逐一还原 |
| P2 | 活冲突证据提交不调用全世界 locator；证据消失/合并拒绝且不分配；失败预算/退避源码已检查 | 普通非审计事务仍查重；无实际帧率改善百分比 |
| P3/P4 | 缺失/型号不符名称诊断；未知格式不猜型号；坏行原文与水位保留 | **没有恢复历史已耗编号、旧等级或缺失记录** |
| U1/U4 | 旧刀绘制与新选择错位、刀→空手/原版/刀/枪；换弹→刀两种顺序与4时点；旧取消不取消新换弹；CZ实例独立 | 真机音画、快速滚轮与每种枪型实机视频仍待验收 |
| U2/U6/U8 | 引擎实际 GamePad 状态与 WidgetInput；全部可选按钮按下/保持/释放；扳机抖动；设备断开；来源释放门；既有多指捕获和投掷时间线 | 桌面模拟输入不是 Android/蓝牙/USB 硬件验收；原版移动/背包冲突由提示提醒，没有接管原版设置 |
| U3 | 真实 EX 页面构造、Enter、Update、BlockItem；4轮属性/装配/返回；全局EX配方页不变；未知参数明确拒绝；原版导航也通过 | EX使用测试分类数据；未遍历所有第三方分类/搜索/配方。现场“配件全变可持续发射器”缺证据，未断言根因已修 |
| U5 | 原版字体/XML 的布局；旧设置默认形状、非有限数夹取、准星与手柄设置序列化；预览和保存回滚实现已检查 | 不等于不同 GPU/分辨率实机外观或磁盘故障验收 |
| U7/§6 | 真实 Subnautica ComponentHealthExt/Immunity：龟免伤、Kraken/鲸受击时钟共用、减伤；真实原 ModLoader 通用伤害上限；低伤章鱼规则 | 通用上限测试调用原 hook，未声称加载全部模组跑完整战斗场景；无真实击杀耗时；无单次上限目标仍可能被高等级武器秒杀 |
| §7 | Lv10/20/30 击杀1000/2500/4500，伤害×2/×4.5/×6.5，射速×1/×2/×3；Zeus5/3/2秒；容量×6.5与耐久×2.5保留；边界与两轮XML | 源档里已真正丢失的状态不能由新曲线恢复 |
| §8 | 35枪明确独立目标表、35×6维修取整；5配件真实引擎类/Content.zip CraftingId、白色颜料data0；跨堆叠扣料、一件产出、不足/连续点击/满库存无额外扣料；既有皮肤与计数器事务 | 没有将昂贵配件与全部工业模组经济做实机平衡认证 |

配件名称测试读取真实物品值，不以导航修复冒充物品改名已解决。配件共用材料涨价会间接提高刀具原料成本；没有更改刀具装配件数。计数器安装保留此前既有玩家等级门槛，没有新增方案未规定的等级要求。

## 存档与成长迁移规则

- 物品布局仍为 v5，35枪型号顺序与现有皮肤ID保持；实例 ID 1～1022 不重新分配、清表或回收。
- 相对于正式1.0.0，记录 schema4→5，新增 `GrowthKillCredit`/`kc` 用于新门槛衔接；此前1.0.3已经是schema5，本次没有再次变更格式。
- 实际 `KillCount` 原样保留，已获等级不下降。原规则已赚取但尚未应用的等级在可安全操作时补应用；等级内进度按新等级步长折算并向玩家有利方向取整。抵扣不是伪造击杀，也不会每次读档重复增加。例：旧 Lv10/1050 杀记录新增抵扣25，进度为新门槛下1075/1150；旧Lv30/3000杀用1500抵扣维持Lv30。
- 现有弹药与耐久不因换曲线补满；Zeus剩余充能按旧/新周期比例转换一次。新鲜模板按当前基础参数生成。后续合法升级保持耐久比例、弹量和充能比例。
- schema1～4 转换前保留现有完整世界快照流程；验证文件清单/哈希，备份失败拒绝加载。最终回归含真实旧DLL生成的记录、源记录不变、两轮XML、旧版预加载保护拒绝schema5、真实引擎区域文件锁的备份回归。
- 正式0.28.2继续走专用迁移：用真实旧DLL覆盖35型号的弹量/消音器组合，保留其一次初始化耐久语义。没有新增0.29～0.36猜测转换。
- 1.0.0分包核心与玩家正式全量 `[API1.9]CS武器1.0.0.scmod` 内核心DLL相同，SHA256为 `31c737bb4b86a8f8d77dbaa3be89a3d47ae17aa8e515c07c05d9051b97e121d3`。
- 本轮未提供或建立不可变的0.37.0完整现场世界夹具；这项长期政策要求不能由schema3/4构造数据代替。测试并非证明任意旧包都能安全降级，也未测试真实系统断电。

## 性能证据的范围

最终一次真实 Logistics DLL 扫描：7入口，每入口1536槽（共享一库），预热10次、测量200次；p50 **0.2376 ms**、p95 **0.4454 ms**、max **2.0239 ms**。另一轮相同夹具p50 0.2101 ms、p95 0.5786 ms、max13.7767 ms，存在宿主调度/JIT/GC波动，不能选取最快一轮承诺游戏帧率。

该测量只包含离线持有者扫描，没有运行游戏渲染、CPU蒙皮、GPU或世界全部模组更新。P5/R6的同设备受控帧时间、内存、日志速率对照仍未完成。

## 为什么不能恢复工业s6

用户再次提供的 `D:/下载/工业s6.scworld` 与此前相同：539,898,668字节，SHA256 `b19ca1cde3ef8e729d27a9423f5c6b30fc242acb172f2bd8a8ad5de006d172c3`。主世界与 Tartareosity 的 Project.xml/Project.bak 均无GunRegistry、GunDataLayout或CS枪械登记；用户确认没有其他故障前备份。

因此停止索取同一材料，也没有对该存档写入修复。防止继续误分配的代码修复不等于找回已经丢失的历史状态。新包尚未替换玩家已安装包，现有Bugs日志不是新包实机复测结果。

## 复跑

在仓库根目录，通过 `tools/dev.ps1` 把开发临时文件留在E盘。路径中的方括号按字面参数传给程序。

```powershell
./tools/dev.ps1 dotnet build tools/PackageCheck/PackageCheck.csproj -c Release --nologo --verbosity quiet '-p:GameDllDirectory=D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1' '-p:OutputPath=E:\Obsidian Document\Document1\ScCsgoKnives\.tmp\sushi-api1931-host'
./tools/dev.ps1 dotnet .tmp/sushi-api1931-host/PackageCheck.dll --scmod output/ScCsgoKnives-1.0.4-preview.scmod --resource-pack output/ScCsgoResources-1.0.0.scmod --previous-growth-package output/ScCsgoKnives-1.0.0.scmod --published-0282 output/ScCsgoKnives-0.28.2.scmod --sushi-inventory-mods 'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods' --vanilla-content 'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Content.zip' --json output/plan-audit-1.0.4-final.json
```

另一次将 `--previous-growth-package` 换为0.41.4包并省略 `--published-0282`，得到 `final-schema3.json`。这些第三方包和旧包是本地只读测试输入，没有随代码分发。

引擎SHA256：Survivalcraft `25816c2fbb6380d820b279ddc69455f6d401eeeaec86be131314a3aaa401372c`；Engine `3845e53eee06fbf3af5075145740069029c631a789a495e2a3aaa637211a98de`；EntitySystem `81b5c056854feccfeafcff986401fca39ad0a86a4fac397b33fb2a84a1ba0784`。已逐份与测试宿主副本比较一致。

第三方DLL：Logistics `b8ab56720c90da072a265e42273f86ae4a40e25170ed45a15ed08666a9188df9`；Subnautica `5fe09f657d9652e44f61425e117e514e0aee2d8f7e644bbe124804aa18fcfc65`；Sushi和RecipaediaEX哈希保留在最终JSON对应检查详情。

原工作树包含此前大量未提交修改；本轮保留并续修，没有整体提交/推送其他工作的文件。本地版本包、源码与报告已经完成，但不将其描述为完成Git跨机器交接。
