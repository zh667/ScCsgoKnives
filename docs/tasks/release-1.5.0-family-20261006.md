# 1.5.0 四包发布：轻量、全量、探员、死亡竞赛（2026-10-06）

状态：**已交付**（2026-10-06 22:50，四包已移入 `output/`）；分支 `release/1.5.0-family`（从 main `d56c607` 建）待用户批准合并。写入者：VPS Claude。

## 用户原话（2026-10-06）

"轻量，全量，探员，死亡竞赛（这个不要加包这个字），1.5.0替换到output里面"；同时确认各开发分支合并状态。

## 基线与范围

- main `d56c607` 含：1.5.0 全量包的全部改动（生存/小队反馈、闪光修正）、子世界空投、死亡竞赛界面（CS2 图标、准备流程、角点光柱、菜单键）、曳光改动（tri，用户未单独验收）。
- 未合并、也不进入本次发布：`codex/refactor-quality-20261006`（bug 修复加两轮重构；修复部分改了存档内容，兼容矩阵未跑；用户已定现在不重构）。
- 构建基底仍是 1.4.0 的三个包（`BASELINES`），与 Codex 的 1.5.0 全量包做法一致；四个候选的 modinfo 版本统一写成 1.5.0，`INSTALL.txt`、`Integrations/ScCsgoBundle.json` 里的 1.4.0 字样同样改写。
- 文件名：`[API1.9]CS武器1.5.0-{轻量|全量|探员}包.scmod`、`[API1.9]CS武器1.5.0-死亡竞赛.scmod`（无"包"字）。

## 流水线改动

| 改动 | 文件 |
|---|---|
| 候选名按 `src/ScCsgoKnives/modinfo.json` 的版本生成，死亡竞赛无"包"字；基底包用 `baseline(label)`（`BASELINE_VERSION`=1.4.0） | `tools/followup_140.py`、`tools/video_fix_140.py`、`tools/completion_140.py` |
| 打包时改写版本字样 | `followup_140.package` |
| 兼容门禁新增对正式发行版的双向切换：1.4.0 全量/轻量核心（`switching-140`）、Codex 1.5.0 全量核心（`switching-150`，`OFFICIAL_RELEASES`） | `completion_140.compat` |
| 测试脚本的包路径按版本生成（`m0.LITE/FULL/AGENTS/DM`） | `tools/MpM0/m0.py` 及 sp_* 脚本 |
| 版本号：核心 1.4.0→1.5.0；死亡竞赛 1.0.0→1.5.0，依赖核心 1.5.0 | 两个 `modinfo.json` |
| 文案："需要配套1.4.0探员包"→"同版本的探员包" | `ScCompatibility.cs` |

## 验收

- `deathmatch_140.py r150a all`（核心、探员、语音、适配器构建；打包四个候选；netloop/appnet/baseline/gates/motion/throw/hotspots/ui；dmbuild/dmpackage/dmcheck/dmabi/dmloop/dmload）。
- `completion_140.py r150a compat`（family、switching-140、switching-150、native、inventory、integration 矩阵）。
- 实机：单机 1.9.3.1（`sp_deathmatch` 轻量+竞技）、联机 1.9.3.2_MP（轻量+探员 主机 / 全量 客户端 + 竞技）。
- 通过后按常设授权移入 `output/`：1.4.0 三包和竞技包移到 `output/history-1.4.0/`（兼容门禁的输入），Codex 的 1.5.0 全量包移到 `output/history-1.5.0/<sha>/`；写 `output/release-1.5.0/manifest.json`（四包）；随后把 `BASELINES`/`BASELINE_DIR` 指向新交付。

## 过程记录（2026-10-06 晚）

- 第一次全流程（`rel150-pipeline-r150a-01`）：四个候选打出；核心门禁全过（main-full 14188/0、main-lite 14500/0、ai 88/0、dmcheck 225/0、tactical 已知 4 项）。三处发现：
  1. 兼容门禁 `switching-140/150` 各失败 1–2 项 `preserves-all-later-item-type-identities`：旧核心单独加载时解析不到探员程序集里的 `ScTacticalShieldBlock`。是我搭的读取器少放了同包的其他 DLL，不是产品问题；改为把包内全部 DLL 放在读取器旁边后，`switching-140` 247/247、`switching-150` 249/249、family 248/248 全过。
  2. 对已交付 1.4.0 包的"已知失败复现"（baseline-main）崩溃：`FeedbackSeptember18Regression` 读取 1.5.0 才有的 `SniperHipCrosshair` 设置字段。已给测试加空值保护（供后续阶段用）；本阶段不重建，候选自身的 main 门禁 0 失败，不需要已知失败过滤。baseline-ai-full 在 1.4.0 全量包上 22/88 失败，全是空投、生存反馈等 1.4.0 没有的功能，属预期。
  3. 联机实跑（轻量+探员主机）加载世界失败："Not Found Res Models/ScCsgoTactical/airdrop"：空投的模型、贴图和署名文件是新资源，流水线只打包既有清单，探员包和全量包里都没有；Codex 只做过"空投开发测试包"（`tools/package_airdrop_dev.py`），从未进正式流程。新增 `docs/tasks/airdrop-package-members-20261006.json`（4 个成员、哈希与开发测试包一致），`followup_140.package` 按记录放入全量和探员包；兼容门禁加 `native-full` 资源门禁。
- 第二次（`rel150-pipeline-r150a-02`，从 package 起重跑）：空投资源进了全量和探员包，但 main-full 的 `standalone/resource-manifest-complete` 失败 1 项：全量包里新增的贴图/模型成员没有列进核心的资源清单 `Assets/ScCsgoResources.xml`。`completion_140.core_members` 现在把按记录打进全量包的战术资源也写进清单。其余门禁全过。
- 第三次（`rel150-pipeline-r150a-03`：package → dm 步骤 → netloop/appnet/baseline/gates → compat → 单机 → 联机；motion/throw/hotspots/ui 不依赖包字节，已在前两次通过，不重跑）：全部通过。

## 结果与交付（2026-10-06 22:50）

| 包 | 文件 | 字节 | SHA-256 |
|---|---|---|---|
| 全量 | `output/[API1.9]CS武器1.5.0-全量包.scmod` | 531,272,164 | `fb513611e132017c6dc8c94cc42182067c829bc8d968caf0ac3374a91f55b3e4` |
| 轻量 | `output/[API1.9]CS武器1.5.0-轻量包.scmod` | 35,779,241 | `dff871b1b027c4b0e12899958478b215dbeccaaefbe0093a676883c9cf85b007` |
| 探员 | `output/[API1.9]CS武器1.5.0-探员包.scmod` | 42,799,085 | `20fa0a05bfa5d68dfe1375811e4d57d11627dd46b8563ce58d835a60e9d38482` |
| 死亡竞赛 | `output/[API1.9]CS武器1.5.0-死亡竞赛.scmod` | 369,196 | `c2466e3a948293a8e14860d86abe5567ce9ba227d37a132d8dec38f271454ff6` |

玩法身份 `17870390eed18819375ec3fda776610a`（四包同一身份，联机两端都要装齐四个）；竞技包身份 `8c7add467411f19d7da45490ef6435bd`。清单 `output/release-1.5.0/manifest.json`（Codex 的单全量包清单改名为 `manifest-full-only-20261006-1218.json` 保留）。

- 门禁（阶段 `r150a`，第三次）：main-full 14191/0、main-lite 14500/0、ai 88/0、vf 292/0、c4 140/0、netloop 28/28 ×2、netstate 109、netgunloop 122、appnet 19/19 ×2、tactical-full-suite 已知 4 项、DeathmatchCheck 225/0、dmloop 60/60 ×2、dmload 全过、dmabi 包自身引用无缺失。
- 兼容：family 248/248；switching-140（1.4.0 全量/轻量核心）247/247；switching-150（Codex 1.5.0 全量核心）249/249；native-hooks、native-lite、native-full、inventory、六组集成矩阵全过。
- 实跑：单机 1.9.3.1 `dm-sp-r150a` 124 步、24 帧、0 报错；联机 1.9.3.2_MP `dm-mp-r150ala`（轻量+探员主机、全量客户端、竞技包）12/12，两端 0 报错。
- 历史包：1.4.0 四包 → `output/history-1.4.0/`；Codex 1.5.0 全量包 → `output/history-1.5.0/7448e340…/`。流水线基底改为 output 里的 1.5.0 三包；`OFFICIAL_RELEASES` 记录 1.4.0 全量/轻量和 Codex 1.5.0 全量作为切换门禁的读取器。
- 未做：手机；用户对竞技界面、曳光和 1.5.0 玩法改动的实机观感。
