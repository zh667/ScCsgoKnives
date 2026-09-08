# 0.38.3: Clean Held Gun Finishes

User scope (2026-09-08): the original gun appearances are acceptable; the eleven
added held finishes look chipped and unfinished. Inventory icons are already
official `light` images and are unchanged. No CS2 export is removed.

## Corrected Diagnosis

The earlier claim that the legacy meshes were unavailable was incorrect. Each
existing AK47/AWP/M4A1-S GLB contains both `body_legacy` and `body_hd`, in the same
bind space. Only the old paths had been searched. Nine previous `palette` bakes
discarded artwork and transferred icon colours onto scratched default albedo.
Ten skins use legacy resources, but only eight of those were palette bakes;
the ninth palette case was the current-model Fade.

## Changes

- Eight custom/gunsmith finishes now transfer their original RGB artwork from
  the legacy surface to the current HD atlas using nearest surface points and
  barycentric UV interpolation. Searches stay within the same named bone.
  UV-island gutters are padded to prevent black-background filtering seams.
- Fire Serpent retains the existing material mask for its unpainted wood.
  The other custom finishes preserve their authored whole-weapon RGB artwork.
- Lightning Strike and Hydroponic no longer multiply paint by the scratched
  default colour luminance. Their existing HD paint coverage remains in use.
- Fade reads the actual structured recipe overrides, including colours,
  rotation and custom coverage. The template test pattern and the former
  icon-palette shortcut are no longer used. Its gradient is continuous across
  parts in the model's longitudinal/vertical plane. PBN red is coverage, not
  a longitudinal coordinate.
- Geometric AO stays in ORM. Existing HD normals are retained. Paint roughness
  uses the recipe; metalness remains an explicitly approximate style value.
- The offline preview now uses Z-up geometry and the exported/runtime Y-down
  UV convention. Its former Y-up camera and V flip made it an invalid visual
  reference. Before/after views use the corrected convention on both images.

No C# gameplay/rendering logic, mesh, rig, original texture, icon, variant ID,
item layout, record schema, durability or save migration is changed. Skin
material filenames remain stable, so all existing material consumers receive
the replacement textures without a save conversion. Catalog version 2 refers
only to the offline bake catalog, not the world save schema.

## Reproduction

```powershell
python -m pip install -r tools/requirements-gun-skins.txt
python tools/build_gun_skins.py --export-root PATH_TO_ALL_WEAPONS
python tools/gun_skins_selftest.py
python tools/gun_skin_preview.py --silencer --out docs/gun-skins-preview.png
dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release
python tools/pack_scmod.py --edition both
```

For staging, supply `--out DIR --manifest PATH` to the baker. It does not write
inventory icons unless explicitly passed `--icons`. UV caches are identified
by GLB content hash, atlas size and algorithm version. Dependencies are offline
build tools only, not game dependencies. Source/output hashes and projection
distance statistics are recorded in `gun-skins-assets.json`.

## Verification And Limits

Six bake regressions pass: clean-coat independence from scratched albedo,
pixel-centred Y-down atlas sampling, packed-alpha RGB preservation, colour-mask
endpoints, structured Fade overrides, and all eleven installed asset hashes /
dimensions / unchanged normals and icons. Full and Lite each pass **6270/6270**
PackageCheck assertions using their packaged DLL and vanilla Content.zip.

Both packages contain 746 entries and share the same DLL hash. A full byte
comparison against the respective 0.38.2 editions finds exactly 23 changed
entries: eleven colour maps, eleven ORM maps and modinfo.json. The other 723
entries, including all original maps, icons, normals, geometry, animations and
the DLL itself, are byte-identical. All 210 Lite texture transformations were
checked against source/output hashes. See `gun-skins-verification-0383.json`.
Full is 248.1 MB; Lite is 122.0 MB. Install only one edition.

Corrected software views cover both sides of all eleven skins and the attached
M4A1-S silencer (`gun-skins-preview.png`, `gun-skins-reverse-0383.png`). Three
representative before/after pairs are in `gun-skins-before-after-0383.png`.
These are texture checks, not in-game PBR screenshots. Release build succeeds
with the existing NCalc.Core/NCalcSync NU1902 dependency warnings; no dependency
upgrade was included in this appearance-only change.

This is a substantial artwork/clean-coat correction, **not an exact port of
Valve's compositor or an official Factory New wear state**. No additional
grunge/wear is generated, but intentional detail in source artwork remains.
Legacy-to-HD geometry differs, so small features and curved surfaces can stretch.
Shared-pattern placement is still the mod's HD-UV approximation; Fade uses a
fixed planar mapping, not CS2 random-seed/fade-percentage reproduction. Custom
normal maps, per-pixel gunsmith metal routing, and pearlescence are not ported.
Actual first/third-person PBR, reload and silencer behaviour still require game
acceptance. Existing durability does not drive visual paint wear in this change.
