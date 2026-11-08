# 1.5.2 正式四包（2026-11-08 交付）

Status: delivered（四包已替换 `output/`；分支 `feat/world-tree-registry-20261107` 未合并 main，等用户批准）
Current writer: VPS Claude；Windows 作业负责构建、测试、交付与 Git 写操作

## 用户要求（2026-11-07 / 11-08）

- 根治“CS 枪和带子世界的模组（尸鬼、远古世界、传送门）一起用总出 bug”：一个主世界文件夹一本枪械记录本（设计与进度 `docs/tasks/world-tree-gun-registry-20261107.md`）。
- 11-08 用户决定：B 阶段报告里五种情况都按建议处理，合并提示改成看得见时再显示；版本号 **1.5.2**（不是 1.6.0）；1.5.1 作为正式版进存档互切矩阵。
- 交付按 1.5.1 的流程；不打 tag、不上传、不合并 main、不安装。

## 源码与构建

- 分支 `feat/world-tree-registry-20261107`，阶段 `r152c` 由提交 `824dc66` 构建（`tools/deathmatch_140.py r152c`，与 r151c 同一步骤列表加 compat），基底为 output 里的 1.5.1 正式四包。
- 在 worktree `E:/projects/ScCsgoKnives-gun-mirror` 里运行，`.tmp`、`output` 临时联接到主目录；worktree 自己的 `.tmp`（含不可改样本）先改名保存，清理时放回。

### 被取代的阶段
- `r152a`：主套件新增失败 `growth30-boundary/actual-after-save-hook-creates-v3-packet`、`growth30-boundary/failure`，兼容 `2/manual-backup-all-runtime-migration-paths`。原因：树模式下掌握世界的进程不再写/读跨世界携带包（世界树共用记录本代替），这些检查专测旧携带包路径。处理（`8952505`）：加只给测试用的 `ScGunTree.EnabledForTests`，旧路径照原样在树模式关闭时检查，另加树模式检查“存档不写携带包、记录在记录本里”。没有放宽任何断言。
- `r152b`：只剩 `gates` 失败：本阶段的 PackageCheck 去跑 1.5.1 原核心（baseline）时，世界树回归找不到 `Game.ScGunTree` 直接抛异常，整套崩溃，已知的 3 个 smoke-view 失败因此被当成“新失败”。处理同 1.5.1 的 `8881622`：找不到类型记一项具名失败 `gun-tree/present`、`gun-tree-merge/present`。
- `r152c`：`STAGE EXIT 0`。

## 包

| 包 | 字节 | SHA-256 | 替换下来的 1.5.1（`output/history-1.5.1/<sha>/`） |
|---|---:|---|---|
| 全量 | 531307852 | `f5793b6c072a2055a0cd62e6297d3104d4abcb63b8df84a66086d3cf304e682d` | `eaebe7c8…` |
| 轻量 | 35813417 | `faed8239911b435d5cb589e1bc202f2685d8fdaa3da78ccccb11fd2acaa238b8` | `69986049…` |
| 探员 | 42805732 | `374470fc4e54ae0911820d43f099afd151245ddffd2c0367c7799f3d109a54ce` | `8b871d29…` |
| 死亡竞赛 | 371934 | `457d81e121306c9a96161f8b7ab0c92ebb8f0040929b56e2bb9fafbcc256a4c5` | `3d87affc…` |

与 1.5.1 逐成员比较（`archive-verification.json`）：全量 11、轻量 6、探员 5、死亡竞赛 2 个成员变化，全部是 DLL / Net bin / modinfo / Integrations 元数据 / INSTALL.txt；模型、贴图、音频、动画字节不变。读回：四包哈希一致、zip 完整、modinfo 版本均为 1.5.2。

## 关卡（阶段 r152c）

- 主套件：全量 3/14275、轻量 3/14584 失败，均为已知 `smoke-view/*/clear-lines-of-sight-stay-clear`（与 1.5.1 相同）；item-travel 24、shot-cadence 14、quick-throw 73 全过。
- 探员完整套件：已知 4 项（同 1.5.1）。AI 92/92 ×2，视频反馈 292/292 ×2，C4 140/140 ×2，DeathmatchCheck 254/254，netdmloop 60/60 ×2，netloop/netstate/netgunloop/appnet 全过，dmload 6 次全过。
- 兼容：family 251/251、switching-140 249/249、switching-150 251/251、**switching-151 251/251**（1.5.1 作为正式版进矩阵）。
- 交付前补充检查（作业 `ccc58c5c…`，request `sccsgo-release-152-post-r152c-20261108a`，30/30）：
  - 原生元数据 7/7；历次正式版（history-1.4.0、history-1.5.0、根目录 1.5.1，17 个包、15 个核心）读写新存档 30/30；引擎契约 5 个引擎 broken 0；PackageCheck 按 1.9.4/1.9.5/1.9.5.1 重建运行无新失败。
  - 实机 `sp_engine_compat` 1.9.3.1/1.9.4/1.9.5/1.9.5.1 全量与轻量+探员各 21/21；`sp_dm_comfort` 5 次全过；`mp_deathmatch` 通过。
  - `sp_uninstall`：1.9.3.1 B 7/7、C（泰拉）12/12，1.9.5.1 B 7/7。
  - `sp_tree.py`（最终全量包）：尸鬼、远古、传送门在 1.9.3.1 和 1.9.5.1 各通过（传送门 11/11）。
  - `sp_tree_b.py`（1.9.5.1，最终全量包）：玩家对调样本复制件 17/17（含提示时机）、后出现的传送门空间 14/14、1.5.1 来回切换 7/7。
- 实机检查中，带联机防火墙规则的脚本从主目录 `tools/MpM0` 运行（与 worktree 只有 `m0.py` 不同：worktree 多了光标释放；两边用同一个测试自动化包 `fa83964e…`，默认放开光标）。

## 测试环境改动（同分支）

- `8a6c425`：隔离测试游戏不再把系统光标锁在游戏窗口（ScTestAutomation131 在 Mouse.BeforeFrame/AfterFrame 前设为可见，`SC_TEST_FREE_CURSOR=0` 可关；m0 每关一局都释放残留的锁定）。1.9.3.1 实测：世界内 248/248 采样被锁 → 0/249；开着它 `sp_engine_compat` 21/21（左键射击照常）。关闭的游戏残留的锁定实测存在，已释放。

## 交付与清理

- 交付作业 `8c7789b1…`：1.5.1 四包移入 `output/history-1.5.1/<sha>/`，1.5.2 四包放入 `output/`，读回通过。清单 `output/release-1.5.2/manifest.json`，证据 `output/release-1.5.2/release-20261108/`。
- 流水线基线改指 1.5.2（`09e5c06`）：`BASELINES`、`BASELINE_VERSION = "1.5.2"`，`OFFICIAL_RELEASES` 加入 1.5.2。
- 清理作业 `56e6e969…`：永久删除约 17.4 GB（阶段 r152a/b/c、`.tmp/dev-temp/release-152-20261108`、本次实机运行目录、worktree 编译输出与 phase-B 测试运行目录），worktree 联接已删、自己的 `.tmp`（含不可改样本）已放回。该作业在删除完成后写回执时因脚本错误中止；回执由作业 `e6ab77ec…` 按其日志补写：`output/release-1.5.2/cleanup-20261108.json`（交付包与 history-1.5.1 哈希不变、样本仍在）。

## 未覆盖

- 未在 Android 手机上测试；没有跑手机做主机的联机。
- 待结算击杀/补偿随记录搬迁、另一棵树标记的世界导入只有离线回归（实机未造出这两种存档）。
- 传送门空间里活塞移动中的方块、其他模组存在物品栏外的物品不改号（只写日志）。
- 未创建 release tag，未上传，未合并 main。
