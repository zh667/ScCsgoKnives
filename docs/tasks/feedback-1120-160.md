# 玩家反馈三条 → 1.6.0（feedback-1120-160）

状态：已随 **1.6.0** 交付（2026-11-20，发布记录 `docs/tasks/release-1.6.0-20261120.md`），未合并 main
分支：`feat/feedback-1120-160`（从 main `910e321`，1.5.3）
写入方：VPS Claude；Windows 作业负责构建、测试、交付与 Git 写操作

## 用户要求（2026-11-20）

玩家反馈原文：
1. 枪械1.5和neo1.4会冲突，导致通用弹匣配方看不到
2. 尸鬼的全套灵魂盔甲也免疫CS武器的伤害，站着给匪打都没事
3. CS武器或许可以加后坐力和随机弹道，模组设置里面可以选择关闭

用户决定：
- 先定位并给修改建议和版本号建议（已给）；随后用户问“你建议呢”，我建议：第 2 条选 A、保底 30%；第 3 条拟真档 Lv10 不清零、最多减到一半；分两个版本发。
- 用户回复：“停止并接手，然后最后给我一个总的1.6.0”——三条合成一个 **1.6.0**，其余按我的建议。
- 不打 tag、不上传、不安装；合并 main 另等同意。

## 定位（证据）

### 1. Neorxna 1.4.3 看不到通用弹匣配方
- 通用弹匣、霰弹、投掷物、C4、枪、刀的配方只在武器装配台（`ScWorkbenchExtension`、`ScWeaponCrafting`、`ScComponentCrafting`），九宫格配方表 `CraftingRecipesManager.Recipes` 里没有。原版配方书靠 `GetBlockRecipeScreen` → `ScAssemblyRecipesScreen` 显示。
- Neorxna 1.4.3（反编译 `E:/projects/analysis/sc-mod-uninstall-survey/mods/_API1.9.4_Neorxna-v1.4.3/src`）的悬停卡和配方对话框不调用 `GetBlockRecipeScreen`：先问 RecipaediaEX（装了的话），否则遍历 `Neorxna.NUI.RecipaediaRecipesNeo.RecipeWidgetProviders`（public static `List<Func<int,bool,IEnumerable<Widget>>>`，默认只有一个遍历九宫格配方表的提供者；bool=true 是“用途”）。悬停卡按物品值缓存结果。

**实机补充（2026-11-20，探针 `tools/MpM0/sp_feedback1120.py`，1.9.3.1 + Neorxna 1.4.3 + 尸鬼 2.0）：** 更严重的原因在加载阶段。
引擎的 LoadingScreen 用一个循环调用每个模组的 `OnLoadingFinished`，没有逐个模组的异常保护。Neorxna 1.4.3 是给 API 1.9.4 编的，
在 1.9.3.1 上它的 `NUISettingsScreen` 构造时调用不存在的 `JsInterface.InvokeAsBoolForYsf`，抛 `MissingMethodException`，循环就此结束。
Neorxna 没写 LoadOrder（0），总排在所有 CS 包前面，于是 CS 的 `OnLoadingFinished` 一个都没执行：装配台的弹匣/霰弹/C4/投掷物配方
（`RegisterBaseRecipes`）、本机设置读取、CS 界面注册全部缺失——玩家在装配台里就看不到通用弹匣配方。日志里没有 `[ScCsgoKnives] … initialized`。
修法：核心加载器记录自己的 `OnLoadingFinished` 是否被调用；主菜单第一帧仍未调用时，对核心及其后的每个 CS 包加载器补调一次并执行其延后动作，各自有异常保护，日志写 Warning。

### 2. 尸鬼灵魂套装免疫 CS 伤害
- 尸鬼 2.0 衣物 90–93（头/腿/胸/脚）ArmorProtection=1、Sturdiness=infinity。原版每件吸收 min(攻击×护甲值, 剩余耐久) → 永远 100%。
- 尸鬼只对尸鬼系攻击者另算（`SAGModLoader.CalculateCreatureInjuryAmount`：护甲值×0.7 减伤，满套再按难度、×0.5）。
- 原版铁甲、钻石甲护甲值也是 1.0，但耐久有限（15–200），会打坏。
- CS 伤害：`BulletAttack.CalculateInjuryAmount` 先算 CS 防护，再交给原生护甲（base），无限耐久的甲把它吃光。

### 3. 后坐与随机弹道
- 生存已有：`gun_handling.json`（AK 每发上跳 1.12°、0.22 s 回正 90%；站立散布 0.34°、连射最多 +1.3°、移动 +1.1°）。`ScGunGrowth.AngleScale` 每级 −10%，Lv10 起为 0（散布和后坐都没了）。
- 死斗附加包有 CS2 固定扫射图案（`DmRecoil`，经 `ScModes.Recoil`/`IScRecoil` 接入，只在死斗世界）。核心 `cs2_weapons.json` 的 `Raw` 里有同样的 vdata 字段（m_nRecoilSeed、m_flRecoilAngle/Variance、m_flRecoilMagnitude/Variance、m_bIsFullAuto、m_flCycleTime）。

## 实现（按条对应要求）

| 要求 | 实现 | 区分正误的检查 |
|---|---|---|
| 1a 装了 Neorxna（1.9.3.1）装配台仍有弹匣配方 | `ScCsgoKnivesModLoader`：`OnLoadingFinished` 被前面模组的异常跳过时，主菜单第一帧补做 CS 全家的加载完成（见上）。 | 实机：1.9.3.1 + 真 Neorxna，日志出现补做 Warning、`initialized` 行，装配台 `IsRecipe(弹匣)` 为真；同一探针在 r160a 包（无补做）上状态为 not run（证伪）。 |
| 1 Neorxna 里看得到弹匣配方 | `World/ScNeorxnaRecipes.cs`：加载完成后反射找到 `RecipeWidgetProviders`，加一个提供者，对装配台能做的物品返回一张卡片（标题、制作途径、材料图标和数量）。卡片内容与装配配方页共用 `World/ScAssemblyRecipeCard.cs`。找不到 Neorxna 什么都不做；字段类型不符只警告一次。“用途”方向不提供。 | 离线：用真实 neorxna.dll 调它自己的查询入口，弹匣得到 1 张卡、普通方块不受影响；未注册时为 0（证伪）。实机：装 Neorxna 悬停弹匣看到配方。 |
| 2 灵魂套装不再免疫 | `World/ScUnbreakableArmor.cs`：CS 的伤害（子弹含敌兵/队友、刀、手雷、C4、战术炸弹）在原生护甲结算后，若目标穿着任一件耐久无限且有护甲值的衣物，伤害不低于“不穿甲时的伤害 × 30%”。原版有限耐久护甲不受影响；死斗（模式自己结算）不受影响；尸鬼的濒死保命不受影响。 | 离线：无限耐久全套 → 30%；同护甲值但有限耐久 → 仍为 0；不穿 → 原值；死斗模式 → 不介入。 |
| 3 后坐与弹道，可关闭 | 模组设置“后坐与弹道”三档（存在 `ScCsgoUi.json` 的 `GunRecoil`，旧版本读写会保留这个键）：**标准**（默认，现状不变）、**关闭**（不上跳；单发枪不随机偏移，霰弹枪弹丸照常散开）、**拟真**（生存也用 CS2 固定扫射图案，取代标准档的随机上跳；散布沿用标准档；成长每级 −5%，Lv10 起停在 50%；电击枪没有后坐数据，按 0 处理，与死斗一致）。死斗按竞赛规则不受影响。联机：房主的档位在玩家加入时下发（消息 77），房主保存设置时广播；客户端设置页只显示“由房主决定”。 | 离线：三档的成长曲线与属性页数值；拟真档 AK 前 10 发与死斗图案一致（同一张表）；关闭档霰弹仍散开；联机回环里客户端收到房主档位。实机：手感由用户验收。 |

版本号：家族升到 1.6.0（三个 modinfo + MetadataCheck）。

## 待确认 / 我自己定的细节

- 保底只看“穿着无限耐久护甲”，不区分这一发随机落到哪个部位：混穿（一件尸鬼甲 + 原版钻石甲）时，原本由钻石甲挡下的那一发也会有 30%。
- 关闭档保留霰弹枪弹丸散开（否则关闭档霰弹等于独头弹）。
- 拟真档没有改移动/跳跃散布（CS2 移动散布 AK 近 10°，会让跑射完全不能用，用户没要求）。

## 验证记录

见 `docs/tasks/release-1.6.0-20261120.md`（阶段 r160d 全部关卡、四引擎实机、联机死亡竞赛、真 Neorxna + 尸鬼实机探针）。

## 未覆盖

见发布记录；三档后坐手感与 Neorxna 卡片外观等用户验收。
