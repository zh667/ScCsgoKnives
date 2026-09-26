# 内部模型与动画编码研究（2026-09-26）

结论：优先保留现有模型、JSON 动画、探员原生缓存的原始字节，在资源内部使用 **Zstd 无损压缩**，由 C# ZstdSharp 解码后交给现有读取器。对当前分离包重建实验资源 DLL 后，预计轻量包约 **35.3 MB**，探员包约 **37.0 MB**，无需删除内容、减面或关闭检视。单靠本轮无损方案尚不能达到每包 30 MB。

这是编码研究和离线原型，未接入正式读取器、未发布新 scmod。以下总包体积是包含解码 DLL 开销的预算，不是已通过游戏/Android 验收的发行包。MB 均为十进制。实验材料位于仓库内 .tmp/resource-codecs-20260926；可移交的摘要、逐资源结果及哈希见 [证据 JSON](internal-resource-codecs-2026-09-26-evidence.json)。

## 当前编码和主要空间来源

从交付包实际提取 199 项资源：62 份嵌入模型、64 份武器动画、63 份 NPC 武器缓存、2 份探员动画缓存、8 份 GLB。本轮压缩实验覆盖前四类共 191 项；8 份 GLB 保持原样。轻量资源 DLL 内共 126 个嵌入资源。

| 资源 | 现有表示 | 未压缩总字节 | 本轮关注点 |
|---|---|---:|---|
| .parts / .skin，62 份 | 自定义小端二进制；浮点顶点、骨骼矩阵、索引 | 29,567,680 | 顶点数值编码及压缩 |
| 武器 .cs2.animation.json，64 份 | 曲线时间/数值数组、骨骼、片段、事件、附加动画元数据 | 33,523,859 | 直接压缩 JSON 与 float32 二进制转换对照 |
| NPC .scmesh，63 份 | 已烘焙的几何、材质和变换缓存 | 51,509,144 | 保留缓存，压缩内部数据 |
| CT/T .scanim，2 份 | 已转换的骨骼通道和逐样本 float32 数据 | 39,237,117 | 直接无损压缩、ACL 输入适配研究 |

模型的具体结构：

- SCK2PART v1：普通顶点为位置 3×float32、法线 3×float32、UV 2×float32，共 32 B；混合蒙皮顶点再带 4×byte 关节索引和 4×float32 权重，共 52 B。分部索引按 uint32 保存。
- SCK2SKIN v2：52 B 蒙皮顶点，骨骼名/逆绑定矩阵，以及材质和 uint32 索引。
- NWM1（magic 0x314D574E）NPC 缓存：26 B 顶点记录、32 位索引，另存部件/材质/变换等数据。
- SCACT001：骨骼名列表、片段名和时长、公共时间数组；每个通道含骨骼索引、属性种类及 3 或 4 个 float32 分量。CT/T 各 170 个片段。

对应生产代码：Cs2RigidMesh.cs、Cs2SkinnedMesh.cs、Cs2Rig.cs、ScActorAnimations.cs、ScNpcWeaponGeometry.cs。本轮只读取这些实现，没有改动它们。

“512”是颜色贴图分辨率，不是模型顶点精度。本轮没有再降低贴图，也没有改变模型轮廓。现有包中轻量资源 DLL 的 ZIP 数据已经占 **21,130,852 B**；探员的 .scmesh 与 .scanim 合计占 **29,014,068 B**。这是本轮着重研究内部编码的原因。未压缩数据体积不能直接等同于下载占用或运行时内存。

## 全包预算：原字节压缩即可取得明显收益

| 方案 | 轻量包估计字节 | 探员包估计字节 | 分别节省 |
|---|---:|---:|---|
| 当前交付基线 | 39,778,988 | 43,432,544 | — |
| 原字节 + Brotli quality 9 | 37,217,215 | 38,024,721 | 2,561,773 / 5,407,823 |
| **原字节 + Zstd level 19** | **35,256,107** | **37,006,502** | **4,522,881 / 6,426,042** |

预算方法：

1. 给各原始资源加研究容器和压缩流；不改变 JSON 文本、浮点值、几何、事件、资源名或缓存内容。该方案不使用 byte/bit shuffle。
2. 实际编译两份研究用 ScCsgoResources.dll，以 7-Zip/Deflate 测量压缩后的 DLL；Brotli 方案为 18,569,079 B，Zstd 为 16,462,008 B。
3. 从真实现行 scmod 字节数替换受影响成员的压缩长度，其余成员保持原流。探员缓存若新编码更大，预算保留原成员。
4. ZstdSharp.Port 0.8.8 的解码 DLL 原始大小为 396,800 B；轻量包计入其压缩体积及新增 ZIP 头，共 **145,963 B**。探员共用核心解码库，避免重复携带。

**未计入**最终读取器代码、清单和许可文件变化；最终发布时仍需构建并测量。研究 DLL 内是新编码资源，现有生产读取器不能直接读，严禁拿实验 DLL 替换正式包。

外层 scmod 仍应使用游戏支持的标准 ZIP；内部 Zstd 与“把整个 ZIP 改成 Zstd 算法”是两回事，需要显式接入读取器。

## 无损布局与二进制动画对照

实测原字节、byte shuffle、相邻记录逐字节 XOR 后 shuffle、256 记录分块 bitshuffle，分别搭配 Deflate9、Brotli9、Zstd19。bitshuffle 不足 8 条的尾部保留原顺序。它们是本地显式实现的布局实验，借鉴 Blosc 思路，未运行 Blosc 容器库。

| 数据组 | 独立文件 Deflate9 对照 | 最佳布局 + Brotli9 | 最佳布局 + Zstd19 |
|---|---:|---:|---:|
| 2 份探员动画 | 13,165,466 | 11,523,868 | 10,869,287 |
| 62 份嵌入模型 | 13,260,204 | 10,764,988 | 10,309,829 |
| 63 份 NPC 模型 | 17,002,348 | 11,972,334 | 11,667,676 |
| 64 份武器动画（二进制候选） | 9,496,479¹ | 7,896,834 | 7,714,002 |

¹ 对照列压的是原 JSON。候选将 Times/Values 转成 float32 数组，保留其他元数据；已验证数组形状、元数据和当前 .NET float32 数值位模式相同，但不宣称原 JSON 文本字节相同。

表中候选包含研究容器及外层 min(stored, Deflate9)；独立文件对照并非旧包真实压缩长度，尤其嵌入资源原先是在一个 DLL 中整体压缩。**不可直接拿此表差额扣减总包体积**；总包预算使用上节的实际 DLL 重建结果。

关键反例：原 JSON 直接 Zstd 的资源容器合计 **6,146,876 B**，比二进制曲线候选的 **7,714,002 B** 更小。去掉十进制文本并不自动更省；文本中的重复模式也能被强压缩。因此第一阶段无需改武器动画格式和采样逻辑。复杂 shuffle 也不是每组都收益，暂不引入生产代码。

## meshoptimizer：无损编码和精度量化要区分

实际运行 Python meshoptimizer==0.2.30a0 的原生绑定，对当前轻量版 62 份模型测试。保留 **603,741 个顶点、1,946,367 个索引**，不减面、不重排索引；骨骼矩阵、关节索引、权重和材质元数据保持不变。

| 顶点方案 | meshoptimizer + Brotli9 | meshoptimizer + Zstd19 |
|---|---:|---:|
| 原始精度，无量化 | 13,490,955 | 13,367,209 |
| 位置 18 bit / 法线 16 bit / UV 16 bit | 8,560,496 | **8,484,990** |
| 位置 16 bit / 法线 12 bit / UV 16 bit | 7,393,763 | **7,325,175** |

在这批模型上，meshoptimizer 单独无损编码甚至比最佳普通 Zstd（10,309,829 B）更大；收益来自对位置、法线、UV 做精度量化。两档分别再省约 **1.82 MB / 2.98 MB 的模型编码载荷**，不是直接测得的整包差值；还未计入 meshoptimizer 运行时解码器。

| 误差上界（实际顶点测量） | 18/16/16 | 16/12/16 |
|---|---:|---:|
| 位置欧氏距离，源模型单位 | 0.000102518880084 | 0.000412574102926 |
| UV 欧氏距离 | 0.000016647444973 | 0.000016647444973 |
| 非零法线夹角，度 | 0.001465004925 | 0.024187275277 |

meshoptimizer 顶点整数缓冲区、索引缓冲区及外层压缩回读均一致，但量化本身是**有损**。尚未实现完整生产容器读取器，也没有渲染和手机验收，不能据误差小就承诺肉眼完全无差异。若以后采用，必须同步核对/重烘焙对应 NPC 原生几何缓存，保持 idle 姿态和几何来源一致。

## C# 解码验证及手机端意义

- 初轮布局候选：**701 组 C# 回读检查，零失败**；每组另做 5 次热解码计时。
- 简单原字节方案：实际重建 **2 份 DLL**，核对资源名及解码后 SHA-256；**252 次嵌入资源检查、382 次资源流检查，零失败**。
- 实测环境：Windows x64、.NET 10.0.5、ZstdSharp.Port 0.8.8。不是游戏加载测试或 Android 性能结果。

简单原字节方案的耗时如下，数字为每文件 5 次热测中位数之和，单位 ms：

| 组 | Brotli | Zstd |
|---|---:|---:|
| 武器动画 64 份 | 86.21 | 53.67 |
| 嵌入模型 62 份 | 123.80 | 76.81 |
| 探员动画 2 份 | 155.59 | 89.58 |
| NPC 模型 63 份 | 188.84 | 109.39 |

磁盘读取、外层 ZIP、JSON 解析、GPU 上传、首次 JIT 和渲染都不在计时范围内；不同批次测量有波动，不能把总和当作玩家进世界时间。当前原型先解压到缓冲区再复制原始数据，存在额外分配；JSON 中的分配数是累计分配量，**不是峰值或常驻内存**。无损压缩主要降低下载/存储，解码后的几何和动画内存不会因此自动变小。

正式接入时应在既有加载/预热和缓存边界解码一次，避免每帧、每次召唤小队重复解压。mode 0 可交付自有缓冲区上的只读流，避免第二次完整复制；若使用池，必须等读取完成才归还。

## ACL 与 ozz：已研究适配，未实跑压缩比

查阅 ACL 的原始轨道、压缩、解压和均匀采样算法文档，并分析真实输入：

- 武器动画：64 文件、524 片段、66,032 曲线、657,407 key；其中 **20,036 条非均匀时间曲线**，共 **2,275 个事件**。
- R8 的 prepare_shoot_revolver 具有 RelativeToFrame / FirstFrame / idle 附加动画语义，不能只压数值而丢掉元数据。
- CT/T 各 170 片段；现有公共时间序列相对均匀网格的最大偏差约 **4.41×10⁻⁷ 秒**，同片段通道共用样本数，更适合进一步试验 ACL。零时长片段仍需单独处理。

ACL 的轨道数组要求一致的采样率和样本数；C++11/header-only 不代表 C# 可直接调用，还需 RTM、骨骼父子关系、误差度量和 shell distance 设置。附加动画需明确 base clip/format；事件、别名、循环边界、缺失通道语义需要外围保留。当前稀疏武器曲线若强制均匀重采样，可能增加样本量并改变插值和时序，不能当作直接替换。

ozz-animation 是 C++17 的加载、采样、混合框架；引入范围比资源无损压缩更大。**本轮没有执行 ACL 或 ozz 压缩器，没有它们在本项目上的压缩比、姿态误差或手机耗时数据。**

建议顺序：先用 ZstdSharp 无损压缩达成两包各低于 40 MB 的目标；需要进一步压小再考虑模型量化；若目标转向动画常驻内存/采样效率，优先对 CT/T 原生动画做独立 ACL 实验。

## 后续接入需要完成的验证

1. 核心提供统一的资源流解码层，探员调用核心；保留旧资源识别、原生缓存与缓存生命周期，不改变内容标识和 v5/schema6/rules7。
2. 明确流所有权：ContentManager.GetStream 共享源保持打开，只释放自有解码流/缓冲区。新封装需要长度上限、版本和完整性检查；坏的新缓存明确报错，不能静默回退到昂贵的 GLB/OBJ 重建。
3. 正式格式仍待设计。研究 RSCODE01 容器只用于测量，不能把成功回读等同于对恶意/损坏输入的生产级验证。
4. 解码库在核心只携带一份；检查程序集身份/依赖隔离，避免其他 mod 的同名 ZstdSharp 版本冲突。补齐锁定版本许可材料，核对目标 Android 的运行时/API、ARM 和 AOT 兼容性。
5. 重新运行资源加载、原生渲染、独立核心/带探员/缺失探员切换和存档兼容矩阵，并在手机测试首次进世界、重进、小队召唤、NEO 换模与峰值内存。没有这些结果前，不宣称卡顿或手机体验已改善。

## 开源依据与复现

直接读取以下官方项目的文档/源码；完整 commit、URL、内容 SHA-256 存在证据 JSON。文档快照与实际运行库版本分开记录，未把 GitHub 最新源码当成已运行二进制。

| 项目 | 本轮用途 | 验证层次 |
|---|---|---|
| [Zstd / facebook](https://github.com/facebook/zstd) | 通用无损压缩 | Python zstandard 0.25.0 / libzstd 1.5.7 实跑 |
| [ZstdSharp](https://github.com/oleg-st/ZstdSharp) | 托管 C# 解码 | NuGet ZstdSharp.Port 0.8.8 实跑 |
| [Brotli](https://github.com/google/brotli) | 无损对照 | Python Brotli 1.2.0 编码、.NET 解码实跑 |
| [meshoptimizer](https://github.com/zeux/meshoptimizer) | 顶点、索引编码与量化路线 | Python 0.2.30a0 原生绑定实跑 |
| [Blosc2](https://github.com/Blosc/c-blosc2) | 数值布局思路 | 文档研究、自写 shuffle 实验；未运行 Blosc 库 |
| [ACL](https://github.com/nfrechette/acl) | 动画编码适配 | 文档与实际输入结构分析 |
| [ozz-animation](https://github.com/guillaumeblanc/ozz-animation) | 动画运行时方案对比 | 文档研究 |

NumPy 为 2.4.6。Python 依赖隔离在 .tmp/resource-codecs-20260926/deps 与既有 .tmp/optimization-deps，脚本从这些路径加载。资源和 DLL 需合法持有的当前交付基线；基线 SHA-256 在证据中，脚本强校验，不能拿旧极简版模型替代。

从仓库根目录依次运行（完整压缩实验耗时较长）：

~~~powershell
./tools/dev.ps1 python -X utf8 tools/research_codec_sources.py
./tools/dev.ps1 dotnet run --project tools/ResourceCodecCheck -c Release -- extract . .tmp/resource-codecs-20260926
./tools/dev.ps1 python -X utf8 tools/research_resource_codecs.py
./tools/dev.ps1 python -X utf8 tools/research_mesh_quantization.py
./tools/dev.ps1 dotnet run --project tools/ResourceCodecCheck -c Release -- bench .tmp/resource-codecs-20260926
./tools/dev.ps1 python -X utf8 tools/research_codec_budget.py
./tools/dev.ps1 dotnet run --project tools/ResourceCodecCheck -c Release -- verify-budgets .tmp/resource-codecs-20260926
./tools/dev.ps1 python -X utf8 tools/research_acl_fit.py
./tools/dev.ps1 python -X utf8 tools/research_codec_evidence.py
~~~

来源采集脚本默认记录当时上游 HEAD，重跑可能取得更新文档；严格复核应使用证据内固定 commit URL。ACL 补充文档与 ZstdSharp Decompressor 源码的固定 URL/哈希在 sourceDetails，其本地抓取清单为 source-details.json；来源采集脚本也会生成该清单。

已校验当前两份交付 SHA-256 和大小均与基线相同。未写入全量/极简包、Mods 或玩家世界。研究工具、报告和证据通过 Git 移交；临时资源和研究 DLL 不作为可安装发行文件。
