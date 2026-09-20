# 战术同伴拓展资源来源

- CT SAS、T Phoenix、人质：用户本机正版 CS2 `game/csgo/pak01_dir.vpk` 经 Source2Viewer 导出的模型、骨架和贴图。仅保留第三人称身体与手套，人质保留 hostage_a；贴图派生至最大 1024，原始导出不变。
- 行走、奔跑、待机、持枪：CS2 world/knife 与 world/rifle 动作。去掉 root_motion 位移，由 Survivalcraft 导航决定世界坐标；人质按同名骨骼适配。并非 CS2 完整 AI、材质或动画系统的移植。
- 防爆盾：本机没有旧版盾牌模型／举盾动作。盾体为本项目编写的适配几何，使用 CS2 保留的 shield_color 材质；第一人称复用本体 CS2 真实手臂和双手持物基础姿态，调整摆放。不宣称原版盾牌模型或原版举盾动画。
- 同伴举盾动作：以 CS2 持枪站立／行走为基础，通过本项目双臂 IK 调整至盾牌握把；不是提取到的原版举盾动作。盾牌中弹声为本机 CS2 `sounds/physics/shield/bullet_hit_shield_01..07`。
- 三种人物均将 GPU 蒙皮骨骼表缩至 48，完整动作骨架仍保留；小辅助骨骼权重合并到祖先骨骼。每种人物检查七段动作共 35 个采样，最大表面偏移分别约 2.1／2.2／1.2 厘米，99% 的采样顶点偏移小于 0.9 厘米。流程见 `tools/compact_tactical_skin.py`。
- 信标图标：本项目绘制。原始与派生文件证据见 `docs/tactical-derived-assets.json`、资源审计文档和 `tools/build_companion_assets.py`。
- 拆弹钳：本项目绘制的 128px 独立图标与平面手持物，生成方法为 `tools/build_tactical_content.py` 的 `defuser()`；不是 CS2 拆弹钳三维模型。敌方复用现有 T Phoenix 身体与持枪动作；安装使用适配蹲姿，没有额外声称移植敌人输密码动画。拆包开始/成功、C4 滴声/警报直接引用本体已提取的 CS2 `c4_disarmstart`、`c4_disarmfinish`、`c4_beep2`、`c4_warning`、`c4_trigger_trip` 音效。
- Valve、Counter-Strike 及相关资源权利属于各自权利人。本项目为非官方粉丝模组，与 Valve 无隶属关系。

代码授权见随包 LICENSE；该代码授权不重新许可第三方游戏素材。
