# CS2 第一人称参考采集与验收：VPS 审查回复

日期：2026-09-05
对应文件：`docs/cs2-reference-validation-handoff-2026-09-04.md`
审查范围：只做审查与决策，没有改动任何运行时代码。

> **2026-09-05 更正（见 `docs/cs2-implementation-spec-2026-09-05.md` §0）**
> 本文有两处后来被证伪，保留原文以便对照，结论以规范文件为准：
> 1. **§A4 / B 里“`Drawable already added` 会让曳光重复增亮”是错的。** Windows 已反编译确认
>    `SubsystemDrawing.AddDrawable` 用 `Dictionary.TryAdd`，重复项记日志后返回，`Draw` 只遍历一次。
>    我当时是基于“如果重复注册”的假设推的，没有验证实现。撤回。
> 2. **D3 第 15 项“34 条无 OGG 的 cue”里，`Weapon_M4A1.AddAmmo` 并非无源。**
>    整个 m4a1 目录只解码出 `.mp3` 没有 `.wav`，而我的工具只认 `.wav`。它有
>    `m4a1_addammo_01.mp3`。34 条已在 0.16.4 全部补齐，覆盖率 69/69，不需要任何豁免。
复核方式：读项目内脚本与资产、读 Windows 侧 CS2 配置、在 VPS 上按现有工具实算。
未重复：310 个 DMX 解析、CPU 蒙皮性能测试。

---

## A. 事实复核

### A0. 属实、不需要改的

- 第一节全部属实（310 DMX、0.2 mm IK、微米级回读、骨长比 0.93–0.58、AK 时间轴一致）。
- 第一节第 2 条的“已知例外”引用准确：`awp shoot1` CS2 49 帧 / CS:MC 50 帧，右手指增量最大 163°、
  相关 0.05，见 `docs/cs2-stage1-report.md` 第 128、185 行。
- 第三节 CPU 蒙皮结论不复测，接受。
- 第八节对两个脚本的批评**全部属实**，逐行确认：`tools/cs2_videocheck.py` 的 `LANDMARKS` 为空、
  只比人工地标、不验规格、依赖 ffmpeg；`tools/cs2_render_check.py` 第 88–93 行确实用
  “出现次数最多的单一颜色”当背景。
- ffmpeg / ffprobe **在 VPS 上也没有安装**，不只是 Windows 缺。

### A1. 声音策略的描述不完整

实际代码 `src/ScCsgoKnives/World/SubsystemScGunBlockBehavior.cs:66`：

```csharp
bool cs2Sounds = KnifeTuning.GunProfile >= 0.5f || KnifeTuning.GunSoundProfile >= 0.5f;
if (!cs2Sounds || !Cs2Sounds.TryGet(key, out var list))
    if (!s_clipSounds.TryGetValue(key, out list)) return;
```

`GunSoundProfile = 1` 会在 `GunProfile = 0` 时**独立**启用 CS2 声音表。这是有意保留的 A/B 开关。
所以“`GunProfile = 0` 时保留旧声音表”只在 `GunSoundProfile = 0` 时成立，文档要补这一句。

### A2. 第五节把“摆放链”整体划进“不需要画面”过宽

链的**实现**已验证（出厂 C# 与离线参照最大 0.0504 px）。链的**输入**里有一个假设值没有验证：

```
fovY = 2 · atan( tan(fovX/2) / (4/3) )      viewmodel_fov 68 -> 53.668°
```

需要画面的是这一个数，不是整条链。其余输入（offset、坐标轴、rig）都是读来的或已证明的。

### A3. 第七节的采集参数与本机实际配置冲突 —— 最严重的一条

已读 `D:\steam\userdata\1415980225\730\local\cfg\cs2_video.txt`：

```
setting.defaultres              1400
setting.defaultresheight        1050
setting.fullscreen              1
setting.aspectratiomode         0
setting.refreshrate_numerator   40122000
setting.refreshrate_denominator 243152        -> 165.0 Hz
setting.videocfg_ao_detail      0
setting.videocfg_shadow_quality 0
setting.videocfg_particle_detail 0
setting.shaderquality           0
setting.msaa_samples            4
setting.videocfg_hdr_detail     -1
setting.videocfg_texture_detail 2
```

即：**在 165 Hz 的显示器上跑 1400×1050 拉伸 4:3**。Xbox Game Bar 抓的是桌面合成输出，
也就是**被水平拉伸之后的画面**。在这种画面上做 <10 px 的地标比对，几何是错的。

另外：

- `videocfg_particle_detail 0` 会简化枪口火光，阶段 5 的人工 QA 会看错；
- `videocfg_ao_detail 0`、`shaderquality 0` 直接影响阶段 2 的亮度；
- `msaa_samples 4` 对地标定位是好事（边缘平滑，亚像素可读）。

### A4. `Drawable already added` 的定性要改

它在旧版本出现过没错，但**后果在 0.16.2 变了**：`SubsystemScGunBlockBehavior.Draw()` 现在除了
开镜遮罩还画曳光，而曳光是 additive 混合。重复注册会让曳光亮度翻倍。
这不是性能问题，是一个便宜就能修的正确性问题。

---

## B. 会产生虚假 PASS 的四处（重点）

### B1. `--fit-fov` 与 offset 同时可调 = 保证 PASS

VPS 实测：把 `viewmodel_fov` 设错，再让三个 `viewmodel_offset` 自由拟合，
AK idle 8 个地标（muzzle / wpnTip / trigger / clip / hand_L / finger_index_1_L / bolt /
finger_index_1_R）的最差残差：

| 假设的 fov | 拟合出的 offset (x, y, z) | 最差残差 |
|---:|---|---:|
| 62 | (1.650, 0.004, −0.769) | 14.74 px |
| 64 | (1.927, 0.002, −1.007) | 9.56 px |
| **66** | (2.210, 0.001, −1.251) | **4.65 px** |
| **67** | (2.354, 0.001, −1.375) | **2.29 px** |
| **69** | (2.647, −0.001, −1.627) | **2.24 px** |
| **70** | (2.797, −0.001, −1.755) | **4.42 px** |
| 72 | (3.102, −0.001, −2.017) | 8.63 px |
| 76 | (3.734, −0.001, −2.561) | 16.46 px |

**在 10 px 门槛下，fov 66 / 67 / 68 / 69 / 70 全部通过。**
只取枪身地标（深度 0.36–0.52 m）更糟：fov 66 残差 3.50 px、fov 70 残差 4.34 px。

结论：

- **offset 必须钉死在读到的 cvar 值上，一个都不许拟合。**
- 钉死之后，10 px 门槛能把 `viewmodel_fov` 判到约 **±1.5°**
  （2° 偏差让地标移动 9.8–28 px），这才是真的在验证 Hor+ 那条假设。
- `--fit-fov` 只能作为诊断输出，**不能进 PASS / FAIL**。
- 地标必须**跨深度**才能把 fov 和 offset 分开
  （muzzle 0.951 m vs trigger 0.357 m，2.7 倍），只取枪身会退化。

参考：各地标深度与像素尺度（1920×1080，fovY 53.668°）

| 地标 | 深度 | 像素尺度 | 10 px 相当于 |
|---|---:|---:|---:|
| muzzle | 0.951 m | 1123 px/m | 8.90 mm |
| wpnTip | 0.964 m | 1107 px/m | 9.03 mm |
| bolt | 0.520 m | 2051 px/m | 4.88 mm |
| clip | 0.474 m | 2252 px/m | 4.44 mm |
| hand_L | 0.560 m | 1906 px/m | 5.25 mm |
| trigger | 0.357 m | 2993 px/m | 3.34 mm |
| finger_index_1_R | 0.358 m | 2985 px/m | 3.35 mm |

### B2. 阶段 2 的“均值和中位数都 <5%”逻辑上站不住

计划书原文是“**拟合环境光倍率**”再比；第九节 B 通篇没提拟合。两种读法都不成立：

- **拟合了**：均值按构造就是 0% 误差，“均值 <5%”不是检验。
- **没拟合**：那是在比 CS2 的 dust2 光照和我们 SC 的环境贴图，两个不同渲染器，
  失败的原因与网格材质无关。

而且形状统计本身鉴别力有限。我们自己渲的三把枪（idle，960×540，`docs/cs2-stage2-render.json`）：

| 枪 | mean | median | p10 | p90 | med/mean | p10/med | p90/med |
|---|---:|---:|---:|---:|---:|---:|---:|
| ak47 | 0.4402 | 0.4280 | 0.3098 | 0.5879 | 0.9723 | 0.7238 | 1.3736 |
| awp | 0.4109 | 0.4182 | 0.2364 | 0.5748 | 1.0178 | 0.5653 | 1.3745 |
| m4a1s | 0.4692 | 0.4533 | 0.3064 | 0.6376 | 0.9661 | 0.6759 | 1.4066 |

`p90/med` 三把枪只差 2%，当判据太钝。

**建议改成**：一个环境光倍率 k 在**一把枪**上拟合，然后用**同一个 k** 验另外两把，
要求它们均值 <5%。这才真正检验“三把枪的贴图和粗糙度相对关系装对了”——
那正是阶段 2 决定的东西。形状比（p10/med、p90/med）作次要项，门槛放到 10%。

### B3. 右手基本不在画面里，右手指标会是空转

按本机 cvar（`viewmodel_offset_x = 2.5`）实算 idle 帧上手臂网格的可见性：

| | 加权顶点 | 屏内 | 屏内比例 | 屏内包围盒 |
|---|---:|---:|---:|---|
| 左手 | 2463 | 2352 | **95.5%** | x 1162–1398, y 716–961 |
| 右手 | 2466 | 327 | **13.3%** | x 1471–1745, y **980–1080** |

`offset_x = 2.5` 把右手推到右下角，只剩贴着下边缘一条约 275×100 px 的碎片；
手腕骨 `hand_R` 投在 (2037, 1621)，完全出画。

**右手不能当 gate，只能报告。阶段 4 的 10 px 必须由左手承担。**

### B4. 输入时间戳对齐不可复现

PowerShell SendKeys → CS2 响应 → Game Bar 编码，三段延迟都不可观测，
Game Bar 的起录延迟也拿不到。再叠加 **165 Hz 渲染被 60 fps 采样**，帧对应至少抖 ±1 帧。

**但这个问题不用解**：阶段 2 / 3 / 4 的三项数字全在 **idle** 上，
而 `idle_ak` / `idle_rifle` / `idle_awp` 的 DMX `duration = 0`，**是单帧静态姿态**。

用**无损 PNG 静帧**做验收：没有对齐问题、没有压缩、不要求 fps、不受 165→60 Hz 重采样影响。
视频只留给人工 QA，那里 ±1 帧无所谓。

---

## C. 十二个决策问题的回答

### 1. 声音表绑 profile 是否正确

**保持，但把 `GunSoundProfile` 写进文档。**

绑定不只是保守，是**正确**的：CS2 的事件帧属于 CS2 的 clip，而 csmc profile 播的是 CS:MC 的 clip，
时长不同（m4a1s inspect 差 9 帧、awp shoot1 差 1 帧）。把 CS2 时间套在 CS:MC clip 上会失同步。
缺行回退旧表也保留。

### 2. 是否删除“声音峰值”硬指标

**同意删除。** 事件帧是源数据，录像音频峰值是它下游更脏的观测
（文件起音、混音混响、设备缓冲、量化），不可能比源更权威。改记 source-verified。

**但要补一条现在漏掉的、不需要录像的 FAIL 项**：69 条 CS2 cue 里
**34 条没有对应 OGG，加载时被静默丢弃**（`WeaponMove1/2/3`、`AddAmmo`、`Inspect_F245`）。
这是可听得出来的内容缺口，应当列为独立的 FAIL 项而不是脚注。

### 3. 阶段 2 是否必须要显式 mask

**必须，而且一张 mask 同时服务两项。** 禁止猜背景。
同一张枪身 mask 既用于阶段 2 的亮度，也用于阶段 3 的**剪影 IoU**——后者比内部点地标稳得多。
但 mask 只是必要条件，判据本身要按 B2 改。

### 4. 录屏还缺哪些必须固定的变量

**缺，而且有三个是阻塞项。**

阻塞：

1. **分辨率与宽高比**（见 A3）。必须以显示器原生 16:9 采集，
   或明确记录拉伸并在比对时反变换。
2. **武器皮肤 / 贴纸 / StatTrak**。我们的贴图是 `_default_` 出厂皮，
   库存里挂了任何皮肤，阶段 2 直接作废。
3. **手套皮肤 + 探员**。我们的手臂是 `weapon_arms.glb` = `bare_arm_133` + `glove_fingerless`。
   挂了手套皮肤就是另外 11 套模型之一，网格和贴图都不同，阶段 4 作废。

需记录但不阻塞：画质档（AO 0 / 阴影 0 / 着色器 0 / 粒子 0 / MSAA 4 / HDR −1）、
`fov_cs_debug`、`weapon_recoil_scale`。

已验证 OK，不用再查（读自 `cs2_user_convars_0_slot0.vcfg`）：

- `cl_prefer_lefthanded = false`
- `cl_silencer_mode = 0`
- `zoom_sensitivity_ratio = 1.0`
- `viewmodel_fov 68`、`offset_x 2.5`、`offset_y 0`、`offset_z −1.5`

`viewmodel_presetpos`：配置文件只存非默认值，所以它在默认档。
采集 cfg 里显式写四个 offset（第七节已经这么做），另外在 console `echo` 一次读回值存进 capture.json。

### 5. 三枪“待机→打空→换弹→检视”是否够

**不够，补两条 clip 加一个状态。**

- **deploy（拔枪）**：动作幅度最大，而且正是 CS:MC 帧数对不上的那条。切枪切回来即可触发。
- **AWP 开镜静帧**：阶段 5 的遮罩几何（0.475 h / 0.05 h）目前只来自贴图，从没在画面上验过。
- **M4 必须消音和不消音各一张静帧**：枪口位置（`muzzle0` / `muzzle1`）、火光包络、
  散布分支三者都不同。

空弹匣 idle（枪机后挂）可选，不必要。

### 6. 手指 <10 px 应比哪些地标

**指尖是最差的地标，别用。** 它最小、对比度最低、最容易被遮挡，
而且 trigger 深度处 10 px 只有 **3.34 mm**，比指甲还细。

- **主判据：左手区域剪影 IoU**（不需要点对应，对 AA 和我们自己光栅器的差异都稳）。
  门槛 IoU ≥ 0.85 且质心偏移 < 10 px。
- **次判据：三个“边”地标，不是“点”**：
  1. 食指指节越过武器上缘轮廓的交点；
  2. 拇指压在机匣侧面的边缘；
  3. 手套袖口与画面边缘的交线（长直边，最稳）。
- 左右分开报，**只用左手 gate**（理由见 B3）。

IoU 比点地标更合理，但两个都要：IoU 稳而不可解释，点地标可解释而不稳。

### 7. 动作时间与视频帧如何对齐

**不要去解，绕开它。** 三项数字全用无损 PNG 静帧（idle 是单帧静态姿态）。
视频只做人工 QA。

万一将来需要动画中段的帧，用**内容对齐**：渲出我们预测的整段序列，
按剪影 IoU 取最佳匹配帧；**绝不用输入时间戳**。

### 8. Game Bar 是否够

**够，不装 OBS；ffmpeg 装在 VPS 而不是 Windows。**

按第 7 问，数字验收只要 `Win+Alt+PrintScreen` 的 PNG；QA 要 MP4，Game Bar 也够。
OBS 的收益只有可控编码，而我们不靠视频拿数字，不值得。
ffmpeg / ffprobe **VPS 上也没有**，装在 VPS 就够——视频送过来在这边抽帧检查。

**唯一要先测一次的**：Game Bar 的 PNG 是不是真无损、抓的是渲染分辨率还是桌面分辨率。
拍一张纯色测试图确认。

### 9. 两个校验脚本合并还是分离

**三分，不是二选一。**

- `tools/cs2_render_check.py` —— **保留为开发工具**（我们的管线 vs 我们的管线）。
  顺手修背景启发式：让 `tools/pbr_emulate.py` 输出覆盖 mask，别猜众数颜色。
- `tools/cs2_videocheck.py` —— **废弃并吸收**。
  它那张手填 `LANDMARKS` 表正是 manifest 要取代的东西。
- `tools/cs2_reference_check.py` —— 新建，**唯一的验收入口**。

留着两个都长得像“和 CS2 比”的工具，迟早有人信了过期的那个。

### 10. AK 弹着图能否独立校准后坐尺度

**能，但只能标定整体尺度，而且缺两个记录项。**

- 必须记 **`weapon_recoil_scale`**（CS:GO 默认 2.0；CS2 若保留且非默认，所有角度按比例错）。
- **不需要墙面距离**：只要开火前的视角被 `setang_exact` 还原（方案已经这么做），
  弹孔的屏幕位置**就是**射击角度，经游戏投影换算即可。
  只需记录**游戏 FOV 和渲染宽高比**。
- `weapon_accuracy_nospread` 要先确认这个 build 还在。不在也能做：
  开火散布 0.0078 比几发之后的后坐位移低一个量级，只是前 2–3 发噪声大。
- 边界照原文：标定的是我们那**一个标量**，不构成 CS2 喷射图案的证据。

### 11. 何时把 GunProfile 默认改成 1

**五条全过 + 一条决策。**

1. 阶段 3：三把枪 idle 静帧，**offset 钉死读到的值、fov 不拟合**，地标 / IoU < 10 px。
2. 阶段 4：左手 IoU ≥ 0.85 且三个边地标 < 10 px；右手只报告。
3. 阶段 2：一个倍率在一把枪上拟合，另外两把均值 < 5%，三把形状比 < 10%。
4. 人工 QA：三段视频各一遍，无漏声 / 错声 / 重复播放、无动画跳变、火光曳光观感正常。
5. `verify_cs.py` + `videocheck.py` 与 0.15.10 逐行相同，六个 CS2 自检 PASS。

**外加一条必须先决策的**：`GunProfile = 1` 现在同时翻**玩法**
（AWP 不开镜散布 0.10°→4.63°、M4 踢枪 +16%）。那不是渲染验收能覆盖的。
**建议把开关拆成 `GunProfile`（视觉）和 `GunNumbers`（玩法）两个**，
视觉先翻默认，玩法单独决定。不拆的话，发布说明必须写明手感会变。

**不 gate 的**：后坐绝对尺度、弹壳、另外 11 套手套、34 条缺音频的 cue
（该补，但属内容缺口）。

### 12. 分级实施清单

见第 D 节。

---

## D. 分级实施清单

### D1. 必须立即做（在采集之前，否则采回来的素材作废）

1. **确认并固定采集时的分辨率 / 宽高比**。以显示器原生 16:9 启动
   （`-w 1920 -h 1080 -fullscreen`），或明确记录 1400×1050 拉伸并在比对时反变换。
   **这一条不做，后面全部白采。**
2. **清空库存影响**：三把枪用出厂皮（无皮肤 / 贴纸 / StatTrak），**卸下手套皮肤**，
   用默认探员。在 `capture.json` 里记录并在截图上人工确认。
3. **一次性验证 Game Bar 的 PNG**：是否无损、抓的是渲染还是桌面分辨率。
4. 采集 cfg 里显式写死四个 viewmodel cvar，并 `echo` 读回值存档；
   同时记录画质档、`fov_cs_debug`、`weapon_recoil_scale`。
5. **把验收从视频改成静帧**：每枪一张 idle 无损 PNG
   （M4 消音 / 不消音各一张，AWP 另加一张开镜），视频只留 QA。

### D2. 默认翻转前必须做

6. `tools/cs2_reference_check.py`（manifest 驱动），规则按 B1 / B2 / B3 定死：
   **offset 不拟合**、亮度用“单枪拟合 + 另两枪验证”、左手 gate 右手报告、
   每项独立 PASS / FAIL、缺输入即 FAIL 不许被总 PASS 掩盖。
7. 每张参考静帧配**显式 mask**（枪身一张、左手一张），
   一张 mask 同时供亮度和剪影 IoU。
8. `ArmPreview cs2` 扩展输出手部地标（按第 6 问的三个“边”，不是指尖）。
9. `tools/pbr_emulate.py` 输出覆盖 mask；`tools/cs2_render_check.py` 改用它，
   删掉众数颜色启发式。
10. VPS 装 ffmpeg / ffprobe（QA 视频抽帧用）。
11. 废弃 `tools/cs2_videocheck.py`，能力并入第 6 项。
12. 修 `Drawable already added`（曳光重复叠加导致亮度翻倍，见 A4）。
13. 补 deploy 与 AWP 开镜两项采集（第 5 问）。
14. 决策并实施 `GunProfile` / `GunNumbers` 拆分（第 11 问）。

### D3. 发布后也可补

15. 34 条缺 OGG 的 CS2 cue：从 `05_audio/decoded/` 转单声道装包。
16. AK 弹着图标定后坐绝对尺度（按第 10 问补记 `weapon_recoil_scale` 和游戏 FOV）。
17. 弹壳特效。
18. 另外 11 套手套。
19. 动画计时改由 CS2 时长驱动（现在仍按 CS:MC 时长，m4a1s inspect 差 9 帧）。
20. AK 的 `cliprelease`（CS2 的 body_hd 没有这块几何）。

### D4. 不应该做 / 指标本身不成立

21. **`--fit-fov` 参与 PASS / FAIL** —— 实测会让 fov 66–70 全过（B1）。降级为诊断输出。
22. **同时拟合 fov 和 offset** —— 同上，保证 PASS。
23. **阶段 2 用“均值 <5%”当判据** —— 拟合了就是 0%，不拟合就是在比两个渲染器（B2）。
24. **右手指作为阶段 4 的 gate** —— 只有 13.3% 在画面里（B3）。
25. **用输入时间戳做帧对齐** —— 三段不可观测延迟 + 165→60 Hz 采样（B4）。改用静帧。
26. **用录像音频峰值验证声音触发时间** —— 源数据更权威（原文第四节判断正确）。
27. **装 OBS** —— 我们不靠视频拿数字，收益不抵成本。
28. **把弹着图当阶段 1–4 的前置** —— 原文第十节已经写对，保持。

---

## E. 下一步

本次没有改动任何运行时代码。

要开始做 D1 第 5 项（验收改静帧）和 D2 第 6 项（`cs2_reference_check.py`）的话，
会先把输入格式（`capture.json` / mask / `landmarks.json` 的字段）、输出格式和每项判据
写成一份规格供确认，再动手。
