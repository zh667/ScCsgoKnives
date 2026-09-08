# 0.38.4: Native Gun Finish Geometry

User authorization: implement the native-model/UV route after reporting scope,
lightning, magazine and handguard defects, same-model equip animation failures,
and oversized creative/inventory icons. This does not resume blocked milestones.

## Rendering Contract

- Ten legacy finishes use their original CS2 `body_legacy` mesh and UVs. M4A1-S
  Fade and all factory appearances keep the existing HD resources byte-for-byte.
- Eight custom/gunsmith finishes sample authored RGB directly. The old
  nearest-surface reprojection is no longer used. Repeat sampling keeps AK
  magazine UVs outside 0..1 instead of collapsing them onto atlas edge pixels.
- Lightning Strike uses the legacy atlas, restoring the continuous side artwork.
  Hydroponic uses native coverage with the green hardware category excluded,
  leaving the magazine bare. Fire Serpent preserves native wooden furniture.
- The game's standard `BlocksManager.DrawMeshBlock` forces PointClamp. Native
  dropped and third-person meshes use a scoped draw helper with LinearWrap;
  color, vertex lighting, size and optional view-projection behavior are retained.
- Every native part retains its bone and normalized-to-rig rest matrix, including
  AK cliprelease, which the previous HD part list did not contain. The generated
  17 parts have zero blended vertices and zero cross-bone triangles.
- AWP's 48-triangle `shared_scope` primitive has its own original material. It is
  opaque scope hardware, not the AUG/SG translucent lens. It never samples paint.
- Normal maps follow the native material or the finish's explicit override:
  Gungnir and Printstream use their supplied normals; material default normals
  are flat. HD original normals must not be applied to legacy UVs.
- First person, third person and dropped/template items select geometry and
  material together. Missing native geometry falls back to factory geometry AND
  factory color/PBR stem, not to a mismatched skin/UV combination.

## Switching And Icons

Selection observes inventory identity, active slot, contents and stable v5 gun
reference. Ammo, wear, charge, finish and silencer state live in the record and do
not cause equip restarts. Initial fresh-template allocation in the same slot is
not a second equip. Same-model guns in different slots do restart deploy.

Switching cancels the previous reload transaction and its scheduled cues without
cancelling the new gun's deploy. Pending silencer, burst and rescope actions are
cleared. Creative templates use the factory gun's .8 icon view scale; finished
icons use 1.38 instead of 1.45 flat size to compensate for reduced alpha padding.
Icon bitmap files remain unchanged.

Gun ordering, paint IDs, v5 layout, schema 2, record state and converters do not
change. No migration, ammo refill, durability reset or ID allocation is added by
the appearance selector. No installed game files or worlds were modified.

## Verification

Eight Python bake/UV tests pass. Full and Lite each pass 6295 packaged checks,
including native assets and animated bindings, actual native third-person OBJ
assembly, separate scope material, template icon scale, same-model switching,
fresh allocation, instance changes and cancellation of the old reload without
erasing the new deploy. Existing registry, migration and state tests also run.

`gun-skins-native-front.png` and `gun-skins-native-reverse.png` are offline native
UV renders, not screenshots of the game's PBR shader. The preview draws the
independent scope hardware with its constant base color; it does not reproduce
that material's reflected lighting. Both editions have one identical DLL.

`gun-skins-native-0384-verification.json` records package hashes and the exact
replacement/addition/removal manifest against 0.38.3. Thirty old skin texture
files are replaced at their existing paths. No resource is deleted. Factory
assets, Fade, all icons, audio and unrelated effects remain byte-identical.

## Reproduction And Remaining Acceptance

Dependencies: `tools/requirements-gun-skins.txt`, the existing CS2 GLBs/material
exports, and the new `11_legacy_composite_inputs` external extraction. Export
filters are `materials/models/weapons/customization/rif_ak47/`, `snip_awp/`, and
`rif_m4a1_s/` from the installed CS2 VPK, using Source2Viewer-CLI `-d`.

```powershell
python tools/cs2_glb_to_obj.py --body legacy --json docs/gun-native-meshes-export.json
python tools/build_gun_skins.py
python tools/gun_skins_selftest.py
python tools/gun_skin_preview.py --silencer --out docs/gun-skins-native-front.png --size 640
python tools/gun_skin_preview.py --silencer --reverse --out docs/gun-skins-native-reverse.png --size 640
dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release -t:Rebuild
python tools/pack_scmod.py --edition both
# Run PackageCheck against both packages before:
python tools/verify_gun_native_release.py
```

No actual game visual acceptance was performed this turn. Check the AWP eyepiece
and both lightning sides during inspect, all four AK magazines, Printstream's
front in actual lighting, reload/silencer part movement, same-model hotbar
switching and icon clipping on the target device. Preserve authored Printstream
details; do not promise that every dark source mark is wear or erase it blindly.

This is not Valve's full paint/wear compositor. Fixed pattern placement, uniform
paint roughness/metalness and absent pearlescence remain known limitations.
Official `light` icons still cannot be labelled strictly Factory New.
