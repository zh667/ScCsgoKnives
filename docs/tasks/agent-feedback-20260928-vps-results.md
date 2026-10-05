# agent-feedback-20260928：VPS实施结果（第一批候选）

状态：P0–P4源码候选完成，Windows离线回归通过；P5仅完成资源盘点。尚未打包、未安装、未实机/手机验收。
写入者：VPS agent。上一写入者（Windows规划）交付后停止；开始时两端Syncthing idle、待同步0、错误null、无冲突文件。
范围依据：[任务](agent-feedback-20260928.md)与[启动提示词](agent-feedback-20260928-vps-prompt.md)。未修改第三方包、原世界、已安装Mods，未写output/，未触碰Zeus未提交改动所在文件。

## 1. 复核Windows诊断

| 诊断 | VPS复核 | 依据 |
|---|---|---|
| 同伴远距卸载Data为空、重建Owner=-1 | 确认 | API 1.9.3.1 DLL反编译：DespawnChunks只带模板/位置/ConstantSpawn/EntityId和钩子写入的Data；SpawnEntity捕获异常后SpawnChunks仍清空SpawnsData；DiscardOldChunks约76800游戏秒删除旧区块记录；旧组件无同伴卸载钩子 |
| 核心完整性扫描不含休眠同伴 | 新发现并确认 | ScGunLoadIntegrity只扫XML中的Slots/掉落物/投射物/移动方块，不扫SpawnsData字符串；ScGunHolders.Scan同样看不到 |
| 台阶 | 部分确认并找到更直接原因 | 原生ComponentPilot仅随机跳（每0.1s 5%），且行走随机跳要求Speed≥1；同伴速度0.7、敌人0.45–0.65，几乎不会主动跳一格台阶。敌人每0.65s SetDestination必然触发重新寻路（m_destinationChanged=true）并每帧直写Rotation |
| 同伴Visible不检查烟雾 | 确认 | 已补 |
| 区域伤害只保护玩家 | 确认 | 玩家C4爆炸同样只按玩家判断，已一并纳入 |
| 创造目录钳子 | 确认 | HasKit只扫前10格 |
| 玩家C4不能拆 | 确认 | 敌方Bombs与玩家charges两个私有集合 |
| 跳跃资源 | 确认包内无；原始导出有 | 见P5 |

## 2. 各批改动

### P0 同伴生命周期（优先完成）

- 招募同伴（CT/T/旧救援）进入世界即关闭原生AutoDespawn，原生空载荷路径不再发生。
- 新增存档子系统`TacticalCompanions`（ScTactical.xdb，兼容清单已登记）：摄像机水平60格外把同伴整体写入账本——实体ID、模板、位置朝向、主人、指令、守卫点、停火、生命、精确5槽value/count、其他mod的OnSaveSpawnData片段；写入后XML回读逐项一致才移除实体。48格内且区块就绪时用原实体ID重建，调用其他mod的OnReadSpawnData，校验槽位后才删除账本项。
- 重建失败保留记录，退避重试5次后阻塞并提示；重新读档再试。损坏或未来格式记录原样保存、从不生成、不猜数值；子系统schema未来版本拒绝加载。
- 休眠同伴的枪通过核心新接口`ScGunHolders.DormantSources`进入holder扫描（排在活动库存之后，不会被当成第二持有者拆分）；账本采用与库存相同的Slots布局，离线完整性扫描可直接读到。
- 休眠前取消未完成换弹（换弹在完成前不扣任何物品，不吞/不复制弹）。死亡、正在淡出、面板打开的同伴不休眠；死亡掉落仍只一次。
- 旧版在淡出中存档造成的“完整实体＋空卸载记录”：读档时取消淡出、删除同ID空记录，保留完整实体。
- 旧版已经丢失主人的空壳：不自动认领；提示“缺少主人记录（旧版远距卸载时丢失）”，空装备的无主空壳可在3秒内再次交互确认移除（需间隔0.6秒，防长按误删）。已丢失的装备无可信来源，不能恢复。
- 拆分轻量：核心新增`ScOptionalAgents.CompanionLedgerProtocol=1`，新探员包配旧轻量核心时明确拒绝并提示同时更新两个文件。

### P1 阵营、避险、召唤公平

- 核心新增`ScFactions`：招募同伴=友军，敌队=敌人，与CT/T外观无关。HE、火、闪光和玩家C4共用：玩家自伤保留；其他玩家及友军只在世界友伤开启时受玩家区域效果；敌方区域不伤敌方；普通生物不变。来源仍是已持久化的玩家编号/敌方标记，主人离线后照样识别。
- `TacticalDanger`：已知危险=本方炸弹、声音范围内（半径+8）的他方炸弹、火区。剩余≤max(12秒, 走出所需时间+3秒)时半径内全体撤离（不开火），12秒前留在半径+3外防守，追击/跟随/守卫目的地被夹到安全环外；站在火里立即走出。撤离点按远离方向扇形备选，卡住换下一个。下包者不再只退6秒。
- 手动敌队：沿玩家指向，距召唤者18–28格、距每位玩家≥18格；找不到整队位置则失败且不消耗信标。3秒预警期内不累计瞄准、不开火不投雷，被攻击立即结束；只作用于新召唤小队。
- 首轮血量：同伴抗性180；敌队步枪/近战120、狙击/爆破100、机枪160。生命仍为0–1比例，受伤比例不变、不补满。

### P2 台阶与寻路（有限改进）

- `TacticalNavigation.Probe`：前方障碍分类（平地/半砖交原生平滑抬升/可跳一格/墙），检查台面与自身头顶空间；只在检测到可跳台阶时下跳跃指令并有0.45秒冷却，不每帧跳、不破墙、不瞬移。
- 跟随地面主人时按同一楼层判定到达（不再无条件忽略高度差，垂直容差0.9）；主人跳跃时用上次落地点。战斗接近仍忽略高度（既有断言保留）。
- 卡住分级：停2秒重规划→一次侧向绕行→提示“同伴暂时无法到达”并暂停4秒（提示20秒一次）。
- 敌人只在目标实际变化时重规划；移动中由原生Pilot唯一转向，站定时用平滑TurnOrder，移除每帧直写Rotation。
- 未做：队形偏移、按角色距离带、掩体选点、队友射线让位、火区作为寻路代价；5/10/20探员性能测量。

### P3 统一拆弹与奖励

- 玩家C4通过临时代理进入同一拆弹入口（不复制、不保存、不改其Fuse/Power/Radius，仍由SubsystemScC4计时和爆炸）；拆除走核心新`SubsystemScC4.Disarm`，终态只一次。自己的C4总可拆；他人C4仅世界友伤开启时可拆。按住拆弹时不会同时开始新下包。
- 创造模式直接视为有钳子（5秒）；生存扫描实际携带背包全部格子。模式变化或钳子丢失仍按原规则取消。
- 奖励：生存（非创造）下拆除“非创造模式生成的敌队”所下的已武装C4，完成者得金属坯件×2、精密机构×1。敌队状态新增`Rewardable`，炸弹新增`BombId/Rewardable`；旧存档缺字段一律不奖励。移除炸弹与写入核心持久收据（`ScGunRecovery.Grant`）在同一帧完成，由核心每秒重试发放，满包时留待空位，读档不重复。

### P4 自动语音

- 核心新增玩家事件`ScAgentVoice.PlayerEvent/EmitPlayer`；投掷事务Commit成功后才发，取消/失败不发。语音包检测到该接口才订阅，旧核心保持原行为。
- 语音设置新增“玩家投掷自动喊话”，默认开，受“玩家主动语音”总开关约束，旧JSON缺字段默认开、未知字段继续保留。沿用玩家冷却、同句3秒防重、全局2声上限。
- 同伴：指令真正变化时立即回应（跟随/守在这里/前方掩护分别用followingfriend/waitinghere/inposition），重复同一指令不说；成功打开面板一次affirmative；装备确实交到同伴栏时thanks。

### P5 跳跃与倒地

- Windows只读盘点（job `464e3322bcea4c0684e8e8b0d837ec6d`）：原始CS2导出`ctm_sas.glb`/`tm_phoenix.glb`各含jump_*/inair_*（knife/pistol/rifle，stand/n/e/s/w及crouch）；腿部骨骼`pelvis/leg_upper/leg_lower/ankle`与包内一致。包内ct/t仅一段预烘动画，确无跳跃状态。
- 本批未改动画和死亡表现：需要在Windows按actor-freeze流程重新派生并预烘Full/Lite的GLB+scanim，再做视觉验收；关节物理倒地仍未开始。不宣称已修复。

## 3. 验证

| 项目 | 结果 |
|---|---|
| VPS类型检查（非拆分核心/探员/语音/PackageCheck） | 通过；缺失的AnimationData嵌入资源仅在临时目录里剔除，不产生交付物 |
| Windows隔离快照构建+探员AI回归（`snapshot_build_test.ps1`） | p0-02/p0-03/p1-01/p3-01/p2-01/final-01；最终final-01：32/32通过 |
| p2-01输入 | 快照 `E:/projects/ScCsgoKnives/.tmp/agentfb-20260928/p2-01/snap`；job `befd76bd60e04025974a008e17bca0a7`；核心DLL与探员DLL哈希见其stdout；测试用临时zip不是交付包 |
| 全量PackageCheck基线对比 | 核心13890/13890；完整探员无新增失败（见第5节） |
| 拆分轻量构建 | 未跑：需Windows分拆暂存脚本 |
| 兼容六向切换、两轮存读、Lite无探员休眠数据 | 未跑：需候选Full/Lite/探员包和CompatibilityCheck（该工具Program.cs有Zeus未提交改动，未触碰） |
| 实机/手机 | 未做 |

新增回归：`TacticalCompanionRegression`（生命周期7项、阵营友伤、同伴撤离C4、指令语音）、`TacticalFeedbackRegression`（创造钳子、玩家C4统一入口/权限/只拆一次、奖励只发一次且读档后发放、旧炸弹无奖励、台阶探测、按需重规划），`TacticalEnemyRegression`新增预警期与炸弹撤离两项，并把手动召唤夹具改为新距离规则（增加距离/方向断言，未放宽原断言）。

## 4. 仍需Windows审查与实机验收

1. 审查本批实际diff（文件清单见第6节），重点：账本兼容语义、友伤矩阵是否符合预期、奖励数值和他人C4权限规则、血量档位。
2. 按build-and-release构建Full、Lite+探员（含`SC_SPLIT`）候选；跑CompatibilityCheck六向＋两轮存读，特别验证：新`TacticalCompanions`组经旧兼容修订/无探员轻量往返后原样返回；休眠同伴的枪ID在完整性扫描中被读到。
3. 实机：CT/T持枪/盾/空手，三指令，走出60格再回来并两次存退；淡出中存档的旧世界；手动敌队距离与3秒预警；敌方下包后全队撤离和12秒后不回追；玩家火/HE/闪光对同伴（友伤开/关）；拆自己的C4、创造直接5秒、生存奖励只一次（满包）；台阶/半砖/楼梯/2格墙/矮屋顶；投掷自动喊话与设置开关、中英CT/T听感。
4. 手机帧时间：多名活动探员下台阶探测和危险查询的开销（每帧少量方块读取，未测）。
5. P5另批：派生jump/inair片段并重新预烘，之后再做关节物理倒地。

## 5. 全量PackageCheck对比

输入：原`output/[API1.9]CS武器1.3.0-全量包.scmod`（sha256 `dc902a45…a97`）；候选是其临时副本，只把`ScCsgoKnives.dll`、`ScCsgoTactical.dll`换成final-01快照构建（sha256 `c61e7075…b8b0`，仅测试用，位于`.tmp/agentfb-20260928/full-04`，不是交付包，资源与其他DLL未变）。Content为`D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Content.zip`（只读）。job `0d2b51b43a90452caae358d4a00ac8ec`。

| 套件 | 原包 | 候选 |
|---|---|---|
| 核心PackageCheck | 13890/13890 | 13890/13890 |
| 完整探员回归（含UI拆弹、资源） | 73项失败22 | 73项失败4，无新增失败 |

原包22项失败中18项是本批新增测试（旧DLL无对应类型/行为，属预期），其余4项在两者上都失败、与本批无关：`optional-package-identity-and-no-bundled-engine`、`native-gltf/ct`、`native-gltf/t`、`npc-full-registry-reload-refuses-without-ammo-loss`，需要Windows另行确认其既有原因。上一轮（job `39d7ed20…`）曾发现候选新增失败`enemy/kit-native-item-draw-carry-and-cs2-sounds`（无实体玩家读取Project空引用），已修复并在本轮通过。

AI-only最终快照final-01：32/32（job `d50b693b108f4a81a6cec81fd065f7c0`；核心DLL `87055537…a98c`，探员DLL `9578efbf…a99`）。

## 6. 改动文件

核心：`src/ScCsgoKnives/World/{ScFactions.cs(新),ScGunHolders.cs,ScGunRecovery.cs,ScOptionalAgents.cs,ScAgentVoice.cs,SubsystemScGrenades.cs,SubsystemScC4.cs}`，`src/ScCsgoKnives/Assets/ScCompatibilityManifest.xml`（`tools/compatibility_manifest.py`重生成，仅新增一行）。
探员：`src/ScCsgoTactical/{SubsystemTacticalCompanions.cs(新),TacticalDanger.cs(新),TacticalNavigation.cs(新),ComponentTacticalCompanion.cs,ComponentTacticalEnemy.cs,SubsystemScTactical.cs,SubsystemTacticalEnemies.cs,SubsystemTacticalBombs.cs,TacticalEnemyState.cs,TacticalModLoader.cs,TacticalPanel.cs,ScSplitAgentMarker.cs,Assets/ScTactical.xdb}`。
语音：`src/ScCsgoVoice/AgentVoice.cs`。
测试：`tools/PackageCheck/{TacticalCompanionRegression.cs(新),TacticalFeedbackRegression.cs(新),TacticalEnemyRegression.cs,TacticalRegression.cs,Program.cs}`。
Windows临时脚本（未同步，`.tmp`下）：`E:/projects/ScCsgoKnives/.tmp/agentfb-20260928/scripts/`。

## 7. 1.4.0打包（用户后续要求）

已按1.3.0三包结构编译并交付 `output/[API1.9]CS武器1.4.0-{全量,轻量,探员}包.scmod`，全部发布门禁通过；详见[1.4.0发布记录](../release-1.4.0-2026-09-28.md)。另把核心/探员包内“需要配套1.3.0”提示改为1.4.0，源 `src/ScCsgoKnives/modinfo.json` 版本改为1.4.0，`package_display.py` 支持1.4.0，`SplitCheck` 版本断言改为跟随核心包版本。
