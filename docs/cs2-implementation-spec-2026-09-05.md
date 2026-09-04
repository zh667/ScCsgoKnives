# CS2 迁移：实施规范（0.16.4）

日期：2026-09-05
输入：`docs/cs2-reference-validation-handoff-2026-09-04.md`、
`docs/cs2-reference-validation-review-2026-09-05.md`、Windows 侧复核结论八条。
本文件先于代码改动写出，作为改动范围与验收规则的约定。

---

## 0. 对 Windows 复核结论的回应（先把事实钉死）

### 0.1 接受的更正

**`Drawable already added` 不是曳光重复增亮 —— 我的说法撤回。**
Windows 已反编译确认 `SubsystemDrawing.AddDrawable` 用 `Dictionary.TryAdd`，重复项记日志后返回，
`Draw` 只遍历一次。我原来的判断是基于“如果重复注册”的假设，没有验证实现，撤回。

补充一条本次查到的事实：**mod 里只有一处 `AddDrawable`**
（`SubsystemScGunBlockBehavior.cs:184`，在 `Load()` 里），没有手工重复注册可删。
日志里那行说明引擎侧已经先收录了这个 `IDrawable`（子系统实现了 `IDrawable`，
`SubsystemDrawing` 很可能自动收集），我们的显式调用只是撞上了已存在项。

**因此本次不动这段代码。** 盲删有风险：若引擎并不自动收集，删掉会让开镜遮罩和曳光整个不画。
要清日志的话，正确做法是 Windows 侧做一次实验——删掉显式注册后确认开镜遮罩仍然绘制——
而不是在 VPS 盲改。

### 0.2 修正的更正

**`Weapon_M4A1.AddAmmo` 不是“无源音频”，它有 MP3。**

`weapon-soundevent-mapping.json` 里它的 `decoded_files` 只有 `.vsnd` 和 `.mp3`，没有 `.wav`：

```
Weapon_M4A1.AddAmmo   resources = sounds/weapons/m4a1/m4a1_addammo_01.vsnd
                      decoded   = m4a1_addammo_01.mp3   (存在，18 KB 级)
```

我原来的 `tools/cs2_sound_timings.py` 只认 `.wav`，所以把它算成“无源”。
整个 m4a1 目录都是这样：`m4a1_clipin/clipout/draw/boltback/boltforward` 也都只有 `.mp3`。

**结论：34 条 cue 全部可以补齐，不需要任何书面豁免。** 见 §5。

### 0.3 采纳的事实

CS2 设置界面：全屏 / 标准 4:3 / 1400×1050 / 144 Hz。
（附注：`cs2_video.txt` 里 `refreshrate 40122000/243152` 算出来是 165.0 Hz，与界面的 144 Hz 不符，
该文件另有 `.bak`/`.pwa` 变体，判定为陈旧。**以设置界面为准**。
对结论没有影响：144→60 和 165→60 都不是整数比，逐帧对齐同样不可靠。）

---

## 1. 开关拆分

| 开关 | 管什么 | 默认 |
|---|---|---|
| `GunProfile` | CS2 网格、材质、动画、摆放、手臂、特效 | 0 |
| `GunNumbers` | CS2 伤害、距离衰减、散布、后坐 | **0** |
| `GunSoundProfile` | 独立 A/B：只换声音时间表 | 0 |

规则：

- 视觉翻默认**不得**连带改变玩法 → `Cs2Weapons.Active` 的判据从 `GunProfile` 改为 `GunNumbers`。
- `GunProfile = 1` 时仍自动采用 CS2 事件表：声音条件保持
  `GunProfile >= 0.5 || GunSoundProfile >= 0.5`。
- 三个开关互不隐含，可任意组合。

**改动文件**：`Rendering/KnifeTuning.cs`、`Animation/Cs2Weapons.cs`。

---

## 2. CS2 动画时长（默认翻转前必须修）

### 2.1 已复核确认的缺陷

```csharp
// CsmcKnifeRig.Sample
else time = MathUtils.Clamp(time, 0f, Math.Max(0f, clip.Duration));   // CS:MC 时长
...
return new KnifeRigPose(..., time);                                    // 存的是截断后的值

// CsmcFirstPersonRenderer.cs:903
Cs2Rig.Pose cs2 = Cs2Rig.Sample(gun, pose.ClipAlias, pose.Time);       // 用截断值采 CS2 rig
```

**机制判断正确，举的例子不成立 —— 实测更正如下。**

`GetDuration` 返回的是 CS:MC json 的 **`Duration` 字段**，不是它 `Times` 数组的末值。
M4A1-S inspect 的 `Duration` 字段是 **5.3**（`Times` 只到 5.0、151 个采样，是曲线数据被截断，
不是 `Duration` 被截断）。所以控制器本来就跑到 5.3，`Cs2Rig.Sample` 再把 5.3 夹到 5.2999。
**M4 inspect 并没有冻结。**

真正受影响的是 CS:MC 与 CS2 时长不一致的四条 clip（`ArmPreview durations` 实测）：

| 枪 | clip | CS:MC | CS2 | 差 |
|---|---|---:|---:|---:|
| ak47 | reload | 2.4667 | 2.4333 | −0.0334 s（−1 帧）动画先结束，尾部空转 1 帧 |
| m4a1s | deploy | 1.1000 | 1.1332 | **+0.0332 s（+1 帧）CS2 拔枪的最后一帧被切掉** |
| m4a1s | shootSilenced | 0.4000 | 0.3999 | −0.0001 s |
| awp | shoot1 | 1.6667 | 1.5999 | −0.0668 s（−2 帧）动画先结束，尾部空转 2 帧 |

即：被切掉的是 m4a1s deploy 的 1 帧；另外两条是状态机比动画多跑 1–2 帧。
量级是 1–2 帧，不是 9 帧。**修法不变、仍然必要**（两边必须用同一个时长），只是收益要按实测说。

### 2.2 修法

**单一时长仲裁者**，控制器、`BusyUntil`、实际采样三处共用：

```csharp
// CsmcKnifeRig
public static float GetProfileDuration(int variant, string clipAlias) =>
    Cs2Placement.Active(variant) && Cs2Rig.Duration(GetAssetName(variant), clipAlias) > 0f
        ? Cs2Rig.Duration(GetAssetName(variant), clipAlias)
        : GetDuration(variant, clipAlias);
```

`Cs2Placement.Active` 已经包含 `IsGun(variant)` 与 `GunProfile >= 0.5`，
所以**刀与 `GunProfile = 0` 走的仍是 `GetDuration`，行为逐位不变**。

**保留未截断的控制器时间**：`KnifeRigPose` 增加两个只读字段

- `RequestedTime`：调用方传进来的原始时间（未截断、未取模）
- `Looping`：调用方是否要求循环

`DrawCs2` 用它们按 **CS2 clip 自身长度**重新算采样时刻：

```csharp
float d = Cs2Rig.Duration(gun, pose.ClipAlias);
float t = pose.Looping && d > 0f
    ? pose.RequestedTime - d * MathF.Floor(pose.RequestedTime / d)   // 按 CS2 长度循环
    : MathUtils.Clamp(pose.RequestedTime, 0f, MathF.Max(d, 0f));     // 按 CS2 长度截断
Cs2Rig.Pose cs2 = Cs2Rig.Sample(gun, pose.ClipAlias, t);
```

### 2.3 要替换的 `GetDuration` 调用点（共 6 处）

| 文件 | 行 | 用途 |
|---|---|---|
| `KnifeAnimationController.cs` | 94 | clip 结束转 idle 的判据 |
| `KnifeAnimationController.cs` | 148 | `IsAttaching` |
| `KnifeAnimationController.cs` | 155 | `IsBusy` |
| `SubsystemScGunBlockBehavior.cs` | 224 | deploy 的 `BusyUntil` |
| `SubsystemScGunBlockBehavior.cs` | 347 | reload 的 `BusyUntil` |
| `SubsystemScGunBlockBehavior.cs` | 379 | 消音器装拆的 `BusyUntil` |

**不替换**：`CsmcFirstPersonRenderer.MeasureHolds`（237–240 行）——
它在 `BuildPlacement` 的 `if (IsGun(variant)) return;` 之后，是刀专用的离线测量。

### 2.4 已知且可接受的副作用

`GunProfile = 1` 时控制器按 CS2 时长跑，而 `state.Pose` 仍是 `CsmcKnifeRig.Sample(...)`，
CS:MC 那份姿态在自己的时长处冻结。这没有影响：cs2 路径只从它取 `ClipAlias`/`RequestedTime`/`Looping`，
姿态本身不画（手臂走 `Cs2SkinnedMesh`，武器走 `Cs2Rig`）。仅在 `Cs2Rig.Sample` 返回 null 的回退路径上会用到。

`idle_ak` / `idle_rifle` / `idle_awp` 的 CS2 时长是 0（单帧静态），
所以 cs2 profile 下待机是**完全静止**的。这是 CS2 的真实行为——待机摇摆来自程序化的
viewmodel lag/bob，在 SC 侧由 `post`（机体运动）承担。

**改动文件**：`Animation/CsmcKnifeRig.cs`、`Animation/KnifeAnimationController.cs`、
`World/SubsystemScGunBlockBehavior.cs`、`Rendering/CsmcFirstPersonRenderer.cs`。

---

## 3. 截图探针与比较空间

### 3.1 不再假定 1920×1080

Windows 先按现有设置（4:3 / 1400×1050 / 全屏）用 `Win+Alt+PrintScreen` 拍**一张** idle PNG，
不改任何设置。VPS 侧用新工具判定：

```
python3 tools/cs2_capture_probe.py <probe.png> --render-width 1400 --render-height 1050
```

工具输出（全部从图像本身可判定）：

- PNG 实际像素尺寸与位深；
- 左右是否有纯黑竖边（pillarbox）及其宽度；
- 去掉黑边后的**内容矩形**及其宽高比；
- 内容宽高比与声明的渲染宽高比（1400:1050 = 4:3）是否一致
  → 一致 = 未拉伸；不一致 = **横向拉伸**，给出拉伸系数；
- 建议的比较空间。

### 3.2 比较空间的定义（写进 manifest，不再默认）

**比较空间 = 截图的内容矩形**，即：

1. 去掉 pillarbox 黑边（若有），得到内容矩形 `W×H`；
2. 若内容宽高比 == 渲染宽高比 → **不重采样**，比较空间就是 `W×H`，
   我们的预测按 `aspect = W/H` 直接渲到 `W×H`；
3. 若内容宽高比 != 渲染宽高比 → 判定为拉伸，把内容**横向反拉伸**回渲染宽高比，
   记录这一步是**唯一**允许的重采样，并在报告里写明插值方式（Lanczos）。

CS2 图与模组图必须落在同一个 `W×H`、同一宽高比、同一色彩空间（sRGB 8-bit），
这三项写进 `capture.json` 并由 checker 校验；缺任一项 = FAIL。

**新增文件**：`tools/cs2_capture_probe.py`。

---

## 4. manifest 驱动的验收工具

**新增** `tools/cs2_reference_check.py`，**废弃** `tools/cs2_videocheck.py`。

### 4.1 输入格式 `reference/cs2/<date>/capture.json`

```jsonc
{
  "format": "ScCsgoKnives.Cs2Reference/1",
  "captured_at": "2026-09-05T12:00:00+08:00",
  "cs2": {
    "build": "1.40.x.x",                  // 控制台 `version` 的输出
    "map": "de_dust2",
    "setpos_exact": [x, y, z],
    "setang_exact": [pitch, yaw, roll],
    "team": "T",                          // §6：必须记录
    "agent": "<agent 名>",                // §6
    "gloves": "none|<glove 名>",          // §6
    "weapon_finish": "default",           // 必须是出厂皮，无贴纸/StatTrak
    "cvars": {                            // console echo 的读回值，不是配置文件里的
      "viewmodel_fov": 68, "viewmodel_offset_x": 2.5,
      "viewmodel_offset_y": 0, "viewmodel_offset_z": -1.5,
      "cl_prefer_lefthanded": 0, "cl_silencer_mode": 0,
      "zoom_sensitivity_ratio": 1.0, "weapon_recoil_scale": 2.0
    },
    "video": {
      "render_width": 1400, "render_height": 1050,
      "aspect_mode": "normal_4_3", "fullscreen": true, "refresh_hz": 144,
      "shadow_quality": 0, "ao_detail": 0, "particle_detail": 0,
      "shader_quality": 0, "msaa": 4, "hdr": -1, "texture_detail": 2
    }
  },
  "capture_tool": "xbox_game_bar",
  "comparison_space": { "width": 1400, "height": 1050, "unstretched": false },
  "shots": [
    { "gun": "ak47", "state": "idle", "image": "ak47_idle.png",
      "weapon_mask": "masks/ak47_idle_weapon.png",
      "hand_mask": "masks/ak47_idle_lefthand.png",
      "landmarks": "landmarks/ak47_idle.json", "sha256": "..." }
  ],
  "videos": [ { "gun": "ak47", "file": "ak47.mp4", "sha256": "...", "purpose": "qa_only" } ]
}
```

`landmarks/<shot>.json`：

```jsonc
{ "weapon": { "muzzle": [x, y], "trigger": [x, y], "clip": [x, y], "bolt": [x, y] },
  "left_hand": { "index_knuckle_over_top": [x, y],
                 "thumb_edge_on_receiver": [x, y],
                 "cuff_crosses_frame_edge": [x, y] } }
```

mask：与图同尺寸的 8-bit PNG，非零 = 目标区域。**没有 mask 就 FAIL，不允许猜背景。**

### 4.2 判据

| 项 | 规则 | 门槛 |
|---|---|---|
| **INPUT** | 尺寸/宽高比/色彩空间/哈希/cvar 与 manifest 一致；mask 与图同尺寸 | 任一不符 = FAIL |
| **PLACE** | 预测坐标由 `ArmPreview cs2` 出；**offset 固定为 manifest 读回值，禁止拟合**；`--fit-fov` 只打印诊断，不参与判定 | 每个武器地标欧氏距离 < 10 px |
| **PHOTO** | “**固定场景下三枪相对光度一致性**”：在**一把枪**上拟合唯一全局倍率 k，把同一个 k 用到另外两把 | 另两把掩码内均值误差 < 5%；三把 p10/median 与 p90/median 误差 < 10% |
| **HAND** | 只在**握持/接触 ROI** 内比左手：IoU、质心偏移、轮廓 Hausdorff 距离；三个边地标 | IoU ≥ 0.85、质心 < 10 px、地标 < 10 px |
| **HAND-R** | 右手只报告（本机 offset 下屏内仅 13.3%，327 顶点） | 不参与 PASS |
| **SOUND** | 事件帧记 source-verified；另检“已打包 cue 覆盖率” | 见 §5，覆盖率 100% 才算 PASS |

“握持/接触 ROI”定义：左手 mask 与武器 mask 各自膨胀 12 px 后的交集，再并上左手 mask
与武器 mask 相邻 24 px 内的部分。ROI 随 manifest 一起存盘，报告里附出来。

### 4.3 输出

`cs2-reference-report.json` + `cs2-reference-report.md`，每项独立 `PASS` / `FAIL` / `SKIPPED(missing input)`。
**没有总 PASS 字段**——只有逐项状态和一句“全部为 PASS 时可以翻默认”。缺输入一律 FAIL，不得静默跳过。

**新增文件**：`tools/cs2_reference_check.py`。**删除**：`tools/cs2_videocheck.py`。

---

## 5. 34 条 cue：补齐，不豁免

只需要 **5 个源文件**就覆盖全部 34 条：

| 源文件 | 覆盖 cue | 条数 |
|---|---|---|
| `movement1.wav` | `AWP/M4A1.WeaponMove1`、`M4A1.SilencerWeaponMove1` | 4 |
| `movement2.wav` | `AK47/AWP/M4A1.WeaponMove2` | 8 |
| `movement3.wav` | `AK47.WeaponMove1`、`AK47/AWP/M4A1.WeaponMove3`、`M4A1.SilencerWeaponMove3` | 17 |
| `ak47_addammo_02.wav` | `AK47.AddAmmo` | 1 |
| `ak47_inspect_f245.wav` | `AK47.Inspect_F245` | 3 |
| `m4a1_addammo_01.mp3` | `M4A1.AddAmmo` | 1 |
| | **合计** | **34** |

（`AK47.WeaponMove1` 指向 `movement3` 不是笔误，是映射表原文。）

装成 6 个 OGG：`cs2_move1/2/3.ogg`、`ak47_addammo.ogg`、`ak47_inspect_f245.ogg`、`m4a1s_addammo.ogg`。
**一律单声道**（引擎的 ogg 字节数只对单声道正确）。

VPS 上没有 ffmpeg/oggenc；本次用 `soundfile 0.14.0 / libsndfile 1.2.2`
（支持 OGG-VORBIS 写、MP3 读）编码，装在用户级 site-packages，不进 mod。
装完用 `tools/SoundCheck` 过一遍引擎解码器。

`tools/cs2_sound_timings.py` 同时修一个缺陷：`decoded_files` 现在只认 `.wav`，改为 `.wav` 与 `.mp3` 都认。

**结论：不存在需要豁免的 cue，声音覆盖率目标是 100%。** 在覆盖率达到 100% 之前，
不得声称声音 PASS——这与人工 QA 的“无漏声”不再矛盾。

**改动文件**：`tools/cs2_sound_timings.py`、新增 `tools/install_cs2_sounds.py`、
`Assets/Audio/ScCsgoKnives/*.ogg`（6 个新文件）、`AnimationData/cs2_sounds.json`（重新生成）。

---

## 6. 手臂对应的 team / agent / loadout

### 6.1 导出件能证明的

- 12 套 `glove_*` 全部把 **`bare_arm_133`** 当手臂材质；整个导出件里**只有 `bare_arm_133` 一种**手臂贴图。
- `glove_fingerless` 本身就是 12 套之一，而 `weapon_arms.vmdl`（= 每把枪引用的手臂模型）
  把 `bare_arm_133` + `glove_fingerless` 烘成两个 primitive。
- 路径都在 `.../shared/arms/`。

→ 手臂网格与材质在导出件里**不随 agent 变**，`glove_fingerless` 是“没有手套皮肤”的默认。

### 6.2 导出件不能证明的

某个具体 agent 是否会覆盖手臂材质/网格。导出是按武器抓的，没有 agent 侧的证据。
**“默认探员、无手套皮肤”确实不足以证明匹配**，这一条 Windows 的判断正确。

### 6.3 用可测量的判据替代猜测

`bare_arm_133` 底色的统计（VPS 实测，1024×2048，仅不透明纹素）：

```
mean RGB   (178.1, 135.3, 108.1)
median RGB (181,   138,   110)
p25/p50/p75  R 173/181/186   G 125/138/147   B 99/110/118
R/G = 1.316   G/B = 1.252
```

**Windows 步骤**：同一位置、同一 cvar，用 2–3 个候选 loadout（例如 T 默认 agent、CT 默认 agent，
各自无手套皮肤）各拍一张 idle PNG。VPS 用
`tools/cs2_capture_probe.py --arm-tone` 在小臂裸露区取样，与上表比色比（R/G、G/B 对光照不敏感）。

- 若各 loadout 取样一致 → 手臂确实与 agent 无关，任选其一并记录。
- 若不一致 → 取与上表色比最接近者，记录 team/agent/gloves 三项进 manifest。

**在这一条确定之前，`cs2_reference_check.py` 的 HAND 项直接 FAIL（`agent_unverified`），
不允许出 PASS。**

---

## 7. 顺手修掉的一处虚假 PASS 路径

`tools/cs2_render_check.py` 用“出现次数最多的单一颜色”猜背景。改为：
`tools/pbr_emulate.py` 增加 `mask=<path>` 输出逐像素覆盖掩码，`cs2_render_check.py` 读它。
这样离线渲染侧不再有任何背景启发式。

**改动文件**：`tools/pbr_emulate.py`、`tools/cs2_render_check.py`。

---

## 8. 预计改动文件清单

### 运行时（C#）

| 文件 | 改动 |
|---|---|
| `Rendering/KnifeTuning.cs` | 新增 `GunNumbers`（默认 0）+ 序列化/解析/注释 |
| `Animation/Cs2Weapons.cs` | `Active` 判据 `GunProfile` → `GunNumbers` |
| `Animation/CsmcKnifeRig.cs` | 新增 `GetProfileDuration`；`KnifeRigPose` 增 `RequestedTime`、`Looping` |
| `Animation/KnifeAnimationController.cs` | 3 处 `GetDuration` → `GetProfileDuration` |
| `World/SubsystemScGunBlockBehavior.cs` | 3 处 `GetDuration` → `GetProfileDuration` |
| `Rendering/CsmcFirstPersonRenderer.cs` | `DrawCs2` 按 CS2 时长重算采样时刻 |

### 工具（Python）

| 文件 | 改动 |
|---|---|
| `tools/cs2_capture_probe.py` | 新增：截图规格/拉伸判定 + `--arm-tone` |
| `tools/cs2_reference_check.py` | 新增：manifest 驱动的验收入口 |
| `tools/cs2_videocheck.py` | **删除** |
| `tools/install_cs2_sounds.py` | 新增：5 源 → 6 个单声道 OGG |
| `tools/cs2_sound_timings.py` | `decoded_files` 兼容 `.mp3` |
| `tools/pbr_emulate.py` | 新增 `mask=` 输出 |
| `tools/cs2_render_check.py` | 用真掩码替换众数背景 |

### 资源

`Assets/Audio/ScCsgoKnives/` 新增 6 个 OGG；`AnimationData/cs2_sounds.json` 重新生成。

### 不动的

- `SubsystemDrawing` 注册（§0.1）
- 22 把刀的任何代码路径
- `GunProfile = 0` 的行为
- DMX 解析、CPU 蒙皮（已通过，不重测）

---

## 9. 本次交付的验收

1. 六个 CS2 自检脚本全部 PASS。
2. `verify_cs.py`、`videocheck.py`（刀的那个，不是被删的 `cs2_videocheck.py`）与 0.15.10 逐行相同。
3. 新增：`cs2_placement_selftest.py` 断言时长仲裁——`GunProfile=0` 与刀具逐位不变，
   `GunProfile=1` 的枪取 CS2 时长，并列出被重新计时的 clip。
4. `cs2_sounds.json` 已打包 cue 覆盖率 = 100%（69/69）。
5. 6 个新 OGG 过 `tools/SoundCheck`。
6. 打包为 **0.16.4**，不覆盖 0.16.3，报告 SHA-256。
