# 1.4.0 全量／轻量／探员包（探员改进）

日期：2026-09-28。用户要求在 `output/` 编译 1.4.0 全量包、轻量包和探员包。玩法改动见[任务结果](tasks/agent-feedback-20260928-vps-results.md)（P0–P4；P5未做）。

## 交付文件（2026-09-29 按常设授权替换为 r2-c4-completion-20260929 c08 候选）

| output/ 文件 | 字节数 | SHA-256 |
|---|---:|---|
| [API1.9]CS武器1.4.0-全量包.scmod | 526,629,508 | 538a74e4b5b874b4d2e4b90b3aa44ff3cd954f34531716803e411107c8a8844d |
| [API1.9]CS武器1.4.0-轻量包.scmod | 35,519,997 | de4dd5e96b524d1e7fae7ce65aaf19a8a20480a399f56713493055a197cb79df |
| [API1.9]CS武器1.4.0-探员包.scmod | 38,451,834 | 03d9ac7d465fc0983dafc1e85cbf34e982e4ce6a6f3a0b0be5eea0986af38454 |

本次内容见[R2/C4补完与爆头/伤害结果](tasks/r2-c4-completion-20260929-vps-results.md)：投掷姿态补完（蹲/跑/跳、原版男女、NekoMeko第三方刚性模型、敌人投掷动作）、C4安装（第一/第三人称、敌人）、自然落点与2.5px轨迹、CT/T蒙皮部位判定、敌对T对玩家伤害表、CS2爆头音效。护甲物品（背心/头盔）未实现。公开版本号仍为1.4.0。只通过离线测试和离线渲染验证，实机录像/听音/手机验收未完成，见[验收交接](tasks/r2-c4-completion-20260929-windows-acceptance.md)。

替换方式：Windows job e7ebb452c58a447abe4c442878e525fd 先核对output原文件与清单一致、候选哈希与c08验证轮（job ab8fd711fec4452d9b6977c7d1356c4e）一致，再同盘移动（剪切）候选覆盖原文件并复核；候选目录已空，只留output一份，VPS不存包。

历次1.4.0（文件本身不再保留，哈希记录在清单 `replaced.previous` 和 `history`）：

| 版本 | 全量 | 轻量 | 探员 | 说明 |
|---|---|---|---|---|
| 首版（2026-09-28） | 34f11870…957e | 53298626…95c1 | d49e4bec…94a | P0–P4 |
| f5-01（2026-09-29） | b60804f8…aca5 | dd365605…ac1b | 9052dc95…537c | F1–F5，job 97c63f669bad438394a2d10673b9bb9b 替换；内容见[后续任务结果](tasks/agent-followup-140-20260928-vps-results.md) |
| r2-06（2026-09-29） | 9219228a…6c66 | 6d9ed6ea…f319 | e92811fa…fa0f | S0、R1–R4，见[视频反馈结果](tasks/video-feedback-20260929-vps-results.md) |
| c08（2026-09-29，当前） | 538a74e4…844d | de4dd5e9…79df | 03d9ac7d…8454 | R2/C4补完、H1/H2/H4 |

清单：`output/release-1.4.0/manifest.json`（revision r2-c4-completion-20260929 + headshot-armor-balance H1/H2/H4）。1.3.0三包与1.0.0/1.2.0兼容包保留在output原位；未安装到Mods，未改世界。

## 三包区别（沿用1.3.0结构）

- 全量：核心＋战术＋语音＋外观适配合一（ScCsgoBundle），原画质资源；单独使用，不另装探员包。
- 轻量：仅核心（`SC_SPLIT;SC_RESOURCE_ZSTD`，无损压缩资源与解码器），可单独使用；兼容清单带`OptionalAgents`，缺探员包时探员数据休眠保留。
- 探员：战术＋语音＋外观适配及探员资源，只能配1.4.0轻量包（依赖声明1.4.0，并有核心协议校验）。

## 构建方式

`tools/release_140.py`（prepare → build → refresh-tools → package → checks → deliver），暂存于 `E:/projects/ScCsgoKnives/.tmp/release-140-20260928`。

- 基线：三个1.3.0交付包（全量 `dc902a45…a97`、轻量 `3399b702…f332`、探员 `06a441b9…34bd`）逐字节核对后作为输入。
- 核心：沿用已验收的capacity-fix构建目录（嵌入的AnimationData/Shaders、拆分类型、引用DLL），覆盖当前共享源码；与现行源码比对嵌入数据无差异。战术和语音沿用feedback-fixes目录（嵌入ArmData），覆盖当前源码；轻量用拆分常量并引用轻量核心，全量引用全量核心。
- DLL：轻量核心 `b6c2f73b…809a`，全量核心 `b1e73792…6978`，探员战术 `94c92fc5…66f7`，全量战术 `5bef8ff7…d517`，语音（两版相同）`47160de9…c24d`。外观适配bin、ScCsgoBundle、资源DLL及解码器未变。
- 打包：仅替换改动DLL、`ScTactical.xdb`（在原包CRLF文本上新增TacticalCompanions子系统并把同伴抗性改为180）、`ScCompatibilityManifest.xml`（各版原文件新增一行，保留轻量的OptionalAgents与语音项）、版本元数据与INSTALL.txt。其余成员（全量1690、轻量1328、探员363个）压缩流逐字节不变。游戏内名称／描述为通用文本。

## 验证（Windows离线，job `e137deff450d4debb5a0ed941d9251f2`）

| 检查 | 结果 |
|---|---|
| PackageCheck 全量／轻量 | 13890/13890、14193/14193 |
| 探员AI回归 全量／轻量+探员 | 32/32、32/32 |
| 原生世界资源门禁 全量／轻量；原生兼容钩子 | 通过 |
| NMM/Neorxna集成 both/reversed/none × 全量、轻量+探员 | 6/6通过 |
| SplitCheck core／agents | 通过 |
| CompatibilityCheck（1.0.0、1.2.0兼容修订核心与1.4.0全量核心） | 243/243 |
| InventoryCheck（含玲兰真实DLL） | 2248/2248 |
| 完整探员回归（非门禁） | 73项失败4，均为1.3.0原包同样失败的既有项：optional-package-identity-and-no-bundled-engine、native-gltf/ct、native-gltf/t、npc-full-registry-reload-refuses-without-ammo-loss |

检查工具修正：`SplitCheck`原断言公开身份版本恒为1.3.0，改为与核心包版本一致（意图不变）。

## 限制

- Android实机、完整游戏操作、跳跃动画和倒地（P5）均未做；离线通过不代表全部反馈已解决。
- 未跑容量矩阵（需玩家世界导出，本批未改编码）；1.4.0与1.0/1.2/1.3在真实世界上的双向切换只经CompatibilityCheck离线覆盖。
- 1.3.0探员包不能配1.4.0轻量包，反之亦然；玩家需同时更新两个文件。旧版远距卸载已丢失主人的同伴无法恢复。
- 升级前请玩家自行备份世界。
