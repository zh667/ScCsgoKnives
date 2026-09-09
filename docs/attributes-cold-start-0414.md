# 属性页冷启动优化 0.41.4

用户报告装配台与帮助/物品信息进入武器属性页卡顿数秒。两个入口共用 ScGunAttributesScreen。

## 根因与改动

ScGunAttributes.Ranges 首次 Build 遍历 35 把枪；为了取换弹时间，调用
KnifeAnimationController.ReloadSeconds → ReloadClip/HasAlias/GetProfileDuration/GetReloadSections。
这些查询原先调用 Cs2Rig.Get，反序列化包含全部骨骼曲线的动画文件。
动画缓存只有 12 个条目，遍历 35 枪会挤掉已加载动画；随后 Rows 又会加载刚被挤掉的枪。
UI 图标绘制已经使用平面 slot 图，不需要为了属性页加载全枪动画。

本次在现有 cs2_catalog.json 中生成每个 clip 的 SourceName、Alias、Duration、Events 与 additive 标志。
不包含骨骼、关键帧曲线、网格。63 个武器/刀/投掷物资源的轻量索引约 433 KB。
Cs2Rig 的时间/别名/事件查询走单独元数据，不触碰 12 项动画 LRU；真实动画 Sample 仍读原文件。
继续复用同一个 Resolve/ResolveOrIdle 和换弹分段计算，没有手填或估算换弹秒数。
原子弹枪、逐发霰弹、空弹匣动作、投掷时刻和 additive 别名的既有行为不变。

generate_cs2_catalog.py 生成索引；pack_scmod.py 对照所有源动画检查索引，过期则拒绝打包。
PackageCheck 在任何动画测试预热前计算 35 枪属性，断言动画缓存仍是 0；随后逐 clip 对照包内原动画，
检查全部 63 个资源的元数据一致。原有动画/战斗/布局/0.28.2 迁移回归继续执行。

## 本机测量（非手机整页实测）

同一 PackageCheck 可执行文件，各启动一个新进程，加载各自安装包 DLL。
0.41.3 通过 `--attribute-benchmark` 测量，0.41.4 在冷启动回归入口执行相同查询。

| 测量 | 0.41.3 | 0.41.4 |
|---|---:|---:|
| 首次 Ranges 计算 | 1151.48 ms | 93.64 ms |
| 含 Ranges，遍历 35 枪 Lv0/Lv10 数据 | 2297.23 ms | 96.26 ms |
| 该过程当前线程累计分配 | 281.99 MiB | 1.91 MiB |
| 结束时完整动画缓存条目 | 12 | 0 |

单次本机测量包含 JIT/反射开销，不是手机帧率或整页加载时间保证，也不是进程峰值内存。
日志：output/attributes-baseline-0413.log、output/check-0414-full.log。
手机可查 `[CS_ATTR_0414] clip metadata` 日志确认索引加载时长。

最终 Full/Lite 各通过 7961 项检查（含 64 项新增冷启动/索引一致性检查），0 失败。
两版包内 DLL SHA256 均为 `f24711baa3b947b8e0fabb44cea965605b2b094ac45b28e856947ce8457e054d`。

## 范围

不改属性值、枪械伤害、弹量、成长、存档结构或任何源动画。原工作区三份未提交属性/UI 文案修改原样保留，
本轮不修改/提交它们。完整与轻量版使用相同 DLL。尚需用户实机确认两个入口第一次打开的体验。
