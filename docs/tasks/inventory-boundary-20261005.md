# 第二轮安全解耦：库存写入、提交和发布

Status: review — 本轮源码、长期门禁、独立差分与受影响配置验证完成；停止于本轮边界，真实游戏及发行验收另列。

2026-10-06 P2续修：**已补齐并验证长期断言**，仅修改`InventoryBoundaryChecks.cs`及本说明；生产源码/DLL保持不变，不进入下一轮重构。新证据集中在`.tmp/dev-temp/inventory-assertions-20261006/`。下方第二轮历史执行结果保留；P2补充的当前测试身份和独立回退补丁见文末。
Current writer: 本聊天Windows Codex，唯一写入者；VPS源码会话空闲，worker无任务。
Scope: 用户采纳`workbench-review-20261005/NEXT-PROMPT.txt`，先补长期门禁，再分步隔离写入回执、提交/修订、槽位发布及记录/ack配合。不得改玩法、协议、锁范围、持久格式、迁移算法或调度；本轮完成即停止，不提交/推送/安装/发包到真实服务器/改原世界。

## 保护基线

分支`fix/quality-20261005`，HEAD `ccbcfbf721abf97deb22cd680c55da45a8c90cc6`及现有未提交修改。已阅读AGENTS、协作/构建/兼容指南和本轮REVIEW；正式1.4.0起的兼容政策高于历史指南窄矩阵。现有发行兼容缺口不在本轮解决范围。

两端Syncthing idle、0 pending/errors、1546文件，无冲突。开工只读查询确认旧VPS会话停在提示符、worker无job。`.tmp/dev-temp/inventory-boundary-20261005/baseline/`保留完整文本候选快照、preexisting.patch、source/input哈希、状态及旧核心可执行DLL；未复制资源树。1733项上一轮输入均匹配。旧核心SHA `721c58e3473829e230b8e65d0014629d2d2eb7d0502180770b958f096d01510e`；独立实际复跑QualityCheck104/104、RequestRules10/10、MP workbench36/36（命令和加载身份见baseline）。

## 先核对的逐路径契约（生产改动前）

| 入口/分支 | 互斥、owner和实际写入 | 记录/修订/发布及失败边界 |
|---|---|---|
| GunMutation.Commit | remote拒绝在锁外；TryEnter后构造journal；核验固定库存/registry/owner/槽位/epoch/record revision；逐项扣料再替换，finally保留实际逆操作 | 发布新record或更新原record→Revision/Holder→Id/Expected→AfterRecordWrite→再次确认storage→需要时SlotRewritten→Changed→完成kill凭据→成功→finally Exit。异常先还原record/Id/Expected、Abandon新id，然后确认owner后立即或延后补偿。提前拒绝无Changed。 |
| ReplaceWithCost | normalize→authority/registry/锁；核对owner/欠账/成本/槽位/容量；扣料逐次复查目的地；Remove后再查、Add后再查 | 实际成功后SlotRewritten→Changed→true；catch确认旧registry及owner，立即或延后回滚；finally释放。 |
| TryCraftBatch | client拒绝在锁外；固定backing及owner，Prepare不写；检查整份槽位快照；每次写前Stable | journal Remove/按计划Add或创造Replace→Stable→Changed；catch有journal/owner时按原storage/owner决策回滚；finally释放。 |
| TryUseItem | 锁内确认slot/action/storage，创造模式直接action且无journal/Changed；生存确认registry/owner后Remove | action失败/抛异常先由action自身撤销实体，再库存回滚；成功Changed。退出位置不变。 |
| KnifeSkinning.Apply | free/quote匹配后入共享锁；按原顺序确认owner/quote.Storage/成本；多次Stable；生存Remove+Add，创造Replace | 成功Stable→Changed；失败按quote backing和owner选择回滚；不分配枪record。 |
| journal Remove/Add/Replace | Touch在原写入前。Remove记录API返回量，同时finally按实际减少记正数undo；Add finally按实际增加记负数undo；创造finally按实际替换记条件undo | 双重核对报告/实际；写后抛异常仍留undo。不能以请求量记账。API和观察异常的原顺序保留。 |
| journal Rollback | m_undo.Reverse→Recovery.Apply逆序，未解决才Enqueue；原替代品未删不先退原物品；Clear最后 | Apply/Enqueue后Changed：先epoch++后TransactionEnded。DeferRollback仅Reverse→Enqueue→Clear→ScNetSlots.Changed，不推进epoch。 |
| Recovery.Apply/Retry | Apply只对原slot删失败插入，原物品可退别的空位；每次部分恢复立即按实际减少义务；Retry共享锁、逐owner解析、缺owner保留 | 以steps绝对量+数量变化判断进展：有进展才Touched；每个可解析库存都Changed（无进展也推进epoch）；完成才删batch；finally释放。Save/Load字段不动。 |
| Changed/TransactionEnded | epoch按真实storage身份递增，不缓存代理映射；随后TransactionEnded | touched空/null直接返回，非host只清touched；移除触碰标记后Changed登记。这个try边界吞发布登记异常；不能扩大到其他通知。 |
| ScNetSlots.Changed/EndOfFrame | host+storage/capability检查原位；按真实IInventory引用去重、首次due次序不变 | 帧末先due快照并清空→角色/能力判断→FlushRows→逐库存Publish。同一try包住整批；false计Unsupported继续，异常中断后续；无重入队。发布中的新due留下一批。Clear不清进程提示缓存。 |
| Correct/ActiveSlotAdopted | Correct先角色/null/peer，再FlushRows，再查询transport和storage后发定向包；ActiveSlot原短路次序 | Correct不捕获FlushRows异常；不改计数和提供方不支持路径。adapter调度不改。 |
| Mirror.SendChangedRows/SendRows | registry变化才清发送缓存；收集changed/removed；先物化全部row再按peer快照循环；每peer单独TryAck | 256行分批，至少1批；ack只附最后一批。sent &= 每批发送，不短路；全批成功才AckSent；所有适用peer成功才推进sent revision缓存。false保留record重发，抛异常沿原调用边界走。 |
| Gun ack来源和应用 | 仅ServerShot在生产成功提交之后增Fired；ServerSettle只标实际不再执行的ClientShots；保留selection/item/player/world来源 | TryAck从当前peer对应input取快照；AckSent按调用时环境清AckDue。客户端完整解码→应用row→ApplyAck；selection/负数拒绝，item未到先defer，registry/selection确认后按原预测规则处理。切枪/退出清理不改。 |

API返回数量、观察到的变化、网络成功入队是三个事实。入队不证明远端应用，不压成一个Success。静态可见的一处旧边界是SlotRewritten异常仍会进入业务catch，尽管旧注释称通知不能撤回事务；本轮保留真实异常边界，不按注释改成吞异常。

## 分步设计与必要调用者适配

1. 门禁工具独立补丁：core/all显式RequestRules、mp/all显式workbench；增加无资源rules组；记录计数/身份，missing和not-executed分清。
2. journal只负责实际写入及逆向凭据，不直接认识网络或epoch；用局部回执值记录请求、报告和实际变化。epoch/结束通知进入明确协调边界，原公共Revision/Changed保留。GunMutation、Replace、CraftBatch、KnifeSkinning仅适配journal触碰和回滚结束调用，不动业务判定、锁和持久字段。
3. 提取纯引用身份touched/due队列，外层负责角色、storage解析、传输、日志和计数。记录发布独立批次/入队结果协调，保持最后批ack和成功缓存规则，不搬预测/动作状态机。
4. 新旧独立DLL执行同一生产入口轨迹；纯规则独立运行并用错误副本证伪；局部通过后冻结候选，执行库存/迁移/MP/变体受影响检查。每步输出可独立审查diff，写清回退依赖。

## 职责实际分离的位置

- `ScGunInventoryJournal`从`ScGunRecovery.cs`分出，负责实际写入观测、逆操作和补偿转交；通过调用者传入的同步beforeWrite执行原位置的Touched，本身不依赖网络、epoch或ScInventoryTransaction。`ScInventoryWriteReceipt`明确区分Requested、Reported、Observed；异常仍原样传播，finally先保留实际变化。创造条件替换保留原写法，不把无限来源当普通数量。
- `ScInventoryChanges`是库存epoch唯一owner及同步结束通知边界。公共Revision/Changed转到它；调用者在原catch/锁范围内明确执行`journal.Rollback→Finished`或`journal.DeferRollback→ScNetSlots.Changed`。GunMutation的record回退、KillToComplete、SlotRewritten相对顺序不变。CraftBatch/TryUseItem/KnifeSkinning仅适配journal触碰委托和回滚结束调用；其业务判定与finally未改变。Recovery保存/读取、Apply撤销规则原样保留，Retry仅改向新的同一epoch入口。
- `ScSlotPublicationQueue`只拥有touched/due，输入已解析的真实storage；引用去重、首次入队顺序、快照清空可独立测试。ScNetSlots保留每个原位置的角色/能力/代理查询、整批try边界、日志及计数，未增加重排、重试或新清理策略。
- `ScRecordBatches`负责单peer的行分批、最后批附ack和全批成功后的确认回执；ScNetMirror继续负责读取真实record、选择peer、写协议、真实发送及所有peer成功后更新sent cache。`ScShotAcknowledgement`是不可变输入快照，明确selection/value/fired/skipped；ScNetGuns依然唯一持有真实计数、预测和deferred状态，算法/输入解析/退出规则未搬走或重写。
- `check_quality.py`的core/all先构建并实际执行RequestRules；mp/all新增workbench及publication；rules组无需资源；报告汇总附命令、exit、耗时、报告hash、计数及实际加载身份。旧入口位置/协议/持久字段/资源未变。

## 每项契约的入口与证据

| NEXT-PROMPT契约 | 生产/检查入口 | 结果 |
|---|---|---|
| 1 共享互斥和finally | Commit、Replace、CraftBatch、TryUseItem、KnifeSkinning、Retry；InventoryBoundary的nested-save、notification-order，原InventoryCheck | 回调内锁仍可见、重入/save拒绝，结束后释放；提前解锁错误DLL造成轨迹差异，未通过差分判定。 |
| 2 实际写入/报告不等价 | journal RemoveExact/AddExact/ReplaceCreative；receipt规则；partial-remove/reported-wrong/remove-throw/add-throw/creative | 观察量及原报告双重校验；写后异常仍补偿。按Requested替代Observed的错误副本被规则门禁拒绝。 |
| 3 逆序/精确owner/部分补偿 | Apply/Retry及调用者catch；debt/owner-change/retune、两次Retry/XML重读；原Sushi检查 | 替代品先撤、剩余义务按实际减少；缺owner/变化保留；保存后重复恢复不多发，旧新义务XML轨迹一致。 |
| 4 不同修订语义 | ScInventoryChanges、Rollback/Defer/Retry；epoch/lock/due逐次轨迹 | 正常、立即、延后、无进展Retry各路径保持，未统一为成功一次通知。 |
| 5 提交/SlotRewritten/Changed/kill | Commit后AfterRecordWrite、SlotRewritten时Clock、角色查询/epoch；C01/C06/gunloop | record先回退再退款；成功路径相对顺序及锁范围保留。kill完成调用未动，既有恢复/成长检查通过。没有把网络失败变成业务重试。 |
| 6 backing引用/代理 | ScInventoryIdentity原解析；受控已登记resolver的同storage代理和retune；原Sushi DLL InventoryCheck | 引用相等合并、值相等不同对象不合并、不缓存retune；原提供方未改。受控代理不是实际Sushi网络桥验收。 |
| 7 同帧、快照与重入 | queue纯规则及ScNetSlots生产；normal/reenter/clear/role/role-during/clear-during；C05 | 同库存只一次、先入队先发送、发布中新项留下一批、环境查询/能力捕获时点保留。 |
| 8 记录先于槽位及失败语义 | EndOfFrame→FlushRows→Publish；真实mutation→平台包→客户端；C01/C05/C06 | 最终提交或回滚槽位入队；同可靠有序通道，客户端枪表/槽位一致。槽位先于记录的错误DLL被PUB顺序断言拒绝。 |
| 9 最后批ack、缓存与defer | 257行/2peer、每peer2批；ScRecordBatches/ApplyRecords/ApplyAck | ack仅最后批；本peer全成功才清，所有peer成功才推进缓存。记录先到时Pending保留、槽位到后处理。提前清ack错误副本被规则拒绝。 |
| 10 真事务来源/迟到确认 | PublicationLoop由真实Commit成功才ServerShot，失败ServerSettle；无变化row的实际拒绝确认；C09/gunloop/workbench退出重连 | 新增42项覆盖分批/多peer/部分失败/重复/切枪/退出；原109项state、122项gunloop保护真实动作和会话路径。未手造成功ack冒充事务。 |
| 11 false/throw/FlushRows | 发布false继续、throw中断、flush-throw、Correct传播；row partial-peer/batch失败后FlushRecords | 旧新一致；slot批异常后不会自动重入队，record仍按原逻辑重发、不重执行业务。 |
| 12 Clear/角色/纠正/活动槽/不支持 | ScNetSlots对应入口；null/unsupported/correction-throw、C09、adapter原调用者 | 清理与计数/异常边界保持；未修改adapter调度或提供方桥。 |

## 旧新对照和长期门禁实跑

证据根：`E:/projects/ScCsgoKnives/.tmp/dev-temp/inventory-boundary-20261005/`。

- `inventory-old.json`/`inventory-new.json`：独立进程、独立故障库存、相同生产入口，**各30/30，210条轨迹逐项相同**。包含槽位值/数量、请求量/报告量、undo剩余XML、库存epoch、record行/revision、返回值/异常、锁可见性、rewrite/Changed顺序及发布。`inventory-comparison.json`零差异、无归一化。
- `publication-old.json`/`publication-new.json`：真实1.9.3.2_MP平台包，独立旧/新核心，**各42/42，69条发布轨迹逐项相同**（peer目标、批次、原始base64载荷、slot快照、ackDue、sent cache、客户端pending）。真实业务为生产GunMutation.Commit；完整枪械Update/时序另由gunloop保护，不把本夹具称为引擎实战。
- 旧/新平台和core SHA都在各report assemblies及`mp-identities.json`；没有对真实服务器双执行任何操作。
- 纯规则**17/17**＝第一轮RequestRules10项＋本轮回执/队列/记录批次/ack7项，源码直接编译且仅基础库引用。无资源最小副本执行：rules退出0；core仍运行规则后将缺资源的QualityCheck标missing/not-executed、退出2；无MP时明确missing/not-executed、退出2。篡改回执规则使实际runner失败、统一门禁退出1，见`gate-probes.json`。
- 4种错误副本均被识别：请求量代替实际量、失败提前清ack、slots先于rows、提前Exit互斥；前两种规则runner退出1，slot顺序的真实生产错误DLL令MP断言失败，早解锁错误DLL虽原计数通过但生产轨迹不等，不能判通过。`mutations.json`记录caught和错误DLL hash。没有把错误副本写入源码。

实际命令（均经dev.ps1；完整展开命令及每次exit在各execution.json）：

```powershell
./tools/dev.ps1 pwsh -NoProfile -File .tmp/dev-temp/inventory-boundary-20261005/capture.ps1
./tools/dev.ps1 python .tmp/dev-temp/inventory-boundary-20261005/compare.py inventory
./tools/dev.ps1 python .tmp/dev-temp/inventory-boundary-20261005/compare.py mp
./tools/dev.ps1 python .tmp/dev-temp/inventory-boundary-20261005/gate_probe.py
./tools/dev.ps1 python .tmp/dev-temp/inventory-boundary-20261005/mutations.py
./tools/dev.ps1 python tools/check_quality.py --group all --mp-refs .tmp/mp-m0-20260929/refs/mp --mods 'D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods' --appearance-refs .tmp/dev-temp/full-quality-20261005/appearance-refs --out .tmp/dev-temp/inventory-boundary-20261005/final
./tools/dev.ps1 python tools/check_quality.py --group core --out .tmp/dev-temp/inventory-boundary-20261005/final-core
./tools/dev.ps1 python tools/check_quality.py --group mp --mp-refs .tmp/mp-m0-20260929/refs/mp --out .tmp/dev-temp/inventory-boundary-20261005/final-mp
./tools/dev.ps1 python .tmp/dev-temp/inventory-boundary-20261005/audit.py
```

| 层次 | 实际结果 |
|---|---|
| 最终统一all | 退出0，10产品、6配置通过；1741项输入运行前后不变。包含新RequestRules/workbench/publication实际执行，没有仅配置CI。 |
| 最后测试补充 | all后仅补充2个测试文件，无生产变化；新旧再比一致，维护入口core/mp重新执行。`final-core`替代core结果为**134/134**（原104＋库存30），规则17；`final-mp`publication为42、workbench仍36；原all中core133/publication36属于补充前记录。不为测试追加重新打完整资源包。 |
| 相邻行为 | Inventory2262、Balance5506、Feedback398、Deathmatch235、Appearance19、argv5，全部通过。含原Sushi语义和恢复/迁移，读取的是已有独立夹具，不写原世界。 |
| MP相邻 | transport28、compat28、state109（含C01/C05/C06/C09）、gunloop122、dmloop60、quality23、workbench36全部通过。 |
| 配置 | Lite Deflate/Zstd与agents、Mini、Mini+Inspect共6/6，仅编译；Full核心及依赖产品已编译。 |
| 静态/结构 | 466维护C#文件语法零错误，240核心文件语义绑定零错误；diff检查通过。 |

最终core SHA-256：`5da592b77769c54aca383e6f8cb3bf332e16201371c2febc9a7bda72332ea344`。旧core为前述`721c58…1510e`。单机API1.9.3.1 Survivalcraft DLL `2ef7c1918ea9d09f52a807719909bf28c722bd3a2793eeaf81db0c343b9eb9d3`；MP1.9.3.2_MP `622976cce1f9da248ff7e3d38144fb7383e034d555961dd1e40d6f5fe33ca504`。原adapter实际字节 `5362913ec40dfcd3a971d6c01031e35ab0c3a47432e3af6797b5c24cf5c6407d`未变。所有产品/变体/输入完整身份见final*/execution、inputs和references JSON。首次全量构建合计63.356秒、runner39.938秒；后续聚焦、故障副本、夹具修正各自记账，人工分析总时间未计量。

## 结构结论、已知基线边界与未覆盖项

核心非SelfTest直接边1188→1206，新增直接双向边0，移除ScGunRecovery↔ScInventoryTransaction。三个纯规则类无项目内向外依赖；journal只依赖Recovery和WriteReceipt，不再依赖NetSlots或InventoryTransaction。最大文件强连通组133→135，新增成员正是拆出的journal/ScInventoryChanges；旧文件之间没有将原先不同强连通组合并的新循环。它们仍处在既有核心业务环，不能宣称整体解耦或大环减少；职责隔离的收益是回执/逆操作、epoch推进、发布登记/传输、ack批次完成条件分别明确且可独立验证，权威状态未重复。

原有边界单列：SlotRewritten内部Clock异常会进入业务catch并回滚，尽管旧注释称通知不会撤回事务；本轮在旧/新独立夹具复现并保持，调整的仅是误导注释。EndOfFrame的FlushRows/Publish异常会中断并丢掉当前slot快照后续项，记录缓存另有重发规则；本轮保持并验证Correct恢复路径，没有暗改成自动重入队。它们是否应另做功能修复不属于本轮纯重构。

失败尝试保留：首次库存夹具19/21，原因是退款Remove仍有实际进展却误断言无进展，以及batch的首槽容量1使原“部分Add”实际完整；修正前提后旧/新一致。MP先注册P2后P1，首次故障注入误认peer顺序导致2个旧基线断言失败，按真实peer次序纠正，没有改产品。规则项目初次C#多var声明编译失败后修正；错误DLL最初的targets误传到Resources子工程，编译失败保留，改为源链接隔离工程后实际故障才执行。预期错误副本不能混入成功统计。

真实Windows游戏/第三方provider回调、跨设备MP/丢包、Android和用户观感均未执行；平台loop不等同这些验收。旧AncientWorld真实程序集、历史二进制ProtectedLoad及正式发行兼容矩阵仍not-executed。正式1.4.0起双向保存兼容缺口保持单列，不能因schema和本轮持久代码未变而宣称关闭。没有安装、发布、推送、替换正式包或写原始世界。

## 本轮差异和安全回退单元

24个文件：生产13个（World八个、Net五个），工具/测试10个，本说明1个。`scope.json`核对开工所有文本与1733输入；无允许范围外的改写或新增。`round-only.patch`只对开工快照，本轮之前的R1/R2/R3和工作台改动保留。

- `01-gates.patch`：第一步RequestRules/workbench维护接入及记录汇总，可单独审查；对应原10项规则/36项workbench。
- `02-writes.patch`：写入回执/journal与epoch/回滚协调。可分别审查新增Receipt/journal的观测逻辑、Changes的epoch逻辑、Recovery的Retry一处替换、四个调用者的协调hunk；**回退必须作为一组**，否则可能漏通知或重复推进epoch。现有锁实现、Registry、存档字段不变。
- `03-publication.patch`：ScNetSlots与queue是一组；Mirror/RecordBatches/Guns不可变ack适配是另一组。这两组可分开评审，回退时各组内部一起，不能只删helper或只还原一端。原调度者不在补丁中。
- `04-checks.patch`：新增长期回归和publication维护模式；工具文件相对01步骤，其余相对开工。规则项目引用新helper，撤回生产组时须同步撤回相应规则链接/测试，不得声称任意拆撤都安全。
- 全部回退仅逆向本轮patch并先check；不要按HEAD全文件restore，不回退他人的未提交改动。源码回退不等同世界数据恢复，本轮未写世界。

仅清理本轮可重建临时引用、错误副本/无资源门禁副本；保留baseline源码/DLL、阶段patch、全部失败/成功JSON和日志、复现脚本及最终变体工作集。精确清理路径/字节与可恢复性见`cleanup-receipt.json`；永久删除、可重建，不在回收站。本轮到此停止。

最终清理累计**501,696,786字节**（含补充检查重建后再次被替代的同路径引用，收据逐次记账，不误作不同目录）；最终MP输入保留于`final-mp/mp-refs`，原all的MP副本已被它替代。`verification.json`合并final/all与后续final-core/final-mp的有效产物身份：1741当前输入无漂移、所有有效产物无漂移；all之后仅上述两个测试文件变化，保护基线和生产候选未变。两端Syncthing idle、0 pending/errors、1555文件，无冲突。

`git -c core.autocrlf=false apply --reverse --check .tmp/dev-temp/inventory-boundary-20261005/round-only.patch`和`git diff --check`实际通过；只check没有撤回文件。分步patch用于审查，整体回退以最终round-only.patch和scope哈希为准，遇到后来重叠修改先重新核对。

## 2026-10-06：复审P2长期断言补齐

复审定位正确：上一轮临时新旧轨迹对照能识别早解锁，但长期QualityCheck只记录通知阶段状态，没有实际assert。本次先核对1741项输入全部匹配，分支仍`fix/quality-20261005`、HEAD不变；两端同步idle、0错误，VPS现有会话声明不编辑。`baseline/identity.json`、测试/说明原文快照保存开工边界。实际再次运行旧维护runner：正常核心134/134，审查同一个提前解锁错误核心库存30/30、退出0，确认缺口仍存在。

只强化原有两个用例，**用例数不变**：

- `commit-notification-order-under-lock`：以实际Add完成作为通知阶段起点，排除入锁前Role查询。收集类型化阶段观测，调用返回后断言write→SlotRewritten→epoch推进/TransactionEnded→return→帧末publish；通知阶段均持锁，rewrite之前epoch不变、due未登记，TransactionEnded看到epoch+1，返回后锁释放且due存在，帧末只发布一次最终值。SlotRewritten中实际尝试registry.Save，必须拒绝。
- `baseline-rewrite-clock-exception-rolls-back`：保留真实Clock异常触发生产catch，收集随后移除替代物→还原原物品→归还材料的实际序列。返回后断言每个补偿步骤持锁、epoch未提前推进、save被拒绝；只有逆操作完成后才推进epoch并登记最终状态；返回释放、帧末发布恢复值。断言不在生产会捕获的回调内执行，失败会到达维护runner。

不对所有Role查询一律要求持锁，也不把日志字符串作为唯一oracle；新观测不写入原inventoryTrace，因此可以继续精确比对原先210条轨迹。

| 实际执行 | 结果 |
|---|---|
| 维护入口`check_quality.py --group core` | QualityCheck **134/134**、RequestRules **17/17**，退出0；`core/execution.json`记录实际build/run/count/assemblies。 |
| 补强后的新旧核心库存 | 当前核心和第二轮开工旧核心各**30/30**，210条轨迹/逐项结果完全相同，未归一化；`verification.json`。正常生产轨迹与本次补强前也相同。 |
| 审查原早解锁错误DLL | 库存**28/30**、完整核心**132/134**，均退出1；两个新断言分别识别通知及异常恢复丢锁。使用精确相同`12875c…4460`错误DLL，不以另造错误代替审查证据。 |
| 同一错误DLL进入真实core门禁 | 在独立最小checkout复制未修改的`check_quality.py`与当前测试源码，通过既有CoreDll工程参数加载错误DLL；独立规则17通过，quality-core失败2项、门禁退出1。`gate-mutant/execution.json`实际失败原因和加载hash均核验，不是因缺依赖失败。 |
| 三个附加错误副本 | Changed提前到SlotRewritten前：失败2项；TransactionEnded早于epoch：失败2项；只有补偿提前Exit：失败1项。均实际构建隔离DLL后执行维护库存runner、退出1。 |

所有命令经Windows dev.ps1：

```powershell
./tools/dev.ps1 pwsh -NoProfile -File .tmp/dev-temp/inventory-assertions-20261006/capture.ps1
./tools/dev.ps1 python tools/check_quality.py --group core --out .tmp/dev-temp/inventory-assertions-20261006/core
./tools/dev.ps1 python .tmp/dev-temp/inventory-assertions-20261006/verify.py
./tools/dev.ps1 python .tmp/dev-temp/inventory-assertions-20261006/finalize.py
```

完整实际子命令、预期/实际exit、时间、失败用例及程序集身份在`baseline/execution.json`、`execution.json`、`core/execution.json`和`gate-mutant/execution.json`。错误DLL仅在隔离runtime/checkout内执行，不替换共享bin里的候选核心。单机依赖仍为API1.9.3.1（Survivalcraft DLL SHA `2ef7c1918ea9d09f52a807719909bf28c722bd3a2793eeaf81db0c343b9eb9d3`）。

- 当前生产核心仍为`5da592b77769c54aca383e6f8cb3bf332e16201371c2febc9a7bda72332ea344`；生产输入全部未改。
- 新QualityCheck runner SHA：`42abe1df6a043df1da1ab694f305837abd3698b2391a86b41d190471770e931b`。
- 审查原错误核心 SHA：`12875c260102652ada27af769c9f086186c42e68322c59faba7519ff70974460`。
- 维护测试前后SHA：`3ea5b472c371e175175aa5818e4b9cf4895d3a75d3ed195f6b1fd2328b224e08`→`a31c38cb7037ffb47fc13d45d84abe7025d5e731b7a3e3a8cb739065cfe3a944`。

最终只变测试文件和本说明，`verification.json`核对范围外无漂移。`assertions-only.patch`是相对此次开工的两文件独立补丁；反向`git apply --check`通过（未执行撤回）。要回退P2只逆向该补丁，不对HEAD或第二轮整体patch操作；本次后旧round-only.patch的文档/测试哈希自然不再是当前身份。

清理仅限本次完成验证的runtime、三错误副本构建和门禁checkout，永久删除且可按verify.py重建；逻辑大小1,117,397,701字节，其中runtime多数为硬链接，**不是实际释放磁盘字节数**。保留原候选/旧核心/审查原错误DLL、当前runner、所有JSON/日志/脚本及原文快照；清理后保护文件hash未变。详见`cleanup-receipt.json`。

这次生产代码未改，因此未重复MP、变体和完整资源构建，原身份匹配证据仍是之前执行结果。真实游戏、跨设备MP、Android及发行兼容矩阵仍未验证。本次没有新生产功能结论，也未提交、推送、安装、发布或修改原世界；完成P2即停止。
