# 0.41.13 当前武器属性、全型号名称与33款皮肤提取进度

日期：2026-09-10。这是部分功能交付与真实阻塞记录，不是33款皮肤全部完成。

## 已完成的游戏功能

- 枪械台新增“查看当前武器属性”，旧入口改为“武器图鉴／等级预览”，两者明确分开。
- 当前属性为独立工作台对话框：可以单击选背包/快捷栏中任意现有枪，默认选手持枪；全部原厂、皮肤、计数器及尚未实例化的合法模板可读。不列创造模式无限目录、不扫描箱子、不自动拿图鉴模板代替空背包。
- 八项属性直接消费 `EffectiveGunStats.Resolve` 的最终结果；不是UI另按等级生成。两个同型号不同等级的枪分别显示，待升级状态仍按已生效等级显示。M4/USP按实际消音器状态选择参考模式；静止、腰射、首发与近距伤害的条件写清楚。
- 不显示耐久数值、制作材料、不显示等级+/-。不分配ID、不改击杀、等级、弹量、耐久或充能；物品移走/同槽替换/注册表换世界时失效，不误读另一个实例。修订变动刷新。
- 两列属性在窄屏转一列，固定返回按钮、滚动详情；返回复用枪械台原导航闭包，不使用配方页切换，避免新增帮助循环。
- 统一35型号简名到 `ScGunNames`。普通枪、皮肤模板、计数器模板、帮助身份和枪械台共用。实际计数器模板和拿到手后的名称一致。内部分配/资源键仍不变。
- 费用档中文化；枪械台皮肤伤害说明由过时“满级再+100%”改为读取当前30级倍率。清除中文描述中旧“编辑物品/铅笔检视”的误导。

## 已提取，未完成可玩移植的部分

33款请求（只取格洛克翡翠1119，不含任何其他阶段）已按当前VPK重新核对，并提取/解码：

- 暂存目录 `.tmp/finish33-source-20260910/`，独立于正式资源包。
- 228份原始编译资源，约195.75MB；解码得到160 PNG、1 EXR、34 VCOMPMAT、33 VMAT。99张库存图为原来的light/medium/heavy对照，不是最终崭新图标。
- `source-manifest.json` 保存每个原始资源的SHA256、每款paint ID/合法最低磨损/模型/种子0及“source-only”状态。
- 每个请求都有原始配方和官方武器关联；本机35枪原有模型/动画未重复复制。
- P250银装素裹目标0.06；MAC-10错觉明确0.21久经沙场；其余目标合法最低0。SC耐久与皮肤磨损不关联。
- 审计工具与暂存工具均可复现，临时数据不随Git传输；使用E盘开发临时目录，不写C盘Temp。

复现（输出目录必须未存在，工具拒绝覆盖已有提取）：

```powershell
./tools/dev.ps1 python tools/audit_requested_cs2_finishes.py > .tmp/requested-skins-emerald-only-20260910.json
./tools/dev.ps1 python tools/stage_requested_cs2_finishes.py --audit .tmp/requested-skins-emerald-only-20260910.json --output .tmp/finish33-source-NEW --cli E:/CSMCReverse-Tools/ValveResourceFormat/CLI/bin/Release/Source2Viewer-CLI.exe
```

## 阻塞：严格指定磨损的最终合成输出

不是没找到贴图，而是尚无经验证的最终皮肤合成路径。

1. 本机 `appmanifest_730.acf` 的 UserConfig/MountedConfig 明确 `DisabledDLC=2279721`（CS2 Workshop Tools）。CS2 content目录未发现工具内容；`game/csgo/bin/win64` 仅client/host/matchmaking/server，未找到Workshop的合成编辑器。没有擅改Steam DLC/用户安装设置，也未启动用户CS2会话。
2. 已读本机Source2Viewer CLI帮助和源码：能输出VCOMPMAT/VMAT、解码纹理、导出模型，未提供按wear/seed运行皮肤合成的入口。原配方包含 `COMP_MAT_PROPERTY_MUTATOR_GENERATE_TEXTURE`，这不是复制TexturePattern就能执行的操作。
3. 检查本机VRF Renderer：无`csgo_customweapon`专门合成实现，未知vfx路由到complex；不能用这个通用预览器宣称已复刻磨损与珠光。旧SC烘焙脚本同样明示没有Valve wear/pearlescence compositor，沿用它会违反这轮严格崭新要求。
4. 公开项目 `hexiro/csinspect` 的README指向外部Skinport截图服务，并非可导出枪体各PBR贴图的本地合成实现；不发送用户资源到外部服务，不用他人商品截图代替可用模型材质。

下一步需要用户在Steam启用并安装CS2 Workshop Tools，再验证官方合成编辑器是否能输出指定wear/seed下的最终贴图；安装工具本身不等于输出路径已经成功。如果官方输出仍不满足，需要专门实现/验证合成器及珠光着色器，不能悄悄降低质量标准。
因此本版**没有把这33款加入ScGunSkinCatalog或创造目录，也没有发布冒称完成的普通/计数器皮肤枪**。已完成的属性/命名可以先使用，原11款皮肤保持不变。新增资源仍需后续版本化资源包（如1.1.0），当前不改1.0.0。

## 验证与包

- Full主包 `output/ScCsgoKnives-0.41.13.scmod`，517716字节。
- 主包SHA256：`27596b3d4744e0023532af66897f519d4ff5fbe0044adf5897e13a00dc11fe02`。
- 内置DLL SHA256：`3e992079afa2e323396446fad00195e29d73207b6aaaa114cbff6d8f7c4fce12`。
- 依赖仍为原 `ScCsgoResources-1.0.0.scmod`，逐字节未改，不需要下载新资源包。
- 完整离线检查9508项，0失败。报告 `output/reports/ScCsgoKnives-0.41.13-check.json`。
- 新检查：35准确简名、全部已有原厂/皮肤/计数器模板名称、全部46计数器模板实例化后名称相同、创造/生存所有权候选、同型号不同等级、待升级、最终属性注入测试、替换/跨世界失效、5种纵横比、八项属性/固定返回/滚动、不分配记录、返回只触发一次。
- 公开0.28.2、0.41.4旧档回归继续通过；layout5/schema4/35型号顺序/原皮肤ID不变。没有修改真实世界或游戏设置。
- 未实机操作手机/GPU界面；离线布局和逻辑通过不等于手机截图验收。

工作区原有ScGunAttributes伤害说明、ScGunAttributesScreen计数器文案及ScGunUi布局修改保留，未据此一并提交他人改动；本机包仍来自当前工作树。
