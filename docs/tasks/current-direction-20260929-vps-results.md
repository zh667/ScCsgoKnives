# current-direction-20260929 VPS 结果：数值护甲、敌队三配置、自然遭遇、提速

规划入口：[current-direction-20260929.md](current-direction-20260929.md)（启动提示 [vps-prompt](current-direction-20260929-vps-prompt.md)）。
本文件只写本轮实际做了什么、实际执行了什么、还缺什么。状态分四类：**代码已实现**、**测试实际执行**（runner + 结果 + 输入哈希）、**人工审阅**、**实机验收**。

## 0. 输入、基线与写入者

- 正式基线 = c08（output 1.4.0）：全量 526,629,508 `538a74e4…a8844d`；轻量 35,519,997 `de4dd5e9…79df`；探员 38,451,834 `03d9ac7d…8454`。开工时 Windows output 与之一致（job `4df3b12e…`）。
- 已安装 Mods 只有 c08 全量（`538a74e4…`，用户自装；job `2a47983a…`），没有 c09/c10 → 没有玩家存档含未发布的物品护甲，不需要物品→数值迁移。
- 单写入者：Windows 本轮只改 docs；src/tools 只由 VPS 写。每次提交前 Syncthing 空闲、无错误/冲突；Windows job 先按 SHA-256 核对全部近 3 天改动的 src/tools 文件（271 个）再运行。独立 Zeus 等未提交修改未触碰（Windows `git status` 116 项保持原样）。

## 1. 代码已实现

### 1.1 玩家数值防护（取代 c10 物品护甲）

- `src/ScCsgoKnives/World/ScArmor.cs`：`ScArmorRules`（头部吸收 60% / 容量 100；躯干+手臂吸收 50% / 容量 150；腿不保护；每次命中按吸收量向上取整扣容量）、`ScArmorPiece`/`ScArmorState`（Owned/Left/Capacity，文本 `1|躯干|头部`）、新子系统 **`SubsystemScArmor`**（项目模板名 `ScArmor`，GUID `e29cebcf-88c6-4370-a759-3de4687ee72b`，写入 `ScCsgoKnivesDatabase.xdb` 与兼容清单）。
  - 键：玩家 `player-<世界玩家序号>`；敌人 `enemy-<小队>-<职业>`。只按世界保存，换世界/读档不补满。
  - 读不懂的条目原样保留、不覆盖；Schema≠1 拒绝加载（不改原数据）。
- 工作台 **“防护装备”**（`ScArmorWorkbench.cs`，“装备”分类，一个入口）：标题显示“无甲/半甲/全甲 · 头部 x/100 · 躯干 y/150（某部位已耗尽）”，说明项写明规则与材料。只提供三种合法变化：无甲→背心（半甲）、无甲→全甲套餐（一次提交两项）、半甲→加装头部；有缺损时维修到满。不能只做头盔。人物等级 3；背心 铁8/铜4/帆布6/皮革4，头部 6/2/2/2，全甲 14/6/8/6；维修 = 制作材料 ×40%×缺损比例，每部位每种材料向上取整。报价冻结当前状态，提交时状态或材料不符则一律不扣；部分扣除会回滚。创造模式免费。不发物品、不占衣物栏、无外观附件。
- 伤害顺序（`ScSurvivalBalance.BulletAttack.CalculateInjuryAmount`）：只对带部位的 CS 枪弹：先逐部位结算 CS 防护，再对剩余威力调用一次原生流程（原版/第三方衣物、抗性系数、AttackResilience），结果缓存，重复查询不再扣耐久或出声。非 CS 攻击完全不经过 CS 防护（原 25% 提案不保留）。创造模式玩家不消耗防护。
- 声音（H4 逻辑沿用，数据改为数值防护）：头部实际被吸收→头盔音；头部未被吸收且掉血→裸头音；躯干/手臂实际吸收→kevlar；每发每目标一次；护盾挡下/无效命中不出声。
- 死亡：`DeadBeforeDrops` 钩子；这次死亡掉落背包时清零玩家防护（无掉落物），世界/模组设为保留背包时保留防护。
- 伙伴：本轮不提供伙伴防护（规划为可选）；c10 的伙伴 7 格记录和面板护具栏已完全退回 c08（反编译对比 c08 DLL：`TacticalPanel`、`ComponentTacticalCompanion` 0 差异；`SubsystemTacticalCompanions` 仅 `[4..]` 编译器差异）。

### 1.2 敌人三配置

- `SubsystemTacticalEnemies.DrawArmor`：每名新敌人独立抽一次，无甲/半甲/全甲各 1/3，满值；同队可混合，不强制凑齐，不按职业，不生成仅头盔。自然队与手动挑战队同一路径（`CreateSquad`）。
- 只在创建时写入；已有敌人没有条目 = 无防护（不按 T 外观回填）。键由小队+职业决定（同队职业不重复），睡眠/唤醒/存读只要敌人状态在，防护值就在；不再有 `|SCT_ARMOR1:` 段。
- 死亡删除其防护，不掉任何护甲物品；原枪/材料/奖励逻辑未改。全甲打到躯干 0、头部仍有余额时保留真实余额，显示“全甲，躯干防护已耗尽”。
- 清理：每 10 分钟（载入后 5 秒首次）删除世界里既不活着、也不在任何睡眠记录中的敌人条目；任一睡眠记录读不懂则整轮不删。

### 1.3 撤下的物品护甲路线

- 删除 `ScArmorBlock.cs`、`ScArmorCrafting.cs`、`ComponentTacticalArmor.cs`、`tools/build_armor_assets.py`；`ScTactical.xdb` 去掉 3 个 `TacticalArmor`（结果与 c08 包内字节一致）；兼容清单去掉 `TacticalArmor` 组件，加入 `ScArmor` 子系统。
- c10 派生资源（2 glb + 5 png + 5 webp）移出源码树到 `E:/projects/ScCsgoKnives/.tmp/archive/armor-item-c10-20260929/`（含 README、逐文件哈希、工具副本；job `f6bea8d6…`），未删除，CS2 原始资源未动。`completion_140` 不再打包护甲资源记录。

### 1.4 自然遭遇

原因（引擎 1.9.3.1 反编译）：原生每 60 秒（冬季 120 秒）一次随机刷新，候选点在每轴 ±24–48（水平 34–68 格）；距所有视角 60 格以外的生物立即休眠；小队出生后原地不动，而索敌只有 32/36 格。固定种子模拟里 200 个原生候选有 25 个在 60 格外（旧逻辑出生即睡），其余大多在索敌圈外。

改为：原生仍决定**是否**刷（周期、人口上限、加权抽签、Slower 均不绕过，不删生物腾位）；CS 只决定**在哪**：
- 在最近玩家 32–44 格找整队可站位置（雪/草/树冠下地面照旧）：先用原生候选方向，再按侧方、前斜、后斜顺序；优先看不见的位置（视锥外或被地形挡住）。
- 实在只有视野内位置时至少 32 格，原生 2 秒渐显 + 3 秒警告（不累计瞄准）。
- 队员距玩家 33–46 格，永远在 60 格休眠圈内。
- 出现后沿玩家当时所在方向走一段，停在离该点 18 格（不行则 22/26 格）的可站点作为巡逻家；不实时追踪、不瞬移。
- 有界事件记录（最近 16 队）：出生距离/是否在视野内/是否巡逻、首次看见/听见/开火、未遇即休眠；每项每队只记一次，2 分钟状态行附汇总；设置页文字同步更新。

### 1.5 提速（第 3 节要求）

- **VPS 本地快速回归**：`TacticalEnemyRegression`、`C4Regression` 可在 VPS 用本地构建的 DLL 直接跑，约 2 秒一轮（需要包内资源/内嵌动画数据的用例在 VPS 必然失败，以 Windows 结果为准）。本轮 c11 的 7 个失败全部在本地复现并修好后才提交下一轮。
- `tools/completion_140.py`：
  - clips 烘焙按完整输入哈希复用（优先缓存，其次同输入的早期阶段；最多两份）；baseline 同样按包+测试源码哈希复用。
  - 构建/打包等前置步骤失败即停，后续步骤不再运行。
  - 新增 `ai` 快速门禁、`compat` 门禁（1.0/1.2/1.3.0 家族矩阵、原生钩子、轻量世界资源、库存、全/轻量集成矩阵）、核心数据库成员替换。
- 实测墙钟：c08/c10 全流程 7.14/7.20 分钟；本轮 c11（构建+AI）1.42 分钟、c12（构建+AI+C4+main+兼容）2.96 分钟，clips 未重烘（c10 已证明重烘与 c08 字节一致，且输入未变）。

## 2. 测试实际执行

| 轮次 | job | 步骤 | 结果 |
|---|---|---|---|
| c11 | `22975f715f6147cb8cb873487cffb426` | prepare→ai | 7/64 失败，原因见下；已修复 |
| c12 | `23a9f3511b7d4ebf95f6b133403d0824` | prepare,build,clips(复用),appearance,package,ai,c4,main,compat | 全部通过：ai 全量/轻量 64/64；c4 全量/轻量 73/73；main 全量 13911、轻量 14214 全过；family 246/246；native-hooks、native-lite、integration ×6、inventory 通过 |
| c13 | `65391ef55d3e4ea698009a551aca6882` | 最终候选：prepare,build,clips(复用),appearance,package,gates,compat,motion,throw,hotspots,ui | 全部通过（4.48 分钟）：ai 64/64 ×2；split core/agents；native-full；vf 292/292 ×2；tactical 105 项仅 4 个既有已知失败（native-gltf/ct、native-gltf/t、npc-full-registry-reload-refuses-without-ammo-loss、optional-package-identity-and-no-bundled-engine，与 c08/c10 相同），无其他；c4 73/73 ×2；main 13911/14214 全过；family 246/246；native-hooks、native-lite、integration ×6、inventory；motion、throw（0 失败）、hotspots、ui 通过 |

c11 失败原因：4 个护甲测试是测试写法问题（`dynamic` 调用把 `object` 参数按 object 绑定）；2 个自然旧测试夹具只有一格地面，新搜索要求整队落脚；遭遇链测试的夹具玩家缺生命组件；另外发现并修复了一个真实缺陷：放置结果按“点+时间”缓存，同一帧世界变化后会用旧结果（改为每次重新搜索）。

新用例（c12 实际执行，明细见 `.tmp/completion-140-20260929/c12/*.json`）：
- `armor-values-configurations-costs-and-encoding`：三配置、编码、非法文本拒绝、操作矩阵、制作/维修报价。
- `armor-workbench-commit-takes-every-material-or-nothing`：材料不足不扣、全甲套餐一次扣 14/6/8/6、过期报价拒绝、维修按报价扣、部分扣除回滚、创造免费、半甲→全甲。
- `armor-store-saves-keeps-unreadable-refuses-future-and-death-rule`：两轮存读一致、未知条目原样保留、Schema 2 拒绝、死亡规则两种情况。
- `armor-regions-once-sounds-and-native-clothing-after`：混合命中只结算一次（0.19，头 100→94，躯干 150→145）、各种声音情形、护盾不扣不响、旧敌人无防护、玩家衣物在 CS 之后只结算一次（0.152）、抗性系数只除一次（0.8/1.25/2）、近战不经过 CS 防护、创造不消耗。
- `enemy-armor-drawn-once-…`：固定种子可重现；3000 次抽取 942/1025/1033，仅头盔 0；手动队（5,5,3）种子 7：`1100022221021`，种子 8：`2220222112100`；受损全甲经睡眠→唤醒→两轮存读不变、不重抽、死亡终止。清理用例在 VPS 本地已通过，c13 首次在 Windows 执行。
- `natural-placement-band-cover-view-patrol-and-refusals`：视野内候选移到侧方 36 格、巡逻到 18 格；有遮挡时保留原方向；只剩视野内时 36 格 + 3 秒警告；全水拒绝并分项计数；落脚规则；原生候选 25/200 在 60 格外；队员 33.3–45.6 格；夹具中巡逻家处 0.3 秒看见、0.8 秒开火（夹具无物理，队员直接放在巡逻家）；日志与汇总。
- C4：`plant-floor-real-terrain/{ground,snow,half-slab,stairs-low,stairs-high,ledge}`（引擎真实射线与方块碰撞盒；雪层抬高到雪面；台阶两级；悬空边缘拒绝安放）、`plant-one-clock/pause/{60,30}fps`（游戏暂停 1.5 秒期间不前进、不提交）。
- 兼容：`{0,1,3}/protection-values-survive-older-reader-two-rounds` —— 1.0.0、1.2.0 容量兼容版与已交付 1.3.0 核心各走两轮“旧版读→旧版存（丢掉未知子系统组）→兼容胶囊恢复→新版读”，数值与原 XML 完全一致。1.3.0 核心取自 output `[API1.9]CS武器1.3.0-全量包.scmod`（哈希记录在 c12 `old130.json`）。

## 3. 人工审阅

- 反编译比对 c08 DLL：伙伴相关三类 0 逻辑差异。
- Windows 检查：c10 重烘的 ct/t.scanim、ScTactical(.Hostage).json、ScCsgoAppearance.bin 与 c08 包内完全一致；clips 输入与 c10 相同 → 复用有依据。
- 未做：工作台“防护装备”页面截图审阅、实际听音。

## 4. 实机验收（未执行，需要有桌面的 Windows 会话或用户）

VPS 没有图形界面/实机权限，以下都没有做，不能算完成：
1. 工作台“防护装备”：无甲→半甲→全甲、全甲套餐、维修报价与实际扣料、等级不足提示、手机布局。
2. 被敌人射击：全甲/半甲下头部/躯干/腿的掉血与三种声音；打敌人时的声音；实际听感与音量。
3. 死亡清零与保留背包模组下保留；读档/换世界不补满。
4. 自然遭遇 A/B：新世界固定种子，原生上限 26 与装 Slower（上限 2 时应提示容不下 3 人队），静止观察与走动探索各测一段；记录日志中 `[CS_SPAWN] natural squad …` 的出生距离、首次看见/开火时间、未遇即休眠比例；确认视野内出现时渐显且 3 秒内不开火。
5. 旧版往返实机：用新版存档 → 在已安装的旧兼容版（1.0.0/1.2.0/1.3.0）读档并保存两次 → 回新版，确认防护值不变、未补满、未重复扣料。
6. 规划列出的其余实机项：C4 雪/半砖/坡地的实际画面与真实暂停、NPC 投掷/安装连续动作、头部准星射击、男女/等级/疾病下的实际伤害、三/五人交火。

## 5. 最终候选、交付与清理

- 交付（常设授权，同卷移动、单份，job `5c7575a4cfba492a99afeabcc8431d67`）：替换前核对 output = c08 清单哈希、候选 = c13 验证哈希。
  - 全量 `[API1.9]CS武器1.4.0-全量包.scmod` 526,641,811 `0bfa61e523c8dc1ba67faf98e4ea8ef3bada4fe3781b8c9b34028a6567826abf`
  - 轻量 `[API1.9]CS武器1.4.0-轻量包.scmod` 35,529,367 `f2eff2bbe0a4ab9dee5c5bb744e2b2810c84952eea53ae6b0c83f3aab06f896a`
  - 探员 `[API1.9]CS武器1.4.0-探员包.scmod` 38,456,949 `ee92acfe43fe612546b37e8357c5210ac2ffcf92bbf06acc16c8de7b98976e8c`
  - 与 c08 相比只变了：全量 = 核心 DLL、战术 DLL、核心数据库、兼容清单（其余 1701 个成员逐字节相同，含 clips、语音、外观适配）；轻量 = 核心 DLL、核心数据库、兼容清单；探员 = 战术 DLL。版本号、包名、范围未变。
  - `output/release-1.4.0/manifest.json` 已更新（旧哈希在 replaced.previous/history）；`tools/completion_140.py` 基线改为以上哈希。清理后复读三包：哈希一致、zip 校验通过（job `93f816ac448a41ffbf747705ee19068d`）。
- 清理（常设授权，job `ad2e5b9c2f1248e8857c486206f6f524`，收据 [current-direction-20260929-cleanup.json](current-direction-20260929-cleanup.json)）：删除 c09–c13 阶段的可重建目录 40 项、9.85 GB（源码副本、构建、未交付的 c09/c10 物品护甲候选包、已清空的 c13 candidate），永久删除（非回收站）；E: 可用 33.81 → 43.6 GB。保留各阶段报告 JSON、日志、motion/throw/hotspots/ui 渲染与报告、clips 缓存（57 MB，按输入哈希供下一轮复用）、归档的物品护甲资源、所有源资源与其他任务阶段。
- 未安装到 Mods，未写玩家世界，未发布，未升版本。

## 6. 已知限制

- 数值（吸收比例、容量、材料、1/3 概率）是首轮试验值，需与已降低的敌人伤害一起实测。
- 夹具没有物理和寻路移动：巡逻“走过去”由实机验证，离线只验证目标点、状态和发现/开火链。
- 伙伴防护未提供（规划为可选）。
