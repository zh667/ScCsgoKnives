# 2026-09-30 三个单机问题：VPS 实施、证据与交付

写入者：VPS Claude（本轮 src/tools 唯一写入者：Windows 端最后写入为 10:36–10:37 的审查文档，src 在此前 3 小时内无改动；Syncthing 空闲、无错误/冲突；审查 evidence.json 所列 8 个源文件哈希与当前 VPS 源码一致）。
入口：[审查](post-mp-bugs-20260930-review.md)、[证据](post-mp-bugs-20260930-evidence.json)。联机暂停：本轮未继续 M1–M4、未改平台、网络适配层不进正式包、未回滚联机成果。未安装、未改玩家原世界、未公开发布。

## 0. 来源、打包范围与证据等级

- 源码 = 当前 VPS 树（含已通过 c17mp 单机门禁、在 1.9.3.1 上保持休眠的联机核心代码）+ 本轮改动。候选包由发布流水线 `completion_140 c18 all` 生成（job `60abd854dc3b4aaab7f11c4e913a3bbd`），**不含** `Net/ScCsgoNet.bin`，版本元数据仍为 1.4.0。
- 实机测试包 `sp-bugs1-lite/full.scmod`（`m0.py sppkg bugs1`：已交付 c15 包只替换本阶段 DLL，断言不含适配层）。构建是确定性的：c18 候选内 DLL 与测试包逐字节相同（全量核心 `fc77069b…`、轻量核心 `7863e213…`、全量战术 `7893a39c…`、语音 `47160de9…` 未变）；所以下文实机测试结论直接适用于候选 DLL。
- 实机环境：Windows 上隔离的固定运行时（`.tmp/mp-test-runtime/server`，1.9.3.2_MP 引擎的单人模式，不联网）+ TestAutomation；不是玩家的 1.9.3.1 安装，也不是 Android。录音为该游戏进程自身的输出（Windows 进程回环，`tools/ProcessAudioCapture`，不含其他程序、无麦克风）；截图为引擎自己的截图路径（`ScreenCaptureManager.Capture`）。**尚无真人手操/听感验收**。

## 1. 快速滚轮切刀：声音叠加/拖尾（已修复，实机 A/B 已测）

### 确认的根因（均有实机复现）
1. 部署/机械音无归属：`ScPresentationSound.Play` 每次 new 一个一次性声音，切槽不取消。c15 实测（10 格快捷栏、封闭石室、无天气）：快速滚动时引擎同时播放的声音最多 20–33 个（滚动前基线 6 个），停在非 CS 格后仍可听到约 0.75 s、引擎计数约 0.85 s 才回落。
2. 绘制钩子逐帧重启部署：原版向普通方块的换手动画不能被打断，在方块上停留过半程（约 0.25 s）再滚回刀时，渲染仍画着方块，`OnFirstPersonModelDrawing` 把手部状态清空，下一帧枪械子系统又当作新切入重新 Draw 并再放一次部署声。同一构建 A/B：关掉修正时 4 次滚轮产生 39–43 次 Draw（每次一声 knife_deploy），打开修正时 2 次。

### 修改
- `ScPresentationSound`：部署声、检视声、枪械片段声（Scheduled）、雷/C4 的 `_draw` 都作为该玩家第一人称手（ComponentFirstPersonModel）+ 动作序号的"持有声音"。切到新物品（新 Draw）、切到非 CS 物品/死亡、检视被打断时，旧声音 60 ms 线性淡出后停止；离开世界立即停止；每人最多 4 个。素材长度、音高不变；刀挥砍、枪声、世界声音不受管理、从不被停。
- `KnifeAnimationController`：新 Draw 释放旧声音；非 CS 物品释放；未完成的检视被新动作取代时释放其声音（Draw 被检视打断时不释放：同一物品）。`FollowHeldItemOnly`（默认开）：绘制钩子不再因滞后的原版 m_value 清空手部状态，由每帧的持有物更新负责。
- 每帧 `ScPresentationSound.Tick()`（任意界面）；`OnProjectDisposed` 调 `ReleaseAll`。诊断日志 `ScPresentationSound.Record`（默认关）。

### 实机结果（`tools/MpM0/sp_knife_audio.py`，引擎级滚轮注入：下一帧 Dispatcher 调 `Mouse.ProcessMouseWheel`，经 ComponentInput→ComponentPlayer 真实改槽）
快捷栏 10 格：刀、AK、HE×3、沙鹰、烟雾×3、AWP、闪光×2、燃烧瓶、泥土、C4。c15 = job 结果 `knife-audio-c15-lite-10slots-norain`，新 = `knife-audio-bugs1-lite-10slots-norain`（两者噪声底 −99 dB）。"可听"含约 0.1–0.2 s 采集延迟。

| 场景（结束在） | c15 最多同时 / 停后可听 / 计数回落 | 新 |
|---|---|---|
| 快速扫过全部格子到非 CS（泥土） | 26 / 0.75 s / 0.85 s | 10 / 0.17 s / 0.06 s |
| 蝴蝶刀同上 | 26 / 0.77 s / 0.68 s | 10 / 0.17 s / 0.06 s |
| 十格全扫到 C4 | 24 / 0.71 s | 10 / 0.50 s（C4 自身部署声） |
| 快速 CS 间切换到刀 | 20 / 0.85 s | 9 /（最后的刀部署声 0.615 s，属正常） |
| 在方块上停留再回刀 | 33 | 7–8（修正关闭时 Draw 40 次） |
| 离开世界时正在部署 | — | 部署声在 0.17–0.19 s 时以 "world exit" 停止 |

新版日志：每次 Draw 一声，旧声音全部 "release → stop"，只有最后留在手里的物品的部署声自然结束。结束在 CS 物品上的场景因部署片段随机（deploy/deploy2）尾长不同，属正常。

逐个声音命名（驱动每帧对比引擎正在播放的声音，经内容缓存反查资源名）：滚轮停下后本模组声音（`Audio/ScCsgoKnives/*`）最后结束的时刻——结束在非 CS 格的 4 个场景（含蝴蝶刀、直接选槽）c15 为 0.57–0.87 s，新版全部为 0.064–0.065 s（60 ms 淡出 + 1 帧）；结束在 CS 物品上时新版 0.42–0.99 s，正是最后那件物品自身的部署/片段声（与 c15 同类场景一致）。此前一次"直接选槽"停后约 1.2 s 的声音由此确认不是本模组声音（同场景本模组声音 0.065 s 内全部结束）；该轮环境中记录到的是世界自身的 `Audio/Sizzles/Sizzle2`、`Audio/Splashes/Splash1`（熔岩/水）。报告 `knife-audio-{c15-lite,bugs1-lite}-named2.json`。

测试环境说明：每轮在玩家所在位置正下方、世界高度 y=14 处挖一间被 9 格花岗岩包围的封闭石室并关闭天气（引擎按 7 格内降水柱决定雨声，封在地下也会响）；个别世界附近的熔岩/水仍有声音，所以最终结论以命名后的本模组声音时间为准，录音作为同时间轴的佐证。滚轮注入与声音采样都在游戏帧内完成（与真实滚轮事件同一入口）。

## 2. 烟雾转视角透视（已修复，离线几何 + 实机截图 A/B）

### 确认的机制
烟片用 `camera.ViewRight/ViewUp`（与屏幕平行），`SmokeAxes` 再按世界 X/Y/Z 分量裁剪尺寸：同一位置只转镜头，烟片的世界朝向和裁剪后大小就变，覆盖随之开合；世界密度体积（AI 遮挡、烟内覆盖）不随镜头转。

### 修改
`ScGrenadeVisuals.FacingAxes/SmokeQuad`：每片烟朝向观察者位置（球面公告板，世界上方向保持竖直），再做原来的云边界裁剪；正上/正下方退化时只用相机右轴决定平面内旋转。`SubsystemScGrenades.DrawSprites` 与 HE 开口采样（`ApplySmokeOpenings`）改用它。粒子数、纹理、alpha、密度体积、AI 规则、烟内覆盖、弹道/伤害都不变。

### 证据
- 离线（`tools/PackageCheck/SmokeViewRegression.cs`，交付 DLL + 交付图集 alpha、渲染器的四角与 uv、0.1 近裁剪；进入 `--visual-checkset` 与主检查）：每种烟龄 1568–1860 条"深"视线（AI 规则体积内长度 >1.5 m），每条在 27 种 yaw/pitch/FOV 下取最小值。旧屏幕对齐：满烟 10 条、成长期 19 条（最低 0.004，完全透开）、消退期 2 条低于 85%，同一视线随转镜头变化最大 1.000；新：0 条，最低 0.893–0.990，随转镜头变化 0.000；体积外 1 m 以上的清晰视线旧版有 9 条被盖住 >35%，新版 0 条。Lite、Full 两版结果相同（job `7eaccab4cadf4e03a5d7f3914c339d98`）。
- 实机（`tools/MpM0/sp_smoke_view.py`，固定位置、烟龄每帧锁定、目标为烟后 7 m 的钻石柱；每个视角三帧：有烟/无烟/无烟无目标，"可见"=目标像素中未被烟改变的比例；5 场景×8 位置×最多 10 个视角，共约 230 视角/次）：站立 8 m、近 5 m、侧移、17 m 蹲低、烟内、第三人称在 c15 与新版都全程被遮（≤0.03）；9 m 高处视线从烟顶上方通过，两版都可见（合理）。**抬高 5 m 处复现了反馈**：c15 同一位置只转镜头，目标可见比例在 0.028–0.223（成长期）、0.002–0.188（消退）、0.005–0.250（HE 开口）之间变化；新版 Lite 分别为 0.099–0.135、0.074–0.101、0.114–0.151，Full 为 0.106–0.141、0.002–0.013、0.231–0.291（随转镜头的变化≤0.06；残留的固定比例是视线从较矮的成长/开口烟体上方看到的柱顶，与体积一致）。对照图 `.tmp/bugs-fix-20260930/smoke-raised-growing-c15-vs-new.jpg`（Windows）。
- 墙体场景中侧移位置目标本来就被墙挡住，记为"不适用"而非"可见"。

## 3. 美好时代新生物不受 CS 枪伤（未能完成目标确认：缺新版包）

- 本机只有 `D:/下载/[API1.9.3]美好时代V1.9.3a.scmod`；另两份（`E:/projects/bf2042-scmod/reference/good-times`、`E:/projects/ScBf1Weapons/local/reference/WonderfulEra`）与它 DLL 相同（`1de79e9f…`，44 个生物模板一致），都没有视频中的持镰刀生物；MP 平台的 WonderfulEra 兼容代码只涉及载具/坐骑。**所缺文件：包含该生物的新版美好时代 .scmod（或该生物代码/错误堆栈）**。没有用旧版通过冒充修复。
- 攻击接口审计：玩家枪（`ScSurvivalBalance.Attack`→`GunAttack`/Zeus `ElectricAttack`）、同伴（`GunAttack`）、敌队（`BulletAttack`）都经 `ScDamageIndicator.Deliver`→原生 `ComponentMiner.AttackBody`，`Projectile` 都为 null；刀为 `MeleeAttackment`。引擎在 `AttackBody` 里捕获 `ProcessAttackment`（含所有第三方钩子、伤害计算、Injure）的任何异常，只记 `Attack execute error`——若新生物的代码对空 Projectile 抛异常，该次命中会被静默丢弃。这是有依据的假设，不是已确认根因。
- 已加：`ScAttackJournal`（默认关）在唯一交付点记录目标模板、攻击类型、Projectile 上下文、提交威力与经钩子后的威力、攻击自身计算的伤害、实际血量变化；"伤害未计算"即 ProcessAttackment 提前中断，对照日志中的 `Attack execute error`。实机验证（`sp_attack_journal.py`，全量测试包）：`GunAttack by MalePlayer on Cow_Brown projectile null power 15 -> 15 injury 0.429 health 1 -> 0.5714`（AK 两发）、`ElectricAttack by MalePlayer on Cow_Brown projectile null power 150 -> 150 injury 4.286 health 1 -> 0`（Zeus），无 `Attack execute error`。刀在该脚本里未打中（目标血量未变），刀与枪走同一交付点，但刀的日志行本轮未实测。拿到新版包后用 `sp_attack_journal.py <包> <标签> <新生物模板名>` 即可取证。
- 一致性修正：敌队 `BulletAttack` 之前不经 `ScProjectileDefense`（已审计的 Subnautica Boss 受击间隔/减伤），现在与玩家、同伴枪击一致；其他目标不受影响。未伪造 Projectile、未广播碰撞钩子；Boss 免疫/间隔/伤害上限/盾挡保持。

## 4. 门禁（c18，job `60abd854dc3b4aaab7f11c4e913a3bbd`）

`failed steps: []`。main 全量 0/13923、轻量 0/14229（比 c17mp 多 9 项为新增 smoke-view），无基线外新失败；video-feedback 两版 0/292；ai 两版 0/66；c4 两版 0/73；split、native、motion、throw、hotspots、ui 通过。战术套件 4 项既有失败与 c15、c16mp、c17mp（及 c04 以来各阶段）名称与失败原因完全一致：`native-gltf/ct`、`native-gltf/t`（missing selected animation）、`npc-full-registry-reload-refuses-without-ammo-loss`（failed reload consumed or allocated）、`optional-package-identity-and-no-bundled-engine`（paired core version not enforced by engine）；无新失败、未豁免。

## 5. 交付与清理

- 交付（常设授权：门禁通过后直接替换 output 同名包；job `72043cfa4dbc4905a120f303f1fe7a7b`）：先核对 output 与 `release-1.4.0/manifest.json` 一致、候选与流水线记录一致，再同卷移动覆盖（只留一份），读回：
  - 全量 `[API1.9]CS武器1.4.0-全量包.scmod` 526703029 字节 `fc54e2ecc1165a62d399619fbca223ff8228ea9875bd923c49cb0524250e1afd`（原 c15 `0e621ed2…`）
  - 轻量 `[API1.9]CS武器1.4.0-轻量包.scmod` 35578081 字节 `37607a216dbdf2b97b5e5fb0efd83df7f711cdf53bb5ffdaa5098759753d2cbc`（原 `31baa9db…`）
  - 探员 `[API1.9]CS武器1.4.0-探员包.scmod` 38463504 字节 `234a21816515563ba98e8fd1aac2f5dfd1778c366f37b04b5172c4037fcd0e2d`（原 `2a397b49…`）
  - 与 c15 逐成员比较：全量只变 `ScCsgoKnives.dll`、`ScCsgoTactical.dll`（1706 项不变），轻量只变 `ScCsgoKnives.dll`（1341 项不变），探员只变 `ScCsgoTactical.dll`（370 项不变）；无增删成员，资源全部原样；三包均无 `Net/ScCsgoNet.bin`；版本 1.4.0 未改。
  - `output/release-1.4.0/manifest.json` 已更新（c15 记录移入 `replaced.previous` 与 `history`）；`tools/completion_140.py` 的 BASELINES 改为新哈希。测试脚本里指向 output 的别名改为 `output-lite/full`。
  - 未安装到 Mods、未写玩家世界、未公开发布；玩家 Mods 中的旧包（审查记录为 c15 全量）未动。
- 清理（常设授权，收据 [post-mp-bugs-20260930-cleanup.json](post-mp-bugs-20260930-cleanup.json)）：永久删除 6544487540 字节 / 2738 项——c18 与 bugs1 阶段的构建树、两个单机测试包、烟雾截图原始帧、9 段被取代的录音；保留所有报告/日志/源码哈希、c15 与新版抬高位置的原始截图（100 张）、4 段录音、对比图。未动联机任务的阶段与包（c16mp、c17mp、mp14–mp17、dev-mp17 等）。清理后再次核对 output 三包哈希不变。

## 6. 执行记录（Windows worker，均经 hash 守卫）

- 构建/测试包：`bugs0930-devbuild-bugs1-01`（job `2d44286835a742de9b2cda9e17e4ab51`）、`bugs0930-sppkg-bugs1-01`（`01f764e9e6c64bacb44cdf697a9aca2d`）；离线视觉检查 `bugs0930-visual-bugs1-01`（`7eaccab4cadf4e03a5d7f3914c339d98`）。
- 声音：c15 `bugs0930-knife-audio-c15-lite-04/06`，新 `bugs0930-knife-audio-bugs1-lite-06/08`（早期轮次为调试测试环境：7 格快捷栏、雨声、缺类型查找等，结论不采用）。
- 烟雾：c15 `bugs0930-smoke-view-c15-lite-03`，新 Lite `…-bugs1-lite-03`，新 Full `…-bugs1-full-02`。攻击记录 `bugs0930-attack-journal-bugs1-full-03`。
- 门禁 `bugs0930-gates-c18-01`（`60abd854dc3b4aaab7f11c4e913a3bbd`）；战术既有失败对照 `bugs0930-tactical-known-04`；交付 `bugs0930-deliver-c18-01`；清理 `bugs0930-cleanup-do-01`（`d4903adaeb15488688897bcee251cb1e`）。

## 7. 剩余缺口

- 美好时代新生物：缺新版包，目标特定根因未确认，未改伤害契约。
- 未覆盖：两个本地玩家（分屏）的声音归属只在代码层按每人第一人称手分开，未实机执行；菜单界面里点选切槽未驱动 UI（直接改 ActiveSlotIndex，覆盖同一改槽路径）；退出后重进世界未单独验证（离开世界时的停止已验证）；实机均在隔离的 1.9.3.2_MP 引擎单人模式，玩家的 1.9.3.1 安装与 Android 未测；真人听感与视觉验收待 Windows 端。
- 刀击的攻击记录行本轮未实测（见 §3）。
