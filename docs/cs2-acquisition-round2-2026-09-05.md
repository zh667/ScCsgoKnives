# CS2 资源获取请求 · 第二轮（给 Windows agent）

日期：2026-09-05。提出方：VPS 侧。上一轮是 `docs/cs2-acquisition-2026-09-05.md`（刀 + 涂装，已交付）。

这轮的起因是三件"0.18.2 没做完的事"，外加一次全面核对：**把 CS2 剩下的 24 把枪全部加进来，
还缺什么**。结论先写在前面：

| 事项 | 要 Windows 做什么 |
| --- | --- |
| 手枪空仓（套筒后挂 / 空仓换弹） | **不需要。** 片段（`shoot_empty / idle_slide_back / reload_empty`）、事件时序、音频都已在本地，是 VPS 侧代码活。 |
| 8 把新枪的枪口火焰 / 曳光参数 | **不需要。** 每把枪的曳光 `.vpcf` 由 vdata 点名、火焰系统由 `items_game.txt` 的 `muzzle_flash_effect_1st_person` 点名，两者本地都有（下表）。唯一引用了却不存在的贴图 `particle_ring_wave_8` 在 VPK 里根本没有（只有 `particle_ring_wave`、`_2`、`_12`），CS2 自己也是缺的，无从导出。 |
| CS2 原版物品栏图标 68 张 | **要，C1。** 这是本轮唯一必做项。 |
| 剩下 24 把枪 | 资源**全部在本地**，逐项核对见第二节；只差 Taser 的两条充能音（C2，可选）。 |
| 0.18.2 实机回归 | **要，第三节。** 这次的修复是"8 把枪从来没走到 CS2 绘制路径"，只有实机能证明它走到了。 |

不赶时间，求全不求快。做完更新 `KEY_MANIFEST.sha256` 和 `FILE_INVENTORY.csv`。

## 一、要导出的

### C1 物品栏图标（必做）—— 68 个文件，9.3 MB

`panorama/images/econ/weapons/base_weapons/*_png.vtex_c`。CS2 自己的武器渲染图：34 把枪、
21 把刀（含 `weapon_knife` / `weapon_knife_t` 两把默认刀）、2 双手套、C4 和投掷物。
mod 里现在 ak47 / m4a1s / awp 用的是 CS:MC 的图（很可能就是从这套图缩的），
其余 8 把是 VPS 用网格渲染后再对着那三张拟合角度和明暗的；有了原图就都换成原图，
风格问题彻底消失。

路径清单 `docs/cs2-acquisition-round2-2026-09-05/C1-base-weapon-icons.txt`（68 行，
从 `pak01_dir-vpk-list.txt` 直接摘的）。

第一步干跑核对条数（`--vpk_list` 不能和 `--output` 同用）：

```
Source2Viewer-CLI --input "<CS2>/game/csgo/pak01_dir.vpk" --vpk_list ^
  --vpk_filepath "panorama/images/econ/weapons/base_weapons/"
```

应当输出 **68** 条。第二步导出：

```
Source2Viewer-CLI --input "<CS2>/game/csgo/pak01_dir.vpk" --output 11_icons ^
  --decompile --threads 8 ^
  --vpk_filepath "panorama/images/econ/weapons/base_weapons/"
```

`--decompile` 会把 `vtex_c` 解成 PNG（**要 PNG，不要 JPEG**，保留原尺寸和 alpha，
图标的透明背景就是 alpha）。同时保留 `raw_compiled/` 里的原始 `_c`。

### C2 Taser 充能音（可选）—— 3 个文件，42 KB

`Weapon_Taser.ChargeNotReady` 和 `Weapon_Taser.ChargeReady_Zap` 两个事件引用
`sounds/ambient/common/energy/zap1..3.vsnd`，`05_audio/decoded/` 里没有解码它们
（其余 35 把枪的 2202 个音频都已解码）。清单 `C2-taser-zap-sounds.txt`。解成 WAV 放到
`05_audio/decoded/sounds/ambient/common/energy/`，并在 `weapon-soundevent-mapping.json`
里把这两条的 `decoded_files` 补上。Taser 排在最后做，所以这条不急。

### 放哪

```
~/workspaces/CSMCReverse/local_cs2_analysis/all_weapons/
    11_icons/
        raw_compiled/panorama/images/econ/weapons/base_weapons/*.vtex_c
        panorama/images/econ/weapons/base_weapons/*.png          <- 解码结果
        FILE_INVENTORY.csv   KEY_MANIFEST.sha256   ACQUISITION_REPORT.md
    05_audio/decoded/sounds/ambient/common/energy/zap{1,2,3}.wav   <- C2
```

交付要求同上一轮：无损、保留原始 `_c`、每批附 SHA-256 与 `FILE_INVENTORY.csv`
（`vpk_path, output_path, bytes, sha256`）、**不改 CS2 游戏文件**。

## 二、剩下 24 把枪：逐项核对（2026-09-05，在 VPS 上对本地导出跑的）

35 把枪 = 已上线 11（ak47, m4a1s, awp, deagle, glock18, usp_silencer, m4a4, famas, mp9, p90,
ssg08）+ 剩余 24（cz75a, elite, fiveseven, hkp2000, p250, revolver, taser, tec9, aug, bizon,
g3sg1, galilar, m249, mac10, mag7, mp5sd, mp7, negev, nova, sawedoff, scar20, sg556, ump45,
xm1014）。

| 类别 | 核对方法 | 结果 |
| --- | --- | --- |
| 第一人称动画 | `tools/cs2_gun_rig.py --all --dry-run`，每把枪 deploy/idle/shoot1/inspect 必须齐 | **35/35 齐**。特殊机制的片段也在：左轮 `prepare_shoot / shoot_alt1 / chamber_position_anim_0..7 / dryfire`，双持 `shoot_left1 / shoot_leftlast / shoot_rightlast / idle_leftempty / idle_leftrightempty`，M249/Negev `bullet_hide`，Negev `empty_reload`，手枪 `shoot_empty / idle_slide_back / reload_empty`（cz75a, deagle, fiveseven, glock, hkp2000, p250, scar20, tec9, usp），AUG/SG `ironsight_shoot / ironsight_fidget`，CZ75 `reload2` |
| 动画图 `.vnmgraph` | `08_first_person/` 里 152 个（raw + 反编译），VPK 里枪的 viewmodel 图 89 个 | 齐 |
| 模型 GLB（带蒙皮） | 逐个打开 `02_models/glb_with_animations/weapons/models/<gun>/weapon_*.glb` 看网格名 | **35/35 有 `body_hd`**（Taser 只有 hd 没 legacy，正常；elite 另带 `eholster`） |
| 材质 | `04_current_weapon_materials/weapons/models/<gun>/materials/*.vmat` | 每把 2 个（本体 + 弹匣），AUG/SG556 多一个镜片 `_lens`，Taser 多一个电量表 |
| 玩法数值 `.vdata` | `01_weapon_data/firearm_blocks/weapon_<gun>.vdata` | 35/35 |
| 开火音 | vdata `WEAPON_SOUND_SINGLE` → `shoot-event-mapping.json` 有解码文件 | 35/35 |
| 动作提示音 | 片段事件（`sound-event-timings.csv`）→ `weapon-soundevent-mapping.json` 有解码文件 | 全部可解析。有 24 条只是大小写不同（`Weapon_p250.Clipin` vs `Weapon_P250.Clipin`），VPS 侧已把查找改成不区分大小写；CZ75 的换弹音事件叫 `Weapon_CZ.*`，也在。**只缺 Taser 的两条 zap（C2）** |
| 曳光 | vdata `m_szTracerParticle` → `06_particles/definitions/` | 35/35（pistol / smg / assrifle / rifle / rifle_ssg / rifle_scar / mach / shot / taser 系列都在） |
| 枪口火焰 | `items_game.txt` 每把枪的 `muzzle_flash_effect_1st_person` → `particles/weapons/cs_weapon_fx/weapon_muzzle_flash_*.vpcf` 和 `unified_weapon_fx/` 的子系统 | 容器和子系统都在本地；火焰贴图 `fire_gas_batch_b_top`、`fire_small_sim_b_top_mv` 也在 |
| 瞄准镜 | AWP/SSG08/G3SG1/SCAR20 用 `07_scope/panorama/images/hud/scope/`；AUG/SG556 用镜片材质 + `07_scope/materials/models/weapons/shared/scope/`（scope_dot、lens_dirt、scope.vmat） | 齐 |
| 手臂 / 手套 | `08_first_person/glb/weapons/models/shared/arms/` | 齐（已上线） |
| 图标 | `panorama/images/econ/weapons/base_weapons/` | **未导出 → C1** |

所以剩下 24 把枪的资源，Windows 只需要 C1（顺手 C2）。其余全是 VPS 侧的活：
每把枪的 rig 生成、`.cs2.parts`、贴图安装、GunSpec 行、提示音安装（`--cues`）、
自检项；以及几类新机制——霰弹枪逐发装填、左轮的双动、双持、M249/Negev 弹链、
Taser 单发无换弹、AUG/SG556 镜片瞄准。

## 三、0.18.2 实机回归（要做，并把结果送回）

包：`output/ScCsgoKnives-0.18.2.scmod`，SHA-256
`50d2110c5101c44955a160df4c09dfb1633c946126739c8b20cf583007aa975f`。
背景与修复说明见 `docs/cs2-0.18.2-response-2026-09-05.md`。

1. 换到 SSG08、P90、USP-S 各一次：应有 CS2 手臂和拔枪动画。`Game.log` 里应出现
   `cs2 profile active: gun=ssg08`（每把枪第一次画时一行），启动时**不应再有**
   `failed to load … Missing embedded CSMC rig resource`。
2. R 换弹：动画播放、听到 clipout / clipin / 拉栓；G 检视：动画播放。
3. SSG08 右键开镜：遮罩立刻出现、原生大"+"消失；两级放大；开镜/关镜各有声音。
4. USP-S：右键（瞄准键）装/卸消音器有螺纹音；装上后开火是消音音。
5. 物品栏：8 个新图标应与 ak47 / m4a1s / awp 同一角度、同一明暗（C1 到位后会整体换掉）。
6. 顺手看一眼三把老枪没坏：AK 拔枪时现在是按 CS2 片段的提示音播的（`movement3` →
   `ak47_draw` → `boltpull`），不应重叠或缺失。
7. 把 `Game.log` 放回 `E:\EdgeDownload\[Windows]SurvivalcraftAPI_1.9.2.1\Bugs\`，
   视频放回 `E:\Obsidian Document\Document1\ScCsgoKnives\SC_VIDEO\`，回复文档写到
   `docs/cs2-0.18.2-windows-review-2026-09-05.md`（或按日期顺延）。
