# 死亡竞赛入口移入 CS 武器模组设置

状态：实现完成、正式构建验证中。Windows Codex 为当前写入者。
用户要求：死亡竞赛不要放在普通设置页面，在“模组设置 → CS武器”中开启；最后打包出来。
源码分支 `fix/deathmatch-settings-entry`，隔离工作树 `E:/projects/ScCsgoKnives-dm-settings-20261009`，由 main d3a9568 建立；纳入尚待合并的 PR #6 元数据和发布工具修复，防止本次打包回退已纠正的字段。未授权合并本任务到 main。

- 移除 DeathmatchModLoader 的 OnSettingsScreenCreated 注册和按钮。
- 在既有 ScGunModSettingsWidget 打开的 CS 武器设置页增加“死亡竞赛设置”，仅加载死亡竞赛提供方时出现。核心通过可选委托接入，不静态依赖附属程序集；核心重载清除委托，附属初始化重新注册。
- 未进世界仅提示进入世界，不启用任何世界。进入世界后恢复 Game 并经既有 DmHud 请求打开菜单；世界启用仍由 DmMenuPanel → EnableArena 处理，保留房主权限、只作用当前世界和防止战斗中改图的规则。打开入口本身不改变世界模式，不自动保存本页未提交的枪械设置。
- 全量和轻量分别通过正式流水线构建，统一身份，元数据继续 1.5.0 / zh667 / API 1.9.3.1。不改资源、不安装 Mods、不写用户世界。

## 过程与验证

直接项目编译通过，但初版增量打包器把 Full DLL 同时用于 Lite，未遵循 SC_SPLIT 归属；这批仅在 `.tmp/dev-temp/settings-fix-20261009`，拒绝交付。生成阶段常量身份和临时打包脚本已移除，改用现有正式流水线。初版还遇到工作树没有 output 和未构建 voice 两个前提错误；没有用这些失败结果宣布发布成功。

正式阶段为主工作区 `.tmp/dev-temp/dm-settings-release-20261009`。需执行原生设置入口专项、Full/Lite 发布检查、模式/联机/加载、全部正式历史读取器兼容、元数据和成员不变检查。结果、哈希与清理回执待补。
