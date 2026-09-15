# Sushi 个人箱重复枪械记录：1.0.3-preview 定向修复

日期：2026-09-12。状态：确定的代理识别错误已修复，真实第三方 DLL 离线回归通过；玩家世界复测待完成。

## 现场证据与纠正

来源为 `D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Bugs\Game.log`，本次读取时最后写入为 16:45:02。

- 16:34:09.059：明确加载 CS 武器 `1.0.2-preview`。
- 16:34:16.796：`1.0.2-preview initialized`。
- 16:34:26.864：records=1022，next=1023。
- 16:34:26.867：ID 1022 和 589 仍各有 7 个持有者。
- 16:34:29.790 / 16:34:37.497：操作返回 DuplicateUnresolved。

前一任务把“仍有七个持有者”直接归因于旧包没替换，证据不充分。本次日志证明新版本已生效，但适配器本身有错误。

`Sushi.ComponentSushiPersonBox` 所有槽位读写都转发至
`m_SubsystemSushiTotal.ComponentMiner.Inventory`。
前两个成员是字段，但引擎 `Game.ComponentMiner.Inventory` 是属性。旧适配器对最后一层调用
`GetField("Inventory")`，返回 null，条件访问没有抛异常，也没有成功映射日志，随后返回代理本身。
这使玩家背包和六个代理获得七个持有者身份，共享底层物品被反复误判为独立复制，最终耗尽编号。

相关组件来自用户安装的玲兰辅助/玲兰科技，不是已经适配的 Logistics.ComponentStorageUnit。
本次以用户 Mods 中的原包读取并使用引擎 GetDecipherStream 解包，只读加载其中 DLL。

## 修复与数据影响

- 将最后一层改为 `(miner as ComponentMiner)?.Inventory`，每次解析读取当前属性，不缓存背包对象。
- 无法解析时输出一次明确诊断，不再完全静默。
- 保留皮肤/计数器工作台结果日志；报价失效记录前后物品值、实例编号和修订号；事务 StateChanged 记录具体拒绝条件、库存修订号和持有者键。
- 模组版本更新为 1.0.3-preview。物品布局仍为 v5，记录 schema 沿用本次修改前的 5。
- 不迁移、回收、重排编号，不清空注册表，不修改玩家世界或已安装模组。
- 已有独立记录的枪可在满表时原位修改；新实例或真实独立复制仍需要空闲编号，满表时保持拒绝。
- 之前误分配所占用的容量、以及可能已被旧版复制隔离逻辑剥离的成长状态，本修复不会自动恢复。恢复需要可信存档证据，不能猜测。

## 回归结果

新增 `tools/PackageCheck/SushiInventoryRegression.cs`，可通过
`--sushi-inventory-mods <游戏 Mods 目录>` 加入包检查。没有以同名假 Sushi 类替代真实 DLL。

DLL SHA256：

- SushiTool.dll：`78c6984f244ab304c5320156fb74580da0adcba006f429cde2cbd9c502cfcec8`
- SushiBase.dll：`55d1a7c388bc9d4e96b6bc534428041700cc49418a854c979040d967c35e8355`

旧包 1.0.2-preview 在专项 14 项中失败 9 项：共享身份、共享修订号、无虚假分配、满表计数器/涂装、状态保持、背包重定向及两轮持久化验证。
其中满表计数器和涂装均实际返回 DuplicateUnresolved。

新包专项 14/14 通过，包括：玩家加六个真实代理归为一个持有者；不同槽位仍独立；210 次跨代理事务不新增 ID；
满表时原枪安装计数器与涂装成功；真实额外持有者仍被拒绝；引擎背包属性变化后代理跟随；
两轮 ValuesDictionary -> XML -> ValuesDictionary -> 注册表重读保留编号水位、19 发弹药、700 耐久、计数器及涂装。

分别在默认 API 检查宿主和用户本机 API 1.9.3.1 DLL 构建的宿主中验证，专项结果一致。
这是离线调用真实引擎类型和第三方组件的结果，未运行玩家世界/完整 UI。

报告：

- `output/sushi-inventory-before-1.0.2.json`
- `output/sushi-inventory-after-1.0.3.json`
- `output/sushi-inventory-api1931-1.0.3.json`

全套检查共 9749 项，新包通过 9498 项，失败 251 项；旧包同名失败已存在，本次没有新增失败名。
失败涉及 creative-counters、skin-growth、growth、generic-travel、survival、animation-switch、gun-skin、travel。
未通过全套验收，不应宣称为已全面验收的稳定版本。构建成功，git diff --check 通过。

## 交付与实机下一步

核心包：`output/ScCsgoKnives-1.0.3-preview.scmod`。

- 核心包 SHA256：`1e5d07232c1f0bfea2c480d2d6e6beeb16b0a024dce60db8eeaf86eec3ca561f`
- 包内 DLL SHA256：`c42dc235771cebd9050fa6400faedbb8e40e4adf25da14ba05a75676691541bd`
- 资源包 1.0.0 与用户 Mods 中现有包 SHA256 一致，无需替换。

使用原世界副本，替换核心包后检查 `1.0.3-preview initialized` 和
`[GUN_STORAGE] mapped Sushi.ComponentSushiPersonBox`，对原有枪复测射击、涂装和计数器。
如果仍报“状态变化”，本机已有日志路径可直接续查；请记录触发时间，以及选择的是原有枪还是新拿出的模板枪。
必要时需要发生问题的世界副本路径，以分析真实引用与记录；当前不需要再提供视频或第三方模组包。

剩余不确定项：现场旧版工作台 Trace 在 Release 被编译移除，因此不能仅凭本次 Game.log 证明用户所述每一次
StateChanged 都是代理问题。新包记录实际拒绝分支，下一次复现可据此区分报价失效、槽位变化和真实重复。

工作区保留大量前序未提交修改。本次没有将前序混合工作整体提交/推送，也没有修改游戏安装目录或世界存档。
