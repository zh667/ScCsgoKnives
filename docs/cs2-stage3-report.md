# 阶段 3 报告：CS2 摆放链与相机

日期：2026-09-04。版本：0.16.0。上一版：0.15.12（阶段 2）。

## 1. 关键发现：CS2 的第一人称动画就摆在相机空间里，没有摆放要解

`tools/cs2_placement.py --measure` 直接从 clip 量出来的：

| 枪 | `wpnEnd`（枪身尾端） | `muzzle` | trigger→muzzle 方向 |
|---|---|---|---|
| ak47 | [0.383, −4.964, −2.968] | [37.422, −4.938, −3.394] | [0.999, −0.001, 0.047] |
| m4a1s | [−0.018, −5.353, −3.335] | [39.583, −4.846, −3.367] | [0.994, 0.006, 0.110] |
| awp | [0.703, −4.839, −3.168] | [55.032, −5.019, −3.428] | [0.999, −0.004, 0.045] |

`root_motion` 在原点，枪身尾端 x≈0，枪口在 +x 三四十英寸处，y≈−5（Source 的 y 是左，负 = 右），
z≈−3（眼睛下方）。**这就是标准 Source view space（x 前、y 左、z 上，原点在眼睛）。**

所以 CS2 的 rig 本来就摆好在相机的坐标系里，整条链只剩四步：坐标轴换、英寸换算、
`viewmodel_offset`、按 `viewmodel_fov` 建投影。CS:MC 那条链里的 hip/aim/roll/48° 全部不需要，
也不需要拟合任何东西。

## 2. 做了什么

1. `tools/cs2_placement.py`：离线参照实现 + `--measure`。
2. `src/ScCsgoKnives/Animation/Cs2Rig.cs`：读 `<gun>.cs2.animation.json`，按 CS2 的 64 根骨采样，
   part 用 `Right * boneAbsolute * Left` 摆放（和 `CsmcKnifeRig` 同一条生产式）。
3. `src/ScCsgoKnives/Rendering/Cs2Placement.cs`：上面那四步。
4. `CsmcFirstPersonRenderer.DrawCs2`：cs2 profile 的武器通道，SC 自己的身体运动仍然叠在上面。
5. `KnifeTuning.GunProfile`（0=csmc，1=cs2，**默认 0**）与四个 viewmodel cvar，改文件 1 秒生效。
6. `ArmPreview cs2` / `cs2sweep` 两个无头模式；`tools/cs2_placement_selftest.py`；`tools/cs2_videocheck.py`。

## 3. 数值来源

`viewmodel` cvar 是**从你本机 CS2 配置直接读的**，不是默认值也不是估计：
`D:\steam\userdata\1415980225\730\local\cfg\cs2_user_convars_0_slot0.vcfg`（`"name" "zh667"`）——
`viewmodel_fov 68`、`offset_x 2.5`、`offset_y 0`、`offset_z −1.5`、`zoom_sensitivity_ratio 1.0`。
同机另外四个账号里 1485560074 是 CS2 默认的 60/1/1/−1，没取那个。

## 4. 验收数字

### A. 出厂 C# 与离线参照一致

`python3 tools/cs2_placement_selftest.py`，14 个用例（三把枪 × idle/deploy/reload/inspect/shoot/attach 若干时刻），
每个比 8 个地标的视空间坐标与 1920×1080 屏幕坐标：

| 指标 | 最差 |
|---|---|
| 视空间 | **5.360e-06 m**（5.4 µm） |
| 屏幕 | **0.0504 px** |

这个量级就是 C# 单精度对 Python 双精度的舍入，不是"容差"。判定门限设在 5e-5 m / 0.1 px，
比阶段 3 的 10 px 目标紧 200 倍，真出偏差照样会红。

### B. 画出来的剪影落在解析预测上

`ArmPreview cs2sweep` 出的 part 矩阵喂给 `tools/pbr_emulate.py`，1920×1080 渲 AK 的 idle：
画出 147,587 px，包围盒 x 1162–1870、y 619–1079（右下角，符合 CS2 的 viewmodel 布局）。
四个预测地标离最近的已画像素：

| 地标 | 预测屏幕坐标 | 离最近已画像素 |
|---|---|---|
| muzzle | (1172.2, 679.6) | 0.4 px |
| wpnTip | (1169.7, 678.4) | 0.5 px |
| trigger | (1523.3, 996.4) | 0.5 px |
| clip | (1378.2, 1040.9) | 0.2 px |

（剪影主轴的极值点离 muzzle 骨 58.6 px，那是准星座/导气箍的顶角，不是误差。）

### C. 阶段 1、2 的自检重跑

绑定矩阵这次改了空间（见 §6.1），所以两份自检都重跑：
`tools/cs2_rig_selftest.py` **A/B/C/E PASS**，`tools/cs2_mesh_selftest.py` **A/B/C/D/E PASS**。

### D. 没坏

| 检查 | 结果 |
|---|---|
| `tools/verify_cs.py` | 与 0.15.10 逐行相同，22 把全 ok，PASS |
| `tools/videocheck.py` | 与 0.15.10 逐行相同，最差地标 24.0 px |
| 包内 Assets | 全部与仓库源逐字节相同 |
| 包内 CS2 贴图高频能量 | 3.45–6.33（噪点版约 19）；比 legacy 的 1.7–2.6 高是因为 4096 源下采样保留了更多细节 |
| 包体 | 29.4 MB → **42.7 MB**，244 个条目（+17 个 CS2 OBJ、+9 张 HD 贴图、+3 份 CS2 rig 进 DLL） |

## 5. 还没做完的：与 CS2 录屏叠合

计划书 §3 阶段 3.3 的"地标误差 <10 px @1080p"**没有完成**，缺录屏。
`tools/cs2_videocheck.py` 已经写好：`--extract` 抽帧，把量到的像素坐标填进 `LANDMARKS`，
再跑一次就出百分比；`--fit-fov` 会扫出最贴合的 `viewmodel_fov`。

## 6. 未解决项

1. **改了阶段 2 的绑定矩阵空间（已修，非遗留问题）**：阶段 2 写的 `Right`/`Left` 让 part 输出在
   归一化空间，而 `Cs2Placement` 吃的是英寸——第一次渲染画出 0 个像素才发现。现在
   `Left = 单位阵`，`Right = N⁻¹ P⁻¹ D_rest⁻¹`，输出英寸；静息时 `Right·D_rest` 必须等于
   `N⁻¹P⁻¹`，转换器里断言到 1e-8。
2. **cs2 profile 现在不画手臂**。CS:MC 的拳头/臂盒求解器是对着 CS:MC 的 rig 和摆放量出来的，
   换了摆放就不成立；CS2 自己的手臂和手套是阶段 4。所以现在 `GunProfile=1` 是一把没有手的枪。
3. **`viewmodel_fov` 的换算是本阶段唯一的假设**：Source 把 `fov` 当成 4:3 下的水平视野、
   竖直角固定（Hor+），`fovY = 2·atan(tan(fovX/2)/(4/3))`，68 → 53.668°。
   这是 Source 1 的既定行为，套到 CS2 上**没有本机实测确认**，报告和代码里都标了 ASSUMED，
   等录屏用 `--fit-fov` 定。
4. **动画时长仍由 CS:MC 的 clip 驱动**：控制器按 `*.csmc.animation.json` 的时长计时，
   cs2 profile 只是在同一时刻重采 CS2 的 clip。两边最多差 1 帧（AK 完全一致），
   但 m4a1s 的 inspect 差 9 帧（CS:MC 那条被截断了），阶段 7 应该把计时也切到 CS2。
5. **AK 的 `cliprelease` 在 CS2 的 body_hd 里没有几何**（阶段 2 §4.4），cs2 profile 下弹匣释放钮不再单独动。
6. **开镜**：cs2 profile 复用了现有的开镜遮罩逻辑（AWP 瞄准时隐藏武器画遮罩），阶段 5 才换成 CS2 的真实贴图。

## 7. 估计值清单

| 值 | 来源 | 是否估计 |
|---|---|---|
| rig 在相机空间、x 前 y 左 z 上 | 从 clip 实测（§1） | 否 |
| `viewmodel_fov` / `offset_x/y/z` | 你本机 `cs2_user_convars_0_slot0.vcfg` | 否 |
| 英寸→引擎单位 0.0254 | 定义 | 否 |
| Source→引擎坐标轴变换 | 两边约定都已知 | 否 |
| `fovY = 2·atan(tan(fovX/2)/(4/3))` | Source 1 既定行为，**未在 CS2 上实测** | **是（ASSUMED）** |

## 8. 怎么试

装 `output/ScCsgoKnives-0.16.0.scmod`，把游戏目录旁边的 `ScCsgoKnivesTuning.txt` 里
`GunProfile` 改成 1，存盘，1 秒生效。三把枪会换成 CS2 的网格、材质、动画和摆放，**但没有手臂**（见 §6.2）。
改回 0 立刻退回现在这套。`Cs2ViewmodelFov` / `Cs2ViewmodelOffsetX/Y/Z` 也能现场调。
