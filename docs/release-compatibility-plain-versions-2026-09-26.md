# 1.0.0 / 1.2.0 全量、轻量兼容包：纯版本号交付

日期：2026-09-26。四个包的 modinfo.json → Version 和 Integrations/CompatibilityFamily.json → version 均只使用 1.0.0 或 1.2.0，不带 -compat.1。双向兼容仍使用 family 1 / protocol 1；版本号相同的未修订原始包不因此成为兼容成员。

## 交付文件

文件均在项目 output/ 目录。同一版本全量、轻量使用同一 gameplay DLL。

| 文件 | 字节数 | SHA-256 |
|---|---:|---|
| [API1.9]CS武器1.0.0-双向兼容-全量包.scmod | 330,113,322 | 8bbcb323b615b331e31e32d60eb9e93b8ed97291813aa89620d943190b8d0a26 |
| [API1.9]CS武器1.0.0-双向兼容-轻量包.scmod | 75,848,241 | 39cabc241c5f232f6d17f7e3236dfd89fca1c3daa30ae2a828cbb0d85f9af80c |
| [API1.9]CS武器1.2.0-双向兼容-全量包.scmod | 376,588,904 | 71e05f1d62683f0081b6de233879cda86acae2f5541d91f9270b533a9438527c |
| [API1.9]CS武器1.2.0-双向兼容-轻量包.scmod | 79,617,268 | 5b23d24b60623baf8f76a752d85d2500b0eee768259841b32069456c3de67d65 |

## 修正内容

- 补齐两版全量包，简化四包内部版本号、文件名和安装说明。
- 修正 1.0.0 历史 DLL 只接受 Full 资源标记的问题：允许既有 Optimized512 资源并启用轻量资源策略。修正保存在历史源码回移脚本中，后续重建可复现。
- 1.2.0 gameplay DLL 未改变。轻量包纹理、模型、音频等未修改 ZIP 成员保留原压缩字节，没有再次转码或降低画质。
- 枪械 ID、v5 / schema6 / rules7、family 1 保存协议、手动备份策略保持不变。原始下载包、玩家存档及既有备份未修改。
- 轻量元数据同步工具重复运行时保留文件与溯源报告，避免无改动时覆盖上次变更记录。

## 验证

- 两版资源和包检查：1.0.0 为 526/526，1.2.0 为 617/617。包含 ZIP 完整性、纯版本号、双版本 DLL 一致、资源清单/哈希、纹理解码和轻量包低于 100 MB。
- 四个最终包分别通过 6/6 原生加载检查，合计 24 项；1.0.0 轻量包原先的资源门禁错误已消除。检查覆盖原生数据库、兼容钩子、真实迁移样例、重复加载不生成自动备份、休眠实体保留。
- 当前 1.0.0 / 1.2.0 / 1.7.2 六方向矩阵 243/243；与上一现代版本 1.7.1 的矩阵 243/243；原 1.0.0 兼容修订、新 1.0.0 兼容修订、1.7.2 矩阵 243/243，合计 729 项。
- 矩阵涵盖 35 枪、多级成长、弹量/耐久、两轮保存重读、模拟射击/击杀及异常数据保留。以上为离线回归和原生加载诊断，未进行 Android 或完整图形世界实机验收。

包/DLL 哈希和兼容矩阵明细见 [交付证据](release-compatibility-plain-versions-2026-09-26-evidence.json)。2026-09-25 记录中的旧文件名和哈希仅作历史证据，当前旧版交付以本文四个文件为准。

## 重建入口

遵循 tools/dev.ps1 的项目临时目录策略。使用 tools/stage_compatibility_sources.py 回移历史源码并构建 DLL；CompatibilityCheck 写入通过的 .tmp/compatibility-check.json 后，分别以 1.0.0、1.2.0 调用 tools/package_compatibility_family.py 生成全量包。已有轻量资源时用 tools/normalize_compatibility_lite_versions.py 同步版本和对应 DLL；无轻量包时使用 tools/package_compatibility_lite.py 派生。最后两版分别运行 tools/verify_compatibility_lite.py，四包分别运行 TacticalLoadCheck --compat-native。
