# 0.28.2 Mini 版与补给品发黑的诊断

日期：2026-09-06。VPS 侧提交，分支 `fix/cs2-only-hands-0.20.4`。

## 一、补给品（弹匣、霰弹、四种零件、装配台）在物品栏发黑

用户截图 `PHOTO2/Snipaste_2026-09-06_17-28-41.png`、`17-28-50.png`：武器页的通用弹匣、霰弹，物品页的四种零件和装配台，
物品栏图标是纯黑剪影（像素值 (0,0,0)，不是深色贴图）。

事实核对：

- 截图时正在运行的游戏是 **0.26.1**：`Game.log` 从 10:20 启动、17:21 最后一行，中间没有重启；日志开头
  `[ScCsgoKnives CS刀具](Version: 0.26.1)`。0.28.0 为此做的两处改动（贴图改显式 RGBA、物品栏网格顶点设为
  emissive）在这台机器上还没有跑过。
- 静态排查排除了三个候选：物品栏的 `BlockIconWidget` 用 `Light = 15`，`DrawMeshBlock` 在颜色变换为白时直接使用
  顶点色；用游戏自带的 `Engine.dll` 实测 `new Color(0.62f, 0.62f, 0.62f)` 得 (158,158,158,255)，顶点色不是黑；
  贴图 `survival_surface.png` 各格最暗也在 25 以上，没有纯黑区域。同为 RGB PNG 的刀枪贴图在第一人称正常显示，
  所以"RGB 贴图解码成黑"也站不住。剩下的可能只能在游戏里看：实际交给 `DrawMeshBlock` 的贴图对象是什么。
- `Texture2D` 没有读回接口，所以这版加了一条**一次性日志**：第一次画补给品时记录贴图尺寸、格式、mip 层数、
  sRGB、顶点色、颜色变换和环境光。重启进新版后，若仍发黑，把 `Game.log` 里
  `supply mesh first draw` 那一行发来即可定位。

这版没有对发黑本身做新的修改；0.28.0 的两处改动保留。

## 二、Mini 版

用户反馈 Lite（512）与 Full（1024）看起来差不多，想要更小的包。先看包里什么占地方（0.28.1 Lite，zip 内压缩后）：

| 内容 | Lite 压缩后 | 说明 |
| --- | --- | --- |
| DLL（内嵌动画 JSON 15.7 + 刚性件 18.2 + 蒙皮 5.0） | 39.1 MB | 三版相同 |
| 贴图 ORM / 法线 / 基础色 / 刀 | 50.8 MB | 177 张 1024 图缩到 512 |
| 音频 377 个单声道 OGG | 8.9 MB | 三版相同 |
| 其它贴图、模型、图标 | 7.5 MB | |

所以再降贴图只能省贴图这一块。Mini 把同样 177 张 1024 图缩到 256（法线缩后重新归一化，与 Lite 同一套流程），
粒子策略沿用 Lite：

| 包 | 大小 | SHA-256 |
| --- | --- | --- |
| `ScCsgoKnives-0.28.2.scmod`（Full） | 220.1 MB | `2704eb7ae155a123b1035cb5c380c0885d3533e26ca229188ec343c83533190b` |
| `ScCsgoKnives-0.28.2-Lite.scmod` | 111.8 MB | `3b9a599392997a2ee39f646995c9cc924f3efd61d4f1ed0ac795d0d8842becdc` |
| `ScCsgoKnives-0.28.2-Mini.scmod` | 76.9 MB | `9eaef6fe4f699e3261f78f7d32ef35f76ce7d28b38d17730f9e1c938a26f69fc` |

三版同一 DLL、同一物品编号、同一玩法，仍然二选一安装。Mini 的武器贴图在第一人称近看会明显发软，适合只求
体积的手机。打包命令：`python -X utf8 tools/pack_scmod.py --edition all`（`both` 仍是 Full + Lite）。

**再往下就得动 DLL**：内嵌数据里刚性件 18 MB 是 float32 顶点，动画 JSON 是文本；改成 float16/int16 的二进制大约
能把 DLL 从 39 MB 压到 20–22 MB，三版都受益（Mini 约 55 MB）。这需要改 `Cs2Rig`/`Cs2RigidMesh` 的读取和生成脚本，
本版没做。

## 三、验证

- Release 构建成功，0 错误。
- 三版一致性 `tools/check_release_editions.py --mini`：2523 项通过，0 失败。Lite 与 Mini 各缩 177 张；
  0.28.1 的全部资源在 Full 里逐字节保留。
- 无头 DLL 自检（`tools/cs2_runtime_selftest.py --scmod`，VPS）：Full 5843 / 5843、Lite 5843 / 5843、
  Mini 5843 / 5843，全部通过。
- `tools/PackageCheck` 需要显卡离屏绘制，VPS 无显示，跑到一半在 `GraphicsResource` 终结器上崩掉；这一项
  留给 Windows 跑（`dotnet run --project tools/PackageCheck -c Release -- --scmod output/ScCsgoKnives-0.28.2-Mini.scmod
  --vanilla-content 'E:\EdgeDownload\[Windows]SurvivalcraftAPI_1.9.2.1\Content.zip'`）。其中
  `packaged-edition-loads-through-content-manager` 已改成 Lite 和 Mini 都算精简策略。

## 四、实机要做的

1. 重启游戏（当前仍在跑 0.26.1），装 0.28.2 任一版，打开创造背包看弹匣、霰弹、零件、装配台图标。
2. 无论好坏，把 `Game.log` 里 `supply mesh first draw` 那一行发来。
3. Mini 版看 AK / 蝴蝶刀近处贴图是否能接受。
