# 0.27.0 共享资源优化与 Full / Lite 双包

## 基线和交付

用户要求先保留当前代码，再为手机与电脑共同优化加载，完整版保留原画质，另交付轻量版。开始修改前工作区干净，`git push origin HEAD` 确认 c5008cd（0.26.8）已同步远端。

| 包 | 大小 | 资源与功能 |
| --- | --- | --- |
| ScCsgoKnives-0.27.0.scmod | 219.9 MB | 完整版 Full，原贴图、原粒子表现 |
| ScCsgoKnives-0.27.0-Lite.scmod | 111.6 MB | 轻量版 Lite，177 张 1024 贴图替换为 512，减少装饰性粒子 |

两个包使用相同 DLL、PackageName、版本号、物品索引与玩法，均保留全部 22 把刀、35 把枪、6 种投掷物、CS2 真实手、动画和无耐久机制。旧世界可切换版本，**同一游戏只能安装其中一个包**。Lite 推荐手机，也能在电脑使用；它不是自动下载的附加资源包。

## 共享代码优化

- 枪、刀、投掷物初始化不再遍历所有型号加载完整模型/贴图。物品栏只加载图标；手持备用模型、掉落物或三维预览需要时再构建对应模型。枪具既有 UI 绘制规则保留。
- 新增约数 KB 的 cs2_catalog.json。查询模型位置、是否有 CS2 资源不再反序列化整个动画文件；目录由动画源生成，打包时校验过期状态。
- 完整动画缓存上限 12 项；蒙皮武器和刚性武器分别 8 项。使用最近最少访问策略，重载后采样与模型保持一致。共享真实手臂仍只加载一份。
- 物品三维模型缓存目标：枪 12、刀 12、投掷物 8。最近两秒使用过的模型允许暂时超出目标，避免地面上大量武器反复卸载/重建；闲置后逐出。
- 世界退出通过 OnProjectDisposed 清理这些托管缓存。缓存逐出只释放拥有的托管引用，不直接 Dispose 仍可能被当前绘制使用的共享资源。

边界：此轮没有接管原版 ContentManager 的 GPU 贴图所有权。完整贴图改为需要时加载，但加载后的纹理仍遵循游戏共享缓存生命周期；这里的数量限制不是整个游戏的显存/内存硬上限。首次展示未缓存武器仍会有加载成本，不能仅凭代码检查承诺手机帧率。

## Lite 资源处理

- 高清源文件与构建目录保持完整。pack_scmod.py 在写 Lite ZIP 时从源清单对应的已验证构建资源生成派生 PNG，不覆盖源贴图，不删除任何资源路径。
- 只缩小 1024×1024 PNG；较小图标、特殊尺寸图集维持原样。颜色/透明度进行缩小滤波，法线贴图缩小后重新归一化并保留 alpha，ORM 各通道保持其用途。
- Lite 的实际大小比之前约 110 MB 的快速估算稍大，因为正式处理对法线向量做了归一化。
- Lite 减少爆炸装饰火星的一半，省去火区附加焰层/火星，并减少装饰烟丝。主火焰覆盖点、烟幕粒子与覆盖、闪光反馈不削减；伤害、遮挡、引信、火区、存档逻辑不变。完整版不改变任何粒子数量。
- Edition.xml 与 modinfo 名称标识 Full/Lite，加载后选择粒子策略。策略属于安装包版本；不需要新增游戏操作按钮。

转换清单见 resource-0270-lite-manifest.json；逐文件对照见 resource-0270-edition-parity.json。

## 验证

- Release 构建成功（仅既有 NCalc NU1902 警告）。完整版、轻量版各 **6025 项**包内/离线检查通过，0 失败。
- **1754 项**资源对照通过：两包 DLL 一致、路径一致；原有全部外部资源在 Full 中与 0.26.8 字节一致；Lite 仅允许 177 张贴图、名称/说明与 Edition.xml 的差异；法线长度误差符合量化容差。
- 新增检查覆盖初始化不加载模型/动画、63 项目录查询不读完整动画、缓存淘汰、热点模型保护、真实动画重载一致、22 刀/32 刚性枪/6 投掷物的三维网格重建、世界退出清理、包内版本标识加载、Lite 粒子差异。3 把既有 OBJ 路径的 GPU 模型不在无图形网格重建检查范围内；其资源字节与绘制构建逻辑保留。
- 使用独立无图形进程，依次访问全部 63 套动画与模型，再进行 GC，保留的托管堆增长：旧版 **161.3 MB**，新版 **25.6 MB**。缓存条目由 63/63/63 变成 12/8/8（条目可包括无对应类型模型的缓存结果）。见 before/after-memory.json。
- 此测量不含世界、GPU 贴图、原生内存和进程全部工作集；不能当作手机整机内存或 FPS 实测。没有启动/关闭用户游戏，也没有进行手机实机验收。

待实机复核：进入世界和首次切枪耗时、多武器反复切换、打开物品栏和三维预览、地面大量不同掉落物、投掷物爆炸/燃烧/烟幕、离开重进，以及 Lite 的远近贴图表现与法线高光。

## 构建命令

```powershell
# 只有导入/修改动画资源时才需要重建目录，然后重新编译
python -X utf8 tools/generate_cs2_catalog.py
dotnet build ScCsgoKnives.sln -c Release --no-restore
python -X utf8 tools/pack_scmod.py --edition both
python -X utf8 tools/check_release_editions.py --full output/ScCsgoKnives-0.27.0.scmod --lite output/ScCsgoKnives-0.27.0-Lite.scmod --baseline output/ScCsgoKnives-0.26.8.scmod --json docs/resource-0270-edition-parity.json
```

Lite 打包需要 Pillow 与 NumPy。默认 pack_scmod.py 仍只生成 Full，可用 --edition lite 单独打包 Lite。PackageCheck 的 --resource-audit 可独立测量；同一包的检查/审计需顺序运行，因为测试宿主按包哈希复用临时 DLL 路径。

## 哈希和本机安装

- Full SHA-256：`cbdf0eccbaed1e6fd472fb040ded6815b1c0058be8311b506b2e290a6ca38152`。
- Lite SHA-256：`4f413a8d3147587e01d3dac011da0149a844c3c190fdf06e41d0d736fe9e1fb6`。
- 共用 DLL SHA-256：`7eeafb27f9dcac50acca529e4ffa910a1e81ae98af2830ce80fc77a0790ed437`。
- 本机已安装 Full：`E:\EdgeDownload\[Windows]SurvivalcraftAPI_1.9.2.1\Mods\ScCsgoKnives-0.27.0.scmod`，哈希一致。
- 旧版备份：`E:\Obsidian Document\Document1\ScCsgoKnives\output\installed-backups\0270-20260906-132025\ScCsgoKnives-0.26.8.scmod`。
- Lite 单独交付在 output；未同时放入电脑 Mods。完整退出并重启游戏后才加载新 DLL。
