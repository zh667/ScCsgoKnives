# VPS 实施手册

## 1. 先读什么、当前授权是什么

工作目录 `/home/dev/source-sync/ScCsgoKnives`。读取根 `AGENTS.md`、当前决策入口 `docs/tasks/deathmatch-addon-design-20261002.md` 的“当前状态”，然后本 change 的 proposal/design/specs/tasks/verification；按改动读取 agent-guide 中兼容、构建、资源指南。当前已有方案的 A/B/C/D 已由用户确认，不再把这四个问题退回用户。

本轮用户请求是详细规划，尚未在这里执行产品代码。收到用户将本手册作为实施指令的消息后按该消息范围开工；不能因为同步看到文件就自动启动实现。此手册不授权安装、原世界写入、改 APK、发消息或公开上传源码/资源。

## 2. 冻结实际基线

1. 确认 Windows 写入者已结束、双方 Syncthing idle/need=0/error=0/conflict=0。不要发未经用户授权的消息或中断其他任务。若有实际重叠写入，先处理不重叠的读操作，协调后才改对应文件。
2. VPS 是无 Git 的源同步目录，不初始化 Git，不 pull/reset/restore。Windows 记录 `git status --short`，保留所有已有改动和当前未发布候选。
3. 记录现有 `output/release-1.4.0/manifest.json` 与包内真实核心、Agents、适配器身份，核对源码是否比 manifest 新；旧文档曾用 mpf1，不据此锁死新的工作基线。
4. 调用现有 Windows worker 的 health，相关工具参见 `docs/agent-guide/collaboration.md`。命令用 argv 数组/结构化参数，不用字符串拼接执行用户路径；Windows cwd 明确为 `E:/projects/ScCsgoKnives`，调用 `./tools/dev.ps1`。
5. 确认 1.4.0 是否已正式发布。未发布先完成模式封套/休眠保存基础能力；已发布则对真实包验证，不能偷偷换包来满足兼容。
6. 原 APK/CS2 原资源/用户地图只读，派生数据到 Windows `.tmp/dev-temp/deathmatch-*`。VPS 只同步小型源码、规格、参数/来源清单、结果摘要，不传大资源。

## 3. 第一轮只解决接口和生命时序的不确定性

先写小的可执行复现，不先铺满 UI 和动画。

- 用实际引擎确定 `OnCreatureInjure` / `CalculateCreatureInjuryAmount` / `DeadBeforeDrops` 等真实顺序与可取消性，列出枪/刀/爆炸/环境伤害的入口；方法名以当前平台实际定义为准。
- 证明阻止原版掉落/销毁后玩家还能正常恢复输入与血量，且不会继续作为活靶/肉盾。若只凭纯函数状态测试无法证明，记录具体运行时问题并安排最小隔离验证，遵守用户当前“不启动游戏、自己实测”的偏好；不得声称该缺口已通过。
- 证明模式提供者为空时，核心原行为通过基线对照；证明 100HP 单一路径不与现有护甲/创造模式叠加。
- 对未知模式数据做实际序列化往返，验证引擎数据库不会先因缺失 .xdb 子系统拒载。不要等整个附属包做完才发现 1.4.0 读不了新增实体。

第一轮输出应是小接口 diff、调用链证据、默认生存回归和明确待证项，不发行完整产品，也不把“后面再处理存档”作为默认方案。

## 4. 实现分工与文件布局建议

同仓新建独立项目，建议目录按职责分为 `Session`、`Arena`、`Loadouts`、`Combat`、`Respawn`、`Net`、`UI`、`Persistence`。这只是文件组织建议，避免为了架构创建大量空抽象类。

核心只拥有通用扩展接入、原有枪记录/库存事务和网络；DM 拥有模式状态、竞技规则、图标/UI及临时统计。把“有效规则”解析集中为一处作用域对象，不能在十几个方法分别添加容易漏掉的 `if (Deathmatch)`。

规则接口建议包含但不局限于：有效武器数值、材料/磨损/成长政策、角色/伤害权限、竞技护甲结算、提交后的攻击事实、致命接管。UI/持有者/能力注册分离，退出世界撤销。具体签名在第一轮依据实际调用链定稿，不承诺本文件里的建议 API 已存在。

接口准备后严格按 tasks §2–7 依赖推进。可以先做简单调试面板供离线驱动，但最终必须交付用户确认的轮盘和全部双端 HUD，不用调试面板冒充完成。

## 5. 关键伪代码与禁止捷径

```text
on_client_loadout_request(connection, request):
  validate connection -> player, session, match, life, request revision
  validate catalogue / skins / per-life grenade limits / mode phase
  if Alive: save DesiredLoadout only
  else if Preparing or DeathView: prepare next-life choice
  else if SpawnProtected and authority deadline not passed:
    stage complete record + slot change; commit once without extending immunity

on_authoritative_damage(attack):
  validate victim life and combat permissions
  compute one mode-specific HP/AP result, preserve shot facts
  if lethal and TryFinishLife(victimLife):
    disable body combat/input; cancel prior actions
    emit unique KillEvent; begin DeathView; stage next loadout
  else apply exactly one permitted damage result

on_respawn_ready(player, version):
  verify matching pending life/loadout and bounded readiness
  revalidate reserved authored spawn point
  commit position + health/armour + full inventory + active weapon + new life
  set three-second authority deadline; publish coherent commit
```

这是流程示意，无法替代事务故障测试。禁止用“客户端上报杀了谁”“客户端自动加弹”“先空背包再异步补发”“先传活人再慢慢装枪”“归还即把永久 ID 改成另一型号”实现。

竞技 StatTrak 与生存字段独立；初稿中的 Zeus 无记录、池恒为人数×2 等假设已废弃。池上界必须可计算和执行验证，跨命/跨局不会无限新增。

## 6. CS2 数据/资源作业

Windows 先清点现有 `E:/projects/CSMCReverse` 和 `.tmp/dev-temp/deathmatch-20261002/` 的来源描述，复用哈希相同的已提取数据。核对本机 CS2 构建，不对旧参数表直接贴“最新”。新增小型 `source-map` 记录参数名、资源成员、单位与转换；仅执行必要提取，不复制整个 VPK。

轮盘是 CS 风格需求；计分板/击杀图标/死亡/保护效果参考并复用可用 CS2 资源。源引擎的 UI/材质/粒子控制逻辑要重建并测 GLES，不能拷文件后声称效果自动生效。未定位到原白色保护效果时在结果中明确，继续调查，不偷偷用自制白色覆盖标作原版。

按 35 枪×实际特殊分支分组建立行为参考。金标准数据与被测算法不能由同一个函数生成；参数精确比较，随机散布/后坐力轨迹用预先定义容差。刀、Zeus、投掷物与护甲分别处理。竞技规则覆盖不改生存调校文件。

## 7. 验证与候选交付

遵循窄迭代：最小失败复现 → 相关代码 → 对应离线检查 → 冻结候选 → 最终门禁。现有工具优先扩展可复用小套件，不每次 UI 调整都烘全部动画或全量打包。

OpenSpec（仓库当前固定工具；Linux 可用同一 npx 命令，Windows 经 dev.ps1）：

```powershell
$env:OPENSPEC_TELEMETRY='0'
./tools/dev.ps1 npx --yes @fission-ai/openspec@1.14.0 validate deathmatch-addon --strict --no-interactive
```

如网络不通优先使用已缓存工具，不能修改规格降低验证强度。不要运行 init 覆盖 AGENTS，也不在实现验收完成前 archive。

所有功能证据填 verification：生产注册与真实序列化、生命/局/租约隔离、真实引擎回调、CS2 对照、布局测试、用户观察分别标明。代码阶段完成并不免除手机和实际联机验收；仅有平台路径故障时，继续其他独立任务，准确标记 Android 客户端阻塞。

正式候选：独立 DM 包，以及确实需要更新的核心/匹配 Agents（不要无关升级所有包）。Full+DM、Lite+DM、Lite+Agents+DM 均验证包内引用/版本/资源，清单记录网络与保存兼容边界。配套版本必须说明清楚，不把开发命名里的技术标识塞进玩家 HUD。

只交付 `output/` 候选，不自动装 Mods/手机或导入原玩家世界。当前用户偏好由其实际试玩；给出少量完整验收路线，并明确未验收状态。最终包经相应门禁后按常设授权替换对应候选与清理本任务可再生中间物，不删除原资产/世界/来源包。

## 8. 可复制的实施启动提示词

> 在 `/home/dev/source-sync/ScCsgoKnives` 按 `AGENTS.md` 和 `openspec/changes/deathmatch-addon/` 实施死亡竞赛独立附属包。当前决策入口是 `docs/tasks/deathmatch-addon-design-20261002.md` 顶部；其历史初稿已被替代，用户选择已确认，不再询问 A/B/C/D。先核对单写入者、同步与实际最新基线，保留未提交工作。按 vps-guide/tasks 的依赖顺序推进：专用竞技世界、个人混战、免费 CS 风格轮盘、全部支持外观开放、下一命配装与安全枪池、CS2 战斗规则、死亡镜头与安全点复活、3 秒保护、全场击杀/榜单、完整 Windows/Android 可编辑 HUD。阶段拆分不减少最终范围。核心仅加必要通用接口，普通生存及从正式 1.4.0 起的存档双向兼容保持。先实现未发布 1.4.0 所需的未知模式载荷保留基础。Android 给定包的 TempNetworkWorld 故障由平台负责，不能擅改 APK或把未验证手机场景标绿。使用 Windows worker 做资源/构建/离线验证，候选放 output，不安装、不写原世界、不自行启动游戏；确有无法离线证明的行为需明确列为待实测。每阶段更新证据和未验收项，完成后给出包及用户测试步骤。默认同仓独立项目；只有记录了真实拆仓必要性才考虑独立 GitHub 仓库，不复制核心或上传原始大资源。

以上提示词供用户交给 VPS 时使用；文档写入/同步不等于已发送或已开始执行。
