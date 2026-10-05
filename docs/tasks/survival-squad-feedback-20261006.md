# 生存平衡与敌对小队反馈

状态：代码与针对性离线检查完成，待游戏内验收；不是正式包发布验收。当前写入者：Windows Codex。分支：`feat/survival-squad-feedback`，基线 `ccbcfbf`（main）。

授权范围：用户的十项玩法/缺陷修复，按任务使用 Conventional Commits 提交。不安装、不发布、不修改原世界。
初始未跟踪文件 `.claude/scheduled_tasks.lock`、`.stignore.bak-before-psh-xdb-20260928`、`exceptions.txt` 保留。
切分支前本机无 Syncthing/Claude 进程，main 已是当前分支；新分支未改动工作树内容。未发起 VPS 交接。

| 任务 | 实现与验收目标 | 状态 |
|---|---|---|
| 1 掉落 | `ComponentTacticalEnemy.Died` 不再分配或掉落枪；弹药 60%、零件 40%、基础材料 50%（铁/铜/煤 1–2）、CS 小鸡生成蛋 8%（1） | 160 固定种子掉落、满表、重复死亡通过 |
| 2 布置 | 手动小队围绕指向地面四格内找位置，保留碰撞/落脚检测与三秒准备；不自动走向召唤者 | 创造/生存 × 三/五人 × 雪/普通地面、失败退回通过 |
| 3 反击 | 受击者不限反击距离；狙击 0.15 秒检查视线并停步射击，射程外保留末端衰减伤害；墙/烟仍遮挡 | 80/160/256 格实际伤害、目标释放、附近共同反击通过 |
| 4 冲锋枪 | `TacticalCombatMovement` 检查落脚/碰撞后横移，交战中可持续射击 | 横移与射击决策、障碍拒绝通过；跑打观感待实机 |
| 5 枪声 | 支持引擎无来源实体的枪声通知；寻找掩体，找不到时选安全远离方向；不赋予攻击目标 | 无来源枪声触发避让且不主动仇恨通过；实景掩体效果待实机 |
| 6 上岸 | 同伴与敌队共用 `TacticalNavigation.StepAssist`，水中依实测水面而非脚底判断岸高；保留墙、低顶与冷却约束 | 水中起跳指令、低顶/悬崖拒绝通过；CT/T 同伴与敌队实际游泳登岸待实机 |
| 7 换弹 | 换弹仅控制弹量计时，不跳过寻敌和导航；换好继续射击 | 不提前补弹、弹量守恒、远距离续射通过 |
| 8 狙击准星 | “狙击枪腰射准星”默认开启，沿用开镜/界面遮挡门控 | 设置保存/取消/默认通过；开镜和腰射观感待用户验收 |
| 9 刀与开局 | 原皮 7/12、皮肤 21/36；不新增耐久。四方案直接列出，点选即领取；三个装备方案均附送 **5 个通用弹匣**，“不赠送”无任何物品。首次出生等待其它模组弹窗和动画结束 | 全刀型伤害、400 次随机方案、满背包持久发放、两次读写、重复/非法网络请求、弹窗顺序、横竖屏通过 |
| 10 投掷物 | 手雷 96→120，火焰 6→7.5/秒（均 +25%，不叠加）；常规闪光半径 20→40；可见屏幕内远处爆点至少闪 1 秒，墙仍遮挡。联机使用独立摄像机视野消息，0.5 秒过期 | 200 格爆点实际致盲、墙后/背后不远闪、火焰重叠、远程视野边界/失效通过；双端体验待实机 |

数值为本轮实现默认值，非实机验收结论。保持已有默认中立规则；用户的创造模式要求不作为擅自恢复生存主动攻击的依据。

用户跟进：不使用循环切换后再确认的开局弹窗；直接列出四个选项，手机适配，以 `D:/下载/[Android] SurvivalcraftAPI1.9.3.2.Apk` 为目标。随后明确装备选择附送五个弹匣。实现以该跟进为准。

## 验证

执行前读取 AGENTS.md 与 collaboration/build-and-release/compatibility/rendering-and-resources。通过 `tools/dev.ps1` 构建与运行针对性检查。
行为检查、编译、实机/用户观感分别记录；未执行的检查不报通过。尸鬼包仅只读参考开局弹窗，不改第三方。

## 结果

执行命令（工作目录 `E:/projects/ScCsgoKnives`，PowerShell 参数含冒号需引号）：

```powershell
./tools/dev.ps1 dotnet build src/ScCsgoTactical/ScCsgoTactical.csproj -c Release '-p:SkipScmodPackaging=true' --nologo '-v:q'
./tools/dev.ps1 dotnet run --project tools/GameplayFeedbackCheck -c Release '-p:SkipScmodPackaging=true' -- .tmp/dev-temp/gameplay-feedback-final.json
./tools/dev.ps1 dotnet run --project tools/FollowupCheck -c Release '-p:SkipScmodPackaging=true' -- 'D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Content.zip' .tmp/dev-temp/survival-feedback-ui
./tools/dev.ps1 dotnet run --project tools/FollowupCheck -c Release '-p:SkipScmodPackaging=true' -- .tmp/dev-temp/android1932-content.zip .tmp/dev-temp/survival-feedback-android-ui
./tools/dev.ps1 dotnet build tools/PackageCheck -c Release --nologo '-v:q'
```

- 本体/战术编译通过；两条原有不可达代码警告。PackageCheck 编译通过（四条原有未使用函数/字段警告），**未执行整套正式包检查**。
- GameplayFeedbackCheck **82/82 通过**：SCAPI NuGet 1.9.3.1 的真实类型与原生算法、隔离内存世界。包含受影响的已维护 TacticalEnemyRegression/StarterEquipmentRegression，以及新回归。不是启动游戏实测。
- FollowupCheck：Windows 原生内容与指定 Android APK 提取的 `assets/Content.zip` 各 **147 项通过**。实际绘制检查 320×568、360×640、640×360、850×479、960×540；四项全部可见、触控高度至少 48、一次点击仅提交一次。使用 Windows 原生渲染器加载 Android 原始资源，不能替代 Android 运行时。
- 已目视核对 360×640 与 640×360 预览。预览：`.tmp/dev-temp/survival-feedback-android-ui/starter-list-360x640.png`、`starter-list-640x360.png`。两组 `followup.json` 保留在各自目录。
- `adb devices` 无连接设备；Android APK 没有安装或改写。未执行 1.9.3.2_MP 双进程联机、Android 实机、Full/Lite 正式打包与 1.4.0 双向切换矩阵，未替换 `output/`。
- 远距离回归的首轮确实失败（80 格仍射击但零伤害），定位到 `ScGunHandling.Falloff` 的射程外硬归零，修复后相同场景通过。首轮报告保留 `.tmp/dev-temp/gameplay-feedback-r1.json`、`r2.json`；没有降低伤害断言。
- 水中额外核查发现引擎浮力使脚底低于水面，单纯移除游泳排除条件不足；已补充水面高度判断及对应失败区分用例。
- 当前检查核心 DLL SHA-256：`292bfbe3d8cb681e9a8aff990c26af16bc90e2f568dbf2b4c86075aca1a3c46d`；战术 DLL：`1c9f653abfd3bc6088a9dd6ff018fb47e6a408be6485225a9b30ae173989c259`。最终源码见本分支提交；正式构建可因程序集构建来源戳改变哈希。
- API 引擎 SHA-256：`2ef7c1918ea9d09f52a807719909bf28c722bd3a2793eeaf81db0c343b9eb9d3`（程序集版本 2.4.0.0，包版本 1.9.3.1）。Android APK SHA-256：`c1f65bdf19f5ecb86362b947a4b43cc38ba3119827a7de0e5f58589814adfb7d`。
- 尸鬼参考包 SHA-256：`6c0a1d7eec67a612ebbdf51908e3a16de80f126bc753cab3090c255600fb3a3c`，与现有只读反编译缓存一致；仅参考其 `SubsystemSAG.Update` 的首次弹窗时机。
- 本轮无安装、发布、玩家世界写入或数据删除。构建复用同一输出目录，无新增多份大构建阶段；15.6 MB Android 内容副本是当前 UI 检查输入，予以保留。aapt 不支持原路径 Unicode、跨盘硬链接尝试未成功，没有产生引用 APK 副本。构建约 3–11 秒/轮、行为检查约 5–6 秒/轮、UI 约 8 秒/轮；无远端工作进程等待。

剩余验收：真实地图上 CT/T 同伴与敌队的水岸导航和跑打/掩体观感；狙击腰射/开镜准星与实际闪光；Android 触控和联机双端。未把共享导航辅助的离线检查冒充三个角色的物理游泳实测。
