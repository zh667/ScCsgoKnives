# CS武器 1.0.0 正式包

以当前0.42.3代码及本地现有UI调整构建，玩法与资源不变。参考用户提供的 `[API1.9]TravelMap2.7.0.scmod`：文件名采用 `[API1.9]名称版本.scmod`，作者为zh667，ApiVersion为1.9.2.1，ScVersion为2.4.0.0。

- 主包名称：CS武器；版本1.0.0；发布文件 `[API1.9]CS武器1.0.0.scmod`。
- 资源包：沿用1.1.2，发布文件 `[API1.9]CS武器资源包1.1.2.scmod`，与既有ScCsgoResources-1.1.2.scmod字节一致。
- 保留PackageName `zh667.ScCsgoKnives` / `zh667.ScCsgoResources`，依赖仍严格匹配资源1.1.2。不能照抄参考模组的PackageName或空依赖。
- 作者字段统一zh667；主包简介补全枪械、刀具、投掷物、皮肤、枪械台、计数器成长与手机设置，并标明非官方模组。来源与许可证文件保留。
- 原0.28.2迁移、layout5/schema4、枪械编号及记录保持原样。关闭的F7诊断与开发日志继续关闭。

构建仍使用 `tools/dev.ps1` 与 `tools/pack_scmod.py --edition full --split-resources`，然后为生成包复制上述社区文件名。文件名变化不改变包内数据；校验社区文件与原始打包文件SHA256一致。仅交付全量版。

发布文件验证：10672项打包检查、0失败，包含0.28.2迁移和现有记录兼容回归。报告为 `output/reports/ScCsgoKnives-1.0.0-check.json`；这是离线检查，不是新一轮实机验收。

主包SHA256：`049bbd114cd98f9bca27faed8dcb8f1d3fd373c472e0403d6a1313500488fbf8`。
资源包SHA256：`d07edc44a4666d0d484c8733044581e59d58658a658b754a807e9b1e08b79583`。
