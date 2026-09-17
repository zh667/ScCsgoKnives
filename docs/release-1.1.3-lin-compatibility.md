# CS 武器 1.1.3：lin 简易枪械第一人称兼容

2026-09-17。玩家反馈：安装 lin 简易枪械后，CS 枪缩小并竖在屏幕右下角，正常的持枪手臂和动画未显示。

## 根因证据

检查用户提供的 `D:\下载\[1.9.3.1] lin的简易枪械v0.4 .scmod`：文件名写 v0.4，内部 modinfo 为 Version=0.3、PackageName=`Lin's gun`。

原包 SHA-256：`73ce311311254626eea1041168afe7956f4e2cf7af732b43e0fbde3bd6976b68`。

其 XDB 将原生 FirstPersonModel 的 Class 参数（GUID `bb0d545a-b76e-48a9-8966-022d79b4769a`）注册替换为 `Game.ComponentNewFirstPersonModel`。API 在数据库合并时先收集 ClassSubstitutes，再根据选择应用替换。

Gun.dll 内这个组件重新实现了 `IDrawable.Draw`，该方法没有调用 `ModsManager.HookAction`，也没有进入原生 `ComponentFirstPersonModel.Draw`。当物品不是它自己的枪时，直接用 `Block.DrawBlock`、普通物品的 FirstPersonScale/Offset 绘制。CS 原本在 `OnFirstPersonModelDrawing` 钩子内绘制完整枪械、手臂与动作；入口被绕过后，就出现截图里的普通物品式小模型。

实际 DLL 的方法体和接口绑定验证支持这个原因；尚无本轮修复后的游戏画面证据。

## 修复方式

在 CS 内注册 API 的 `OnIDrawableAdded`：只识别 `Gun` 程序集中的已知 `Game.ComponentNewFirstPersonModel`，为其添加一个绘制转接对象。

- 当前或切换期间仍在显示 CS 物品时，调用其继承的 API 绘制入口，恢复原来的 CS 渲染钩子。
- lin 枪、空手及其他物品仍调用 lin 原始的接口绘制方法。
- 保留 lin 原组件、Update、换弹/检视状态；不替换玩家组件，不重写它的 XDB，不修改 lin scmod。
- 使用绘制值和当前选中值共同判断，保留切入/切出时的物品交接；投掷物/C4 消耗槽位后短暂保留的模型也走原有逻辑。
- 重复注册不会重复绘制；实体移除时移除转接对象，退出世界清理；尊重之前其他模组已经声明的绘制接管。
- 检查方法槽位。若未来 lin 版本变成真正重写基类 Draw，拒绝套用当前方案，避免递归调用；不宣称兼容所有未知版本/替换组件。

不新增运行时反射逐帧开销：两个绘制方法在注册时绑定为委托。枪械编号、布局 v5、schema 6、等级、击杀数、弹量、耐久和资源均未修改。本轮基于 1.1.2，只出全量包，保留之前版本，不写游戏 Mods 或玩家世界。

## 检查与限制

新增 **22 项专项检查全部通过**，直接读取用户 lin 包内 Gun.dll 与交付包内 CS DLL：

- 原包 XDB 替换注册、lin 方法体缺少钩子、API 原生方法包含钩子。
- 实际引擎 `SubsystemDrawing.OnEntityAdded` 注册、两个委托分别绑定到真实的基类与 lin 方法。
- CS 枪、刀、手雷、C4、弹药、零件的分流；lin AKM 类型、原版物品、空手继续走 lin。
- 切入/切出、重复注册、移除再加入、两名玩家与退出清理。

这些是无 GPU 检查。绘制分流测试将最终图形调用委托换为计数器，验证恰好调用正确入口；**没有声称执行了完整 GPU 绘制或得到了修复后的实机截图**。lin 枪自身的开镜、换弹音画仍需游戏中验收。

## 安装与快速验收

退出游戏，用 `output/[API1.9]CS武器1.1.3-全量版.scmod` 替换旧 CS 武器包，保留用户提供的 lin 包。CS 只安装一个版本，无需另装 CS 资源包。

1. 进入原世界，依次拿 AK、M4、刀、手雷/C4，应恢复正常大小与手臂动作。
2. CS 枪 → lin 枪 → 空手/原版物品 → CS 枪，各切换几次，检查没有双模型和停在角落的旧模型。
3. 分别检查两边枪械的开火、换弹、检视、开镜；退出重进再看一次。

若实际游戏仍异常，需要当次游戏日志判断本机实际加载的 CS/lin 版本与组件选择；不要根据这张旧截图宣称真机修复已经验收。

## 交付记录

最终检查摘要与包哈希见 [release-1.1.3-evidence.json](release-1.1.3-evidence.json)。完整本机报告：`output/release-1.1.3/full-check.json`。

- 编译 0 警告、0 错误。最终报告 21,831 项、0 失败，其中 21,685 项回归/资源检查，146 条来源与模板审计信息。
- 整包 375,784,677 字节（约 358.4 MiB），SHA-256：`8050f218e00b960834bfe319fbfb8aa9d9ddcafee99e5a7078a4cdecbfa6866f`。
- 玩法 DLL SHA-256：`3a2081fc2c94c5bb273856fc893e08335c5ed6819190b28c2ab4ad1b0b298f06`。
- 资源 DLL 与 1.1.2 相同：`e785dea8ef5b1cae84a6f534bdd63f556b444a8355a9182a7a02c92db9420de2`。
- 首次完整运行因旧审计清单中一个模组已移走而中断；重新只读提取当前 Mods 清单后完成了最终回归，没有修改游戏安装目录。

复核命令：

```powershell
./tools/dev.ps1 python -X utf8 tools/extract_creature_templates.py 'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods' .tmp/creature113-xdb
./tools/dev.ps1 dotnet run --project tools/PackageCheck '-p:GameDllDirectory=D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1' -- --scmod 'output/[API1.9]CS武器1.1.3-全量版.scmod' --lin-gun-package 'D:\下载\[1.9.3.1] lin的简易枪械v0.4 .scmod' --previous-growth-package 'D:\下载\[API1.9]CS武器1.0.0.scmod' --vanilla-content 'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Content.zip' --sushi-inventory-mods 'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods' --creature-extracted-root .tmp/creature113-xdb --json output/release-1.1.3/full-check.json
```

仅跑本次专项时可加 `--lin-compat-only`；XDB 审计目录的生成方法沿用 1.1.2 交付说明。
