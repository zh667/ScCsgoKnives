# 可同步的紧凑诊断证据

这些 JSON 从 Windows 已执行的实际 DLL 离线探针原样复制，便于 VPS 不依赖未同步的 `.tmp` 即可查看结论。输入哈希、复现边界与原路径在 [审计报告](../mp-client-and-sushi-audit-20261002.md)，实施入口在 [规划](../mp-state-consistency-plan-20261002.md)。

- `offline-probe.json`：旧 mpb 的模板不扣预测、旧 ID7 回到 5 发、ID12=20、跨 registry 预测污染，以及真实平台槽0可写。复现旧错误成功不等于修复通过。
- `sushi-offline-probe.json`：开启特定堆叠选项时1→4；六个人箱代理正确去重；真实 RemoveChannel(1) 后 Save 的非连续键异常。频道 UI 可达性未知。
- `mpc3-offline-results.json`（2026-10-02，VPS 执行轮）：候选 mpc3 的离线结果紧凑副本——管线各步骤计数、状态用例 83 项与目标断言 9 项的逐项结果、对已交付 mpb 轻量包的基线复现（9 项失败 7 项）、C4 声音与玲兰堆叠检查逐项、以及候选包内核心/适配器在用户实际 Windows 平台 DLL 上的结果。全部是不启动游戏的离线检查，不是实机验收；各项的含义与限制见 [verification](../../../openspec/changes/weapon-state-consistency/verification.md)。
- `sushi-inputs.json`：实际包元数据及解出的程序集哈希，供版本核对；文件名与内部 Version 不完全相同。
- `mpc3-opcode-probe.json`、`subworld-travel-probe.json`、`ancient04116-inputs.json`、`ancient04116-provider-probe.json`、`ancient04116-inventory-envelope.xml`（Windows 诊断轮）：mpc3 上确认与命中共用编号 42 的实际 DLL 复现；远古 0.41.16 实际序列化器只带物品值的复现。都是“旧错误复现成功”，不是修复通过。
- `mpd2-offline-results.json`（2026-10-02，VPS 第二执行轮）：候选 mpd2 的离线结果紧凑副本——管线各步骤计数；状态用例 109 项、目标断言 12 项、两端真实枪械状态机回环 45 项的逐项结果；同一组目标断言与回环在已交付 mpc3 轻量包上的失败项（3/12、31/45，即用户反馈的复现）；匪中立/共同报复、伙伴、携枪迁移（含远古实际 DLL）各项；family 兼容矩阵；候选包内程序集在用户实际 Windows 平台 DLL 上的结果。全部是不启动游戏的离线检查。含义与限制见 [verification](../../../openspec/changes/weapon-state-consistency/verification.md) §10、§6。
- `mpe1-offline-results.json`（2026-10-02，VPS 第三执行轮，只修“停火后回升一发”）：候选 mpe1 的离线结果紧凑副本——管线各步骤计数；两端各自帧时钟的枪械状态机回环 95 项逐项结果（候选 95/95；已交付 mpd2 轻量包 32/95 失败，即用户反馈的复现，含逐发显示/执行/Skipped 轨迹）；单机射击节奏 14 项在候选与 mpd2 上的结果；“只改时间轴”的对照实验（28/95 失败，未进入任何包）；候选包内程序集在用户实际 Windows 平台 DLL 上的结果。全部是不启动游戏的离线检查。含义与限制见 verification §11.1、§6 的 R13–R17。

不含玩家完整世界、媒体或程序集。重新运行所需 Probe 和实际包仍在 Windows，通过 worker 读取即可。不要复制历史 raw 工作树到 VPS 或把这些摘要当成未来联机版玲兰的验收。
