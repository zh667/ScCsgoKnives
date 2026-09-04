# 阶段 1 报告：CS2 动画与骨架进入 rig 管线

日期：2026-09-04。版本：0.15.11。上一版：0.15.10（104876e）。

## 1. 做了什么

1. **写了 CS2 二进制 DMX 读取器**（`tools/cs2_dmx.py`）。格式是 `dmx encoding binary 9 format model 22`，
   没有现成实现可用，布局是从文件本身逐字节反出来的，没有引用或复制任何 Valve / GPL 代码，也不进 mod。
   与常见文档描述不同的一点：**编码 9 的数组类型不是"标量 id + 14"，而是标量 id 或上 `0x20` 位**
   （element array = 0x21、vector3 array = 0x2a、quaternion array = 0x2d）。按旧规则解会在第一个
   `children` 数组上就崩掉。
2. **写了 rig 库**（`tools/cs2_viewmodel.py`）：骨架层级、通道采样、绝对矩阵，约定与 `rigprobe.py` /
   `CsmcKnifeRig` 完全一致（行向量 `v' = v @ M`，`local = R(q) @ T(p)`，`absolute = local @ parent`）。
   导出的每个 clip DMX 自带合并骨架：56 根 viewmodel 骨 + 该枪自己的武器骨，作为 DmeModel 的两个根并列；
   武器根按 `viewmodel.vnmskel` 的 `m_attachToBoneID` 接到 `wpn` 上合成一棵树。
3. **写了自检脚本**（`tools/cs2_rig_selftest.py`，先于功能写），五项检查，见 §3。
4. **写了转换器**（`tools/cs2_dmx_to_rig.py`）：产出 `AnimationData/<gun>.cs2.animation.json` 与
   `AnimationData/cs2_bone_map.json`。
5. **写了声音时间工具**（`tools/cs2_sound_timings.py`）：把 `sound-event-timings.csv` 的帧号按各 clip
   自己的 DMX 帧率换算成秒，产出 `AnimationData/cs2_sounds.json`，并逐条对上 mod 里已有的 OGG。
6. **mod 侧**：新增 `Animation/Cs2Sounds.cs` 读这张表；`KnifeTuning.GunSoundProfile` 在旧表与 CS2 表之间切换，
   **默认 0（旧表）**，CS2 表没有的 clip 一律回落到旧表。

## 2. 来源文件

唯一权威源：`~/workspaces/CSMCReverse/local_cs2_analysis/all_weapons/`。

| 用途 | 文件 |
|---|---|
| 动画本体 | `08_first_person/decompiled/animation/anims/viewmodel/rifle/{rifle_ak,_default_rifle,rifle_awp}/*.dmx` |
| 事件轨 | 同目录 `*.vnmclip`（KV3 文本） |
| 主骨架与接骨点 | `08_first_person/decompiled/animation/skeletons/characters/viewmodel.vnmskel` |
| 逐枪武器骨架 | `.../skeletons/weapons/{ak47,m4a1_silencer,awp}.vnmskel` |
| 声音触发帧 | `08_first_person/sound-event-timings.csv` |
| 事件→文件 | `05_audio/weapon-soundevent-mapping.json` |
| 逐枪 clip 清单 | `08_first_person/first-person-catalog.json` |
| 对照用 CS:MC animbin | `src/ScCsgoKnives/AnimationData/{ak47,m4a1s,awp}.csmc.animation.json`（本仓库既有） |

## 3. 验收数字

完整输出见 `docs/cs2-stage1-selftest.json`，复现：`python3 tools/cs2_rig_selftest.py`。

### A. 解析
`decompiled/` 下 **310 个二进制 DMX 全部逐字节读完**，剩余偏移为 0；另有 36 个 `.dmx` 是
`keyvalues2` 文本（各枪的 skeleton），不走这个读取器。任何一个字节对不上都会抛错，所以这是硬证据。

### B. rig 数学（不依赖 CS:MC 的独立证明）
`hand_L/hand_R` 必须落在动画师对齐用的 IK 目标 `wpnHand_L/wpnHand_R` 上。三把枪全部 15 个 clip
逐帧共 **1842 个采样，最大偏差 0.008104 英寸（0.21 mm）**。四元数取转置的话这个距离会变成约 12 英寸，
所以它同时锁死了四元数约定、乘法顺序、层级和采样。

### C. 武器骨架接骨点
`viewmodel.vnmskel` 只为 13 把枪显式写了 `m_attachToBoneID`，并且全是 `wpn`；AK 和 M4A1-S 在表内，
**AWP 不在表内，`wpn` 是推定值**，用手到枪口的距离验证：

| 枪 | CS2 手→枪口 | CS:MC 手→枪口 | 差 |
|---|---:|---:|---:|
| ak47 | 28.837 in | 28.520 in | +1.11% |
| m4a1s | 29.545 in | 29.243 in | +1.03% |
| awp | 47.148 in | 46.696 in | +0.97% |

三把枪偏差一致（约 +1%），与下面 §4 的骨长差同源，不是接错骨。

### E. 输出 JSON 回读
把 `<gun>.cs2.animation.json` 按 `CsmcKnifeRig` 的采样方式重建绝对矩阵，与直接从 DMX 算的逐帧比：

| 枪 | clip 数 | 最大矩阵元误差 |
|---|---:|---:|
| ak47 | 11 | 6.783e-05 in |
| m4a1s | 7 | 5.936e-05 in |
| awp | 5 | 6.209e-05 in |

即 1.7 µm 量级，来自写出时的 6 位小数取整。这证明转换器里的**预卷帧裁剪和常值曲线压缩是无损的**。

### D. 与 CS:MC animbin 的比对
见 §4，这一项**没有按计划书写的方式通过**。

## 4. 关键发现：CS:MC 与 CS2 不是同一副骨架

计划书 §2.3 与启动提示词都假设"CS:MC 的 animbin 是同一批 CS2 clip 的转码，应当只差单位与骨名"，
并把"逐骨绝对矩阵对上"设为进入阶段 2 的硬门槛。**这个前提经测量不成立。**

证据（全部来自静息姿态的局部平移，与任何约定、任何拟合都无关——骨长只取决于子骨的局部平移长度）：

| 骨 | CS2 静息 \|t\| | CS:MC 静息 \|t\| | 比值 |
|---|---:|---:|---:|
| `hand_R` | 11.083 | 10.296 | 0.929 |
| `finger_middle_1_R` | 1.918 | 1.775 | 0.925 |
| `finger_middle_2_R` | 1.518 | 1.009 | 0.665 |
| `finger_index_1_R` | 1.907 | 1.433 | 0.752 |
| `finger_thumb_1_R` | 1.856 | 1.264 | 0.681 |
| `finger_pinky_1_R` | 1.674 | 0.963 | 0.575 |

比值不是常数，所以不是"只差单位"。层级也不同：CS2 有 `finger_*_meta_*`、`armUpperShoulder_*`、
`armUpperStraighten_0_*`、`arm_upper_*`、`attachHand_*`、`wpn*`、`econ`，CS:MC 一个都没有；
CS:MC 的 `arm_lower_*` 把 CS2 分开的肩关节吞了进去，另有 CS:MC 独有的 `arm_lower_*_twist`、`root_43/44`。

把 39 根同名骨在每一帧上做一次相似变换拟合（Umeyama，单一缩放+旋转+平移）：

| 枪 | clip | 拟合缩放 | 平均残差 | 最大残差 |
|---|---|---:|---:|---:|
| ak47 | draw_ak | 0.02432 | 0.711 in | 6.193 in |
| ak47 | idle_ak | 0.02535 | 0.521 in | 1.647 in |
| ak47 | reload_ak | 0.02489 | 0.519 in | 2.580 in |
| m4a1s | draw_rifle | 0.02471 | 0.428 in | 2.534 in |
| m4a1s | reload_rifle | 0.02424 | 1.048 in | 8.370 in |
| awp | draw_awp | 0.02441 | 0.539 in | 2.127 in |
| awp | reload_awp | 0.02460 | 0.513 in | 2.489 in |

拟合缩放稳定在 0.0243–0.0256，即英寸→米的 0.0254——单位关系是对的；但残差半英寸量级，
远超任何解析误差（对照 §3.E 的 1.7 µm）。

**同时，两边确实是同一批动作、同一条时间轴。** 时间轴（CS:MC 一列取其 `Times` 末值，不取 `Duration` 字段，理由见表下）：

| 枪 | CS2 clip | fps | 帧数 | 时长 | CS:MC clip | 帧数 | 时长 |
|---|---|---:|---:|---:|---|---:|---:|
| ak47 | draw_ak | 30.0000 | 31 | 1.0000 | deploy | 31 | 1.0000 |
| ak47 | idle_ak | (静态) | 1 | 0.0000 | idle | 1 | 0.0000 |
| ak47 | shoot1_ak | 29.9987 | 24 | 0.7666 | shoot1 | 24 | 0.7667 |
| ak47 | reload_ak | 30.0004 | 74 | 2.4333 | reload | 74 | 2.4333 |
| ak47 | lookat01_ak | 29.9998 | 138 | 4.5667 | inspect | 138 | 4.5667 |
| m4a1s | draw_rifle | 30.0009 | 35 | 1.1332 | deploy | 34 | 1.1000 |
| m4a1s | shoot1_rifle | 30.0000 | 13 | 0.3999 | shootSilenced | 13 | 0.4000 |
| m4a1s | reload_rifle | 29.9997 | 93 | 3.0667 | reload | 93 | 3.0667 |
| m4a1s | lookat01_rifle | 30.0000 | 160 | 5.2999 | inspect | 151 | 5.0000 |
| m4a1s | silencer_attach_rifle | 30.0002 | 146 | 4.8333 | attach | 146 | 4.8333 |
| awp | draw_awp | 29.9992 | 39 | 1.2666 | deploy | 38 | 1.2333 |
| awp | shoot1_awp | 30.0000 | 49 | 1.5999 | shoot1 | 50 | 1.6333 |
| awp | reload_awp | 29.9997 | 111 | 3.6666 | reload | 111 | 3.6667 |
| awp | lookat01_awp | 30.0000 | 151 | 5.0000 | inspect | 151 | 5.0000 |

**AK 五个 clip 帧数全部一模一样**；其余最多差 1 帧，只有 m4a1s 的 inspect 差 9 帧——而 CS:MC 那条 clip 的
`Duration` 字段写着 5.3，正是 CS2 的 5.2999，采样数组却只到 5.0，说明是 CS:MC 转码时被截断，
CS2 这边才是原本。（顺带：CS:MC 的 `Duration` 字段与其 `Times` 末值本身就有不一致，比较帧数比比较 Duration 可靠。）

动作本身用**逐帧绝对旋转增量**比（增量对绑定姿态和全局坐标系都不敏感，正好隔离掉骨架差异）：

`draw_ak` vs `deploy`，39 根映射骨，单位度：

| CS2 骨 | CS:MC 骨 | 平均 \|Δ\| | 最大 \|Δ\| | 相关 |
|---|---|---:|---:|---:|
| `finger_thumb_2_L` | `finger_thumb_2_l` | 12.524 | 94.879 | 0.474 |
| `finger_middle_2_L` | `finger_middle_2_l` | 12.040 | 73.201 | 0.613 |
| `finger_index_2_L` | `finger_index_2_l` | 14.103 | 71.715 | 0.659 |
| `finger_pinky_2_L` | `finger_pinky_2_l` | 14.742 | 68.710 | 0.650 |
| `finger_ring_2_L` | `finger_ring_2_l` | 12.645 | 64.957 | 0.642 |
| `finger_pinky_1_L` | `finger_pinky_1_l` | 9.439 | 50.019 | 0.692 |
| `finger_middle_1_L` | `finger_middle_1_l` | 9.256 | 46.316 | 0.688 |
| `finger_index_1_L` | `finger_index_1_l` | 10.181 | 42.817 | 0.734 |
| `finger_thumb_1_L` | `finger_thumb_1_l` | 7.266 | 42.559 | 0.649 |
| `finger_ring_1_L` | `finger_ring_1_l` | 9.110 | 40.107 | 0.718 |
| `finger_middle_0_L` | `finger_middle_0_l` | 5.884 | 30.128 | 0.815 |
| `finger_pinky_0_L` | `finger_pinky_0_l` | 6.072 | 29.956 | 0.763 |
| `finger_index_0_L` | `finger_index_0_l` | 6.442 | 27.467 | 0.842 |
| `finger_ring_0_L` | `finger_ring_0_l` | 5.628 | 24.260 | 0.829 |
| `finger_thumb_0_L` | `finger_thumb_0_l` | 5.214 | 21.098 | 0.848 |
| `hand_L` | `hand_l` | 3.362 | 15.495 | 0.921 |
| `arm_lower_L` | `arm_lower_l` | 1.785 | 9.408 | 0.901 |
| `finger_thumb_2_R` | `finger_thumb_2_r` | 1.357 | 5.355 | 0.946 |
| `clip` | `v_weapon_ak47_clip` | 1.380 | 5.351 | 0.934 |
| `trigger` | `v_weapon_ak47_trigger` | 1.380 | 5.351 | 0.934 |
| `cliprelease` | `v_weapon_ak47_cliprelease` | 1.381 | 5.351 | 0.934 |
| `finger_thumb_0_R` | `finger_thumb_0_r` | 1.364 | 5.349 | 0.945 |
| `finger_thumb_1_R` | `finger_thumb_1_r` | 1.372 | 5.348 | 0.944 |
| `bolt` | `v_weapon_ak47_bolt` | 1.380 | 5.346 | 0.934 |
| `muzzle` | `muzzle` | 1.375 | 5.316 | 0.934 |
| `hand_R` | `hand_r` | 1.366 | 5.314 | 0.935 |
| `finger_pinky_0_R` | `finger_pinky_0_r` | 1.454 | 5.311 | 0.930 |
| `finger_index_2_R` | `finger_index_2_r` | 1.185 | 5.311 | 0.962 |
| `finger_middle_0_R` | `finger_middle_0_r` | 1.380 | 5.302 | 0.934 |
| `finger_ring_0_R` | `finger_ring_0_r` | 1.381 | 5.302 | 0.934 |
| `finger_index_1_R` | `finger_index_1_r` | 1.321 | 5.297 | 0.942 |
| `finger_middle_1_R` | `finger_middle_1_r` | 1.401 | 5.293 | 0.933 |
| `finger_pinky_1_R` | `finger_pinky_1_r` | 1.443 | 5.282 | 0.930 |
| `finger_middle_2_R` | `finger_middle_2_r` | 1.400 | 5.280 | 0.933 |
| `finger_ring_1_R` | `finger_ring_1_r` | 1.385 | 5.270 | 0.933 |
| `finger_pinky_2_R` | `finger_pinky_2_r` | 1.393 | 5.267 | 0.940 |
| `finger_index_0_R` | `finger_index_0_r` | 1.363 | 5.256 | 0.942 |
| `finger_ring_2_R` | `finger_ring_2_r` | 1.354 | 5.226 | 0.937 |
| `arm_lower_R` | `arm_lower_r` | 0.755 | 3.637 | 0.920 |

规律很清楚：**武器骨与右手（持枪手）几乎完全一致**（muzzle/bolt/clip/trigger 与右手手指平均 1.3–1.4°，
相关 0.93–0.96；`shoot1_ak` 全 39 根最大都在 3.32° 以内，最好的几根到 0.003°），
**发散的集中在左手（扶枪手）指尖 `*_2_*`**——那正是两副骨架差最大、且两边都靠 IK 解到枪身上的位置。
唯一的例外是 `awp shoot1`（右手指最大 163°、相关 0.05），它同时也是帧数对不上的那条，属于版本不同。

**结论：解析本身没有问题**（A/B/C/E 四项独立证明），**对不上的是计划书的前提**。

## 5. 未解决项 / 需要决定

1. **阶段 2 的门槛**：计划书写的"逐骨绝对矩阵对上才能进阶段 2"按字面无法满足，因为两副骨架物理上不同。
   建议改判据为已经通过的 A/B/C/E + D 的时间轴与增量一致性。**这一条需要用户拍板，没拍板前不进阶段 2。**
2. **AWP 的接骨点 `wpn` 是推定值**（`viewmodel.vnmskel` 未列 AWP），已用 §3.C 的距离旁证，但不是直接读到的。
3. **`cs2_sounds.json` 里 34/69 条 cue 没有音频**：`WeaponMove1/2/3`（布料/移动 foley）、`AddAmmo`、
   `Inspect_F245`。CS2 的 WAV 都在 `05_audio/decoded/` 里，需要转单声道 OGG 装进包，属于后续工作。
4. **CS2 声音时间尚未对着录屏核过**：与旧表最大差 580 ms（m4a1s ClipHit），所以 `GunSoundProfile` 默认仍是 0。
   核对方法见 §7。
5. **旧表里有 CS2 事件轨中没有的 cue**：旧 `m4a1s:attach` 有 `m4a1s_silencer_on`（0.97 s），
   CS2 的 `silencer_attach_rifle` 事件轨里没有对应事件。切到 CS2 表后这一声会消失，需要在核对时确认。
6. **`<gun>.cs2.animation.json` 暂未打进 DLL**：三个文件共 5.5 MB，现在没有代码读它们，
   `csproj` 里显式 `Remove` 掉了，等 cs2 profile 真正采样时再加回。
7. **调参文件会被重写**：`KnifeTuning` 的默认值指纹因为新增 `GunSoundProfile` 变了，
   玩家本地手改过的 `ScCsgoKnivesTuning.txt` 会被刷回默认值——这是该机制既定行为，但用户会看到。

## 6. 估计值清单

本阶段**没有引入任何估计的数值**。所有输出都直接来自导出件：

| 值 | 来源 | 是否估计 |
|---|---|---|
| 帧率、帧数、时长 | DMX 的 `DmeChannelsClip.frameRate` 与 `DmeTimeFrame.duration` | 否 |
| 骨骼静息平移/旋转 | DMX 的 `DmeTransform` | 否 |
| 动画曲线 | DMX 的 `DmeVector3LogLayer` / `DmeQuaternionLogLayer` | 否 |
| 声音事件帧 | `sound-event-timings.csv`（源自 `.vnmclip` 事件轨） | 否 |
| 事件→WAV | `05_audio/weapon-soundevent-mapping.json` | 否 |
| 武器骨接到 `wpn` | AK/M4 读自 `viewmodel.vnmskel`；**AWP 为推定**（见 §5.2） | AWP 是 |
| 静态 clip 的帧率取 30 | DMX 对静态 clip 写的 `frameRate` 是退化的 1.0；30 是该导出里所有动画 clip 的实测值 | 约定，非估计 |

## 7. 需要用户在 Windows 上做的事

本阶段只需要第 1 项，其余是后续阶段的，先列出来以便一次安排。

1. **（现在需要）核对 CS2 声音时间**：CS2 离线对局（`map de_dust2` + `bot_kick`，或创业者模式），
   1920×1080、60 fps、关 HUD（`cl_drawhud 0`、`cl_showfps 0`、`net_graph 0`），
   拿 AK-47 录一段"站定 → 打空一梭 → 换弹 → 检视"，M4A1-S 和 AWP 各同样一段。
   录屏要带**游戏内音频**（这一步音频比画面重要）。VPS 侧用 ffmpeg 抽音轨做峰值检测，
   对 `ak47:reload` 的 Clipout 0.3667 s / Clipin 1.0667 s / BoltPull 1.6000 s。
2. （阶段 3 用）读本机 viewmodel cvar：`viewmodel_fov`、`viewmodel_offset_x/y/z`，
   在控制台逐条敲出来截图，或直接把 `.../csgo/cfg/cs2_user_convars_0_slot0.vcfg` 发过来。
3. （阶段 3 用）同规格再录一遍 AK/M4/AWP 的"待机、拔枪、检视、换弹、开火、AWP 开镜"，用于叠合定标。
4. （随时）装 `output/ScCsgoKnives-0.15.11.scmod` 试玩，确认刀和三把枪与 0.15.10 表现一致；
   要听 CS2 时间的话把 `ScCsgoKnivesTuning.txt` 里的 `GunSoundProfile` 改成 1，存盘 1 秒生效。

## 8. 不破坏既有路线的证据

| 检查 | 0.15.10（104876e，独立 worktree 跑） | 0.15.11 |
|---|---|---|
| `tools/verify_cs.py` | m9 21.2/0.919/0.875、karambit 15.3/0.881/0.922、butterfly 35.8/0.915/0.874、tactical 23.4/0.862/0.832，22 把全 ok，PASS | 逐行相同 |
| `tools/videocheck.py` | 最差地标 24.0 px（idle 4.5 / mid 10.5 / hold 24.0 / late 17.0） | 逐行相同 |
| 包内 Assets | — | 213 个条目与仓库源逐字节相同，`git status` 无 Assets 改动 |
| 包内枪贴图高频能量 | — | ak47 2.58、awp 1.69、m4a1s 1.73（噪点版约 19） |
| 包体 | 29,439,414 B | 29,443,186 B（+3,772 B，即 `cs2_sounds.json` 与新代码） |

## 附：CS2 viewmodel + AK 武器骨架（64 骨，`draw_ak` 与 `idle_ak` 相同）

```
root_motion
  wpn
    wpnEnd
    wpnTip
    wpnHand_L
    wpnHand_R
    weapon
      weapon_offset
        bolt
        clip
        cliprelease
        econ
        muzzle
        trigger
  armUpperShoulder_L
    arm_lower_L
      hand_L
        finger_middle_meta_L
          finger_middle_0_L
            finger_middle_1_L
              finger_middle_2_L
        finger_pinky_meta_L
          finger_pinky_0_L
            finger_pinky_1_L
              finger_pinky_2_L
        finger_index_meta_L
          finger_index_0_L
            finger_index_1_L
              finger_index_2_L
        finger_thumb_0_L
          finger_thumb_1_L
            finger_thumb_2_L
        finger_ring_meta_L
          finger_ring_0_L
            finger_ring_1_L
              finger_ring_2_L
        attachHand_L
      armUpperStraighten_0_L
        arm_upper_L
  armUpperShoulder_R
    arm_lower_R
      hand_R
        finger_middle_meta_R
          finger_middle_0_R
            finger_middle_1_R
              finger_middle_2_R
        finger_pinky_meta_R
          finger_pinky_0_R
            finger_pinky_1_R
              finger_pinky_2_R
        finger_index_meta_R
          finger_index_0_R
            finger_index_1_R
              finger_index_2_R
        finger_thumb_0_R
          finger_thumb_1_R
            finger_thumb_2_R
        finger_ring_meta_R
          finger_ring_0_R
            finger_ring_1_R
              finger_ring_2_R
        attachHand_R
      armUpperStraighten_0_R
        arm_upper_R
```

`idle_ak` 是单帧静态姿态（DMX 里 `duration = 0`，`frameRate` 退化为 1.0），
CS:MC 的 `idle` 同样是单帧，两边一致。
