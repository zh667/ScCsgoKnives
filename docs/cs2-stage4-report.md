# 阶段 4 报告：CS2 手臂与手套（CPU 蒙皮）

日期：2026-09-04。版本：0.16.1。上一版：0.16.0（阶段 3）。

## 1. 做了什么

1. `tools/cs2_glb_to_skinned.py`：`weapon_arms.glb` → `AnimationData/cs2_arms.skin`
   （位置/法线/UV/4 骨索引/4 权重 + 82 根骨的逆绑定矩阵，逆绑定已换算到 rig 的英寸）。
2. `src/ScCsgoKnives/Rendering/Cs2SkinnedMesh.cs`：每帧 CPU 蒙皮，走
   `Display.DrawUserIndexed`（SC 没有 GPU 蒙皮，但引擎有这个入口，索引是 32 位，没有 21845 的限制）。
3. `KnifePbrRenderer.TryDrawSkinned`：同一套 PBR 通道吃动态顶点数组。
4. `tools/install_arm_textures_cs2.py`：裸臂与无指手套的贴图。
5. `KnifeTuning.Cs2Arms`（1/0）开关；`ArmPreview cs2arms` 无头模式；`tools/cs2_arms_selftest.py`。

## 2. 蒙皮为什么是对的（实测，不是假设）

`view = Σ w_j · (vertex · inverseBind_j · boneAbsolute_j)`

手臂 GLB 与动画 DMX 共有 55 段骨，**48 段长度对到 0.1% 以内**——这是与姿态无关的比较，
说明两者是同一副骨架、关节局部坐标系一致。对不上的 7 段是 `wpn`/`wpnEnd`/`wpnTip`（绑定姿态下塌缩成 0 长）
和两侧肩关节（绑定姿态两臂张开），属于姿态差异，逆绑定矩阵本来就吸收它。

**四根扭转骨**（`arm_lower_{L,R}_TWIST` / `_TWIST1`）不在动画骨架里，却承担了**网格 15.55% 的权重**。
CS2 自己用 `AnimConstraintTiltTwist` 驱动它们，定义就在 `weapon_arms.vmdl` 里：

```
_class = "AnimConstraintTiltTwist"
  AnimConstraintSlave  parent_bone = "arm_lower_L_TWIST"   weight = 0.5
  AnimConstraintBoneInput parent_bone = "hand_l"           weight = 1.0
  input_axis = 0   slave_axis = 0
```

`_TWIST` 权重 **0.5**、`_TWIST1` 权重 **1.0**、输入是同侧的手、轴 0（骨自身 X）——**这四个数是读出来的**。
把 "tilt twist" 解释成"把手相对小臂的旋转按 X 轴做 swing-twist 分解，从动骨取 `weight` 份扭转"
是本移植的读法（Valve 的约束实现拿不到），这一条在下面标为"建模行为"。

## 3. 验收数字

`python3 tools/cs2_arms_selftest.py`，完整输出 `docs/cs2-stage4-selftest.json`。

### A. 骨架
48/55 段骨长对到 0.1%；**48 根承重骨全部可解析**（44 根在 rig 里，4 根是上面的扭转骨），
没有一根落空。

### B. 出厂 C# 蒙皮 vs 离线参照

| 枪 | clip | t | 顶点 | 最大误差 | 平均误差 |
|---|---|---|---|---|---|
| ak47 | idle | 0.00 | 6274 | 9.511e-07 m | 5.330e-07 m |
| ak47 | reload | 1.00 | 6274 | 1.045e-06 m | 4.884e-07 m |
| m4a1s | deploy | 0.50 | 6274 | 1.408e-06 m | 5.427e-07 m |
| awp | inspect | 2.00 | 6274 | 1.364e-06 m | 6.255e-07 m |

1.4 µm，还是单精度舍入。

### C. 手指确实握在枪上（idle，rig 英寸）

按渲染器真正用的绑定摆放武器（`Right · boneAbsolute`），量手指顶点到最近武器顶点的距离：

| 枪 | 右手 | 左手 |
|---|---|---|
| ak47 | 中位 0.729 in，p90 1.204 in | 中位 0.828 in，p90 1.435 in |
| m4a1s | 中位 0.889 in，p90 1.446 in | 中位 0.623 in，p90 1.053 in |
| awp | 中位 0.448 in，p90 0.898 in | 中位 1.111 in，p90 1.674 in |

手指自身半径就有 0.3–0.4 in，所以半英寸到一英寸的中位数就是"包在枪上"。

### D. 权重
每顶点影响数 {1:1881, 2:2401, 3:804, 4:1188}，权重和恒等于 1.000000。

### 屏幕布局（1920×1080，你的 cvar）
手臂 6274 个顶点里 6122 个在近平面前，其中 3231 个在屏幕内（52.8%），
屏幕包围盒 x 1069–1745、y 716–1080——右下角、下缘出画，就是 CS2 viewmodel 的样子。
`hand_L` 骨投在 (1230, 913)，`hand_R` 在 (2037, 1621)（画面外右下，你的 `offset_x=2.5` 把右手推出去了）。

### 不破坏既有路线
`verify_cs.py` PASS 且与 0.15.10 逐行相同；`videocheck.py` 最差地标 24.0 px 不变；
阶段 3 的 `cs2_placement_selftest.py` 仍 PASS（5.360e-06 m / 0.0504 px）。
包体 42.7 → **46.9 MB**（250 条目，+`cs2_arms.skin` 452 KB 进 DLL、+6 张手臂/手套贴图）。

## 4. 未解决项

1. **计划书 §3 阶段 4.4 的"手指在握把/护木上误差 <10 px"没有完成**：需要录屏。
   §3.C 用的是三维距离，是能离线做到的最强替代。
2. **每帧 CPU 蒙皮耗时没有实测**：`<3 ms` 的目标要在你的 3060 上跑。代码每 120 帧往日志写一次
   `cs2 arms: CPU skinning X ms/frame`，装包玩一会儿看日志就有。VPS 上没有 GPU，量不了。
3. **裸臂与手套的粗糙度是估计值**（0.55 / 0.75）：CS2 这次没导出这两个材质的 VMAT，
   只有 vmdl 和贴图。金属度取 0 不是估计（皮肤和布都是绝缘体）。
   粗糙度烘在 `cs2_arm_orm.png` / `cs2_glove_orm.png` 里，改要重跑
   `tools/install_arm_textures_cs2.py --arm-roughness/--glove-roughness` 再打包——
   着色器没有逐材质粗糙度输入，所以没做成实时可调的旋钮（做了也是死的）。
4. **扭转骨的驱动方式是建模行为**（见 §2）：权重 0.5/1.0 是读出来的，
   把它实现成"按 X 轴 swing-twist 分解后按权重 slerp"是我的读法。
5. **只装了 `weapon_arms.glb` 自带的无指手套**：另外 11 套 `glove_*` 还没做（计划书 §3 阶段 4.1
   本来就说先用自带的）。切换手套需要按同一条路子导出对应 GLB 并装贴图。
6. **裸臂的 AO 贴图是纯白**（均值 255），CS2 那张就是空的，不是转换丢了。

## 5. 估计值清单

| 值 | 来源 | 是否估计 |
|---|---|---|
| 顶点/法线/UV/骨权重/逆绑定 | `weapon_arms.glb` | 否 |
| 米→英寸 39.370079 | 定义 | 否 |
| 扭转骨权重 0.5 / 1.0、输入骨、轴 0 | `weapon_arms.vmdl` 的 `AnimConstraintTiltTwist` | 否 |
| 扭转的具体算法（X 轴 swing-twist + slerp） | 对约束语义的读法 | **建模行为** |
| 裸臂粗糙度 0.55、手套粗糙度 0.75 | 无来源 | **是（估计）** |
| 裸臂/手套金属度 0 | 皮肤与布是绝缘体 | 否 |

## 6. 怎么试

装 `output/ScCsgoKnives-0.16.1.scmod`，`ScCsgoKnivesTuning.txt` 里 `GunProfile = 1`。
现在三把枪是 CS2 的网格 + CS2 的材质 + CS2 的动画 + CS2 的摆放 + **CS2 的手臂和手套**。
`Cs2Arms = 0` 只看枪。玩一两分钟后翻日志里的 `cs2 arms: CPU skinning ... ms/frame`，
那个数字就是 §4.2 要的。
