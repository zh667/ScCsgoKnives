# 自然敌队0天仍不生成：日志与实际DLL核对

状态：Windows只读诊断完成，补充video-feedback-20260929任务；未修改代码、第三方包、设置或玩家世界。

## 输入与确认事实

用户日志 D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Bugs/Game.log 含多次启动/多个世界，不能把先前创造世界的5名敌人当作新生存世界自然刷新成功。已冻结本机副本 .tmp/spawn-feedback-20260929/Game.snapshot.log；SHA-256 d54e0b6c2de4bc3105dab015e57b5d0a951b711d0da40b38d88f83e72856aba8。原日志可能继续追加，以下行号属于该副本。

| 行/时间 | 证据 |
|---|---|
| 13374 / 00:21:02.724 | Slower Creature Spawns Mod: Initialized (rate = 0.1x) |
| 13561 / 00:21:10.071 | Slower Creature Spawns Mod: Limits adjusted (0.1x) |
| 13700 / 00:21:22.313 | Bobia Gresyne加载成功，游戏模式=生存 |
| 13705 / 00:22:01.678 | world rules changed natural=True graceDays=0 density=Dense |
| 13794 / 00:27:51.890 | natural refusals: placement=1 |
| 14104 / 00:30:24.680 | Zbelandpan加载成功，游戏模式=生存 |
| 14113 / 00:31:31.907 | world rules changed natural=True graceDays=0 density=Dense |
| 14127 / 00:37:52.702 | placement=2 |
| 14129 / 00:39:00.112 | 再次设置natural=True graceDays=0 density=Dense |
| 14132 / 00:39:24.021；14148 / 00:41:59.422 | 分别placement=1 |

结论：这两次世界内0天/较多已成功应用，不是仍卡在30天。新世界先从设备默认30天/标准初始化，随后用户将各世界单独改为0天/较多，也符合现有“世界规则与新世界默认分开”的设计。

## 足以完全阻止整队生成的预算冲突

读取实际安装 [1SlowerCreatureSpawnsMod.scmod，SHA-256 101886580c11f5e7167c975765a0b0318da7b42b9ba63a4abeb788cae5c2965b。在独立.NET进程加载其DLL，只调用OnLoadingFinished（没有PatchAll、没有载入游戏世界）：

- SpawnRateMultiplier=0.1。
- SubsystemCreatureSpawn.m_totalLimit由26改为2。
- m_areaLimit由3改为1。
- 当前CS自然敌队至少3人，后期5人；SpawnSquad检查CountCreatures(false)+roles.Length > m_totalLimit就返回0。
- 即使CountCreatures(false)=0，3>2、5>2始终成立；CS自己的Dense上限15不能覆盖该外部全局门槛。

这是实测安装DLL静态限制与CS条件之间的确定冲突。没有附加到游戏进程直接读最终静态字段；若其他mod之后再次改了全局上限，需要运行时日志确认。但此次日志明确记载同一0.1倍加载，现有材料中没有上限被再次提升的证据。

第三方还有原生SpawnRandomCreature/SpawnChunkCreatures的0.1概率放行补丁。CS只注册原生候选类型，并不独立按40/60秒定时造敌队，因此会同时受到原生调度、随机门槛、全局/局部预算和加权候选竞争影响。密度的Cooldown是成功生成后最短间隔，不是“每40秒必来一队”。0天只取消开档等待，不绕过这些条件。上限2保持时继续等也不能造出完整3人队。

本地探针：.tmp/spawn-feedback-20260929/{Program.cs,Probe.csproj,native-limit-proof.json}。命令 tools/dev.ps1 dotnet run --project .tmp/spawn-feedback-20260929/Probe.csproj -c Release。输出在独立进程，不修改当前游戏或第三方包。

## placement和诊断盲区

placement汇总只说明进入CS Suitable的少量候选被Safe拒绝，不能从它断言具体是树/草/视线/距离。Safe现要求已加载地形、接近列顶、两格严格空气、可碰撞地面、离所有玩家至少28格、不在无遮挡视锥中和无实体占位；这比原生允许非碰撞草等的表面规则严格。SpawnSquad又改用GetTopHeight找每名成员地面，有树冠/屋顶等环境可能继续失败。没有候选坐标和分项日志，不能证明本世界每次都是哪一条。

当前rejections不统计原生预算阻止调用、不统计SpawnSquad中的全局上限返回0，也不统计整队位置不足。因此只出现placement不代表没有容量冲突，日志甚至可以在绝不可能成功时一直只显示“已允许生成”。这属于CS集成与可观测性缺口，不能统一推给玩家设置或设备。

历史release-crafting-1.4.7文档已经记录Slower上限2与自然整队冲突；之前只把手动召唤从自然人口门槛分离，没有解决自然生成。不要把手动信标能造队当自然刷新回归通过。

## 给VPS的补充范围

在video-feedback-20260929任务中加入S0，优先于继续微调密度：

1. 用实际Slower DLL创建上限2、已有普通怪0/1/2的场景，以及无Slower默认26场景，测试完整原生调度→候选→整队提交，不只单测AllowsAfter或手动SpawnManual。
2. 先补状态反馈：开关/天数满足不等于可生成。全局上限小于整队人数时显示“等待期已结束；当前生物上限2，无法容纳3人小队”，并有界汇总上限、现数量、队伍人数、密度、预算来源可辨信息。若不能确认谁修改，不随意点名其他mod。细分placement原因和整队失败，注明哪些入口根本没被原生调用。
3. 不能只删Count+roles检查：原生调用本来就可能被人口预算/概率拦住，且这样可能突破玩家安装低生成率mod的意图。也不能改写第三方DLL或全局m_totalLimit、删除普通生物腾位、拆成不完整单兵后称整队成功。
4. 提交两种明确策略供用户选择：A遵守原生人口预算，冲突时说明而不假称能刷；B显式启用“CS小队独立遭遇预算”，由CS世界开关/等待天数/密度及空间上限驱动独立低频有界调度，不修改第三方，但必须说明CS敌队不再受其普通生物数量/生成率限制。B是新的行为选择，当前只诊断与规划，不静默当已授权默认。
5. 若后续用户选择B，保留世界模式、0/30天、整队原子提交、已加载可站地形、视线/玩家距离/新索敌规则、活动+休眠去重、手动来源分离、有界总数与性能预算；关闭只停新增、不删现存。适用旧世界和两轮存读，不能再次清状态。独立调度与密度文案匹配，必须实测等候时间分布。
6. 地形原因未明确前不要取消所有安全条件；先收候选坐标/拒因，在新测试世界覆盖草/雪/坡地/林冠/屋顶和纯空地，保留原世界。

当前临时验证方法：只在新测试世界启用CS及必要依赖，临时不启用Slower，设置0天/较多并检查自然生成。不要拿原有内容世界卸载内容mod后保存；不要为了本轮诊断自动替用户停用或删除模组。即使无Slower，地形/视线/原生随机生成也意味着并非按键即刷。

## 未做与交接

未改产品代码、世界和安装目录；未让玩家原档卸模组；未从在线游戏内读取最终全局上限；未实机生成整队。已经用真实DLL证实默认限制不容纳最小队伍、确认日志里的0天生效。VPS实现仍需遵守现有单写入者、兼容和候选验收规则。
