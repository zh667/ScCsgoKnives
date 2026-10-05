# 第四轮（2026-10-01）：烟片朝向角色、调试视角射击语义——实施文档（已完成并交付）

> **已被取代（2026-10-01 晚）**：本轮的射击原点规则（原版火枪原点 眼位+Right×0.3−Up×0.2，沿摄像机方向）已由 [first-person-eye-shot-20261001](first-person-eye-shot-20261001.md) 取代——所有视角伤害射线都从眼睛出发：第一人称沿准星，其他视角沿角色视线（即第一人称准星位置）。“摄像机不移动、不弯折弹道”的要求仍然有效。本文其余内容为历史记录。

## 当前状态（2026-10-01 03:22 JST）

| 用户要求 | 实现 | 区分正确/错误实现的检查 | 结果（1.9.3.1 = 隔离 SurvivalcraftAPI 1.9.3.1 副本） |
|---|---|---|---|
| 烟片朝向**玩家角色位置**，不朝摄像机；摄像机只管投影/深度/排序/裁剪 | 锚点=视图所属玩家的眼位；每片三张四边形 | 角色与烟固定、只动摄像机（环绕/升高/横移）→ 法线不变；摄像机固定、只动角色 → 法线跟随角色 | 1.9.3.1 录像：只动摄像机 0.000° 转角、与"指向眼位"偏差 0.000°；只动角色逐帧 ≤7.41°、偏差 ≤0.54°（`4d939784…`） |
| 保留 r3k 固定布局/尺寸；第一/第三人称遮挡、烟内、HE 开口不回归 | 布局函数未改；三片补厚度 | 场景遮挡指标 + 离线深视线覆盖 | 1.9.3.1 smoke-view 无失败（`6b271303…`）；离线 visual 0 失败、摄像机偏离角色覆盖 ≥0.906 |
| 多人各视图用各自角色 | `ViewAnchor(camera)` 取该视图的 PlayerData | 代码路径（分屏未实机测） | 静态；未实机测分屏 |
| CS 伤害射线=原版火枪语义：原点 眼位+Right×0.3−Up×0.2，方向=瞄准方向，不按摄像机命中点汇聚；去掉 0.75 m 摄像机原点分支 | `ScAimRay.Resolve` 重写，枪/刀/客户端输入/服务器重定四处调用 | DebugCamera 与 BumanCamera：仅平移→原点/方向不变；仅转→方向跟随、原点不变；同一视角下原版火枪与 CS 同线同向 | 候选包 1.9.3.1 camera-fire **24/24**（`cd49cb06…`）；r4a 包 24/24（`003364bb…`） |
| 覆盖第一/第三人称、DebugCamera、BUMANCAMERA、近墙、双枪左右枪口 | — | 同上 24 项 | 全部通过（近墙=第三人称隔墙两项；双枪=`muzzle_l`/`muzzle_r`） |
| 曳光/枪口火焰仍从模型出；不退回飞行子弹；不一刀切禁调试视角 | 未改 | 第三人称曳光在身体枪口；FlyCamera 不开火、DebugCamera/Buman 可开火 | 通过 |
| 保留 R8/Glock 切视角取消、服务器角色侧原点重定 | 未改 | R8/Glock 两项；M2 服务器轨迹 | 1.9.3.1 通过；MP M2 轻量 **31/31**（`c383fe31…`） |
| 找 BUMANCAMERA 真实类型/来源 | 第三方模组 `[API1.8]生存开透视角.scmod` 的 `Game.BumanCamera` | 测试中实际加载并切换到该摄像机开火 | 已找到并实测 |
| 交付 | r4x 候选移入 `output/` | 发布管线 + 上述门 | 全量 `c705aa7f…`、轻量 `73d76dbe…`、探员 `8caf6714…`（未变） |

MP 快照上的单机结果（第 3b 节）是平台规则更新前的历史证据，不作为单机验收。

写入者：VPS Claude（本轮 src/tools 唯一写入者；开工时 Syncthing 100%/idle/无冲突，无运行中的 Windows 作业）。
基线：r3k 已交付的 1.4.0（全量 `6f0c1ac1…`、轻量 `8de09daf…`、探员 `8caf6714…`）。本轮只做两项，不动联机平台、Projectile 兼容、Zeus、切刀。

## 1. 烟雾贴图朝向玩家角色位置

根因：r3k 的 `FacingAxes/SmokeQuad` 与 `SubsystemScGrenades.DrawSprites/ApplySmokeOpenings` 仍以 `camera.ViewPosition` 为朝向锚点；布局/尺寸已世界固定，但摄像机独立移动时烟片仍随摄像机转。
改动：
- 锚点 = 视图所属玩家的角色**眼位**（`camera.GameWidget.PlayerData.ComponentPlayer.ComponentCreatureModel.EyePosition`；无角色的视图（发行版没有）退回摄像机）。多玩家分屏各视图各取其角色。第一人称下摄像机在眼位上，与 r3k 完全一致。
- 每片烟由三张同位置的片组成（`ScGrenadeVisuals.SmokeQuads`）：朝向角色的主片（球面广告牌，世界上方向保持直立）、与之正交的垂直片、水平片。三张都不读摄像机位置；角色移动时前两张一起转。原因：摄像机离开角色（第三人称/环绕/调试视角）时单张朝角色的片会被侧面/俯视看成一条线，云会漏光——这是"不退回朝摄像机"的代价，用厚度补。
- 摄像机仍用于投影、深度排序、裁剪、烟内叠加与距离剔除。
- 离线回归：`SmokeViewRegression` 新增"角色不动、摄像机在别处（第三人称后上方、环绕角色 5 m × 三个高度 × 八个方位、远侧 8 m、云顶正上方 12 m）"的深视线覆盖检查（三片合成）；`SmokeMotionRegression` 的裁缩检查扩到三片。VPS 本地夹具：29/29 通过，摄像机偏离时深视线最低覆盖 full 0.930 / growing 0.988 / fading 0.979。
- 填充率：每片烟三张四边形（Full 384、Lite 192 张），比 r3k 多两倍，Windows 实测帧率未量化，Android 未测。

## 2. 调试视角/BUMANCAMERA 射击语义

核对（反编译 1.9.3.1 `SubsystemMusketBlockBehavior.OnAim` Completed 分支）：原版火枪发射点 = `EyePosition + Body.Matrix.Right×0.3 − Body.Matrix.Up×0.2`，方向 = `Normalize(origin + aim.Direction×10 − origin)` = `aim.Direction`，不用摄像机位置重选汇聚点。`DebugCamera`：`IsEntityControlEnabled=true`、`m_position/m_direction` 由输入自由移动。`BumanCamera`：Everything 索引里没有同名文件；扫描本机 DLL/scmod 内部字符串（作业 `r4-find-bumancamera-01`）找到来源——第三方模组 `[API1.8]生存开透视角.scmod`（作者"不满的汤"，`生存模式开透视视角.dll` 7680 B `a6622935…`，位于 `D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods/`）。反编译：`Game.BumanCamera : BasePerspectiveCamera`，公开 `m_position/m_direction`，`UsesMovementControls => true`、`IsEntityControlEnabled => true`，`Activate` 从上一个摄像机取位置/方向，`Update` 按 CameraMove/CameraLook 输入自由飞行；`bumanModLoader` 通过 `OnCameraListInit`/`ManageCameras` 钩子 `gameWidget.AddCamera(new BumanCamera(gameWidget))`。即：与 DebugCamera 同类的"角色可控 + 摄像机自由"视角，r3k 的汇聚规则在它上面同样把摄像机平移变成射击变化。测试把该 scmod 一并复制进测试运行时的 Mods，按类型名找到摄像机，用反射设置位置/方向。
根因：r3k 的 `ScAimRay.Resolve` 用摄像机射线首个命中点作瞄准点再从眼位汇聚，并在摄像机距眼位 ≤0.75 m 时直接接受摄像机原点——摄像机平移会改变射击方向（汇聚点变）甚至原点。
改动：`ScAimRay.Resolve(player, aim, melee)` = 从 `GunOrigin`（眼位 + Right×0.3 − Up×0.2，刀为眼位）沿 `aim.Direction` 的射线；不再读地形/身体、不汇聚、无 0.75 m 分支。调用点：枪 `Fire`/客户端 `SendInput`、刀 `ViewRay`、服务器 `ScNetGuns.FromEye`（服务器仍以自己的角色侧原点重定）。曳光/枪口沿用 r3k（第一人称枪口缓存按摄像机校验、第三人称按骨骼）。R8/Glock 切视角取消不变。
测试（`sp_camera_fire.py`）：所有"射线原点=眼位"改为"=原版原点"；新增 DebugCamera：角色不动，摄像机置于侧方同向 → 原点=原版原点、方向=摄像机方向；仅平移（上 4 m 后 3 m）→ 原点与方向与上一发相同；仅转 20° → 方向随之、原点不变；原版火枪在同一调试视角与第一人称下通过 `OnAim(Completed)` 发射，读取弹丸初始位置/方向对照 CS。第三人称"注入射线越墙"改为只借方向、原点仍在角色。

## 2b. 验收平台改为隔离的 SurvivalcraftAPI 1.9.3.1（用户 2026-10-01 01:40："你用1.9.3.1测试呗"；AGENTS.md 同日更新）

此前 r4a 的单机运行时测试（camera-fire、smoke-motion、smoke-view）都跑在 1.9.3.2_MP 快照的单机模式上（`m0.Game` + 快照自带的 Survivalcraft.TestAutomation 模组），只是自动化方便；按新规则，单机验收必须在隔离的 1.9.3.1 副本上做，MP 只验多人路径。第 3 节里 r4a 的 MP 引擎结果保留为历史证据，**不作为单机验收**。
做法（测试基础设施，不进发行包）：
- `tools/MpM0/TestAutomation131/`：把快照的 TestAutomation 模组移植到 1.9.3.1（`ScTestAutomation131`）——同一套回环 UDP 命令口（KEYDOWN/KEYUP、MOUSE*、MOVE_INPUT、INTERACT/HIT/DIG/AIM_RAY、DROP_ONCE、CLICK_WIDGET、FUNC/EXEC，FUNC 用 Roslyn 按已加载程序集编译 C# 片段）和日志口（新增 `UdpLogSink`，`Log.MinimumLogType = Verbose`，因为 1.9.3.1 的 `Entered screen "…"` 是 Verbose 级）。1.9.3.1 程序不解析参数，端口经 `SC_TEST_CMD_PORT/SC_TEST_LOG_PORT` 环境变量传入。物理键鼠仍用 Harmony 静音（1.9.3.1 的 `Keyboard.KeyDownHandler`/`Mouse.MouseDownHandler`/`Mouse.BeforeFrame`/`ComponentInput.Update`），注入走引擎自己的 `Keyboard.ProcessKeyDown`/`Mouse.ProcessMouseDown`/`ProcessMouseMove`（1.9.3.1 里都是 public）。反编译 1.9.3.1 核对过：`ModsManager.Dlls` 以程序集全名注册、`AssemblyResolve` 回退到已加载程序集，故 Roslyn 的 DLL 随 scmod 一起加载即可；`GameWidget.FindCamera<T>(bool)`、`SubsystemMusketBlockBehavior.OnAim`、`m_projectiles`、`ScreenCaptureManager.Capture(int,int,string)`、`ShowGuiInScreenshots` 等脚本用到的 API 与快照一致；主菜单/新世界按钮名同为 Play/NewWorld/Play；1.9.3.1 没有"Player"屏，NewWorld→GameLoading→Game。
- `m0.py`：`ta131` 步（分离副本构建并打包 `pkg/ScTestAutomation131.scmod`，成员=模组 DLL + Microsoft.CodeAnalysis*.dll，记录 `pkg/ta131.json`）；`Game131`（固定运行时下的 `sp131` 文件夹：从纯净下载镜像引擎文件、不带用户状态目录与玩家的模组设置文件，Mods 只放被测包；同一把锁/作业对象/进程清点；`"Player into playing."` 等待映射为 `Entered screen "Game"`）；`m0.game()` 按 `M0_ENGINE`（mp/131）选引擎并补上自动化模组；`m0.enter_world()` 两引擎共用的进世界流程。三个单机脚本改用它们，并把 `engine`（版本、目录、exe/DLL/模组哈希）写进结果 JSON。
- 提交器把 `M0_ENGINE` 带进 Windows 作业。r4a 在 MP 上的 camera-fire 04 复跑已取消，改在 1.9.3.1 上跑。
- 新增 `sp_probe.py`（先于长场景跑的前置探针：模组加载、FUNC、进世界、键位注入、截图）。1.9.3.1 探针发现并修正的两处引擎差异：`SubsystemPlayers.MainPlayer` 是 MP 快照独有（`mp_m1.MAIN` 改为反射取 MainPlayer、否则取 `PlayersData[0]`（1.9.3.1 的 PlayerIndex 从 1 开始，按 0 查找会找不到；自动化模组的输入注入同样改为"世界里的第一个玩家"））；1.9.3.1 新世界同样先进 Game 屏再由 `SubsystemPlayers` 切到 Player 屏（PlayButton 加入玩家数据后回 Game 屏、地形就绪后才生成玩家实体），`enter_world` 改为两引擎都经 Player 屏、1.9.3.1 再轮询玩家实体存在。探针 01–03 的失败（`bcf8…` 之外的作业 `83ec0a42…`/`f9b474b0…`）均是测试流程问题，引擎/模组/端口/Roslyn 在 1.9.3.1 上首跑即通。

## 3. 作业与结果

### 3a. 隔离 1.9.3.1 副本上的单机验收（r4a devbuild 轻量包 `006d3b99…` + Buman 视角模组）

引擎身份：`D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1` 镜像到 `.tmp/mp-test-runtime/sp131`，`Survivalcraft.exe` `37c5f9bb…`、`Survivalcraft.dll` `25816c2f…`、`Engine.dll` `3845e53e…`；自动化模组 `ScTestAutomation131.scmod` `654e4fdb…`。
- 探针 06（`a64f0227…`）：5/5 通过（模组加载、FUNC、进世界并生成玩家、键位注入、截图 1.19 MB 可读，无引擎错误）。
- camera-fire（`003364bb…`，`run/camera-fire-r4a-lite-131-20261001-010503/`，`results/camera-fire-r4a-lite-131.json`）：**24/24 通过，0 引擎错误**。逐项：第一人称按钮/键位从原版原点命中；FlyCamera 不开火不耗弹；R8 拉锤切视角不出弹；Glock 连发切视角丢弃剩余；第三人称隔墙从原版原点判伤（`eye −105.50,72.55,374.50 → origin/ray −105.80,72.35,374.50`，方向=摄像机方向 `−0.000,−0.084,0.996` 对 `dir −0.002,−0.087,0.996`）、去墙命中、曳光在身体枪口（`tracer −105.52,72.75,375.53 bone muzzle`）；越墙注入射线只借方向、原点仍在角色；环绕视角从角色原点开火；创造飞行第一人称仍开火；双枪第三人称两发来自左右枪口（`muzzle_l` / `muzzle_r`）；**DebugCamera**：置于角色侧方 4 m（`camera −101.50,73.55,374.50`）→ `origin −105.80,72.35,374.50`、`dir 0.003,−0.120,0.993` = 摄像机方向；仅平移（上 4 m 后 3 m）→ 原点/方向与上一发相同；仅转 20° → `dir 0.345,−0.118,0.931` 跟随 `camera-dir 0.340,−0.120,0.933`、原点不变；**原版火枪**从同一调试视角开火：弹丸 `−105.53,72.26,375.24`、方向 `0.342,−0.107,0.934`，在规则原点 `(−105.8,72.35,374.5)` 沿摄像机方向的射线上（引擎把起点推到射手包围盒外约 0.8 m），与 CS 弹道同线同向；第一人称火枪/CS 同规则（`rule-origin −105.96,72.35,373.68 = CS ray`）；**BumanCamera**（`Game.BumanCamera`，control True）：侧方→原点=角色规则原点、方向=摄像机方向（`camera-dir −0.023,0.101,0.995` 对 `dir −0.027,0.104,0.994`）；仅平移→原点/方向相同；仅转→方向跟随、原点不变。方向容差 .05（枪散布量级）。
- smoke-motion 首跑（`b1d6b60b…`，`run/smoke-motion-r4a-lite-131-20261001-010705/`）在第 177 帧（路径 approach-45-4m-and-back）失败：引擎日志显示该帧前 0.2 s 游戏窗口失去焦点（GameScreen 的 Deactivated 处理器"设置已保存/世界已保存"），随后 `Engine.Window.Size` 为 0×0（窗口被最小化），`ScreenCaptureManager.Capture` 抛 `'width'`。诱因未查明（笔记本上的其他窗口/交互）。测试侧修正：`KEEP_ACTIVE` 每帧检测 0×0 并经反射把 `Window.m_view.WindowState` 恢复为 Normal，`capture()` 遇最小化先恢复并返回 "minimized"、`snap()` 重试；两个烟雾脚本改为分析完帧后才释放运行时（此前先释放，与同时排队的候选测试作业的归档步骤冲突：`7eeaebfa…` PermissionError、本次分析 FileNotFoundError）。重跑排队（顺序链，不再并行两条链）。
- smoke-motion 复跑（`4d939784…`，`run/smoke-motion-r4a-lite-131-20261001-013413/`，`results/smoke-motion-r4a-lite-131.json`，386 帧三联截图 + 每条路径 `frames-*.gif` 与带 UTC 时间戳/眼位/seen 的 `sheet-*.jpg`）：无失败、无引擎错误。**验收 1（角色与烟不动、只动摄像机）**：环绕 10 m 17 帧、升 1→12 m 13 帧、横移 12 m 13 帧——探测烟片法线逐帧最大转角 0.000°、与"指向角色眼位"的偏差 0.000°（接触表 `sheet-cam-orbit-10m.jpg` 17:50:49–17:51:55 UTC：摄像机绕角色一周，云团始终是实心团块，无侧面透光）。**验收 2（摄像机不动、只动角色）**：横移 12 m 13 帧——法线逐帧转 ≤7.41°（角色每帧移 1 m），与指向眼位的偏差 ≤0.54°。第三人称原地转 31 帧法线转 ≤0.74°、横移 41 帧 ≤2.95°，偏差 ≤0.63°。遮挡（seen，0=全遮）：第一人称横移 8 m 0–0.32、接近 4 m 往返 0、原地转 0、环绕 7 m/快速掠过/升高 0–1.0（路径经过标记与烟不共线的位置，与 MP 引擎同路径一致）、第三人称原地转 0–0.009、横移 0.001–0.57。与 MP 引擎白天复跑（`1dd1ee3d…`）各路径数值一致（差异 ≤0.03、角度 ≤1.7°）。
- 候选 M2 轻量（MP，`fb12384c…`）31 项 8 失败——全部是"身体/头部命中"类：服务器轨迹 `ray −107.70,72.35,504.00 dir 0.0663,−0.0763,0.9949`，即射线已按原版规则从眼位右 0.3 m 出发，而测试的 `aim_at`/`aim_head` 仍按"从眼位指向目标中心"取方向，平行偏移 0.3 m 后在 7.5 m 外擦过 0.6 m 宽的玩家包围盒边缘未命中；客户端也因此没有命中标记/音效。这是原版火枪语义的固有几何（用户明确要求，准星线与弹道线平行相距 0.3 m），不是产品缺陷：测试改为从枪口规则原点指向目标中心取方向（输入射线起点仍为眼位，服务器照旧在自己角色上重定原点），工作台交互射线不变。M2 重跑排队。
- smoke-view（`6b271303…`，`run/smoke-view-r4a-lite-131-20261001-011929/`，`results/smoke-view-r4a-lite-131.json`；白天、无天气、无 GUI）：无失败、无引擎错误；标记可见比例（seen，0=完全遮挡）full：站立 8 m 0–0.020、近 5 m 0–0.014、抬高 5 m 0.005–0.012、远蹲 17 m 0、侧偏 0–0.003、烟内 0–0.001、第三人称 8 m 0–0.027、高 9 m 俯视 0.59–0.66（越过云顶看到标记，几何使然，与 c18/MP 一致）；growing/fading 同量级（抬高 5 m 分别 0.10–0.13 / 0.04–0.05）；HE 开口：侧偏 0.41–0.46（开口处看得见）、其余 ≤0.14；墙场景与 full 一致。与 MP 引擎同脚本（`37d95feb…`）逐项相差 ≤0.02。第一/第三人称、烟内、HE 开口的遮挡回归在 1.9.3.1 上成立。
- 候选 M2 轻量复跑（MP，`c383fe31…`，`run/m2-r4x-lite-*`）：**31/31 通过**——服务器轨迹 `ray 597.80,72.35,−116.50`（= 眼位 + Right×0.3 − Up×0.2）、方向为客户端瞄准方向；护甲吸收（150→135）、受击（1→0.873）、受击标记/凯夫拉音、射手命中确认、头盔吸收（100→82）与 dink 音、刀击、工作台、修理、合成、护甲制作、布局不同客户端被拒、三方无错误日志。多人射击路径在 r4x 候选 + 适配层上成立。
- 候选 camera-fire（1.9.3.1，`dev-r4x-lite.scmod`）第 2 次（`53a40ee6…`）24 项 4 失败，全是"命中牛"类（`hit none`）：本次牛的实际中心比出生点偏左约 0.3 m，`look_at` 仍按"摄像机中心射线碰到目标"选姿势，选中了准星线擦到牛左缘的姿势，而弹道线在准星线右 0.3 m 处，从牛旁掠过（原点/方向语义本身与第 1 次完全一致：`origin −132.80,72.35,254.49 = eye + Right×0.3 − Up×0.2`，`dir` = 摄像机方向）。与 M2 同一几何原因：0.6 m 宽的牛在 7.5 m 外，准星线与弹道线平行相距 0.3 m，命中与否取决于目标实际位置的厘米级差异。测试改为按"枪线"（规则原点沿摄像机方向的射线 `GUNLINE`）选姿势。第 3 次（`90070f6f…`）同样 4 失败：`look_at` 的候选方向来自 `aim_at`，而它用身体**当前**矩阵算枪口原点——转身前身体朝 −z、Right=+x，转向目标后 Right=−x，原点差 0.6 m，选出的姿势弹道线偏离目标中心 0.6 m。改为 `aim_facing`：按"转向目标后的偏航"预测 Right/Up 再算原点与方向（`look()` 正是用该偏航设置身体旋转）。第 4 次（`c32c6da0…`）：按键开火命中（`hit 24`，牛 1→0.143），但屏幕开火按钮的两次按下都没有产生射击（`LastShotDebug` 为空），第三人称两次按钮开火也为空，随后脚本解析空结果崩溃（8 项 3 失败）。服务器日志：按钮由两次 FUNC 调用 `SetFireButton(true/false)` 注入，相隔 130–160 ms，而第二次 FUNC 的 Roslyn 编译本身在主线程上占用约 100 ms；按键走 `Keyboard.ProcessKeyDown/Up`，不编译、不阻塞。推断（未直接测帧）：这次按下与松开之间没有跨过一次枪械更新；第 3 次同一路径却出了弹，故为间歇性的测试注入时序问题——真实触屏按钮按住的是整帧。测试改为一次 FUNC 按下并由 `Window.Frame` 处理器在 3 个引擎帧后松开（`button_frames`），并让空结果使比较失败而不是崩溃。**第 5 次（`cd49cb06…`，`run/camera-fire-r4x-lite-131-*`）：24/24 通过，0 引擎错误**——第一人称按钮/键位命中（`origin −97.80,72.35,−79.49`、牛 1→0）、第三人称隔墙/去墙/曳光、注入射线、环绕、创造飞行、双枪、DebugCamera 侧方/平移/转动、原版火枪对照（调试视角与第一人称）、BumanCamera 侧方/平移/转动。
- 候选一致性：最终候选阶段 r4x（`r4-pipeline-r4x-all-01`，`eaec5284…`）的 `source-hashes.json` 与 r4a devbuild 的 `src/` 285 项完全相同，`builds.json` 六个 DLL 哈希逐项相同（lite/core `5a765daa…`、full/core `5ee70d8c…`），即 r4a 轻量测试包里的 DLL 与候选包里的字节一致；r4a 的 1.9.3.1 运行时证据对候选成立，不再对候选重复烟雾录像。
- 观察（非回归，r3k 规则）：调试/Buman 视角"仅转动 20°"后曳光起点记录为射线原点（`tracer = ray`）——`ScThirdPerson.TryGetMuzzleWorld` 只在身体模型最近 2 帧被绘制过时给出枪口骨骼位置（`Time.FrameIndex − state.Frame > 2 → false`），摄像机转开后角色不在画面里、没有第三人称绘制，曳光便从射线原点出发（画面里本来也看不到角色）；"侧方"一发角色在画面内，曳光仍从枪口骨骼出发（`tracer −105.52,71.97,375.62 bone muzzle`）。

### 3b. 历史：r4a 在 1.9.3.2_MP 快照单机模式上的结果（平台规则更新前；不作为单机验收）

- r4a 构建：devbuild 通过；sppkg 首次因 `m0.py` 断言"基础包不含 `Net/ScCsgoNet.bin`"失败（r3k 交付后 output 已含适配层）——改为复制时剔除该成员（单机测试包仍不含适配层），重跑通过。
- r4a `visual`（`38ae5672…`）：轻量/全量 0 失败；新增"摄像机偏离角色"覆盖：full/growing/fading 深视线最低 0.906/0.987/0.977（轻量）、0.913/0.986/0.978（全量）。
- r4a camera-fire 首跑（`1a26a261…`）：前 9 项通过（第一人称按钮/键位命中、FlyCamera 不开火、R8/Glock 取消、第三人称隔墙从原版原点判伤、去墙命中、曳光在身体上），随后脚本自身 `vec` 误用中止；已修，复跑排队。第三人称一发的记录：`eye −88.50,72.55,619.50 origin −88.80,72.35,619.50 camera-dir 0.000,−0.084,0.996 ray −88.80,72.35,619.50 dir 0.003,−0.086,0.996`——原点=眼位+身体右 0.3−上 0.2，方向=摄像机方向，不再汇聚。
- r4a camera-fire 复跑 03（`bcf816b0…`，24 项/11 失败）：失败全部是测试度量问题，不是射击语义问题——(1) 方向容差 .002 小于枪本身的随机散布（每发约 .005，例 `dir 0.002,−0.115,0.993` 对 `camera-dir 0.000,−0.120,0.993`）；(2) 原版火枪弹丸位置读到的是引擎 `SubsystemProjectiles.CanFireProjectile` 推到射手包围盒（+0.4 m 余量 +0.1 m）之外的起点，即沿方向前移约 0.8 m（例 `−77.52,72.26,−85.76` 对规则原点 `−77.8,72.35,−86.5`、方向 `0.352,−0.107,0.930`，横向偏差 ≈0）；(3) 火枪 `ApplyImpulse(−4×方向)` 的后坐把角色推动，之后沿用的原点采样过期。语义结论不变：每一发 CS 的射线原点都等于眼位+右 0.3−上 0.2（例 `eye −77.50,72.55,−86.50 → origin/ray −77.80,72.35,−86.50`）；仅平移摄像机（上 4 m 后 3 m，`camera −73.50,77.55,−83.50`）原点/方向与上一发相同；仅转 20° 时方向跟随、原点不变；BumanCamera 已加载并在其上开火（`Game.BumanCamera : BasePerspectiveCamera control True movement True`）。脚本改为：方向容差 = 散布量级（.05 ≈ 2.9°，远小于所测 20° 转动）；火枪弹丸判"在规则原点沿方向的射线上（前移 <1.5 m、横向 <0.12 m）"；每发前重新采样规则原点。复跑 04 排队在白天烟雾复跑之后。
- r4a smoke-motion（`1dd1ee3d…`，`run/smoke-motion-r4a-lite-20260930-232654/`）：**摄像机单独移动（角色不动）——环绕 10 m/17 帧、升 1→12 m/13 帧、横移 12 m/13 帧：探测烟片法线逐帧最大转角 0.000°，与"指向角色眼位"的偏差 0.000°**；**摄像机固定、角色横移 12 m/13 帧：法线逐帧转 ≤7.4°（角色每帧移 1 m），与指向角色眼位的偏差 ≤0.54°**；第三人称原地转动 31 帧：法线转 ≤1.8°（头部转动带动眼位几厘米）、横移 41 帧 ≤3.7°。接触表目视（环绕 10 m、快速掠过、第三人称横移）：云团从各方向都是实心团块，侧面/俯视无透光，随视点呈体积视差。缺陷：这次录像后半段世界已入夜，画面很暗，"seen"（标记被遮挡比例）指标在夜景下失真（有烟/无烟两帧都暗）——脚本改为开局锁定白天，遮挡数据以复跑为准。
- r4a smoke-view（`819c705d…`，c18 的场景脚本：full/growing/fading/HE 开口/墙 × 站立 8 m/近 5 m/高 9 m/抬高 5 m/远蹲 17 m/侧偏/烟内/第三人称 8 m × 每处 4–10 个视角）：无失败、无错误；full：站立 8 m seen 0–0.012、近 0–0.055、抬高 5 m 0.005–0.018、远蹲 0、侧偏 ≤0.001、烟内 0、第三人称 0–0.045、高 9 m 0.59–0.66（从 9 m 高处越过云顶看到标记，几何使然）；每处的多个视角之间数值几乎不变（转头不改变遮挡）。与 c18 同脚本结果的逐项对照见作业 `r4-smoke-view-compare-01`。

## 4. 交付（标准授权，门通过后）

- 发布管线 `r4-pipeline-r4x-all-01`（`eaec5284…`，7.4 min）：failed steps `[]`；vf/ai/c4 全量与轻量 0 失败，main-full 13945 项、main-lite 14251 项 0 失败；tactical-full-suite 107 项中 4 项为已登记的已知项（`native-gltf/ct`、`native-gltf/t`、`npc-full-registry-reload-refuses-without-ammo-loss`、`optional-package-identity-and-no-bundled-engine`），其他 0。复用：clips（c11）、资源包、适配层成员、基线缓存（输入身份一致）。
- 交付作业 `r4-deliver-r4x-01`（`a39430ac…`）：核对 output=清单、候选=管线记录，同盘移动、回读哈希一致，清单改写并保留历史。成员对比：全量/轻量只有 `ScCsgoKnives.dll` 变化（1708/1342 项未变），探员包字节不变；适配层 `Net/ScCsgoNet.bin` 仍为 `93415266…`。
  - 全量 `[API1.9]CS武器1.4.0-全量包.scmod` 526,729,186 B `c705aa7f6cee80ddc54e42f76ddd1e5ff138cd0bb80cdf6c396abc204ff2e7db`
  - 轻量 `[API1.9]CS武器1.4.0-轻量包.scmod` 35,603,708 B `73d76dbe9debe94da90b4fa74fe9ee0a8601f3b50a313f80bf1543eb2e77031a`
  - 探员 `[API1.9]CS武器1.4.0-探员包.scmod` 38,463,564 B `8caf671427759efecb774d8df301d9269b2b964f5980cafa89ad7e4ecc797071`（未变）
- `tools/completion_140.py` 的 BASELINES 已指向上述哈希。

## 5. 清理（标准授权，交付后）

作业 `r4-cleanup-do-01`（`55c430f0…`），回执 `docs/tasks/round4-smoke-facing-camera-shots-20261001-cleanup.json`：永久删除 12,216 项、10.37 GB——r4a/r4x 构建树（日志、报告、builds/net/packages/source-hashes 保留）、4 个测试包副本（sp-r4a-*、dev-r4x-*）、r4a 各录像/场景运行的原始 PNG 帧（GIF、接触表、JSON 保留）。删除前后 output 三个包哈希不变；E: 可用 134 GB（13.4%）。1.9.3.1 副本本身与 `ScTestAutomation131` 保留供后续测试。

## 6. 耗时（2026-09-30 23:57 → 2026-10-01 03:22 JST，约 3 h 25 min）

- Windows 作业计算时间（按作业开始/结束）：构建/打包/管线约 10 min（devbuild 1.0、sppkg 0.6、pipeline 7.4、candpkg 0.1、ta131 0.3）；MP 快照上的单机录像/场景 82 min（烟雾录像 2 次 55 min、场景 2 次 27 min——其中第 1 次因夜景失效重跑；随后按新平台规则只作历史证据）；1.9.3.1 上 58 min（camera-fire 1.9、smoke-motion 首跑 12.1 失败 + 复跑 27.4、smoke-view 14.4）；候选门 12 min（camera-fire 5 次 8.3、M2 2 次约 3）；交付+清理约 3 min。
- 返工：测试脚本度量问题（方向容差、火枪弹丸起点、后坐）1 次；MP→1.9.3.1 平台切换（移植自动化模组、6 次前置探针）约 25 min；两条链并行导致的运行时冲突 1 次；0.3 m 枪线几何导致的目标姿势选择 3 次；按钮注入时序 1 次。产品代码在 r4a 之后未改。
- 等待：作业之间的空档（修脚本、Syncthing 同步）未单独计时。

## 7. 未测/剩余不确定

- Android 未测；三片四边形的填充率成本（Full 384、Lite 192 片）Windows 帧率未量化。
- 多人**同屏分屏**各视图各自锚点：只有代码路径，未实机；多机真人联机未测（M2 为本机多进程）。
- 蹲下/奔跑/跳跃中的烟片朝向与射击原点未单独录制（原点随眼位与身体矩阵，按原版规则变化）。
- 1.9.3.1 副本上的一次窗口最小化诱因未查明（测试侧已能恢复）。按钮注入时序的根因是推断（未逐帧计数）。
- BumanCamera 以外的第三方自由视角模组未测。
