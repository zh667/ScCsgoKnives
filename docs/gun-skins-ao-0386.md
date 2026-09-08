# 0.38.6 — Final-material AO, not paint-generation inputs

User approved fixing Printstream's dirty-looking shading after a resource audit.
The paired request is a comprehensive report of current weapon numbers and
crafting materials, not authorization to rebalance weapons or change settings.

## Fix

The legacy bake used `TextureAmbientOcclusion1` from `csgo_composite_inputs` and
copied its R channel into the runtime ORM. This is a paint-generation input,
not the gray AO referenced by the target gun's final `csgo_weapon` VMAT.

The exported `_shared_paint_generic.vcompmat` initializes from target_instance,
generates color and metalness/roughness, copies the finish normal when present,
and replaces AO only for an explicit ambient_occlusion override. No such
override is present in the inspected Printstream recipe.

`gun_inputs(..., legacy=True)` now resolves AO from the actual legacy weapon
VMAT `TextureAmbientOcclusion`. Composite maps remain preserved as source inputs.
AK's final HD adaptation already replaces AO with HD AO and is unaffected.

Changed outputs: six ORM maps for Lightning Strike, Gungnir, Dragon Lore,
Printstream, Player Two and Golden Coil. Only AO/R changes; G roughness and B
metalness must remain byte-identical. Every base color/normal/icon, original gun
appearance, AK HD adaptation, Fade, mesh, animation and gameplay file is preserved.
No installed game file or world is modified. No new migration is needed.

Do not remove intentional X/heart/barcode/stock symbols: the shipped Printstream
color equals a direct RGB resize of the source pattern. Fine texture/normal
sampling artifacts and the missing full pearlescence compositor are separate
issues; this patch does not claim every dark pixel is eliminated.

## Checks

- Eleven Python bake tests, including final-material AO binding regression.
- Full/Lite PackageCheck and exact package-diff audit; see the versioned
  verification JSON for measured counts/hashes, same-DLL proof and removal list.
- `gun-skins-ao-0386.png`: before/after rendered with identical lighting,
  color, roughness and metalness, changing AO only. Orthographic face-normal
  offline diagnostic, not an in-game screenshot or a full normal-map GPU test.
- Existing NCalc dependency NU1902 build warnings remain; no dependency upgrade
  was bundled into this resource fix.

## Numbers report

`WeaponStatsExport` reads the packaged DLL methods and tables and emits
`weapon-stats-0386.json`; `tools/weapon_stats_report.py` produces the full Chinese
reference `weapon-stats-and-recipes-0386.md`. It includes 35 gun types, 22 knives,
6 throwables, 11 finishes, derived raw-material totals, actual survival power,
falloff, headshots, recoil, spread, clip durations, reload costs and repair costs.

The game's current tuning file was read only: GunNumbers=0. The user withdrew
their momentary objection; no setting, weapon balance, recipe, ID, v5 layout or
schema 2 field is changed. The report explicitly distinguishes CS2 reference
damage values from the runtime survival Attack path and marks final-health,
optional-mode and device-testing limitations.

## Reproduction

```powershell
$env:PYTHONPATH = 'tools;.tmp/skin-bake-deps'
python tools/build_gun_skins.py
python tools/gun_skins_selftest.py
python tools/gun_finish_lighting_probe.py --baseline-package output/ScCsgoKnives-0.38.5.scmod --out-stem gun-skins-ao-0386
dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release --no-restore -t:Rebuild
dotnet build tools/PackageCheck/PackageCheck.csproj -c Release --no-restore
python tools/pack_scmod.py --edition both
# Run PackageCheck with --json for both packages, then:
python tools/verify_gun_native_release.py --version 0.38.6
dotnet tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll --scmod output/ScCsgoKnives-0.38.6.scmod --weapon-stats docs/weapon-stats-0386.json
python tools/weapon_stats_report.py
```
