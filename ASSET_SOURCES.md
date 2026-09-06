# Asset sources

## Current package: 0.20.4

All 22 knives and 35 guns now use the CS2 animation / mesh pipeline, including CS2 skinned arms and gloves. Source exports are available in the sibling `CSMCReverse/local_cs2_analysis/all_weapons` directory (guns in `08_first_person`, knives in `09_knives`, meshes/materials in their existing export folders). This extraction remains intact.

The runtime uses `*.cs2.animation.json`, `*.cs2.skin`, `*.cs2.parts`, the first three guns' `*_cs2_*.obj`, CS2 `_hd` / `_cs2` texture sets, and the existing slot icons. AUG and SG553 retain their existing CS2 `body_hd` models; the 0.20.4 scope change adjusts projection and the optical aperture, not the source mesh.

The retired CS:MC animation files, weapon OBJ records and their unused texture maps were removed after migrating the inventory/world meshes. `docs/cs2-0.20.4-removed-assets.json` lists all 158 removed files with hashes and the backup commit. Shared PBR environment/LUT textures, audio/effects and slot icons remain because the active renderer still uses them. Their existing attributions remain applicable.

The sections below are the historical conversion record, not the current package layout.

## Historical CS:MC conversion

The first-person geometry, base-colour textures, skeletal animations, and
inventory icons are converted from the CSMC client resources
(`CSMCClient20260822.zip`, `overrides/gec_texture_stream/`). Only the default
factory finishes are used; no weapon skins are included. The knife sounds
remain from `[TaCZ X LR] CS2 Knifes Packet v1.0.1`, CurseForge file `6635636`,
by `White_Food`. Their use in this Survivalcraft port is based on the
permissions recorded in `THIRD_PARTY_NOTICES.md`.

Every knife installs as `Models/ScCsgoKnives/<name>_<record>.obj` (one OBJ per
rigid mesh record, so folding knives keep their moving parts),
`Textures/ScCsgoKnives/<name>.png`, `Textures/ScCsgoKnives/<name>_slot.png`,
and `AnimationData/<name>.csmc.animation.json`.

| Variant | Name | CSMC mesh + animation | CSMC base colour | CSMC icon |
|---|---|---|---|---|
| 0 | `karambit` | `knife_karambit/*.meshbin` + `weapon_knife_karambit.animbin` | `tex/source2_vmat/weapon_knife_karambit/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_karambit.webp` |
| 1 | `m9` | `knife_m9/*.meshbin` + `weapon_knife_m9.animbin` | `tex/source2_vmat/weapon_knife_m9/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_m9_bayonet.webp` |
| 2 | `butterfly` | `knife_butterfly/*.meshbin` + `weapon_knife_butterfly.animbin` | `tex/source2_vmat/weapon_knife_butterfly/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_butterfly.webp` |
| 3 | `bayonet` | `knife_bayonet/*.meshbin` + `weapon_knife_bayonet.animbin` | `tex/source2_vmat/weapon_knife_bayonet/*_color_*.webp` | `icons_128/base_weapons/weapon_bayonet.webp` |
| 4 | `bowie` | `knife_bowie/*.meshbin` + `weapon_knife_bowie.animbin` | `tex/source2_vmat/weapon_knife_bowie/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_survival_bowie.webp` |
| 5 | `canis` | `knife_canis/*.meshbin` + `weapon_knife_canis.animbin` | `tex/source2_vmat/weapon_knife_canis/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_canis.webp` |
| 6 | `cord` | `knife_cord/*.meshbin` + `weapon_knife_cord.animbin` | `tex/source2_vmat/weapon_knife_cord/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_cord.webp` |
| 7 | `css` | `knife_css/*.meshbin` + `weapon_knife_css.animbin` | `tex/source2_vmat/weapon_knife_css/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_css.webp` |
| 8 | `default_ct` | `knife_default_ct/*.meshbin` + `weapon_knife_default_ct.animbin` | `tex/source2_vmat/weapon_knife_default_ct/*_color_*.webp` | `icons_128/base_weapons/weapon_knife.webp` |
| 9 | `default_t` | `knife_default_t/*.meshbin` + `weapon_knife_default_t.animbin` | `tex/source2_vmat/weapon_knife_default_t/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_t.webp` |
| 10 | `falchion` | `knife_falchion/*.meshbin` + `weapon_knife_falchion.animbin` | `tex/source2_vmat/weapon_knife_falchion/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_falchion.webp` |
| 11 | `flip` | `knife_flip/*.meshbin` + `weapon_knife_flip.animbin` | `tex/source2_vmat/weapon_knife_flip/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_flip.webp` |
| 12 | `gut` | `knife_gut/*.meshbin` + `weapon_knife_gut.animbin` | `tex/source2_vmat/weapon_knife_gut/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_gut.webp` |
| 13 | `kukri` | `knife_kukri/*.meshbin` + `weapon_knife_kukri.animbin` | `tex/source2_vmat/weapon_knife_kukri/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_kukri.webp` |
| 14 | `navaja` | `knife_navaja/*.meshbin` + `weapon_knife_navaja.animbin` | `tex/source2_vmat/weapon_knife_navaja/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_gypsy_jackknife.webp` |
| 15 | `outdoor` | `knife_outdoor/*.meshbin` + `weapon_knife_outdoor.animbin` | `tex/source2_vmat/weapon_knife_outdoor/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_outdoor.webp` |
| 16 | `push` | `knife_push/*.meshbin` + `weapon_knife_push.animbin` | `tex/source2_vmat/weapon_knife_push/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_push.webp` |
| 17 | `skeleton` | `knife_skeleton/*.meshbin` + `weapon_knife_skeleton.animbin` | `tex/source2_vmat/weapon_knife_skeleton/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_skeleton.webp` |
| 18 | `stiletto` | `knife_stiletto/*.meshbin` + `weapon_knife_stiletto.animbin` | `tex/source2_vmat/weapon_knife_stiletto/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_stiletto.webp` |
| 19 | `tactical` | `knife_tactical/*.meshbin` + `weapon_knife_tactical.animbin` | `tex/source2_vmat/weapon_knife_tactical/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_tactical.webp` |
| 20 | `talon` | `knife_talon/*.meshbin` + `weapon_knife_talon.animbin` | `tex/source2_vmat/weapon_knife_talon/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_widowmaker.webp` |
| 21 | `ursus` | `knife_ursus/*.meshbin` + `weapon_knife_ursus.animbin` | `tex/source2_vmat/weapon_knife_ursus/*_color_*.webp` | `icons_128/base_weapons/weapon_knife_ursus.webp` |

| ScCsgoKnives asset | Upstream asset |
|---|---|
| `Audio/ScCsgoKnives/knife_deploy.ogg` | `tacz_sounds/melee/knife/knife_deploy1.ogg` |
| `Audio/ScCsgoKnives/knife_slash.ogg` | `tacz_sounds/melee/knife/knife_slash1.ogg` |
| `Audio/ScCsgoKnives/butterfly_draw.ogg` | `tacz_sounds/melee/knife/bknife_draw01.ogg` |
| `Audio/ScCsgoKnives/butterfly_inspect.ogg` | `tacz_sounds/melee/knife/bknife_look01_ab.ogg` |

## Conversion

`tools/CsmcAssetConverter` reads the binary records and preserves the animation
curves, parent hierarchy, and mesh-record-to-bone names, then changes the model
basis to Survivalcraft's item coordinates. `tools/install_knives.py` drives it
for every knife: it shares vertices and trims float precision
(`tools/optimize_obj.py`), drops animation clips the mod never plays, resizes
the base colour to 512x512, and copies the official 128x128 inventory icon.
`tools/grip_offsets.py` derives each knife's grip point from its own mesh.

## PBR material maps (v0.13.0)

Exported by `tools/export_pbr_textures.py` from the same CS:MC client package as the base colours
(original-author authorisation covers all of it):

| File | Source |
|---|---|
| `Textures/ScCsgoKnives/<knife>_orm.png` | `tex/source2_vmat/weapon_knife_<x>/*_ao_*.webp` (R) + `*_rough_*.webp` (R = roughness -> G, G = metalness -> B), factory finishes only |
| `Textures/ScCsgoKnives/<knife>_normal.png` | `*_normal_*.webp` where the finish ships one; a 4x4 flat map for the knives that use CS's shared 1x1 default |
| `Textures/ScCsgoKnives/env_specular_rgbm.png` | `CSMCMod-5.10-o.jar: assets/csmcmod/textures/source2_environment/studio_specular_rgbm.png`, unchanged (6 roughness rows x 6 cube faces, 128px, RGBM) |
| `Textures/ScCsgoKnives/env_brdf.png` | computed here (split-sum GGX lookup, no external data) |

The shader itself (`Shaders/KnifePbr.vsh/.psh`) is written from the standard metallic-roughness
model; CS:MC's own shader files are a protected container and were not opened.


## Survival grenades — 0.23.0–0.25.0

Six grenade body models and 54 first-person clips were read from the local CS2 installation using ValveResourceFormat 20.0. Conversion: `tools/import_cs2_grenades.py`. The existing CS2 arm/glove rig is reused. The Steam installation, prior exports and VPS resources were not modified or removed.

Source hashes and material dependencies: `docs/survival-grenade-sources.json`. Existing local CS2 audio/particle exports and derived output paths: `docs/survival-grenade-audio-sources.json`, `docs/survival-smoke-sources.json`, `docs/survival-fire-decoy-sources.json`.

The body already carries its attached pin/ring/handle; detached shared pin/spoon debris is not duplicated. The Molotov rag/liquid helper bones use their exported local bind under the animated parent; cloth and liquid simulation are not reproduced. Body, liquid and flame materials remain separate. Smoke/fire use CS2 sprite slices with this mod's bounded particle arrangement, not the original Source 2 simulation. HE/flash bursts are simplified transient light. The release time uses the exported `.Throw` sound cue as an explicit port mapping; it is not an exported server gameplay event.

## 0.26.0 生存内容外观完善

- 六种投掷物栏位图来自既有 CS2 `panorama/images/econ/weapons/base_weapons/` 导出，按透明边界裁切并统一留白。
- 火焰、爆炸烟尘和烟雾来自既有 CS2 `fire_small_sim_a`、`explosion_fireball_large_01_smoke`、`vistasmokev1_emods` 图像序列，各抽取 16 帧组成带边距的 atlas。
- `survival_surface.png` 是本项目生成的金属、橡胶、黄铜、镜片和工作垫材质；物品与工作台网格由 `ScSurvivalMesh` 创建。`grenade_glow.png` 为程序生成的柔边光核。
- 可重复导入脚本：`tools/build_survival_polish_assets.py`；逐文件 SHA-256 和源路径见 `docs/survival-polish-sources.json`。本轮不删除任何既有本地、Steam 或 VPS 资源。

### 0.28.0 combat feedback

BF1 normal kill confirmation is extracted from the user-provided installation, `Sound/UI/UI_KillMessage_Wave`, chunk `28a7c346-7512-c4af-22b0-cedf75129c6e`. See `docs/bf1-feedback-source.json` and `tools/extract_bf1_feedback.py`. The full 2.521-second stereo sound is preserved and converted to Ogg.

The supply atlas is encoded as RGBA with alpha 255; all decoded color pixels and its 256×128 size are identical to 0.27.0. Original weapon texture quality is unchanged.

### 0.28.1 clear kill chime

`bf1_kill_ding.wav` derives from BF1 `Sound/UI/UI_KillMessage_HeadShotAdd_Wave`: trimmed and equalized metallic layer, 0.95 seconds, mono PCM16 48 kHz. This port uses it on all confirmed kills; no headshot result is inferred. The previous normal kill audio remains preserved. See `docs/bf1-ding-0281-source.json`, `tools/extract_bf1_feedback.py --kind ding` and `tools/build_bf1_ding.py`.
