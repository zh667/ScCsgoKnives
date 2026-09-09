# 0.40.2 — 原生帮助页布局修复

## 根因与源码依据

2026-09-09，用户截图中的黑底、武器列表顶到左上、属性卡掉到左下均属于模组 UI 实现问题，不是枪械资源问题。

对照本机已有 `SC-SPM/SurvivalcraftApi` 源码（`.tmp/survivalcraft-api-source`，本次查看 HEAD `35245f4`）以及游戏安装目录 `Content.zip` 的原生控件：

- `Screens/RecipaediaRecipesScreen.xml` 的两个直接子控件是 Panorama 和横向布局。真正的 TopBar 在横向布局内部，不是名为 `TopBar` 的直接子控件。
- `Widgets/TopBarContents.xml` 定义的是宽 64、通高的左侧绿色导航栏，不是顶部栏。旧版隐藏直接子控件时连背景与导航栏一起隐藏。
- `StackPanelWidget` 根据 `ParentDesiredSize == PositiveInfinity` 分配剩余空间。仅用 Stretch 或 Canvas 的 `Size=-1`，仍然是按内容测量，不等于剩余空间。
- `ScreensManager.LayoutAndDrawWidgets` 将屏幕换算到 `850 / UIScale` 等逻辑尺寸；不能拿窗口的像素宽度作为断点。
- 本机 Game.log 的 10:37:17 异常为 `RecipaediaRecipesScreen.Enter` → `ScAssemblyRecipesScreen.Enter` 读取空参数数组越界。

## 实现

- 新增共享 `ScWeaponHelpScreen`。为满足 API `GetBlockRecipeScreen` 返回类型，仍是 `RecipaediaRecipesScreen` 的子类，但仅保留原生 Panorama 与导航栏，移除旧配方内容，不再运行旧配方页的 Enter/Update。
- 属性页与装配配方页共用原生外壳，分别设置标题；列表与属性卡底板直接采用原生帮助页的 `Styles/Area`（半透明背景与灰色边框），不使用模组设置页的深色底板。Back/Esc/手机返回都回到最初入口，属性与配方之间来回不构成返回循环。
- 内容区与列表、属性区显式申请剩余空间。正常横屏左侧为 200 逻辑单位的滚动列表，右侧为预览与属性；极窄界面列表收为上方 120 高的独立滚动区域。
- 右侧有效宽度不少于 520 时显示两列属性；不足则单列。名称、精确数值和说明分行排版并自动换行。
- 枪械预览、身份、八项属性、成长说明在同一个可滚动区域；装配配方和返回按钮固定在底部，不被长文字推出界面。首次选中状态与调整窗口后的控件重新挂接均已修复。
- 首次布局／尺寸变化输出 `[UI_LAYOUT native-rail-v2]`，含页名、逻辑尺寸、内容及返回键边界。不逐帧刷屏。
- 检查同时暴露旧计数器模板判断在 BlocksManager 未初始化时抛异常的问题；改为与已有皮肤模板一致的 TryGetValue 判断。未改 block ID、物品编码、计数规则、存档格式或枪械数值。

## 验证与边界

新增 `WeaponHelpLayoutRegression`，直接构造打包 DLL 中的界面，使用安装目录原生 XML、Pericles 字体字宽和游戏的 Measure/Arrange；不使用自己模拟的布局算法。

- 850×479（覆盖用户 1536×825 窗口的逻辑布局）、708×399（较大 UI）、1000×479、1200×675、480×850、360×640，再返回 850×479。
- 全部 35 把枪在以上 7 次布局中逐项验证标题和八项属性文字边界；包含狙击枪、霰弹枪、电击枪长说明。
- 原生背景可见且铺满、返回栏保留、旧配方内容不存在、左右/上下区域不重叠、滚动区及底部按钮有足够高度、末尾成长说明可滚到可见范围。
- 两个页面在浏览前后用空参数再次 Enter，不再越界。
- 新增 303 项布局检查；完整检查 6882 项。最终 Full/Lite 报告在 `output/ScCsgoKnives-0.40.2[-Lite]-check.json`。
- 测试只使用惰性的纹理对象占位，不创建 GPU，不渲染帧、不读写玩家世界。因此这些是原生布局计算检查，不是实机截图或手机触摸验收。
- 构建仍有 API 间接依赖 NCalc 的既有 NU1902 警告，本轮没有升级引擎依赖。

开始本轮时已有的 `ScGunAttributes.cs` 伤害说明、`ScGunAttributesScreen.cs` 两处身份文案、`ScGunUi.cs` 断点改动以及未跟踪皮肤规划文件均保留；不把这些既有修改代为提交。Windows 交付包包含工作区现有文案，其他端仅拉本轮提交时文案可能略不同，布局修复与测试逻辑不依赖这些文案。

## 实机验收

安装 Full/Lite 中的一份 0.40.2，避免同名旧包造成混淆。主菜单与游戏内分别进入帮助→武器→属性，检查背景、左侧绿色返回栏、列表、两列/单列属性与底部按钮；滚到底；进入装配配方再返回，重复两轮。手机需另外验证滑动列表与属性区、返回键及旋转屏幕。若仍异常，日志应包含 `native-rail-v2`，可据此判断是否实际加载本版以及采用何种逻辑尺寸。
