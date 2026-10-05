# 代码质量整改 2026-10-05

Status: review — R1/R2/R3 续修源码及当前离线检查完成；真实游戏/provider回调、跨设备MP、Android和发行兼容门禁未执行。第一轮“全闭环”的结论已被复审纠正，历史证据见后文。
Current writer: 接续聊天 01a10c09 的 Windows Codex（唯一写入者）；原聊天系统错误结束，VPS 会话空闲。

## 复审续修（2026-10-05）

- 当前分支仍为 `fix/quality-20261005`；1723 项第一轮输入哈希全部匹配。两端 Syncthing idle、0 pending/errors、1535 文件，无冲突；VPS worker 无活动任务，源码会话停在输入提示符。保留全部既有未提交改动。
- R1/R2：持久收据负责未完成到达的来源、库存所有者、逐枪进度；重载从收据恢复，不能把混合编号库存当作没有到达。保护跟随同一库存内待迁移项，跨容器/丢弃在归属尚未确定前暂缓，完成后恢复正常流转。使用/再次导出与该义务一致；不全局禁用同编号本地枪。
- R3：单机超预算地图保留原数据；host 明确报告同步受限，保持本地视图/其他发布可运行；缩减编辑后重新发布完整地图。保留网络预算及正常单机/联机行为。
- 结构：抽出共享库存互斥，移除库存替换对上层枪械事务的反向依赖；迁移完成依据收敛到收据。不在本轮重写133文件核心依赖环。
- 验收：旧DLL失败→多枪部分成功/两次序列化重载/无来源文件恢复/不重复分配或重置记录；玩家移槽/补偿换槽/容器转移门禁及解除/同编号本地枪正常；单机保存大图→host发布/加入→逐步修复→正常广播。执行证据集中于 `.tmp/dev-temp/quality-remediation-20261005/revision/`，不安装、不推送、不改原世界或正式包。

### 续修完成情况与结构证据

| 要求 | 最终实现与实际证明 |
|---|---|
| R1：部分迁移保存重载 | 收据保存原envelope、库存所有者和已改号项的身份/槽位；到达对象不再保存独立的完成标志或编号映射。两把枪部分成功后，经真实子系统Save、ValuesDictionary/XML往返、原生ComponentInventory.Save格式、加载完整性检查恢复；连续两次重载后只改剩余项，记录不重置、不再分配。来源文件缺失仍从目标世界收据恢复。 |
| R1：使用/再次离开 | 枪械事务Prepare/Commit、玩家使用、重复/成长持有者扫描、ReadyForTravel、直接Export和保存XML的Capture均尊重未完成义务。来源扩展编号不存在于目标世界时，加载校验只按所属玩家的明确收据核对导入记录，其他记录保护不放宽。 |
| R2：换槽/补偿 | 同一库存内寻找唯一待迁移值；已成功项的槽位持久记录用于排除与来源编号重叠的目标值。手动换槽、journal补偿到其他空槽、多枪部分成功加补偿欠账保存重载、重复恢复均执行通过。遇到额外同值副本不猜测，义务和保护保留。 |
| R2：容器/丢弃 | 原生移动、拖动、丢弃入口暂缓未完成库存的枪械流转，防止失去身份；材料仍可移动，完成后枪械正常流转。目标世界其他库存的同编号枪不被全局封禁。未经原生钩子的第三方直接Remove/Add跨库存行为没有可证明的身份传播能力，不声称已实机覆盖。 |
| R3：超预算地图 | 原始单机地图及持久数据完整保留；host发送有明确原因的“编辑中/暂不能联机比赛”状态，主机本地视图保留全图。禁止开始/进入不可同步地图的比赛，但允许一次次减少超长数据；修复后完整广播并清除原因。状态发送抛异常时保留dirty/加入待发状态，本地视图仍更新。 |
| 结构 | 新增ScInventoryCommit作为共享互斥叶节点；ScTravelEnvelope、ScTravelLedger独立承载传输数据和持久状态。事务/到达流程依赖它们，账本不依赖枪械事务、库存、网络或UI。没有通用恢复框架或空壳接口。 |

语义依赖图（Full、排除SelfTest，`revision/semantic.json`、`architecture.json`）：**新增直接双向依赖0组，移除3组**：ScGunMutation↔ScInventoryTransaction、ScGunMutation↔ScGunRecovery、ScGunRegistry↔ScItemTravel。最大强连通组仍为133文件；总直接边1173→1187，部分来自将数据类型移到独立文件后显式化的引用。不能用这个数字宣称整体架构已解耦；库存journal/网络通知等原有耦合仍在。234个核心文件语义绑定零错误；454个维护C#文件语法清点零错误。

### 本轮最终执行（替代第一轮的当前候选身份）

```powershell
./tools/dev.ps1 python tools/check_quality.py --group all --mp-refs .tmp/mp-m0-20260929/refs/mp --mods 'D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods' --appearance-refs .tmp/dev-temp/full-quality-20261005/appearance-refs --out .tmp/dev-temp/quality-remediation-20261005/revision/final-verified
```

退出0；1728项输入哈希运行前后不变。实际命令、退出码、耗时、TargetPath及完整SHA在 `revision/final-verified/execution.json`；逐项断言和实际加载程序集在同目录各runner JSON。语法/语义工具沿用原审查工具，但本轮实际重新运行；不是借用旧结果。

| 层次 | 最终结果 |
|---|---|
| 产品构建/条件配置 | 10/10；6/6（Full相关产品、Lite Deflate/Zstd及agents、Mini、Mini+Inspect；未打正式包） |
| QualityCheck，API 1.9.3.1 | 81/81：原F1/F4/F5/F9共26项、R1/R2共13项、现有item-travel/generic-travel共42项 |
| DeathmatchCheck，API 1.9.3.1 | 235/235；包含实际单机编辑/Save、host受控发布、逐步修复、发送故障后重试 |
| Balance / Feedback / Inventory | 5506/5506、398/398、2262/2262（含原Sushi DLL离线语义） |
| Appearance / argv | 19/19、5/5 |
| MP，实际1.9.3.2_MP程序集 | transport/compat各28；state109；gunloop122；dmloop60；quality23，全部通过。R3经实际Load、PeerAccepted和真实平台包序列化/分发完成，但仍是进程内多端夹具。 |

最终核心SHA-256 `b0bcc5f798269b51445c8ce1b13f48576020e5de760c7a14f86500819160fcb3`；Deathmatch `b3e6fecd9f9d7a55ec587bb9fc3262078802b6437160aa559e8dd5635d010ca3`。单机和MP平台输入仍分别为前轮所记录的 `2ef7c191…eb9d3` 和 `622976cc…ca504`，完整值及每个provider的身份在最终报告中。最后完整执行的编译合计49.685秒、runner合计31.062秒；初轮完整检查及更早小范围验证另见各目录账本，人工分析和环境排错总耗时未计量。

旧错误及以下中间失败报告已保留：`arrival-before.json` 26/32，6个新增场景识别旧错误；`dm-before.json` 实际Publish抛出arena-too-long；`arrival-after.json`的补偿用例初次未等待库存quiet窗口，补充第二次Step以满足生产前提后通过。接入既有ItemTravelRegression时发现其假库存Project为空、两个实体Id均0，无法证明持久所有者：同样3项在旧核心和新核心均失败（`neighbors-old-core.json`、`core-neighbors/core.json`）。修正夹具为真实Project、唯一实体Id及与生产Load相同的所有者解析，未放宽生产保护。一次R3编译将现有void SendTo当成bool，编译器拒绝后按原API调用修正；语法工具首次工程名写错，定位到SyntaxAudit.csproj后执行成功。最后另补充迁移中世界切换边界，故再次完整执行最终候选，之前`final/`结果不冒充当前结果。

### 文件边界、F项状态和剩余验收

- R1/R2生产：`World/ScTravelArrival.cs`、`ScItemTravel.cs`、新增`ScTravelLedger.cs`/`ScTravelEnvelope.cs`，以及`ScGunRegistry.cs`、`ScGunLoadIntegrity.cs`、`ScGunTravel.cs`、`ScGunHolders.cs`、`SubsystemScGunBlockBehavior.cs`、`Mod/ScCsgoKnivesModLoader.cs`的保护入口；相关hunk作为迁移状态/入口整体审查和回退。
- 事务结构：新增`World/ScInventoryCommit.cs`，配套`ScInventoryTransaction.cs`、`ScGunMutation.cs`、`ScGunRecovery.cs`及共享保存互斥的调用；`ScGunMutation.IsCommitting`兼容入口保留。该组可以独立审查，但不能只回退互斥的一端。
- R3生产：`ScCsgoDeathmatch/Net/DmNet.cs`、`Session/SubsystemScDeathmatch.cs`；网络投影、发布故障边界及编辑/入场门禁一起回退。存档地图未截断、包预算未取消、正常玩法时序及资源字节未改变。
- 回归：`tools/QualityCheck/{Program.cs,QualityCheck.csproj,ArrivalRecoveryChecks.cs}`、`tools/PackageCheck/ItemTravelRegression.cs`的夹具前提修复、`tools/DeathmatchCheck/{Program.cs,PublicationChecks.cs}`、`tools/NetLoopCheck/QualityCases.cs`。统一入口实际执行新增回归；原第三方提供方未改。
- F1：直接拒收及复审R1/R2的源码/离线场景通过；F2：原合法大图及R3通过；F3/F4/F5/F9：当前候选相关回归通过；F6/F8：原有效测试/参数回归仍通过；F7：统一本地入口实际通过，远端CI仍只是已配置、没有执行。
- 真实Windows/provider回调、跨设备多人/丢包、Android、用户观感、发行兼容矩阵仍未执行。QualityCheck明确把缺少SC_ANCIENT_PACKAGE的真实AncientWorld程序集案例标为notExecuted，未混入通过数；历史二进制重生成/ProtectedLoadCheck同样未执行。
- 新收据子组带版本并对未知/损坏内容拒绝；既有五字段完成收据、记录schema7和物品布局6保留。**旧二进制会丢弃新增到达进度，不能将未完成迁移的存档带回旧代码后宣称安全**；源码回退不恢复持久数据。正式发行前必须按现行兼容门禁验证，本轮未安装或改写任何原世界。
- 清理：`revision/cleanup-superseded.ps1`实际永久删除4个被最终候选替代的引用/变体目录，共519,244,171字节，可重建但不在回收站；完整路径、大小、最终证据前后哈希见`revision/cleanup-receipt.json`。保留旧核心复现输入、全部失败/成功JSON和日志，以及`final-verified`工作集。

## 第一轮历史记录（后续结论以上述续修为准）
User-authorized scope: 依次实施 F1、F2/F3、F4/F5/F9、F6/F8/F7，交付源码、回归与执行证据。
Allowed paths: 对应生产调用链、检查工具/CI、相关调用文档及本说明。
Out of scope: Mods/设备安装、原世界写入、第三方修改、发布/推送/正式包替换、架构重写、资源降质、无关清警告。

## 基线与协作

- Windows HEAD / 审查基线：`df6247035d245452a4fc6640532c31ccc7ceb261`；独立分支 `fix/quality-20261005`。
- 审查证据仅在 Windows `.tmp/dev-temp/full-quality-20261005/`；本轮证据放 `.tmp/dev-temp/quality-remediation-20261005/`。不把旧通过结果算成本轮验证。
- 开始时逐一计算审查 `inputs-resumed.json` 的 877 个输入 SHA-256，无差异。
- 原有未跟踪 `.claude/scheduled_tasks.lock`、`.stignore.bak-before-psh-xdb-20260928`、`exceptions.txt` 保留。
- 两端 Syncthing：idle，1524/1524 files，needTotalItems=0、pullErrors=0、errors=0；Windows 未发现 conflict 文件。VPS tmux 项目会话停在已完成上次提交的空闲提示符；worker 无运行任务，health 正常。本轮无跨端写入者交接。
- 已读 AGENTS、collaboration、compatibility、build-and-release、TASK_TEMPLATE、capacity/naming/three-version-switching 参考及 REVIEW。历史发行说明不覆盖当前 AGENTS 的 1.4.0 起双向兼容要求。

## 保留契约与验收

| 阶段 | 修复与区分错误实现的场景 | 当前结果 |
|---|---|---|
| F1 | 先复现拒收仍成功；生产随行改号正常成功、静默拒收、部分修改后抛异常、目标变化、回滚失败、重复恢复；核对物品/材料/记录，存档互斥与持久补偿 | 已修复，最终故障/相邻路径回归通过 |
| F2 | 合法128点、中文标签、确切字节上限收发，超限拒绝且旧展示不改；单机不受网络预算限制 | 已修复，单机与MP离线分别通过 |
| F3 | 正常持续输入完成拆弹；停发取消、迟到释放、断线重连及旧连接清理 | 已修复，真实MP包与实际拆弹计时器离线通过 |
| F4 | 请求结果未知不重试；退出/超时/断线释放回调，迟到响应不串新请求 | 已修复，生命周期与既有MP回归通过 |
| F5 | 离开取消未提交动作，保留已提交装填与持久义务；同编号新玩家不被误清 | 已修复，实际Update/事务回归通过 |
| F9 | host→菜单/单机/新host、旧世界重复清理，不保留旧所有者、不清新所有者 | 已修复，实际Dispose及所有者回归通过 |
| F6 | 真实格式/来源夹具、拒绝原因、零时刻轻抛与已接受即时松手规则 | 已修复，Balance/Feedback最终零失败 |
| F8 | 实际子进程argv、Unicode/空格/等号/分号、环境恢复与非零退出码 | 文档/工具已修正，5/5通过 |
| F7 | 全部维护产品、支持配置、有效行为runner；区分通过/失败/缺依赖/未执行 | 入口已实际全量执行通过；CI已配置，GitHub尚未执行 |

保留枪/刀/皮肤顺序与永久身份、弹药/耐久/成长/充能及存档格式、正常动作时序、联机权威和发行变体边界；资源字节不改。不建立自动世界备份、不重置状态、不删除功能、不弱化保护。

补充约束（同一工作流）：明确业务规则、引擎入口、网络、展示、持久化职责，只做本次所需结构整理，不新增循环依赖/重复权威状态/旁路写入/无用途接口。F1 复用既有 journal/recovery；较大架构治理只记录。每项分别说明错误路径与正常路径预期，核对实际状态及未完成义务。未知执行结果不自动重试。Git 分支不是 Syncthing 隔离，后续换分支/写入者/批量调整前重查影响。统一入口先清点现有工具，以当前维护产品/支持配置/有效 runner 为准，不运行历史安装清理发布脚本。CI 配置、实际执行、目标平台验收分开报告。各阶段自审后直接继续，不逐阶段等批准。

## 执行记录

每阶段先保留旧错误证据，修复后核对实际 diff 和相关回归，再进入下一阶段。Windows 工具均经 `./tools/dev.ps1`；构建仅 DLL 与最小必要依赖，不反复打包资源。

### 阶段一 F1

- 生产：`ScInventoryTransaction` 复用 `ScGunInventoryJournal`/`ScGunRecovery`/`ScGunMutation` 互斥，核对实际增减量、原世界/库存/持久所有者；失败按逆序回滚，无法确认原所有者时保存欠账，不写新目标。`ScTravelArrival` 仅实际完成后报成功，未完成的旧编号保持禁止使用，并允许同一映射恢复。`ScItemTravel.Complete` 拒绝事务中/欠账未结清的完成声明。
- 正常：扣料仅一次、创造成员直接替换且保持不能扣料的规则、迁移记录所有字段/原世界本地记录不改、晚到重复恢复不重新分配编号。
- 执行：`./tools/dev.ps1 dotnet run --project tools/QualityCheck/QualityCheck.csproj -c Release -- .tmp/dev-temp/quality-remediation-20261005/f1-before.json`，旧 core `F093CF9A…18893`，3/10 通过、7 失败（预期识别旧错）。
- 修复：`./tools/dev.ps1 dotnet run --project tools/QualityCheck/QualityCheck.csproj -c Release '-p:SkipScmodPackaging=true' '-p:CustomAfterMicrosoftCommonTargets=E:/projects/ScCsgoKnives/tools/quality-no-assets.targets' -- .tmp/dev-temp/quality-remediation-20261005/f1-final.json`，14/14 通过，实际加载 core `8D1E5349A4079D57EB4E1D2E6B8798AD9D8E2B36865C4E9827A6F64901545200`，API DLL `2EF7C1918EA9D09F52A807719909BF28C722BD3A2793EEAF81DB0C343B9EB9D3`。包含实际 Save 拒绝、重入拒绝、世界/所有者变化、回滚欠账保存重读和重复恢复。
- 相邻路径：`./tools/dev.ps1 dotnet run --project tools/InventoryCheck/InventoryCheck.csproj -c Release -- E:/projects/ScCsgoKnives/src/ScCsgoKnives/bin/Release/net10.0/ScCsgoKnives.dll 'D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods' E:/projects/ScCsgoKnives/.tmp/dev-temp/quality-remediation-20261005/f1-inventory.json`，2262/2262，包括实际 Sushi DLL 离线语义；不是游戏验收。
- 失败尝试保留：首构建未引用 `-v:quiet` 被 PowerShell 改成 `quiet`（F8）；下一次构建局部同名变量编译失败，已改名；`f1-after.json` 仍加载旧 DLL（hash 与 before 相同），不能作修复评价。QualityCheck 改为默认 ProjectReference，显式 CoreDll 只用于指定基线，消除默认旧 bin 路径。成功构建约 3.4s，专项约 3.5s，相邻 runner 约 13s；人工/环境排错耗时未计量。
- 自审：检查实际三个生产文件 diff，`git diff --check` 通过。持久化格式、目录/身份分配、资源/玩法参数未改变；既有 World 内事务职责被复用，没有新通用恢复系统或转发接口。回退边界是这三个生产文件及对应 TravelChecks；后续阶段共享 runner 需保留其他阶段条目。
- 最终自审追加：早期修复重新检查已完成迁移，会把正常把枪移出背包误作未完成；先为空后迟到的物品也会受旧 settled 标记影响。`f1-adjacent-before.json` 24/26、两失败，随后修正：只有实际再次恢复旧值或尚未完成时核对完成；迟到导入清除 settled；重复恢复再次拒收时收据回到未完成。最终26/26包含这两项和失败重复恢复。早期core哈希只是阶段历史，最终身份见下方。

### 阶段二 F2/F3

- 地图：`DmArenaRules.MaxNetworkBytes` 定义网络 JSON 的 48 KiB 预算（不改变存档格式、不截断标签），`DmNet` 发送/读取一致核对 UTF-8 大小，完整消息仍受原 64 KiB 限制；编辑入口在提交过长描述前拒绝。128 点、中文标签及原正常小图均完整接收，129 点/超长拒绝且旧 view 不变。
- 最终编辑门禁只应用于 `ScNet.IsHost`；在 API 1.9.3.1 增加48K个中文字符标签的单机编辑回归，保留原单机数据契约。MP附加检查实际 `TacticalDefuseClock` 正常完成/过期取消，最终协议专项18/18。
- 拆弹：`TacticalNet` 复用 `ScNetGuns.InputLease=1s` 与 `ScNet.Now`，输入绑定已接受 peer；停发失效，过期视线失效，清理只作用于匹配 peer。正常 .25s 刷新/六秒持续输入保持，迟到释放不重新按下。
- 复用 `NetLoopCheck` 的真实 MP packet/握手入口，新增 `--quality`；不是新建第二套平台仿真。基线 `f23-before.json` 捕捉长地图和停发错误。初次重连夹具只是同连接再次握手，不能证明重连；`f23/mp.json` 的残留失败由此造成，保留记录。已改为从实际连接列表移除、执行 adapter 离开处理、建立新 ClientSession，并断言 peer 身份不同，未弱化产品条件。
- 当前：`./tools/dev.ps1 python tools/check_quality.py --group mp-quality --mp-refs .tmp/mp-m0-20260929/refs/mp --out .tmp/dev-temp/quality-remediation-20261005/f23-final`；16/16 通过（含确切 48 KiB 接收边界、发送方超限拒绝、旧连接重复离开不清新输入）。完整命令/耗时/构建路径与 SHA-256 在该目录 `execution.json`，MP 依赖每个 DLL 哈希在 `references.json`，实际加载列表在 `mp.json`。
- 自审：本阶段 4 个生产文件局部 diff、`git diff --check` 通过；输入权威仍在服务端，客户端只有展示，未改变拆弹时间、奖励、视线/距离限制或原地图资源。网络预算属领域数据约束，持久化读取不被网络上限替换。真实多人/Android 尚未执行。

### 阶段三 F4/F5/F9

- `ScNetWorkbench` 保存请求所属 Project/transport/发送时刻，30s 超时、最多64待确认请求，id 在进程内不复用；响应、退出和断线均最多回调一次，回调异常不阻止其他清理。发送明确失败为未发送（-1），可能发出/服务端异常/超时/断线为结果未知（-2），不自动重试、不声称未扣料。正常本地执行和服务端成功响应保留。
- 入口：gun subsystem 帧更新/Dispose、网络 adapter 更新/新会话/退出，以及 core 无参数退出兜底；旧 Project 清理只释放旧请求，transport 被替换也结清旧回调。
- `SubsystemScGunBlockBehavior.PlayerLeft` 集中取消未完成动作、离开瞄准、清理声音安排/HUD和三个玩家索引；已提交装填不返还、世界枪记录/kill/recovery 队列不删除，真实未完成义务继续阻塞迁移。
- `ScNetMirror` 以对象身份释放 registry/wants/armor 及其发送缓存；由 gun/armor Dispose 精确调用，菜单退出兜底只清孤立所有者，旧世界重复 Dispose 不影响新世界。进程资源缓存不增删。
- 基线 `f459-before.json` 15/23 通过、8失败；修复 `./tools/dev.ps1 python tools/check_quality.py --group core --out .tmp/dev-temp/quality-remediation-20261005/f459-final` 24/24，通过实际请求正常/重复答复、超时/迟到答复/不重发、断线重加入、旧世界重复清理、容量/回调异常、离开动作及欠账、host退出/新host/单机切换。该报告实际 core `C9A8792D6441F1B9763D5F42FDA5F41E747119B8E0ED957994ACDBBAC66EAD91`。
- adapter 受影响后重跑 `check_quality.py --group mp-quality ... --out .tmp/dev-temp/quality-remediation-20261005/f459-mp`，16/16，完整输入/命令见目录账本。不是跨设备联机。
- 结构自审：没有第二份权威世界状态；请求只是有时限的回调元数据、mirror只保存发送缓存，持久义务仍归 registry。变更沿既有 Engine→Net/World 入口与既有相互依赖，没有新增层间接口；大规模依赖环治理留待独立任务。回退本阶段须一起撤回清理调用与相应入口，不能只撤回一端。`git diff --check` 通过。

### 阶段四 F6/F8/F7

- 再执行旧测试：Balance 5502/5503，Feedback 383/396，结果在 `f6-*-before.json`，不是借用历史审查。当前表改为当前布局，历史表直接读取 `tools/fixtures/balance-20260924/1.0.0.xml.gz` / `1.2.0.xml.gz`（实际发行writer的不可变输出，固定SHA-256和发行来源已在测试内核对）。拒绝测试核对具体原因及输入不变，额外保证schema7伪装layout5仍被拒绝。
- `FeedbackCheck` 用明确 committed 状态区分未发生与零时刻释放；quick两种参数都验证当前即时松手契约，增加按住不释放/零时刻只释放一次。产品投掷代码没改。
- 参数说明修正为完整引用 `'-p:...'` / `'-v:...'` 或字面参数数组，Python调度器直接传argv数组。`check_dev_arguments.ps1` 的5项检查通过。首次探针2/4失败是Python输出中文与PowerShell解码不一致，改为ASCII JSON转义后真实Unicode参数相等；原报告 `f8-arguments.json` 保留。没有在包装器内猜回已被PowerShell删除的前缀。
- 现有工具清点后扩展 `NetLoopCheck --quality`，新增 `QualityCheck` 仅覆盖本轮缺口；统一入口 `tools/check_quality.py` 调用7个既有/专项runner及argv检查，清点10个产品工程、编译6个已有支持配置。没有调用历史安装/清理/发布脚本，没有拿 `dotnet test` 或只编译主工程替代行为执行。
- `--group mp` 不提供依赖时，`final-missing-dependency-probe/execution.json` 明确 missing-dependency / not-executed，实际 `$LASTEXITCODE=2`；实际失败状态见前期 `f23/execution.json`、`final-core/execution.json`。后者补充装填夹具的Holder错误被生产保护拒绝，已改用实际 `ScGunHolders.Key`，未改产品保护。
- `.github/workflows/build.yml` 改用同一入口并保留文本证据。GitHub运行器缺少原始资源/第三方输入时将明确非成功；本轮没有推送或触发GitHub，所以只声称CI配置完成，不声称远端CI已过。

## 最终执行与输入身份

冻结后实际命令：

```powershell
./tools/dev.ps1 python tools/check_quality.py --group all --mp-refs .tmp/mp-m0-20260929/refs/mp --mods 'D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods' --appearance-refs .tmp/dev-temp/full-quality-20261005/appearance-refs --out .tmp/dev-temp/quality-remediation-20261005/final-unified
```

父入口退出0；所有纳入的可执行检查通过，独立历史DLL重生成/ProtectedLoadCheck标为未执行。最后补充的2处生产调整使早期DLL身份失效，所以刷新本次最终完整入口；没有重跑资源打包。

| 层次/runner | 最终结果 | 能证明的范围 |
|---|---|---|
| 源码/输入 | diff检查通过，1723输入哈希运行前后不变 | 含维护代码、实际内嵌数据、行为资源、历史夹具、外部编译输入；没有追认全部源码已逐行证明 |
| 产品构建 | 10/10 | 包括Tactical/Deathmatch/Appearance/Voice/Net/Compat/Bundle/Resources/Codec/Core |
| 条件配置 | 6/6 | Lite Deflate/Zstd核心及agents、Mini、Mini+Inspect；仅编译，不是发行包验收 |
| QualityCheck (1.9.3.1) | 26/26 | F1/F4/F5/F9错误与正常相邻路径、实际库存/记录/义务 |
| DeathmatchCheck (1.9.3.1) | 226/226 | 原225条规则及单机地图保留 |
| BalanceCheck (1.9.3.1) | 5506/5506 | 实际历史XML读入、格式保护原因及规则 |
| FeedbackCheck (1.9.3.1) | 398/398 | 时序/声音数据/资源结构，非听感或画面验收 |
| InventoryCheck (1.9.3.1) | 2262/2262 | 原Sushi DLL语义、事务/持久补偿等 |
| AppearanceNetCheck | 19/19 | 原NMM/NEO DLL离线行为；不改第三方 |
| MP transport / CompatNet | 28/28 / 28/28 | 实际1.9.3.2_MP平台程序集包往返 |
| MP state / gunloop / dmloop | 109/109 / 122/122 / 60/60 | 现有真实生产状态机在进程内的多端夹具 |
| MP quality | 18/18 | 大地图、边界、持续/失效拆弹、断线/重连 |
| dev.ps1 argv | 5/5 | 子进程参数、临时环境和非零退出码 |

证据主目录 `E:/projects/ScCsgoKnives/.tmp/dev-temp/quality-remediation-20261005/final-unified/`：`execution.json` 保存展开后的每条命令、退出码、耗时和构建TargetPath/SHA；`inputs.json` 保存输入哈希；`references.json` / `appearance-inputs.json` 保存依赖身份；各runner JSON保存实际加载程序集与逐项结果。InventoryCheck报告也保存实际Sushi DLL哈希。构建/条件配置合计55.487s，runner合计34.500s；哈希收集、人工检查和环境排错另计且未完整计时。

最终关键SHA-256：

| 输入/产物 | SHA-256 |
|---|---|
| Core | `823d77f814cc625d31089e2541e690cb3dc113b50aabd4c3b945dc7ab8b54544` |
| Tactical | `b4eff16ba68c7ce035084eccf10a9bb0a2bf96108d1d55fbfa9c8c704e566dfe` |
| Deathmatch | `8e2becbdb18ea931ca6c1d67f750e97bfb1a147a3d1096df95d8006b8a5cf951` |
| Net adapter | `7021a4bbba5f9cf519911f5134a5ea20e2b6a1b09f2fa68ddb53ae2cc0d61c6b` |
| 单机 Survivalcraft.dll（1.9.3.1） | `2ef7c1918ea9d09f52a807719909bf28c722bd3a2793eeaf81db0c343b9eb9d3` |
| MP Survivalcraft.dll（1.9.3.2_MP） | `622976cce1f9da248ff7e3d38144fb7383e034d555961dd1e40d6f5fe33ca504` |

## 文件边界与回退

| 项目 | 生产/工具文件与原因 | 回退边界 |
|---|---|---|
| F1 | `World/ScInventoryTransaction.cs`、`ScTravelArrival.cs`、`ScItemTravel.cs`：真实事务、待完成保护、完成核对 | 三文件相关hunk一起，配套TravelChecks；不删除已经保存的既有格式补偿/迁移收据 |
| F2 | `ScCsgoDeathmatch/Arena/DmArena.cs`、`Net/DmNet.cs`、`Session/SubsystemScDeathmatch.cs`：有限网络预算及正常单机保留 | 发送/读取/编辑条件一起，配套NetLoop/DeathmatchCheck |
| F3 | `ScCsgoTactical/TacticalNet.cs`：输入时效、peer身份及精确清理 | 同文件与对应网络用例；协议字段未改 |
| F4 | `Net/ScNetWorkbench.cs`、`ScNet.cs`、`ScCsgoNetAdapter.cs`、core ModLoader及gun subsystem入口 | 请求表与生命周期调用一起；不能只移除清理的一端 |
| F5 | `World/SubsystemScGunBlockBehavior.cs`：统一玩家离开清理 | PlayerLeft、Update、Dispose相关hunk，不能清registry义务 |
| F9 | `Net/ScNetMirror.cs`、`World/ScArmor.cs`、gun subsystem/core ModLoader入口 | 精确所有者释放与所有调用一起 |
| F6 | `tools/BalanceCheck/LoadIntegrityCheck.cs`、`FeedbackCheck/Program.cs` | 仅测试；历史夹具文件未改 |
| F8 | `tools/dev.ps1`注释、build guide、`argv_probe.py`、`check_dev_arguments.ps1` | 文档与参数回归；不需回退产品 |
| F7/回归 | `tools/check_quality.py`、`quality-no-assets.targets`、`QUALITY_CHECKS.md`、`QualityCheck/*`、`NetLoopCheck/{Program,StateLoop,QualityCases}.cs`、`DeathmatchCheck/Program.cs`、CI工作流 | runner共享文件按hunk回退，保留其他已接受用例；不回退已有工具 |

相对生产路径均在 `src/ScCsgoKnives/` 下，标明其他工程的除外。保留三个原未跟踪项；没有reset/stash/覆盖他人改动，没有提交/推送/安装/正式包替换。分支只是Git审查边界；结束前两端Syncthing仍idle、0 pending/errors（1535文件），VPS会话仍空闲，无冲突文件。

## 清理收据

实际经 `./tools/dev.ps1 pwsh -NoProfile -File .tmp/dev-temp/quality-remediation-20261005/cleanup-superseded.ps1` 清理8个已替代目录，共 **1,871,948,956 bytes**。均在本任务临时根目录，事前验证绝对路径/无reparse/无活动构建；永久删除可重建副本，非回收站恢复。精确列表/大小见同目录 `cleanup-receipt.json`：`f23/mp-refs`、`f23-final/mp-refs`、`f459-mp/mp-refs`、`final-mp/mp-refs`、`unified/mp-refs`、`unified/appearance-refs`、`unified/variants`、`final-variants/variants`。保留所有JSON/失败日志/输入哈希、原审查目录、最终 `final-unified` DLL引用/六配置工作集、源码/原资源/正式output；最终证据JSON清理前后哈希不变。以后条件配置编译不复制大资源DLL，只直接引用原输入。

## 验证边界与剩余工作

- 单机离线使用 API 1.9.3.1；MP 使用 1.9.3.2_MP 并单独记账。
- 源码检查、编译、离线行为、真实 Windows 游戏、真实多人、Android、用户观感分别报告；后四项当前均未执行。
- 不执行正式发行全矩阵/交付门禁，不据源码交付宣称正式发布可用。
- 真实Windows游戏穿越、真实设备多人连接/丢包时序、Android、用户观感均未执行；离线结果不代替这些。正式1.4.0起全发行双向保存矩阵也未执行，本轮不进入发行门禁。
- 已实现的恢复以既有provider身份/实际增减量为依据；真实第三方provider组合与引擎回调时序仍需后续独立游戏副本验收。未新增通用恢复系统或自动世界备份。
- 较大的核心依赖环治理、进程缓存/显示架构拆分不在本轮，未实施。现有无关编译警告保留。
- 回退以阶段文件 diff 为边界，不回退用户原有文件，不回退/覆盖世界状态。
