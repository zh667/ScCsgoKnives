# 验证矩阵与证据登记

> 最新状态（2026-10-02，14:42 用户反馈后）：候选 **mpd2**（协议 6）已交付 output，原离线门禁保持其覆盖范围内的结果；用户实测“大部分没问题”，但短连射停火后仍偶发回升一发。真实候选 DLL 的离线诊断已复现，此项为 runtime-fail / baseline-reproduced，见 §11；其他场景未逐项确认，不能统一标成 runtime-pass。子世界旅行为 blocked-provider（远古包未接入）。
>
> 历史（2026-10-02用户11:11视频）：mpc3枪械反馈与确认实机失败，实际DLL完整注册探针证实ACK42被Hit42覆盖。旧R4/R5保持历史夹具结果，不构成新需求通过。新增证据与矩阵见§9、§10；此前“实机未运行”描述指VPS上一执行轮，用户现已提供失败视频。

## 9. mpc3 新反馈证据（Windows诊断轮）

主报告：[mpc3-feedback-subworld-20261002.md](../../../docs/tasks/mpc3-feedback-subworld-20261002.md)。真实候选、视频与日志哈希见该文；本轮不启动游戏，只读用户证据。

- `mpc3-opcode-probe.json`：生产顺序Register Guns+Feedback后42归ReceiveHit；selection7/fired2解析成假outcome7，尾部未消费；权威2/pending2显示0，确认错路由后仍0，超时回2。真实已安装mpc3 DLL，runner最终exit0表示错误复现成功。
- `subworld-travel-probe.json`：实际mpc3 DLL＋合成跨世界XML，完整快照可把源1映射成目标2；无快照Prepare=null。不是远古实际切换验收，也未证明反馈者用了此CS哈希。
- Game(3).log：API1.9.3.1 standalone、远古0.41.15，主/子世界#1法玛斯/M4A4互冲、#3缺记录。随后用户提供0.41.16，实际DLL调用已复现同类问题；旧日志版本和新实包身份分开记录。
- VPS前轮StateLoop未注册Feedback，故其ACK测试没有复现生产handler覆盖。新门禁需生产全表注册。

## 10. 新增矩阵（mpd2：离线结果已登记，实机全部待用户）

| Case | 需求 | 必须区分的场景与结果 | 当前证据/状态 |
|---|---|---|---|
| R07 | W6 | 全模块注册后ACK独占handler；空射0提示，真命中/头/击杀正确 | **offline-pass**（mpd2）：游戏自身的注册（`ScNet.RegisterCore`＋探员包＋外观集成，共 35 个处理器）无重复编号、无覆盖、无拒绝（C13、T5）；真实状态机回环对空 12 发×2 种输入 0 条命中消息、无标记（G01）；服务器上报的命中/爆头/击杀各自显示（G03、C14）。**baseline-reproduced**（已交付 mpc3，同一检查）：T5/T6 失败，回环对空 12 发收到 12 条命中。实机待用户 |
| R08 | W6 | selection低字节1/2/3/7与畸形payload，不可变成反馈；重复注册有明确策略 | **offline-pass**：10 种不是命中确认的 payload（含 mpc3 的确认格式、outcome 0/4/200、多余/缺失字节、不可能的距离）逐条被丢弃且不显示；伤害标记/提示/换弹答复/记录消息的错误格式同样整条丢弃（C14）；同一处理器重复注册幂等，另一处理器抢占已用编号（同向或反向）被拒绝并记录（C13） |
| R09 | W7 | 服务端剩2/客户端预测0，自动与手动reload都走权威接受/完成/取消 | **offline-pass**：两端真实 `SubsystemScGunBlockBehavior.Update`，5 种延迟/房主帧率下客户端显示 0 时服务器仍有 1–4 发：服务器打完客户端已显示的再换弹，读数保持 0 直到满弹；松开扳机的情形以服务器满弹结束；按键换弹、被拒绝（客户端 7 帧内收回）、切枪取消、服务器自行开始的换弹、新枪首次换弹（G04–G09，回环共 45/45）。**baseline-reproduced**（mpc3）：回环 31/45 项失败，其中“换弹后读数仍是服务器剩余弹量、服务器未换弹”。实机待用户 |
| R10 | W3/W7 | 记录与ACK跨帧、旧预测超时后新预测再遇旧ACK、连续点射 | **offline-pass**：确认与记录同一条消息（C01 断言没有任何不带记录行的确认）；答复迟到三帧且成组到达时读数逐帧等于 30−已显示数（C15）；放弃后的迟到答复只结算旧的那一发，新的一发仍待确认；另一选择的答复不结算当前选择；服务器多打一发时计数跟随（C15）；长按与点射读数只降不升、无超时结算（G01、G02） |
| N01 | N1 | 野生、召唤、Source缺省旧匪：靠近/空枪/暖机结束仍中立；伙伴不先开战 | **offline-pass**：`enemy/neutral-until-attacked-then-bounded-retaliation-and-release`（3–64 格、前后、枪声）、`enemy/summoned-squad-faces-its-summoner-and-stays-neutral-until-attacked`（暖机及其后的搜索结束仍中立，3 人与 5 人）、`enemy/natural-placement-band…`（自然小队面对可见玩家 10 秒不动手）、`npc-leaves-neutral-squads-alone-until-they-turn-on-its-owner-or-it`。实机待用户 |
| N02 | N2 | 打一名，附近跨小队/来源匪打同一攻击者；远处不扩散，无关玩家不替代 | **offline-pass**：`enemy/one-attacked-all-within-32-take-the-same-attacker-once`（经引擎的 ProcessAttackment 钩子：同小队、自然、召唤、来源缺省的成员都取同一攻击者并开火；33 格处的不加入；被通知者不再外传到 60 格处；旁观玩家不受攻击；已在报复的不被第二个攻击者拉走）。32 格为候选值。实机待用户 |
| N03 | N2 | 致死受击、环境无来源、墙烟、攻击者消失、客户端假hit | **offline-pass（部分）**：致死一击仍通知附近；无攻击者、攻击力为 0、同类误伤不挑衅；墙后的攻击者：取目标但不开火，8 秒后放弃；攻击者死亡/移除由既有 Drop 规则处理（既有用例）。**未单独新增**：烟雾（沿用同一 `Visible` 判定）。客户端假 hit 已由 R07/R08 排除，仇恨只在权威端由真实攻击事件产生 |
| T01 | T1/T2 | 主/子已有ID1同型/异型，双向原生枪往返保真 | **offline-pass（CS 核心）**：`item-travel/same-number-other-model…`、`same-number-same-model…`、`round-trip-gun-born-in-the-main-world`、`…-in-the-sub-world`、`whole-state-carried`、`an-ordinary-move-leaves-one-usable-gun` |
| T02 | T1/T4 | 实际远古保存/携带白名单/晚恢复生命周期 | **blocked-provider**：远古 0.41.16 不调用 CS 入口，游戏内旅行现象不变。离线已用其实际 DLL 的 Capture/Save/Load/Apply 验证“按建议放置三处调用”后的结果正确（见 T04）。接入建议已写，未在远古上实施。**2026-10-03 新的用户日志再次复现**（`D:/下载/Game(2) (1).log`，SHA-256 `cbf0be15…`；Android API 1.9.3.1 单机，远古 0.41.15，CS 轻量 1.4.0 + 探员）：主世界 0 条记录 → 进远古 0 条 → 回主世界 0 条，04:52:32 `unusable gun data 86`（SCAR-20、记录 #1）→ 再进远古该世界已有 1 条记录，04:52:47 `unusable gun data 92`（截短霰弹枪、记录 #1：主世界里新开的 #1 与远古的 #1 撞号）；全程没有 `[GUN_TRAVEL]` 导入行。与代码一致：满弹/空弹的新枪不占记录（`GunSpec.MakeData` 的 FreshFull/FreshEmpty），第一次改变状态时才在**当时所在的世界**分配记录，所以“新枪能带过去”不等于迁移成功。**2026-10-03 通用方案（不依赖提供方）**：核心读取“离开的那个世界”存档里本来就有的携带包，在到达库存与之逐格吻合时按同一规则导入（只在同一世界文件夹的世界之间，见 `docs/tasks/subworld-travel-generic-20261003.md`）。候选 sw2：离线 item-travel 24/24（含 0.41.16 实包无桥直走）；隔离 1.9.3.1 实机——远古 0.41.16 用其自身 `BeginTravel`、尸鬼 2.0 用其自身 `TransPortal`，各 11/11（新 SCAR-20 进子世界开枪生成记录 → 回主世界可用、弹数连续、主世界自己的 #1 不动、无 `unusable gun data` → 再出去回到原编号 → 存档重进）。**provider-agnostic-pass（Windows 隔离副本）；用户安卓实测待验** |
| T03 | T2/T3 | 子内消耗/改皮后返回、两个玩家、目标占用、重复导入、损坏拒绝 | **offline-pass（CS 核心）**：消耗后返回、两名旅行者两份信封、目标同身份被其他容器持有→拒绝且不写入、同一旅程重复→同一映射且不写入、信封被改/截断/版本或 schema 不认识→拒绝且不写入、导出拒绝（叠放、同枪两份、缺记录、待结算击杀）。远古自身只允许单玩家旅行，未绕过 |
| T04 | T2/T4 | 0.41.16实际Capture→Save→Load→Apply；方块号304→320、源7发/目标同号同型23发 | **baseline-reproduced ＋ offline-pass（加桥流程）**：`item-travel/ancient-world-0.41.16-real-assembly`（管线内读取用户 zip，DLL `f9fe4ccf…`）：原样→仍 #1，同型读 23、异型不可用；放置三处调用→#2、可用、读 7、记录行与来源相同；其库存 XML 丢弃未知扩展节点 |
| T05 | T4 | 完整XML与独立快照＋版本化扩展两个适配共用核心；receipt重放与crash恢复 | **offline-pass**：`saved-xml-importer-and-live-importer-share-identities-and-the-rule`（同一把枪两条路径同一身份、同一本地编号）、`restart-after-commit-and-crash-before-save`、`the-same-transfer-again-changes-nothing`、`a-build-that-does-not-know-the-receipts-key…`、`return-to-a-world-whose-identity-table-was-dropped`；既有 `travel/*`、`generic-travel/*` 仍通过 |
| U01 | U1 | 正常未验证构建仅日志，真正不兼容仍提示 | **offline-pass**：C17（用户实际平台构建不在已验证清单内：已接受的玩家无提示，日志行保留构建标识与修正状态）；C12（协议不同仍按名拒绝并提示） |

所有新候选以实际核心/适配器/Agents身份登记，双端实机结果由用户提供；不把修常量后离线通过称整组自动换弹已修。当前change不archive。

本轮 runner 的补充说明（与 §2 同等效力）：
- `tools/NetLoopCheck` 的三种模式都改为执行游戏自身的注册（`ScNet.RegisterCore`，再按探员包、外观集成各自加载器的调用注册），对没有该入口的已交付构建按其加载器顺序逐模块注册并比对处理器表。mpc3 轮的夹具手工只注册了四个模块，没有命中反馈，所以当时的 83/83 不能证明这条链。
- `--gunloop`（`GunLoop.cs`，G01–G09）：客户端端与服务器端各运行真实的 `SubsystemScGunBlockBehavior.Update`（真实动画数据：部署、射速、换弹里程碑），客户端由开火键/鼠标射线/换弹请求驱动，服务器一侧只由线路上到达的消息驱动；之间是 CS 适配器的包、平台的写入/解码与库存包，每个方向固定延迟若干帧，另有“房主帧率为客户端 1/3、1/4”的情形。逐帧记录客户端读数的两个来源（镜像记录、未确认数）与服务器弹量。**不覆盖**：真实链路抖动与丢包重传、生存模式换弹的弹药消耗（夹具为创造模式）、Android 运行时、画面与声音。
- 同一组目标断言与同一回环在已交付的 output 轻量包（mpc3）上运行，失败即复现，不计入通过。
- `tools/PackageCheck/ItemTravelRegression.cs`：两个世界各有自己的枪表、迁移账本、枪方块编号与库存；远古部分只在给出 `SC_ANCIENT_PACKAGE` 时运行（管线对 zip 核对 SHA-256，缺失则 `main` 步骤失败），在内存中读取 DLL，不解包、不写入。

## 1. 状态定义

- `diagnosed`：日志/源码支持原因，可能仍有事件级缺口。
- `baseline-reproduced`：实际旧包/实际 DLL 重现目标错误，不是修复通过。
- `offline-pass`：修改后候选由指定 runner 执行通过。
- `candidate-delivered`：候选按哈希交付，实机可仍未验收。
- `runtime-pass`：记录真实平台、两端角色、构建和执行证据。
- `blocked-input`：列具体输入缺失与独立可继续工作，不能整项消失。
- `not-run`：本轮没有执行，写明原因。

2026-10-02 VPS 执行后的总体状态：**候选 mpc3 已生成并交付 output（candidate-delivered），离线用例通过（offline-pass）；没有任何一项是 runtime-pass**。本轮按用户要求没有启动游戏，下列“离线”均指在真实程序集上、不启动游戏进程的检查；它们不证明真实链路时序、Android 运行时、任何画面或声音。

## 2. 证据来源

- 旧行为基线：mpb 轻量 `1dd379bd…`＋探员 `d0a49c56…`（审计报告）；Windows 探针摘要在 [evidence](../../../docs/tasks/mp-state-consistency-evidence-20261002/)。
- 本轮 runner（均在仓库内，可重跑）：
  - `tools/NetLoopCheck`：`--state`（C01–C12，83 项）与 `--baseline`（4 组目标断言 9 项，只使用 mpb 也有的成员，可对新旧两个构建运行）。真实的是平台的包表、序列化、`InventorySyncPacket`、`ComponentCreativeInventory`、CS 的事务/镜像/输入/预测代码；代替游戏的是枪械状态机本身（夹具按相同顺序调用相同入口），见 `StateLoop.cs` 文件头。
  - `tools/PackageCheck`：`C4NetSoundRegression`（真实 `SubsystemScC4` 下包循环）、`SushiInventoryRegression`＋`SushiSyncInventoryRegression`＋`SushiStackingRegression`（用户实际 SushiBase/SushiTool 程序集）、`AmmoHudRegression` 新增一项、`AimRayRegression`/`NetClientRegression`/`NetIdentityRegression` 既有项。
- 管线：`tools/completion_140.py`，阶段 `mpc3`，步骤 `all`；`netloop` 步骤包含上述状态用例及对**已交付 output 轻量包（mpb）**的基线复现；`c4` 步骤带 `--sushi-inventory-mods`（用户 1.9.3.1 Mods，只读）。
- 可同步的紧凑结果：`docs/tasks/mp-state-consistency-evidence-20261002/`（见其 README）。

Windows 进程内加载 1.9.3.1 / 1.9.3.2 的 DLL 做检查不等于启动了对应的游戏。源码夹具编写、编译、真实 DLL 离线、运行时 gameplay 四者分开登记。

## 3. 用例状态

| Case | 需求 | 离线结果（mpc3） | 旧包基线 | 实机 |
|---|---|---|---|---|
| C01 | W1 | offline-pass：槽 0/5/9 各 8 项（服务器扣一次并分配记录；同帧依次发 记录→该库存一次→确认；A 与旁观 B 的槽都等于服务器；HUD 29；静止 30 帧无新包且保持；第二发只发记录行与确认） | baseline-reproduced：mpb 的 T1/T2 失败（客户端槽仍是模板、HUD 30） | 待用户 |
| C02 | W1 | offline-pass：三把涂装枪逐把拿在手里即成为带涂装的实例，A/B 一致，之前的枪不变（6 项） | diagnosed（同一根因；未单独对 mpb 跑） | 待用户（涂装与持枪动作需目视） |
| C03 | W1/W3 | offline-pass：复制分离后旧记录保持 5 发与磨损，A/B 的槽改指新记录；服务器换弹后读 20；未确认的预测超时后仍是 20，镜像旧记录始终 5（5 项） | baseline-reproduced：mpb 的 T3 三项失败（预测把镜像 7 改成 0、槽仍是 7、超时后 HUD 5） | 待用户 |
| C04 | W2 | offline-pass：槽先到时枪不可用、不预测、不发扳机、服务器不开火；记录到达即可用；记录丢失 0.5 s 后索取并恢复；服务器不应答时 5 次后停止索取、保持不可用（5 项）。记录先到的顺序即 C01 | — | 待用户 |
| C05 | W2 | offline-pass：同帧两次提交只发一次库存；整批重复投递两次无变化；旧记录行晚到被丢弃；记录行发送失败后保留并补发、服务器不重算；平台库存包与 CS 包同在通道 0（5 项）。**限制**：平台库存包没有版本号，防旧依赖平台通道的有序可靠传输（设计 §10.1），这一点是按平台代码得出的结论，夹具只断言通道号 | — | 待用户 |
| C06 | W2 | offline-pass：记录写入后失败→槽恢复、无残留记录、发布的是恢复后的槽；提交前枪被移走→拒绝、不占编号、不发布；库存拒绝写入→拒绝、槽保持模板（4 项） | — | 不适用（故障注入） |
| C07 | W3 | offline-pass：新世界同编号记录立即读 20 且超时后不变；玩家实体替换后无预测；世界关闭清理；连接离开时松开扳机并忘记输入序号；重新接受后可继续（7 项） | baseline-reproduced：mpb 的 T4 失败（新世界记录 7 被写成 5） | 待用户（重连/重进世界） |
| C08 | W3 | offline-pass：三发中确认两发→镜像 18、待确认 1、HUD 17；重复确认不重复抵扣；换弹后镜像 20、HUD 19；新射击确认一次；未确认的超时后 HUD 等于服务器值（5 项） | — | 待用户 |
| C09 | W4 | offline-pass：客户端指向 A 而服务器该槽是 B→不开火、不扣 B；持续 0.5 s 后只向该客户端更正一次；更正后能打 B；活动槽不一致先不开火，持续 0.5 s 后服务器采纳客户端所指的槽并通知其他客户端，随后该槽的枪可用；为 A 排队的按键在切到 B 后丢弃；新枪第二发仍带模板值被接受（合法映射），改写窗口过后不再接受（11 项）。**未覆盖**：玲兰自动补位的真实触发（只模拟了“服务器端该槽被换成另一把枪”） | — | 待用户 |
| C10 | W4 | offline-pass：短按一次；重复消息零新增；按住后客户端沉默→约 0.92 s 内停火（10 发，余 19 发），恢复后按当前状态继续、不补打；C4 下包键与手雷按住同一租期（6 项）。**未覆盖**：真实链路抖动下 1.0 s 租期是否偏紧 | — | 待用户 |
| C11 | W5 | offline-pass（部分）：枪移到别的槽后射击不新分配编号、记录不变、不发库存（1 项）。玩家→箱→掉落→伙伴→玩家并保存两次的完整链本轮未单独跑；保存格式与编号规则未改，既有保存/迁移回归在管线默认套件中仍通过 | — | 待用户 |
| C12 | W5 | offline-pass：协议 4 的客户端被拒绝并说明“CS 网络协议 …/4 与服务器 …/5 不同”，拒绝后不发输入，当前协议可再次被接受（3 项）；Full/Lite/探员的身份组合沿用 `NetIdentityRegression`（管线内通过）。握手传输 28 项×2（有/无 CompatNet） | — | 待用户（两端都换成本候选） |
| C13 | A1/A2 | offline-pass：客户端下包时房主本机播放启动音、每次按键音、放置音各一次，服务器向其他客户端各发一次且排除下包者；房主下包时本机一次并通知所有客户端；下包客户端自己播放一次且不发声音消息；取消后不再播放/发送，重新下包从启动音开始并完成（8 项） | diagnosed（mpb 房主不播放；旧核心没有 `HeardHere`，同一检查在旧包上报告“older core”） | **待用户听音**（计数不证明听感、距离衰减） |
| C14 | A2 | offline-pass：本机无法播放任何声音时下包仍完成、其他客户端仍被通知、倒计时继续（1 项）；客户端沉默时下包被取消、不生成炸弹、不扣物品（1 项）。**未跑**：爆炸本身在夹具中不执行（粒子需要游戏内容），其声音走同一受保护的播放函数 | — | 待用户 |
| S01 | I1 | offline-pass（我方部分）＋**已知缺口**：玲兰两个堆叠选项开启后，CS 枪方块字段被改成 4，但方块自己回答 1，玩家背包/原版库存容量为 1；玲兰自有存储仍为 4（提供方可控，CS 无法否决，见提供方说明 §1.1）；能力状态如实记录（4 项） | baseline-reproduced（1→4，探针）；旧核心无 `GetMaxStacking` 覆盖 | 待用户 |
| S02 | I1 | offline-pass（拒绝策略）：同记录两把叠在一格→以“叠放”拒绝并提示、原样保留；分开后第二把首次使用获得自己的记录，原件编号/弹药/击杀不变；编号用尽时拒绝并原样保留；旧存档里叠着的新枪整理时分到空格或原样保留（5 项）。未实现自动拆分（设计 §10.6） | — | 待用户 |
| S03 | I2 | offline-pass：玩家＋6 个人箱一个持有者、换背包后映射跟随、同步箱多代理一个持有者、换频道后退款不跟随（`sushi-inventory` 18 项、`sushi-sync` 26 项，候选核心＋用户实际 DLL） | baseline-pass（6 代理） | 待用户 |
| S04 | I2 | **not-run / 提供方缺口**：个人箱在进程内绑定唯一 miner，多人下不能保证 A/B 各自背包（提供方说明 §1.2，代码阅读结论）。CS 不猜操作者 | 静态风险 | blocked-input（目标联机玲兰包） |
| S05 | I3 | offline-pass（部分）：玲兰的一键存入、整理、取出、同名存入对两把同名不同记录的枪保持完整物品值与数量、不分配编号；平铺拒绝不同的枪；换弹途中手上被换成另一把枪→不扣弹匣、两条记录都不变（4 项）。**未覆盖**：连发途中的补位、界面触发路径 | — | 待用户 |
| S06 | I3 | **not-run**：机器吸物、死亡保物、重生重连需要世界、掉落物与实体，离线夹具未搭建；同伴库存转交未跑 | — | blocked-input（目标联机玲兰包）/待用户 |
| S07 | I4 | 提供方缺口已整理（提供方说明 §1.3）；CS 侧“同号重建不继承旧退款”“欠条跨两次保存仍围住重用频道”在 `sushi-sync` 中通过 | baseline-reproduced（Save 异常；未找到调用点） | 不适用（第三方） |
| S08 | I5 | 能力记录 `ScSushiCompatibility`（离线读取为：两个映射已核对、堆叠被放大）；平台桥范围整理在提供方说明 §2.2；Android 无桥 | 静态能力缺口 | blocked-input（目标联机玲兰包） |
| X01 | 补充 | **未定位**。日志：房主（Android）02:05:22–02:17:24 共 8 条 “Gun hit pose unavailable; narrow body-only fallback: controller pose is not placed at logical body”；客户端日志无。含义：该目标该帧只按物理身体盒判定（无头部/肢体分区），不会接受错误命中。本轮只把模型路由、实体、是否玩家、姿态盒与身体盒写进这条警告（每个模型路由一次），下一次实机日志即可看出是哪类实体 | — | 待下一份房主日志 |
| X02 | 补充 | **未定位**。日志：区块 (-7,3)、(-7,4)、(-12,15)（三次）、(-3,7) 在 6 轮列修补后仍与玩家 1 不一致。本轮只在放弃时追加：仍不同的列数与列坐标、与上一轮相同列集合的轮数（相同→修补在该客户端不生效或被改回；不同→该处地形在持续变化）。没有增加重试次数 | — | 待下一份房主日志 |

## 4. 平台/角色覆盖（实机，全部待用户）

核心矩阵：Android Host→Windows Client；Windows Host→Android Client；额外第二观察客户端；晚加入/重连。C01–C03 覆盖本地第一人称、别人第三人称、快捷栏；C13 覆盖所有听者，不用下包者录屏声轨推断房主可听。

库存矩阵：创造快捷栏槽0/5/9＋生存库存；原皮模板、皮肤模板、已有记录、计数/成长、充能；直接移动与真实复制分开。单机受影响行为使用1.9.3.1，不能用MP单机代替。

玲兰矩阵：先对给定两份单机包证明接口语义（本轮离线已做）；目标联机包到手再验证实际版本和桥。

用户要求自己实机测试，本轮 agent 未启动游戏。最短测试步骤与需要采集的信息在规划入口 §9。

## 5. 必要诊断字段与对照

每个失败用例采集：构建身份、world/session、服务器解析的玩家身份、库存真实身份/代次、slot index、old/new item value、record ID/revision、权威弹药/耐久、预测pending与确认水位、动作选择序号、消息发/收/应用/丢弃原因。

本候选在联机会话内把输入被判过期、租期到期、更正库存、下包键/投掷过期写入内存跟踪（`ScNet.Trace`，最近 64 条，不写日志文件）；写日志文件的只有：协议拒绝、记录索取 5 次无应答、平台库存复制不可用、存储无法由平台寻址（每个存储一次）、`[CS_SUSHI]` 能力行（每个世界一次）。HUD截图不能证明服务器扣弹；收到记录也不能证明槽位绑定；库存界面看见枪不能证明存档保真。

## 6. 执行记录

| Run | 覆盖 | 输入身份 | runner 与平台 | 结果 | 证据 | 限制 |
|---|---|---|---|---|---|---|
| R0 2026-10-02 | 任务 1.1/1.2 接手核对 | output 为 mpb：全量 `dc714993…`、轻量 `1dd379bd…`、探员 `d0a49c56…`；mpb 轻量核心 `580e387f…`、适配器 `c90d84e4…`；SushiBase `55d1a7c3…`、SushiTool `78c6984f…` | `win-worker health`、Syncthing REST、worker 读取 Windows 探针目录 | worker 空闲、双端 idle、无冲突文件；哈希与审计报告一致 | 本表 | 无 |
| R1 2026-10-02 | 旧包基线（任务 1.4） | mpb 轻量核心＋适配器（同上） | `NetLoopCheck --baseline`：VPS，用户实际 Windows 平台 DLL（Survivalcraft `7856ec63…` / Multiplayer `aa6136c5…`）；另在管线内对 output 中的 mpb 轻量包、管线固定的 1.9.3.2 引用再跑一次 | 9 项中 **7 项失败**：C01 T1（A、B 的槽）、T2；C03 T3 三项；C07 T4。两次结果相同 | `mpc3-offline-results.json` 的 `netstate-delivered` | 这是旧错误的复现，不是任何通过结果 |
| R2 2026-10-02 | 首轮管线 mpc1（构建＋netloop＋appnet＋c4） | 当时源码 | Windows worker，job `ea4e3fc6fe534aa7b1a349f3a2d3b154` | c4 步骤失败 1 项：`c4-net-sound/no audio` 夹具跑到爆炸时粒子系统需要游戏内容（夹具问题，非产品）；其余通过 | 阶段日志（已保留） | 该项改为只验证倒计时继续 |
| R3 2026-10-02 | 管线 mpc2（all） | 当时源码 | job `7fa2a5d9092547e48b0163bdafa3d63d` | failed steps []（netstate 81/81） | 阶段日志（已保留） | 之后补了“采纳客户端活动槽”“显示中的换弹不被分配打断”，由 mpc3 取代，未交付 |
| R4 2026-10-02 | **候选 mpc3**：管线 all | 全量 `e15a6052190a2e420f06876a60d46f3b9611cb7522ee8012a6cab3604edbca37`（526,889,657 B）；轻量 `41674d3ab7e92ed951c119a6b80e6ff093c0565ff196711494f143d50b83eeb9`（35,732,086 B）；探员 `9c09b521418fb34b883d0b7154674c412a31beb8711fbf6af996e9a724d86709`（38,467,933 B）；轻量核心 `ca054737…`，适配器 `1c67d2c4…`（57,344 B），CompatNet 部分 `ca40acbc…`；玩法身份 `76bb41956ac9b1092034e028ef835e10` | Windows worker，request `mpc-pipeline-mpc3-all-01`，job `e782e810e54940e189aa903003b8fa26`；管线固定的 1.9.3.2 引用与 1.9.3.1 引用；玲兰为用户 1.9.3.1 Mods 中的两份包 | failed steps []：netloop 28/28×2；netstate 83/83；目标断言 9/9；appnet 19/19×2；c4 140/140×2（c4 73、c4-net-sound 10、sushi-inventory 18、sushi-sync 26、sushi-stacking 13）；main-full 14084、main-lite 14394，0 失败且相对基线无新增失败；ai 66×2；vf 292×2；tactical-full-suite 4 项既有已知失败；throw 0 失败；motion/hotspots/ui 通过 | `mpc3-offline-results.json`；阶段 `.tmp/completion-140-20260929/mpc3` 的 logs 与 JSON | 全部离线；family 兼容矩阵（`compat` 步骤）未重跑：本轮未改存档布局、枪表 schema 或物品编码 |
| R5 2026-10-02 | 候选包内的核心＋适配器 × 用户实际平台 | 轻量包内 `ScCsgoKnives.dll` `ca054737…`、`Net/ScCsgoNet.bin` `1c67d2c4…`；平台 Survivalcraft `7856ec63…`、Multiplayer `aa6136c5…`、CompatNet `b0330dd8…` | VPS，`NetLoopCheck` | 传输 28/28×2、状态 83/83、目标断言 9/9 | `mpc3-offline-results.json` 的 `vpsOnUsersActualPlatform` | Android 版引擎程序集不在手（日志显示其 Multiplayer 模块与 Windows 相同 `448d2b2c`，引擎模块不同）；未在其上运行 |
| R6 2026-10-02 | 交付与清理（任务 7.2/7.6） | 同 R4 | Windows worker：交付 job `f45599588f87416aabf671890dab013f`，清理 job `bed8981148cd404b861537692e43df1f` | 三个候选以移动方式进入 `output/`（各一份），回读哈希一致，manifest 标注 CANDIDATE；清理本任务可再生中间件 7,473,581,947 B，output 未变 | `output/release-1.4.0/manifest.json`；`docs/tasks/mp-state-consistency-20261002-cleanup.json` | 未安装到 Mods 或设备 |
| R7 2026-10-02 | 第二轮接手核对（任务 8–11 开始前） | output 为 mpc3（同 R4）；远古 zip `e88b68f6…`、DLL `f9fe4ccf…`；Windows 探针目录 `mp-feedback-111155-20261002`、`ancient-travel-20261002` | `win-worker health`、Syncthing REST（completion 100、idle、无错误）、无冲突文件、无 `.ai-collab/OWNER`；Windows 磁盘 E: 11.7% 可用、游戏未运行 | 可写；规划者的诊断与探针为只读输入 | 本表 | 无 |
| R8 2026-10-02 | mpc3 基线复现（任务 8.1/8.3/8.4） | mpc3 轻量核心 `ca054737…`＋适配器 `1c67d2c4…` | VPS：`NetLoopCheck --baseline` 与 `--gunloop`，用户实际 Windows 平台 DLL；管线内对 output 轻量包再跑一次（固定的 1.9.3.2 引用） | 目标断言 **3/12 失败**（T5 编号 42 两义且处理器被替换、T6 对空射击收到命中消息、T7 确认不结算）；状态机回环 **31/45 失败**（对空 12 发收到 12 条命中并画出标记、读数回跳、换弹后仍为服务器剩余弹量、服务器对换弹无任何答复） | `mpd2-offline-results.json` 的 `netstate-delivered`、`netgunloop-delivered` | 这是旧错误的复现，不是任何通过结果 |
| R9 2026-10-02 | 首轮管线 mpd1（all＋compat） | 当时源码 | Windows worker，job `c5db0c9acd8948a9bd2cc7b6d866eb95` 与其后的 compat job | **失败两处**：`gates`：`combat/held-counter-template-wired-before-gun-routing`（按 IL 检查 `Update` 的调用顺序；逐玩家更新已拆成 `UpdatePlayer`，检查未跟随——检查问题，非产品）；`compat`：`build-tool-InventoryCheck` 编译失败（`SushiInventoryRegression.cs` 引用 mpc3 轮新增的 `SushiStackingRegression`，该工具工程未链接此文件；mpc3 轮未重跑 compat 所以当时未暴露）。其余步骤通过 | 阶段 `mpd1` 的 logs（保留） | 两处修正后由 mpd2 取代，未交付 |
| R10 2026-10-02 | **候选 mpd2**：管线 all＋compat | 全量 `7239fc30774781322e1103016388c924efbaeb77dbc9780986d32d2aca631fbc`（526,906,594 B）；轻量 `72de5c429768fdd3ec54ebf5df61e64b8cb8adae80182f2c578b656d4cb27109`（35,749,845 B）；探员 `a1b7602d07ab23fd022da43ab31c5536c88fd0e512e5432507e8b38980ad8a25`（38,467,945 B）；轻量核心 `0be510de…`，适配器 `3e7fd306…`（57,344 B），CompatNet 部分 `ca40acbc…`；玩法身份 `08400e52af609988ea03eb27183c1d15` | Windows worker，request `mpd-pipeline-mpd2-all-01`（job `4b3e99aaa55a4f0a96e718cacf3f1a58`）与 `mpd-pipeline-mpd2-compat-01`（job `fa1014e3e8314510932d7dbef1bd9398`）；固定的 1.9.3.2 与 1.9.3.1 引用；玲兰为用户 1.9.3.1 Mods 中的两份包；远古为用户 zip | failed steps []：netloop 28/28、28/28；netstate 109/109；目标断言 12/12；状态机回环 45/45；appnet 19/19×2；c4 140/140×2；main-full 14104、main-lite 14414，相对基线无新增失败，item-travel 20 项全过（含远古实际 DLL）；ai 67/67×2；vf 292/292×2；tactical-full-suite 110 项中 4 项既有已知失败；family 兼容矩阵与其余 compat 作业通过；throw 0 失败；motion/hotspots/ui 通过 | `mpd2-offline-results.json`；阶段 `.tmp/completion-140-20260929/mpd2` 的 logs 与 JSON | 全部离线，没有启动游戏 |
| R11 2026-10-02 | 候选包内的核心＋适配器＋探员程序集 × 用户实际平台 | 轻量包内 `ScCsgoKnives.dll` `0be510de…`、`Net/ScCsgoNet.bin` `3e7fd306…`、探员 `ScCsgoTactical.dll` `16d66ebe…`；平台 Survivalcraft `7856ec63…`、Multiplayer `aa6136c5…`、CompatNet `b0330dd8…` | VPS，`NetLoopCheck` 各模式 | 传输 28/28×2、状态用例 109/109、目标断言 12/12、状态机回环 45/45（探员程序集按其加载器注册） | `mpd2-offline-results.json` 的 `vpsOnUsersActualPlatform` | Android 版引擎程序集不在手，未在其上运行 |
| R12 2026-10-02 | 交付与清理（任务 11.2） | 同 R10 | Windows worker：交付 job `e5db627794e64a1bb9d400631950a1bb`，清理 job `490989c884d242258a6c50da9dbddc52` | 三个候选以移动方式进入 `output/`（各一份），回读哈希一致，manifest 标注 CANDIDATE，mpc3 进入 history；清理本轮可再生中间件 5,523,314,208 B，output 未变 | `output/release-1.4.0/manifest.json`；`docs/tasks/mpc3-feedback-subworld-20261002-cleanup.json` | 未安装到 Mods 或设备 |
| R13 2026-10-02 | mpd2 残余一发：在已交付构建上复现（任务 12.1/12.3） | mpd2 轻量核心 `0be510de…`＋适配器 `3e7fd306…` | VPS：扩展后的 `NetLoopCheck --gunloop`（两端各自的帧时钟），用户实际 Windows 平台 DLL（Survivalcraft MVID `ca4dabc9…` / Multiplayer `448d2b2c…`）；管线内对 output 轻量包再跑一次（固定的 1.9.3.2 引用） | **32/95 失败**，两次相同：G10 20 项、G14 4、G15 5、G12 2、G11 1；逐场景见 §11.1 | `mpe1-offline-results.json` 的 `netgunloop-delivered` 与 `vps-delivered-mpd2-on-user-platform` | 模拟帧时钟，非真实引擎帧 |
| R14 2026-10-02 | 对照实验：只改时间轴 | 临时副本（未进入任何包） | VPS，同上 | **28/95 失败**（100/50 下 15 次短连射仍 3 次回升） | 同文件 `vps-experiment-schedule-only` | 仅用于说明取舍 |
| R15 2026-10-02 | **候选 mpe1**：管线 all＋compat | 全量 `46befae3d0ef39a669c1f14cda471abf3821000ce70c0dea89777876cbc11dcb`（526,907,327 B）；轻量 `ee3e38eb47dcbd6dfd8f2c9e0a5d35539131eb0c872ca2223fdc537a84689440`（35,750,517 B）；探员 `8443c77b7c304b316c48de99924e8c94ba8edfb8b5df84b18c0ca27ee20543ba`（38,467,957 B）；轻量核心 `4e31ef27…`，适配器 `3e7fd306…`（与 mpd2 相同），玩法标识 `50e65beb38b074c66696ff6563e1adc4` | Windows worker：job `24bc5a5c76e1480690a08fecebfff6d4`（all）、`32bda87c83d74210a5ec659485e50ad8`（compat）；无游戏进程 | failed steps `[]`：netloop 28/28×2、netstate 109/109、目标断言 12/12、gunloop 95/95、appnet 19/19×2、c4 140/140×2、main-full 14118 / main-lite 14428 无失败（shot-cadence 14 项、item-travel 20 项全过）、ai 67/67×2、vf 292/292×2、tactical 110 项仅 4 个既有已知失败、family 246/246 与其余兼容作业、throw/motion/hotspots/ui | `mpe1-offline-results.json`；阶段 `.tmp/completion-140-20260929/mpe1` 的日志与报告（已保留） | 离线；模拟帧时钟与有序延迟 |
| R16 2026-10-02 | 候选包内的核心＋适配器＋探员程序集 × 用户实际平台 | 轻量包内 `ScCsgoKnives.dll` `4e31ef27…`、`Net/ScCsgoNet.bin` `3e7fd306…`、探员 `ScCsgoTactical.dll` `c4153adb…`；平台 Survivalcraft `ca4dabc9…`、Multiplayer `448d2b2c…` | VPS，`NetLoopCheck` 各模式 | 传输 28/28×2、状态用例 109/109、目标断言 12/12、状态机 95/95 | 同文件 `vps-packaged-on-user-platform` | Android 版引擎程序集不在手 |
| R17 2026-10-02 | 交付与清理（任务 12.4） | 同 R15 | Windows worker：交付 `mpe1-deliver-do-01`，回读 job `90c7ff7c9d1d4fa8a9c21225f05a4a75`，清理 job `a773cba26a2e41bfb76e92c01980221f` | 三个候选以移动方式进入 `output/`（各一份），回读哈希一致，manifest 标注 CANDIDATE，mpd2 进入 history；清理本轮可再生中间件 2,541,804,399 B，output 未变 | `output/release-1.4.0/manifest.json`；`docs/tasks/mpd2-ammo-jitter-20261002-cleanup.json` | 未安装到 Mods/手机 |
| — | 用户实机（任务 7.4、11.3） | mpd2，双端包哈希已核对 | Redmi Note 9 Pro Host→Windows Client | 用户反馈“大部分没问题”；视频确认弹药仍偶发回升一发，**runtime-fail** | [14:42 诊断](../../../docs/tasks/mpd2-ammo-jitter-20261002.md) | 其他场景未逐项确认；未由 agent 启动游戏；mpc3 的 11:11 视频保留为历史证据 |
| R18 2026-10-02 | output 由 mpe1 换为 **候选 mpf1**（另一工作流 quick-throw-20261002：投掷物松键即投；本变更的枪械代码未再改动） | 全量 `37cda147612dec3b0d39065936b636798ff7280a4c8d78c93b1b1f21b6a91579`；轻量 `902cbb6fc8b172039bc1de9613ff622861da613fa2a818322a3e491f3799876e`；探员 `ad5c7b85aa4741c755690ec0f0a434d7b35adca95272041a12fcc926fbfda174`；轻量核心 `730ac1d9…`，适配器 `3e7fd306…`（未变），玩法标识 `7ce76d15524341ee350c35a39aafa0d5` | Windows worker：job `2efaa890813948fe92cb0f5cc3a14b39`（all）、`850a3b4a7f7b45fabb8457fed7828a63`（compat）；VPS：包内成品×用户实际平台 | failed steps `[]`；netstate 109/109、目标断言 12/12、gunloop 122/122（其中本变更的 G01–G16 与 setup 共 95 项全过，另 27 项为投掷）、shot-cadence 14/14、family 246/246；包内成品在用户平台上同为 122/122 | `docs/tasks/quick-throw-20261002.md` §7、`quick-throw-20261002-offline-results.json` | 离线；本变更的实机项不变 |
| — | 用户实机（任务 12.5） | mpf1（含 mpe1 的修复） | 待用户 | **pending**：短连射停火后读数是否仍回升；高帧率客户端＋低帧率房主；Android；单机射速手感 | — | agent 未启动游戏 |
| — | 远古世界游戏内带枪穿越（任务 10.8） | 远古 0.41.16 未接入 | — | **blocked-provider** | — | 需要打了接入补丁的远古包 |
| — | 目标联机玲兰包（任务 7.5） | 未取得 | — | **blocked-input** | — | 取得实际包后核对哈希/接口再验 |

失败保留，不删失败日志只留最后绿结果。代码改动后标记哪些历史结果失效，只复跑受影响范围和最终适用门禁。

## 7. 未运行与已知缺口（汇总）

- 任何实机项：两个主客方向、第二观察者、晚加入/重连、听音、持枪动作与涂装目视。
- Android 引擎程序集上的离线运行（不在手）。
- 实体尚未在客户端生成时到达的库存包：平台按实体 ID 解析，找不到即丢弃，之后由实体创建时的数据带来当前槽位；这条平台路径没有离线运行。
- 晚加入：依赖平台的工程快照加握手后的全量记录（mpb 已有路径），本轮未单独离线运行。
- 玲兰：自有容器内的堆叠（提供方可控）；个人箱多人归属；机器吸物、死亡保物、同伴库存转交；界面触发的整理/存取路径；任何多人下的玲兰行为。
- 生存模式新枪第一次换弹时客户端显示的换弹不被打断这一点只有单元级检查（`net-client`），没有整条状态机的离线运行。
- X01、X02 未定位，只增加了诊断信息。
- OpenSpec change 未 archive；任务 5.6、7.4、7.5 保持未勾选。
- **mpd2 轮未完成项**：用户已提供 14:42 实机视频与日志，弹药一发回升仍失败；其余实机场景未逐项确认。远古游戏内旅行（blocked-provider）；真实链路抖动下的确认与换弹时序；帧调度造成的预测/权威节奏差异（§11 已在约 50 FPS 的房主条件复现，不能限于“帧率低于射速”）；烟雾遮挡下的共同报复没有单独用例；生存模式换弹的弹药消耗不在状态机回环里（创造模式夹具）；1.0/1.2 兼容修订包未重建。任务 10.8、11.3、12.2–12.4 保持未勾选。

- **mpe1 轮未完成项**：任务 12.5（用户实机）。离线未覆盖：真实链路与 Android 引擎帧调度；切枪同一帧显示的一发与服务器停顿超过 125 ms 时的最后一发仍按拒绝回滚（有离线记录，属预期）；50 ms / 125 ms 为候选取值。

## 8. 规划结构校验

（mpd2 轮：design 追加 §12、tasks 更新 §8–§11 的复选框、本文件更新 §10 与 §6；proposal 与 specs 未改。校验结果：2026-10-02 在 Windows 项目根经 `tools/dev.ps1 npx --yes @fission-ai/openspec@1.14.0 validate weapon-state-consistency --strict --no-interactive` 执行，worker job `e948a4e41cb14b469fcf1b2aad267c87`：exit 0，`Change 'weapon-state-consistency' is valid`，status 4/4。只证明结构合法；change 未 archive）

CLI：`@fission-ai/openspec@1.14.0`，`validate weapon-state-consistency --strict --no-interactive`。规划阶段（Windows）结果为 valid；本轮只在 design 追加了 §10、更新了 tasks 复选框与本文件，没有改 proposal 或 specs。追加后于 2026-10-02 在 Windows 项目根经 `tools/dev.ps1 npx --yes @fission-ai/openspec@1.14.0` 再次执行（worker job `5c5ce3e043814aa1ae58df89c173007a`）：exit 0，输出 `Change 'weapon-state-consistency' is valid`；`status` 显示 4/4 规划产物存在。它只证明结构合法，不表示任何产品任务完成；change 未 archive。

## 11. mpd2 14:42 实机残余与离线反证（Windows诊断轮）

完整输入身份、采样时刻、轨迹与实施建议：[mpd2-ammo-jitter-20261002.md](../../../docs/tasks/mpd2-ammo-jitter-20261002.md)。两端轻量包 `72de5c42…`、探员包 `a1b7602d…` 与 output 一致，协议 6 握手成功。

- **runtime-fail**：用户视频约 0.9–1.0 秒 `18→19`、4.2–4.3 秒 `8→9`；两次均在短连射后的收回阶段。未收集服务器逐发轨迹，不能断言每次的精确网络/帧耗时触发条件。
- **baseline-reproduced**：实际候选核心 `0be510de…`、适配器 `3e7fd306…`＋用户 Windows 平台 MVID `ca4dabc9…` / `448d2b2c…`，生产核心＋探员注册 33 handler。临时 NetLoopCheck 副本两端运行真实 Update、三轮短连射。60/60 FPS 固定每向 50/250ms 均不回升；100/50 FPS 固定每向 50ms 两次回升一发，不加模拟传输延迟也两次；60/60 FPS 加有序抖动一次。服务器 Skipped 结算到达时预测减少，权威记录没有加弹。
- 8 项诊断检查完成、exit 0，只证明夹具完成及最终 HUD 与服务器一致，**不是产品回跳修复通过**。最终结果：Windows-only `.tmp/dev-temp/mp-feedback-144217-20261002/diagnostic-user.json` 与 `diagnostic-user.trace.json`；runner build 0 警告/0 错误。预览平台的首遍诊断结果相同，最终结论以上述实际用户平台复核为准。
- 本次无产品修改、候选替换、安装或游戏启动；帧率为模拟值，非 Android 引擎执行，不证明用户视频的精确逐发原因。正式 GunLoop 需要扩展到不整齐主客节奏、输入相位与有序抖动；原 45/45 不覆盖这些残余场景。
- 诊断文档更新后结构校验：2026-10-02 经 `tools/dev.ps1`、bundled Node、缓存中已核对版本的 `@fission-ai/openspec@1.14.0/bin/openspec.js` 执行 `validate weapon-state-consistency --strict --no-interactive`，进程设置 `OPENSPEC_TELEMETRY=0`；exit 0，输出 `Change 'weapon-state-consistency' is valid`。没有安装 CLI 或修改 proposal/specs；不表示产品修复通过。

### 11.1 VPS 实施轮结果（2026-10-02，候选 mpe1；任务 12.2–12.4）

状态：**offline-pass / runtime-pending**。上面的诊断条目保留为修复前记录；诊断 runner 的 exit 0 不计入下面任何一项。

**检查方法（正式 GunLoop 的扩展，`tools/NetLoopCheck/GunCadence.cs`，用例 G10–G16）**：客户端与服务器各有自己的帧时钟（帧长函数可为定值、±35 % 波动或周期性停顿），在同一条时间线上交替执行真实的 `SubsystemScGunBlockBehavior.Update`，消息经真实平台包按“有序、每向延迟函数”投递（定值、周期性成批、有序随机）。每次连射记录：客户端每发显示时刻、上报时刻、服务器每发执行时刻、Skipped 及其时刻、弹药读数的每一次上升、结束时两端弹量与未结算数。断言不是“最终弹量相等”，而是：显示的发数＝执行的发数、Skipped＝0、读数上升 0 次、每发在其上报到达后 125 ms 内执行、任意 n 发用时不少于 (n−1)×间隔−125 ms。13 组节奏：100/50（50 ms、无延迟、成批 +90 ms）、60/50、60/60（50 ms、250 ms、成批 +150 ms）、60/30、144/50、50/100、±35 % 帧耗时（约 100/50）、±35 %＋20–120 ms 有序随机延迟（约 60/50）、房主每 23 帧停顿 120 ms；每组 15 次连射、12 个松手相位。

| 项 | mpd2（R13） | mpe1（R15/R16） |
|---|---|---|
| G10 短连射×13 组节奏（26 项） | 20 项失败；例 100/50＋50 ms：显示 63、执行 57、Skipped 6、6 次回升；显示于 [0.010 0.120 0.230 0.340 0.450 0.560]，执行于 [0.080 0.200 0.320 0.440 0.560]，0.680 Skipped，读数 24→25。60/60 定延迟 50/250 ms 通过（与 Windows 诊断一致）。50/100：服务器多打（显示 57、执行 63） | 26/26；各组显示＝执行、Skipped 0、回升 0；上报→执行等待：定延迟 0 ms，成批到达 ≤ 33.3 ms |
| G11 虚报与超速（2 项） | 1 项不成立（旧构建不按上报执行：一次报 5 发执行 0，属旧语义，不是故障复现） | 通过：一次报 5 发只执行 3 发（时间轴＋125 ms 允许的），其余 Skipped，读数按确认回滚；按住时双倍速率上报不超过射速 |
| G12 R8 落锤前/同时/之后松手×4 组（4 项） | 2 项失败（显示 1、执行 0、回升；以及未显示却执行） | 4/4 |
| G13 FAMAS/Glock 三连发（6 项） | 通过 | 通过 |
| G14 沙鹰贴着射速点射×4 组（4 项） | 4 项失败（显示 6、执行 4、回升 2 次；或多执行 1 发） | 4/4 |
| G15 连射中切枪、开菜单（6 项） | 5 项失败 | 6/6；非稳态节奏下“切枪同一帧显示的一发”允许被拒绝并回滚（记录在案），不会从另一把枪打出 |
| G16 连射中断连、重新接入后再连射（2 项） | 通过 | 通过 |
| G01–G09 原有 45 项（首发、换弹请求/答复、打空、迟到确认、切枪、延迟） | 通过 | 通过 |
| 合计 | **32/95 失败** | **95/95** |

**单机节奏（`tools/PackageCheck/ShotCadenceRegression.cs`，主套件内 14 项，1.9.3.1 引用，逐帧驱动真实 `UpdateGun`）**

| 项 | mpd2 | mpe1 |
|---|---|---|
| 按住 AK 2.55 s：20 / 30 / 60 FPS | 26 发，100 ms | 同 |
| 50 FPS | **22 发，120 ms** | 26 发，100–120 ms，均值 100.8 |
| 100 FPS | **24 发，110 ms** | 26 发，100–110 ms，均值 100.4 |
| 144 FPS | **104 ms** | 26 发，97.2–104.2 ms，均值 100.3 |
| 约 60 FPS、帧耗时 ±35 % | **24 发，均值 110.3 ms** | 26 发，均值 100.7 ms |
| 5 / 8 / 12 FPS（不补射，每帧至多 1 发） | 通过 | 通过（200 / 125 / 166.7 ms） |
| 停顿 0.86 s 后再扣（不积攒） | 通过 | 通过 |
| 沙鹰每 150 ms 点一次（标称 225 ms） | 通过（300 ms） | 通过（300 ms） |
| FAMAS 按住三连发轮间（标称 550 ms） | **560 ms** | 550–560 ms，均值 553.3 |
| Glock 三连发，每 90 ms 一按（标称 500 ms） | 540 ms | 540 ms |

结论按事实陈述：单机按住连射的间隔从“向上取整到帧”变为武器标称间隔，50/100/144 FPS 下射速比 mpd2 高（最多约 20 %，即回到标称值）；这不是自动等同于“正确”，手感需用户确认。R8（`r8-input/`）、菜单动作、换弹与持枪处理（GunHandling 187 项）等既有单机回归未改断言并通过。

**限制**：没有游戏进程运行；帧时钟与网络为模拟；Android 引擎程序集不在手；真实链路的乱序/丢包重传时序由平台可靠通道保证有序，这里只模拟有序延迟。切枪同帧的一发与超过 125 ms 的服务器停顿属于仍会回滚的已知边界。

**规划结构校验**：本轮 design 追加 §13（并把 §12.3 末条标为历史）、tasks 勾选 12.2–12.4 并新增 12.5、本文件追加 §6 的 R13–R17、§7 一条与本小节；proposal 与 specs 未改。2026-10-02 在 Windows 项目根经 `tools/dev.ps1 npx --yes @fission-ai/openspec@1.14.0 validate weapon-state-consistency --strict --no-interactive`（`OPENSPEC_TELEMETRY=0`，job `45254e2712ed49a785e39d4ac0fb5217`）：exit 0，`Change 'weapon-state-consistency' is valid`。未 archive。

