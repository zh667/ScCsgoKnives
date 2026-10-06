# 连续闪光与友军致盲

状态：1.5.0 全量包已重新打包并替换 output，SHA-256 `7448e340…`；15 组发布检查全部通过，实机视觉/Android/真实 MP 双端验收仍待完成。当前写入者：Windows Codex；基线 `4330d51`，分支 `feat/survival-squad-feedback`。本轮没有 VPS 交接；开始时没有 Syncthing/Claude 进程。保留三项原有未跟踪文件。

## 用户要求与范围

用户确认每颗闪光独立判定，屏幕内可见爆炸重新触发白屏；已有致盲不能免疫后续闪光；弱闪不得缩短已有强闪。追加友军的闪也致盲玩家，不受友伤开关影响。沿用已明确的屏幕内保底与遮挡规则，不调整范围、角度曲线、减弱闪光设置或其它伤害规则。修改闪光子系统和相应回归，不安装或修改原世界。

## 证据与方案

- 视频 `D:/下载/QQ20261006-114211-HD.mp4`，约 4.2 秒和 16.1 秒可见爆闪却不重新白屏；只有画面，没有当时运行日志，免疫是代码确认的冲突，不能声称逐事件运行追踪。
- 原判定先调用 Friendly，再以 ImmuneUntil 跳过新闪；客户端 BlindLocal 直接覆盖旧状态。
- 已在原有发布阶段核心上运行新增回归：89/96，7 个新增案例失败（连续闪、友伤关闭时队友闪、客户端弱闪缩短强闪），旧有 89 项通过。原始报告 `.tmp/dev-temp/flash-before.json` 记录 DLL/引擎 SHA-256。
- 新命中结束时间取 max(旧结束时间, 当前时间+本次时长)，以剩余时间重启白屏和淡出；单机/服务端与客户端共用 ApplyBlindness。服务端发送合并后剩余时长。闪光跳过伤害阵营过滤；HE/火焰继续原规则。
- 保留旧 ImmuneUntil 字段及存档表示用于旧读写器和有界记录清理，完全取消其新闪门控；不更改存档结构。

## 验收矩阵

| 场景 | 检查 | 结果 |
|---|---|---|
| 自己、另一玩家、敌人投掷 × 友伤开关 | 实际 Detonate 连续触发、弱闪保尾、强闪延长、恢复后立即可中闪 | 6 种组合通过 |
| 遮挡与屏幕外、远处屏幕内 | 实际 Detonate，不穿墙、不改远闪保底 | 通过，包含遮挡不重置已有闪光 |
| 客户端连续收闪 | 实际服务端 Detonate→OpBlind→客户端处理；BlindLocal 不能缩短原有效果 | 通过；模拟传输，非真实 MP 双端 |
| HE 友伤 | 原 Friendly 判断不变 | 6 种来源/开关组合通过 |
| 保存/载入 | 保留原状态字段、实际子系统 XML 往返 | 连闪状态两轮往返通过 |
| 实机第一人称白屏、双端联网、Android | 用户视觉验收/实际双端与设备证据 | 未运行 |

用户随后明确要求“重新打包，替换 output 里的”，交付记录见末节。以下验证记录是打包前阶段的历史证据。

## 验证记录

- 生产代码仅修改 `src/ScCsgoKnives/World/SubsystemScGrenades.cs`；新增回归由 `tools/GameplayFeedbackCheck/Program.cs` 和 `tools/PackageCheck/TacticalEnemyRegression.cs` 的实际 runner 执行。弱闪合并为旧剩余时间并重启曲线，不累计旧完整时长。
- 使用 SCAPI NuGet 1.9.3.1，离线内存场景，不打开安装包或玩家世界。引擎 SHA-256 `2ef7c1918ea9d09f52a807719909bf28c722bd3a2793eeaf81db0c343b9eb9d3`。
- 普通源码构建被共享 VBCSCompiler 锁定 obj DLL 阻止；没有停止其它进程，改用现有隔离发布阶段。通过 `./tools/dev.ps1 python tools/release_full_150.py build` 构建，核心 3.59 秒、战术 2.26 秒，保留已交付内嵌资源。
- 最终执行 `./tools/dev.ps1 dotnet run --project .tmp/dev-temp/release-full-150/feedback/GameplayFeedbackCheck.csproj -c Release -- .tmp/dev-temp/flash-final.json`，97/97 通过，约 9.6 秒（含 runner 编译）；`git diff --check` 通过。
- 核心 DLL `.tmp/dev-temp/release-full-150/core/bin/Release/net10.0/ScCsgoKnives.dll` SHA-256 `7de5c8d18b8977d0ed64db2e9a44cd899715933b24a7a7a04e6758484b4c1a38`；战术 DLL `51bac2c63cba531ac1ff1a9d0c944cf176e4a4148f11098059e886e6000b7a7e`（源码未改，构建身份随核心更新）。
- 原失败证据 `flash-before.json`；普通构建失败 `flash-after.log`；新增网络 fixture 的编译字段名错误 `flash-after-stage.log`、缺少 Health/远端视角前提失败 `flash-after.json` / `flash-after-stage2.log`；最终 `flash-final.json` / `flash-final.log`，均在 `.tmp/dev-temp` 保留。修正 fixture 前提后实际断言视角消息已接受，再执行真实服务端判断；没有为测试修改产品的前提保护。
- 复用已有隔离阶段，没有新增资源副本或清理旧阶段。本轮无删除、无安装、无输出替换。output 1.5.0 仍为 `0ca107d6d6d531effe7af4341063fd2f5c89c6c207ed42135a0012e4a314f424`。
- 下一步用户验收点：快速连续投闪每次重新白；关友伤仍被队友闪白；强闪未结束时弱闪重新白且不提前恢复。交付新安装包前仍需针对候选包执行发布门禁，不能把本轮离线检查当成正式包/Android/真实联机验收。

## 重新打包交付（2026-10-06）

- 已替换 `output/[API1.9]CS武器1.5.0-全量包.scmod`，526,940,597 字节，SHA-256 `7448e3400e68f6a22d08b057123c24eddc4a61320f78921c84b1cd300015a01f`。完整成员 CRC/哈希回读通过；旧包 `0ca107d6…` 与证据保留在 `output/history-1.5.0/0ca107d6d6d531effe7af4341063fd2f5c89c6c207ed42135a0012e4a314f424/`。
- 新旧包仅核心/战术两个 DLL 变化，其余 1,714 个成员逐字节不变。272 个当前源文件与构建阶段逐一比对通过；核心与战术 DLL 哈希与上一节已验证候选相同。版本仍为 1.5.0，全量版，无安装或玩家世界写入。
- 执行 `./tools/dev.ps1 python tools/release_full_150.py 'build,package,checks'`，15 组检查通过：主包 14,188/14,188、战术 85/85、玩法 97/97、库存 2,262/2,262、UI 149 项零失败；1.4.0 Full/Lite 与新包往返 247/247，1.5.0 首包与新包 249/249，替换前最新 1.5.0 与新包 249/249；原生资源、兼容钩子、三种外观依赖组合、内嵌资源一致性全部通过。均为 SCAPI 1.9.3.1 隔离离线/原生检查，不宣称真实联机或 Android 实测。
- `tools/release_full_150.py` 增加当前旧包的固定哈希、额外往返门禁和正确归档目标；仍保留首包兼容检查及严格目标校验。随后执行 `deliver`，再运行 `verify_flash_delivery.py post` 回读交付文件并对比旧包与源文件。
- 耗时：核心/战术构建 16.72/5.75 秒，主包 78.38 秒、战术 16.73 秒、库存 32.06 秒、玩法 3.33 秒、UI 13.14 秒，其余单项约 0.28–7.86 秒；检查最多两路并行。完整命令、输入哈希、日志和原始报告在 `output/release-1.5.0/`。
- 清理了本轮重复候选包 `.tmp/dev-temp/release-full-150/[API1.9]CS武器1.5.0-全量包.scmod`，释放 526,940,597 字节；删除前验证目标/祖先无链接、无相关运行任务、候选和交付哈希一致，删除后交付哈希不变。是可重建文件的永久删除，不是回收站。其余发布阶段保留为后续验收修正的单一工作集（固定旧包 DLL、提取的内嵌资源、构建项目仍由 release_full_150.py 的 build/package/checks 使用），没有重试此前被拒绝的整个阶段目录删除。回执 `output/release-1.5.0/flash-cleanup.json`。

用户仍需观察连续闪、友军闪、弱闪保留强闪尾段；完整发布检查通过不等于实机视觉验收通过。
