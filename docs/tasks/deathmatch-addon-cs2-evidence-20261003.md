# 死亡竞赛 · CS2 规则与资源证据表（2026-10-03）

OpenSpec `deathmatch-addon` 任务 4.1 / 4.8 的证据。结论先行：**数值字段逐项取自本机 CS2 文件；CS2 代码里的算法（护甲结算、散布随开火的增长与恢复、后坐力图案、HE 衰减、刀伤害、穿透）没有在 CS2 里实测过**，本表逐项标明。任何“完全复刻 CS2”的说法都不成立，也没有这样写进玩家说明。

## 1. 来源身份

| 项 | 值 |
|---|---|
| CS2 安装 | `E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive`（只读） |
| steam.inf | PatchVersion `1.41.8.8`，ClientVersion `2000922`，VersionDate `Sep 30 2026` |
| pak01_dir.vpk | 7,698,168 B，SHA-256 `fac8ff988ab9a20c3175c33fd4c5196da3798a0ddcea9fe0a5565ab33cffb5a9` |
| 武器数据 | `E:/projects/CSMCReverse/local_cs2_analysis/all_weapons/01_weapon_data`：35 个 `firearm_blocks/weapon_*.vdata` + `source/weapons.vdata`，逐文件 SHA-256 记在生成物的 `Source.files` |
| 提取脚本 | `tools/cs2_dm_profile.py`（Windows 作业 `26fa38a64ceb46a18eef25a8cc3f59ad`，exit 0） |
| 生成物 | `src/ScCsgoDeathmatch/Data/dm_cs2_profile.json`，50,774 B，SHA-256 `d3cac5abe37bbb7bb225f5e633570b141c469478836776ef4f1a176c755b417c`；内嵌进附属包 DLL |
| 规则指纹 | `DmWeapons.Fingerprint` = 上述 JSON 字节 + 固定规则常量的 SHA-256 前 16 位；联机握手比较（不同即拒绝加入并说明） |

生成物只存 vdata 原值、原字段名，不做任何换算。所有换算都在 `src/ScCsgoDeathmatch/Combat/DmWeapons.cs` 与 `DmCombat.cs`。

## 2. 字段 → 用法 → 状态

状态含义：**原值** = 直接使用 vdata 数字；**换算** = 单位转换，公式写明；**建模** = 用 CS2 的值喂本模组自己的模型，算法不是 CS2 的；**公开资料** = 数字来自长期公开的 CS 资料而非本机文件；**未实现**。

| 规则 | vdata 字段 / 来源 | 用法 | 状态 | 离线检查 |
|---|---|---|---|---|
| 单发伤害 | `m_nDamage` | 35 枪逐型 | 原值 | W03（AK 36、AWP 115、Glock 30、Nova 26×9、M4A1-S 38、Negev 35、Zeus 500 手抄对照） |
| 弹丸数 | `m_nNumBullets` | 霰弹枪每丸独立命中 | 原值 | W03 |
| 爆头倍率 | `m_flHeadshotMultiplier` | 头部命中 ×倍率 | 原值 | W03（M4A1-S 3.475，其余 4） |
| 护甲穿透率 | `m_flArmorRatio` | 见护甲结算 | 原值 | W03 |
| 距离衰减 | `m_flRangeModifier` | 伤害 × 系数^(距离/500 单位) | 原值 + 换算（1 格 = 1 米 = 39.37 单位，沿用核心表） | W05、C14（AK 12.7 格 0.98，Glock 0.85） |
| 射程 | `m_flRange` | 射线最大长度 = 单位/39.37，上限 128 格 | 换算；**上限 128 格是本模组的限制**（CS2 的 8192 单位 = 208 米，超过可加载视距） | W10、W11（Zeus 120 单位 = 3.05 格） |
| 弹匣 | `m_iMaxClip1` | 每命满弹匣；备弹无限、仍需换弹 | 原值 | W04（35 枪与核心容量逐一相等） |
| 射速 | `m_flCycleTime` | 发射间隔 | 原值 | W06 |
| 站立/蹲伏散布 | `m_flSpread` + `m_flInaccuracyStand` / `Crouch` | 圆锥半角 = atan(spread + inaccuracy) | 换算（“无量纲 → 角度”是核心表沿用的读法，**不是从 CS2 子弹代码验证的**） | W08、W09 |
| 移动/跳跃散布 | `m_flInaccuracyMove` / `Jump` | 作为“满速/腾空时的附加角度”，按核心已有的速度比例混合 | **建模**：CS2 的速度曲线在代码里，未测 | W07（有限、非负、有序） |
| 连发散布增长 | `m_flInaccuracyFire`、`m_flRecoveryTimeStand` | 每发增量 = fire；上限 = fire / (1 − 10^(−cycle/recovery))（CS 衰减到 1/10 的稳态）；用核心的线性回落 | **建模**：CS2 是指数回落，本模组是线性回落 | W07 |
| 镜头后坐 | `m_flRecoilMagnitude`、`m_flRecoilAngleVariance`（经核心表换算） | 俯仰/偏航踢动幅度 | **建模 + 估计**：核心表以 AK 为锚点按比值换算；**CS2 的定种子后坐力图案没有复现** | — |
| 开镜/消音第二组值 | 各字段的第二元素 | 开镜、去消音时使用 | 原值（取数），手感同上为建模 | W09（AWP 腰射 4.63°，开镜 0.126°） |
| 护甲结算 | 无 vdata 字段（算法在代码里） | 有甲：伤害 ×(ratio/2) 进血，其余的一半耗甲；甲不够时余量透传；血甲取整 | **公开资料**（CS:GO SDK 系公开的 `OnTakeDamage` 算法）；CS2 是否逐字相同未在 CS2 内实测 | C01–C16 全部为手算期望值 |
| 部位倍率 | 无 | 头 = 武器倍率；胸/臂 = 1；腿 = 0.75、无甲覆盖 | **公开资料**；**腹部 1.25 没有单独判定区**，按胸处理（已知差异） | C04、C09 |
| 100 血 / 100 甲 / 头盔 | 用户确定（DM-08） | 每命恢复 | 用户要求 | C17、M14 |
| 刀 | `weapon_knife` 块只有 `m_flArmorRatio` = 1.7 是有效值（`m_nDamage` 是预设占位） | 轻击 40 / 背刺 90，重击 65 / 背刺 180；背刺判定余弦 0.475 | 护甲率原值；**伤害与背刺阈值为公开资料** | W12 |
| Zeus | `weapon_taser`：500 / 120 单位 / ratio 2 | 与枪同一条路径 | 原值 | W03、W10 |
| 高爆手雷 | `weapon_hegrenade`：`m_nDamage` 99、`m_flRange` 350、`m_flArmorRatio` 1.2 | 中心伤害按 99 缩放；**衰减曲线与半径沿用核心（线性、7.8 格）** | 数值原值，衰减为建模 | W13 |
| 燃烧瓶/燃烧弹 | `m_nDamage` 40，`m_flArmorRatio` 1.8 / 1.475 | 按每秒 40 结算，整点累计（不随帧率丢失） | 数值原值（读作“每秒”为公开资料），**跳伤节奏未测** | W14 |
| 穿透（木板/薄墙） | `m_flPenetration` 已读入 | — | **未实现**：没有材质映射与多段命中；击杀播报的“穿透”标记因此不会出现 | — |
| 持枪移动速度 | `m_flMaxSpeed` 已读入 | — | **未实现**：所有武器移动速度相同 | — |
| 命中体积 | 核心已有的头/身/臂/腿判定 | 沿用，不随外观变化（规则只看型号） | 沿用核心；没有对 T/CT/第三方模型逐一再测 | — |

## 3. 核心表与本档的交叉核对

核心早先用同一批 vdata 生成过 `AnimationData/cs2_weapons.json`。离线检查 W16 逐枪比较两份来源的伤害、护甲率、距离系数、爆头倍率：35 枪全部一致（两次独立提取）。

## 4. 表现资源（任务 4.8 / 7.7）

导入脚本 `tools/import_cs2_dm_assets.py`（Windows 作业 `d6b32634c080448eaeb2760bfdd1f26a` 首次因文件尚未同步失败，未产生任何输出；重跑作业成功，exit 0）。逐项哈希在 `docs/tasks/deathmatch-addon-assets-20261003.json`。

| 资源 | VPK 成员 | 包内成员 | 状态 |
|---|---|---|---|
| 爆头 / 盲狙 / 穿烟 / 穿透 / 致盲 / 自杀 图标 | `panorama/images/hud/deathnotice/{icon_headshot,noscope,smoke_kill,penetrate,blind_kill,icon_suicide}.vsvg_c` | `Assets/Textures/ScCsgoDeathmatch/kill_*.png`（64 px，保留原形状） | 已导入；**画面效果待用户看** |
| 复活音效 | `sounds/player/pl_respawn.vsnd_c` | `Assets/Audio/ScCsgoDeathmatch/respawn.ogg`（单声道，1.82 s） | 已导入；**听感待用户听** |
| 比赛结束音效 | `sounds/ui/deathmatch_end.vsnd_c` | `Assets/Audio/ScCsgoDeathmatch/match_end.ogg`（2.23 s） | 已导入；待用户听 |
| 白色复活保护效果 | **VPK 里没有以资源形式存在**（CS2 由代码渲染） | 附属包自绘：受保护玩家身周一圈缓慢明暗的浅白外壳（纯色面片，Windows/GLES 同一路径） | **这是自制标记，不是 CS2 原效果**；待用户看 |
| 购买菜单 / 轮盘 / 计分板布局 | `panorama/layout/buymenu.vxml_c` 等是 Source 2 UI 代码 | 不可运行于本引擎；轮盘与计分板为附属包自己的控件 | 按用户要求做“CS 风格轮盘”，不声称是 CS2 现行购买界面 |
| 武器/刀/投掷物图标 | — | 直接用各物品自身的方块图标（含皮肤缩略图） | 无新增资源 |

没有复制整个 VPK；导出目录 `.tmp/dev-temp/deathmatch-20261003/cs2` 只含上述成员，位于 Windows。
