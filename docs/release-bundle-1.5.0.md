# CS武器 1.5.0 / 战术同伴 1.4.0 全量与轻量总包

**后续交付已按用户要求改为[1.5.1单文件scmod总包](release-single-1.5.1.md)，轻量88.43 MB。以下保留为上一轮ZIP分发的历史记录。**

2026-09-24。用户现已授权打包；仅生成交付文件，未安装、删除旧包或改写玩家世界。

## 交付与体积

两个 ZIP 均包含主包、战术拓展、安装升级说明及 SHA256SUMS。ZIP 先解压，再导入其中两个 `.scmod`，不是直接导入 ZIP。保留 `zh667.ScCsgoKnives` 和 `zh667.ScCsgoTactical` 两个模组身份、原实体类型及保存键；不强行合并程序集或删除战术包身份。

| 总包 | 主包 | 战术拓展 | 含说明总计 |
|---|---:|---:|---:|
| 全量 | 382,808,551 B | 108,317,391 B | **491,129,067 B / 491.129 MB / 468.377 MiB** |
| 512轻量 | 70,818,796 B | 23,441,148 B | **94,263,117 B / 94.263 MB / 89.896 MiB** |

轻量总包低于严格的 **100,000,000 B**，余量5,736,883 B，较全量减少约80.8%。没有为凑体积删除枪械、皮肤、人物、手套、声音或动画。安装文件体积不代表运行内存。

文件位于项目根目录 `output/`：

- `[API1.9]CS武器1.5.0-含战术同伴1.4.0-全量总包.zip`
- `[API1.9]CS武器1.5.0-含战术同伴1.4.0-512轻量总包.zip`

全量 SHA-256：`3e65ce7c45fe2d020cf6a05905a08414612b93a917d2e64da62faac2b3f37db0`。

轻量 SHA-256：`7f7530d5eb9e38688a6cfd94988eace8a87bce2bd6fce82dae52845421ffc715`。

## 包含的改动和资源策略

纳入此前 `079b42f` 的自制物资、无线电模型和 `8dc64d5` 的平衡、材料、±10预览、Sushi兼容及旧版本迁移保护。具体数值见[平衡与迁移实施记录](gun-balance-implementation-2026-09-24.md)。不加入生物着火，不实施未授权的第二轮成长曲线。

用户指定的已安装战术1.3.2 SHA-256为 `25db07ab4d68be479ef53e2911e3680232f865050bb229cc146370f24183162c`，与之前交付版本相同。全量战术保留其全部 `Assets/` 文件原字节，更新的是本次源码构建DLL和配套版本元数据；依赖主包最低1.5.0，防止新无线电网格接口搭配旧主包。内置人物外观适配保留为按需加载的 `.bin`，不提前加载可选前置。

轻量策略：

- 主包：复用既有512上限、WebP Q85贴图与受约束枪械减面流程；294份模型三角形由1,852,333减到980,038。保留UV、骨骼分区边界等约束。特殊数字、RGBM及枪口特效图集无损；C4材质alpha在缩放前按既有不透明规则处理。
- 战术：56张独立PNG转512上限WebP；人物GLB内部大贴图缩到512并保留PNG编码，兼容原生glTF加载器。法线缩放后重新归一化；无修改原始资源。
- 战术GLB全部167,332个非图像bufferView逐字节相同；accessor、动画、蒙皮、节点、网格、场景结构保持相同。骨骼、网格和动画不减面、不抽帧；独立贴图透明通道按缩放结果无损保存。
- 两版 `ScCsgoKnives.dll`、`ScCsgoTactical.dll`、`ScCsgoAppearance.bin` 字节相同；只有纯资源程序集 `ScCsgoResources.dll` 因派生枪械网格而不同。低分辨率会降低近距离纹理细节。

## 检查证据与边界

详见[精简证据](release-bundle-1.5.0-evidence.json)，原始报告在 `output/release-bundle-1.5.0/`。

- 主包实际scmod检查：全量12,224项、轻量12,881项，全部通过；轻量另含派生资源相关检查，项目数不同。
- 战术实际scmod检查：两版各56项，全部通过，涵盖同伴保存、装备、敌队、拆弹等。
- 可选外观前置：两版各9个独立进程，未装、单独装、两者齐备、注册顺序相反、禁用、过旧、旧独立外观并存、重载禁用，共18组通过。
- `BalanceCheck/Run.ps1` 28,627项通过，包含用户实际1.0/1.2历史DLL及冻结XML迁移、报价和事务验证；测试目录主DLL SHA与交付包内完全一致：`89d2964dc136ad46ddf27cfd057dcfc6ad1dca69234b07f9cedeb44db87203b4`。未使用玩家真实世界做迁移。
- Lite CT/T/旧人质模型：从交付scmod提取GLB，原生OpenGL ES加载绘制站立及三阶段倒地，共12帧；骨骼边界和可见像素记录在`lite-render/gpu.json`。这项只检查人物，不宣称道具渲染或游戏内验收。
- ZIP CRC、内部scmod字节、资源完整性、DLL一致性、原版战术Assets保留、GLB非图像数据逐字节比对通过。

发布检查修正了旧测试中写死的10秒充能、旧涂装/组件价格、未补偿霰弹预览和共用HE/小鸡爆炸断言；增加WebP读取支持。没有降低断言来掩盖行为故障；新预期取自已批准数值方案，完整回归重新通过。

这些是包内DLL回归与原生诊断，不等同实际游戏、Android、联网或全部Mod组合验收。长期经济实测、战斗体验及真人存档副本测试仍按原计划保留待验收。

## 安装和迁移

1. 退出世界，备份整个世界目录并保留旧包。
2. 全量与轻量只选一套。停用或移出旧CS主包、旧战术拓展，以及早期独立CS资源/CS人物外观包；导入ZIP内两个scmod，不让同一模组两版本同时启用。
3. 含同伴的世界需同时保留战术拓展。保留物品布局v5、schema6/rules7和原IDs；旧1.0转换仍受完整备份校验保护。
4. 玩家CT/T外观需要自行安装NekoMeko Model1.1与Neorxna1.4；总包不附带第三方Mod。同伴和第一人称手套不依赖这两项。
5. 需要退回旧版时恢复升级前世界备份，不直接用旧DLL打开已升级存档。

## 复现入口

所有命令经 `tools/dev.ps1` 执行，临时数据留在E盘。按依赖顺序：

1. `dotnet build src/ScCsgoAppearance -c Release '-t:Rebuild' '-p:SkipScmodPackaging=true'`
2. `python -X utf8 tools/pack_standalone_editions.py --edition Full --report output/release-bundle-1.5.0`，然后`python -X utf8 tools/pack_tactical.py`。
3. `optimize_weapon_resources.py` 输入Full主包、stage `.tmp/bundle150-derived`、report同上；再`pack_standalone_editions.py --edition Optimized512`使用相同stage/report。
4. `pack_tactical_lite.py --source <战术Full> --output <战术Lite> --report output/release-bundle-1.5.0/TacticalLite.json`。
5. 两版运行`PackageCheck`及`--tactical-package`检查，报告名称`core-full-check.json`、`core-lite-check.json`、`tactical-full-check.json`、`tactical-lite-check.json`；`TacticalLoadCheck`末尾支持指定配套core scmod。
6. `pack_release_bundles.py`拒绝缺失、失败或哈希不匹配的四份包检查报告，复核资源后生成总包及`bundles.json`。ZIP直接存储已压缩scmod，避免无意义的二次压缩。
