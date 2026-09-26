# 两个 1.3.0 分离包的开源无损压缩实测

用户询问能否用开源工具继续压小。以分离初版为基准，测试 Zopfli、ECT，再与已有 7-Zip 压缩流逐文件择优。
本轮不删内容、不降低贴图分辨率或质量、不减面、不删动作、不改代码和存档格式。公开版本仍为 **1.3.0**。

## 最终结果

MB、KB 均按十进制统计。两份交付已替换项目 output 下同名文件。

| 文件 | 原字节数 | 新字节数 | 新 MB | 节省 |
|---|---:|---:|---:|---:|
| 轻量包 | 39,927,338 | **39,778,988** | **39.778988** | 148,350 B，0.372% |
| 探员包 | 43,762,485 | **43,432,544** | **43.432544** | 329,941 B，0.754% |

合计省 **478,291 B（478.291 KB，约 0.48 MB）**。外层 ZIP 的进一步收益有限；本次没有把探员包压到 40 MB。
两包仍直接导入游戏，轻量包可独立使用，探员包依赖配套轻量包。全部 35 枪、44 枪皮、22 刀及刀皮、检视与探员内容均不变化。

新 SHA-256：

- `[API1.9]CS武器1.3.0-轻量包.scmod`：`2d4cfc231c7a4e419eb3e7332fbf6b89d1fdcc0601e2c174d0c60b2543c43ea3`
- `[API1.9]CS武器1.3.0-探员包.scmod`：`9c430eecdbfd44ee501cd75352f3086c26ad98e6cda210bccc120ffac6c5d2db`

压缩前的两份交付保留在 `.tmp/split-lossless-20260927/baseline/`，属于开发归档。未安装到游戏 Mods，未改玩家世界，其余 scmod（含全量、极简）均校验不变。

## 实验方法

1. 基准已经使用 Python zlib9 与 7-Zip Deflate `-mx=9 -mfb=258 -mpass=15` 逐成员择优，并非未优化的普通 ZIP。
2. Google Zopfli 的 Python 绑定固定为 `zopfli==0.4.1`。2 MB 以上成员使用 5 轮，较小成员 15 轮，block splitting max=15。对 985 个成员试验；小于 256 B 或原压缩率大于等于 98% 的成员保留原流。验证 Zlib 容器和去头尾后的 raw Deflate 均解回原字节。
3. 官方 ECT 0.9.5 Win64 对两包运行 `-9 -zip --strict --disable-png --disable-jpg --mt-deflate=2`。禁用 PNG/JPEG 内部优化，不启用 metadata stripping，避免更改资源文件字节。发行 ZIP 和 EXE 的 SHA-256 存入证据。
4. 比较每个成员的原流、Zopfli 流、ECT 流，取字节数最小者。相同时保留原流，其次 Zopfli。重新写标准 ZIP stored/Deflate，不用 ZIP64、加密或新运行时解码器。
5. Python CRC/SHA-256 和游戏自己的 `Game.ZipArchive` 都读取最终包，对照基准逐项比较；通过后使用临时文件加 `os.replace` 更新交付。

| 候选策略 | 轻量包字节数 | 探员包字节数 |
|---|---:|---:|
| 原流与 Zopfli 择优 | 39,880,741 | 43,703,474 |
| 单独 ECT -9 结果 | 39,788,679 | 43,436,335 |
| 原流、Zopfli、ECT 三者择优 | **39,778,988** | **43,432,544** |

| 文件 | 保留原压缩流 | 采用 Zopfli | 采用 ECT |
|---|---:|---:|---:|
| 轻量包 | 677 | 386 | 267 |
| 探员包 | 54 | 213 | 102 |

Zopfli 单独择优共省 105,608 B；它对最大的资源 DLL、CT/T 动画缓存没有收益。ECT 则分别让资源 DLL 少 64,456 B、CT 缓存少 51,688 B、T 缓存少 50,867 B。不能把工具宣传的典型压缩率套用到已经强压缩的资源上。

## 验证与边界

- 原生解压器验证轻量包 **1,330** 个、探员包 **369** 个成员，共 **1,699** 项全相同。
- 路径、模型、贴图、枪皮、刀皮、所有动作/事件、NPC/演员缓存、音频、DLL、modinfo/依赖和兼容清单均保持原字节。
- 本轮相对上一版完全无损；此前轻量化资源已经做过的画质处理仍然存在。
- 资源读取路径未变。上一版渲染和存档测试对应的内容没有变化，本次补验新 ZIP 流，没有声称重新跑全部 GPU 或 Android 实机测试。
- 下载包变小不代表展开后的模型/动画占用下降，也不能据此声称帧率提高或卡顿进一步改善。

## 后续空间

实测表明，这两种工具对现有 Deflate 包的额外收益很小，并不意味着已穷尽全部编码方案。
更大幅度缩小值得继续研究内部模型/动画布局和编解码，需要同时验证误差、动作事件、缓存、解码时间及手机加载。
已有 meshoptimizer、ACL、Brotli、Basis 调研见 [之前的实测](minimal-open-source-compression-2026-09-26.md)，不能把离线编码实验当作已接入的运行方案。
7z、LZMA、Zstd 等容器也不能只改后缀冒充可直接导入的 scmod。

## 来源与复现

- [Google Zopfli](https://github.com/google/zopfli)：标准 Deflate/Zlib/Gzip 输出，现有解压器可读取。
- [py-zopfli](https://github.com/fonttools/py-zopfli)：本次固定 0.4.1，大小文件迭代次数按其建议配置。
- [ECT 0.9.5](https://github.com/fhanau/Efficient-Compression-Tool/releases/tag/v0.9.5)：使用官方 Win64 发行文件。
- [完整证据](split-lossless-compression-2026-09-26-evidence.json)：固定基准和最终哈希、每成员 SHA-256/新旧压缩大小/来源、原生检查绑定、工具参数和来源哈希。

基于证据中固定 SHA-256 的分离初版文件，通过 `tools/dev.ps1` 运行 `recompress_split_lossless.py` 与 `refine_split_lossless.py ect`。两者可并行，均完成后依次执行后者的 `merge`、`validate`、`publish`。Python 依赖装入阶段目录 `deps`，ECT 位于 `bin/ect.exe`。

阶段目录为 `.tmp/split-lossless-20260927`（本机时区目录标签），报告用 UTC 时间戳。压缩缓存、完整过程日志及下载来源原文均留在其中。每次发布重新绑定候选哈希、成员哈希、原生报告与当前交付身份，不重新构建或改写原始资源。
