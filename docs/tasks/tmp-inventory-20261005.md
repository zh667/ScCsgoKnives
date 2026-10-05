# Windows `.tmp` 旧目录清单（2026-10-05，待用户决定）

用户原话："第二项先列出来"。大小来自 Windows 任务 `tmp-list-01`，用途来自 `tools/` 里的引用和 `docs/`。这里只列 9 月份之前的旧目录，当前工作目录不在其中。

## A. 必须保留（现在的构建要用、测试固定件、备份）

| 目录 | 大小 GiB | 原因 |
|---|---|---|
| release-140-20260928 | 2.03 | 每次构建的基底（followup_140 的 REL） |
| followup-140-20260928 | 3.83 | completion_140 的基底 |
| actor-freeze-20260926 | 1.17 | `before/` 是 followup_140 的 CT/T 原始角色（DENSE） |
| cs2-companions-audit-20260920 | 2.09 | `export/` 是 followup_140 的探员模型来源 |
| resource-codecs-20260926、optimization-deps、skin-bake-deps、vrf-cli | 约 0.9 | 构建和导入脚本的依赖 |
| compatibility | 0.76 | 1.0.0 / 1.2.0 兼容测试固定件 |
| creature-audit-20260917（其中的 neorxna.dll） | 小 | completion_140 检查用的第三方 DLL |
| gloves-20260921、finish33-source-20260910、optimized-resources、skin-uv-cache | 约 1.3 | 外观/皮肤资源构建的输入 |
| player-world-audit-20260927 | 0.10 | 容量检查用的世界样本（含存档文件，按受保护处理） |
| balance-20260924 | 0.08 | MigrationCheck 的固定件 |
| phone-backup-20261001、c4-color-backups | 0.13 | 备份 |
| SurvivalcraftApi | 0.07 | 参考源码 |

## B. 已删除（2026-10-05，用户：“B直接删”；68.49 GiB，记录 `docs/tasks/tmp-group-b-cleanup-20261005.json`）

| 目录 | 大小 GiB | 是什么 |
|---|---|---|
| agentfb-20260928 | 25.84 | 1.3.0 时的 agent-feedback 测试快照和临时候选（文档写明不是交付包） |
| mobile-resources-130、gameplay-audio-130、settings-rollback-130、codec-release-130（2026-09-27） | 18.60 | 1.3.0 各轮发布阶段 |
| split-lite-130、lite-smooth、minimal-*-130（5 个）、split-lossless | 9.26 | 1.3.0 轻量版拆分/压缩阶段 |
| zeus-172、package-names、nmm-official-audit | 4.31 | 1.3.0 以前的打包/审计 |
| crowd-smooth、crowd-opt、crowd-perf、lite-actor | 3.15 | 9-26 的人群性能实验 |
| standalone110/111/114/121-derived、bundle150-*（3 个） | 2.19 | 9 月中旬的旧资源派生树 |
| public-version-130、compat-version、video-fix-140 | 1.30 | 旧版本整理/视频修复阶段 |
| monolithic-old-split、replaced-resource100-formal、superseded-c4-111 | 1.56 | 1.0.0～1.1.1 的旧安装包（1.4.0 之前，不在兼容矩阵里） |
| compression-audit、ContactProbe、tactical-111-installed-gpu、appearance-installed-check、syncbox-audit、registry-audit、thirdperson-world-prop-check | 2.27 | 早期一次性审计/探针 |

## C. 已按文末方案修剪（2026-10-05，用户：“可以”；删 14.63 GiB，保留部分前后一致，记录 `docs/tasks/tmp-group-c-cleanup-20261005.json`）

| 目录 | 大小 GiB | 情况 |
|---|---|---|
| mobile-common-130-20260927 | 5.54 | 现在的构建只用其中一个第三方 DLL（`runtime/AppearanceCheck/sc-nekomekomodel.dll`）。要先把它挪到固定位置并改脚本，验证后才能删其余部分 |
| feedback-fixes-130-20260927 | 5.70 | 只有“从头重建 release-140 基底”时才用（release_140.py）；基底本身保留着。删了就不能从头重建那个基底 |
| capacity-fix-20260927 | 4.24 | completion_140 的兼容检查用到其中的旧版本包；要查清用到哪部分，才能只留那部分 |

## B 删除后的发现与修复

- `codec-release-130-20260927` 里有 ZstdSharp 上游源码，`src/ScCsgoResourceCodec/ScCsgoResourceCodec.csproj` 默认从那里编译。
  查引用时漏了 .csproj，删除后这个工程就编不了了。现在的构建不编译它（打包沿用已交付包里的 DLL），所以没有构建受影响。
- 修复：从 `feedback-fixes-130-20260927/ZstdSharp-source.zip`（同一提交 2cd0c019…，sha256 `7c2f6dc3…`）恢复到
  `.tmp/third-party-refs/`（含 README 和哈希），把 csproj 的默认路径改到这里。Windows 上编译 0 个错误；产出 395,776 字节，
  和已交付的 `ScCsgoResourceCodec.dll` 大小相同，哈希不同（这个工程不是确定性编译）。
- `sc-nekomekomodel.dll` 也复制到了 `.tmp/third-party-refs/`（哈希相同，`1ba873e7…`），`followup_140.py` 和 `completion_140.py`
  已改为从这里读取。
- 删除时跳过了 36 个指向旧位置 `E:\Obsidian Document\...` 的目录链接（目标早已不存在）：先单独移除链接，再删目录。

## C 逐项查看（2026-10-05）

| 目录 | 现在仍要用的部分 | 其余（可删） |
|---|---|---|
| mobile-common-130-20260927（5.54） | 无。唯一用到的 DLL 已复制到 `.tmp/third-party-refs/`，脚本已改指过去 | 全部 |
| feedback-fixes-130-20260927（5.70） | `agents/source`、`full/agents/source`（180 MiB）：release_140.py 从头重建 release-140 基底时的输入。ZstdSharp 压缩包已另存 | 其余约 5.5 GiB（1.3.0 的检查副本、候选包、基线包、运行时副本等） |
| capacity-fix-20260927（4.24） | `1.0.0` 和 `1.2.0` 下的 `src/ScCsgoKnives/bin/Release/net10.0`（259 MiB，completion_140 兼容检查每次都用）；`lite`、`full`（408 MiB，release_140 重建基底用）；`recovered-worlds`（恢复出的世界存档，按受保护处理）；日志和 JSON | 其余约 3.5 GiB（baseline、candidate、old100/old120/old130 旧包、1.0.0/1.2.0 的源码副本） |

## 合计（2026-10-05）

曳光轮次中间产物 24.26 + 旧联机阶段 17.77 + B 68.49 + C 14.63 = 125.15 GiB；E 盘可用从 31.9 GiB 到 156.6 GiB。`output/` 每次删除前后核对不变。
