# Windows复核：c08已交付，c09护甲未通过，不能称全任务完成

2026-09-29。用户要求查看VPS完成情况。本轮只读检查代码、实际包哈希、worker持久任务、测试报告、离线图片和最新Bugs/Game.log；不修改玩法、不重跑构建、不安装或写世界。使用游戏故障诊断技能区分源码、离线验证和实际运行证据。

## 结论

确有实质进展：c08完成此前R2大量缺口、C4、地形、轨迹、部位判定和敌人对玩家伤害；实际output三包与清单一致，当前Mods全量也是c08。整个护甲/头盔需求仍未完成，且最新c09已经跑过但门禁失败，不再是“请求是否受理未知”。

### 实际交付身份

- 全量526629508字节：538a74e4b5b874b4d2e4b90b3aa44ff3cd954f34531716803e411107c8a8844d。
- 轻量35519997字节：de4dd5e96b524d1e7fae7ce65aaf19a8a20480a399f56713493055a197cb79df。
- 探员38451834字节：03d9ac7d465fc0983dafc1e85cbf34e982e4ce6a6f3a0b0be5eea0986af38454。
- output/release-1.4.0/manifest.json明确revision为R2/C4+H1/H2/H4；不含H3装备。不能从当前源码的ScArmorBlock/ComponentTacticalArmor推断安装包已经有护甲。

### c08检查记录核对

worker job ab8fd711fec4452d9b6977c7d1356c4e（hab-c08）status=succeeded、exit0、日志无截断，failed steps为空。保留报告实际计数：AI全量/轻量各59/59，视频反馈各292/292，C4各65/65，主套件13903/13903和14206/14206。拆分、原生、motion/throw/hotspots/ui有通过记录。

注意战术全套件不是全绿：100项仍有4项被KNOWN_TACTICAL白名单容许。至少“npc-full-registry…”还将Next=1023当满表，已不适合扩容后的66558条容量，这是一处具体的旧夹具错误，不能永久白名单代替修订。另两项native-gltf跳过缓存层、包身份断言也应明确适用条件并修工具，保持实际验证意图。

本轮实际看过的离线图：c08/throw/ct-c4-plant-commit-1-side.png、ct-grenade_molotov-strong-crouch-held-side.png、ct-grenade_hegrenade-strong-run-held-side.png，以及motion/ct-hit-regions-crouch-side.png、t-hit-regions-stand-front.png。可以确认已生成对应姿态和头/身区域图，不能用这些静态图替代释放时刻连续录像、玩家视角和音效听感。并非宣称整个96组合全由Windows逐张审阅。

## 用户截图与自然刷新

截图的“候选在玩家视野内被拒2次”表示避免眼前凭空刷怪的累计拒绝，不是ERROR，也不是全世界只试两次。它不等于刷新成功或永久失败。

最新Bugs/Game.log里，Mibritish Liwi在16:09:21加载生存，16:09:51设置0天/较多。后续限制显示普通上限26，非之前Slower的2；阶段中还有budget-full、in-view、fluid等拒绝。

明确成功证据：16:22:37.264自然日志squadsCreated=1；16:24:34.482–.491另有kind=squad requested=3 success=True，16:24:36.189自然汇总再记squadsCreated=1。这说明本次这份生存世界已实际自然生成过，不再是“从未刷出”。仅凭日志不保证生成点在玩家可见范围或玩家遇见了它，也不保证固定等待时间。首次成功汇总距设置约12分46秒，不是精确首次生成时刻。

16:26:04后又加载Vislandsba创造世界，16:28/16:30的blocker=Mode符合创造不自然刷新的规则，不能和之前生存段混在一起。

## c09更新：已执行，失败，不应重交同request

hab-c09 job ef72aee53ee44cdd86ca76916ddb4dfc：status=failed、exit1，最终failed steps=['gates']。构建/生成候选确实完成，motion/throw/hotspots/ui通过，但护甲门禁没过。先查询此原job和报告，不重复提交hab-c09。

| 报告 | 失败情况 |
|---|---|
| ai-full / ai-lite | 各1/63失败：armor-npc-regions-once-sounds-dormancy-spawn-and-drops |
| tactical-full-suite | 5/104失败：4旧失败+上述新增护甲失败 |
| main-full | 3失败：装配台菜单数量两项、setup-or-layout |
| main-lite | 4失败：上述三项+optimization/texture-count-and-format |
| vf-full/lite；c4-full/lite | 292/292；65/65通过，但不是护甲全套证明 |

具体诊断：

1. NPC护甲测试计算Infinity：TacticalEnemyRegression.Enemy用new ComponentHealth{Health=1}但未Load；该测试随后只设置AttackResilience=100，未设置AttackResilienceFactor=1。实际getter是m_attackResilience*AttackResilienceFactor，默认0导致除0；生产Load会初始化1。因此它首先是夹具不完整的证据，不是已证实游戏无限伤害。失败发生在第一段断言，后续音效、休眠、生成、掉落部分没执行，不能因为名字包含这些就称已测。
2. 装配台检查出现KeyNotFoundException: ScArmorBlock未注册，提前退出大量后续检查，默认全量数量从13903降到12585。先修正确注册/实际运行夹具并复跑，不能只更新菜单count或删断言。
3. Lite增加PNG护甲资源后，原轻量texture-count-and-format断言失败。应决定并实现一致的Lite资源策略/正确来源清单后验证，不把未查明的格式差异默认为无关失败。

c09候选仍在.tmp/completion-140-20260929/c09/candidate，未替换output；用户当前安装c08，不受c09未交付逻辑影响。

## 仍需补齐的工作

- H3：修c09门禁并运行后续覆盖；可见护具佩戴附件未做；新物品在历史兼容版本的载体与穿戴保留、两轮存读/六向兼容未完成。
- 护甲新类型不能让旧包静默变空气，旧同伴休眠7槽不能以“拒绝并保留”冒充正常版本互换。明确回移范围，必要时形成精确旧包修改清单，不修改玩家原世界。先前用户已有长期双向兼容要求，不能把整个读写保护当可跳过；真正需要扩大的历史包版本范围才说明并确认。
- C4雪/半砖/坡地离线夹具未执行；暂停只是时间倍率模拟，不等同真实暂停菜单。
- 新降伤表只经代码/抗性100夹具验证，男女/等级/疾病/三五人集火实战仍未验收。不能把0.022的测试掉血当一级玩家实际2.2%。
- 头盔/背心命中音效在c08无装备触发，源码虽已有对应样本，不代表玩家能实际听到有甲分类。源headshot_armor样本峰值偏低，多层事件和实际听感未核对。
- 所有实机录像/手机、NPC投掷/安装连贯性、NMM真实游戏表现仍缺；本次复核不构成这些验收。

## 建议下步

维持c08正式包，继续c09护甲门禁与兼容，不再把“是否执行未知”当阻塞；修复工具初始化/方块注册并恢复完整检查覆盖。把完成状态写成“c08动作与基础战斗已交付，H3未完成”，不要写全任务完成。用户本次是复核请求，本轮未替VPS修代码或提交新构建，也未发跨agent消息。后续实施按既有任务授权和共同规范进行。
