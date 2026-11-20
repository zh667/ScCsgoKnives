# 1.6.0 正式四包（2026-11-20 交付）

Status: delivered（四包已替换 `output/`；分支 `feat/feedback-1120-160` 未合并 main，等用户同意）
Current writer: VPS Claude；Windows 作业负责构建、测试、交付与 Git 写操作

## 用户要求（2026-11-20）

三条玩家反馈合成一个 1.6.0（设计、定位与验收对照见 `docs/tasks/feedback-1120-160.md`）：
1. Neorxna 1.4 一起装时看不到通用弹匣配方；
2. 尸鬼全套灵魂盔甲免疫 CS 伤害（选 A：无限耐久护甲保底 30%）；
3. 后坐与随机弹道，可在模组设置关闭（三档；拟真档成长减到一半为止）。
不打 tag、不上传、不安装；合并 main 另等同意。

## 源码与构建

- 分支 `feat/feedback-1120-160`，阶段 `r160d` 由提交 `a319120` 构建：`tools/deathmatch_140.py r160d prepare,build,clips,appearance,package,dmbuild,dmpackage,dmcheck,dmabi,netloop,dmloop,dmload,appnet,baseline,gates,neorxna,motion,throw,hotspots,ui,compat`，`STAGE EXIT 0`，失败步骤为空。新增的 `neorxna` 步骤带 1.9.3.1 的 Mods 目录跑 PackageCheck，要求真实 neorxna.dll 的检查按名字通过。
- 被取代的阶段：
  - `r160a`：dmcheck F01（电击枪：核心空表 vs 死斗零表）、自检 AK 表预期值写错、C18 用了全局丢包计数、护甲夹具衣物缺槽位、设置提示超过 40 字；另外发现默认主套件不带第三方 DLL，Neorxna 真实 DLL 检查被静默跳过。均已修正（`d681573`、`3bcb12a`、`8089888`）。
  - `r160b`：上述修正通过了 dmcheck 256、netstate 110，随后因实机发现加载循环问题而取消。
  - `r160c`：轻量核心编译失败（`ModEntity.Loader` 在 1.9.3.1 是单个加载器，不是列表），`a319120` 修正。

## 包

| 包 | 字节 | SHA-256 | 替换下来的 1.5.3（`output/history-1.5.3/<sha>/`） |
|---|---:|---|---|
| 全量 | 531321319 | `4b21959426a6e97de93f89c302814c4feb3e7179de6abaec7a88bbe635c82fd7` | `9c1724c8…` |
| 轻量 | 35824354 | `f3876cc06c08cc766edba5b1d3619ecd96d86baa4dbc9a7268cb8e8999f083c4` | `43cb5637…` |
| 探员 | 42805665 | `3f441a04566e7d425f5b822376bbd949e4a665ede798ca0d9d3f2baed8c776fd` | `0a7c97b7…` |
| 死亡竞赛 | 371965 | `5230fe31e1da2ccc78a01e657f9dd85a883f6a7b0a846fb705a212bd9203e961` | `eb86f51c…` |

与 1.5.3 逐成员比较：全量 11、轻量 6、探员 5、死亡竞赛 2 个成员内容变化，全部是 DLL / Net bin / modinfo / Integrations 元数据 / INSTALL.txt；模型、贴图、音频、动画字节不变（打包日志列出的 4 个动画成员是重写过的成员，内容与 1.5.3 逐字节相同）。读回：四包哈希一致、zip 完整、modinfo 版本均为 1.6.0。清单 `output/release-1.6.0/manifest.json`，证据 `output/release-1.6.0/release-20261120/`。

## 关卡（阶段 r160d）

- 主套件：全量 3/14294、轻量 3/14602 失败，均为已知 smoke-view（与 1.5.x 相同），无新失败；新增 `feedback-1120/*` 自检、`weapon-help-layout/neorxna/*` 全过。
- neorxna 步骤（真实 neorxna.dll 1.4.3）：全量 12、轻量 11 项全过；Neorxna 自己的配方遍历对通用弹匣在登记前 0 张卡、登记后 1 张，两次登记只留一个提供者。
- AI 93/93 ×2（含 `enemy/unbreakable-clothing-keeps-30-percent…`：不穿 0.1、无限耐久全套 0.03、同护甲值有限耐久 0、生物近战 0）；视频反馈 292/292 ×2；C4 140/140 ×2（含 Sushi 检查）；探员完整套件仅已知 4 项。
- DeathmatchCheck 256/256（新增 F01 核心与死斗 70 张扫射表一致、F02 35 把枪 40 发逐发一致、半档缩放正好一半）。
- 联机回环：两个联机引擎上 netloop、netstate 110（含 C18：客户端被接受后询问，按房主档位而不是自己的；房主保存后 A、B 都收到）、目标路由、netgunloop、netdmloop 全过；dmload、appnet 全过。
- 存档互切：family 251/251、switching-140 249/249、switching-150/151/152/153 各 251/251。
- motion、throw、hotspots、ui 通过。

## 实机（最终候选 = 交付包）

- `api_engine_checks.py r160d`：1.9.4 / 1.9.5 / 1.9.5.1 契约与套件对比，无新失败。
- `sp_engine_compat`：1.9.3.1、1.9.4、1.9.5、1.9.5.1 × 全量、轻量+探员，各 21/21，无游戏错误。
- `mp_deathmatch`：1.9.3.2_MP 全量 17/17；1.9.5.1_MP 全量 17/17；1.9.5.1_MP 房主轻量+探员 / 客户端全量 17/17。
- `sp_feedback1120.py`（真 Neorxna 1.4.3 + 尸鬼）：1.9.3.1 全量、1.9.3.1 轻量+探员（尸鬼 2.0）、1.9.5.1 全量（尸鬼 2.0.1）各 15/15：
  - 1.9.3.1：引擎加载循环被 Neorxna 的异常打断，CS 在主菜单补做加载（日志 Warning），装配台有弹匣配方，Neorxna 卡片显示装配台配方；1.9.5.1：引擎正常完成加载，同样显示。
  - 尸鬼灵魂套装：CS 子弹 0.02，正好是不穿甲 0.0667 的 30%；原版钻石套 4 发全挡；站在开火的 T 前 15 s 掉血 0.4～0.55。
  - 后坐：AK 连射 8 发，标准约 1.1°～1.4°、关闭 0°、拟真约 6.0°，松手后都回到原位。
  - 证伪：同一探针在 r160a 包（无补做）上状态为 `not run`、Neorxna 遍历 0 张卡。
  - 探针自身修过的问题（不是产品问题）：射手先用了会还击的 T（磨坏盔甲），改为牛；原版钻石甲要求 2 级，新档 1 级时引擎下一帧会脱下，改为先把等级设为 5 并等一帧再测。
- 截图（给用户看）：`output/release-1.6.0/release-20261120/pictures/`（Neorxna 配方窗口、三档后坐连射中的画面）。

## 未覆盖

- 三档后坐的手感、Neorxna 卡片的样子：等用户实际看。
- 未在 Android 手机上测试；没有跑手机做主机的联机；三人及以上未跑。
- 混穿（一件无限耐久 + 有限耐久）每发都有 30% 保底：按设计如此，未专门实机测。
- sp_uninstall、sp_tree / sp_tree_b、dm-comfort 未重跑：存档、世界树、死斗操作相关代码未改。
- 未创建 release tag，未上传，未安装，未合并 main。
