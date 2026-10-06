# 源码同步与 Windows 资源执行

用户于 2026-09-28 明确要求 ScCsgoKnives 改回 Syncthing。本文件与 AGENTS.md
最新交接段落取代 2026-09-08 的 Git-only 接力规则，不改变游戏功能或存档要求。

## 工作位置

- Windows 完整项目：`E:/projects/ScCsgoKnives`（2026-09-28 从 Obsidian 笔记库迁出，同步配置已迁移）
- VPS 当前源码副本：`/home/dev/source-sync/ScCsgoKnives`
- VPS 旧副本：`/home/dev/workspaces/ScCsgoKnives`，保留用于追溯，不再参与同步。
- 新文件夹 ID：`source-sccsgoknives-collab-20260928`，避免重放旧同步的删除记录。
- 两端 `.stignore` 引用项目根目录的 `.stignore.shared`；共同规则本身参与同步。

## 范围

同步源代码、脚本、工程/依赖配置、文本资源描述、Markdown、清单及小型测试证据。
不传输 `.git`、缓存、临时目录、`output`、二进制资源、截图视频、原始解包数据，
以及 `src/ScCsgoKnives/AnimationData` 内约 136 MB 的批量动画数据。
代码范围采用显式文本类型列表；新扩展名须先审查并更新共同规则。不得把缺少
资源当作数据被删除，也不得自动下载整个资源树到 VPS。

完整构建、动画/资源处理和手机验证在 Windows 执行。VPS 通过现有 windows-worker
提交任务并读取受控结果。可分享的输出摘要、哈希、清单放在 `docs` 或 `.ai-collab`；
完整输出/报告仍留在 Windows。保留源码内 XML/JSON 等小型描述文件。

## 交接

1. 停止上一位写入者，确认两端 Syncthing 无错误、无冲突、无待同步文件。
2. 保存任务说明、审查意见和 Windows 构建任务编号，再交给下一位写入者。
3. 不允许两端同时修改同一文件；OWNER/文档不是强制锁。
4. 两端都可使用 Git，遵守 `AGENTS.md` 的任务分支、Conventional Commits 和用户批准后合并 `main` 的流程；但文件是同一份，一端的切换、合并、拉取、重置会以“未提交改动”的样子出现在另一端，所以会改写共享文件的 Git 操作只在 Windows 执行，VPS 的 Git 只读或通过 Windows 任务执行。各端独立维护 `.git`，不通过 Syncthing 同步。切分支前先提交干净、确认无其他写入者、两端同步空闲、无游戏或测试任务；文本文件两端统一 LF（`.gitattributes`）。
5. 构建固定输入后再执行，不能把“同步完成”视为“测试通过”。

## 初始化与恢复

初始化先 Windows sendonly / VPS receiveonly，完整清单与 SHA-256 一致后才切换双向。
Windows 原有未提交代码必须逐项保留，不提交、不 stash、不 reset。
规则和配置备份：`E:/Develop/Syncthing/Backups/20260928-source-only`；VPS 对应
`/home/dev/.config/syncthing/backup-20260928-source-only`。旧 VPS 数据未清理，
因此旧资源仍占用原来的磁盘空间。不得直接恢复旧配置并启动全量同步。

双向同步日常会传播共享范围内的后续修改/删除，不能代替 Git 或备份。
此次迁移不执行文件删除；忽略规则不含 `(?d)`，不是磁盘配额。
真实验收结果以知识库的“Syncthing 源码同步恢复记录（2026-09-28）”为准。
