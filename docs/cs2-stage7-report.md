# 阶段 7 报告：清理与交付

日期：2026-09-04。版本：0.16.3。

## 1. 计划书的阶段 7 与你的规则冲突，规则优先

计划书说"cs2 profile 成为默认"。你的规则 3 说"新管线放在可切换的 cs2 profile 下，**默认仍为 csmc，
直到该阶段验收通过**"。阶段 3/4 的验收（与 CS2 录屏叠合，地标 10 px、手指 10 px）**没有通过——录屏还没有**，
阶段 2 的亮度 5% 和阶段 1 的声音峰值同样在等它。

所以 **`GunProfile` 默认仍是 0**。录屏到位、`tools/cs2_videocheck.py` 跑出 <10 px 之后翻默认，是一行改动。

## 2. 交付物

### 代码与资源（都在 `origin/main`）

| 阶段 | commit | tag | 版本 |
|---|---|---|---|
| 1 动画与骨架 | `6c7d38a` | `cs2-stage1` | 0.15.11 |
| 2 网格与材质 | `a4aa42b` | `cs2-stage2` | 0.15.12 |
| 3 摆放与相机 | `9e5dd74` | `cs2-stage3` | 0.16.0 |
| 4 手臂与手套 | `4c66559` | `cs2-stage4` | 0.16.1 |
| 5 特效与开镜 | `c7cd44f` | `cs2-stage5` | 0.16.2 |
| 6 数值 + 7 清理 | 本次 | `cs2-stage6` / `cs2-stage7` | 0.16.3 |

### 新增的工具（全部在 `tools/`）

`cs2_dmx.py`（二进制 DMX 读取器）、`cs2_viewmodel.py`（rig）、`cs2_glb.py`（GLB）、`cs2_kv3.py`（KV3）、
`cs2_dmx_to_rig.py`、`cs2_glb_to_obj.py`、`cs2_glb_to_skinned.py`、`cs2_sound_timings.py`、
`cs2_effects.py`、`cs2_weapons.py`、`cs2_placement.py`、
`install_gun_textures_cs2hd.py`、`install_arm_textures_cs2.py`、
以及六个自检脚本 `cs2_{rig,mesh,placement,arms,effects,weapons}_selftest.py` 和叠合工具 `cs2_videocheck.py`、
渲染检查 `cs2_render_check.py`。

### 新增的运行时代码

`Animation/Cs2Rig.cs`、`Cs2Sounds.cs`、`Cs2Effects.cs`、`Cs2Weapons.cs`、
`Rendering/Cs2Placement.cs`、`Cs2SkinnedMesh.cs`，以及 `CsmcFirstPersonRenderer` 里的 `DrawCs2` 分支。

### 文档

`docs/cs2-stage1..7-report.md` 六份阶段报告 + 本篇；`docs/first-person-composition.md` 新增"0.16.x：CS2 profile"一章；
每阶段的原始验收数字在 `docs/cs2-stage<N>-selftest.json`。

## 3. 所有阶段的自检，一次跑完的结果

| 脚本 | 结果 |
|---|---|
| `cs2_rig_selftest.py` | A/B/C/E PASS（310/310 DMX 逐字节、IK 目标 0.008 in、接骨 ±1%、回读 6.8e-05 in） |
| `cs2_mesh_selftest.py` | A/B/C/D/E PASS（绑定残差 3.6e-07 in、0 混合权重、legacy 回归 ≤0.02 mm、17 个 OBJ 合规） |
| `cs2_placement_selftest.py` | PASS（C# 与离线参照 5.4e-06 m / 0.05 px） |
| `cs2_arms_selftest.py` | A/B/C/D PASS（48 根承重骨全解析、蒙皮 1.4e-06 m、手指贴枪 0.45–1.11 in） |
| `cs2_effects_selftest.py` | A/B/C PASS（枪口 0.0005 in、7 个粒子系统齐、遮罩 r=0.95） |
| `cs2_weapons_selftest.py` | A/B/C/D PASS（24 个字面值 0 处不符、换算重算 2.6e-05） |
| `verify_cs.py` | PASS，与 0.15.10 逐行相同 |
| `videocheck.py` | 最差地标 24.0 px，与 0.15.10 逐行相同 |

**22 把刀从头到尾没被碰过**：两个刀的验收脚本在六个版本里输出一字不变。

## 4. 全部估计值（跨阶段汇总）

| 值 | 阶段 | 为什么是估计 |
|---|---|---|
| AWP 的武器骨接在 `wpn` 上 | 1 | `viewmodel.vnmskel` 没列 AWP；用手到枪口距离旁证（+0.97%，与另两把一致） |
| `viewmodel_fov` 的 Hor+ 换算（68 → 53.668°） | 3 | Source 1 的既定行为，没在 CS2 上实测 |
| 裸臂粗糙度 0.55、手套粗糙度 0.75 | 4 | CS2 没导出这两个材质的 VMAT |
| 扭转骨的 swing-twist 算法 | 4 | 权重 0.5/1.0 是读的，算法是对约束语义的读法 |
| 曳光带半宽 0.012 单位 | 5 | vpcf 用贴图+半径曲线决定宽度 |
| 火光精灵尺寸 0.09 / 0.035 | 5 | 沿用旧的手调值，没从 vpcf 的半径曲线换算 |
| 后坐的绝对尺度（AK 1.6°/30 单位） | 6 | 比例是 CS2 的，尺度沿用旧手调值 |
| 移动散布的线性插值 | 6 | 两端是读的，插值方式是本移植的选择 |
| 伤害衰减的 `/500` | 6 | 社区文档而非 SDK 源码（两处独立来源一致） |

## 5. 没做的事

1. **四条与录屏有关的验收**（阶段 1 声音峰值、2 亮度 5%、3 地标 10 px、4 手指 10 px）。
2. **每帧 CPU 蒙皮耗时**没实测（VPS 没 GPU）；代码每 120 帧打一次日志。
3. **弹壳**（阶段 5 的可选项）。
4. **逐发喷射图案**（阶段 6）：种子已记录，生成器没有。
5. **护甲比/穿透/爆头倍率**读出来了但 SC 没有对应模型。
6. **另外 11 套手套**：只装了 `weapon_arms.glb` 自带的无指手套。
7. **AK 的 `cliprelease`**：CS2 的 body_hd 没有这块几何，cs2 profile 下弹匣释放钮不再单独动。
8. **动画计时仍由 CS:MC 的时长驱动**：cs2 profile 只是在同一时刻重采 CS2 的 clip。AK 完全一致，
   m4a1s 的 inspect 差 9 帧。
9. **CS:MC 路线保留**：按计划书"保留一个版本周期"。

## 6. 现在能做什么

装 `output/ScCsgoKnives-0.16.3.scmod`，改 `ScCsgoKnivesTuning.txt`：

```
GunProfile = 1        # 三把枪整体切到 CS2（网格/材质/动画/摆放/手臂/特效/数值）
Cs2Arms = 0           # 只看枪，不画手臂
Cs2ViewmodelFov = 68  # 你自己的设置，可现场调
GunSoundProfile = 1   # 只换声音时间，不换别的（GunProfile=1 时自动跟随）
```

存盘 1 秒生效，改回 `GunProfile = 0` 立刻退回原来那套。刀不受任何影响。
