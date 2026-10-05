# current-direction-20260929 第二轮 VPS 结果：同伴护甲、有效命中事件、Dink/火花、护甲 HUD、物理攻击防护、投掷预览 A/B、MP M0 及后续联机

规划入口：[current-direction-20260929.md](current-direction-20260929.md)，证据 [evidence.json](current-direction-20260929-evidence.json)。c13 的历史结果仍在 [vps-results.md](current-direction-20260929-vps-results.md)，本文件只记本轮。
状态分为：**代码已实现**、**离线测试实际执行**（runner + 结果 + 输入）、**离线渲染人工审阅**、**实机验收**（Windows 游戏 / Android / 双端联机）。

## 0. 输入与写入者

- 起点：c13 正式包（全量 `0bfa61e5…`、轻量 `f2eff2bb…`、探员 `ee92acfe…`）。
- 写入者：Windows 本轮只写规划/证据文档；VPS 写 src/tools。每次 Windows 构建前按 SHA-256 核对输入文件；Syncthing 空闲、无错误和冲突。Windows 未提交的独立改动（含 `tools/CompatibilityCheck/ZeusBalanceRegression.cs`）未动。

## 1. 本轮发现并纠正的规划前提

- **EntityId 在本引擎是稳定且不复用的**：`Project.SaveEntities` 直接写 `Entity.Id`（旧 EntityToIdMap 已标 Obsolete），`NextEntityID` 随存档保存且只增不减；同伴远距休眠台账按原 ID 重建。因此同伴护甲键用 `companion-<EntityId>`。另一方案（同伴组件内加 GUID 字段）会在 1.3.0 旧版打开存档时被旧版自己的 TacticalCompanion 保存逻辑丢掉。
- **c13 的护甲在"计算伤害"时就扣了耐久**：原生 `CalculateInjuryAmount` 在 `Injury.Process` 的无敌判断之前执行。本轮改为"计算时只规划、`ComponentHealth.Injured`（存活、非无敌、伤害>0）时才提交"。

## 2. 代码已实现

| 项 | 实现 | 主要文件 |
|---|---|---|
| 同伴工作台护甲 | 目标选择（自己 / 8 格内自己的同伴），报价冻结目标身份与库存修订后原子制作/维修（`TryCommitFor`，材料不足或目标变化则整单拒绝、不扣料），由主人付料；同伴死亡/解散/失主移除时结束其护甲记录，每 600 s 清理已不存在的同伴键 | `ScArmorWorkbench.cs`、`TacticalArmorTargets.cs`、`ComponentTacticalCompanion.cs`、`TacticalPanel.cs`、`SubsystemScTactical.cs`、`SubsystemTacticalCompanions.cs` |
| 统一有效命中结算 | `SubsystemScArmor.Plan` 在伤害计算时按通道/部位规划吸收，挂到 Attackment；`ComponentHealth.Injured` 真正生效时一次提交并发出 `Settled` 事件。墙体、关闭的友伤、取消的伤害、无敌、未命中都不提交也不发声 | `ScArmor.cs`、`ScSurvivalBalance.cs`、`ScCsgoKnivesModLoader.cs` |
| 头盔 Dink 与火花 | 由实际结算选择声音，不看 CT/T 外观：头部防护有吸收 → CS2 `headshot_armor_e1`（受害者 .75 / 射手 .5 / 旁观 .5）+ 头部命中点青白短火花；无头盔或头盔耗尽且掉血 → 裸头声；躯干吸收 → kevlar。霰弹按一枪合并，同目标 0.06 s 内不重复。火花：12 条拖尾（0.05–0.3 s）+ 2 个子火花（≈0.1 s）+ 1 个辉光，加性、做深度测试、最多同时 8 个、64 m 外剔除；本人被命中时移到眼前、减半、无辉光 | `ScHitSounds.cs`、`ScHelmetSpark.cs`、`tools/import_cs2_armor_feedback.py` |
| 护甲 HUD | 头部与躯干分开显示（100/150 两个预算不相加），CS2 头盔/护甲剪影自下而上按剩余填充，颜色沿用弹药 HUD（>50% 绿、20–50% 黄、≤20% 红），未配置灰色"—"，耗尽红 0。每个本地玩家一份；设置页开关"显示防护装备 HUD（头部 / 躯干）"；布局编辑器可单独选中、拖动、缩放、旋转，位置单独保存；默认左下角并避开触屏按钮 | `ScArmorHud.cs`、`ScUiSettings.cs`、`ScGunSettingsScreen.cs`、`ScGunLayoutScreen.cs` |
| 非 CS 物理攻击 | 只接经确认会走原生 `ProcessAttackment` 的通道：普通近战（`MeleeAttackment`）躯干吸收 25%（近战不判头部）；物理投射物（`ProjectileAttackment`）按命中几何判部位，头部 50%、否则躯干 40%。火焰、摔落、爆炸、直接调用 `ComponentHealth.Injure` 的能力不拦截 | `ScArmor.cs`（`BeforeNative`）、`ScGunHitTest.cs` |
| 投掷预览 | 只改出手前预测弧线：保留球形终点和物理精度，加 1 px 羽化边；没有出手后轨迹 | `ScGrenadeTrajectory.cs` |

**百分比是试验起点，不是结论。** 真实 Mods 攻击入口核对（静态反编译，未执行第三方代码）：
- Ghoul（`sc-saghoulmod.dll` sha256 `8a68b5fc…`）：普通攻击经原版挖掘者和显式 `MeleeAttackment` 走 `AttackBody`，会被本轮近战通道覆盖；灵魂冲击/电击/地震直接调用 `ComponentHealth.Injure`，按设计不拦截。
- 深海迷航、恶灵传说：包为受保护格式（文件头声明禁止修改），未绕过保护，攻击入口**未核实**。
- 与原版衣物防护的数值对照只停留在规划证据（皮甲 .5/40、铁甲 1/100 等），**本轮没有做实机成本/伤害对照**。不声称已适配全部模组。

## 3. 投掷预览 A/B（离线渲染人工审阅）

在同一场景渲染 4 个变体（FollowupCheck `preview-ab.txt` 与 PNG）：
- A 当前（带人为起段偏移）：近处从偏移点弯出一个"钩"，远处是真实路径。
- B 去掉偏移：真实路径在视线方向上几乎侧面对齐，退化成一条竖线。
- C 加锐：边缘更硬，形状同 A。
- D 另一种偏移：与 A 近似。

结论只是**离线观察**：钩形是真实路径的透视加上装饰性偏移共同造成的，去掉偏移会让弧线在正前方投掷时几乎看不见，因此保留当前偏移，只加 1 px 羽化。这不是经实机复现的根因结论；4 px 粗线不恢复。本地 TrajectoryVisualRegression 7/7 通过。

## 4. 离线测试与最终候选

- c14（job `ebb40bcf83ae4aee993dceabc5519e19`）：全部通过，发现并修正 HUD 布局面板与 HUD 重叠后进入 c15。
- **c15（job `0938fb29ce5e4fe0b3319e89d9c2ee51`，request `m-c15-final-01`）**，729 个源码输入逐一核对哈希后构建：

| 门禁 | 结果 |
|---|---|
| 构建（lite/full core、agents、voice、appearance、工具） | 全部 PASS；clip 缓存 `e32b553d5c3477bb` 复用（输入未变，未重烘） |
| AI | full 66/66，lite 66/66 |
| video-feedback | full 292/292，lite 292/292 |
| tactical 全套 | 107 项中仅 4 项既有已知失败（`native-gltf/ct`、`native-gltf/t`、`npc-full-registry-reload-refuses-without-ammo-loss`、`optional-package-identity-and-no-bundled-engine`），无新失败 |
| C4 | full 73/73，lite 73/73 |
| main | full 13914 项 0 失败，lite 14220 项 0 失败，相对基线无新失败 |
| compat | family 246/246（含 1.0.0/1.2.0/1.3.0 防护往返与 `companion-4242` 小数值）、native hooks、integration ×6、inventory 全部 PASS |
| motion / throw / hotspots / ui | 全部 PASS（ui 为 HUD、布局、火花、轨迹 A/B 渲染） |

资源：新增 `helmet_dink.ogg`、`helmet_spark`、`hud_armor`、`hud_helmet`（全量 PNG、轻量 WebP），退役 `headshot_armor.ogg`；来源与哈希见 [armor-feedback-assets.json](current-direction-20260929-armor-feedback-assets.json)。

## 5. 交付与清理

- 按常设授权以同盘移动替换 output（job `aa42924a4ca5442da82639ca5f307587`，request `cd-output-replace-c15-r2`），移动前核对 output 与 manifest、候选与验证哈希，移动后复核：

| 包 | 字节 | SHA-256 |
|---|---|---|
| `[API1.9]CS武器1.4.0-全量包.scmod` | 526,658,569 | `0e621ed2c7ccab9ec178b4f4a3b9a516a77b678cd404b3ff28fdf1dfce4e38d2` |
| `[API1.9]CS武器1.4.0-轻量包.scmod` | 35,541,450 | `31baa9dbd5c51e05a7147441aa7327d83d76fba05cb2ce3544906dd9cc8242a2` |
| `[API1.9]CS武器1.4.0-探员包.scmod` | 38,457,503 | `2a397b49b32b29d01566398a2c43e3ce9ad05a569bd9396441843847bbeb0208` |

  `output/release-1.4.0/manifest.json` 已更新（旧 c13 哈希记入 `replaced.previous`/`history`），`tools/completion_140.py` 基线改为 c15。与 c13 相比，全量实际变化只有 `ScCsgoKnives.dll`、`ScCsgoTactical.dll`、`ScCsgoResources.xml` 与上述 4 个新增资源、1 个退役资源；探员只变 `ScCsgoTactical.dll`。
- 清理（dry job `5349c29f…`，delete job `6aa581bc188e42608616752e17e8171f`）：c14/c15 可重建阶段目录 17 项、3.58 GB（E: 可用 39.77 → 43.36 GB），保留报告、日志、渲染、`packages.json`/`source-hashes.json`、clip 缓存与 MP 阶段目录。回执：[current-direction-20260929-r2-cleanup.json](current-direction-20260929-r2-cleanup.json)。
- 未安装、未改玩家原世界、未发布、未扩 Mini。

## 6. MP M0：隔离可行性原型（双端实测通过）

M0 只证明"同包安全装载 + 能力检测 + 握手 + 一个服务器确认的无副作用请求"，**不代表联机玩法可用**。M0 部件只存在于隔离测试包，正式包未包含任何网络层。

**6.1 平台与输入**
- 源码快照：`D:/下载/SurvivalcraftApi-API_1.9.3.2_MP/...` 复制到 `.tmp/mp-m0-20260929/src`（job `2a0dea49…`，2991 文件 67.9 MB，关键文件哈希与 evidence.json 一致）。
- Windows 构建（job `3cb58c24ffd64ee198bf9c36a218a1d0`，net10.0-windows / win-x64，0 错误）；TestAutomation 与参考 DLL（job `647c41dd6a834aca821a66649af7616b`）：Survivalcraft.dll `622976cc…`、Engine.dll `41ec4fc8…`、EntitySystem.dll `9a6d892e…`、CompatNet.scmod `d64219f2…`、Multiplayer.scmod `bd90abb5…`、TestAutomation.scmod `9d42c5a7…`；57 个托管参考 DLL 的哈希在 `.tmp/mp-m0-20260929/refs/refs.json`。
- 存储：MP 版 `ModsManager.DocPath = app:/doc`，世界/设置/Mods 都在程序目录下（`data:` 只被 1.26→1.27 迁移使用），所以每个角色一份独立程序副本即可隔离，不会碰到玩家世界。

**6.2 源码与二进制事实**（决定了装载方式）
- MP 加载器对包内每个非 Assets 的 `.dll` 立即 `Assembly.Load` + `GetTypes()`，任一类型解析失败会让整包加载中止（`ModEntity.cs:450-544`）→ 适配层不能做根 DLL，只能是包内 `Net/ScCsgoNet.bin` 延迟装载。沿用项目已有做法（`TacticalAppearanceIntegration`：GetFile → Assembly.Load → GetTypes → `ModsManager.Dlls` → `HandleAssembly`）。
- 外部适配器通过 `CompatNetAdapterRegistry.Register` 注册；`CompatNetPacket.Handle` 在服务器上先校验发送者身份再调 `HandleRequest`；服务器用 `packet.From` 只回给请求者。适配器 id 选 `0x5343`（21315），避开内置 1–20。
- MpAbiCheck（仅元数据）对 MP 构建：全量 3 个 DLL 2292 个引擎引用、轻量 1339 个、探针 16 个，**0 个成员无法绑定**；引用的程序集中只标出 `ScCsgoResources`/`ScCsgoResourceCodec`（包内自带，已在运行中确认从本包加载）与可选外观集成依赖的 NekoMeko/Neorxna（第三方，未安装时不启用）。job `0af3965f1b784dcfa41a7819447036ed`，报告 `.tmp/mp-m0-20260929/results/abi-mp-*.json`。

**6.3 原型构成**（`tools/MpM0/`，仅测试）
- `Probe/ScNetProbe.cs`（按 1.9.3.1 API 编译，不写任何 MP 类型名）：无 `Game.NetworkManager` → 单机，不读适配层；有 → 逐项核对适配层依赖的 23 个 CompatNet/网络成员签名与协议版本 2，全部一致才装载 `Net/ScCsgoNet.bin`，否则拒绝网络层并记录原因。
- `Adapter/ScCsgoNetAdapter.cs`（按 MP 构建编译）：握手比对 CS 网络协议/版本、物品布局 6、枪记录 schema 7、核心版本与核心构建 MVID；服务器只在内存记会话，不写世界。只读状态请求返回游戏时间、发送者身份/血量/防护，按请求 id 去重（重复 id 回缓存结果、不重新求值）。客户端 10 s 无应答 → 超时并拒绝 CS 网络功能。
- `Harness131/`：只装入 1.9.3.1 隔离副本的测试模组（静音，加载完成后写报告并退出）。
- `m0.py`：Windows 编排；测试包 = c15 正式包原样 + `ScCsgoNetProbe.dll` + `Net/ScCsgoNet.bin`（轻量 `151b563f…`、全量 `bc1c0036…`），**从未进入 output**。构建 job `a9d8c8b7a4b649e6a51a3e10196deb6d`。

**6.4 实测结果**（Windows 实际进程，loopback；结果与日志在 `.tmp/mp-m0-20260929/results/`）

| 场景 | job | 结果 |
|---|---|---|
| 1.9.3.1 隔离副本 + 轻量测试包 | `1dc2c12f357343339bc25365f25f80bc` | 到达主菜单，0 错误；探针判定单机；`Net/ScCsgoNet.bin` 未加载；进程中没有任何 MP 类型 |
| 1.9.3.1 隔离副本 + 全量测试包 | `d748df8472bb4fb2ab0723a899ce5912` | 同上（加载 Bundle/Tactical/Voice），0 错误 |
| MP 主机 + 客户端，两端轻量测试包 | `8d7804e2c275497f8f13ddea0f639556` | 两端 23 项签名 0 差异并挂上适配层；无客户端时主机为 Host/remote 0（MP 本地世界）；客户端握手 Accepted；状态请求得到服务器应答，请求前后服务器上两名玩家的血量/防护/背包快照完全相同；重复 id 回缓存；声称布局 5 的 hello 被拒 → 客户端本地拒发请求、强行发送也被服务器拒绝；兼容 hello 重新接受后请求正常；两端 0 错误；核心 DLL 未被任何预加载模组改写 |
| MP 服务器只装正式轻量包（无适配层）+ 客户端测试包 | `ad03d011e69e446794e2e639dee67c7e` | 客户端 10 s 超时 → TimedOut，拒绝 CS 网络功能；服务器无我们的网络日志；0 错误 |
| MP 主机 + 客户端，两端全量测试包 | `e2bae60f3d8f46d8aa09d8cbf231e911` | 客户端存在护甲/战术/敌人/同伴子系统；握手 Accepted，无副作用请求与去重同上；0 错误 |

**M0 结论**：同一个包能在 1.9.3.1 单机安全装载、在 MP 引擎按能力装载独立适配层，主机/客户端握手、拒绝和超时路径都有双端实测。这只是装载与通信基础；玩法层面（开火、伤害、背包记录、AI、投掷物）在 MP 下仍完全未适配。

## 7. 仍缺的验收（本轮未补）

- 实机听音（Dink/kevlar/裸头音量是否被枪声盖住）、工作台/HUD 实机界面、正常寻路走到遭遇区、C4/NPC 连续动作：没有 1.9.3.1 实机自动化，也不在玩家 Mods 安装，本轮未补。M0 证明了"隔离程序副本 + 测试模组驱动"在 Windows 上可行，下一步可用同样方式在 1.9.3.1 隔离副本里做实机截图/录音，但本轮尚未做。
- Android：无证据。
