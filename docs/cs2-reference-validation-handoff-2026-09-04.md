# CS2 第一人称参考采集与验收：VPS 交接审查

日期：2026-09-04

请作为第二审查方，基于以下已经验证的事实，评审 CS2 第一人称迁移项目接下来的验收和自动采集方案。重点是判断哪些指标真正需要 CS2 运行时参考、采集方案是否足够严谨、现有工具还需要补什么。不要重复已经完成的逆向和离线验证。

项目目录（Windows）：

```text
E:\Obsidian Document\Document1\ScCsgoKnives
```

计划书（Windows）：

```text
E:\Obsidian Document\Document1\CSMCReverse\B方案-CS2第一人称迁移计划-2026-09-04.md
```

## 一、已经确认的硬证据

### 1. CS2 动画解析

- 310 个 binary DMX 全部逐字节解析完成，读取余量为 0。
- 用 hand IK 目标做独立校验，最大偏差约 0.2 mm。
- 输出 JSON 回读误差为微米级。
- 解析正确性已通过，不需要录像继续证明。

### 2. CS2 与 CS:MC 骨架关系

- 两套骨架物理结构不同，不是单位换算问题。
- 同名骨静息骨长比值约 0.93–0.58。
- CS2 多出肩关节、掌骨、扭转骨。
- AK 等主要 clip 的时间轴和动作增量一致，说明 CS:MC 使用的是同源动作套在旧手骨架上。
- 原计划“逐骨绝对矩阵必须一致”的门槛前提错误，已取消并放行后续阶段。
- 已知例外：阶段 1 报告记录 `AWP shoot1` 存在版本/帧数差异，不能笼统认为所有 clip 完全相同。

### 3. 阶段 2–6 离线验证

已有自检工具：

- `tools/cs2_rig_selftest.py`
- `tools/cs2_mesh_selftest.py`
- `tools/cs2_placement_selftest.py`
- `tools/cs2_arms_selftest.py`
- `tools/cs2_effects_selftest.py`
- `tools/cs2_weapons_selftest.py`

已经验证：

- C# 出厂摆放链与 Python 离线参考最大差约 0.0504 px。
- CS2 手臂蒙皮顶点与独立参考最大误差约 1.4 µm。
- 网格、材质绑定、动画、骨权重和逆绑定等结构检查通过。
- CS2 数值表从 vdata 读取，24 个字面值核对无不符。
- 仍属估计项：手臂/手套粗糙度、部分火光尺寸、曳光宽度、后坐绝对尺度、移动散布插值方式等。

## 二、0.16.3 本机运行验证

安装包：

```text
E:\Obsidian Document\Document1\ScCsgoKnives\output\ScCsgoKnives-0.16.3.scmod
```

大小：

```text
46,901,361 bytes
```

SHA-256：

```text
71BC7DF73D62C922FDEB77360457531C754F69902C99FFD0FF09EB6ED05E4BEB
```

游戏目录中的安装包与上述文件哈希一致。

本机已经完成：

- 安装 0.16.3。
- AK-47、M4A1-S、AWP 运行试玩。
- `GunProfile = 1` 新 CS2 管线测试。
- 改回 `GunProfile = 0` 的热回退测试。
- 刀具回归测试，刀具不受 GunProfile 影响。
- 日志确认 `0.16.3 initialized`。
- CS2 手臂和无指手套进入真实渲染路径：`cs2 arms drawn: bare_arm_133, glove_fingerless`。

当前测试配置：

```text
GunProfile = 1
Cs2Arms = 1
```

## 三、CPU 蒙皮本机实测

日志共取得 147 组样本，每组 120 帧：

- 最小：0.166 ms/frame
- 平均：0.1742 ms/frame
- 中位数：0.172 ms/frame
- P95：0.181 ms/frame
- 最大：0.241 ms/frame
- 原验收目标：<3 ms/frame

即：

- 中位数约有 17.4 倍余量。
- 最大值约有 12.4 倍余量。
- CPU 蒙皮性能验收通过，不需要继续阻塞发布测试。

日志还存在：

```text
SubsystemDrawing: Drawable [Game.SubsystemScGunBlockBehavior] already added.
```

但该错误在旧版本运行记录中也出现过，目前不像 0.16.3 新引入的回归，可另行追踪，不作为此次性能失败。

## 四、声音结论

原始声音事件时间来自：

- CS2 `.vnmclip` 中的 `CNmClipDocEvent_Sound`；
- 对应 DMX clip 的真实 frameRate；
- 生成文件 `AnimationData/cs2_sounds.json`。

因此声音事件“何时触发”不需要用录像音频峰值证明。录屏峰值会受到音频文件自身的静音头/起音、混音和混响、音频设备及录制缓冲、60 fps 时间量化等因素影响，不能高于原始事件轨的权威性。

但是不能简单把 CS2 时间表强制设为所有旧管线的全局默认，因为：

- `AWP shoot1` 存在 clip 版本差异；
- 阶段 1 报告记录过部分 CS2 cue 没有对应 OGG，会在加载时被丢弃；
- M4 消音器旧表存在一条 CS2 事件轨中没有的 cue。

当前代码策略：

- `GunProfile = 1` 时自动采用 CS2 事件表；
- `GunProfile = 0` 时保留旧声音表；
- CS2 表缺少对应行时回退旧表。

请判断这一按 profile 绑定声音表的策略是否应保持。

## 五、真正仍需要 CS2 运行时画面的项目

不需要画面的项目：

- DMX 解析正确性；
- 动画事件帧；
- 网格、UV、切线和材质绑定；
- 骨权重和蒙皮；
- 第一人称摆放链的数学实现；
- CPU 蒙皮性能。

需要画面才能兑现原计划数字的项目：

1. 阶段 2：实际 CS2 画面的亮度均值/中位数误差 <5%。
2. 阶段 3：最终屏幕空间武器地标误差 <10 px。
3. 阶段 4：最终可见手指/握持位置误差 <10 px。
4. 动态观感、漏声/错声等最终人工 QA。

声音事件触发时间不再作为依赖录屏的指标，应记为“源数据验证通过”。

## 六、当前采集环境

本机已安装 CS2：

```text
E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe
```

CS2 cfg 目录：

```text
E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg
```

本机 viewmodel 配置：

- `viewmodel_fov 68`
- `viewmodel_offset_x 2.5`
- `viewmodel_offset_y 0`
- `viewmodel_offset_z -1.5`

Xbox Game Bar：

- 已安装并启用；
- 游戏音频捕获已开启；
- 当前捕获配置显示为 1280×720，需要改为 1920×1080、60 fps；
- 可用 `Win+Alt+R` 开始/停止 MP4；
- 可用 `Win+Alt+PrintScreen` 输出 PNG。

Steam Game Recording 也可用，但导出 MP4 需要经过 Steam 录制界面。

当前未安装：

- ffmpeg
- ffprobe
- OBS

而 `tools/cs2_videocheck.py --extract` 当前直接调用 ffmpeg，因此现状下不能直接抽帧。

## 七、建议的自动采集设计

拟新增 `game/csgo/cfg/codex_capture.cfg`，固定：

```cfg
sv_cheats 1
bot_kick
cl_drawhud 0
cl_showfps 0
viewmodel_fov 68
viewmodel_offset_x 2.5
viewmodel_offset_y 0
viewmodel_offset_z -1.5
```

拟新增 `tools/cs2_capture.ps1`，负责：

- 使用 `-insecure -console -w 1920 -h 1080 +map de_dust2 +exec codex_capture` 启动 CS2；
- 等待地图载入；
- 回到固定 `setpos_exact` / `setang_exact`；
- 自动给予 AK/M4A1-S/AWP；
- 模拟攻击、换弹、检视；
- 调用 Xbox Game Bar 热键录制 MP4 和截取 PNG；
- 写入每次输入动作的时间戳。

固定位置需要首次进入 dust2 后人工选择一次光照均匀处，并用 `getpos_exact` 保存。之后可完全重复。

每枪采集：

- 待机 2 秒；
- 待机无损 PNG；
- 打空弹匣；
- 换弹；
- 检视；
- 在换弹/检视关键帧输出 PNG。

额外：

- M4：消音器拆装；
- AWP：腰射、开镜、射击后退镜/回镜。

建议目录：

```text
reference/cs2/<date>/
  capture.json
  ak47.mp4
  ak47_idle.png
  m4a1s.mp4
  m4a1s_idle.png
  awp.mp4
  awp_idle.png
  masks/
  landmarks.json
```

`capture.json` 记录：

- CS2 build/version；
- 地图；
- setpos/setang；
- viewmodel cvar；
- 分辨率；
- HDR/画质设置；
- 录制工具；
- 每个动作的开始时间或帧号；
- 所有输入文件 SHA-256。

## 八、现有校验脚本的问题

`tools/cs2_videocheck.py` 当前：

- `LANDMARKS` 为空；
- 只比较人工填写的武器地标；
- 不检测视频规格；
- 不测亮度；
- 不测声音；
- 不测手指；
- 依赖尚未安装的 ffmpeg。

`tools/cs2_render_check.py --reference` 当前也不能直接用于真实 dust2 截图，因为它用“画面最常见的单一颜色”判断背景。这只适用于离线纯色背景，不适用于真实地图。

因此“一段录像加现有 videocheck 就能完成四项验收”不成立。

## 九、建议的校验脚本扩展

请评审是否应实现一个 manifest 驱动的统一工具，例如：

```text
tools/cs2_reference_check.py --manifest reference/cs2/<date>/capture.json
```

### A. 输入规格

- ffprobe 校验 1920×1080、60 fps；
- 检查音轨；
- 检查持续时间/掉帧；
- 核对 cvar、CS2 版本和文件哈希。

### B. 阶段 2 亮度

- 每张 CS2 参考图必须有显式 `mask.png`；
- 禁止用最常见颜色猜背景；
- 在正确线性化的 RGB 空间计算枪身亮度；
- 比较均值和中位数；
- 两项误差都必须 <5%。

### C. 阶段 3 摆放

- `landmarks.json` 记录枪口、扳机、弹匣等像素位置；
- 调用 `ArmPreview cs2` 输出同名预测坐标；
- 输出每个地标欧氏距离；
- 最差值必须 <10 px；
- 必要时 `--fit-fov` 只拟合 `viewmodel_fov`。

### D. 阶段 4 手指

- 扩展 ArmPreview 输出全部可见指节/指尖；
- 在参考帧标注对应手指地标；
- 分别报告左右手误差；
- 最大误差 <10 px。

### E. 声音

- 不以视频音频峰值决定触发时间；
- 原始事件帧判为 source-verified；
- 视频只用于检查漏声、错声、重复播放和明显音画异常。

输出：

- `cs2-reference-report.json`
- `cs2-reference-report.md`
- 每项独立 PASS/FAIL
- 不允许用一个总 PASS 掩盖某项缺失输入。

## 十、AK 弹着图方案

该项不阻塞发布测试，只用于把后坐绝对尺度从估计变成实测。

建议：

1. 固定玩家位置和初始 `setang_exact`。
2. 正对墙面，鼠标不动。
3. 若当前 CS2 支持，开启 `weapon_accuracy_nospread 1`，先剥离随机散布。
4. AK 连射 30 发。
5. 用原始 `setang_exact` 恢复开火前视角。
6. 截取 1920×1080 PNG。
7. 重复三次。
8. 根据弹孔相对画面中心的像素分布换算角度，只拟合后坐整体尺度。

限制：

- 当前移植没有实现 CS2 确定性逐发喷射图案；
- 弹着图只能校准整体幅度/RMS/P95，不能证明每发轨迹一致；
- 不应把这项当作阶段 1–4 的前置条件。

## 十一、请重点决策并回答

请给出明确结论，不要只复述方案：

1. `GunProfile=1` 使用 CS2 声音表、旧 profile 使用旧表的策略是否正确？
2. 是否同意把“声音峰值”从录像硬指标中删除，改为源事件帧验证？
3. 阶段 2 的亮度 `<5%` 是否必须要求显式枪身 mask？
4. 真实 CS2 录屏是否还缺少必须固定的变量，例如 agent、手套、武器皮肤、地图坐标、曝光/HDR、画质或帧同步？
5. 三枪“待机→打空→换弹→检视”是否足够覆盖阶段 2–4？还应增加哪些 clip？
6. 手指 `<10 px` 应比较哪些可稳定辨认的地标？是否比轮廓/接触区域 IoU 更合理？
7. 自动输入的动作时间与视频帧如何可靠对齐？输入时间戳是否足够，还是需要视觉检测动作起点？
8. Xbox Game Bar 是否足够，还是应安装 ffmpeg/OBS；请说明取舍。
9. `cs2_videocheck.py` 和 `cs2_render_check.py` 应合并还是保持分离？
10. AK 弹着图的无散布方案是否真的能独立校准后坐尺度？
11. 在哪些验收通过后，才应把 `GunProfile` 默认值从 0 改成 1？
12. 请输出一份按优先级排列的实施清单，区分：
    - 必须立即做；
    - 默认翻转前必须做；
    - 发布后也可补；
    - 不应该做或指标本身不成立。

请特别指出上述事实或方案中任何技术上不严谨、互相矛盾、无法复现或会产生虚假 PASS 的部分。

## 十二、执行边界

- 先完成审查和决策，不要在结论明确前修改运行时代码。
- 可以读取项目内报告、脚本和资产证据复核上述事实。
- 不要重新做已通过的 310 个 DMX 解析或 CPU 蒙皮测试。
- 如果决定实施校验工具，先列出准确的输入格式、输出格式和验收规则，再开始编码。
