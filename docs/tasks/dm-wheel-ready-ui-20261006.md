# 死亡竞赛：轮盘图标、准备流程文案、角点光柱、菜单键提示（2026-10-06）

状态：已实现并通过离线检查和界面探针，等用户在游戏里看（分支 `feat/dm-wheel-ready-ui`，从 main `a186cc1` 建）。写入者：VPS Claude。未合并、未交付。

## 用户原话（2026-10-06）

1. "配置轮盘里的枪械HUD，可以用cs2自己的（现在应该是CSMC风格的），然后刀具现在只有刺刀有HUD"
2. "配置轮盘后面不用加全部免费，入场这俩字改成准备，并且房主能查看准备列表，能看谁没准备，然后不需要CS2数据1.41.8.8"
3. "角点的高度可以再高些，然后地图编辑那里（就是没打开之前的一个小窗口），我之前改了打开菜单为U键，但是那里还是显示F6打开菜单"

## 要求 → 实现 → 检查

| 要求 | 实现 | 区分错误实现的检查 | 结果 |
|---|---|---|---|
| 轮盘用 CS2 自己的枪械图标 | `tools/build_dm_hud_icons.py` 把 CS2 装备 SVG 平滑渲染为 64 px 高的白色图标（`Assets/Textures/ScCsgoDeathmatch/hud/`，记录 `deathmatch-addon-hud-icons-20261006.json`）；`DmPx.HudIcon/ItemHud`、`DmHudIcon` 以线性采样绘制；轮盘扇区改用它们（像素图标仍用于击杀信息和装备行） | 探针帧 `wheel-top`、`wheel-rifles`：扇区图标边缘平滑，不是像素块 | 通过（dmu1 帧 08、09） |
| 刀具全部有图标 | 原因：刀具资源名（karambit、m9…）不带 `knife_`，只有 bayonet 与 CS2 图标名一致。`DmPx.s_knifeSprite` 建立 22 把刀到 CS2 图标名的映射，像素图标与平滑图标共用 | 探针帧 `wheel-knives`：8 个扇区都有图标 | 通过（帧 13：爪子刀、M9、蝴蝶刀、刺刀、鲍伊猎刀、求生匕首、系绳匕首、海豹短刀） |
| 去掉"· 全部免费" | `DmWheel` 标题 | 帧 `wheel-top` 标题只有"配装轮盘" | 通过 |
| "入场"→"准备" | 轮盘按钮、菜单按钮、状态、提示、HUD 大厅行、拒绝文案、通知；探针脚本点"准备" | 源码里不再出现"入场"；探针 `click("准备")` 成功 | 通过（源码 0 处；探针点中“准备”，entered=True） |
| 房主能看准备列表 | `DmMenu.Ready()`：房主见"已准备 n/m：名字 / 未准备：名字"，客户端见人数 | 探针帧 `lobby-menu` 状态栏 | 通过（“已准备 0/1：无 / 未准备：巴西尔”） |
| 不显示 CS2 数据版本 | 菜单状态去掉该行；`DmWeapons.Cs2Version` 保留给日志 | 帧 `lobby-menu` 无"CS2 数据" | 通过 |
| 角点光柱更高 | `CornerBeamHeight = 18`（原 7；复活点、准备点仍 7） | 帧 `corner-1-pending-box` | 通过（光柱出画面顶） |
| 地图编辑小窗显示实际菜单键 | `DmEditElement.Show` 读 `DmUiSettings.KeyOf(KeyMenu)`，未设置时提示 | 默认键仍显示 F6；改键后显示所改的键 | 默认键通过（帧 02 显示“F6 打开菜单操作”）；改键后的显示待用户在自己的设置下看 |

## 验证计划

- VPS：`ScCsgoDeathmatch` 类型检查。
- Windows：`deathmatch_140.py <tag> prepare,build,dmbuild,dmpackage,dmcheck`；`sp_deathmatch.py <label> output-full stage-<tag>-dm`（1.9.3.1，以用户在玩的 1.5.0 全量包为核心）看帧。
- 死亡竞赛包的 modinfo 依赖是对象写法，引擎只解析数组写法，所以不做版本校验：1.4.0 的竞技包今天就在 1.5.0 全量包上运行。

## 结果（2026-10-06）

- VPS 类型检查通过。Windows 开发阶段 `dmu1`（`.tmp/completion-140-20260929/dmu1`）：核心构建、竞技包、`DeathmatchCheck` 225/225。
- 竞技包：`dmu1/candidate/[API1.9]CS武器1.4.0-死亡竞赛包.scmod`，358,453 字节，SHA-256 `ec1540bafa82b2aa8e8cf2ba3de208b37f8439d4b51c2c0c44ab35aae8c9f1a4`；装配体 `e0ccf604…`，玩法身份 `693530f1…`。
- 界面探针 `dm-sp-dmu1`（1.9.3.1 隔离副本，核心为 output 的 1.5.0 全量包）：124 步、24 帧、0 报错；结果 `.tmp/mp-m0-20260929/results/dm-sp-dmu1.json`，缩略帧 `.tmp/dev-temp/dm-ui-20261006/dmu1/`（原始帧按清理规则已删）。
- 未做：联机探针（文案改动不涉及协议）；用户实机观感。
