# 1.5.1 单文件总包与进一步无损压缩

2026-09-24。用户要求进一步缩小轻量版，并明确“都打包成scmod”。本次最终交付替代此前ZIP分发方式：**每种画质只安装一个 `.scmod`，直接导入，无须解压子包。** 未安装或改写任何玩家世界。

| 文件 | 字节 | 十进制MB |
|---|---:|---:|
| `output/[API1.9]CS武器1.5.1-作者ZH667-全量总包.scmod` | 491,121,202 | 491.12 |
| `output/[API1.9]CS武器1.5.1-作者ZH667-512轻量总包.scmod` | 88,433,760 | **88.43** |

相对原94,263,117字节轻量ZIP，总包减少 **5,829,357字节（5.83 MB / 6.18%）**。在既有512轻量资源上没有进一步降低画质、减少模型、删除声音或裁剪动画。

全量 SHA-256：`64b9bb79518424a8ebf02bc5b9c9ca94e8ab69503a5c04d26ee284ac711ca762`

轻量 SHA-256：`eff0cb4df8e00d9b69b33710a0e17a4c055581c15a6e55fc8b66c1a303fe82f0`

## 内容与兼容处理

总包包含已验证的主包1.5.0、战术1.4.0和内置玩家外观适配，所有平衡、材料价格、±10预览、自制模型及Sushi修复均保留。三个玩法载荷 `ScCsgoKnives.dll`、`ScCsgoTactical.dll`、`ScCsgoAppearance.bin` 与上一轮完全相同，全量/轻量也相同。纯资源程序集因两版资源不同而不同，沿用上一轮已验证内容。

包版本1.5.1只对应整合分发和新增的小型身份适配器，独立主包/战术源码版本仍为1.5.0/1.4.0。根PackageName仍为 `zh667.ScCsgoKnives`，不是新模组身份。战术原metadata保存在 `Integrations/ScCsgoTactical.modinfo.json`；资源来源说明和许可证保留。

引擎允许一个scmod含多个ModLoader，但旧世界的`UsedMods`按PackageName检查缺失模组。因此新增 `ScCsgoBundle.dll`：

- 到`OnLoadingFinished`的延后动作才登记`zh667.ScCsgoTactical`身份，避免在原生程序集遍历期间修改模组集合。
- 身份项没有archive、Loader、BlockTypes和资源，不重复注册DLL、钩子或XDB。`GetModEntity`能识别旧战术身份，`SubsystemUsedMods.Save`继续保存两项原身份。
- 总包的主Loader仍是`ScCsgoKnivesModLoader`。模组重载时引擎原有初始化会清空身份列表；别名不持有总包archive，不会重复释放资源。
- 若同时启用独立战术拓展，总包初始化报出明确的重复安装错误，在进入世界前要求停用独立包。

布局v5、schema6/rules7、武器顺序、物品ID、同伴类型及保存字段均未改变。原1.0/1.2的迁移保护继续由相同主DLL负责；此前28,627项平衡/迁移回归证据可按DLL哈希对应，此轮没有声称重新实测玩家真实世界。

人物外观适配仍为按需加载`.bin`；NekoMeko Model1.1与Neorxna1.4不打包、不变成强制依赖。默认加载顺序先加载这两项前置的数据库，再由总包适配CT/T人物。

## 压缩方式与验证

`recompress_lite_bundle.py`使用7-Zip的**标准ZIP Deflate**，参数`mx9/fb258/pass15`。这不是Deflate64、LZMA或把其他格式改扩展名。每个原轻量scmod内部文件解压后逐字节一致：主包1,337项、战术84项。单独容器分别从70,818,796→65,909,846字节、23,441,148→22,570,620字节。

`pack_single_scmods.py`直接复制经过验证的压缩数据流，避免再次使用普通压缩而丢失收益；合并时生成新metadata、资源哈希索引，加入身份适配器及安装说明，其他载荷保持原字节。所有文件名显式UTF-8，只有Store/Deflate方法，无加密、ZIP64或嵌套scmod。输入必须与上一轮通过检查的包哈希一致。

检查汇总（[精简证据](release-single-1.5.1-evidence.json)，原始报告在`output/release-single-1.5.1/`）：

- 实际总包主DLL回归：全量12,224项、轻量12,937项通过。
- 实际总包战术回归：全量/轻量各56项通过。
- 18个独立加载进程通过：两版分别覆盖无前置、单前置、齐备、注册顺序反向、禁用、过旧、旧外观并存及重载禁用。包括总包根DLL清单、主Loader、别名延后登记、重复登记幂等、无重复资源/Loader、重复战术身份拒绝。
- 原生`SubsystemUsedMods`保留两个身份。完整前置及无前置场景各执行两轮XML保存/重读；刻意缺失依赖的测试主机场景检查原生内存保存，避免让全局序列化器扫描故意缺少依赖的第三方测试DLL。
- 游戏自身`Game.ZipArchive`与桌面解包器分别解出全量1,422项、轻量1,424项，逐文件SHA一致；打包脚本另核对全部文件与原载荷的哈希。
- 更新轻量检查器，联合主包和战术两份派生资源清单；全部712张独立WebP通过引擎图片解码和尺寸检查。

仍属于离线包内回归和原生加载诊断，不等同完整游戏、Android、联网或全部Mod组合实测。

## 安装

退出世界并备份完整世界目录，停用旧独立CS武器、CS资源、战术同伴及早期独立CS外观包，然后**只导入一个1.5.1总包**。全量与轻量二选一。需要CT/T玩家角色时保留NekoMeko/Neorxna前置。退回旧版本应恢复升级前世界备份。

## 复现

命令通过`tools/dev.ps1`执行，临时目录保留在E盘：

1. `dotnet build src/ScCsgoBundle -c Release`。
2. `python -X utf8 tools/recompress_lite_bundle.py`；会逐字节复核已生成的压缩包后复用，首次强压可能耗时较长。
3. `python -X utf8 tools/pack_single_scmods.py --edition Full`及`--edition Lite`。
4. 对实际单文件总包运行`PackageCheck`；战术检查的`--scmod`和`--tactical-package`均指向同一总包。`TacticalLoadCheck`支持相同路径，按默认原生LoadOrder验证数据库合并。
5. `TacticalLoadCheck --archive-identical <参考包> <原生解包目标> <报告>`可比较两个包，或以同一总包分别使用.NET与游戏ZIP实现逐文件对照。
