# 1.7.0 实现计划（feedback-1124-170）

设计与编号（D*/P*/Q*/R*）见 [../feedback-1124-170.md](../feedback-1124-170.md)。本计划写到“照着做、不用再做设计判断”：遇到 Q 项未定时按其“默认”实现，并在任务文档结果里注明；P 项全部做成具名常量或数据表字段。

## 0. 通用规则

- 分支 `feat/feedback-1124-170`（worktree `E:/projects/ScCsgoKnives-170`），基线 d921bad（#9）。#9 若先合并 main，开工前把本分支 rebase/merge 到新的 main，并记录。
- 每个步骤一个或几个 Conventional Commits，范围名：`shotgun`、`reload`、`tactical`、`l10n`、`economy`、`cases`、`release`。
- 开工前看 `git status --short`；只改本任务文件。
- 有混合 CRLF/LF 的文件（`ScAssemblyRecipesScreen`、`ScGunAttributes`、`ScGunAttributesScreen` 等）按字节改、保持原换行；改前用 `git ls-files --eol` 看。Windows 上传用 `wputb.py`/`wput_lf.py`，别用会转 CRLF 的 `wput.py`。
- 测试方法名用独立名字，不给 `GetMethod(name)` 查的方法加重载（AGENTS.md）。
- 自检可能在 BlocksManager 注册前运行；不要在静态初始化里解析方块编号。
- 迭代时只编译和跑相关自检，冻结候选后再跑全套阶段流水线。

## 第 0 步：兼容性摸底（先做，结果决定 Q7）

（独立 ScCsgoBox 未公开发布，不测共存，见 D20。）

1. 用真实发布包（1.4.0、1.5.x、1.6.0 的 Full/Lite，在 `output/history-*` 和发布记录里核对哈希）各做一次：1.7.0 候选存一个有箱子/钥匙（背包、箱子、掉落物）、有首次做枪记录、有举盾 C4 匪的世界 → 用旧版打开、存档 → 再用 1.7.0 打开。记录：箱子钥匙是否还在、方块编号是否被占、首次记录是否还在。
2. 把结果写进任务文档 Q7 下面；如果旧版会丢箱子，**停下来问用户**（Q7），其余不依赖这一点的步骤继续。

## 第 1 步：配件改名（D10）——`l10n`

- `src/ScCsgoKnives/Blocks/ScWeaponMaterialBlock.cs`：`Names` 改为语言键查找，例如 `public static string NameOf(int kind) => ScWeaponText.Get("Material"+kind, Fallback[kind])`，`Fallback = ["金属件","机械零件","握把","瞄准镜","涂装材料"]`；`GetDisplayName`/`DefaultDisplayName` 用它。`Names` 数组若被别处引用，保留为兼容只读（值改成新名）或全部改调用点（git grep `ScWeaponMaterialBlock.Names`）。
- `ScWeaponCrafting.Help`、`ScGunBlock.cs:204` 维修提示、`SubsystemTacticalBombs.cs:154` 拆弹奖励提示（改成按 `DefuseReward` 拼名字，别再写死数量和名字）、`ScWeaponRepair.cs:16` 注释。
- `Assets/Lang/zh-CN.json`、`en-US.json` 在 #9 新加的 `ScCsgoKnives/Items` 下加 `Material0..4`：zh 金属件/机械零件/握把/瞄准镜/涂装材料；en Metal Part / Mechanical Parts / Grip / Scope / Paint Supplies。
- 工具与断言：`tools/PackageCheck/SurvivalPackageIntegration.cs`、`tools/weapon_stats_report.py`、`tools/render_supply_review.py` 里的旧名改新名。
- 验收：`git grep -E "金属坯件|精密机构|握持组件|光学组件" -- src tools` 只剩注释或历史文档；WeaponPresentationCheck 的 LocaleChecks 加一条“5 种配件中英名都来自语言文件”。

## 第 2 步：铁用量（D11~D15）——`economy`

- `src/ScCsgoKnives/World/ScComponentCrafting.cs` `All`：
  - kind 0：`[("ironingot",4),("coalchunk",2)]`
  - kind 1：`[("ironingot",2),("copperingot",4),("germaniumchunk",2)]`（删掉 `sccsgomaterial:0`）
  - kind 2/3/4 不变。
- 武器配方 `ScWeaponCrafting.Build`、维修 `ScWeaponRepair.FullCost` **不动**（D14）。
- `tools/PackageCheck/CommunityRepairRegression.cs:117` 的期望配方表同步改；跑一遍 git grep `ironingot` 看有没有别的断言写死旧配方。
- 匪掉落（P6）：`src/ScCsgoTactical/ComponentTacticalEnemy.cs` `Died()` 第 389 行：概率 0.4→`MaterialDropChance=0.5f`，种类按 50/30/20（金属件 / 机械零件 / 握把或瞄准镜各半）；铁铜煤掉落（第 390 行）保留。
- 空投（P7）：`SubsystemScAirdrops.RewardsFor` 在随机资源前固定加 `(金属件, 2~3)` 和 `(机械零件, 1)`；注意它说明里“按投放 ID 固定”——用同一个 `r` 继续取随机数，并在注释写明 1.7.0 起表变了（飞行中的旧空投落地时按新表）。确认总格数 ≤ 箱子格数（现在最多约 11 格，加 2 格后仍小于 16）。
- 钥匙（D15）在第 6 步做。
- 验收：新增自检 `economy/blank-recipe-4-iron`、`economy/mechanism-no-blank`、`economy/ak-iron-total-34`（用 `ScWeaponCrafting.All` × `ScComponentCrafting.All` 算出每把枪的铁锭总数，断言表中五个值：14/22/34/42/48）；空投自检断言每个投放 ID 都有金属件和机械零件、格数不超。

## 第 3 步：散弹枪射程/衰减/击退（D1~D4）——`shotgun`

- `docs/gun-handling-adjustments-170.json`（新）：`{"Version":1,"Guns":{"nova":{"RangeScale":1.5,"FalloffStartScale":1.5,"MidDistanceScale":1.5,"FalloffFloor":0.3},…xm1014/mag7/sawedoff 同}}`（P2）。
- `tools/generate_gun_handling.py`：读完 proposal 后应用调整表（只这四把），写入 json 时 `Range` 取整到 0.5 格；在输出打印“applied 1.7.0 adjustments: 4 guns”。
- `tools/verify_gun_handling_release.py:29`：比较前对 proposal 应用同一张调整表（共用一个函数），别放宽断言。
- 重新生成 `src/ScCsgoKnives/AnimationData/gun_handling.json`：Nova 36 / XM1014 33 / MAG-7 30 / 截短 27 格，`FalloffStart` ×1.5，`MidDistance` ×1.5，`FalloffFloor` 0.3。
- `ScGunHandling.Falloff` 的上限校验 `g.Range<=128` 不受影响。
- `src/ScCsgoKnives/World/ScSurvivalBalance.cs:100`（手感关闭路径）：`Pellets>1` 分支改为 7.5 格内 1、7.5→22.5 线性到 0.4、22.5→37.5 线性到 0.3（用具名常量 `ShotgunFullTo/ShotgunMidAt/ShotgunEndAt/ShotgunFloor`）。核对手感关闭时散弹枪的最大射程从哪来（`GunSpec.RangeBlocks`，`GunSpec.cs:76/282` 附近），同样 ×1.5。
- 只改生存（Q2 默认）：确认死斗 `DmWeapons`（用 CS2 原始 `m_flRange`/`m_nDamage`）不读这两处；`ComponentTacticalEnemy.cs:326`、`ComponentTacticalCompanion.cs:230` 用的 `stats.Falloff(spec,…)` 会读 `ScGunHandling`——**敌人和同伴的散弹枪也会跟着变**。按 Q2“敌方 NPC 散弹枪不变”：给 `EffectiveGunStats.Falloff` 加一个 `npc` 参数（新方法名 `NpcFalloff`，不要重载），NPC 路径用调整前的数值（从 proposal 原值另存一份 `BaseRange/BaseFalloff*` 字段，生成器一起写出）。同伴算玩家一方，跟玩家一样用新值。
- 击退（D4/P3）：`ScSurvivalBalance.AttackWith` 第 126 行：`eligible && !melee && spec.Pellets>1 && 距离<=ShotgunKnockbackRange(6) && !ScProjectileDefense.Protects(body) && body.Mass<ShotgunKnockbackMassCap(300)` 时 `ImpulseFactor=ShotgunKnockbackImpulse(1.5)`，否则维持 0.6。距离用 `player` 眼睛到 `point`；`spec` 从 `credit?.Variant`/手持值取（同第 115 行的逻辑）。`ScProjectileDefense` 里若没有“是否保护”的查询，加一个只读的 `Protects(ComponentBody)`。
- `ScGunAttributes.cs:117/119` 的显示（衰减起点/末端）自动读新值；`ScGunAttributesScreen.cs:279` 文案不变。
- 验收自检：`shotgun/range-x1.5`（四把 Lv0 射程 36/33/30/27）、`shotgun/floor-30`（射程末端 `Falloff==0.3`，两条路径）、`shotgun/total-power-unchanged`（Σ PelletPower 在 0 距离时与 1.6.0 相同）、`shotgun/npc-unchanged`（敌人 Nova 在 20 格的衰减与 1.6.0 相同）、`shotgun/knockback-close-only`（5 格 1.5、10 格 0.6、首领 0.6）、`deathmatch/shotgun-unchanged`。

## 第 4 步：管式换弹每循环多发（D5）——`reload`

- `SubsystemScGunBlockBehavior.BeginReload`（约 2060~2113 行）：
  - `int perLoop = Math.Max(1, (capacity + spec.Magazine - 1) / spec.Magazine);`（`spec.Magazine` 是基础容量）
  - `int loops = (shells + perLoop - 1) / perLoop;`
  - 时长 `KnifeAnimationController.ReloadSeconds(variant, empty, loops)`；动画 `TriggerReload(player, empty, loops)`；`ScheduleLooped(..., loops)`。
  - `ShellTimes` 改成每个循环一项，并记录这个循环装几发：新增 `state.ShellBatch`（`List<int>`，与 `ShellTimes` 同步增删），第 k 个循环 = `min(perLoop, shells − k×perLoop)`。
- 第 1361~1370 行：每个到点的循环调用 `state.Reload.InsertShells(n)`（新方法，内部循环调 `InsertShell()`，任何一发失败就停止并返回已装数，`LastResult` 保持第一处失败原因）；成功后更新 `value/rounds`。
- `KnifeAnimationController.FinishReloadEarly` 返回的是“已装循环数”，调用方（grep `FinishReloadEarly`）把剩余的 `ShellTimes/ShellBatch` 截到同样的循环数。
- 弹药扣除不变（`ScReloadTransaction.RequiredFor(spec, capacity)` / 霰弹按发扣）：确认 `InsertShell` 是按发扣霰弹，多发循环不改变总消耗。
- 联机：服务器按同样的 `perLoop` 算；客户端只显示（`ScNetGuns.ShownRounds`）——客户端预测的动画循环数要用同一公式，放到一个静态函数 `ScReloadTransaction.ShellsPerLoop(spec, capacity)` 两边共用。
- 验收自检：`reload/tube-per-loop`（Nova Lv0 每循环 1 发；Lv10 容量 12 → 每循环 2 发；Lv50 容量 52 → 每循环 7 发、8 个循环；空仓装满的时长与 Lv0 相差 ≤ 1 个循环）、`reload/interrupt-keeps-loaded`（第 3 个循环后开火：装入 = 3×perLoop，弹药扣除对应）、`reload/ammo-short`（只有 5 发霰弹时只装 5 发）、`reload/mag7-unchanged`。现有 `Cs2SelfTest` 的 `gun/{gun}/reload.seconds`（基础容量满装时长）应保持通过。
- 需要进游戏看：Lv10/Lv50 Nova 空仓装填动画和声音是否连贯（1.9.3.1 隔离副本，静音不影响检查声音事件的日志）。

## 第 5 步：防爆盾伤害 ×2 与 C4 匪举盾（D6/D7/Q1）——`tactical`

- `src/ScCsgoTactical/TacticalBlocks.cs`：`ScTacticalShieldBlock` 加 `public const int DamageScale=2;`，`Life` 不动。
- `ScShieldProtection.Spend` 第 29 行：`Wear+Math.Clamp(ceil(damage*DamageScale),1,Life)`。
- 敌人举盾（R2/R3）：
  - `TacticalEnemyState`：新增 `public int ShieldWear {get;set;}`（缺省 0）；`[JsonIgnore] public bool ShieldUp => Role==TacticalRole.Demolition && Bomb && ShieldWear<ScTacticalShieldBlock.Life;`（Q1 默认：放包后 `Bomb=false` → 收盾；盾磨满 → 收盾，换回原手枪 `Variant`）。`Decode` 加校验：`ShieldWear` 在 0..Life，非 Demolition 必须为 0；不改 `Schema`（新增字段旧版会忽略，见 R2）。
  - `ComponentTacticalEnemy.PresentedValue`（第 71 行）：投掷/放包优先，其次 `State.ShieldUp` → 防爆盾方块值（data = ShieldWear），否则 `DisplayValue`。
  - `ComponentTacticalEnemy` 战斗：`ShieldUp` 时不开枪（第 296~300 行开火段前 `if(State.ShieldUp)return;`，但走路和放包逻辑照常），朝向目标（已有转向）。不扔手雷（`TryGrenade` 前同样返回）。
  - `ScShieldProtection.Holding`：除 `IInventory` 外，`body.Entity.FindComponent<ComponentTacticalEnemy>()?.State is {ShieldUp:true}` 也算举盾；`Filter` 里命中敌人盾时走新方法 `SpendEnemy(ComponentTacticalEnemy, damage)`：只在 `ScNet.IsAuthority` 时 `State.ShieldWear += ceil(damage*DamageScale)`；客户端不写。命中音效复用。
  - `ComponentTacticalModel.cs:205~221`：敌人分支里 `HeldValue` 是盾时，用 `ScShieldProtection.Pose(body)` 画（复用玩家同伴持盾那段，抽成一个 `DrawShield(camera,value)`）。
  - 联机：确认敌人快照（`TacticalNet`/`SubsystemTacticalEnemies` 的广播）带的是 `PresentedValue` 或足以推出它的字段；若只带 `Variant/Bomb`，加 `ShieldWear`（读写两端长度上限一起改）。
- 验收自检：`shield/damage-x2`（磨损 0 的盾挨 100 伤害 → 200）、`shield/old-wear-kept`（磨损 1500 的盾读档后不坏，挨 100 → 1700，再挨 150 → 2000 坏）、`shield/enemy-blocks-front`（正面子弹被挡、背后不挡）、`shield/enemy-wear-server-only`、`enemy-state/decode-shield-wear-range`、`enemy-state/old-json-demolition-gets-shield`、`enemy-state/1.6.0-roundtrip-loses-only-wear`（用 1.6.0 包的 DLL 反序列化再序列化）。
- 需要进游戏看：五人队 C4 匪举盾推进、放包、收盾掏枪；第三人称盾位置；联机客户端看到盾（1.9.3.2_MP）。

## 第 6 步：敌方 C4 爆炸清掉落物、毁空投物资（D8/D9/P4/P5/Q3）——`tactical`

- 只改 `SubsystemTacticalBombs.Explode`（Q3：玩家 C4 `SubsystemScC4` 不变）。只在服务器（`ScNet.IsAuthority`）执行。
- 玩家死亡点记录（P5）：新类 `TacticalDeathMarks`（放 `SubsystemTacticalBombs` 或新子系统），玩家 `ComponentHealth.Health` 首次 ≤0 时记 `(位置, GameTime)`；存进本子系统的存档组（`Marks` 列表，最多 32 条，超过 15 游戏分钟的丢弃），读档时校验数值有限。
- 清掉落物：`SubsystemPickables.Pickables` 中，距离 ≤ `c.Radius`、并且从炸点到物品的地形射线不被挡（与伤人同一条 `terrain.Raycast(..., ScGunRange.TerrainStopsBullet)`），并且：
  - 不在任何死亡标记 4 格内、15 分钟内；
  - 不是 CS 枪/刀（`ScGunBlock`/`ScKnifeBlock` 的值——带实例记录，不能无记录地销毁；这类物品敌人不会掉，一般是玩家的）；
  - 满足以上就 `pickable.ToRemove = true`（用引擎原生的删除方式，联机自然同步）。
- 空投箱（P4）：在 `Radius` 内扫描 `ScAirdropBlock` 的方块实体（`SubsystemBlockEntities`，按炸点周围的格子扫，半径取整），对 `ComponentChest` 每格 `RemoveSlotItems(slot, count/2)`（向下取整；只剩 1 个的格子用炸弹 ID 做种子的随机决定留或毁，保证确定性）；CS 枪格子按 FreshFull 模板值可以删，有实例 ID 的枪格子跳过。改完调 `ScNetSlots.Touched`/`ScInventoryTransaction.Changed`。
- 给附近玩家一条提示：“C4 炸毁了附近的物资”（语言文件）。
- 验收自检：`c4-blast/clears-loot-in-radius`、`c4-blast/keeps-behind-wall`、`c4-blast/keeps-player-death-drop`（含存档重进后仍受保护）、`c4-blast/keeps-gun-with-record`、`c4-blast/airdrop-halves-deterministic`（同一炸弹 ID 两次结果相同）、`c4-blast/player-c4-unchanged`、`c4-blast/client-does-nothing`。

## 第 7 步：开箱并入主包（D16~D21, Q4/Q5）——`cases`

来源：`zh667/ScCsgoBox` 0.3.5（`6af872a`）。

1. **代码迁入** `src/ScCsgoKnives/Cases/`：`CaseModels.cs`、`CaseRegistry.cs`、`CaseRoller.cs`；`Blocks/ScCaseBlock.cs`、`Blocks/ScCaseKeyBlock.cs`；`UI/CaseOpeningDialog.cs`、`CircleWidget.cs`、`RarityStyle.cs`、`RewardCardWidget.cs`、`RotatingBlockIconWidget.cs`（放 `src/ScCsgoKnives/Cases/UI/`）；`World/SubsystemScCaseBlockBehavior.cs`。类名、命名空间 `Game` 不变（D20）。检查与现有类重名（git grep 每个类名）。
2. **资源**：`Assets/Models/ScCsgoBox/csgo_box.obj` 及其贴图、`Assets/ScCsgoBox/Cases/survivor.json`、`Assets/ScCsgoBox/Integrations/survivalcraft-stash.json`，路径保持 `Assets/ScCsgoBox/...`（联动扫描路径不变，第三方联动文件继续生效）。语言条目并入 `Assets/Lang/*.json` 的 `ScCsgoBox` 分类（键名不变）。
3. **方块与模板**：`ScCsgoBoxBlocksData.csv` 两行追加到 `ScCsgoKnivesBlocksData.csv`，`DefaultCategory` 改 `ScCsgoWeapons`，`CraftingId` 保持 `sccase`/`sccasekey`；`ScCsgoBoxDatabase.xdb` 的 `MemberSubsystemTemplate Name="ScCaseBlockBehavior" Guid="35da8f9b-99d6-48a9-91fe-29c37a6ed1f5"` 原样并入主包 xdb；`ScCompatibility.RegisterOwned("Subsystem","ScCaseBlockBehavior")`。
4. **配方**（D15/Q4 默认）：钥匙 `3 铜锭 → 1 钥匙`（九宫格，形状照独立版去掉铁）；箱子保留 `4 木板 + 4 铜锭 + 1 木箱`。写进主包的 `.cr`（或 `GetProceduralCraftingRecipes`，看主包现在用哪种）。
5. **ModLoader 合并**（R5）：把独立版 `OnCreatureDied/OnLevelUpdate/OnMinerDig/OnBlockDug/OnLoadingFinished` 的逻辑搬到 `ScCsgoKnivesModLoader`，业务放 `Cases/CaseAcquisition.cs`，加载器只转调。注意 1.6.0 修过的“其他模组打断 OnLoadingFinished 循环”（主菜单第一帧补做），箱子注册也走同一个补做入口。
6. **奖池**（D17）：`survivor` 箱（data 0）保留；品质映射：common=普通、uncommon=稀有、rare=史诗、epic=传说、legendary=金色，权重 70/20/7/2.5/0.5 不变。新增代码提供的奖励源 `Cases/CsCaseRewards.cs`，在 `CaseRegistry.Reload` 后追加：
   - 普通：金属件 2~3、机械零件 1、通用弹匣 ×2、霰弹 ×12、随机手雷 1；
   - 稀有：握把 1、瞄准镜 1、涂装材料 1~2、拆弹钳（仅探员包可用时）；
   - 史诗：随机枪械涂装模板（`ScGunSkinTemplateBlock.GetCreativeValues()`，在开箱时取，Lite/Full 都只取 `ScGunSkinCatalog.Available`）、击杀计数器模板；
   - 传说：枪，开箱那一刻按玩家等级过滤 `ScWeaponCrafting.RequiredPlayerLevel(name) <= (int)player.PlayerData.Level`，值用 `GunSpec.WithId(variant, GunSpec.FreshFull)`（同空投，首次使用时分配记录）；电击枪不进奖池；
   - 金色：刀（`ScMinimalEdition.KnifeAvailable` 过滤，原厂外观）。
   - 原版奖励按原 tier 并在对应档里。等级过滤后某档为空时，该档按权重并入下一个较低档，抽奖结果不能为空。
7. **获取**（D18）：`CaseAcquisition`：
   - `OnCreatureDied`：只算 `injury.AttackerPlayer` 非空、非创造；目标是敌方 T（通过探员包注册的判定 `ScCaseHooks.IsHostileAgent`，探员包没装时为假）→ 15%，其他非玩家生物 → 3%（原 6% 改为 3%）；
   - 空投：`SubsystemScAirdrops.RewardsFor` 固定加 1 个箱子（探员包里引用主包的 `ScCaseBlock` 值，Lite/Full 都有）；
   - 拆弹：`SubsystemTacticalBombs.DefuseReward` 加 `(箱子,1)`，走现有 `Recovery.Grant`（与现在的拆弹奖励一起，存档原子性不变）；
   - 升级：每级 1 个（原 5），读档不补发（沿用独立版内存记录，R7）；
   - 坟墓/石堆：照搬。
8. **联机**（Q5 默认）：`ScNet.IsMultiplayer` 时开箱提示“联机暂不支持开箱”，不消耗；掉落、拾取、存放照常。
9. **许可**（D21）：`LICENSES/Reclizer-CsgoBox.txt`（从独立仓库拷），`THIRD_PARTY_NOTICES.md` 增加一节；独立版 `ASSET_SOURCES.md` 里的模型来源并入主包对应记录。
10. **Full/Lite 归属**：代码在核心程序集，两版都有；检查 `SC_SPLIT` 条件编译与 `ScMinimalEdition` 不把箱子带进 Mini；打包脚本（`tools/followup_140.py` 等）把新资源成员加入两版的成员清单。
- 验收自检：`cases/registered-names-match-standalone`、`cases/survivor-weights`、`cases/legend-level-filter`（1 级玩家 1000 次抽传说档不出 AWP；18 级可以出）、`cases/empty-tier-falls-back`、`cases/no-taser`、`cases/drop-rates`（注入随机源：匪 15%、狼 3%、创造 0、非玩家击杀 0）、`cases/airdrop-has-case`、`cases/defuse-grants-case`、`cases/level-up-one-each`（升 3 级给 3 个；读档后第一次更新不发）、`cases/key-copper-only`、`cases/stash-integration-still-loads`、`cases/multiplayer-refuses-open-without-consuming`、`cases/standalone-present-disables`。
- 需要进游戏看：开箱滚动界面在 1.9.3.1 电脑端和手机竖屏是否正常（从独立版移植后 UI 父容器可能不同）。

## 第 8 步：首次做枪奖励（D22~D28）——`cases`

- 新文件 `src/ScCsgoKnives/Cases/ScFirstCraftLedger.cs`：
  - 数据：`Dictionary<string owner, HashSet<string gunName>>`；`owner` 用 `ScGunHolders.RecoveryOwner(project, inventory)` 同一套玩家身份（和拆弹奖励一致，联机下按玩家区分）。
  - 新子系统 `SubsystemScFirstCraft`（xdb 新模板，新 GUID），`Load/Save` 写 `Project.xml` 组 `ScFirstCraft`：`Schema=1`、每个玩家一行“枪名列表”；名字必须在 `GunSpec.All` 里，未知名字原样保留不删（前向兼容）。
  - 世界文件夹副本 `ScCsgoFirstCraft.xml`：每次保存写（临时文件 + 校验后替换，旧的留 `.bak`），带世界种子；只在 `Project.xml` 没有 `ScFirstCraft` 组时读取，并核对种子（照 `ScGunRecordMirror` 的规则，不合并、不双向）。这是用户 2026-11-24 对“世界文件夹副本”的同意，写进 `docs/agent-guide/compatibility.md` 的例外条目。
- 触发点：`SubsystemScWeaponWorkbench.cs:128` 武器条目 `TryCraft` 返回成功且 `entry.Knife==false`、非创造、`ScNet.IsAuthority` 时：`if(ledger.TryMark(owner, entry.Name))` → 发 1 个箱子（D25 默认：先尝试放背包，放不下用 `SubsystemPickables.AddPickable` 掉在玩家脚边，只掉一次）；标记和发放在同一帧，保证一个存档要么都有要么都没有。Q6 若用户改选“存着等有空位”，换成 `registry.Recovery.Grant(owner, [(箱子,1)], "first craft "+name)`。
- 批量制作（`ScCraftBatch`）一次做多把同型号只给 1 个；一次做成功但数量为 0 的不给。
- 联机：客户端点装配台是发请求给服务器（`ScNetWorkbench`），只在服务器上执行上面的逻辑；客户端不写账本。
- 1.7.0 之前做过的枪不预填（D23），账本从空开始。
- 提示：“首次制作 {枪名}：获得 1 个武器箱”（语言文件）。
- 验收自检（D28 全覆盖）：
  - `first-craft/once-per-gun`（同一玩家做两次 AK 只给 1 个；做 M4 再给 1 个）；
  - `first-craft/save-reload-no-repeat`（标记后存档→读档→再做不给；用真实的子系统 Save/Load 往返两次）；
  - `first-craft/two-players-separate`；
  - `first-craft/cancel-or-fail-none`（材料不足、取消对话框、`TryCraft` 抛异常各一例）；
  - `first-craft/inventory-full-drops-once`（背包满：脚边 1 个掉落物；同一帧重复回调不再掉）；
  - `first-craft/uninstall-reinstall-keeps`（把 `ScFirstCraft` 组从 Project.xml 删掉模拟无 CS 存档，再读：从世界文件夹副本恢复，种子不符时不恢复）；
  - `first-craft/creative-none`、`first-craft/knife-none`、`first-craft/client-does-not-write`。
- 需要进游戏看：1.9.3.1 隔离副本真退出到主菜单再进世界（不是只调 Save/Load），再做同一把枪不给。

## 第 9 步：版本与文档——`release`

- `src/ScCsgoKnives/modinfo.json`、`src/ScCsgoTactical/modinfo.json`、`src/ScCsgoDeathmatch/modinfo.json` 版本 1.7.0；外观包/语音包/资源前置包不改。
- 玩家可见更新说明（发布记录里），技术修订号不放进物品名。
- `ScGunAttributes.MaxLevelSummary` 等说明文字里提到配件名或铁的地方同步。
- 任务文档第 5 节填结果。

## 第 10 步：验证与打包（照 1.6.0）

1. 迭代中：`./tools/dev.ps1 dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release -p:SkipScmodPackaging=true` 和受影响的 PackageCheck 子集。
2. 冻结候选：`./tools/dev.ps1 python -X utf8 tools/deathmatch_140.py r170a prepare,build,clips,appearance,package,dmbuild,dmpackage,baseline,gates`（阶段名每次换新：r170a、r170b…，旧阶段被取代后按 AGENTS.md 清理）。
3. 发布关卡：`tools/api_engine_checks.py <tag>`（1.9.3.1/1.9.4/1.9.5/1.9.5.1）；存档互切矩阵对 1.4.0、1.5.x、1.6.0 实际发布包（含第 0 步的新物品场景）；探员完整套件（已知 4 项失败保持不变）；主套件（已知 3 个 smoke-view 失败保持不变）；WeaponPresentationCheck 本地化 952+ 项。
4. 实机（1.9.3.1 隔离副本、静音）：第 4 步换弹、第 5 步举盾匪、第 6 步 C4 清物资、第 7 步开箱界面、第 8 步退出重进。联机（1.9.3.2_MP）：举盾匪客户端显示、C4 清物资同步、首次做枪房主发放、联机开箱提示。
5. 全部通过后按常设授权替换 `output/` 同名包、更新清单哈希，旧包移 `output/history-1.6.0/`；清理中间产物并写清理回执。**不合并 main、不发 Release**，等用户同意。

## 交付给用户的观察点（候选包）

1. Lv1 和 Lv10 以上 Nova：远距离还能打中，末端伤害明显比以前高；空仓装填时间接近 1 级。
2. 五人 T 队的 C4 匪举盾推进；正面打盾、绕后打人；放完包后收盾。
3. 敌方 C4 爆炸后，附近掉落物被清，自己死的地方的东西还在；空投箱里少了一半左右。
4. 背包和装配台里看到新配件名；做一把 AK 的铁锭明显减少。
5. 第一次做一把新枪得到 1 个武器箱，退出重进再做不再给；开箱界面正常，低等级开不出高级枪。
