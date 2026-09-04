# CS2 资源获取请求（给 Windows agent）

日期：2026-09-05。提出方：VPS 侧。
用途：两件事——**给 22 把刀换 CS2 的真实手臂/动画**，以及**做武器皮肤**。

不赶时间，**求全不求快**。可以分批、可以断点续传、可以隔几天做一批。
每一批做完只要更新 `KEY_MANIFEST.sha256` 和 `FILE_INVENTORY.csv` 就行。

按 `pak01_dir-vpk-list.txt`（135686 行，你上次导出时留下的完整清单）逐条核对过，
下面每一组都附了**可直接喂给解包器的路径清单文件**，不需要你再去猜路径。

---

## 零、先说已经有的，别重复劳动

VPS 上现有的 `~/workspaces/CSMCReverse/local_cs2_analysis/` 里已经包含：

| 已有 | 位置 |
|---|---|
| 三把枪（AK/M4A1-S/AWP）全套第一人称 | `all_weapons/08_first_person/`（377 条路径） |
| 共享手臂 + 手套模型/贴图 | `08_first_person/glb/weapons/models/shared/arms/` |
| 全部枪的原皮 PBR 材质 | `all_weapons/04_current_weapon_materials/`（1109 文件） |
| 枪的合成输入（cavity/masks/surface/pos） | 同上，`composite_inputs/` |
| 粒子系统定义 + 粒子贴图 | `all_weapons/06_particles/` |
| `items_game.txt`（含 534 条涂装定义） | `all_weapons/01_weapon_data/source/` |
| `weapons.vdata`（88 条，**已含 `weapon_knife`**） | 同上 |

所以**刀的数值不用再导**，`weapon_knife` 已经在 `weapons.vdata` 里。

---

## 一、A 组：刀的资源（优先，先做这组）

这组做完，22 把刀就能走和三把枪完全相同的管线：CS2 网格 + CS2 动画 + CS2 手臂，
不需要做任何骨架重定向。

VPK 里 22 把刀**一把不少**，和 mod 现有的 22 把逐一对得上
（bayonet, bowie, butterfly, canis, cord, css, default_ct, default_t, falchion,
flip, gut, karambit, kukri, m9, navaja, outdoor, push, skeleton, stiletto,
tactical, talon, ursus）。

一处已核实过、免得你白找：**`default_ct` 没有自己的 clip 目录**，它的模型、材质、
骨架、动画图都在（已列进 A1/A3），但第一人称动作走共享的 `_default_knife/`（13 段），
这也是它的图叫 `viewmodel_knife.vnmgraph+default_ct` 的原因。A2 清单里已经把
`_default_knife/` 包含在内，不是遗漏。

另外 `animation/anims/world/knife/**`（366 条）是**第三人称世界模型**动画，
第一人称用不到，**故意没列**。`animation/graphs/worldmodel/**` 同理。

| 组 | 内容 | 条数 | 压缩体积 | 路径清单 |
|---|---|---|---|---|
| **A1** | 刀的模型 `.vmdl_c` + 材质 `.vmat_c` + 合成输入贴图 | 145 | 90.7 MB | `docs/cs2-acquisition-2026-09-05/A1-knife-models-materials.txt` |
| **A2** | 刀的第一人称动画 `.vnmclip_c`（含 `_default_knife` 共享段） | 317 | 5.8 MB | `A2-knife-viewmodel-anims.txt` |
| **A3** | 刀的动画图 `.vnmgraph_c`（44）+ 骨架 `.vnmskel_c`（22） | 66 | 0.3 MB | `A3-knife-graphs-skeletons.txt` |
| **A4** | 刀的音效（**可选**，mod 现在用 CS:MC 的音效，够用） | 460 | 10.4 MB | `A4-knife-sounds.txt` |

**A1+A2+A3 合计约 97 MB，这是最要紧的一组。**

### 处理方式：和上次导枪完全一样

```
raw_compiled/     原始 _c 文件，原样保留
decompiled/       vnmclip / vnmskel / vmat 反编译成 KV3 文本
glb/              vmdl 转 GLB（含蒙皮权重和逆绑定矩阵）
                  vtex 转 PNG（不要 JPEG，不要有损）
```

上次 `08_first_person/` 就是这个结构，照抄即可。另外请一并生成
`clip-index.json`（每段 clip 的路径 + 源 DMX + 主骨架），VPS 侧的
`tools/cs2_dmx_to_rig.py` 直接吃它。

### 需要特别确认的两件事

1. **蒙皮权重必须在 GLB 里**（`JOINTS_0` / `WEIGHTS_0` 和 `inverseBindMatrices`）。
   上次枪的手臂 GLB 是带的，刀的模型本身没蒙皮无所谓，但**如果导出器有"烘掉骨骼"
   的选项，请关掉**。
2. **`_default_knife` 目录别漏**。多把刀共用它，缺了就少一批 clip。

---

## 二、B 组：武器皮肤（数据量大，可以慢慢来）

### 背景：现在缺的到底是什么

原皮的 PBR 全套、合成器要的 `cavity` / `masks` / `surface` / `pos.exr` 都已经有了。
`items_game.txt` 里也有全部 **534 条涂装定义**（名字、style、`wear_default`、
`wear_remap_min/max`、`seed`）。

**唯独缺图案贴图本身**——`materials/models/weapons/customization/paints/**` 一个都没导。

CS2 的皮肤是运行时合成的（`csgo_weapon.vfx`：图案贴图 + `masks` 分区上色 +
`pos.exr` 投影 + `cavity` 磨损 + wear + seed）。mod 不打算复刻这个着色器，而是
**离线烘成 base color 再随包发布**——合成器的其它输入我们都有，只差图案图。

| 组 | 内容 | 条数 | 压缩体积 | 路径清单 |
|---|---|---|---|---|
| **B1** | 涂装图案贴图 | 1146 | **2274.0 MB** | `B1-paints-textures.txt` |
| **B2** | 涂装 `.vmat` 定义（说明每款皮肤用哪张图、哪些颜色） | 1091 | 4.7 MB | `B2-paints-vmats.txt` |
| **B3** | legacy 涂装定义（`use_legacy_model` 的那 373 款要用） | 1398 | 3.9 MB | `B3-paints-legacy.txt` |
| **B4** | 贴纸 / 闪粉 / 全息共享贴图 | 11 | 0.9 MB | `B4-shared-stickers.txt` |

**B2 + B3 + B4 只有 9.5 MB，请先做这三个**——有了它们，VPS 侧就能算出
"哪款皮肤用哪张图案图"，之后 B1 可以只取需要的那部分，不必全量 2.27 GB。

### B1 建议的分批顺序

按风格目录分，从小到大、从易到难：

| 批次 | 目录 | 个数 | 体积 | 烘焙难度 |
|---|---|---|---|---|
| 1 | `anodized_air/` | 17 | 12.1 MB | 低（分区上色为主） |
| 2 | `spray/` | 56 | 47.2 MB | 中（要 `pos.exr` 投影） |
| 3 | `anodized_multi/` | 90 | 67.6 MB | 低 |
| 4 | `hydrographic/` | 95 | 73.1 MB | 低（按 UV 直铺） |
| 5 | `antiqued/` | 102 | 165.6 MB | 中 |
| 6 | `custom/` | 437 | 1057.7 MB | 高（一款只对一把枪） |
| 7 | `gunsmith/` | 348 | 850.7 MB | 高 |

**做完第 1 批就可以先给 VPS 试通管线**，不用等全部。

### 顺带一个可选项

如果解包器能导出着色器，请把 `csgo_weapon.vfx` 的反编译结果也带一份
（放 `shader_dump/` 下）。不是必需——合成规则可以从 vmat 参数反推——但有它能
省掉一轮试错。现有的 `shader-resource-list.txt` 里没有它。

---

## 三、交付要求

1. **无损**。贴图一律 PNG 或 EXR，不要 JPEG，不要缩尺寸。上次枪的导出就是这么做的。
2. **保留原始 `_c`**。反编译/转换可能有 bug，原始文件是唯一的兜底。
3. **每批附 SHA-256**。沿用 `KEY_MANIFEST.sha256` 的格式，追加即可。
4. **附 `FILE_INVENTORY.csv`**：`vpk_path, output_path, bytes, sha256`。
   VPS 侧要靠它核对有没有漏，以及后续引用哪个文件。
5. **不要改 CS2 游戏文件**（原有硬规则，仍然有效）。

## 四、放哪

沿用现有布局，新增两个编号目录：

```
~/workspaces/CSMCReverse/local_cs2_analysis/all_weapons/
    09_knives/          <- A 组
        raw_compiled/  decompiled/  glb/
        clip-index.json  FILE_INVENTORY.csv  KEY_MANIFEST.sha256
    10_paints/          <- B 组
        textures/      <- B1，按风格目录分子目录
        vmats/         <- B2
        legacy/        <- B3
        stickers/      <- B4
        FILE_INVENTORY.csv  KEY_MANIFEST.sha256
```

因为走 Syncthing 同步，**B1 全量 2.27 GB 会占同步带宽和 VPS 磁盘**。
如果不想同步全量，B1 可以先只放需要的批次；VPS 这边会先用 B2/B3 算出需求清单再要。

---

## 五、优先级一句话总结

```
1. A1 + A2 + A3        约 97 MB    刀能换真实手臂和 CS2 动画，这是主线
2. B2 + B3 + B4        约 9.5 MB   有了它们才能算出皮肤要哪些图，很小，顺手做
3. B1 第 1 批           12 MB      跑通皮肤烘焙管线
4. B1 其余             按批来      看后面到底要做哪几款
5. A4                  10.4 MB    可选，现在的 CS:MC 音效够用
```
