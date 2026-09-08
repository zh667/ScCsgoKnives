# StatTrak 外观资源：已就位的部分与仍缺的确切文件

日期：2026-09-09。对应实现版本 0.40.0。

> 0.40.1 Windows 审查更新：下文“缺失”描述的是 VPS 的 0.40.0 环境，不是现在的交付状态。
> 本机已从 CS2 VPK 导出模块与数字图集，并新增真正的第一人称、第三人称、掉落物绘制；
> 不是仅将 `ModuleAvailable` 改为真。详见 [审查记录](gun-counter-review-0401.md) 和
> [来源哈希](stattrak-module-source-0401.json)。

## 1. 已从 CS2 取到并已进入模组的部分

`tools/extract_stattrak_attachments.py` 读取本机 35 份 CS2 枪械模型导出
（`CSMCReverse/local_cs2_analysis/all_weapons/02_models/events_full/weapon_*.analysis.txt`），
取出 Valve 自己的两个附件：

| 附件 | 覆盖 | 用途 |
|---|---|---|
| `stattrak` | **35 / 35** | 高清枪体上的计数器安装位 |
| `stattrak_legacy` | **34 / 35**（电击枪没有旧版枪体，CS2 本身就没有这一项） | 旧版枪体上的安装位 |

每条包含骨骼名（通常为 `weapon_offset`，双枪为 `weapon_r`）、四元数旋转和以英寸为单位的偏移。
同一把枪的多个渲染网格上重复出现的附件逐字段比对一致，出现分歧会被标为 `ambiguous` 而不是取其一——本轮 0 条分歧。

产物：

- 设计审计：`docs/gun-stattrak-attachments-2026-09-09.json`（含每个模型文件的 SHA-256，游戏不加载）。
- 运行时数据：`src/ScCsgoKnives/AnimationData/gun_stattrak.json`，编译进 DLL，由 `ScGunStatTrak` 读取。

`ScGunStatTrak.For(asset, legacyBody)` 按当前实际绘制的枪体选择 `stattrak` 或 `stattrak_legacy`，
**不会为了装计数器把一把枪在新旧枪体／UV 之间切换**。所有安装位都不是目测的。

## 2. 仍然缺失的确切文件

本机 **没有 CS2 的 VPK**，只有此前导出的枪械子集。计数器模块的网格与数字图集从未导出过，因此本版
**不在枪身上绘制任何计数器**，也不绘制任何替代几何。需要 Windows 侧从 CS2 的 `pak01_dir.vpk` 导出以下文件：

| VPK 内路径 | 用途 | 模组期望的落地位置 |
|---|---|---|
| `weapons/models/shared/stattrak/stattrak_module.vmdl_c` | 计数器模块本体网格 | `Assets/Models/ScCsgoKnives/stattrak_module.obj` |
| `weapons/models/shared/stattrak/materials/stattrak_module.vmat_c` | 模块外壳材质（含 color/ao/rough 三张贴图） | `Assets/Textures/ScCsgoKnives/stattrak_module{,_orm,_normal}.png` |
| `weapons/models/shared/stattrak/materials/stattrak_module_color_tga_a44d35b2.vtex_c` | 外壳颜色 | 同上 |
| `weapons/models/shared/stattrak/materials/stattrak_module_ao_tga_abf699bb.vtex_c` | 外壳 AO | 同上 |
| `weapons/models/shared/stattrak/materials/stattrak_module_rough_tga_9f02384d.vtex_c` | 外壳粗糙度 | 同上 |
| `weapons/models/shared/stattrak/materials/stattrak_module_display.vmat_c` | 显示面板材质，含 `F_STATTRAK=1`、`F_SELF_ILLUM=1`、动态 `g_nStatTrakValue` | 参数已记录，需实现自发光数字着色 |
| `weapons/models/shared/stattrak/materials/stattrak_digit_atlas_psd_bf07cc9c.vtex_c` | 数字图集（枪械用） | `Assets/Textures/ScCsgoKnives/stattrak_digit_atlas.png` |

刀具专用的 `stattrak_module_knife.vmdl_c`、`stattrak_digit_atlas_knife_*` 本期**不需要**：首期计数器只做枪械。

0.40.0 的导出只会让 `ScGunStatTrak.ModuleAvailable` 变为真，并不会自动产生绘制调用；0.40.1 已补齐该调用链。在资源缺失时记录警告，
说明缺哪几个文件，计数改在属性页与物品说明中显示。

## 3. 本版对缺失的处理

- 计数器**可以安装、可以计数、可以升级**，安装确认窗口明确写出"本版尚未包含 CS2 计数器模块模型"。
- **绝不**用自制或近似模块顶替，也不在枪身上画任何数字。
- 面板位数上限按官方六位处理（`ScGunStatTrak.PanelText`），但记录里的计数是 64 位且不被显示位数截断。
- 资源到位后仍需实机验收：第一人称、检视、第三人称、掉落物、物品栏五处的一致性，以及数字不被枪皮或 AO 染色。
