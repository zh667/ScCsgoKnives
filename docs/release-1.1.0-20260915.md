# CS 武器 1.1.0：全量与 512 轻量独立安装包

用户要求从公测 1.0.0 延续到 1.1.0，并将资源和逻辑放在同一个 scmod。暂停新提出的性能重构；玩法代码保持优化前基线 a8fa495。运行时代码的本轮变化仅为内置资源校验同时接受 Full 和 Optimized512，并调整不完整安装提示。

## 安装

- 全量版：`output/[API1.9]CS武器1.1.0-全量版.scmod`，375,776,191 字节（375.78 MB）。
- 512 轻量版：`output/[API1.9]CS武器1.1.0-512轻量版.scmod`，66,727,614 字节（66.73 MB），含本页末尾的 C4 颜色修正。
- 二选一安装，两个版本都包含资源及逻辑。退出游戏后移出旧 CS 武器包；若此前安装过单独的 CS 武器资源前置包，也应移出，避免重复加载。其他模组不需要因此移除。
- 两版 PackageName 都是 zh667.ScCsgoKnives，版本都是 1.1.0，无资源前置依赖。内部仍有两个 DLL，但玩家只需一个 scmod。
- 轻量版在没有保存过明确设置时默认启用“简化材质”；已有设置优先。两版可互换，画质差异不会改变枪械编号或击杀计数。

## 轻量规格

沿用现有工具生成 604 张 WebP：普通图 Q85、最长边不超过 512；4 张特殊图集（C4 数字、RGBM 环境、枪口火焰、枪口烟雾）保留尺寸和像素，采用无损 WebP。法线缩放后归一化。音频原样保留。

286 份模型资源中 230 份减少面数，累计三角形从 1,846,045 降至 974,110。沿用边界、骨骼连接和极值点保护；不是运行时 LOD，也不承诺所有模型都减半。126 份动画/二进制网格通过独立检查：动画字节不变，骨骼头、材质身份和保留顶点的属性不变。

模型总面数是资源全集统计，不是一帧开销。包体积减少约 82.2%，不能换算为帧率收益；Android GPU、低端手机帧率及温度没有在本轮实测。

## 验证

测试实际加载交付 scmod 内 DLL，使用本机 API 1.9.3.1；旧版本输入为用户提供的 `D:/下载/[API1.9]CS武器1.0.0.scmod`。

- 全量：20,072 项，0 失败。
- 轻量：20,677 项，0 失败；包括 604 张 WebP 的真实 Engine.Media.Image 解码。
- 2,480 项两版交付对照通过：玩法 DLL 相同、音频与非派生内容相同、资源清单覆盖、图像尺寸/透明度/特殊图集、最终报告哈希匹配。
- 两版含旧 DLL 生成状态的迁移、两轮 XML、模型/皮肤/动画及单文件资源校验。并非完整玩家世界的实机测试。

未修改世界，也未安装到游戏 Mods。当前“仅计数”世界迁移会错误升级的已知问题沿用基线，没有以打包掩盖为已修复；普通“计数＋成长”迁移通过。历史丢失记录和已耗尽编号不会由升级自动恢复。参见 `migration-100-risk-20260915.md`。

## 文件哈希

```text
Full scmod   da60f45f2d286c2c51c0685aca10cde483352ff9cf81ad1d6c111eb4ae1cc618
Lite scmod   852789750c3ee1fa9aa2cf590b88c70c78f5cf76521b42382e9e2811a8848c79
Gameplay DLL 7e4196500cd41a8d04af80a754486678ba6dac6339f76d2a74088910b8b46799
```

本轮重新生成每个交付文件及报告，不沿用旧 1.0.14 ZIP 或其测试报告。完整证据位于 `output/release-1.1.0/`。

## 复跑

所有命令在仓库根目录通过 `tools/dev.ps1` 运行；构建临时文件留在项目 .tmp。

1. `dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release -p:GameDllDirectory=<实际游戏目录>`。
2. `python -X utf8 tools/pack_standalone_editions.py --edition Full`。
3. `python -X utf8 tools/optimize_weapon_resources.py --source-package "output/[API1.9]CS武器1.1.0-全量版.scmod" --stage .tmp/standalone110-derived --report output/release-1.1.0`。
4. `python -X utf8 tools/validate_optimized_models.py --stage .tmp/standalone110-derived --report output/release-1.1.0/model-integrity.json`。
5. `python -X utf8 tools/pack_standalone_editions.py --edition Optimized512`。
6. 分别对两个包运行 PackageCheck，使用 `--previous-growth-package <公测1.0.0包>`，无需 `--resource-pack`。JSON 分别输出到 full-check.json、lite-check.json。
7. `python -X utf8 tools/verify_standalone_editions.py --version 1.1.0`。

## C4 轻量贴图修正（同版本替换）

用户确认保持 1.1.0，只替换轻量 scmod。原轻量文件哈希为 `569febaebb76b579e1d5f3c8337de67c41b3ea10a8f078a9f33e407837847507`，新哈希见上。全量 scmod 原字节保持不变。

C4 主体源图的 Alpha 存储材质数据，主体渲染路径始终按不透明 RGB 绘制。轻量工具误按透明 RGBA 缩放和有损 WebP 编码，丢掉 Alpha 为零区域的有效 RGB，造成黑色外壳和大片白色条带。修正为对 `c4_cs2.png` 在缩放之前转换成 RGB，其他透明图标/粒子仍保留 Alpha。无需修改着色器、模型、动画或存档逻辑。

本次包内仅更改 `c4_cs2.webp`、派生资源来源清单和资源哈希清单；玩法 DLL、资源 DLL、所有模型/动画及其他纹理/音频与原 1.1.0 轻量包字节相同。错误版备份保存在项目 .tmp/c4-color-backups 下。

颜色回归以正确 RGB 缩放为参照，旧转换平均绝对通道误差 83.97，修正后 2.30，原 Alpha=0 区域的 RGB 误差为 2.39（通道范围 0–255）。图标透明度检查通过。独立桌面 GPU 使用生产简化着色器和实际轻量 C4 网格重现旧黑白异常，修正后恢复主体颜色；这是离线渲染，不是 Android 游戏截图。结果位于 `output/release-1.1.0/c4-fix/`。

源打包工具已修正，正常重建轻量版会带入正确颜色；现成文件的本次定向替换可通过 `tools/repack_c4_color_hotfix.py` 复核，颜色检查为 `tools/verify_c4_color_conversion.py`。
