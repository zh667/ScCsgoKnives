# 战术拓展 1.2.1：内置玩家 T／CT 外观

2026-09-20。用户要求减少安装包，把额外功能并入战术拓展。原来的 CS 玩家外观 1.1.0
现在随战术拓展交付；本体仍为 1.4.5 Full，文件逐字节保持不变。

## 安装

| 使用范围 | 启用的模组 |
| --- | --- |
| 武器 | CS 武器 1.4.5 Full |
| 武器和战术玩法 | CS 武器 1.4.5 Full + CS 战术同伴拓展 1.2.1 |
| 同时使用玩家 T／CT 外观 | 上述两包 + NekoMeko Model 1.1 + Neorxna 1.4 |

关闭游戏后用 1.2.1 替换旧战术拓展，并移除独立的 `CS玩家T-CT外观1.1.0` 包。
本体 1.4.5、已有 NekoMeko 和 Neorxna 无需重复安装。没有 NekoMeko 的玩家可使用
现有 `NekoMekoModel1.1-源码构建.scmod`；Neorxna 沿用用户已有版本，不另外分发。

在 NekoMeko 的角色选择中选 `CS2 · CT SAS` 或 `CS2 · T Phoenix`，也可以切回默认人物。
原有选择继续按玩家保存，第一人称使用本体 CS2 手臂。眼部修复、第三人称换弹／切枪／检视、
同伴战斗和敌对小队行为均沿用上一版。外观不改变阵营、生命或护甲。

## 实现与兼容

- 战术根 DLL 不引用 NekoMeko、Neorxna 或外观 DLL。原生加载器只扫描到战术 DLL，
  避免仅使用同伴玩法时因外观类型缺少父类而加载失败。
- 外观 DLL 作为 `Integrations/ScCsgoAppearance.bin` 内置。只有两个前置均已启用且
  版本至少为 1.1／1.4，才由原生 `ModEntity.HandleAssembly` 注册其加载器。
  判断依据是本轮启用的模组列表，不会因进程中残留上次加载的程序集误启用。
- 合并 NekoMeko 的模型／皮肤描述文件，共用战术 GLB，没有重复打包人物模型。
  `ScCsgoAppearance` 程序集名、组件类名及 `zh667.cs.ct`／`zh667.cs.t` 保存键保留。
  战术程序集绑定版本保留 1.2.0.0，包版本及文件版本为 1.2.1，兼容旧适配器引用。
- 如果旧独立外观包仍启用，由旧包负责外观加载器，新包不重复注册。资源路径一致，
  原生内容表覆盖同路径条目，不产生重复模型键。正常升级仍建议移除旧外观包。
- NekoMeko 和 Neorxna 是独立框架，保留它们的包身份和生命周期；没有并进战术拓展。
  `src/ScCsgoAppearance` 是内部构建项目，其旧 manifest 保留兼容信息，不再用于交付。
  `tools/pack_appearance.py` 现在只输出可选的 NekoMeko 源码构建包。
- 枪布局 v5、记录 schema 6 和战术 schema 1 均未改变。没有新增网络同步；
  第三人称动作与材质近似仍以 `release-thirdperson-1.4.5.md` 的边界为准。

## 验证与复现

最终包证据见 `release-tactical-1.2.1-evidence.json`。
原生包加载验证在 NuGet 引擎和用户安装目录引擎各运行九个独立进程，覆盖无前置、
只有 NekoMeko、只有 Neorxna、前置禁用、前置版本不足、完整前置、加载器初始化顺序颠倒、
旧外观包共存，以及框架禁用后进程残留程序集。18 次共 176 项通过。
检查原生归档扫描、类型解析、数据库类替换、保存键对应资源及只注册一次预览钩子。
顺序颠倒用例验证 DLL 加载器初始化；数据库仍按默认 NekoMeko 先于战术的顺序合并。

战术原有 52 组回归分别在两套引擎通过。安装目录引擎上的外观／动作诊断 542 项通过，
含 80 张人物持枪动作渲染。三个交付包的 DLL、源资源和校验值逐项核对；沿用未改变的
本体 12,156 项与 Full 交付 1,325 项验证记录。上述为原生离线诊断，不是完整游戏、
安卓设备或联机验收。

构建顺序：本体已具备对应 1.4.5 DLL 时，使用 `tools/dev.ps1 dotnet build` 依次构建
`src/ScCsgoTactical`、`src/ScCsgoAppearance`（`--no-dependencies` 保留本体字节）。
随后运行 `tools/pack_tactical.py`。验证入口为 `tools/TacticalLoadCheck`、
`tools/verify_tactical_loading.py`、`tools/PackageCheck` 和 `tools/verify_appearance.py`。
两个 verify 工具参数均为用户的游戏安装目录，通过 `tools/dev.ps1 python -X utf8` 调用。

## 清理

在验证完成后，将被替代的战术 1.2.0 和独立玩家外观 1.1.0 安装包移入 Windows 回收站，
清单为 `cleanup-tactical-integration-20260920.json`。保留本体、可选 NekoMeko、存档、备份、
源资源与历史验证记录。`tools/fixtures/appearance-1.1.0.scmod` 是 25 KB 的升级测试基线，
不放在交付目录、不供安装。
