# Asset sources

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
