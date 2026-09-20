# 战术同伴拓展资源来源

- 1.2.0 新增 CT/T 共各 102 段 CS2 world 拔枪／换弹片段，仅导入上半身，采样至约 20 Hz 后由引擎插值。运行时按实际动作进度采样，下半身保持导航步态，根节点不产生位移。检视、射击微动与消音器操作为本项目适配，不是原生 CS2 world 检视片段。弹匣／枪械机件使用本体保留的 CS2 武器动画；人物握持和机件来自不同动作集，不宣称逐帧完全复现。

- 1.1.4 修复发白眼部：T 从源眼球纹理和双眼遮罩烘焙固定前视虹膜／瞳孔；CT 用源镜片底色与 AO 烘焙深色镜片和静态微弱高光。原 Source 2 专用眼球着色器、动态目光和金属反射并未移植。仅替换派生 GLB 内相应图片，所有几何、UV、蒙皮权重和动画 accessor 保持逐项相等；源导出不变。见 tools/bake_agent_face_materials.py。

- CT SAS、T Phoenix、人质：用户本机正版 CS2 `game/csgo/pak01_dir.vpk` 经 Source2Viewer 导出的模型、骨架和贴图。仅保留第三人称身体与手套，人质保留 hostage_a；贴图派生至最大 1024，原始导出不变。
- 行走、奔跑、待机、持枪：CS2 world/knife 与 world/rifle 动作。去掉 root_motion 位移，由 Survivalcraft 导航决定世界坐标；人质按同名骨骼适配。并非 CS2 完整 AI、材质或动画系统的移植。
- 防爆盾：本机没有旧版盾牌模型／举盾动作。盾体为本项目编写的适配几何，使用 CS2 保留的 shield_color 材质；第一人称复用本体 CS2 真实手臂和双手持物基础姿态，调整摆放。不宣称原版盾牌模型或原版举盾动画。
- 同伴举盾动作：以 CS2 持枪站立／行走为基础，通过本项目双臂 IK 调整至盾牌握把；不是提取到的原版举盾动作。盾牌中弹声为本机 CS2 `sounds/physics/shield/bullet_hit_shield_01..07`。
- 三种人物均将 GPU 蒙皮骨骼表缩至 48，完整动作骨架仍保留；小辅助骨骼权重合并到祖先骨骼。1.2.0 CT/T 各检查 109 段动作、545 个采样，人质 7 段、35 个采样。最大表面偏移分别约 3.93／3.55／1.16 厘米，99% 的采样顶点偏移小于 0.72 厘米。流程见 `tools/compact_tactical_skin.py`。
- 信标使用本项目制作的便携无线电三维几何与绘制贴图；CT/T 用蓝/红色调区分，不宣称 Valve 无线电模型。原始与派生文件证据见 `docs/tactical-derived-assets.json`、`docs/tactical-item-assets.json` 和对应工具。
- 拆弹钳：1.1.1 起使用本机 CS2 `weapons/models/defuser/defuser.vmdl_c` 导出的装备包模型（原作就是腰挂工具包外观）。维修包使用 `models/generic/toolkit_01/toolbox_01_closed.vmdl_c` 工具箱。`tools/build_tactical_items.py` 烘焙静态模型和图集，保留源导出；图集保留原来的重复 UV，不将大于 1 的 UV 截到边缘。旧二维源图保留，但不再作为这两项的手持模型。
- 人质的模型根节点增加中性 Root，将轴转换置于其下，避免游戏 180° 朝向修正将身体倒置入地。三类人物死亡时保存最后活体姿态，按受击方向倒地，放松双臂并弯曲颈部；这是本项目骨骼适配，不是提取的 CS2 死亡片段，也不是具有地形碰撞的完整布娃娃模拟。
- 敌方复用现有 T Phoenix 身体与持枪动作；安装使用适配蹲姿，没有额外声称移植敌人输密码动画。拆包开始/成功、C4 滴声/警报直接引用本体已提取的 CS2 `c4_disarmstart`、`c4_disarmfinish`、`c4_beep2`、`c4_warning`、`c4_trigger_trip` 音效。
- Valve、Counter-Strike 及相关资源权利属于各自权利人。本项目为非官方粉丝模组，与 Valve 无隶属关系。

代码授权见随包 LICENSE；该代码授权不重新许可第三方游戏素材。
