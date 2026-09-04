# CS2 资源获取请求（给 Windows agent）

日期：2026-09-05。提出方：VPS 侧。
用途：两件事——**给 22 把刀换 CS2 的真实手臂/动画**，以及**做武器皮肤**。

不赶时间，**求全不求快**。可以分批、可以断点续传、可以隔几天做一批。
每一批做完只要更新 `KEY_MANIFEST.sha256` 和 `FILE_INVENTORY.csv` 就行。

按 `pak01_dir-vpk-list.txt`（135686 行，你上次导出时留下的完整清单）逐条核对过，
下面每一组都给了**已在 VPS 上复算验证过的路径前缀**，直接粘进 `--vpk_filepath` 就行，
不需要你再去猜路径。

## 用什么工具

[Source2Viewer CLI](https://github.com/ValveResourceFormat/ValveResourceFormat)（⭐2411，
2026-09-03 推送）。你上次导枪应该就是用的它。这次的命令我从它的源码
`CLI/Decompiler.cs` 里核过参数，有两个坑写在下面对应位置：
`--vpk_dir` 不是"保留目录结构"，`--vpk_list` 不能和 `--output` 同用。

配套两个文件：

```
docs/cs2-acquisition-2026-09-05/PREFIXES.txt   每批的前缀 + 应得的文件数和体积，可直接粘贴
docs/cs2-acquisition-2026-09-05/A*.txt B*.txt  逐条路径清单，出问题时用来核对是哪几条漏了
```

另有一份调研 `docs/cs2-prior-art-2026-09-05.md`，结论影响到本文两处：
着色器你不用导了（VPS 自己能拉），以及 B2 组的优先级要提前。

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

### 取件命令

用 [Source2Viewer CLI](https://github.com/ValveResourceFormat/ValveResourceFormat)（⭐2411，
2026-09-03 推送）。`--vpk_filepath` 是**前缀匹配、大小写敏感**
（源码 `CLI/Decompiler.cs`:1541，`filePath.StartsWith(filter, StringComparison.Ordinal)`），
所以给目录前缀就够，不用把路径一条条列上去。

**第一步，干跑核对条数。`--vpk_list` 不能和 `--output` 同用**（源码 251 行会直接拒绝），
所以这是单独一条命令：

```
Source2Viewer-CLI --input "<CS2>/game/csgo/pak01_dir.vpk" --vpk_list ^
  --vpk_filepath "weapons/models/knife/,animation/anims/viewmodel/knife/,animation/graphs/viewmodel/viewmodel_knife,animation/graphs/viewmodel/viewmodel_inspects.vnmgraph+knife,animation/skeletons/weapons/knife"
```

应当输出 **528** 条（145 模型材质 + 317 动画 + 66 图和骨架）。对不上再回来看清单文件。

**第二步，真正导出**：

```
Source2Viewer-CLI --input "<CS2>/game/csgo/pak01_dir.vpk" --output 09_knives ^
  --decompile --threads 8 ^
  --gltf_export_format glb --gltf_export_materials --gltf_export_animations ^
  --vpk_filepath "weapons/models/knife/,animation/anims/viewmodel/knife/,animation/graphs/viewmodel/viewmodel_knife,animation/graphs/viewmodel/viewmodel_inspects.vnmgraph+knife,animation/skeletons/weapons/knife"
```

输出是目录时会自动按 VPK 内的路径展开，不需要额外开关
（`--vpk_dir` 是**把目录清单打到控制台**，不是保留结构，别加）。

**前缀在 VPS 上按 VRF 的过滤逻辑逐条复算过：选中 528 条，与清单 0 漏 0 多。**
全部批次的前缀在 `docs/cs2-acquisition-2026-09-05/PREFIXES.txt`，可直接粘贴。

`A1`/`A2`/`A3` 三个清单文件留作**逐条核对**用，不必喂给命令行。

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

调研过了（`docs/cs2-prior-art-2026-09-05.md`）：**离线上皮这件事开源界没人做过**，
合成器得我们自己写。所以 B2 里那些 `.vmat` 不只是索引，它们是**唯一的配方来源**——
每款皮肤用哪张图案、哪几个颜色、怎么和 `masks` 分区对应，只写在里面。优先级最高。

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

### 取件命令

先做配方（B2+B3+B4，共 **2500 个文件、9.5 MB**）：

```
Source2Viewer-CLI --input "<CS2>/game/csgo/pak01_dir.vpk" --output 10_paints ^
  --decompile --threads 8 ^
  --vpk_filepath "materials/models/weapons/customization/paints/vmats/,weapons/paints/,materials/default/stickers/"
```

同样在 VPS 上复算过：**选中 2500 条，与清单 0 漏 0 多**。

B1 按批加前缀，每批一条命令，例如第 1 批：

```
  --vpk_filepath "materials/models/weapons/customization/paints/anodized_air/"
```

七个批次的前缀和各自的文件数/体积都在 `PREFIXES.txt` 里，逐批核对：

```
B1-1 anodized_air     17 个   12.1 MB
B1-2 spray            56 个   47.2 MB
B1-3 anodized_multi   90 个   67.6 MB
B1-4 hydrographic     95 个   73.1 MB
B1-5 antiqued        102 个  165.6 MB
B1-6 custom          437 个 1057.7 MB
B1-7 gunsmith        348 个  850.7 MB
B1-x shared            1 个    0.0 MB
                    ----------------
                    1146 个 2274.0 MB
```

### 着色器不用你导了

`csgo_weapon.vfx` 已经在 `SteamTracking/GameTracking-CS2`（⭐943，持续更新）里，
VPS 侧一条命令就能拉，不劳你动手：

```
game/csgo/shaders_vulkan_dir/shaders/vfx/csgo_weapon.slang   124 KB / 2289 行
```

详见 `docs/cs2-prior-art-2026-09-05.md`。

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
1. A  (528 个文件)      96.7 MB    刀能换真实手臂和 CS2 动画，这是主线
2. B234 (2500 个)        9.5 MB    上皮的配方表，没它写不了合成器；很小，顺手做
3. B1-1 anodized_air     12.1 MB   17 个文件，跑通烘焙管线用
4. B1-2 .. B1-7         按批来     看后面到底要做哪几款
5. A4 (460 个)           10.4 MB   可选，现在的 CS:MC 音效够用
```

每批的前缀直接从 `docs/cs2-acquisition-2026-09-05/PREFIXES.txt` 复制。
