# CSMC asset converter

This is a standalone .NET converter for the binary assets used by the CSMC
client. It deliberately has no SCAPI dependency and converts all three knife
variants used by ScCsgoKnives.

From the `ScCsgoKnives` directory:

```powershell
$mesh = "E:\Obsidian Document\Document1\reference\CSMCClient20260822-selected\gec_texture_stream\obj_bin\knife\knife_m9\weapon_knife_m9_legacy_weapon_knife_m9.body_legacy.meshbin"
$anim = "E:\Obsidian Document\Document1\reference\CSMCClient20260822-selected\gec_texture_stream\anim_bin\weapon_knife_m9.animbin"
dotnet run --project tools\CsmcAssetConverter\CsmcAssetConverter.csproj -c Release -- `
  --mesh $mesh --anim $anim `
  --out .tmp-previews\m9-csmc-diagnostic.json `
  --obj .tmp-previews\m9-csmc.obj `
  --runtime .tmp-previews\m9-csmc.animation.json
```

The reader follows the CSMC decompiled readers (`b$4nj`, `b$4nk`, `b$4j3`,
and `b$4k0`): all integers and floats are little-endian, strings are UTF-8
with a 32-bit byte length, and every array has a non-negative 32-bit length.
The M9 mesh uses a 28-byte vertex stride. The output keeps each vertex buffer
as base64 and also exposes the first eight vertices as position, UV candidate,
and two packed 32-bit fields. Animation curves and their bone/transform
bindings are retained without baking or resampling, so the next conversion
step can map them to the SCAPI renderer without losing source data.

The OBJ export decodes the confirmed 28-byte layout: three position floats,
two UV floats, four color bytes, and a signed normalized three-byte normal plus
padding. It converts the CSMC model basis to SC with
`(x,y,z) = 1.6 * (sourceY,sourceX,sourceZ)` and can use `--obj-parts-dir` to
preserve butterfly knife rigid mesh records. The runtime animation export includes only `deploy`, `inspect`, and
`idle`; these map to `firstperson_draw`, `firstperson_lookat01`, and
`firstperson_idle`. The zero-length `inventory_inspect` clip is an inventory
pose and is intentionally not used as the first-person inspect animation.
