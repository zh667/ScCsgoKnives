# 1.5.3 正式四包（2026-11-08 交付）

Status: delivered（四包已替换 `output/`；PR 合并见文末）
Current writer: VPS Claude；Windows 作业负责构建、测试、交付与 Git 写操作

## 用户要求（2026-11-08）

- 适配插件联机版 1.9.5.1_MP（主要是死亡竞赛）：设计、基线、改动和候选验证见 `docs/tasks/mp-1951-20261108.md`。
- 用户决定：先说“版本号保持 1.5.2，重新出一版 1.5.2”，随后改为**正常升版本叫 1.5.3**；又说“就改了版本名，没必要全量回顾测试吧”——关卡缩减为下面四项，其余复用候选证据。
- 交付后开 PR 合并 main（用户已同意）；不打 tag、不上传、不安装。

## 源码与构建

- 分支 `feat/mp-1951-20261108`，阶段 `r153a` 由提交 `fd1d096`（`build(release): bump the family to 1.5.3`）构建：`tools/deathmatch_140.py r153a prepare,build,clips,appearance,package,dmbuild,dmpackage,dmcheck,compat`，`STAGE EXIT 0`。
- 与 1.5.2 的源码差别只有 `ScNet.cs`（1.9.5.1_MP 上不再误报“平台冻结修正未能启用”）和版本号（三个 modinfo + MetadataCheck 版本门槛）；其余提交都是测试/流水线工具。
- 在 worktree `E:/projects/ScCsgoKnives-mp1951` 里运行，`.tmp`、`output` 临时联接到主目录（清理时已删联接）。

### 被放弃的阶段
- `r152r2`（同源码、版本号仍 1.5.2，用户第一个决定）：阶段关卡全部通过（主套件全量 3/14275、轻量 3/14584 均为已知 smoke-view；AI、视频反馈、C4、探员已知 4 项；family 251、switching-140 249、150/151/152 各 251；netloop/netdmloop 两个联机引擎全过）。交付前检查跑到 1.9.4 实机时，用户改为 1.5.3，作业 `67b97d83…` 被取消；随后确认没有残留游戏/工具进程、测试运行时锁可正常获取释放、鼠标未被锁定。该阶段已在清理中删除。

## 包

| 包 | 字节 | SHA-256 | 替换下来的 1.5.2（`output/history-1.5.2/<sha>/`） |
|---|---:|---|---|
| 全量 | 531308358 | `9c1724c8718e25088856541e7349e3b6fe6629c6e6295d79eaabc52a819cf990` | `f5793b6c…` |
| 轻量 | 35813873 | `43cb5637ca56ff5716cb1403550d0901426499cf4f0eec47266d83ceb7a225eb` | `faed8239…` |
| 探员 | 42805738 | `0a7c97b7ecd767a5b8fb2828a06fb2ed0ce5ac725481377068d74a72ee738200` | `374470fc…` |
| 死亡竞赛 | 371938 | `eb86f51cdcaa4af79c73e9c02c33f4a5e734efcac1db4c20cd6debdd39427f40` | `457d81e1…` |

与 1.5.2 逐成员比较（`archive-verification.json`）：全量 11、轻量 6、探员 5、死亡竞赛 2 个成员变化，全部是 DLL / Net bin / modinfo / Integrations 元数据 / INSTALL.txt；模型、贴图、音频、动画字节不变（与 1.5.1→1.5.2 的变化面相同：程序集重新编译、版本号进了说明文字）。读回：四包哈希一致、zip 完整、modinfo 版本均为 1.5.3。

## 关卡（缩减，用户决定 2026-11-08）

1. 构建与打包自带校验：阶段 `r153a` 全部步骤通过；DeathmatchCheck 254/254。
2. 包元数据：原生元数据 7/7（版本 1.5.3、依赖要求 ≥1.5.3 且不接受 1.5.2/1.5.1/1.4.x）。
3. 存档互切：family 251/251、switching-140 249/249、switching-150 251/251、switching-151 251/251、**switching-152 251/251**（1.5.2 ↔ 1.5.3；版本号在运行时作为 `ScCompatibility.ActiveBuild` 写进存档）。
4. 实机单机：`sp_engine_compat` 1.9.3.1 + 最终全量包 21/21（`engine-compat-r153a-131-full.json`）。
5. 实机联机：`mp_deathmatch` 1.9.5.1_MP，房主轻量+探员、客户端全量 + 死亡竞赛（最终四包）17/17（`dm-mp-r153a-1951-lite-agents-full.json`，判断：`upstream fixed: this CompatNet has no CommandCompatNet adapter …`，提示 none）；其中“CompatNet 判断符合 1.9.5.1_MP 且没有平台提示”房主、客户端各一项。

复用的证据（同一处源码改动、版本号 1.5.2 的候选，未对 1.5.3 重跑）：
- 候选阶段 `mp1951-20261108a`：1.9.3.2_MP 与 1.9.5.1_MP 上 netloop 28、netstate 109、目标路由 12、netgunloop 122、netdmloop 轻量/全量各 60 全过；dmabi 单机 1.9.3.1、1.9.3.2_MP、1.9.5.1_MP 死亡竞赛包 0 问题；dmload、appnet 全过。
- 实机死亡竞赛 17/17：`dm-mp-fix-1932-full`（1.9.3.2_MP）、`dm-mp-fix-1951-full`、`dm-mp-fix-1951-liteagents`（1.9.5.1_MP）；证伪 `dm-mp-neg152-1951-full`（已交付 1.5.2 在 1.9.5.1_MP 上恰好挂这两项，15/17，有提示截图）。
- 被放弃的 `r152r2` 阶段的全套阶段关卡（见上）。
- 未重跑：历次正式版读写、引擎契约、1.9.4/1.9.5/1.9.5.1 单机实机、dm-comfort、sp_uninstall、sp_tree / sp_tree_b（这些覆盖的代码 1.5.2→1.5.3 没有变化）。

## 交付与清理

- 交付作业 `r153-deliver-20261108-a`：先核对联机结果里两项平台检查都在且通过，再把 1.5.2 四包移入 `output/history-1.5.2/<sha>/`，1.5.3 四包放入 `output/`，读回通过。清单 `output/release-1.5.3/manifest.json`（含缩减关卡说明与复用证据），证据 `output/release-1.5.3/release-20261108/`。
- 流水线基线改指 1.5.3（`c6d20b9`）：`BASELINES`、`BASELINE_VERSION = "1.5.3"`，`OFFICIAL_RELEASES` 加入 1.5.3（1.5.2 保留，存档互切矩阵照旧包含它）。
- 清理回执：`output/release-1.5.3/cleanup-20261108.json`（删除阶段 r153a、被放弃的 r152r2、候选阶段 mp1951-20261108a、临时脚本目录、本次实机运行目录与测试包副本、worktree 编译输出；保留 1.9.5.1_MP 引擎构建 `.tmp/mp-1951` 和 `refs/mp-1951` 供以后联机关卡用）。

## 未覆盖

- 未在 Android 手机上测试；没有 1.9.5.1_MP 安卓包；没有跑手机做主机的联机。
- 三人及以上、C4/手雷/装配台/探员的实机多人这次没跑（由离线联机回环覆盖）。
- 缩减关卡外的项目只有复用证据，见上。
- 未创建 release tag，未上传，未安装。
