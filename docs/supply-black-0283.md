# 0.28.3 补给品发黑：贴图在地形线程上被创建

日期：2026-09-07。

## 现象

放置武器装配台，完全退出游戏再进世界：世界里的装配台、手持的装配台、物品栏里的弹匣/霰弹/零件/装配台图标
全部纯黑（`PHOTO2/Snipaste_2026-09-07_00-24-09.png`）。0.28.0 的 RGBA 贴图、0.28.1/0.28.2 的 emissive 图标和
0.28.3 草稿里的"物品栏强制白色变换"都没有改变结果——因为它们改的都不是根因。

## 根因

这七样东西共用一张贴图 `Textures/ScCsgoKnives/survival_surface`，各处都是用到时才
`ContentManager.Get<Texture2D>`，由 ContentManager 缓存。装配台是唯一可放置的方块，它的
`GenerateTerrainVertices` 在**地形更新线程**（`TerrainUpdater` 的 `Task.Run`）上跑，里面又调了
`GetDefaultTexture`。一个已经放了装配台的世界在加载时先生成区块，于是**第一次**加载这张贴图的就是地形线程。

读 `Engine.dll` 的 IL：`Texture2D.Load` / `AllocateTexture` 直接调 `GL.GenTextures`、`GL.TexImage2D`，没有
`Dispatcher`、没有线程检查（只有 `GraphicsResource.Finalize` 用了 Dispatcher）。工作线程上没有 GL 上下文，
得到的是一个空的 GL 对象；ContentManager 把它按名字缓存了一整局，之后物品栏、手持、世界全都采样这一个
坏对象，采出来就是黑。这也解释了为什么只有这几样黑（其它贴图第一次都是在渲染线程加载的）、为什么离线检查
全过（单线程）、为什么改颜色和顶点没用。

## 修法

- `ScSurvivalMesh.Surface`：贴图只创建一次。装配台和补给方块的 `Initialize()`（主线程，任何世界加载之前）
  调 `Preload()` 先把它解析好；`GetDefaultTexture` 只返回这个字段。
- 兜底：若仍有工作线程先来（`MainThread` 已记录且当前线程不同），用 `Dispatcher.Dispatch(..., waitUntilCompleted: true)`
  把加载交给主线程并等待，不在工作线程上建 GL 对象。
- 首次绘制的诊断行现在带线程信息：贴图在哪个线程创建、主线程是哪个、是否经过派发、本次绘制在哪个线程。
- `AGENTS.md` 加了一条：可放置方块在 `GenerateTerrainVertices` 里用到的贴图必须在 `Initialize()` 里于主线程
  先解析，之后只读字段。

保留了 Windows 0.28.3 草稿里的两处改动（图标网格与世界网格同时建、物品栏用白色变换），它们无害。

## 验证

- Release 构建 0 错误。
- 只打了 Lite 包（当前安装的是 Lite，且同步链路慢）：`output/ScCsgoKnives-0.28.3-Lite.scmod`，SHA-256 `0408e21b2c9691f643d32f52a84c05be1154d84e42c748e1d189633bd21b46ab`。
  Full / Mini 用同一提交在 Windows 上 `python -X utf8 tools/pack_scmod.py --edition all` 即可。
- 无头 DLL 自检：Lite 包 5843 / 5843 通过。
- 这个根因离线复现不了（需要真实 GL 上下文和地形线程），只能实机确认。

## 实机

1. Windows 先 `bash tools/sync_git_from_origin.sh main`。
2. 装 0.28.3-Lite，完全退出再进那个已经放了装配台的世界：世界里的台子、手持、物品栏三处都应有颜色。
3. 把 `Game.log` 里 `supply mesh first draw` 一行发来：`texture created on thread N (main thread N …)` 两个数应相同。
