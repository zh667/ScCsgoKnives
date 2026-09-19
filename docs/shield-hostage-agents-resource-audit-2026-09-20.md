# 防爆盾、人质、默认探员与护甲：资源核实

日期：2026-09-20。本轮按用户“先看看到底有没有这些资源”检查，未实现玩法、未修改运行时代码、未生成新包。当前交付仍是 1.3.3。

## 结论

| 内容 | 本机实际找到的资源 | 当前可用程度 |
| --- | --- | --- |
| 防爆盾 | 第一/第三人称盾牌材质、颜色/粗糙度/损坏贴图、7 个中弹音效 | **缺盾牌模型和专用动作**。已提取目录也只有材质，不是完整盾牌资源 |
| 人质 | `models/hostage/hostage.vmdl_c`，四种外观、骨骼、材质，以及跟随回应/痛叫等声音；另有搬运模型 | 模型和贴图成功导出；普通人质 GLB **没有动画**，不能直接宣称已具备走路、持枪、举盾动作 |
| 默认 CT | `agents/models/ctm_sas/ctm_sas.vmdl_c`，SAS | 模型、骨骼、贴图、动画成功导出 |
| 默认 T | `agents/models/tm_phoenix/tm_phoenix.vmdl_c`，凤凰战士 | 模型、骨骼、贴图、动画成功导出 |
| 防弹衣 | `models/weapons/w_eq_armor.vmdl_c`，另有 armor_helmet / assault_suit | 独立装备模型、贴图成功导出；穿戴到角色仍需位置/比例适配 |
| 防弹头盔 | `models/weapons/w_eq_helmet.vmdl_c` | 独立模型、贴图成功导出；不是已经适配 Survivalcraft 头部的可穿戴成品 |

默认 CT/T 的选择还与本机 `scripts/items/items_game.txt` 的默认物品一致：5036 `customplayer_t_map_based` 指向凤凰战士，5037 `customplayer_ct_map_based` 指向 SAS，两者有 `baseitem=1` / `flexible_loadout_default=1`。未将收费皮肤或任意变种当默认。

## 检查范围与证据

主要来源是本机安装的：

`E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo/pak01_dir.vpk`

重新生成了当前 VPK 清单，没有仅凭旧清单判断。同时检查 game 目录下 core、csgo_core、csgo_imported、csgo_lv 和本机六个社区地图 VPK 的模型/动画清单，未找到名称包含 shield/riot/ballistic 的模型或动作。这是本机安装资源范围的结论，不能推论历史 CS:GO、其他安装版本或所有地图都没有盾牌模型。未去下载来源不明的模型替代。

原提取目录中盾牌材质仍在：

`../CSMCReverse/local_cs2_analysis/all_weapons/03_legacy_vmodels_materials/materials/models/weapons/v_models/shield/`

此次使用本机 Source2Viewer-CLI 实际导出五个模型，并逐一解析 GLB、确认外部贴图引用均有对应文件。保留的导出目录：

`.tmp/cs2-companions-audit-20260920/export/`

| 导出模型 | 网格数 | 材质数 | 图片数 | 动画数 |
| --- | ---: | ---: | ---: | ---: |
| CT SAS | 70 | 9 | 36 | 2,062 |
| T 凤凰战士 | 69 | 6 | 26 | 2,062 |
| 人质 | 4 | 9 | 45 | 0 |
| 防弹衣 | 1 | 1 | 4 | 0 |
| 头盔 | 1 | 1 | 4 | 2（参考姿态） |

探员导出包含多个网格/LOD/部件与大量共享动画，**不是 70 套人物，也不是都应该打包**。全动画审计源文件约 1.10GB / 1.02GB，后续只能选择需要的身体网格、材质与少量动作再转换，不能把这个完整审计导出直接装进模组。原始资源保持完整，不据此承诺最终增包大小或手机帧率。

已确认导出动作中有持步枪待机、向前行走、向前奔跑。主资源包另有 `animation/anims/world/shared/sh_hostage_*` 救援相关动作。它们仍需选取、检查骨骼/绑定姿态、重定向或重新编排；人质自己的 86 骨骼蒙皮不能仅凭与某探员骨骼数相近就判定完全兼容。还要处理 root_motion，防止重现小鸡跑动回弹。

机器证据：`companion-resource-audit-20260920.json`，记录清单哈希、模型哈希、引用完整性、动画数与具体资源路径。复查工具：`tools/audit_companion_resources.py`。这些是导出/结构验证，尚未在 Survivalcraft 中加载渲染或验收动画。

导出命令（先用 `tools/dev.ps1` 运行）：

```powershell
./tools/dev.ps1 ./.tmp/vrf-cli/Source2Viewer-CLI.exe `
  -i 'E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo/pak01_dir.vpk' `
  -o .tmp/cs2-companions-audit-20260920/export -d `
  --vpk_filepath 'models/hostage/hostage.vmdl_c,agents/models/ctm_sas/ctm_sas.vmdl_c,agents/models/tm_phoenix/tm_phoenix.vmdl_c,models/weapons/w_eq_armor.vmdl_c,models/weapons/w_eq_helmet.vmdl_c' `
  --gltf_export_format glb --gltf_export_materials --gltf_export_animations
```

## 用户提出的玩法：保留要求与实现边界

以下是后续需求记录，不表示本轮已制作。

1. **防爆盾**：高耐久、抵挡正面伤害、持盾时不能同时用枪。资源还缺盾体与举盾动作；正面方向、盾牌覆盖范围、耐久扣除及破盾处理要自行实现。“防爆盾”名称不能直接解释成全方向爆炸免伤。
2. **人质**：枪械台制作召唤物，召唤后跟随召唤玩家；可装备枪械和弹匣，或装备盾。需要主人身份、寻路、交互装备栏、卸装归还/死亡掉落和存档持久化，不能直接套用小鸡的简单跟随就完成。
3. **持枪人质**：CS 原作人质不是战斗队友。攻击对象选择、敌我识别、射线遮挡、射击/换弹动作、弹匣消耗都需自写。现有枪械还有实例编号、皮肤、计数器、耐久等状态；交给 NPC 必须纳入持有者查重与存档恢复，不能复制枪值后就从玩家背包删掉。
4. **持盾人质**：移动减速，与持枪互斥。要允许玩家躲在后面，盾必须能真正截住射向身后玩家的弹道/攻击；只降低人质自己的受伤数值并不能保护后面的人。CS 射线、原版弹射物、近战、第三方模组的独立伤害逻辑需要分别检查，不能承诺自动防住所有未知模组。
5. **CT/T 探员**：默认资源可用。后续需明确用于玩家外观、同伴外观，还是二者都支持。若替换玩家外观，还涉及第三人称动作、第一人称手臂、现有衣物装备和多人各自选择。
6. **防弹衣/头盔**：保留为以后更新。已有模型、贴图和装备图标，防护部位、耐久、穿戴槽及减伤属于玩法开发，不是资源提取即可实现。

建议后续先做一个默认探员的少量动作适配和跟随验证，确定模型/骨骼可用，再接装备存档和 NPC 射击。盾牌线先补齐真正的模型与举盾动作，再做“能遮住身后玩家”的命中实验。此建议不是额外批准流程，本轮工作范围止于资源核实。
