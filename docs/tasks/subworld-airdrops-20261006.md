# 跨世界开枪与探员空投（2026-10-06）

Status: PUBG crate + tall red smoke development candidate delivered; user requested source merge to main on 2026-10-06; official release gates remain pending
Source branch: `feat/subworld-airdrops` (base `fbff6da`); Windows Codex prepares the requested commit/push/merge.

## 用户要求

- 最新外观决定（2026-10-06）：用户明确同意“直接我们的烟换成红色就可以”，取代之前要求取得 PUBG 原烟资源的约束。使用已导入的 PUBG Mobile 箱体＋现有 CS2 烟雾贴图染红，保留高、粗、远处可见的要求；约 80 格高、顶部直径约 13 格另加漂移、896–1024 格淡出是实现候选参数，不是用户指定数值。箱体与烟雾分别记录真实来源。

- 新开发分支，Conventional Commits。
- 检查并修复 AncientWorld 0.41.16 中主世界携枪、子世界开枪、返回后不可用；反向往返同样保持弹药、耐久、身份。输入 `D:/下载/AncientWorld_v0.41.16.zip`。
- 探员包增加空投，使用实际 CS2 资源，空投冒烟，附近有敌对小队守卫。
- 用户补充：枪械、弹匣、CS 武器材料、原版材料等，数量不宜过多；固定五人小队守护。初版默认每箱一把随机枪、两个弹匣、两小堆 CS 材料、两小堆原版材料（总计 9–11 件）。第五名守卫保留手枪/手雷，不携带会炸毁补给的 C4。
- 最新用户要求覆盖所有生存模式敌对小队：空闲走动巡逻，感知范围内主动攻击，范围外受击也反击。替代旧“中立直到被攻击”规则；创造模式保持受击反击。初版感知 32 格、巡逻锚点附近 6 格，原有远距反击与遮挡规则保留。
- 不安装用户 Mods、不写原世界、不修改第三方包。保留其他未跟踪文件。

## 当前证据与验收

- 原聊天最后一轮只读取了项目规则，未改代码。旧跨世界实现和 2026-10-03 实测存在；本次新报告重新开放当前版本验收，不能引用旧通过替代。
- 原聊天 idle；VPS tmux 停在输入提示，无当前编辑。Windows 无 syncthing 进程；新分支从当前 HEAD 创建，不切换任何源码内容。
- 跨世界：当前 1.5.0 + 指定实包，隔离 API 1.9.3.1；复测首次开枪、已使用枪双向往返、存档重进、同编号冲突。记录实际结果后定位最小修复。
- 空投：核对本地 CS2 VPK 资源，使用现有探员生成/库存/网络生命周期；实际模型与烟雾观感待用户验收。生成、领取、守卫敌对性、存档与联机权威状态由代码与针对性检查验证。

## 实现与默认玩法

- 敌队（自然/召唤/空投）在生存模式下主动索敌并巡逻；空投队固定五人。保留创造模式受击反击、三秒生成保护、墙体/烟雾遮挡、受伤避险和范围外反击。同伴继续帮助主人对抗已经瞄准己方的小队。
- 生存且自然敌队开关开启时，约 10–15 分钟尝试一次空投（首次 10 分钟）；地点在活玩家附近 32–56 格的已加载露天地面。模式附属包运行时不自然投放。受原版普通生物数量、敌队人数上限和完整五人站位约束；失败重试，不提高原版上限。
- 五人守卫先完整提交，再开始 12 秒下降；箱子落地后原生 Chest 实体唯一持有物资。下落、烟柱剩余时间单独保存，箱子破坏由原生机制掉落物资。取空或重进不重新发奖。最多同时记录三个下降/冒烟事件；烟柱持续三分钟。
- 每箱一把随机满弹新枪、两个弹匣、1–3 个金属坯件、一份其他 CS 材料、两个铁锭、两个铜锭或煤。共六格、9–11 件；按空投身份固定结果，重试不会重抽。枪池 AK/M4A1-S/MP5/AWP/Glock/XM1014/FAMAS/SSG08。
- 当前箱体使用用户下载的 Rex PUBG Mobile 模型（3848 三角形、1024² 原色图），署名随包附带；来源与转换见 `pubg-airdrop-assets-20261006.json` 和 `tools/import_pubg_airdrop.py`。烟雾保留本地 CS2 `smoke_vertical_large` 所用 64 帧原图，由顶点颜色染红（255,24,12），没有重新压低贴图质量；约 80 格高、顶部烟片直径约 13 格＋漂移，896–1024 格淡出。早期 CS2 箱体导出记录 `airdrop-assets-20261006.json` 仅作历史，已不代表当前箱体。
- 新方块身份跟随其他可选探员物品放在分体核心，行为、模型、烟雾、生成逻辑归探员包；新增保存组注册到既有兼容胶囊。

## 红烟最终候选（2026-10-06）

- 已更新 `output/[API1.9]CS武器-空投开发测试-全量包.scmod`，SHA-256 **`055593c726f36add6cf49598d34bf7c4f458a048bf7bcf1319dc6ca61fe8372c`**，531271626 字节。只替换已核对的原开发包（旧 SHA `58c56f…`），正式包、用户 Mods 和原世界均未改。封装逐成员读回及最终输出 SHA 验证通过。仍为开发候选，未宣称通过正式全版本发布矩阵。
- 最后构建：`./tools/dev.ps1 dotnet build src/ScCsgoTactical/ScCsgoTactical.csproj -c Release '-p:SkipScmodPackaging=true' '-v:q'`，**0 警告/0 错误，4.74 秒**。本次仅渲染/资源改动，没有重跑无关存档和战斗测试；下表历史测试保留原输入身份。
- 红色初拍发现水面盖掉低处烟柱。实际代码确认透明地形在 100 阶段绘制，原空投烟在 10；已将烟柱改为与现有烟雾一样的 `SubsystemScGrenades.EffectsDrawOrder`（310），下落箱体保留 10，DepthRead 遮挡保留。首次红色结果/截图仍记录在 stage 的 `red-photos` 和 `red-photos-sheet.jpg`，未拿失败图充当通过。
- 最终包隔离 **API 1.9.3.1** 实拍 8 个视图、零游戏错误。箱体＋五名守卫、整根红烟、水面背景无断层、水平 **128/384/768 格**高空无遮挡远距图均已拍摄；主代理查看最终整根烟柱、近景及 768 格图片。高度、粗细和红色主观观感由用户验收，Android/全地形地面视角未覆盖。原生箱内物资仍存在，但本轮不宣称重跑完整生命周期或联机测试。
- 实拍交付：`output/pubg-airdrop-preview-20261006/red-full-plume.png`、`red-crate-and-guards.png`、`red-range-768.png`。最终输入/引擎/状态报告和成员清单在 `docs/tasks/subworld-airdrops-evidence-20261006/pubg-red-{final-photos,candidate}.json`。完整日志与两轮红烟联系表在 `.tmp/dev-temp/pubg-airdrop-20261006/`；重复 raw PNG 和 stage scmod 在验证输出后清理，见 `pubg-airdrop-red-cleanup-20261006.json`。

## 原开发候选已执行检查（外观更换前，历史）

原候选 SHA-256 `58c56f3d04d41ae477a6f272dd908d72edeeb5a97842b7588e1bbb551e1b65b4`，已被上节红烟开发候选替换。以下检查针对原候选执行，不能冒称以新包重跑。主要通过/失败 JSON 与原始打包记录归档在 `docs/tasks/subworld-airdrops-evidence-20261006/`。

| 范围 | 实际结果与证据 |
|---|---|
| AncientWorld 当前包检查 | 原包 ZIP SHA-256 `e88b68f6edaf53b419b89db24ed8dd487734bf0aff0830af30824145f0977a66`；内部 scmod `ccc9b649623025dcf41bc2c4675e4b0151fe96a5af008cc8c7319508d74d6c62`。当前 1.5.0 全量基线 SHA-256 `7448e3400e68f6a22d08b057123c24eddc4a61320f78921c84b1cd300015a01f`。隔离 **1.9.3.1** 创造库存往返 11/11；实际生存 `ComponentInventory` 两次往返＋两边开枪＋保存重进 15/15，均零游戏错误。结果在 `.tmp/mp-m0-20260929/results/subworld-ancient-{baseline150,actual-survival150}.json`。未复现新反馈，不推断原反馈已解决，也未猜改枪械迁移代码。 |
| 敌队与空投离线回归 | 最终候选 `PackageCheck --tactical-ai-only` **87/87**。包含巡逻目的地、范围内主动攻击、80/160/256 格反击、创造模式、失去视线、原有同伴/拆弹、空投飞行保存格式、枪池与少量奖励不重抽。报告 `tactical-regression.json`、完整命令输出 `tactical-regression.log` 位于本任务 stage。 |
| 全量构建 | 当前源 `ScCsgoTactical.csproj` Release 成功，零警告零错误；最后 DLL 构建 1.88 秒。 |
| 分体构建 | 当前源 `SC_SPLIT + SC_RESOURCE_ZSTD` 核心＋探员编译成功；新方块 core 所有权和类型转发通过，零错误，7 个已有警告（第三方 Zstd/原刀具条件代码）。脚本 `tools/check_airdrop_split.py`；stage `split/build.log`，7.52 秒。尚未打包分体候选。 |
| 单机空投 | 最终候选 API **1.9.3.1** 的 `sp_airdrops.py ad5` **16/16**、零游戏错误，覆盖下落保存（五名守卫一起保留）、五人不重刷、实走巡逻、主动索敌、枪械取出开枪与持久化、箱内物资不补满、空箱重进不重刷、移除烟柱。 |
| 联机空投 | 最终候选 API **1.9.3.2_MP** 房主＋客户端 `mp_airdrops.py ad4` **14/14**：实际 24/26 生物时拒绝投放且无半成品；隔离竞技场清除测试野生动物后保持原上限、完整五人、下落、落地、原生箱子物资同步、取物、不重复、移除后烟雾同步；双方零游戏错误。结果 `.tmp/mp-m0-20260929/results/airdrops-mp-ad4.json` 保存两个引擎/包哈希。 |

## 失败记录、边界与待验收

### PUBG 箱体接续结果（红烟定案前，历史）

- 用户提供 `D:/下载/pubg-mobile-air-drop.zip`，SHA-256 `4fbf20318feefa89fab608b552759c2fec1ce9f17a8eb922da558c2c6977f271`。仅含 `source/DROP.fbx`、`textures/DROP_D.tga.png`、`textures/DROP_N.tga.png`，没有烟雾资源。原包和原始解包保留在 `E:/projects/CSMCReverse/references/pubg-airdrop-rex-ed80ad57/`；原用户 ZIP 未修改。
- `tools/import_pubg_airdrop.py` 通过任务内安装的 `ufbx==0.0.5` 转换，保留全部 3848 三角形、原 1024² 色图字节（SHA 相同），处理节点世界矩阵、法线、UV 原点；最大尺寸仍 .98 格，实际高约 .788 格。原法线图保留，现有静态网格材质未新增法线贴图着色。资产记录 `pubg-airdrop-assets-20261006.json`；CC BY 4.0 作者说明随包置于 `Assets/PUBG-airdrop-attribution.txt`。
- 本轮内部候选 `.tmp/dev-temp/pubg-airdrop-20261006/airdrop-dev-full.scmod`，SHA-256 `787ed6ab0654a329d3db4353e91831e1d6a6db7b353473ee3e1606bb33d8c5c3`；仍是原 CS2 灰烟图，不能称完成 PUBG 红烟。`output` 原开发包未替换。
- 同候选 API **1.9.3.1** 两轮隔离新世界实拍（定点调用生产 TryStart、创造飞行观察、自然地形）；箱体下落/落地、蓝布红箱绑带和五名守卫已见图，箱内原生物资存在，均零游戏错误。第一轮远距镜头用了箱体地面高度，进入异地地形/液体遮挡，远距证据无效并保留。第二轮仅修正测试镜头到 Y=250，水平距离 **128 / 384 / 768 格**都实际看到烟柱（距地面锚点直线约 **224 / 426 / 790 格**），没有把高空无遮挡测试说成地面各地形全覆盖。代码保留 DepthRead，未新增穿墙绘制。现场结果/引擎哈希/日志在 stage 的 `photos` 和 `clear-range-photos`，所用脚本 `.tmp/dev-temp/airdrop-photos-20261006/capture.py`。
- 分体条件 `SC_SPLIT + SC_RESOURCE_ZSTD` 构建通过：`./tools/dev.ps1 python tools/check_airdrop_split.py`，10.20 秒，0 错误/7 个已有警告。资源封装读回逐成员相等通过；未重跑无关完整发布矩阵。红烟素材、红色观感、Android 与正式发布闸门仍未完成。

### 素材取得前的检索与准备记录（历史）

- PUBG 素材检索：Rex 的 `https://sketchfab.com/3d-models/pubg-mobile-air-drop-ed80ad57edd94dce94fcde85c5a277fe` 页面标为 CC BY 4.0、3.8k 三角形，但下载要求登录；MrPifo 的 `https://www.cgtrader.com/free-3d-models/military/other/pubg-airdrop-crate` 是作者用 Blender 制作的免费模型（非已证实官方提取），同样要求登录。未下载、未改变账号状态。红烟检索目前只有普通红烟图片/绿幕视频，未取得可确认的 PUBG 粒子/纹理源。`E:/SteamLibrary/steamapps/common/PUBG` 仅有五个卸载残留文件，缺少 `TslGame/Content/Paks`，不能作为已安装可导出资源。
- 烟柱独立准备：`SubsystemScAirdrops.cs` 将落地烟柱与下降箱体剔除距离分离，前者 1024 格（896 格开始淡出）、后者仍 160 格；候选高度 80 格、144 张烟片、顶部烟片直径约 12.7 格且有横向漂移，保留深度遮挡。当前仍加载原 CS2 灰烟图，未染红或声称完成 PUBG 替换。`./tools/dev.ps1 dotnet build src/ScCsgoTactical/ScCsgoTactical.csproj -c Release '-p:SkipScmodPackaging=true' '-v:q'` 已执行，0 警告/0 错误，5.84 秒；未运行新版游戏远距验收、未打包/替换 output。下一步需要用户提供 PUBG 箱体与红烟纹理/粒子资源或可直接下载的来源，之后完成导入、红色参数及实际远距验收。

- `subworld-ancient-survival150` 先切 WorldSettings 未重载玩家，仍是 CreativeInventory，不能当作生存证据。随后从新世界创建时选择 Survival，记录实际库存类型，`actual-survival150` 才是有效生存结果。
- `airdrops-ad3` 的存档重进未恢复测试人物的高抗性，射击时枪表已有 MP5 28 发但随身格变空；修正测试生命前提后，`ad4` 的实际使用与保存检查通过。失败报告保留，未改产品以掩盖失败。
- 联机旧 `ad1` 等待落地超时，没有足够诊断字段；同包 `ad2` 通过，不能据重跑绿色声称修复。追加人口证据后 `ad3` 确认 24/26 容纳不下五名守卫；产品改为先提交完整守卫才投放，最终 `ad4` 明确验证满员拒绝和人口充足时的完整流程。第一轮具体阻塞状态未留存，不能断言每次超时只有同一原因。
- 当前没有原反馈故障时的包身份、日志、其他库存模组或失败世界副本；跨世界结论仅限上述当前包、真实提供方旅行函数和两种库存场景，不能证明 Android/原用户组合已修复。耐久精确变化未单独新增断言。
- 箱体/烟柱实际外观与玩法强度待用户体验；没有把资源导出/无报错当作视觉验收。Android 未测。
- 2026-10-06 用户要求真实游戏图片，已用同哈希开发候选在隔离 API 1.9.3.1 新世界实拍。为拍摄切换创造观察、固定白天、调用生产 TryStart 定点投放；自然地形未整平，未改产品实现。两个场景均记录五名守卫、落地箱内物资，零游戏错误。交付照片：`output/airdrop-preview-20261006/`（下落、烟柱与五人、箱体近景）；拍摄状态、引擎/包身份和日志：`.tmp/dev-temp/airdrop-photos-20261006/{result.json,detail/result.json}`。远景最初看似灰色方块，近景可见棕灰色网状包覆纹理和边框；照片确认当前外形较简单，尚不能仅凭照片判定是资源外形还是导入/渲染问题。外观是否符合用户预期仍待验收。
- 正式全版本双向切换矩阵、分体包实际加载/卸载恢复以及最终全量发布闸门未执行；这是开发候选，不冒充正式发布。正式发布前必须验证新增方块在每个既往官方版本中的保留/返回语义。
- 中间候选原位替换，输出后清理重复 stage 包及第三方测试解包副本；日志/JSON 与当前可复现 CS2 资源输入保留。原 CS2 VPK、第三方 ZIP、用户 Mods/原世界未修改。见清理回执 `subworld-airdrops-cleanup-20261006.json`。
