# 0.30.0 社区反馈 M1b：原版生物爆头、黄色命中标记、模组接入口

日期：2026-09-07。对应 `docs/community-feedback-plan-2026-09-07.md` 的 M1b（F12）。

## 判定方式

- 每颗弹丸先照旧用 0.35 容差的身体盒射线找到目标，再由 `World/ScHeadshotProbe.cs` 用目标**当前姿态**判断打中哪块：
  用 `ComponentModel.ProcessBoneHierarchy` 从模型自己的骨骼变换重新合成世界矩阵（不用含相机变换的
  `AbsoluteBoneTransformsForCamera`），把每个网格的骨骼局部包围盒当作判定体，射线在骨骼空间做 slab 测试，
  **最近的网格盒决定部位**：身体挡头就是身体，只擦过容差盒、没穿过任何网格盒的记 Unknown，按原来的普通命中结算。
- 网格盒在墙后面（容差盒在墙前）时按打墙处理，不再隔墙结算。
- 头区 = 挂在 `Head` 骨骼上的网格盒（`ScHeadRule.Default`），可按模型用 `Shrink` 收缩。
- 原版渲染器只在模型被画出的那一帧推进动画（`SubsystemModelsRenderer.PrepareModel`），所以本帧没画的生物姿态是旧的：
  这种目标记 Unknown，只算普通命中，不用旧姿态误判爆头。
- 倍率 `ScHeadshot.Multiplier = 2.0`（估计，规划的首轮测试起点），乘在 F13 之后的单弹丸威力上，只乘一次；电击枪不加成。
  霰弹按弹丸分别判定，只有打中头的弹丸翻倍，整枪只结算一次伤害/硬直/击退。
- 命中标记：击杀红（0.45 s）> 未击杀爆头黄 `RGB 255/210/50`（0.22 s）> 普通白（0.22 s）。同一枪多个目标按优先级合并；
  新事件覆盖旧事件，上一枪的红不会盖住下一枪的黄。只有确认扣血才出标记；击杀音效逻辑不变。

## 生物支持

`docs/headshot-support-0300.md`：73 个原版实体模板逐条列出。32 个模型有 `Head` 骨骼网格并已注册在
`ScHeadRules.VanillaHeadModels`；9 个鱼类模型没有 Head 骨骼，只算普通命中；羊驼模型不在本机 Content.zip。
其他模组的生物：用原版四类模型组件且有 Head 网格的自动按默认规则；否则调用
`ScHeadRules.Register("Models/Xxx", rule)`（`rule = null` 表示禁用）。

宽头模型（Bull、Bison、Moose、Reindeer、Gnu、Cow、Lion 等）的角/耳和头是同一个子网格，无法按材质拆分，
首版头区包含角，实机看到角上出黄标再按轴收缩校准。

## 验证（VPS 无头）

- 运行时自检 5855/5855，新增 `headshot-geometry`（俯射中头、仰射身体挡头、盒内、擦边 Unknown、射程外、非单位方向）、
  `headshot-bind-pose-compose`、`headshot-rules`、`hit-marker-priority`。
- PackageCheck（含 Content.zip）新增 `headshot/*` 34 项：注册表与 Database.xml 推导集合一致；鱼无头；
  32 个模型逐个用引擎的 Collada 解析出绑定姿态，头盒在身体底之上、尺寸合理（人类头 0.30×0.37×0.30 m、狼 0.38×0.51×0.44 m、长颈鹿头高 3.18 m），
  从上往下的射线命中头，身体中心射线命中身体，远处射线 Unknown。结果见 `docs/community-m1b-0300-packagecheck.json`。
- 日志：`shot ak47: 狼 part=Head at 12.3 m (Models/Wolf)`，每人每秒最多 4 条，爆头不限流；Unknown 会带原因
  （`no head rule` / `pose not refreshed` / `outside every mesh box`）。

## 实机要看

站着/低头/转头/奔跑/跳的狼或牛：打头黄、打身白、打死红；从背后打身体不出黄；墙挡头不结算；
霰弹只有头部弹丸翻倍（MAG-7 全中头约 72，全中身 36）；电击枪打头不出黄；鱼不出黄；第三人称视角一致。
角上出黄标的模型截图给我，我按模型收缩头区。
