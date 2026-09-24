# 玲兰库存适配审计与修复（源码，未打包）

用户要求：继续核查玲兰模组的同类适配问题，一并修复，先不打包。
2026-09-24；修改前源码为 `5b5db73`，工作区干净，已 fast-forward 检查。
仅修改 CS武器与回归工具；未改第三方 DLL、游戏 Mods 或任何用户世界。

## 证据与审计范围

只读提取 `D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods` 中实际安装包：

- `[API1.9]玲兰辅助v3.0.3.scmod` 的 SushiBase.dll SHA256：
  `55d1a7c388bc9d4e96b6bc534428041700cc49418a854c979040d967c35e8355`。
- `[API1.9]玲兰科技v3.0.4(需玲兰辅助).scmod` 的 SushiTool.dll SHA256：
  `78c6984f244ab304c5320156fb74580da0adcba006f429cde2cbd9c502cfcec8`。
- 已安装 CS武器 1.4.9 的核心 DLL SHA256：
  `f17c1064e449cce4301ca235f421bf9492b8082464cdbe348f397198681b363c`。

玲兰两包的内部 Version 均为 3.0，不能用文件名后缀断言故障日志来自不同 DLL。
故障日志的 CS武器版本为 1.2.0，尚未取得该用户故障世界；没有声称重演了该存档耗尽的全过程。

核查实际继承链、槽位读写、频道保存/删除/重建、个人箱背包转发，以及 CS侧扫描、
重复拆分、修订号、事务、退款、创造候选项、制作路径。
普通 ComponentSushiBox 和 SushiMachine 派生库存使用各自的基类槽位，不是共享代理，
保持对象独立；未将相同内容当成共享证据。辅助模组没有发现另一个 IInventory 代理。
玲兰自动换弹的入口限定原版 BowBlock/CrossbowBlock/MusketBlock；既有实际 Dig hook 回归保留。
这不是所有玲兰功能、设置组合或 Android/UI 的全面验收。

## 已确认并修复

1. **同步箱字典被错当 IList**：真实成员是 `Dictionary<int,SushiSyncInventory>`。
   现在按键查找，并支持 0、7、12 等稀疏频道；不默认回退到频道 0，不以 Count 限制频道号。
   无法解析已知代理时停止扫描/枪械事务并输出诊断，不将其当成独立存储。
2. **无箱子入口的频道漏扫**：频道数据保存在非 IInventory 的 SubsystemSushiSyncBox 中。
   现在枚举所有已存频道，与实体箱入口按真实库存去重；即使当前没有箱实体也能发现枪。
3. **个人箱丢失创造库存语义**：先解析真实库存，扫描、武器槽判断、候选列表、属性查询、
   制作预检沿用原生创造槽规则，目录无限来源不作为普通持枪槽；事务使用创造替换路径。
4. **可切换代理导致事务/退款目的地漂移**：枪械事务固定开始时的真实 IInventory，
   提交前后检查入口和恢复归属；切频道时拒绝旧事务，已扣物品只回原库存。
   原频道在回调中被删除时，不向脱离世界的对象“退款成功”，而是保留未完成补偿。
   制作、道具使用失败回滚及刀皮退款也检查原目的地仍有效。
5. **同步箱补偿归属按实体保存不可靠**：两个同频道箱统一到真实库存的会话恢复身份。
   原频道仍为同一对象时自动重试，不随箱子切频道改变；旧实体代理收据不猜原玩家或频道。

## 恢复边界（重要）

玲兰频道没有持久化 UUID，频道号可删除后复用。因此只凭频道号无法证明重进后的
库存仍是失败事务的原库存。本次没有向玲兰世界数据写入新字段或修改第三方实现。

新同步库存退款收据使用已有 Owner 字符串中的 `sushi-sync/<channel>/<session-token>`。
只有同一运行世界里仍注册的原库存对象允许自动补偿。
若重进世界、频道被重建，或者收据来自只保存实体的旧代理路径：

- 保留退款步骤、数量、原枪引用和日志，不向新频道或新玩家猜测退款。
- 未决收据阻止同号频道继续进行本模组事务；原频道不明的旧同步收据保守阻止同步库存事务。
- 旧个人箱实体收据同样不猜玩家；相关个人/同步库存事务保守等待核验。
- **需要可信存档证据后另行恢复**，本次没有做自动迁移/人工清债工具。
  此限制只影响未完成补偿，不影响正常枪械存取、正常保存重进或普通玩家收据自动重试。

物品布局仍为 v5、GunRegistry schema 仍为 6、Recovery schema 仍为 1。
没有重排/复用/回收编号，没有清表，没有给旧枪补弹、补耐久、重置皮肤或等级。
Owner 原本就是字符串，旧读者仍可保留不认识的收据；不承诺降级后拥有新适配行为。
已经耗尽的历史编号以及截图 SCAR-20 的缺失状态不在本次自动恢复范围。

## 测试证据

新增 `tools/InventoryCheck`：直接检查已编译核心 DLL，不生成 scmod。
真实 Sushi DLL 回归纳入既有 `--sushi-inventory-mods` 检查入口；没有伪造同名 Sushi 类。

同一套 2248 项的对照：

| 对象 | 通过 | 失败 | 说明 |
| --- | ---: | ---: | --- |
| 已安装、未修改的 CS武器 1.4.9 DLL | 2223 | 25 | 22 项新增适配断言失败，另有 3 项既有失败 |
| 修复源码构建 DLL | 2245 | 3 | 玲兰专项 44/44，未增加失败名 |

44 项包括既有个人箱 18 项，以及新同步/创造/恢复 26 项：
共享修订号和身份、稀疏频道、无入口频道、210 次真实代理事务不增编号/不丢击杀数、
满表独立旧枪可用、真实复制与新枪仍拒绝、切频道、删除重建、失配拒绝、
原生 SushiSyncInventory 与枪械注册表两轮 XML 存读、原库存退款、未决退款两轮保存与隔离。

其余运行了 Recovery、Survival、CreativeCounters、CreativeSkins、Growth、SkinGrowth、
SaveGuard、0282Migration 原有回归。日志中的 injected/unknown schema 等 ERROR 是负向测试输出。
3 项既有失败均为 `survival/third-person-weapon/{ak47,awp,m4a1s}`，异常 `no weapon`；
修改前后相同，未将这部分报告为通过，也没有扩大范围修第三人称模型。

构建 core 和 PackageCheck 均为 0 warnings / 0 errors；`git diff --check` 通过。
本轮核心测试 DLL SHA256：`5cf41350bc83d6a7ae6ab084e5d3214b44b334ac0bb9b9fb56143cf7163d7f8f`。
本机详细结果在 `.tmp/syncbox-audit-20260924/{before-installed,after}.json`，临时原包/DLL不提交。

复跑（PowerShell；冒号参数需引号，避免被 dev.ps1 参数解析吞掉）：

```powershell
./tools/dev.ps1 dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release '-p:SkipScmodPackaging=true'
./tools/dev.ps1 dotnet run --project tools/InventoryCheck/InventoryCheck.csproj -c Release -- src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll 'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods' .tmp/inventory-check.json
```

第二条命令因上述 3 项既有失败退出 1；按 JSON 分组检查，不忽略失败退出码。
`SkipScmodPackaging=true` 是新增的显式纯编译开关，默认打包行为未改变。
未升级模组版本，未打包，未安装，未加载用户世界。现存 build scmod 修改时间仍是 2026-09-21。
后续实机需要在故障世界副本中检查双同步箱、切频道、正常存取/存读和独立旧枪操作。
