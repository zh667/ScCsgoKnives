# VPS 开工交接核查（2026-09-07）

> 执行更新：2026-09-08 00:22:15 +08:00，用户暂停 M4 之外的全部批次。当前先读 [M4 专项方案](vps-m4-durability-plan-2026-09-08.md)，其中的范围和顺序覆盖本页历史批次安排。本页保留作资源与环境参考。

对应 [社区反馈规划](community-feedback-plan-2026-09-07.md)。本次通过已配置 SSH 连接实际读取 VPS 文件和环境；不是仅根据同步规则推测。游戏功能未实施，本次准备规划、源资源和核对资料。

## 1. 固定要求

- 不增加远距离声音，不启用 distant 枪声/烟雾爆响或近远声混合。
- 按规划一次推进一个批次，M0 → M1 → M1b → M2 → M3 → M4 → M5。每批需实机视频与检查结果再进入下一批；VPS 无对应设备时交由 Windows/手机端验证，不能以离线图代替。
- 第三人称人物、换弹和投掷手臂姿态为手工制作；CS2 提供枪体/弹体、枪部件运动及时间参考，不提供可直接套用的原版人物动作或烟体运行时实现。
- 枪械伤害 ×1.5；22 刀轻刀 2.2 格、重刀 1.8 格，不加挖掘能力；爆头未击杀黄色，任何击杀红色。
- 每版新建世界，不做旧存档迁移；同版本保存与枪械流转必须完整。耐久以实例 ID + 世界状态表为主方案，首版单调分配不复用 ID。
- `AGENTS.md` 已更新上述新要求覆盖关系；旧的全物品无耐久报告属于历史证据，不能据此取消新功能。

## 2. 路径对应

| 内容 | Windows 工作区内相对路径 | VPS 实际位置 |
|---|---|---|
| 本模组 | `ScCsgoKnives` | `/home/dev/workspaces/ScCsgoKnives` |
| 开箱联动 | `ScCsgoBox` | `/home/dev/workspaces/ScCsgoBox` |
| CS2 主导出 | `CSMCReverse/local_cs2_analysis/all_weapons` | `/home/dev/workspaces/CSMCReverse/local_cs2_analysis/all_weapons` |
| 投掷物正式导出 | 主导出下 `12_grenades` | 主导出下 `12_grenades` |
| 本机 API 核对资料 | `CSMCReverse/references/scapi-1.9.2.1-windows-20260907` | `/home/dev/workspaces/CSMCReverse/references/scapi-1.9.2.1-windows-20260907` |

Linux 不使用规划中的 `E:\...` 路径。`tools/import_cs2_grenades.py` 现在默认从同级 CSMCReverse 正式目录取源文件，也支持 `--source` 显式指定；源清单以项目根目录为相对路径基准。运行导入器会重写生成资产，只有实施确实需要重新生成时才执行；检查路径用 `--help` 或清单校验即可。

## 3. 原先缺少什么，以及本次补齐的内容

| 项目 | 初次远端检查 | 本次处理 |
|---|---|---|
| `.tmp-survival-tools/grenades` | VPS 不存在，被项目 `.stignore` 的 `.tmp-*` 排除 | 313 文件、236,225,426 字节复制至共享 `12_grenades`；原目录保留，附逐文件 SHA-256 清单 |
| `reference/_scapi_decompiled/Game` | VPS 不存在 | 在共享 API 资料中提供本轮涉及的 30 个参考类，不复制整个历史参考仓库 |
| `reference/SurvivalcraftApi-audit-20260905` | VPS 不存在 | 不依赖此目录开工；使用本次归档的实际 DLL 重新核查关键接口 |
| 本机实际 API DLL/XML 与 `Content.zip` | 本机安装路径不在 VPS；现有 PackageCheck 输出无 Content.zip | API 资料的 `runtime/` 中归档 3 DLL、3 XML、deps/runtimeconfig JSON 及 Content.zip，共 9 文件 |
| 规划与三张截图 | VPS 已收到此前版本 | 更新规划，截图继续保留；最终以文档哈希核对 |

投掷物 `source-relocation-manifest.json` 记录全部 313 文件；API `manifest.json` 记录 39 文件、19,959,186 字节，包括 9 个当前安装文件和 30 个参考源码。归档清单不计入各自记录的源文件数量。

API 的 `reference-decompiled/` 来源是本机已有反编译目录，**未证明与本次 DLL 相同**，仅供定位；重要时序和数据结构请重新反编译 `runtime/` 中实际 DLL。`runtime/` 是核对资料，不是完整可启动游戏，也不是要求覆盖 NuGet 或测试宿主依赖。

本机 CS2 游戏 VPK 及 Windows `Source2Viewer-CLI.exe` 没有打包传到 VPS。已有导出足以开始规划中的工作；缺少新的源依赖时，由 Windows 端按清单补提取。不要把缺少 Windows EXE 当作编译本模组的阻塞，也不要为此下载整套游戏。

## 4. 已存在的资源与核查强度

首轮核查：`ScCsgoKnives/src/ScCsgoKnives` 的 903 文件、`ScCsgoBox/src/ScCsgoBox` 的 30 文件、三张反馈截图，按相对路径/大小/内容 SHA-256 核对一致；不含 bin/obj 和缓存。

CS2 主导出的 01–11 号目录，包括模型、材质、音频、粒子、开镜、枪刀 viewmodel、涂装和图标，按全部文件的路径及大小索引核对一致。**这部分不是逐文件内容哈希证明**；任务用到的具体源文件仍可对照原来源清单复核。10_paints 本轮不新增皮肤，仅核实既有资源存在。

最新快照见 [机器可读核查结果](vps-handoff-audit-2026-09-07.json)。新增两个归档必须满足 `manifestExists=true`、`failureCount=0` 且两端 manifest 哈希相同才算完整接收；看到目录或清单先到达不等于源文件全部同步完成。

## 5. API 二进制差异

当前 Windows 安装与 VPS NuGet 都标 1.9.2.1，但核心 DLL 内容不同。NuGet 包及 PackageCheck 当前输出相互对应；XML 的 Survivalcraft.xml 哈希则与本机相同。不能只用版本号或 XML 判断运行时完全一致。

| 文件 | Windows 实机 SHA-256 | VPS NuGet SHA-256 |
|---|---|---|
| Survivalcraft.dll | `51f9b4734a33193347670e8f3bfdac844c943348dda17dcc38ace4568af4c7e1` | `e334ed5ee69976e35fb7736ce603d4e651b14c6c4aa05d9e3596f2ff8c5286ea` |
| Engine.dll | `657701057aa5e3ac120707fc0223d518d5cc533b136168e62d3169b1bde16f73` | `168c2ba0b1e749248a521814d6d3854f87f8021a77fefb6da0ff0325d00ecc5c` |
| EntitySystem.dll | `81b5c056854feccfeafcff986401fca39ad0a86a4fac397b33fb2a84a1ba0784` | `322007734782d48e6e8eb4c8e960729a233a152ea6bfc776cbf20ab310006036` |

以归档 manifest 中的完整哈希为机器校验值。保留原 NuGet 构建流程；M0 对比所依赖的伤害、库存、动画与绘制接口及关键行为，再在 Windows 实机验证。包内 DLL 检查、NuGet 宿主检查和实际游戏测试分别报告。

## 6. VPS 工具环境

实际验证的 SDK 是 `.NET 10.0.302`，位置 `/home/dev/.dotnet`。非交互 SSH 的 PATH 没有它，应先设置：

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
cd /home/dev/workspaces/ScCsgoKnives
dotnet --list-sdks
```

| 工具/依赖 | 已确认状态 | 是否阻塞 |
|---|---|---|
| .NET SDK 与 SCAPI 1.9.2.1 NuGet | 已有；不在初始 PATH | 设置 PATH 后可进行既有构建流程 |
| Python3、Pillow、NumPy、SciPy、soundfile | 当前 Python 可导入 | 导入、打包和 CPU 工具基础依赖已在 |
| ILSpy | `/home/dev/.dotnet/tools/ilspycmd`，实测版本 11.0.0.9375 | 已可用于核对归档 DLL |
| GLSL 编译器 | `/home/dev/tools/glslang/bin/glslang`，实测 16.5.0；现有脚本已用此路径 | shader 静态编译工具已在，不代表实机渲染通过 |
| ffmpeg | `/home/dev/tools/ffmpeg` 存在，不在初始 PATH | 录屏后处理按需用此显式路径 |
| moderngl | 当前 Python 环境不可导入 | `render_survival_frames.py` / `render_survival_polish.py` 的 GPU 离线渲染需要它及可用 GL/EGL 后端；是否另有虚拟环境未全面核查 |
| xvfb-run、Source2Viewer-CLI | 初始 PATH 中未发现；常见 VRF 工具路径也未发现 | 不是本轮全部工作的共同前置；已有导出无需重提取，不宣称整台 VPS 绝对未安装 |
| Windows/手机实际游戏与 GPU 行为 | 此次只核查远端文件及 CLI，未验证 VPS 可运行对应实机环境 | 烟雾、第三人称和触屏必须由具备相应设备的一端提供视频 |

不把复制 Windows DLL 视为 Linux 实机环境已搭好，不把安装 moderngl 视为 GL 上下文已经可用。

## 7. Git 与开工顺序

本次初查两端 HEAD 均为 `5235e09b2cf017ee3bac6175ba828aa27809c874`。VPS 初查另有 `LICENSE`、两个安装记录 JSON 和两个旧内存报告 JSON 的本地差异，本次未重写/提交它们；它们不是缺少资源的证明。

1. 等待 Syncthing 完成，先读 AGENTS、规划、本文件及资源清单。git 对齐遵循既有项目约定，不能直接 pull/merge 覆盖共享工作树；不要提交另一端的在途修改。
2. 执行下面的只读核查，确认清单无缺项。核查脚本不会安装依赖、导入资源或修改项目：

```bash
python3 tools/handoff_resource_audit.py --workspace /home/dev/workspaces
```

3. 设置 .NET PATH，按需用 ILSpy 重查实际 Windows DLL；核对主 NuGet 与实机差异。需要重建时使用 `dotnet build ScCsgoKnives.sln -c Release`，三版打包沿用 `python3 tools/pack_scmod.py --edition all`；这些构建命令属于实施阶段，本次没有替 VPS agent 启动功能开发。
4. 先完成 M0 并提交证据，再推进单个批次。无实机视频的阶段停留在“待实机验收”，可以整理当前批次证据和修复当前问题，不进入下一批实现。

正式源资源在同级 CSMCReverse，**只复制规划文件或只克隆 ScCsgoKnives Git 仓库不能获得这些外部原始导出**；当前这台 VPS 经共享目录接收，其他 VPS 必须另行提供同级资源树或明确传入资源根目录。
