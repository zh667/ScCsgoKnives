## ADDED Requirements

### Requirement: E1 One authoritative scoring event
服务端 SHALL 以 MatchId/VictimKey/VictimLifeId 去重终结事件；同一事件同时驱动击杀分、死亡、助攻、全场播报和竞技计数器。

#### Scenario: Duplicated or late messages
- **WHEN** 同一击杀重复广播、重新请求榜单或旧局事件迟到
- **THEN** 本局只计一次，旧局不会污染新局，StatTrak 与榜单一致

#### Scenario: Suicide or environment
- **WHEN** 玩家自杀或没有确认攻击者的环境致死
- **THEN** 死亡增加但无人获得击杀分，播报不伪造杀手

### Requirement: E2 Independent kill-method facts
击杀事件 SHALL 保存本发确认的部位、发射时开镜、有效命中段穿烟/穿透及攻击者致盲事实，图标按事实组合，不从动画或客户端自报推断。

#### Scenario: Headshot kill versus nonlethal head hit
- **WHEN** 存在非致命头部命中和另一发致命身体命中
- **THEN** 最终击杀不因先前头部反馈误标爆头；致命头部命中有正确爆头图标

#### Scenario: No-scope and through-smoke
- **WHEN** 狙击发射时未开镜，且其实际致命命中段穿过有效烟区
- **THEN** 可以同时标记盲狙和穿烟；普通手枪不开镜、墙后有烟但命中段未穿过都不误标

### Requirement: E3 Scoreboard, assists and results
系统 SHALL 显示击杀分、击杀、死亡、助攻和剩余时间；有效击杀 +1 分，助攻仅计数，同分并列，房主局长决定结束。

#### Scenario: Assist ledger boundary
- **WHEN** 其他攻击者满足声明的同生命伤害/时间阈值，或伤害来自上一命
- **THEN** 前者增加一次助攻且不增加击杀分，后者不继承助攻资格

#### Scenario: Late join or reconnect
- **WHEN** 玩家迟加入或同身份重连
- **THEN** 收到带事件序号的最新榜单/结算，不重播整局旧播报、不清除已有当局统计、不继承其他连接槽用户数据
