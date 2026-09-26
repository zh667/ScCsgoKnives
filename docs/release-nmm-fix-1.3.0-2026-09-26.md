# 1.3.0：官方 NekoMeko Model 1.1 加载修复

后续交付：同日的 [人物加载卡顿修复](release-actor-freeze-1.3.0-2026-09-26.md) 仅替换全量包，并保留本次 NekoMeko 修复。本文全量哈希为上一阶段历史记录；轻量包仍保持本文哈希。

日期：2026-09-26。用户授权修复后覆盖当前全量、轻量包，公开版本继续使用 1.3.0。

## 原因

玩家提供的 Game (3).log 显示，官方 NekoMeko 已以 sc-nekomekomodel, Version=0.0.0.0 加载，但 CS 内置外观组件在 TacticalAppearanceIntegration.Initialize 的 GetTypes 阶段请求 Version=1.0.0.0，触发 ReflectionTypeLoadException。这个 1.0.0.0 来自先前本地源码构建的 SDK 默认程序集版本，与模组对外显示的 1.1 不是同一个版本字段。

日志中后续的 Cannot convert string Center to Game.WidgetAlignment 是类型加载失败后的连锁错误。旧交付包配合官方 NekoMeko 已在原生无界面检查中复现同一缺失程序集错误；不是要求玩家改用本地修改版。

## 修复范围

- 编译参考程序集显式使用官方的 0.0.0.0，内置外观组件据此重新生成。
- 对旧本地 1.0.0.0 构建增加限定请求程序集及完整名称的解析处理，只服务 CS 外观适配器；不修改全局 DLL 映射或第三方包。
- 每份总包只替换 ScCsgoTactical.dll 和 Integrations/ScCsgoAppearance.bin。全量其余 1630 个成员、轻量其余 1632 个成员的内容与原始压缩字节保持一致。
- IL 范围核对：战术 DLL 的 29 个顶层类型中，只有 TacticalAppearanceIntegration 及其嵌套实现改变；外观适配器 6 个顶层类型的方法与字段比较无变化，依赖程序集版本修正。
- 核心玩法 DLL 保持 SHA-256 dd8120ff30f07e4ec32c474304bd98077c424888262def3261d0d545126e44fc。既有电击枪修正、道具 ID、v5/schema6/rules7、手动备份策略及全部资源保留。

## 当前交付

已在项目根 output/ 覆盖同名文件。根 modinfo、核心身份、bundle 及兼容家族的公开版本继续是 1.3.0。

| 文件 | 字节数 | SHA-256 |
|---|---:|---|
| [API1.9]CS武器1.3.0-全量包.scmod | 493,814,303 | 82627ceded4e6f5fb7edfc203373c093f8d954790d7e5b9cb3f825bc41ca8f08 |
| [API1.9]CS武器1.3.0-轻量包.scmod | 91,206,182 | bfa85bcf02cd40246b883099367ee7a3a8bd7bac8c1e8b1602f5f8a8d596c9d7 |

## 验证

- 官方 NekoMeko 1.1 与旧本地构建各测试 Full/Lite：完整依赖、反向加载顺序、均缺失、仅 NMM、仅 Neo、禁用 NMM、Neo 版本不足，共 28 组、440 项检查全部通过。
- 两包各通过 12 项原生资源/世界加载门禁和 6 项原生兼容加载检查，共 36 项。
- 从当前 1.0.0、1.2.0 兼容修订包及本次 1.3.0 候选包提取核心 DLL，重新通过 243 项双向状态/变更/两次存取/异常处理检查。
- 合计 719 项正向检查，失败 0；另保留旧包缺失程序集的预期失败作为负对照。测试报告按候选包哈希与生成时间绑定，发布后再次校验同一哈希。
- 官方第三方输入 SHA-256 为 12a659a67676640203277b9ad862d91e3116c4b3e3db84f35fdf9e0003cdeafc；测试时旧本地包 SHA-256 为 7345a25427dc15bed8dcd49d7d7ad8c241194f68af663d2ca82e1fd015c233f2。

测试使用 Windows API 1.9.3.1 的原生加载、数据库、资源与存档逻辑。Neo 测试夹具为 1.4，玩家日志为 1.4.2；未声称复现玩家所有模组组合，也未完成图形世界或 Android 实机验收。未安装到 Mods、未写入玩家世界、未修改第三方包。

原始诊断见 nekomeko-official-load-audit-2026-09-26-evidence.json；交付、测试与 IL 证据见 release-nmm-fix-1.3.0-2026-09-26-evidence.json。打包脚本 tools/package_nmm_130_fix.py 将既有交付保留在项目 .tmp 内供开发回溯；这不是游戏自动世界备份。
