# 联机故障（安卓房主 + Windows 客户端）：当前工作简报（2026-10-02）

Status（历史）：mpb 于 2026-10-02 放入 `output/`，用户实机握手成功并反馈了客户端状态问题；**`output/` 现已由后续候选 mpc3 取代**，当前状态见 [mp-state-consistency-plan-20261002.md](mp-state-consistency-plan-20261002.md) §8。以下为 mpb 的实施记录，不再更新。
原状态行：**候选 mpb 已放入 `output/`（2026-10-02）；只通过了离线门禁，未做任何游戏内运行，真机联机验收待用户**（用户 2026-10-02："你不用启动实机测试，做完放到output里面我自己测试就可以"）。
Current writer: VPS Claude（src、tools、本文件）。此前的写入者 Windows Codex 只写过本文件的诊断版（已并入 §2，原结论被修正处见 §8）。
前一版交付：r12c（`docs/tasks/round12-aim-sign-agents-hint-settings-20261001.md`）。

## 1. 用户要求（2026-10-02 交接原文要点，未经改写）

目标："让联机功能真正可用：客户端刀能造成服务器认可的伤害，枪械和皮肤正确同步，投掷物正常生成，远端玩家探员持枪及动作正确。只消除提示、阻止操作或隐藏异常不算完成。"

执行要求（逐条保留）：
1. 先复核诊断，区分已证实事实与假设，然后直接实现我们模组范围内的修复。
2. 优先确认安卓消息传输的可行方案：现有 CompatNet 能否在目标安卓平台工作，或是否存在可供 CS 使用的自定义消息接口。不能假定直接复制 Windows 的 CompatNet 包就能用。
3. 如果必须修改平台或 NMM／NEO 才能解决，给出具体文件、接口差异和最小补丁建议；本轮不直接修改、重打包第三方组件。继续完成不依赖它们的 CS 修复。
4. 保持服务器权威，不允许用客户端自行扣血、生成实体或修改库存绕过联机问题。保留现有物品 ID、存档和无关改动。
5. 外观问题检查远端 ModelKey、实际模型、骨骼与锚点是否一致；已有骨骼缓冲扩容不能当成根因已经解决。
6. 玩家动作同步必须覆盖房主被客户端观察、客户端被房主及其他客户端观察；NPC 动作同步不代替玩家同步。
7. 更新本报告为当前工作简报，记录最终修改、证据、测试及阻塞。
8. 按项目规范经 Windows worker 执行必要构建和验证；Windows 双进程通过不能冒充 Android 真机通过；缺安卓环境时明确留下具体测试缺口。
9. 不覆盖既有工作，不安装到用户 Mods／设备，不修改原世界。
10. 按现有授权和发布规则交付；未通过必要验证的结果只能标为候选，不能称全部修复。

用户随交接追加："你不用启动实机测试，做完放到output里面我自己测试就可以"。因此本轮没有启动任何游戏进程（含隔离的 1.9.3.1 和 1.9.3.2_MP 测试副本），只做构建和离线检查；交付物放入 `output/`，状态为**候选**。

## 2. 证据身份与复核后的事实

日志与截图身份沿用诊断版（三份日志 SHA-256 前缀 `222de521…`、`80367220…`、`c74e30d0…`，本轮重新读取核对了内容）：

- 安卓房主（`D:/下载/Game(1) (1).log`）：Adreno 830、Android SDK 36、分辨率 3392×2400，**不是**用户那台 Redmi（worker 上 ADB 连接的 M2007J17C）。API 1.9.3.2，加载了 Multiplayer Mod 1.0.0、NekoMeko Model 1.1、Neorxna 1.4、CS 武器**全量版** 1.4.0；没有 CompatNet、没有 Compat23。通过中继（relay 120.79.212.67）开房。
- Windows 客户端（`E:/EdgeDownload/[Windows] SurvivalcraftAPI1.9.3.2`）：轻量包 `05947864…` + 探员包 `5eab74d4…`（r12c）。平台程序集 MVID 为 `ca4dabc9…`（Survivalcraft）/`24f856fb…`（CompatNet）/`448d2b2c…`（Multiplayer），与我此前验证过的预览构建（`d5c1a418`/`52747411`/`b3822a2d`）不同，日志记为 "platform unverified"。
- 第二个客户端（`D:/下载/Game (2).log`）：全量版，无启动段。

本轮新增的直接证据（均为只读）：

1. **用户实际的 Windows 平台二进制已取回并反编译**（`Survivalcraft.dll` `7856ec63…`、`Survivalcraft.Multiplayer.dll` `aa6136c5…`、`Survivalcraft.CompatNet.dll` `b0330dd8…`、`Engine.dll` `5e95a919…`，以及 NMM `1ba873e7…`、NEO `e39bb016…`）。下文凡说"平台代码"均指这份实际构建，不是源码快照。
2. **平台自带公开的自定义包接口**：`Game.Network.PacketManager.RegisterPacket<T>()`（Multiplayer 内置模组，256 个槽位，平台自用 0–98），发送走 `NetworkManager.Queue(packet)`，接收方由平台把连接写入 `Packet.From`。CompatNet 自己就是这样注册 98 号包的。
3. **安卓构建附带的 Multiplayer 与 Windows 是同一个 scmod**：源码 `build/NetworkMod.targets` 对安卓和 Windows 都复制同一个 `Survivalcraft.Multiplayer.scmod`（以 `net10.0-windows` 构建的 IL）；安卓工程没有打包 CompatNet 的步骤。**未证实**：用户那台安卓设备上 APK 的逐字节身份（Windows 上找不到 1.9.3.2 的 APK，设备不在手边）。
4. 房主日志 "不存在的包ID:98" 两次：客户端的 CompatNet 握手包到达了一个没有注册 98 号包的包表，与第 2、3 条一致。
5. 平台每个包单独成帧（`NetworkServer.Send`/`RelayHost.Send`），未知包只丢它自己，不连带丢别的包；中继路径同样尊重 `Packet.To`。

复核结论（事实）：

| 现象 | 原因（已证实的部分） |
|---|---|
| 握手 10 秒超时 | 旧适配器实现了 CompatNet 的接口并经 98 号包收发。安卓房主没有 CompatNet，核心按契约拒载整个联机层；客户端的握手无人应答。 |
| 客户端刀无伤害 | 伤害只在服务器执行；握手未通过时请求不发送。本地照常播放挥刀，所以看起来"砍中了没伤害"。 |
| 投掷物扔出后消失 | 同上：实体只由服务器生成；本地照常播放整个投掷。 |
| 手里的枪变成贴图 | 物品值里有型号（低 6 位）和记录号；弹药、耐久、涂装在记录里。客户端的记录表是服务器的镜像：入场时随世界数据来一次，之后只靠我们的联机层增量。联机层没起来时，入场后才产生记录的枪在客户端"无记录"，`ScGunBlock.DrawBlock` 对无记录的枪在所有绘制模式下画物品图标。 |
| 远端探员持物异常 | 两个独立原因：上一条；以及 `ComponentNeoModel.ItemAnchorPositions` 越界（见 §4.3）。 |
| 远端玩家无武器动作 | 第三人称姿势读的是本进程里该玩家的动作状态；其他玩家的检视、刀击、换弹、投掷、下包阶段此前没有任何同步。握手恢复也不会自己好。 |
| 房主 `SubsystemTacticalBombs` 更新失败 | 引擎的 Ogg 读取对"立体声且总帧数为奇数"的文件抛异常（见 §4.5）；我们用 `CreateSound` 直接播放 `c4_disarmstart`，异常冒到了炸弹更新里。 |

## 3. 传输方案的结论（要求 2）

- CompatNet 能否在安卓上工作：**不依赖这个问题**。平台的安卓构建根本不带它；即使手工复制，也没有证据它能在安卓加载（它带 Mono.Cecil 并在内存里改写别的模组，未验证）。
- 存在可供 CS 使用的自定义消息接口：就是 Multiplayer 自己的包表（§2 第 2 条）。CS 现在注册**自己的包**（id 197，高于平台已用范围），不再经过 CompatNet。安卓与 Windows 走同一条路径。
- 兼容性：新旧 CS 版本互不通信（旧版在 98 号包上握手，新版在 197 号包上），会按"服务器没有回应"提示；双方必须同一版。平台以后若占用 197，注册会失败并明确提示，不会覆盖。

## 4. 已实施的修改

### 4.1 联机层（核心 + 适配器）

- `src/ScCsgoNet`（`Net/ScCsgoNet.bin`）：改为自有包 `ScCsgoPacket`（请求：客户端→服务器；广播：服务器→指定客户端）。发送者身份取平台交付的连接（`Packet.From.PlayerIndex`），不信任客户端自报。只引用引擎和 Multiplayer，**不引用 CompatNet**（流水线用不含 CompatNet 的引用目录编译它，以保证这一点）。
- `src/ScCsgoNetCompat`（新，`Net/ScCsgoNetCompat.bin`）：原来的"命令适配器冻结闸门"单独成包，只在平台有 CompatNet 且契约匹配时由核心加载；没有 CompatNet 不算错误。
- `ScNet`（核心）：契约检查拆成必需的传输契约（引擎 + Multiplayer）和可选的 CompatNet 契约；拒载时记录给玩家看的原因。
- 握手：未应答时每 3 秒重发，12 秒后提示并停用 CS 武器，之后每 15 秒慢速重试 8 次；迟到的应答仍被接受。提示文案区分三种情况：本机组件不可用（含原因）、服务器没有回应（不再猜"版本不同"）、服务器明确拒绝（附原因）。
- **房主提示**：房主的联机层被拒载时，第一个其他玩家进入后提示一次"本机（房主）的 CS 武器联机组件未加载（原因），其他玩家的 CS 武器无法使用"。此前房主毫不知情。
- 协议版本 3 → 4。

### 4.2 玩法侧（服务器权威不变）

- 刀、投掷物、C4：客户端联机层未被接受时不再播放本地假动作，改为提示原因；请求发不出去时撤销本地动作。**没有**任何客户端自行结算。
- 枪：多人客户端上"型号可靠但记录未到"的枪（`ScGunBlock.AwaitsRecord`）在手上、第三人称、掉落物里画**默认涂装的模型**，物品栏仍是图标，名称显示"等待服务器同步"；在记录到达前不可用，扣扳机时提示原因。单机和服务器上无记录的枪行为不变（仍不猜测、仍显示异常）。记录号 ≥1024 的枪型号只在记录里，仍无法显示。
  - 这是一处需要你确认的取舍：项目规则是"不猜测缺失记录"。这里没有读写或推断任何记录，只是把"客户端镜像还没到"与"存档损坏"区分开来显示。如不接受，可只保留握手修复，回退这一条。
- 客户端手持未同步的枪时，不再弹"旧版本数据或损坏存档"的红字。

### 4.3 远端玩家的武器动作（要求 6）

新增 `ScNetPresentation`（核心，轻量包也有）：
- 谁读取某个玩家的输入设备，谁就是该玩家动作表现的来源。它在动作变化时发送动作（型号、种类、片段、时长、已过时间），投掷和下包阶段进行中每秒 20 次发送阶段值，结束时再发一次。
- 客户端 → 服务器 → 其他已握手的客户端（不回发给来源）；房主自己的玩家直接广播给所有客户端。三种观察关系都覆盖：房主被客户端看、客户端被房主看、客户端被其他客户端看。
- 观察方按玩家保存最后状态，用自己的时钟推进；第三人称姿势（原版/NMM 模型的 `ScThirdPerson`，CS 探员的 `CsPlayerPose`/`CsPlayerItems`）统一从这里读。本进程自己模拟的状态优先（服务器为远端客户端运行的开火、换弹、投掷、下包），复制来的只补它没有的（检视、刀击等）。
- 纯表现：不触发伤害、开火、生成或消耗。读取时全部限幅；服务器先解析再转发，每个客户端每秒最多 60 条。
- NPC 的 `TacticalNet` 路径未动，也不替代本路径。

### 4.4 外观集成（要求 5；`src/ScCsgoAppearance`）

- **持物骨骼越界（已定位）**：NEO 只在自己的 `Update` 里填 `ComponentNeoModel.AbsoluteBoneTransforms`，NMM 画手持物时用当前模型的骨骼索引去读。平台的 CompatNet 在客户端上跳过所有 `neorxna.dll` 组件对"非本地玩家"的更新（`ScopedUpdateGuardCompatNetAdapter` 13 号，范围 AllEntitiesExceptLocalPlayer），所以远端玩家的这个数组永远是空的；另外任何刚由网络包创建的实体也会在首次更新前被画一帧。原有的 `CsNeoBoneBuffer` 保护的是另一个缓冲（`ComponentRigidBody`），与此无关。
  - 修复（我们组件内）：非 CS 模型走 NMM 绘制前，若 NEO 的数组不可用，就用当前姿势按 NEO 自己的两步填好；NEO 自己在更新时不碰它。未改 NMM/NEO 任何代码。
  - "CS 模型但 IsCs 为假"这种不一致**不可达**（两者都来自同一个 `ResModel`）。
- **远端 ModelKey（已定位）**：NMM 的模型选择只存在实体存档数据里，平台只在入场时把服务器实例的数据发给加入者，之后任何变化都不发。结果：客户端选的探员从未到达服务器（别人和重进后的自己都看到默认模型）；房主入场后再换，别人也看不到。CompatNet 的 NEO 适配不同步模型键。
  - 修复：新增 `ScNetAppearance`，只同步涉及我们两个模型（`zh667.cs.ct`/`zh667.cs.t`）的选择：拥有者 → 服务器（对自己的实例调用 NMM 同一个选择接口，因而存档和后来加入者都带上）→ 其他客户端。离开 CS 模型时带上换成的模型，观察方不会一直留着探员。其他 NMM 模型之间的切换不归我们同步。
- 远端模型、骨骼、锚点的一致性结论：CS 探员路径不使用 NEO 的锚点缓冲，持枪由我们按 `cs_weapon_mount` 骨骼绘制；只要 ModelKey 一致（上一条）且枪记录或型号可用（§4.2），持枪与动作（§4.3）即一致。

### 4.5 其他

- `SubsystemTacticalBombs.Play`：声音创建失败只损失声音，不再中断炸弹更新。
- 发现但**未改**（资源变更需你决定）：全量包里有 26 个立体声音效，其中 14 个总帧数为奇数。引擎 `Ogg.Load` 按 `TotalSamples*2` 分配字节，立体声会只读到一半，奇数帧直接抛异常（引擎的 `AudioManager.PlaySound` 吞掉异常，表现为不响）。用用户平台的 `Engine.dll` + `NVorbis.dll` 离线复现：`c4_disarmstart`、`c4_explode1` 均抛 `ArgumentOutOfRangeException(bytesCount)`。受影响：`c4_explode1`、`c4_disarmstart`、`c4_key_press1/2/4/6`、`c4_plant_quiet`、`c4_shockwave_01`、`c4_shockwave_debris_01/02/04`、`c4_shockwave_hit_01`、`c4_thump_01`、`bf1_kill_confirm`。1.9.3.1 的引擎是同一段代码。轻量包这些都是单声道，不受影响。建议把全量包这 26 个转为单声道（保留 44.1 kHz）。

## 5. 需要平台或第三方处理的问题（未改动它们）

1. **平台安卓构建缺 CompatNet**（`Survivalcraft.Android.csproj` 只导入 `build/NetworkMod.targets`；Windows 工程第 55–78 行另外打包 CompatNet）。影响：Windows 房主的 CompatNet 会向没有它的安卓客户端广播 98 号包（安卓端刷"不存在的包ID:98"）；依赖 CompatNet 适配的其他模组在安卓上没有联机支持。建议：安卓也附带 CompatNet，或 CompatNet 只向声明支持的会话广播。CS 已不依赖它。
2. **NMM 1.1 的 `PlayerAdded` ABI**：1.9.3.1 是属性 `Action<PlayerData> PlayerAdded { get; set; }`，1.9.3.2 改成了 `event`，没有 `get_/set_PlayerAdded`。NMM 的 `NekoMekoModLoader.OnProjectLoaded` 整个方法因此 JIT 失败。**实际影响很小**：该方法只订阅一个填默认模型键的处理器，NMM 初始化时本来就有等价的回退；我们的代码不依赖它。对 NMM/NEO 全部引擎引用做了 ABI 核对（1609 处），只有这两个成员缺失。
   - NMM 侧最小补丁：`subsystemPlayers.PlayerAdded += handler;`（或删掉订阅，或反射兼容两种形态）。
   - 平台侧最小补丁：在 `SubsystemPlayers` 上保留事件并补两个普通方法 `get_PlayerAdded()` / `set_PlayerAdded(Action<PlayerData>)`（`PlayerRemoved` 同理）。Compat23 和 CompatNet 里目前都没有针对它的改写。
3. **NEO 着色器在 Windows 1.9.3.2 上编译失败**：该平台 Windows 一律用 ANGLE，上下文是 OpenGL ES 3.0（`GLWrapper` 请求客户端版本 3）；1.9.3.1 请求的是原生 GLES 3.2。`NeorxnaRenderer` 静态构造里第一个 NEO 自己的着色器（PBR）报 "unsupported shader version"，之后 NEO 的渲染器整个会话不可用（NMM 的第一人称手、第三人称 PBR/Unlit 渲染、预览控件回退到原版绘制）。我们的 CS 路径不经过 `NeorxnaRenderer`。**未证实**：NEO 着色器的具体 `#version`（资源加密，未解）。
   - 平台侧：Windows 提供 ES 3.2 能力的上下文；NEO 侧：着色器声明 `#version 300 es` 或逐个惰性编译并回退。
4. **平台不同步 NMM 模型键**（§4.4）：我们只补了自己两个模型；通用修复属于平台（组件数据变化时重发实体）或 NMM。
5. **引擎 Ogg 立体声读取**（§4.5）：属引擎缺陷；我们可通过资源规避。

## 6. 验证（本轮实际执行）

全部为离线执行；没有任何游戏进程。

| 检查 | 内容 | 结果 |
|---|---|---|
| 本地编译（VPS） | 适配器在**不含 CompatNet** 的引用下，对用户实际 Windows 平台 DLL 编译；CompatNet 部分、核心、探员、外观（对真实 NMM/NEO DLL）编译 | 通过 |
| `tools/NetLoopCheck`（新） | 用真实平台程序集做回环：核心按契约从字节加载适配器 → 注册包 → 经平台自己的序列化与包表往返 → 握手、重试、超时、迟到应答、拒绝、双向消息、发送者鉴别、畸形包、离开、向另一客户端转发动作。分两次：进程里**没有** CompatNet（安卓构建的情形）；有 CompatNet（闸门安装） | VPS（用户实际平台 DLL）28/28 ×2；Windows 流水线（固定引用）见下 |
| `tools/AppearanceNetCheck`（新） | 真实 NMM 1.1 / NEO 1.4 程序集上：NEO 持物骨骼缓冲在空、过短、NEO 自己填过三种状态下的处理；CS 模型选择的发送、转发、屏蔽规则 | VPS 19/19；Windows 流水线见下 |
| PackageCheck `net-client/*`（新，35 项） | 客户端枪记录未到的显示与单机/服务器不变；被拒客户端不发消息；动作/投掷/下包的编解码、限幅、拥有者发送、房主广播、转发与限流、观察方合并 | 见流水线 |
| 发布流水线 mpa（首轮） | job `729b66e5df6b48e2a973a03e1ed33e99`：`failed steps: []`；netloop 28/28 ×2；main-full 0/14065、main-lite 0/14375（新增项只在旧基线上失败）；ai 0/66 ×2；vf 0/292；c4 0/73；throw 0 失败 | 通过（未交付，随后有复查修正） |
| 发布流水线 mpb（最终候选） | job `cb4cdb6f48b74ba587bd918ab864cda4`：`failed steps: []`；netloop 28/28 ×2；appnet 19/19（全量、轻量各一次）；main-full 0/14069、main-lite 0/14379（含 net-client 35 项；旧基线只在这组的入口项上失败）；ai 0/66 ×2；vf 0/292；c4 0/73；tactical 仅 4 项已知失败；throw 0 失败；玩法身份 `005f0035…` 已写入各核心和探员 DLL | 通过 |

未执行（缺口，需真机）：
- 任何真实会话。尤其：**安卓设备上的加载与握手**（`Assembly.Load` 适配器、包注册、JSON 握手在该设备运行时上的行为只由"同一份 Multiplayer IL + 契约检查"推断）。
- Windows 双进程联机矩阵（M1–M4）本轮未重跑：传输层换了实现，此前的矩阵证据只对玩法消息处理器仍然有效，对新的收发路径无效。
- 外观：NMM 自己的 `SetResModel` 切换到另一模型（加载模型资源）未在离线检查中执行。
- 着色器、截图类观感。

## 7. 交付

按用户指示移入 `output/`（`mp-deliver-mpb-01` = job `cec556bf9a0f42e2b3dd10e935aad9ce`，剪切，单份），状态为**候选**：

| 包 | SHA-256 | 字节 |
|---|---|---|
| 全量 | `dc7149930fbc5d991432c186c14569338e671e0f63d6f816b3f2764c6f59ed5b` | 526,880,978 |
| 轻量 | `1dd379bd2aa56e68c30295d79108f9938b1b0c08c5d17efbcb485338bb8be1ed` | 35,724,024 |
| 探员 | `d0a49c56cd2223452ff68c257305b510a0dc2daaae79ac14fd125eeb7863d347` | 38,467,897 |

相对 r12c 的成员变化（无删除）：
- 全量：改 `Integrations/ScCsgoAppearance.bin`、`Net/ScCsgoNet.bin`、`ScCsgoKnives.dll`、`ScCsgoTactical.dll`；新增 `Net/ScCsgoNetCompat.bin`。
- 轻量：改 `Net/ScCsgoNet.bin`、`ScCsgoKnives.dll`；新增 `Net/ScCsgoNetCompat.bin`。
- 探员：改 `Integrations/ScCsgoAppearance.bin`、`ScCsgoTactical.dll`。

适配器 `c90d84e4…`、CompatNet 部分 `ca40acbc…`（两个版本包内相同）。清单已更新（`verification.status` 写明是候选），`tools/completion_140.py` 的 BASELINES = mpb。

清理（`mp-cleanup-mpb-01` = job `5246abb2…`）：mpb、mpa 的构建树与未交付候选，以及只读分析用的平台 DLL 导出（`.tmp/dev-temp/mpx`），共 4.90 GB；output 哈希不变，E: 剩余 13.0%。回执 `mp-user-logs-20261002-cleanup.json`。

**联机时所有人必须使用这一版**：网络协议 3 → 4，与此前的 1.4.0 各版本互不握手（会提示"服务器没有回应"）。

### 用户验收时请记录

1. 两端 Game.log 里的 `[ScCsgoNet] decision:` 行（应为 `transport contract matched, adapter attached; CompatNet absent`（安卓）或 `…; CompatNet command gate: installed`（Windows））和 `packet 197 registered` 行。
2. 房主日志的 `[ScCsgoNet] server: hello … accepted`，客户端的 `client: hello … accepted by server core`。
3. 若仍失败：房主是否看到红字"本机（房主）的 CS 武器联机组件未加载（原因）"，客户端提示的完整文字。
4. 验收矩阵（原诊断版保留）：安卓房主 + Windows 客户端、Windows 房主 + 安卓客户端、第二个观察客户端、晚加入；刀轻/重击伤害只结算一次；已有记录的枪、入场后新拿的枪、皮肤枪；T/CT 外观在各端一致，持枪、俯仰、开火/换弹/检视/刀击可见；投掷物强/弱投、取消、库存只扣一次。

## 8. 对诊断版的修正（历史）

- "补齐 Android 平台通道或实现独立适配"：已选后者，并确认平台本身有公开接口，无需平台改动。
- "CS 专用持物路径未识别导致越界"：不成立。越界发生在**非 CS 模型**走 NMM 绘制时，根因是 NEO 的缓冲在远端玩家上从不更新（CompatNet 的更新屏蔽）或首帧未更新。
- "NMM PlayerAdded 属 ABI 不兼容，需平台或 NMM 处理"：成立，但影响被高估，功能上有等价回退。
- "换视角能否恢复 NEO 着色器"：不能，静态构造失败后整个会话都不可用；与我们无关。
- 诊断版把图标枪"高度吻合"记为假设：现已在代码路径上证实，并说明了为什么只有入场后产生记录的枪受影响。
