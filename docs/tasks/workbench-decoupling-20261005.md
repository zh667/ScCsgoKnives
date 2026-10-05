# 工作台请求生命周期安全解耦（第一轮）

Status: review — 本轮源码、独立新旧对照和受影响配置验证完成；停止于工作台请求管理边界。
Current writer: 本聊天 Windows Codex，唯一源码写入者；VPS会话空闲、worker无活动任务。
User-authorized scope: 2026-10-05用户要求只整理工作台网络请求生命周期并完成验证；完成本轮后停止。
Allowed paths: `src/ScCsgoKnives/Net/ScNetWorkbench.cs`、同目录内部辅助类、`tools/QualityCheck`/`tools/NetLoopCheck`对应检查和本说明。
Out of scope: 协议/调度改变、库存/迁移/存档/动作/资源改动、安装、推送、发布、原世界及正式包写入。

## 基线与写入边界

- 沿用 `fix/quality-20261005`，HEAD `ccbcfbf721abf97deb22cd680c55da45a8c90cc6`。工作区包含已接受R1/R2/R3及此前F项修改，不切分支、不reset/stash，不提交基线或本轮修改。
- 先读AGENTS.md、collaboration/build-and-release指南、`quality-followup-review-20261005/REVIEW.md`。该报告的正式发行兼容缺口属于原基线，本轮不处理或宣称解决。
- 两端Syncthing idle、0 pending/errors、1540文件，无冲突。VPS源码会话停在输入提示符；未向另一聊天/agent派发修改。
- `.tmp/dev-temp/workbench-decoupling-20261005/baseline/identity.json`记录HEAD、分支、全部已脏/未跟踪路径、候选范围文件哈希；`preexisting.patch`是开工前tracked差异，`baseline/src`/`tools`保留候选文件原文。`inputs.json`保留1728项输入身份，原核心DLL另存于baseline供独立进程比较。
- 实际执行 `./tools/dev.ps1 pwsh -NoProfile -File .tmp/dev-temp/workbench-decoupling-20261005/capture-baseline.ps1`；1728输入及23产物全部匹配，原核心SHA `b0bcc5f798269b51445c8ce1b13f48576020e5de760c7a14f86500819160fcb3`。重新执行原QualityCheck 81/81、MP state109/109、quality23/23；展开命令/平台/DLL身份见baseline的execution和各runner JSON。

## 原调用链与必须保留的契约

| 入口 | 原行为/顺序 |
|---|---|
| Run | 先Tick；单机/房主立即`done(local())`；客户端被拒绝时直接回调-1；容量64或编号耗尽直接回调-1，不发送；先递增编号、再读取世界/transport/time登记、再编码发送。返回false完成-1；发送抛异常记录日志、完成Unknown=-2。 |
| ReceiveResult | 完整解码并Finish读取→Tick→存在请求才更新LastResult→先移除请求后回调。无请求的迟到/重复响应不改LastResult。 |
| Tick | 遍历登记表快照；每项按世界身份、会话身份、remote-client、blocked、时钟倒退、elapsed≥30秒的短路顺序判断。每项继续读取当前环境，两次时钟读取保留；回调可重入或改变环境，不能冻结整批上下文。 |
| WorldClosed / SessionClosed | 快照中身份ReferenceEquals匹配的请求完成Unknown。旧所有者清理不影响新所有者，退出不重置编号。 |
| ClearOrphaned | 每项读取当前Project；null或不匹配才完成Unknown。保留回调重入后改变世界的语义。 |
| ScNet.Attach | 先替换Transport，身份改变才SessionClosed(previous)，故取消回调中的新请求属于新会话。 |
| 外部调度 | gun subsystem Update调用Tick、Dispose调用WorldClosed；adapter Update调用Tick，ProjectLoaded/Dispose调用SessionClosed；core无参数退出调用ClearOrphaned。位置、频率、所有者均不改变。 |
| 完成及异常 | 每请求最多一次；删除登记先于用户回调；回调异常记录warning后继续其他清理。快照内其他已被重入完成的项不会再次回调；快照外新项留到后续入口。结果未知绝不自动重试。 |

进程拥有唯一登记表和单调编号；每条记录持有准确世界/会话对象、登记时间和完成回调。生命周期辅助类接收不透明所有者、显式环境读取函数及完成结果，不查询引擎静态状态、不编码或发送消息、不生成玩家提示；外层负责环境适配、协议、发送/本地执行、LastResult、反馈文本及日志。使用函数输入是为了保持原有回调重入和短路读取时机，不是引入调度或DI框架。

## 验收方法

- 生产入口独立进程对照：原DLL/新DLL的发送op与原始payload（含真实id）、回调代码/文本/顺序、待处理数量、LastResult；正常/重复/迟到、30秒边界/倒退、发送false/抛异常、64上限/编号耗尽、断连/Attach/旧世界重复清理、异常回调/多种重入、完成后GC上下文释放。
- 纯生命周期检查只需要object所有者、可控时钟和委托，无Project/网络/引擎启动；规则类源码独立编译并执行，验证确实可脱离引擎。
- MP通过既有NetLoop平台包夹具，使用实际1.9.3.2_MP；单机/受控入口用1.9.3.1。没有向真实服务器发包。
- 先局部DLL和回归，冻结后编译受影响配置，复用不变资源；不制作完整资源包。语义依赖图核对新增反向依赖和唯一权威表。最终按开工哈希核对范围外文件未变，并输出本轮单独patch。

## 最终职责与变更边界

- `ScNetWorkbench.cs`：保留全部public入口、请求/结果结构、协议50/51/52、编解码、网络发送、本地执行、LastResult、玩家反馈及日志；环境读取集中为明确传入的Context读取函数。Run/ReceiveResult内部原次序及所有外部Tick/退出/Attach调用源码保持不变。
- 新增同目录内部`ScWorkbenchRequests.cs`：唯一pending字典、进程级单调编号、登记/移除/超时/所有者取消规则。仅依赖.NET基础库；不认识Project、ScNet、传输接口、ScWorkbenchResult、UI或日志。完成前删除，异常交给外层原warning函数。Context读取函数保留原短路/重入语义，未引入异步、调度所有者或全局事件。
- `QualityCheck`：新增WorkbenchChecks，默认入口实际运行；`--workbench-only`用于新旧独立进程对照。旧LifecycleChecks仅将pending数量观察改为兼容两种私有存储布局的观察函数，原七项F4断言不变。新增RequestRules小项目直接编译生产规则源码，父工程排除此独立入口。
- `NetLoopCheck`：新增`--workbench`模式、WorkbenchLoop，并复用同一套WorkbenchChecks；StateLoop报告额外记载原始工作台载荷及轨迹。所有原模式继续执行；原QualityCases未改。
- 本轮共13个文件（2个生产、10个测试/工程文件、1份本任务说明），详见`scope.json`。开工时1728项输入中，除允许的既有工作台/测试入口文件外无哈希变化；R1/R2/R3、库存/迁移/存档/动作/资源源码保留。未修改其他对外调用者。

## 实际执行与证据

所有路径前缀为 `.tmp/dev-temp/workbench-decoupling-20261005/`。下列命令均在Windows根目录经dev.ps1实际执行；JSON记录展开命令、退出码、耗时、加载DLL身份和逐项结果。

```powershell
./tools/dev.ps1 pwsh -NoProfile -File .tmp/dev-temp/workbench-decoupling-20261005/capture-baseline.ps1
./tools/dev.ps1 dotnet run --project tools/QualityCheck/QualityCheck.csproj -c Release '-p:CoreDll=E:/projects/ScCsgoKnives/.tmp/dev-temp/workbench-decoupling-20261005/baseline/ScCsgoKnives.dll' -- .tmp/dev-temp/workbench-decoupling-20261005/behavior-baseline.json --workbench-only
./tools/dev.ps1 python .tmp/dev-temp/workbench-decoupling-20261005/run_checks.py core
./tools/dev.ps1 python .tmp/dev-temp/workbench-decoupling-20261005/run_checks.py rules
./tools/dev.ps1 python .tmp/dev-temp/workbench-decoupling-20261005/run_checks.py mp
./tools/dev.ps1 python .tmp/dev-temp/workbench-decoupling-20261005/check_mutations.py
./tools/dev.ps1 python tools/check_quality.py --group variants --out .tmp/dev-temp/workbench-decoupling-20261005/configurations
./tools/dev.ps1 python tools/check_quality.py --group mp --mp-refs .tmp/mp-m0-20260929/refs/mp --out .tmp/dev-temp/workbench-decoupling-20261005/mp-regression
./tools/dev.ps1 dotnet run --project .tmp/dev-temp/full-quality-20261005/semantic/Semantic.csproj -c Release -- E:/projects/ScCsgoKnives E:/projects/ScCsgoKnives/.tmp/dev-temp/workbench-decoupling-20261005/semantic.json
./tools/dev.ps1 dotnet run --project .tmp/dev-temp/full-quality-20261005/syntax/SyntaxAudit.csproj -c Release -- E:/projects/ScCsgoKnives E:/projects/ScCsgoKnives/.tmp/dev-temp/workbench-decoupling-20261005/syntax.json
./tools/dev.ps1 python .tmp/dev-temp/workbench-decoupling-20261005/final_audit.py
```

| 实际检查 | 结果与边界 |
|---|---|
| API1.9.3.1，旧/新生产入口 | 各23/23；233条轨迹完全相同。`behavior-comparison.json`比较发送op、base64原始载荷（含实际id/字段）、callback代码/文本/顺序、pending数量、local次数、时钟读取次数、LastResult；无归一化。 |
| 真实API1.9.3.2_MP平台，旧/新独立进程 | 各36/36；233条合同轨迹及7条原始请求/回复载荷完全相同。真实包序列化/反序列化、生产服务端拒绝、重复答复、adapter Tick超时、ProjectLoaded会话清理、重连更大id/成功响应；另外23项生产入口合同在MP程序集上执行。`mp-comparison.json`。 |
| API1.9.3.1完整QualityCheck | 104/104＝保护基线81项＋工作台23项；`core-after.json`。R1/R2/R3相关既有核心场景保持。 |
| 无引擎规则项目 | 10/10；所有程序集引用仅System.*。身份匹配、上限/编号、超时边界、快照重入、短路读取、异常隔离和GC释放，`rules-final.json`。 |
| 错误实现敏感性 | 4/4错误副本被拒绝：超时≥改成>、WorldClosed重置编号、ReferenceEquals改值相等、Tick整批冻结时钟；每个runner按预期退出1，`mutations.json`逐一记录失败原因。生产源码未被替换。 |
| 受影响配置 | Full核心与MP相关产品/adapter构建通过；Lite Deflate/Zstd及各agents、Mini、Mini+Inspect共6/6编译通过。未打资源包。 |
| 当前候选的MP相邻回归 | transport28、compat28、state109、gunloop122、dmloop60、quality23全部通过；`mp-regression/execution.json`与各runner报告。 |
| 源码/结构 | 458个C#文件零语法错误；235个核心文件零语义绑定错误。新增唯一边ScNetWorkbench→ScWorkbenchRequests；新类向外项目依赖0；无新增双向边，既有强连通组逐组相同，最大仍133。`architecture.json`。 |

两种平台都验证了：正常/重复/迟到响应、30秒前/正好30秒/时钟倒退、明确发送false与发送抛异常、发送中同步完成后再抛异常、64上限与int.MaxValue耗尽、阻塞握手/断连重连、Attach先换transport后取消、旧世界/旧session重复清理、回调异常、回调中再次请求/完成另一请求/改变世界或时钟、完成后world/transport/callback上下文GC释放。单机及host本地执行各测一次调用和异常传播。Unknown仍为-2，confirmed-unsent仍为-1，未新增重试。

输入/产物身份：

| 对象 | SHA-256 |
|---|---|
| 开工前核心 | `b0bcc5f798269b51445c8ce1b13f48576020e5de760c7a14f86500819160fcb3` |
| 本轮候选核心（两平台实际加载相同字节） | `721c58e3473829e230b8e65d0014629d2d2eb7d0502180770b958f096d01510e` |
| 单机Survivalcraft.dll（1.9.3.1） | `2ef7c1918ea9d09f52a807719909bf28c722bd3a2793eeaf81db0c343b9eb9d3` |
| MP Survivalcraft.dll（1.9.3.2_MP） | `622976cce1f9da248ff7e3d38144fb7383e034d555961dd1e40d6f5fe33ca504` |
| MP Multiplayer DLL | `3e0b24efbf51b21372b337cd8f5d12242ec5fc446a90782f2eb4d274124e9cfe` |
| 新规则源码 | `42610633663d0f0b0ca06eebe923b252e3629e4547a148772774c4aaa6ef1ae3` |

完整依赖/变体SHA在`mp-identities.json`、`mp-regression/references.json`、`configurations/execution.json`和各runner assemblies中。旧/新比较使用独立进程、独立库存/Project夹具，未向真实服务器重复操作。

最终记账的局部/配置编译合计50.640秒、runner合计7.437秒；开工复跑、首次直接对照、4个错误副本约10.702秒、语法/语义分析及修正夹具另计，不混作纯构建时间。人工分析总耗时未计量。最终1733项输入哈希在配置/MP执行后仍无漂移；两端同步idle、0 pending/errors、1546文件，无冲突。

失败尝试也保留：`behavior-before.json`最初19/23，4个夹具错误地假设Dictionary在经历删除后仍按登记先后遍历；原实现已能反证此前提，改为无论哪项先回调均触发相同行为的夹具，并继续逐条比较真实顺序。MP初编译缺少Game.Network using，失败日志保存为`build-mp-runner-previous-1.log`，补齐后通过。清理首次因未加载的Deathmatch依赖重编译后哈希不同而停止、未删除；核对原R基线仍保留精确旧输入后才清理。没有为测试改变产品规则。

## 差异、回退、清理与停止边界

`round-only.patch`相对开工快照生成，仅包含上述13文件；`scope.json`记录前后哈希和范围外未变的证明。回退只逆向本轮patch（先check且确保无后来重叠修改），不逆向整个git diff、不reset工作区、不提交此前修复。已有文件恢复到开工版本，新增辅助类/测试项目/本说明一起撤回；helper与调用者须同步回退。它不影响原世界持久数据，因为本轮没有存档读写变更。

已执行 `git -c core.autocrlf=false apply --reverse --check .tmp/dev-temp/workbench-decoupling-20261005/round-only.patch`，通过；仅检查，未回退文件。补丁生成显式使用LF以匹配实际源码（首次Windows默认CRLF输出的check失败已定位修正）。`git diff --check`通过。

`cleanup.ps1`实际永久删除2个可重建目录：本轮重复MP引用目录和四个错误副本编译目录，共**163,684,481字节**。保留旧核心/原文/预先dirty差异、所有JSON/日志/复现脚本、最终MP与6配置工作集。`cleanup-receipt.json`列出精确路径、大小、每个被删引用的保留位置和证据哈希；不是回收站删除。

真实游戏/跨设备网络时延/Android、正式包及发行兼容矩阵未执行，也不以本轮离线结果替代。未改变任何引擎入口或回调调度时机；测试覆盖的是现有调度被调用时的规则及真实平台包链路。项目已有133文件依赖环、迁移新旧发行兼容缺口保持原状。本轮到此结束，不继续库存/枪械核心解耦。
