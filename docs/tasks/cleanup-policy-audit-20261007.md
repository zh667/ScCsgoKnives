# 本项目清理拒绝与遗留产物审计（2026-10-07）

用户问题：是否能绕过清理阻止，以及以前是否有类似原因造成临时产物未清理。
本轮只读审计；没有重试已被拒绝的删除、换工具间接删除或修改审批配置。下列测量均为本轮实际枚举结果，不是旧回执的推算。

## 已确认的三组记录

| 记录 | 本轮仍存在的范围 | 当前字节数 | 处理边界 |
|---|---|---:|---|
| 10 月 5 日代码审查，`.tmp/dev-temp/code-quality-20261005/REVIEW.md` | `artifacts/bin/DeathmatchCheck/release/Assets`、`artifacts/bin/ScCsgoDeathmatch/release/Assets` | 各 387,989,811；共 775,979,622（740.03 MiB），各 1461 文件 | 原记录确认是可重建资源副本；删除曾被拒绝，目前保留 |
| 10 月 6 日全量发布，`survival-squad-feedback-20261006.md` | `.tmp/dev-temp/release-full-150` | 1,106,096,343（1.03 GiB），870 文件 | 原完整阶段删除被拒绝；后来只清理了一份重复 scmod，其余被后续修复复用，不能把所有现存内容都当作垃圾 |
| 10 月 7 日空投显示第一轮，`airdrop-item-presentation-20261007.md` | 下表 7 个原拒绝目标 | 900,694,378（858.97 MiB），46 文件 | 3 个旧中间包仍在；split bin/obj 本轮已用于第二轮构建，不再全是旧输入，暂保留工作集 |

这是对 `docs/` 和本项目 `.tmp/dev-temp` 中已有审查/任务记录的审计，不是对全部 Codex 历史会话或其他项目的完整枚举。旧资料中的“未清理”不一定是审批拒绝，不能混为同一原因。

所有路径的绝对根是 `E:/projects/ScCsgoKnives/`。当前拒绝目标明细（只读枚举；目标自身无重解析属性，不代表已完成删除所需的全部祖先/子树和活动任务检查）：

| 相对路径 | 字节数 |
|---|---:|
| `.tmp/dev-temp/airdrop-item-presentation-20261007/full/airdrop-dev-full.scmod` | 531,272,212 |
| `.tmp/dev-temp/airdrop-item-presentation-20261007/split/[API1.9]CS武器1.5.0-轻量包.scmod` | 35,778,230 |
| `.tmp/dev-temp/airdrop-item-presentation-20261007/split/[API1.9]CS武器1.5.0-探员包.scmod` | 42,798,831 |
| `.tmp/dev-temp/airdrop-item-presentation-20261007/split/ScCsgoKnives/bin` | 137,876,512 |
| `.tmp/dev-temp/airdrop-item-presentation-20261007/split/ScCsgoKnives/obj` | 3,731,297 |
| `.tmp/dev-temp/airdrop-item-presentation-20261007/split/ScCsgoTactical/bin` | 143,401,656 |
| `.tmp/dev-temp/airdrop-item-presentation-20261007/split/ScCsgoTactical/obj` | 5,835,640 |

## 历史清理不是全失败

- `docs/tasks/release-1.5.0-family-cleanup-20261006.json` 记录已实际清理 6,270,586,346 字节（约 5.84 GiB）的指定中间目录和原始捕获帧。正式包、历史兼容基准、结果和联系表保留。
- `output/release-1.5.0/flash-cleanup.json` 记录已删除重复全量包 526,940,597 字节；发布回读哈希不变。没有重试此前整个 `release-full-150` 的拒绝动作。
- `tools/release_full_150.py` 仍固定使用 `release-full-150`，旧 DLL、嵌入资源和构建项目仍是后续 build/package/checks 输入。若将来清理，先迁移并验证必要输入，再提出不同、风险更低的精准方案。

## 审批边界

工具上次只返回 `blocked by policy`，没有具体审核理由；不能断言是目录联接、文件权限或磁盘错误。当前会话虽提供完整文件系统访问，也不证明这项拒绝已解除。

[官方 OpenAI 文档](https://learn.chatgpt.com/docs/sandboxing/auto-review) 明确区分拒绝和普通沙箱错误：不能为同一结果换工具、间接执行或规避策略；只能采用实质更安全的替代方案，或请求明确审批。其当前开源 TUI 提供 `/approve` 对某次精确拒绝动作批准一次重试；不是通用放行，重试仍经审核。本桌面任务没有暴露该批准入口，不能承诺它在桌面里同样可用。本轮未擅自修改 `config.toml`、权限模式或组织策略。

当前第二轮测试包和用户正在测试的第一轮包位于 `output/` 子目录，不在上述被拒绝目标内。原资源、用户文件、正式发布及兼容基准均保留。
