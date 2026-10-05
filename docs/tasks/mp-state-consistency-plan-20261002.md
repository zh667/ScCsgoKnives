# CS 联机状态一致性与玲兰兼容：VPS 执行规划

日期：2026-10-02。**当前状态：候选 mpd2（协议 6）已交付 output，交付时离线门禁通过；现已收到用户实测“大部分没问题”，但 14:42 视频证实短连射停火后偶发回升一发。该现象已用实装 DLL 在 100/50 FPS 与有序抖动下复现，属于尚未修正的射击预测/权威节奏差异，不能统称为正常网络延迟。最新诊断及建议见 §13，未修改产品。** mpd2 的已交付变更与原验证记录见 §12；远古世界本身仍未接入（blocked-provider），目标联机玲兰包仍为 blocked-input。用户的整体反馈不等于每一项单独完成实机验收。
规划编写者：Windows Codex；最新 14:42 反馈诊断已登记。执行者：VPS（2026-10-02，两轮，均按用户的执行授权），执行完毕后停止写入；最新诊断前已核实其终端在提示符、双端同步 idle 且无待同步或错误。本段不是锁，下一位写入者开始前仍需核实双端同步状态。

## 1. 唯一入口与阅读顺序

这是 **mpb 用户反馈之后** 的当前实施规划入口。不要从旧的“安卓缺 CompatNet，CS 无法握手”结论重新开工。mpb 自有包 197 / 协议 4 已在本次 Android 房主＋Windows 客户端成功握手。

1. `AGENTS.md`、`docs/agent-guide/collaboration.md`；涉及数据读 `compatibility.md`，涉及构建读 `build-and-release.md`，涉及显示/声音读 `rendering-and-resources.md`。
2. [proposal](../../openspec/changes/weapon-state-consistency/proposal.md)：范围与玩家可见结果。
3. [design](../../openspec/changes/weapon-state-consistency/design.md)：逐步实现建议、源文件入口、失败处理、协议选择。
4. [specs](../../openspec/changes/weapon-state-consistency/specs/weapon-state/spec.md)、[音频规范](../../openspec/changes/weapon-state-consistency/specs/world-audio/spec.md)、[第三方库存规范](../../openspec/changes/weapon-state-consistency/specs/inventory-compatibility/spec.md)：可验收需求。
5. [tasks](../../openspec/changes/weapon-state-consistency/tasks.md) 与 [verification](../../openspec/changes/weapon-state-consistency/verification.md)：执行清单和证据登记。

[审计报告](mp-client-and-sushi-audit-20261002.md) 是根因与原始证据索引；[mpb 简报](mp-user-logs-20261002.md) 保留该候选的实施历史。只按需要查证，不要求重复阅读全部历史任务链。

当前状态只在本入口更新，实施复选框只在 OpenSpec tasks 更新，验收结果只在 verification 更新；审计报告中的历史观察不因修复被删除。必要时更新设计决定并说明依据，不维护另一份相反结论。

## 2. 授权与工作边界

本轮直接用户请求是“详细写入规划文件，并参考 OpenSpec”。因此本轮只写规划，不实施产品修改。VPS 若获得“按此规划修复”等执行指令，即在该授权内继续，不重复询问已授权的例行源码修改、离线检查和候选 output 交付。

既有安排：用户要求不启动实机，由用户测试候选。后续如无新的指令，VPS 做源码、构建及离线验证，保留 Windows/Android 实机项为待验收；这不把必需的行为验证取消，也不准把未跑的矩阵称通过。

允许规划的实现路径：`src/ScCsgoKnives`、`src/ScCsgoNet`、必要的 `src/ScCsgoTactical` / 现有兼容集成、相关 `tools/*Check` 及证据文档。包构建沿用现有 Full/Lite/Agents 归属。
不由本规划授予：安装到用户 Mods/手机、改原世界、复制完整玩家世界作自动备份、改/重打包玲兰或平台、公开发布、联系维护者、向另一聊天发消息。第三方缺陷可以整理可审核的补丁建议和最小复现，先完成不依赖第三方的 CS 工作。

## 3. 当前事实，避免重复修错方向

| 项目 | 当前结论 | 下步要证明什么 |
|---|---|---|
| mpb 通道 | 两端 02:04:36 accepted；两端安装轻量/探员哈希相同 | 在新候选保留自有通道，不回退要求 Android 安装 CompatNet |
| AK 30 发不变 | 新模板 ID 没有记录，预测不会扣镜像；槽位新 ID 漏同步可触发 | 所有槽位、第一次开火后客户端绑定服务器新 ID；服务器真实扣弹 |
| 最后取出的皮肤枪 | 服务器模板实例化后槽位没同步，下一次拖动刷新前一把 | 取第 1 把即正常，不依赖再取第 2 把/拖动物品 |
| Glock 5 发/99% | 旧 ID7=5/1185，新 ID12=20/1200；日志明确 7→12，视频换弹前已 99% | 客户端槽 5 改绑 12，预测不再回写 7；区分历史磨损与换弹 |
| C4 声音 | shownBy 非本地时房主跳过播放；广播又不回到房主 | 房主听到客户端开始/按键/放置，各端每次一次 |
| 个人箱重复编号 | 已有底层库存去重，真实 DLL 6 代理测试有效 | 保留保护，补多人操作者归属验证 |
| 玲兰堆叠 | 条件开启时上限 1→4，实际 DLL 已复现 | 不让已实例化 CS 枪堆叠；既有堆叠不丢数量/状态 |
| 同步箱删除中间频道 | 实际 RemoveChannel→Save 可抛 key 缺失；未找到当前 UI 调用 | 第三方接口复现/建议，不能把它写成用户当前已丢档 |

完整视频时间戳、代码链、哈希和不确定性见审计报告。格洛克旧 ID 是强因果解释，不虚构视频时刻客户端槽值 trace。

## 4. 优先级和完成定义

P0：统一改号后的槽位与记录发布；预测按会话/枪身份隔离。P1：输入身份和超时、C4 房主声音。P1：玲兰堆叠与 CS 库存适配契约。第三方并行证据项：个人箱玩家归属、频道保存、机器同步桥。P2：命中姿态 fallback 与地形修补耗尽，先定位，不在没有证据时扩大改动。

阶段交付 A：修好当前不装玲兰时的四个客户端故障，保留现有其他玩法。
阶段交付 B：完成 CS 可负责的第三方契约、已有两份玲兰包的离线验收及平台依赖结论。
阶段交付 C：拿到实际目标“联机版玲兰”二进制后补目标验收；缺包只阻塞这部分，不阻塞 A/B。

“代码完成”“离线检查通过”“候选已生成”“用户实机通过”是四个不同状态。A 通过不等于 B/C 通过；临时禁止无效动作、隐藏图标或全局禁用玲兰功能不算完成用户功能要求。

## 5. VPS 启动步骤

工作目录 `/home/dev/source-sync/ScCsgoKnives`。检查现有 writer 和 Syncthing 双端 idle/错误/待同步/冲突。源树无 Git 是正常部署形态，禁止初始化 Git、pull/reset 或覆盖现有同步源。

先执行 `win-worker health`，通过现有 worker 查看 Windows 项目、实际候选 manifest 和两份证据 JSON，核验源码没有在其他会话被更新。Windows 路径是 `E:/projects/ScCsgoKnives`；资源仍在 Windows。不能因 VPS 没原始包就用占位 DLL 或旧资源构建。

先读已有探针：`.tmp/dev-temp/mp-feedback-20261002/Probe/Program.cs`，以及 `offline-probe.json`、`sushi-offline-probe.json`（Windows-only，worker 读取）。它们是窄诊断夹具，不是完整游戏联机测试。把必要场景迁入正式 Check runner；不得将“旧错误可复现”的 exit 0 当成新功能已通过。

无需重做 mpb 传输栈或 NMM 修复。先在真实 mpb 上建立失败基线，再按 tasks 小步落地。选择一个写入者；本规划不要求增加代理或开新聊天。

## 6. OpenSpec 的使用

采用 [Fission-AI/OpenSpec](https://github.com/Fission-AI/OpenSpec) 的 `spec-driven` 目录和格式，CLI 固定 `@fission-ai/openspec@1.14.0`（本次 npm 核实版本）。参考：[README](https://github.com/Fission-AI/OpenSpec/blob/main/README.md)、[CLI](https://github.com/Fission-AI/OpenSpec/blob/main/docs/cli.md)、[schema](https://github.com/Fission-AI/OpenSpec/blob/main/schemas/spec-driven/schema.yaml)。工具更新会改变行为，后续不要默默换 latest。

在项目根目录、Node >=20.19.0 的环境运行：

```sh
OPENSPEC_TELEMETRY=0 npx --yes @fission-ai/openspec@1.14.0 validate weapon-state-consistency --strict --no-interactive
OPENSPEC_TELEMETRY=0 npx --yes @fission-ai/openspec@1.14.0 status --change weapon-state-consistency
```

Windows 用进程环境变量并经 `tools/dev.ps1 npx ...`。不需要全局安装，不需要执行 init 覆盖 AGENTS/CLAUDE，也不依赖 slash commands 被安装。VPS 没 CLI 时仍可以按 Markdown 执行任务，Windows 可完成格式校验。

OpenSpec 的 artifact `done` 只表示规划文件存在；不是产品已实现。未完成行为验收时不要 archive，更不能把 checklist 全打勾掩盖未完成。当前 specs 都是拟新增的正式行为规范；项目之前没有 OpenSpec 基线，因此放在 ADDED 中，不表示所有底层功能全是新实现。

现有 `.stignore.shared` 已允许 md/yaml/json，且忽略 `.tmp`；本次不改同步策略。不能仅因符合扩展名规则就声称已同步到 VPS，交接时仍要核实。

## 7. 输出登记

规划轮（Windows）：本入口、OpenSpec proposal/design/specs/tasks/verification、审计报告入口链接及少量可同步的探针结果；产品代码未改。

执行轮（VPS，2026-10-02）：见 §8。OpenSpec 校验只验证规范结构。

### 可直接发给 VPS 的短提示

```text
在 /home/dev/source-sync/ScCsgoKnives 接续 mpb 的客户端反馈。
先读 AGENTS.md 和 docs/tasks/mp-state-consistency-plan-20261002.md，按其中链接的 OpenSpec design/specs/tasks 执行我授权的修复。
先修创造槽位改号与枪表同步、预测身份、输入身份/过期、C4 房主声音，再做我们侧的玲兰库存与堆叠兼容。保留已通的包 197 方案及已有别名去重。
核实双端同步和 writer；通过 Windows worker 做真实程序集离线复现及构建，不启动游戏，候选按现有授权放 output，由我实机测试。目标联机玲兰包缺失只阻塞对应验收。
外部平台/玲兰缺陷提供复现和最小补丁建议，不直接改第三方包；不要重置 ID、修改原世界或安装包到我的游戏目录。完成项、测试证据和剩余缺口分别登记，不把规划校验或离线通过写成实机通过。
```

## 8. 执行结果（2026-10-02，VPS）

四个状态分开：

| 阶段 | 代码完成 | 离线检查通过 | 候选已生成/交付 | 用户实机通过 |
|---|---|---|---|---|
| A：创造槽位改号与枪表同步、预测身份、输入身份/过期、C4 房主声音 | 是 | 是（verification R4/R5：状态用例 83/83、目标断言 9/9、C4 声音 10 项；旧包 mpb 同一组目标断言失败 7/9） | 是（mpc3） | **未测** |
| B：CS 侧的玲兰库存与堆叠兼容 | 我方可控部分是；玲兰自有容器内仍可叠 4 把相同的枪（提供方可控，未解决） | 是（用户两份单机玲兰包的真实程序集：inventory 18、sync 26、stacking 13）；机器吸物、死亡保物、同伴转交**未执行** | 是（同一候选） | **未测** |
| C：目标联机玲兰包 | — | — | — | **blocked-input**（包未取得） |

### 8.1 output 中的候选（mpc3，协议 5）

| 包 | SHA-256 | 字节 | 相对 mpb 变化的成员 |
|---|---|---|---|
| `[API1.9]CS武器1.4.0-全量包.scmod` | `e15a6052190a2e420f06876a60d46f3b9611cb7522ee8012a6cab3604edbca37` | 526,889,657 | `ScCsgoKnives.dll`、`ScCsgoTactical.dll`、`Net/ScCsgoNet.bin` |
| `[API1.9]CS武器1.4.0-轻量包.scmod` | `41674d3ab7e92ed951c119a6b80e6ff093c0565ff196711494f143d50b83eeb9` | 35,732,086 | `ScCsgoKnives.dll`、`Net/ScCsgoNet.bin` |
| `[API1.9]CS武器1.4.0-探员包.scmod` | `9c09b521418fb34b883d0b7154674c412a31beb8711fbf6af996e9a724d86709` | 38,467,933 | `ScCsgoTactical.dll` |

manifest（`output/release-1.4.0/manifest.json`）标注为 CANDIDATE，上一版（mpb）在其 history 中。没有安装到任何 Mods 或设备；没有改动任何世界；没有修改任何第三方包。存档布局、枪表 schema、物品编号规则未变。

**两台设备必须都换成这一版**：CS 网络协议由 4 升为 5，与 mpb 混用时客户端会被拒绝并提示“CS 网络协议 …/4 与服务器 …/5 不同”，CS 武器停用。

### 8.2 改了什么（玩家可见的结果）

1. 创造模式拿的新枪：服务器给它分配记录后，持有者和旁观者的那一格立刻改成同一把枪，弹药数字正确；不再需要靠拖动别的东西来“刷新”。皮肤枪拿在手里即可用，不依赖再拿下一把。
2. 被复制的枪分开后（日志里的 Glock 7→12），客户端那一格显示并使用新记录；预测不再把旧记录的数值写回去。
3. 客户端的预测只影响自己看到的数字，不写入枪表；换世界、重连、换枪、角色重生后旧预测作废。
4. 开火/换弹输入带着“为哪一格哪一把枪按的”；服务器对不上就不执行，并在持续不一致时纠正（重发该库存，或采纳客户端所选的槽）。按住的扳机在客户端沉默 1 秒后松开；C4 下包键和手雷按住同理。
5. 客户端下包时房主能听到启动音、按键音、放置音（各一次）；其他客户端照旧，下包者自己不重复。
6. 装了玲兰并开启它的两个堆叠选项时：玩家背包、原版箱子里 CS 枪仍是一格一把；玲兰自己的箱子/频道/机器里仍可把**完全相同**的枪叠到 4 把，这样的一叠会原样保留、不能直接使用并提示分开，分开后各自正常（复制品首次使用时获得自己的记录）。
7. 日志：装了玲兰时每个世界一行 `[CS_SUSHI]` 能力说明；命中姿态退化与地形修补放弃的警告带上了定位信息。

### 8.3 未做 / 已知缺口

- 没有任何实机结果。离线用例不证明真实链路时序、Android 运行时、画面和声音。
- 玲兰自有容器的堆叠、个人箱的多人归属、同步箱频道保存缺陷：属于提供方，已整理复现与补丁建议（[提供方说明](mp-state-consistency-provider-notes-20261002.md)），未改第三方包。
- 机器吸物、死亡保物、同伴库存转交：未执行（任务 5.6）。
- X01（命中姿态 body-only fallback）、X02（地形修补耗尽）：未定位，只加了诊断信息，需要下一份房主日志。
- mpb 遗留的两项待用户决定未变：等待记录的枪按模型显示（`IsShown/AwaitsRecord`）是否保留；全量包 26 个立体声音效是否转单声道。

### 8.4 证据与文件

- 验证矩阵与执行记录：[verification](../../openspec/changes/weapon-state-consistency/verification.md)；任务勾选：[tasks](../../openspec/changes/weapon-state-consistency/tasks.md)；实施决定：[design §10](../../openspec/changes/weapon-state-consistency/design.md)。
- 紧凑结果：[evidence/mpc3-offline-results.json](mp-state-consistency-evidence-20261002/mpc3-offline-results.json)。
- 管线：request `mpc-pipeline-mpc3-all-01`，job `e782e810e54940e189aa903003b8fa26`，阶段 `.tmp/completion-140-20260929/mpc3`（日志与 JSON 保留）。交付 job `f45599588f87416aabf671890dab013f`；清理 job `bed8981148cd404b861537692e43df1f`，回执 `docs/tasks/mp-state-consistency-20261002-cleanup.json`。
- 新增/主要改动的源码：`src/ScCsgoKnives/Net/ScNetSlots.cs`（新）、`ScNetGuns.cs`、`ScNetMirror.cs`、`ScNet.cs`、`ScNetC4.cs`、`ScNetGrenades.cs`；`World/ScGunMutation.cs`、`ScGunRecovery.cs`、`ScInventoryTransaction.cs`、`ScReloadTransaction.cs`、`SubsystemScGunBlockBehavior.cs`、`SubsystemScC4.cs`、`ScAmmoHud.cs`、`ScGunHitTest.cs`、`ScSushiCompatibility.cs`（新）；`Blocks/ScGunBlock.cs` 及两种模板方块；`src/ScCsgoNet/ScCsgoNetAdapter.cs`、`ScCsgoNetPlatform.cs`；`src/ScCsgoTactical/ScShieldProtection.cs`。
- 新增/扩展的检查：`tools/NetLoopCheck/StateLoop.cs`、`StateCases.cs`；`tools/PackageCheck/C4NetSoundRegression.cs`、`SushiStackingRegression.cs`，以及 `AmmoHudRegression`、`NetClientRegression`、`AimRayRegression` 的增补；`tools/completion_140.py` 的 `netloop`、`c4` 步骤。

## 9. 给用户的实机测试步骤

前提：Android 与 Windows 都装 `output/` 里的这一版轻量包＋探员包（或全量包），用测试世界。

最短步骤（约 10 分钟，客户端操作、房主旁观，之后主客互换再做一遍）：

1. **进世界**：客户端拿任意枪能开火，没有红字“CS 武器已停用”。
2. **新原皮 AK**：客户端从创造目录只拿一把 AK 放第 1 格，开一枪。右下角应为 29 并随射击递减；停手 3 秒数字不变。把这把枪拖到第 6 格、第 10 格各打一枪，数字接着减。再拿一把全新的 AK 直接放第 6 格重复一次。
3. **皮肤枪**：只拿一把皮肤枪，拿在手里不做别的操作。约 1 秒内应变成正常持枪（涂装正确、有弹药数字）。再拿第二把、第三把，每一把都不需要靠拿下一把才恢复正常。
4. **复制后换弹**：把一把打过几发的 Glock 复制一份（例如给同伴一份，或在两格各放一份），客户端用其中一把开一枪后换弹。换弹后应显示满弹，之后不应跳回复制前的数字。
5. **按住开火时切枪**：按住 AK 开火的同时切到另一把枪。另一把枪的弹药不应减少；切回 AK 能继续打。
6. **C4**：客户端下包，房主站在 10 格内，应听到启动音、按键音、放置音；房主下包，客户端应听到。中途松手取消后重新下包应正常。
7. **（装玲兰时）**：开启“调整堆叠”和“无耐久非放置物堆叠”，背包与原版箱子里枪应一格一把；玲兰箱子里把同一把枪的两个复制品叠在一格时，应提示“这一格叠放了多把枪…请分到不同的格子”，分开后可用。

出现问题时请提供：两端的 Game.log；出问题的是第几格、哪把枪、当时右下角数字和另一端看到的情况；是否刚拖动过物品、刚切枪或刚重连。

完整矩阵（时间允许时）：Android 房主→Windows 客户端；Windows 房主→Android 客户端；第三台设备旁观持枪者的第三人称；客户端断线重连后重复第 2、4 步；晚加入的客户端看已有枪的弹药是否正确；生存模式里新造的枪第一次换弹。

## 10. mpc3 实机反馈后的执行增补（2026-10-02）

先读 [最新诊断](mpc3-feedback-subworld-20261002.md)。用户要先处理命中/弹药及匪行为，再分析子世界；本轮按该顺序完成诊断。Windows 本次只改报告/规划与离线探针，未改产品；VPS继续实施时无需重新询问用户已经明确的中立规则或截图提示移除要求。

- P0：OpShotAck=42覆盖冲突，实际最终handler是ReceiveHit。使ACK唯一并检查全注册表，修正接收验证；回归使用正式注册链，不沿用缺Feedback的夹具。
- P0：记录/确认/预测跨帧和超时确认身份；自动与按键换弹均需要权威接受/完成，防止本地动画结束仍只有服务器旧弹量。
- 匪徒：**自然和召唤均默认中立，一名被攻击后附近所有匪攻击同一攻击者**。用户强调附近同目标；不要把范围限定为同Squad，不要只改单个被打者。现有群体参与现象不可因一个直接回调就断言不存在。检查伙伴对中立匪的主动索敌，避免旁路挑衅。
- 子世界：远古0.41.15/API1.9.3.1单机日志已显示主/子世界#1法玛斯/M4A4互冲和#3缺记录；不是MP缺消息。现已取得0.41.16实包并验证旅行生命周期，详见§11；不能合并整表、猜原始状态或承诺所有mod无需接入就通用。
- 玩家界面移除截图里的“平台构建未经验证；兼容修正已按契约启用”正常诊断提示；日志保留，真正拒载/版本不兼容提示保留。

当前 OpenSpec change 新增tasks §8–§11、specs中的协议反馈/权威换弹、neutral-bandits、subworld-travel、platform-notices；旧勾选只保留历史实现/离线证据，不覆盖新增失败。当前可同步复现结果见 evidence 中 mpc3-opcode-probe.json 和 subworld-travel-probe.json。

VPS续接短提示：

```text
继续 /home/dev/source-sync/ScCsgoKnives。读当前规划 §10、docs/tasks/mpc3-feedback-subworld-20261002.md 及 OpenSpec 新增任务8–11。
先修mpc3中ACK和Hit共用42的真实注册冲突，再用完整枪状态机验证预测/自动换弹，不要只改常量就交付。
匪按用户要求：野生/召唤默认中立，任一受攻击后附近所有匪打同一个攻击者，保留视线/烟雾判定；检查伙伴误挑衅路径。
移除截图指定的正常平台构建提示，保留日志和真正故障提示。随后按规划§11的通用迁移设计推进；0.41.16实包已可供Windows worker读取，不再用缺远古包作为静态分析阻塞，也不把它等同反馈者0.41.15的字节身份。不猜玩家原枪属性。
按既有同步与单写入者规则推进，不启动游戏、不改原世界或第三方包，完成源码/离线验证并按既有授权交付候选供我测试。新的实机失败保持未解决，直到有匹配证据。
```

## 11. 远古0.41.16实际包与通用解法（最新）

用户提供 `D:/下载/AncientWorld_v0.41.16.zip`，要求不限一个子世界模组。读 [通用迁移分析与VPS实现指导](universal-item-travel-analysis-20261002.md)，其§2定位实际断点，§3为实际DLL复现，§4定义通用接口/事务/提供方接入，§6列执行步骤。

已证实：Ancient自有TravelerSnapshot只传物品整数/数量/类型，无枪记录；目标玩家出现后Apply库存，不走我们ProjectXmlLoad的数据导入。实际DLL测试中源ID1/7发遇到目标ID1同型枪后变成23发，异型则不可用；方块类型重映射成功不等于实例记录迁移成功。

通用决策：保留世界分表与历史ID；把来源/稳定identity/完整状态放进版本化物品旅行payload，统一导出→目标预检→slot+record提交→幂等回执。原生XML和各mod自定义快照使用同一核心算法，只各自接旅行生命周期。完全不携带附加数据的第三方无法由CS事后无歧义还原，不能通过全局共享表、缓存上一世界或同名匹配掩盖。

0.41.16已有WildBond专用可选桥，但没有CS/通用物品状态接口。VPS可先实现我方统一API、detached/runtime核心和离线fixture，拟提供方最小接入建议；未获第三方改包授权不得直接重打远古。只有真实桥接落地后才能宣布该包可兼容。反馈者0.41.15仍是历史日志版本，不宣称取得了其包。

compact evidence：`ancient04116-inputs.json`、`ancient04116-provider-probe.json`、`ancient04116-inventory-envelope.xml`（均在当前evidence目录）。原DLL/反编译/Probe仍留Windows `.tmp/dev-temp/ancient-travel-20261002/`，通过worker读取，不复制全量资源到VPS。

## 12. 执行结果（2026-10-02，VPS 第二执行轮：mpc3 反馈修复＋通用携枪迁移核心，候选 mpd2）

§10、§11 是这一轮的输入；本节是结果。§8、§9 是 mpc3 那一轮的历史，保持原样。四个状态分开：

| 项 | 代码完成 | 离线检查通过 | 候选已交付 | 用户实机通过 |
|---|---|---|---|---|
| 一、误命中提示、弹药跳变、换弹 | 是 | 是（见 12.3） | 是（mpd2） | **未测** |
| 二、匪默认中立＋附近共同报复 | 是（“附近”= 32 格，**候选值**） | 是 | 是 | **未测** |
| 三、移除“未经验证”玩家提示 | 是 | 是 | 是 | **未测** |
| 四、通用携枪迁移 | CS 一侧的核心与入口：是；**远古世界本身未接入** | 是（含用户实际 0.41.16 DLL 的现状基线与加桥流程） | 核心随候选交付 | **blocked-provider**：需要远古采纳接入补丁（或用户另行授权我们做特定适配）后才能测 |

### 12.1 output 中的候选（mpd2，协议 6）

| 包 | SHA-256 | 字节 | 相对 mpc3 变化的成员 |
|---|---|---|---|
| `[API1.9]CS武器1.4.0-全量包.scmod` | `7239fc30774781322e1103016388c924efbaeb77dbc9780986d32d2aca631fbc` | 526,906,594 | `ScCsgoKnives.dll`、`ScCsgoTactical.dll`、`Net/ScCsgoNet.bin` |
| `[API1.9]CS武器1.4.0-轻量包.scmod` | `72de5c429768fdd3ec54ebf5df61e64b8cb8adae80182f2c578b656d4cb27109` | 35,749,845 | `ScCsgoKnives.dll`、`Net/ScCsgoNet.bin` |
| `[API1.9]CS武器1.4.0-探员包.scmod` | `a1b7602d07ab23fd022da43ab31c5536c88fd0e512e5432507e8b38980ad8a25` | 38,467,945 | `ScCsgoTactical.dll` |

manifest（`output/release-1.4.0/manifest.json`）标注为 CANDIDATE，mpc3 在其 history 中。没有安装到任何 Mods 或设备；没有改动任何世界；没有修改或重打包任何第三方包。

**两台设备必须都换成这一版**：CS 网络协议由 5 升为 6，与 mpc3 混用时会被拒绝并提示“CS 网络协议 …/5 与服务器 …/6 不同”，CS 武器停用。全量、轻量、探员三个包带同一玩法身份 `08400e52af609988ea03eb27183c1d15`，须成套更换。

存档：枪表 schema 7、物品布局 6、编号规则、既有各键都没有变。新增一个可选键 `GunTravelReceipts`，只有发生过“运行时携枪导入”的世界才会写出；不认识它的构建会忽略并丢弃它（只失去在途旅程的重复保护）。family 兼容矩阵（`compat` 步骤）本轮已重跑通过。

### 12.2 改了什么（玩家可见的结果）

1. **对空开枪不再出现命中白杠**。原因是 mpc3 把“射击确认”的消息编号定成了 42，与“命中反馈”相同，后注册的覆盖了前者：每一发确认都被当成命中显示，而确认本身永远收不到。现在一个编号只能有一个处理器（第二个会被拒绝并记录），确认不再单独占用编号；命中消息必须是 1/2/3 之一且格式完整才显示。
2. **弹药数字不再多减后回升**。确认与弹药记录在同一条消息里到达，按“第几发”结算，不再靠 1 秒超时。客户端显示的弹量 = 服务器弹量 − 自己已显示但尚未确认的发数。
3. **换弹由服务器执行并答复**。客户端按 R 或空仓扣扳机开始显示换弹的同时向服务器发出请求；服务器答复接受/拒绝/完成/取消。被拒绝或取消时客户端收回动画；动画播完不代表装弹成功，数字始终是服务器的。此前“显示 0、播完换弹仍是 2”的情况，现在以服务器装满结束。
4. **匪默认中立**：野生的、召唤的、旧存档里的都一样。走近、在旁边开空枪、召唤后的 3 秒警告结束，都不会让它们动手。**打了其中一个，被打者周围 32 格内所有活着的匪都以同一个攻击者为目标**（不分小队和来源，不向更远处扩散，不迁怒没动手的玩家）。隔墙、隔烟仍不开火；看不见攻击者 8 秒后放弃；超出各自报复距离（40 格，狙击 64 格）的不追。
5. **伙伴不再主动打中立的匪**。匪把目标定为伙伴的主人、伙伴自己或同主人的其他伙伴时才应战；主人先动手时伙伴照旧跟进。
6. 进入联机时不再弹出“CS 联机：此联机平台构建……未经验证；兼容修正已按契约启用。”；日志里仍有平台构建与契约状态。真正不可用（网络层未加载、协议不同、被拒绝、冻结修正未能启用）的提示保留。
7. 带枪进出子世界：**这一版单独使用不会改变远古世界里的现象**——远古 0.41.16 不调用新入口。CS 一侧已经具备：导出携带枪的完整状态、在目标世界分配本地编号（同号同型/异型都不会串）、往返回到原编号、重复提交不重复写入、失败不留半截。远古接入建议见 [ancient-world-cs-bridge-suggestion-20261002.md](ancient-world-cs-bridge-suggestion-20261002.md)。

### 12.3 每个用户问题的证据

| 用户问题 | 旧包（mpc3）上的复现 | 候选上的结果 | 实机 |
|---|---|---|---|
| 对空开枪出现命中提示 | 真实状态机回环：对空连发 12 发，客户端收到 12 条“命中”，准星标记被画出（`netgunloop-delivered` G01）；注册表里 42 被 `ReceiveHit` 覆盖（T5） | 同一回环 0 条命中消息、无标记、无击杀行；服务器真实上报的命中/爆头/击杀各自正确显示（G01、G03、C14） | 待用户 |
| 弹药多减再回升 | 同一回环：1.2 秒连发时读数降到 9 后回升（观察结束时读数 13、服务器 18、未确认 5），未确认数最高堆到 10（G01）；点射同样回跳（G02） | 读数逐发递减、只降不升，始终等于“服务器记录 − 未确认数”，未确认数不超过在途发数，没有一发靠超时结算（G01、G02、C15） | 待用户 |
| 显示 0、播完换弹仍是 2 | 同一回环：客户端显示 0 时服务器还剩数发，换弹动画播完后读数回到服务器的剩余弹量，服务器从未换弹（G04 最后一项：结束时服务器 4、读数 4） | 自动换弹与按键换弹都以服务器满弹结束；服务器先把客户端已显示的几发打完再换弹；读数在满弹到达前保持 0；拒绝/取消时客户端收回动画（G04–G09） | 待用户 |
| 匪应默认中立、一个被打附近都打同一个人 | —（行为变更，不是缺陷复现） | `enemy/neutral-until-attacked…`、`enemy/one-attacked-all-within-32…`、`enemy/summoned-squad-faces-its-summoner…`、`npc-leaves-neutral-squads-alone…` 等，Full 与 Lite＋探员各一遍 | 待用户（含 32 格是否合适） |
| “未经验证”提示 | — | C17：在用户实际平台构建（未列入已验证清单）上，已接受的玩家没有该提示，日志行保留 | 待用户 |
| 远古带枪变纸条/串成别的枪 | 用户实际 0.41.16 DLL：同号异型不可用；同号同型读成目标世界那把的 23 发（`item-travel/ancient-world-0.41.16-real-assembly` 的“as shipped”部分） | 同一 DLL，按建议放置三处调用后：落到新编号、可用、读 7 发，记录与来源完全相同；另有 20 项核心用例（往返、重复、重启、拒绝等） | **不能测**：远古包未接入 |

数字与逐项结果：[evidence/mpd2-offline-results.json](mp-state-consistency-evidence-20261002/mpd2-offline-results.json)；状态定义与限制：[verification](../../openspec/changes/weapon-state-consistency/verification.md) §10、§6。

### 12.4 未做 / 已知缺口

- 交付时没有实机结果；现已回收 14:42 用户视频与两端日志，弹药仍存在一发回升，详见 §13。原离线回环的固定延迟不代表真实链路抖动、Android 运行时、画面和声音。
- 交付时已注明停火后未被服务器打出的预测会回滚。最新诊断纠正“房主帧率低于武器射速所需”的归因：约 50 FPS 也能触发，核心是每次按实际帧重设下一发时间累积节奏偏差；链路抖动也可触发。该缺口仍需修正，不能将回升当成弹药一致性验收通过。
- 32 格是候选值；烟雾遮挡沿用既有视线判定，没有单独新增用例。
- 远古世界：未接入，现象不变。已损坏的枪（此前穿越留下的串号/缺记录）不会被修复，恢复它们需要原始记录与来源证明；本轮没有写任何用户世界。远古 0.41.16 只支持单玩家旅行，没有绕过。
- 1.0/1.2 兼容修订包的重建脚本已把新文件加入共享清单，但那两个修订包本轮没有重建或重新交付。
- mpc3 轮遗留项不变：玲兰自有容器堆叠、个人箱多人归属、频道保存缺陷（提供方）；任务 5.6（机器吸物、死亡保物、同伴转交）；X01、X02 未定位；目标联机玲兰包 blocked-input。

### 12.5 证据与文件

- 管线：`mpd-pipeline-mpd2-all-01`（job `4b3e99aaa55a4f0a96e718cacf3f1a58`）与 `mpd-pipeline-mpd2-compat-01`（job `fa1014e3e8314510932d7dbef1bd9398`），阶段 `.tmp/completion-140-20260929/mpd2`。首轮 mpd1（job `c5db0c9acd8948a9bd2cc7b6d866eb95`）失败两处并保留在 verification：一项按 IL 检查“每帧更新”的回归因逐玩家更新拆成独立方法而失败（改了检查），`compat` 步骤的 InventoryCheck 工具缺一个链接源文件（mpc3 轮引入、当时未重跑 compat，本轮补上）。
- 交付 job `e5db627794e64a1bb9d400631950a1bb`；清理 job `490989c884d242258a6c50da9dbddc52`，回执 `docs/tasks/mpc3-feedback-subworld-20261002-cleanup.json`。
- 主要源码：`src/ScCsgoKnives/Net/ScNet.cs`、`ScNetGuns.cs`、`ScNetMirror.cs`、`ScNetFeedback.cs`、`ScNetMessage.cs`、`ScNetSlots.cs`、`ScNetPresentation.cs`；`World/SubsystemScGunBlockBehavior.cs`、`ScCombatFeedback.cs`、`ScItemTravel.cs`（新）、`ScGunTravel.cs`、`ScGunRegistry.cs`；`Mod/ScCsgoKnivesModLoader.cs`；`src/ScCsgoNet/ScCsgoNetAdapter.cs`、`ScCsgoNetPlatform.cs`；`src/ScCsgoTactical/ComponentTacticalEnemy.cs`、`SubsystemTacticalEnemies.cs`、`ComponentTacticalCompanion.cs`、`TacticalModLoader.cs`。
- 检查：`tools/NetLoopCheck/StateLoop.cs`、`StateCases.cs`（C01–C17）、`GunLoop.cs`（新，G01–G09）；`tools/PackageCheck/ItemTravelRegression.cs`（新）、`TacticalEnemyRegression.cs`、`TacticalRegression.cs`、`CombatRegression.cs`；`tools/completion_140.py` 的 `netloop`、`main` 步骤；`tools/InventoryCheck/InventoryCheck.csproj`。
- 设计决定：[design §12](../../openspec/changes/weapon-state-consistency/design.md)。远古接入建议：[ancient-world-cs-bridge-suggestion-20261002.md](ancient-world-cs-bridge-suggestion-20261002.md)。

### 12.6 给用户的最少测试步骤

前提：**两台设备都换成 output 里这一版**（轻量＋探员，或全量），用测试世界。约 10 分钟，客户端操作、房主旁观；条件允许时主客互换再做一遍。

1. **进世界**：不再有白字“此联机平台构建……未经验证”；没有红字“CS 武器已停用”。
2. **对空开枪**：客户端拿 AK 朝天长按约 1 秒，再点射几发。准星处不应出现命中白杠/黄杠/击杀行；右下角数字逐发递减，停手后不变。（mpd2 用户视频已发现停火后回升一发，此项尚未完整通过；最新原因和下一轮检查见 §13。）
3. **打空自动换弹**：按住不放直到打空，让它自动换弹。动画结束后应为满弹，继续按住能接着打。
4. **按 R 换弹**：打几发后按 R（或触屏换弹键）。结束后满弹。换弹途中切到别的格再切回：应没有装上（数字是切走前的值），再换一次正常。
5. **新枪**：从创造目录拿一把新 AK，点三发（30→27），再打空让它换弹：满弹。
6. **打中目标**：打一只动物或匪：白杠（命中）、黄杠（爆头）、红杠＋击杀行应照常出现。
7. **匪**：走到野生的匪和用信标召唤的匪旁边，站 10 秒、在旁边朝天开几枪：它们不应开火。然后打其中一个：附近的匪（不止被打的那个、不止同一小队）应一起打你；站得远的另一名玩家不应被打。带伙伴时：伙伴不应先开火；你或伙伴被打、或你先动手后伙伴应参战。
8. **子世界（可选）**：这一版配现有远古包，带枪穿越的现象预计**不变**（仍可能变纸条或变成另一把枪的状态）。请不要用重要的枪测试；需要的话只确认“现象未变、没有更糟”。

出现问题时请提供：两端的 Game.log；是第几步、哪把枪、当时右下角数字和另一端看到的情况；房主是哪台设备。

## 13. mpd2 实测残留：停火后回升一发（2026-10-02，14:42 视频）

详细证据与 VPS 修正建议：[mpd2-ammo-jitter-20261002.md](mpd2-ammo-jitter-20261002.md)。Windows 本轮只做诊断、临时离线夹具与文档，没有改产品、替换候选或启动游戏。

- 视频约 0.9–1.0 秒 `18→19`、4.2–4.3 秒 `8→9`；第二轮停在 13，没有回升。两端实装包哈希匹配 mpd2，协议 6 握手成功。
- 用户实际 Windows 平台＋候选包的真实状态机回环：固定 50/250ms、60/60 FPS 不回跳；100/50 FPS 三轮中两次回升一发，即使不加模拟传输延迟也一样；60/60 FPS 加有序抖动可回升一发。没有超时放弃或消息解码丢弃。
- 机制：客户端先多显示一发，服务器独立的帧调度尚未打出；停火到达后标成 Skipped，确认清掉预测，显示回到真实弹量。固定延迟不是必要条件，也不能直接归咎于平台通道缺失。
- 下一轮优先稳定射击时间轴、消除逐发帧取整误差，并核对停火前合法射击与权威事务的对应关系；加入不整齐主客帧率、帧耗时波动和输入批量到达检查。具体建议见报告 §4，产品修正待 VPS 按用户执行指令继续。
- “大部分没问题”仅是用户整体反馈；弹药此项为实机仍失败，其他未逐项提供结果的场景保持待验收，原单机/子世界/玲兰边界不变。

### 13.1 执行结果（2026-10-02，VPS 第三执行轮：只修“停火后回升一发”，候选 mpe1）

**当前状态：本修复以候选 mpe1 交付后，`output/` 已于同日被候选 mpf1 替换——mpf1 = mpe1 的全部内容＋投掷物“松键即投”（见 [quick-throw-20261002.md](quick-throw-20261002.md) §7 的哈希与步骤；枪械部分的 95 项回环在 mpf1 上同样通过）。下表的 mpe1 哈希是历史记录；实机请用 output 里的 mpf1。离线门禁全部通过；没有启动游戏、没有安装到 Mods/手机；实机验收待用户。** 上面 §13 开头的五条是修复前的诊断（历史），本小节是其结果。本轮没有改子世界、玲兰、匪、外观、音效；这些项的状态仍是 §12 的记录。

| 包 | 字节 | SHA-256 |
|---|---:|---|
| `[API1.9]CS武器1.4.0-全量包.scmod` | 526,907,327 | `46befae3d0ef39a669c1f14cda471abf3821000ce70c0dea89777876cbc11dcb` |
| `[API1.9]CS武器1.4.0-轻量包.scmod` | 35,750,517 | `ee3e38eb47dcbd6dfd8f2c9e0a5d35539131eb0c872ca2223fdc537a84689440` |
| `[API1.9]CS武器1.4.0-探员包.scmod` | 38,467,957 | `8443c77b7c304b316c48de99924e8c94ba8edfb8b5df84b18c0ca27ee20543ba` |

玩法构建标识 `50e65beb38b074c66696ff6563e1adc4`；轻量核心 `4e31ef27…`，全量核心 `8079f103…`，探员程序集 `c4153adb…`；联机适配器 `3e7fd306…` 与 mpd2 **逐字节相同**（协议仍为 6，线格式未变）。与 mpd2 相比包内只换了 `ScCsgoKnives.dll`（全量/探员另有带构建标识的 `ScCsgoTactical.dll`）。**两端都要换成本候选**：不同构建在握手时互相拒绝。

**改了什么**（依据与取舍见 design §13）

1. 射击时间轴不再累计帧误差：下一发从“这一发到期的时刻”起算（到期后 50 ms 内执行时），单机、房主、客户端预测共用。mpd2 的实测：AK 标称 100 ms，100 FPS 实际 110 ms、50 FPS 实际 120 ms，所以客户端 100 FPS/房主 50 FPS 时客户端 0.6 s 显示 6 发、房主只打 5 发。
2. 服务器对远端玩家按“该客户端已显示的发数”执行射击，并用自己为这把枪维护的时间轴校验（到期前后 125 ms 内）；弹量、枪身份、玩家状态、输入租期照旧由服务器检查。超出时间轴或报告过期的不执行、不补射，记 Skipped，客户端按确认回滚。
3. 客户端输入改在帧末发送，同一条消息带本帧的按下/松开、发数与瞄准；服务器同一帧收到“按下、发数、松开”时仍能对应。
4. R8：服务器的击锤在客户端报告了这一发时落下；落锤前松手不走火。
5. 只改时间轴（`NextShot += cycle`）不够：对照实验仍 28/95 失败（100/50 下 15 次短连射 3 次回升）。

**复现前后（同一组扩展 GunLoop，两端真实 `Update`、真实平台包，逐次记录客户端显示、服务器执行、Fired/Skipped 与读数每次回升）**

| 场景（15 次短连射，覆盖 12 个松手相位） | mpd2 | mpe1 |
|---|---|---|
| 客户端 100 / 房主 50 FPS，每向 50 ms | 显示 63、执行 57、Skipped 6，**6 次回升**；例：显示于 0.010…0.560 s 共 6 发，执行于 0.080…0.560 s 共 5 发，0.680 s Skipped，读数 24→25 | 显示 66、执行 66、Skipped 0，回升 0 |
| 100 / 50，无传输延迟 | 6 次回升 | 0 |
| 100 / 50，有序抖动（成批 +90 ms） | 5 次回升 | 0 |
| 60 / 50，50 ms | 8 次回升 | 0 |
| 60 / 60，50 ms 与 250 ms | 0（与 Windows 诊断一致） | 0 |
| 60 / 60，有序抖动（成批 +150 ms） | 4 次回升，另有服务器多打 1 发 | 0 |
| 60 / 30、144 / 50 | 1 / 8 次回升 | 0 |
| 50 / 100 | 服务器每轮多打 1 发（显示 57、执行 63） | 显示 63、执行 63 |
| 帧耗时 ±35 %（约 100/50；约 60/50＋20–120 ms 有序抖动） | 1 / 2 次回升 | 0 |
| 房主每 23 帧停顿 120 ms（输入成批到达） | 13 次回升 | 0 |

整套：mpd2 **32/95 失败**，mpe1 **95/95 通过**（管线内用包内成品；另在 VPS 上用包内成品×用户实际 Windows 平台程序集再跑一遍，同为 95/95）。其余用例：一次虚报 5 发只执行时间轴允许的、其余 Skipped 并回滚（G11）；R8 在落锤前/同时/之后松手（G12）；FAMAS/Glock 三连发（G13）；沙鹰贴着射速点射（G14）；连射中切枪、开菜单（G15）；连射中断连再接入（G16）；原有 G01–G09（首发、换弹、打空、切枪、延迟等）不变并通过。

**单机（API 1.9.3.1 引用，离线）**：新增 `ShotCadenceRegression` 14 项，逐帧驱动真实 `UpdateGun`：按住 AK 在 20/30/50/60/100/144 FPS 与不均匀帧下每发间隔为标称 100 ms（单发抖动不超过一帧）、每帧至多一发；5/8/12 FPS 不补射；停顿后不积攒；沙鹰点射、FAMAS/Glock 三连发周期不短于标称。同一组在 mpd2 上 5 项失败（50 FPS 120 ms、100 FPS 110 ms、144 FPS 104 ms、不均匀帧 110 ms、FAMAS 轮间 560 ms）。**这意味着单机按住连射的实际射速回到武器标称值（例如 50 FPS 下 AK 从 500 发/分回到 600 发/分）——这是消除累计帧误差的直接结果，请在实机上确认手感。** R8、换弹、菜单、瞄准等既有单机回归（`r8-input/`、`GunHandling` 187 项等）不变并通过。

**执行过的检查**（均为离线，结果数字见 [mpe1-offline-results.json](mp-state-consistency-evidence-20261002/mpe1-offline-results.json)，逐条见 verification §11.1）：管线 `all`＋`compat` failed steps `[]`：netloop 28/28×2、netstate 109/109、目标断言 12/12、gunloop 95/95（mpd2 包 32/95 失败）、appnet 19/19×2、c4 140/140×2、main-full 14118 / main-lite 14428 无失败（含 shot-cadence 14 项、item-travel 20 项）、ai 67/67×2、vf 292/292×2、tactical 110 项仅 4 个既有已知失败、family 246/246 及其余兼容作业、throw/motion/hotspots/ui。

**未覆盖 / 已知边界**

- 没有任何游戏进程运行：真实链路、Android 做房主或客户端、你视频里的 100/50 FPS 组合，都只有离线模拟，等你实机确认。
- 切枪的同一帧显示的那一发、以及服务器停顿超过 125 ms 时停火前的最后一发，仍会被拒绝并回滚一发（这是“真正被拒绝的预测”，离线用例里有记录）。
- 50 ms / 125 ms 是候选取值；实机网络下若仍偶发，请附双方日志。
- 手感：单机与联机的按住连射射速现在等于标称值，比 mpd2 在 50/100/144 FPS 下略快。

**给用户的最少测试步骤**

1. 两台设备都换成 `output/` 里的本候选（全量或轻量；用探员则两端都装探员包），进同一房间。
2. 客户端拿 AK 或 M4，短连射 3–6 发后松手，重复十几轮，盯弹药读数：松手后不应再出现 `18→19` 这种回升。高帧率客户端＋低帧率房主（你上次的组合）优先。
3. 顺手各试一次：打空自动换弹、手动换弹、连射中切枪、R8 左键蓄击、FAMAS/Glock 三连发、沙鹰快速点射。
4. 单机进一个世界按住 AK 打一梭子，感觉射速是否正常（应与 60 FPS 时一致）。
5. 仍有回升时，请给出大致时间点和两端 `Game.log`。

