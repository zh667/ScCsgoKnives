## ADDED Requirements

### Requirement: C1 Mode-scoped combat profile
竞技规则 SHALL 通过世界/参与者范围的有效规则提供者解析；现有生存数值不被全局重写。竞技外观、皮肤、计数器、人物等级与成长 SHALL 不提供强度优势。

#### Scenario: Skin and level comparison
- **WHEN** 两名竞技玩家分别使用原皮/带皮肤/计数器外观及不同生存等级
- **THEN** 同型号在相同竞技状态下具有相同伤害、射速、散布、耐久消耗与充能规则

#### Scenario: Same core without active deathmatch
- **WHEN** 核心装有扩展接口但当前世界没有启用 DM
- **THEN** 原生存伤害/成长/皮肤/Zeus 行为与基线一致

### Requirement: C2 Authoritative 100-health armour settlement
每次复活 SHALL 恢复 100 HP、100 AP 和头盔，伤害使用已核对的 CS2 部位/穿甲/剩余护甲规则，实际生命与 HUD 一致，不能叠加生存护甲或原版衣物重复减伤。

#### Scenario: Armour runs out or a leg is hit
- **WHEN** 各武器命中有甲部位、无甲腿部或护甲不足以吸收本发
- **THEN** HP/AP 分别按核实公式结算一次，覆盖剩余护甲耗尽边界，不套统一固定减伤

#### Scenario: Creative mode map
- **WHEN** 地图由创造模式搭建但玩家已进入比赛
- **THEN** 参赛者实际可受竞技伤害，编辑者的建造权限与保护不泄漏给参赛者

### Requirement: C3 Versioned CS2 weapon behaviour and provenance
系统 SHALL 锁定 CS2 数据构建/资源哈希并对 35 枪、刀和允许装备记录参数、单位转换和行为证据；弹道、后坐力、恢复、射速、装填和穿透 SHALL 按验证的 CS2 规则实现，不能仅复制配置便宣称等效。

#### Scenario: Movement and recoil comparisons
- **WHEN** 对固定武器分别测试站立、移动、蹲伏、跳跃、开镜、连射和停火恢复
- **THEN** 与匹配 CS2 构建的参考轨迹/弹着数据按预先记录的误差标准比较，未验证项目明确未验收

#### Scenario: Material conversion
- **WHEN** 子弹经过不同厚度的 SC 方块或未知第三方材质
- **THEN** 使用版本化材质/单位映射和明确未知默认，记录穿透事实，不把未知材质标记成已精确复刻

### Requirement: C4 Normal reload with infinite reserve
竞技枪械 SHALL 免备弹材料且不磨损，但保持有限弹匣、正常换弹时序和服务端射速校验；投掷物按房主开放范围与每命数量控制。

#### Scenario: Empty magazine or interrupted reload
- **WHEN** 枪打空、换弹被切枪/死亡/菜单操作中断
- **THEN** 状态按核实武器规则结算，不扣生存材料、不双倍补弹、不恢复旧命换弹

#### Scenario: Forged rate or grenade purchase spam
- **WHEN** 客户端虚报射速或反复购买已达本命上限的投掷物
- **THEN** 服务端拒绝超限操作并一致同步，不因免费模式绕过验证

### Requirement: C5 Fair hit geometry and complete attack routing
竞技人物 SHALL 使用可验证且公平的命中部位契约，所有相关伤害路径走同一竞技判定；未知外观不猜爆头，不能利用模型缩放获得优势。

#### Scenario: Different player appearances
- **WHEN** 受支持的 T/CT、原版男女或可选外观参与同一竞技场
- **THEN** 命中规则符合统一契约，未经验证的外观有明确兼容处理，普通生存外观不被全局改动
