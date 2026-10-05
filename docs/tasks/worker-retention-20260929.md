# Windows worker 队列卡住修复（2026-09-29）

## 结论

截图中的 `Worker storage admission limit` 是 Windows worker 在 `E:/Develop/AgentBridge/jobs` 保留目录达到 200 个后的保护性拒绝，不是构建、VPS 或磁盘空间不足。检查时没有运行中的任务，磁盘可用约 43.4 GiB；旧目录中 153 个成功、45 个失败、1 个取消、1 个超时。

## 已实施

- 新增 `job_storage.py`：维护热目录与归档目录的索引，保留 request_id/fingerprint 去重和原 job ID。
- 达到 180 个热任务时，将已结束且超过 1 小时的旧任务以同盘目录移动到 `E:/Develop/AgentBridge/job-archive/`，保留最近 140 个热任务；不删除日志或脚本。
- 运行中、排队中、最近任务、标记保留、状态损坏或身份冲突的记录不自动移动；元数据有问题时宁可拒绝新任务。
- `job`、`jobs`、`log` 和重复 `request_id` 都能访问归档任务；增加 `storage_status` 与默认 dry-run 的 `maintain_jobs` 工具。
- 将输入校验前置，非法提交不会先创建目录。磁盘低于 2 GiB、并发达到 2 个或无法安全腾出名额时仍拒绝。

## 验证

- `python -m unittest -v test_job_storage`：22/22 通过，覆盖归档字节保留、路径/重解析点、防覆盖、损坏元数据、重复提交、并发和磁盘保护。
- 实机 bridge smoke：从 200 个热任务安全移动 60 个至归档，逐文件哈希一致；归档日志可读；无副作用任务 `worker-retention-ready` 成功；重复 request_id 复用原 job；最终热目录 141（含 smoke），可用名额 59。
- 结果收据：[live-verification.json](../../../Develop/AgentBridge/acceptance/retention-20260929-212351/live-verification.json)。这是本机 bridge 验证，不是游戏构建或玩家世界写入。

## 备份与边界

修改前备份在 `E:/Develop/AgentBridge/acceptance/retention-20260929-212351/`。本次只移动旧 worker 任务目录，没有删除目录、安装 Mod、覆盖 output 或修改玩家世界。归档目录会继续占用磁盘；它解决的是 200 条目 admission 上限，不是无限磁盘增长。后续应在磁盘预算允许时另行审阅归档证据后清理，不应把“自动归档”当成永久删除策略。
