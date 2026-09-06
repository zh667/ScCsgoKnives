# 0.26.7 生存崩溃修复与全物品无耐久

## 用户要求

所有模组物品均不使用耐久：刀、枪、投掷物、弹匣、霰弹、零件、武器装配台。弹药和投掷物仍按玩法正常消耗数量；不增加维修、损坏或耐久条。现有物品索引和变体顺序不变。

## 根因与修复

2026-09-06 Game.log 在 12:03:46、12:04:24、12:04:53 记录相同 `ScKnifeBlock.DrawBlock` / `BlockIconWidget.Draw` 越界异常。最后一次日志显示默认 CT 刀的 value 从 131375 变为 393519，data 从 8 变成 24。

刀具型号占用 data 低 5 位，但原版 `Block.SetDamage` 从第 4 位开始写磨损。原版 `ComponentMiner.DamageActiveTool(1)` 将型号 8 改成 24，而模组只有 22 把刀；物品栏随后按 24 访问贴图数组，导致世界退出。这不是尸体专有的伤害异常：其他近战命中和原版工具磨损也能触发，部分刀还会悄悄变成别的有效型号。

枪虽然覆盖了 GetDamage/SetDamage，但 CSV 耐久为 0，原版 DamageItem 仍可走到销毁物品分支。

- 新增抽象 `ScNoDurabilityBlock`，所有 6 个具体 Block 类型经此继承，统一 GetDurability=-1、GetDamage=0、SetDamage 原值返回。无新物品注册。
- 刀/枪 CSV Durability 同步为 -1；删除刀命中的 DamageActiveTool 调用。将此约束写入 AGENTS.md 和生存规划。
- 刀具绘制在访问数组前检查型号；未知型号使用已有未知物品图标，名字和说明保留数据提示，第一人称不再夹到最后一把刀。不会猜测旧数据的真实型号并自动改写。
- 首次装备发放完成后排队请求一次存档；等待玩家进入 Playing、存活且实体已在当前 Project，再调用 GameManager.SaveProject。同步快照异常间隔 10 秒重试；后台磁盘保存错误由游戏原有日志和对话框报告。无成功弹字。

## 存档与恢复边界

已备份日志及受影响的完整 World29 到：

`E:\Obsidian Document\Document1\ScCsgoKnives\output\survival-incident-20260906-120733`

世界名 111，种子 32001。备份中的 Project.xml 和 Project.bak 都没有玩家实体，Players 为空，GrantedPlayers 为空，TotalElapsedGameTime 约 0.021。原版正常自动保存间隔为 300 秒，这几次开局在首个周期前就崩溃；现有文件不能恢复未保存的角色与背包。没有重写、删除或回滚原世界。

新增首次出生存档请求缩短这个空窗，不是事务式防崩溃存档系统；尚未实机验证磁盘保存完成。

## 检查与生存审查

Release 构建成功，只有既有 NCalc 依赖的 NU1902 警告。最终包由 tools/pack_scmod.py 生成，检查加载的是包内 DLL。

- 全部 5289 项通过，0 失败；比 0.26.6 增加 1387 项。
- 209 种物品数据状态：全部刀具、枪械满弹/空弹/一发及消音状态、全部新增物品。使用实际 BlocksManager.DamageItem，验证大额磨损也不会销毁或改写。
- 1045 项实际 ComponentMiner.DamageActiveTool + ComponentInventory.Save/Load 检查，覆盖 Survival、Harmless、Challenging、Cruel、Creative，每种状态连续 32 次磨损后核对型号、数量、弹量与保存结果。WorldSettings 的调色板初始化在无图形测试宿主中跳过；磨损和背包代码均来自真实 SCAPI。
- 34 把普通枪的射击扣弹、完整换弹、换弹中断、弹药不足及背包存取；管式霰弹与弹匣式区分验证。电击枪沿用既有冷却检查。
- 6 种投掷物的投出成功扣量、生成失败返还、防重复提交与背包保存；复核现有投掷状态/刀恢复/电击枪冷却保存路径。
- 10 个未知刀型号的识别、说明、动画解析与数据保留；首次出生存档的延后、重复抑制、失败保留待办、多玩家和加载领取标记。

同一测试宿主对照旧 0.26.6 包：995 项既有接口行为检查失败，新版全部通过；另 13 项需要新版新增接口，旧包不具备这些接口，不能计作旧故障复现。详见 survival-0267-baseline-summary.json。

包内/无图形测试没有执行实际世界渲染和 NPC 命中；仍需重启游戏后验证生存轻刀/重刀攻击动物与尸体、打开背包、退出重进、首次出生磁盘存档。旧版回归通过并不能代替本次实机检查。

## 安装

- 包：`output/ScCsgoKnives-0.26.7.scmod`，699 entries，219919759 bytes。
- SHA-256：`9a245929abbaa6c511e2ec3556b3ae556a35beb0f1c314e8a77ae00d6aa250fb`。
- 已安装：`E:\EdgeDownload\[Windows]SurvivalcraftAPI_1.9.2.1\Mods\ScCsgoKnives-0.26.7.scmod`，副本哈希一致。
- 旧包已核对原报告哈希并备份到：`E:\Obsidian Document\Document1\ScCsgoKnives\output\installed-backups\0267-20260906-122351\ScCsgoKnives-0.26.6.scmod`。
- 未关闭正在运行的游戏。完整退出并重新启动游戏后才加载新版 DLL。
