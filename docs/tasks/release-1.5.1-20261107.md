# 1.5.1 正式四包（2026-11-07 交付）

Status: delivered（四包已替换 `output/`；分支 `fix/gun-record-mirror-20261106` 未合并 main，等用户批准）
Current writer: VPS Claude；Windows 作业负责构建、测试、交付与 Git 写操作

## 用户要求（2026-11-06）

- 玩家日志：没装 CS 时打开并存过档的世界，装回 CS 后报"旧枪仍在，但整个枪械记录表或布局来源缺失"，进不了世界。
- 用户："那就按照这个方案做吧，版本用1.5.1"——每次存档把枪械记录另存到世界文件夹，装回时 Project.xml 里记录丢了才从副本恢复。
- 用户随后同意加上"卸载期间别的模组占了 CS 方块编号时改回"（"可以"）。
- 用户："开始吧"——正式打包、跑发布检查，通过后按既有授权替换 `output/`。

## 源码与构建

- 源码：分支 `fix/gun-record-mirror-20261106`。阶段 `r151c` 由提交 `8881622` 构建（产品源码自 `cdbe98a` 起未变）；之后的提交只改检查工具、流水线常量和文档。
- 构建：`tools/deathmatch_140.py r151c` 全部步骤加 `compat`，Windows 作业 `94e807a6…`（request_id `sccsgo-release-151-stage-r151c-20261106a`）。玩法身份 `c1ff5584ab43762ed1a7c51abfddba6f`。
- 构建基底：output 里 10-21 的 1.5.0 正式四包（同 1.5.0 的做法）。
- 在 git worktree `E:/projects/ScCsgoKnives-gun-mirror` 里运行：`.tmp`、`output` 临时做成指向主目录的目录联接（不复制资源），交付后已删除联接。为此改了 `tools/followup_140.py`（`77b97c2`，见下）。

### 被取代的阶段

- `r151a`（作业 `0b46d8ca…`）：探员包编译失败。`followup_140.prepare` 按 `ROOT/.tmp/release-140-20260928` 拼字符串去改写 csproj 的引用路径；从 worktree 运行时 ROOT 是 worktree，csproj 里写的却是主目录真实路径，没替换上，探员包仍引用 9 月的旧核心。修复 `77b97c2`：替换时把联接解析后的真实路径也换掉（主目录里两者相同，行为不变）。
- `r151b`（作业 `667a8a7a…`）：构建和全部检查跑完，失败项与 1.5.0 相同，唯一新问题是 `baseline-main-*` 进程崩溃——本阶段编出的 PackageCheck 去跑原 1.5.0 核心时，新回归 `GunRecordMirrorRegression` 用抛异常的方式找 `Game.ScGunRecordMirror`（旧核心没有），整个套件 `TypeLoadException` 退出，`gates` 因此失败。修复 `8881622`：找不到类型时记一项具名失败 `gun-mirror/present`，不崩溃（与 `ScTravelArrival` 当时的处理相同）。这不放宽检查：旧核心本来就没有该功能。
- `r151c`：用 `8881622` 从头构建，`STAGE EXIT 0`。

## 包

| 包 | 字节 | SHA-256 | 替换下来的 1.5.0（存于 `output/history-1.5.0/<sha>/`） |
|---|---:|---|---|
| 全量 | 531285732 | `eaebe7c82b750d4e8e8a1212575157f30dab2862d34a70374422ae791a0c6e5a` | `faf1071c…` |
| 轻量 | 35790849 | `69986049b8a9a0d57f49d1bdf16a05ae65e2ad48733fc8ccf70343a6fdd76b89` | `48ef04ff…` |
| 探员 | 42805727 | `8b871d29af26ffed1e478ca3dd19f50eabff24017d8f0f3915a6fa5e83f28a2c` | `b6dd6f7d…` |
| 死亡竞赛 | 371933 | `3d87affc5f3cbeb747689838e502f6580a86f91c84046a01cd487e547250bdc3` | `aea705b0…` |

与 1.5.0 正式包逐字节比较（`archive-verification.json`）：全量 11 个成员、轻量 6、探员 5、死亡竞赛 2，全部是 DLL / Net bin / modinfo / Integrations 元数据 / INSTALL.txt；模型、贴图、音频、动画字节不变。读回：四包哈希一致、zip 完整、modinfo 版本均为 1.5.1。

## 关卡

### 阶段 `r151c`（与 1.5.0 `r150p` 同一套步骤）
- 主套件：全量 3/14225、轻量 3/14534 失败，均为已知的 `smoke-view/*/clear-lines-of-sight-stay-clear`，与 1.5.0 完全相同；新增 22 项 `gun-mirror/*` 全部通过。
- 探员完整套件：已知 4 项（`native-gltf/ct`、`native-gltf/t`、`npc-full-registry-reload-refuses-without-ammo-loss`、`optional-package-identity-and-no-bundled-engine`），与 1.5.0 相同。
- AI 92/92 ×2，视频反馈 292/292 ×2，C4 140/140 ×2，DeathmatchCheck 254/254，netdmloop 60/60 ×2，netloop/netstate/netgunloop/appnet 全过，dmload 带包/不带包/再带包 6 次全过；dmabi 2 个未解析引用与 1.5.0 相同。
- 兼容：family 251/251、switching-140 249/249、switching-150 251/251；原生钩子、集成矩阵、背包检查全过。
- baseline：新工具对原 1.5.0 核心只多 `gun-mirror/present` 一项（预期）。

### 交付前补充检查（作业 `54a7f81b…` 与第二轮 `706ea03d…`、重跑 `6b78c26c…`）
- 原生元数据 7/7（`tools/MetadataCheck` 期望版本改为 1.5.1，`7d424b5`）。
- 历次正式版存档往返：history-1.4.0、history-1.5.0 与根目录 1.5.0，共 15 个全量/轻量包、13 个不同核心，26/26。
- 引擎契约：10 个 DLL × 1.9.3.1 / 1.9.3.2_MP / 1.9.4 / 1.9.5 / 1.9.5.1，broken 0。
- PackageCheck 按 1.9.4、1.9.5、1.9.5.1 重建运行：通过（`api-engines.json`）。
- 实机 `sp_engine_compat`：1.9.3.1、1.9.4、1.9.5、1.9.5.1 上全量和轻量+探员各 21/21。
- `sp_dm_comfort`：1.9.3.1 全量/轻量、1.9.4、1.9.5.1 全量 13/13；1.9.5 全量首次 12/13——"HE 爆炸吓跑动物"一项里测试用的牛在第一次取样前就不见了（每次都是新随机世界），同包同引擎重跑两次 13/13，两次都看到牛进入 RunningAway。按偶发测试环境记录，未改检查。
- `mp_deathmatch`（1.9.3.2_MP，轻量+探员主机、全量客户端）15/15。
- 1.5.1 自身改动 `sp_uninstall`（最终全量包）：1.9.3.1 B 7/7、C（泰拉工作台占编号 300→315）12/12，1.9.5.1 B 7/7。
- 第一轮里实机检查从 worktree 运行，被联机防火墙预检拦下（规则按主目录的程序路径配置）；第二轮改从主目录逐字节相同的 `tools/MpM0` 运行。`sp_uninstall` C 第一轮 11/12：物品放在快捷栏第 7–9 格，第 3 局窗口较窄，引擎把隐藏格里的物品扔成掉落物（`ComponentInventory.VisibleSlotsCount`）；`1f8baa6` 改为不用第 7–9 格后重跑通过。

## 交付与清理

- 交付作业 `c1ed26bf…`：1.5.0 四包移入 `output/history-1.5.0/<sha>/`，1.5.1 四包放入 `output/`，读回通过。清单 `output/release-1.5.1/manifest.json`，证据 `output/release-1.5.1/release-20261106/`（阶段 JSON、logs、post-logs、runtime、runtime-candidate-20261106）。
- 流水线基线改指 1.5.1（`fad09c8`）：`BASELINES`、`BASELINE_VERSION = "1.5.1"`，`OFFICIAL_RELEASES` 加入 1.5.1。
- 清理作业 `6bea47a9…`，回执 `output/release-1.5.1/cleanup-20261107.json`：永久删除 12.4 GB（阶段 r151a/r151b/r151c、`.tmp/dev-temp/release-151-20261106`、本次测试运行目录、worktree 编译输出与旧实机测试目录 `.tmp-gun-mirror-20261106`），删除 worktree 的两个目录联接（只删联接）。保留构建缓存、构建基底、API 引擎、测试框架与结果、全部 output 包与 history。

## 未覆盖

- 未在 Android 手机上测试；没有跑手机做主机的联机。
- "编号被占"实机只验证了空投箱被占（枪本身被占、卸载期间物品被挪动只有离线回归）。
- 放在地形里的武器装配台、空投箱不在恢复范围内（设计如此，已在玩家提示里说明）。
- 未创建 release tag，未上传，未合并 main。
