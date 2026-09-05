# 09_knives / 10_paints 接收审查

日期：2026-09-05。审查方：VPS。结论：**无硬阻塞，主线可以直接开工。**

## 一、完整性：全部通过

| 项 | 结果 |
|---|---|
| `09_knives/KEY_MANIFEST.sha256` | **2196 / 2196 OK**，exit 0 |
| `10_paints/KEY_MANIFEST.sha256` | **8411 / 8411 OK**，exit 0 |
| A 组原始文件 vs `A1+A2+A3` 清单 | 528 要求，528 实有，**0 缺 0 多** |
| B 全量原始文件 vs `B1+B2+B3+B4` 清单 | 3646 要求，3646 实有，**0 缺 0 多** |
| 磁盘 | 09_knives 1.1 GB + 10_paints 8.7 GB，VPS 剩余 32 GB |

集合比对是把 `raw_compiled/` 递归展开后与清单做**精确集合差**，不是只比数量。

## 二、09_knives 是否满足"22 把刀用 CS2 真实手臂"——满足，而且比预期好

原先的判断是"CS:MC 与 CS2 不是同一副骨架，必须做只借旋转的重定向"。
**那个前提现在作废了**：拿到 CS2 自己的刀动画后，根本不需要 CS:MC 的骨架。

逐条验证：

1. **clip DMX 带合并后的整棵树**。`knife_m9/idle1_m9.dmx` 59 根骨，其中 **44 根手臂骨**；
   AK 的 `idle_ak.dmx` 是 64 根骨、**同样 44 根手臂骨**。差的 5 根是武器自己的骨。
2. **22 把刀逐一比对，手臂骨集合与 AK 的差异为 0**（`knife_push` 例外，见下）。
3. **手臂网格的 48 个承重关节，44 个直接命中**。差的 4 个是
   `arm_lower_{L,R}_TWIST` / `_TWIST1` —— 而 **AK 的 DMX 里同样没有**：
   CS2 用 `AnimConstraintTiltTwist` 驱动它们，mod 早已在
   `Cs2SkinnedMesh.Twist` 里按 weight 0.5 / 1.0 合成。**不是缺口，是同一情况。**
4. **`knife_push` 是超集**：比 AK 多 `weapon_hand_l` / `weapon_hand_r`（推刀双持），不少任何东西。
5. **主骨架 `viewmodel.vnmskel` 不在 09_knives，但已在 `08_first_person`**，
   是同一副共享骨架，`clip-index.json` 里 317 段 clip 全部指向它。不用补导。
6. **22 个 GLB 全部带蒙皮**，且活动件就是靠骨骼驱动的：

```
butterfly  10337 顶点  承重 blade / lock / rear / weapon_offset
flip/navaja/stiletto   承重 blade / weapon_offset
push                   承重 weapon_l / weapon_r（双持）
m9/karambit 等          承重 weapon_offset（单件）
```

`blade` / `lock` / `rear` 正好对上 mod 现有的 `v_weapon_blade1` / `v_weapon_lock` / `v_weapon_rear`。

**所以刀和枪走完全相同的管线，一行重定向都不用写。** 上一轮估的"2–3 天 + 22 把逐把核对
握持"整个作废——握持是 CS2 动画自带的。

## 三、clip-index.json 兼容性：结构兼容，但真正要改的不是它

- 刀的 `clip-index.json` 字段与 `08_first_person/clip-index.json` **完全一致**
  （`path` / `source_dmx` / `primary_skeleton` / `secondary_skeletons` / `event_count`）。
- 但 `cs2_dmx_to_rig.py`、`cs2_viewmodel.py`、`cs2_rig_selftest.py` **都不读 clip-index**，
  管线是直接读 DMX 的。所以"兼容性"这一项实际不成立。

**真正要改的是路径根**：`cs2_viewmodel.py` 里

```python
ANALYSIS = .../all_weapons/08_first_person     # 写死
CLIPS    = ANALYSIS/decompiled/animation/anims/viewmodel
```

要能同时看 `09_knives`。这是 VPS 侧改，符合"不要改 Windows 导出资源"。

## 四、皮肤：B1 已全量到位，规划相应改变

原计划是"只做 anodized_air 17 个跑通管线，其余按需再导"。**B1 全量 1146 个已经在本地**，
所以不再需要分批索要，改为：

1. 先解析 B2 的 1076 个 `.vmat` 配方 + B3 的 1398 个 legacy `.vcompmat`，
   建立 **paint kit → (风格, 图案贴图, 颜色, wear 范围)** 的完整表；
2. 用 `items_game.txt` 的 534 条定义 + 图标路径反推的武器↔皮肤配对，得出哪些组合真实存在；
3. 按风格实现离线合成器，从 `anodized_*`（分区上色，最简单）做起，
   `hydrographic`（UV 直铺）次之，`spray`/`antiqued` 要 `pos.exr` 投影，
   `custom`/`gunsmith` 最后；
4. 烘出来的图按现有 `install_gun_textures_cs2hd.py` 的路子进包，一款约 1 MB。

**这条线不阻塞主线，排在刀之后。**

## 五、需要改的代码

| 文件 | 改动 | 为什么 |
|---|---|---|
| `tools/cs2_viewmodel.py` | 路径根从写死 `08_first_person` 改为可指定/自动在两处查找 | 唯一的硬性兼容改动 |
| `tools/cs2_dmx_to_rig.py` | 加刀的 clip 别名表；`GUNS` 扩展为含 22 把刀 | 生成 `<knife>.cs2.animation.json` |
| `tools/cs2_glb_to_skinned.py` | 复用它把刀的 GLB 也转成蒙皮资产 | 刀是单网格蒙皮，正好走同一条路 |
| `src/.../Cs2Rig.cs` | 允许刀走 CS2 rig（现在 `Cs2Placement.Active` 只放行 `IsGun`） | 开关 |
| `src/.../Cs2SkinnedMesh.cs` | 增加"刀网格"这一路，与手臂共用一次 CPU 蒙皮 | 刀现在也是蒙皮网格 |

## 六、实施顺序

```
1. cs2_viewmodel.py 支持 09_knives           ← 现在做
2. 先出 1 把（m9）的 cs2.animation.json，自检对齐
3. cs2_glb_to_skinned.py 出 m9 的蒙皮资产
4. C# 侧放行刀走 cs2 profile，m9 单把跑通
5. 铺开到 22 把，跑全套自检
6. 皮肤合成器（另起）
```

## 七、是否还需要 Windows 补资源

**不需要。** A4 音效是可选项且现有 CS:MC 音效够用；`viewmodel.vnmskel` 已在
`08_first_person`；`csgo_weapon.slang` VPS 自己能从 GameTracking-CS2 拉。
