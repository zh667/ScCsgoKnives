# 已有开源成果调研：哪些能直接用，哪些没人做过

日期：2026-09-05。star 数和推送时间是当天用 GitHub API 查的实数，不是从网页快照抄的。

结论先写在前面：

* **解包/反编译这块，已经有一个几乎是事实标准的工具，应该直接用它。**
* **上皮肤（paint kit 合成）这块，开源界没有人做过。** 能做（有闭源产品在做），
  但没有可抄的参考实现。
* **把 CS 武器带第一人称动画搬进沙盒游戏这件事，也没人做过。**

---

## 一、能直接用的

### ValveResourceFormat / Source2Viewer — ⭐2411，2026-09-03 推送，C#

Source 2 资产的解包、查看、反编译一体工具，本项目已经在用它的产物。
真正有价值的是它的 **CLI 支持按路径前缀批量过滤**，这正好配 `docs/cs2-acquisition-2026-09-05/` 里的清单。

从 `CLI/Decompiler.cs` 的参数签名读出来的可用开关（不是从文档抄的）：

```
--input <pak01_dir.vpk>     --output <dir>      --decompile
--vpk_filepath  a,b,c       逗号分隔；用 filePath.StartsWith(filter) 做前缀匹配
--vpk_list                  只列不解，先干跑一遍核对数量
--vpk_dir                   保留 VPK 内目录结构
--gltf_export_format glb    模型导 GLB
--gltf_export_materials     连材质一起导
--gltf_export_animations    连动画一起导
--gltf_animation_list ...   只导指定动画
--threads N
--texture_decode_flags Auto 贴图解码
--recursive / --recursive_vpk
```

**前缀匹配这点很关键**：不需要把 2237 条路径全列进命令行，给目录前缀就行。
获取请求文档里已经按这个改写了命令。

### SteamTracking/GameTracking-CS2 — ⭐943，2026-08-28 推送

持续跟踪 CS2 每次更新的内容仓库，3 GB。里面有一件对我们有用的东西：

```
game/csgo/shaders_vulkan_dir/shaders/vfx/csgo_weapon.slang   124 KB / 2289 行
game/csgo/shaders_vulkan_dir/shaders/vfx/csgo_weapon_stattrak.slang
```

这是 **CS2 武器渲染着色器**，由 Source 2 Viewer 反构出来的源码（文件头自己写着）。
里面有贴纸的 5 个槽位、`TextureWearMaskSticker*`、`g_flSticker*Wear`、
`g_vWearBias*`、`g_fWearScratches*`、`g_tStickerWepInputs`（R=贴纸遮罩，G=贴纸凹陷）
这些参数的完整定义。

**注意它是"渲染"着色器，不是"合成"着色器**——它画的是已经合成好的皮肤贴图 + 贴纸，
真正把涂装烘到武器上的那一步在别处（`tools/met/` 材质编辑器那条路）。
所以它能省掉贴纸和磨损表现的反推，但省不掉上皮那一步。

这份文件**VPS 上可以直接拉**，不用麻烦 Windows：

```
gh api repos/SteamTracking/GameTracking-CS2/contents/game/csgo/shaders_vulkan_dir/shaders/vfx/csgo_weapon.slang \
   --jq .content | base64 -d > csgo_weapon.slang
```

仓库里**没有**涂装贴图，也**没有** `items_game.txt`（都查过了，0 命中）。

### ByMykel/CSGO-API — ⭐799，2026-09-02 推送

非官方 JSON API，列出全部皮肤/箱子/贴纸/收藏品，多语言名称 + 官方图。
对我们的用处是**命名和中文本地化**——`items_game.txt` 里只有
`#PaintKit_cu_ak47_asiimov` 这种本地化键，要显示"AK-47 | 阿西莫夫"得靠它。
资产本身它没有。

---

## 二、看着相关、其实没用的

| 仓库 | star | 为什么不适用 |
|---|---|---|
| `Nereziel/cs2-WeaponPaints` | ⭐411 | **服务器插件**：改玩家物品属性，让 CS2 自己的客户端去渲染。全程不碰贴图，我们要的是离线烘出来的图 |
| `csfloat/inspect` | ⭐546 | inspect 链接 → 物品数据（float/seed/贴纸）。只有元数据，没有资产 |
| `skullboypl/cs2-weapon-paint-website` | ⭐10 | 上面那个插件的前端 |
| `realBoltDev/inspect3d` | ⭐0 | three.js 看模型，皮肤用现成图片 |
| 各种 `cs2-skin-changer-*` | — | 搜索结果里一大批，全是挂垃圾仓库，不是代码 |

---

## 三、没人做过的（这两件得自己写）

### 1. 离线 paint kit 合成器

搜遍了仓库和代码：VRF 里 `CompositeInputs` **0 命中**，全网代码搜
`cs_weapon_fx` / `composite_inputs` **0 命中**。

最有说服力的证据来自 `skullboypl/cs2-weapon-paint-website` 自己的源码注释：

```js
/**
 * Real CS2 finish styles need VRF-extracted pattern/wear atlases + per-style shaders
 * (see Skinshotter / CS.Money). Until kits exist under /data/paint-kits/, we apply a
 * lightweight approximation on the LielXD UV map:
 * - seed → UV offset (deterministic)
 * - wear → darken / contrast toward metal (approx scratches)
 */
```

一个想做这件事的项目，最后退回到"seed 当 UV 偏移、wear 当整体压暗"的近似，
并且指向 Skinshotter 和 CS.Money —— **两个都是闭源商业产品**（Skinshotter 在
GitHub 上 0 命中）。

那些浏览器里能按 float/seed 实时看皮肤的网站（vskin.gg、csskincrafts、inspect.skin
等）说明**这件事确实做得出来**，但没有一个把实现开源。

所以：合成器要自己写，好在
* 合成器需要的输入（`masks` / `surface` / `pos.exr` / `cavity` / `nopaint`）**VPS 上已经全有**；
* 每款皮肤用哪张图、哪些颜色，写在 `paints/vmats/*.vmat` 里（B2 组，只有 4.7 MB）；
* 我们只要**离线烘一张图**，不需要实时、不需要复刻整个着色器；
* 按风格分档做，`anodized` / `hydrographic` 这些最简单的先做（见获取请求的分批表）。

### 2. 这个移植本身

搜 `counter-strike minecraft mod weapon`、`csgo minecraft skins port` 等，
最相关的是 `Derec-Mods/StatTrak-Mod`（⭐0，2024-03-16），只是个计数器。

Survivalcraft 生态里 star 最高的是 `XiaofengdiZhu/Gigavolt`（⭐11）和
API 模板（⭐11）—— 整个 SC 模组开源生态的规模就是十几个 star 的量级。

**带真实第一人称骨骼动画、CPU 蒙皮手臂、从 vpcf 还原特效的 CS 武器移植，没有先例。**

---

## 四、对现有计划的影响

1. **获取请求不变，但命令简化了**：用 `--vpk_filepath` 前缀过滤，不用列全部路径。
   已写进 `docs/cs2-acquisition-2026-09-05.md`。
2. **`csgo_weapon.slang` 从"请 Windows 顺便导"降级为"VPS 自己拉"**，一条命令的事。
   获取请求里那条可选项已删掉。
3. **皮肤合成没有捷径**。原计划就是"离线烘贴图"，调研没找到能省事的现成实现，
   但也确认了这条路是对的——闭源产品都是这么做的，而且我们的输入已经齐了。
4. **B2 组（涂装 vmat，4.7 MB）的优先级要往上提**。它是合成器的配方表，
   没有它连"这款皮肤该怎么烘"都不知道。原先排在 B1 之前是对的，这里再确认一次。
