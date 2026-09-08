# 0.38.5 — AK HD geometry and M4 finish lighting

User scope: correct the legacy-looking AK handguard and the dark M4 finishes
shown in the supplied in-game screenshots. No gameplay, storage or inventory
transaction changes are made in this release.

## Resource selection

The earlier statement that every finish has an official HD-UV version was not
established by the export. A CS2 installation contains both legacy-compatible
and HD bodies; a legacy paint recipe is not proof of an official HD equivalent.
This release explicitly distinguishes source artwork (`legacyModel`) from the
chosen display geometry (`displayBody`) in the offline catalog.

- Four AK finishes now use the same CS2 HD body as the factory AK in first
  person, third person and dropped-item rendering. Default geometry is unchanged.
- The source coat is composed in legacy UV, then transferred to the HD atlas
  with bone-constrained correspondence and repeat sampling. Unlike 0.38.3, UVs
  outside 0..1 are never clamped when transferring color OR coverage.
- Hydroponic's complete HD magazine and its atlas gutters explicitly retain
  the HD factory substrate. Native and HD masks preserve HD wood furniture for
  Hydroponic and Fire Serpent. HD normal/AO maps follow HD geometry.
- Wild Lotus and Vulcan transfer the authored artwork onto the HD parts. This
  is an adaptation, not an official Valve HD bake: local geometric projection
  distortion and differences in pattern placement remain possible.
- AWP and M4 retain their 0.38.4 native model/UV combinations, including the
  independent AWP scope hardware and the continuous Lightning Strike artwork.
  Fade remains HD. All non-AK texture bytes are unchanged from 0.38.4.

No assets were deleted. The old AK legacy OBJ set is retained for offline
binding checks and source comparison but is no longer selected in game.

## M4 brightness

`KnifeTuning.PbrGunEnvIntensity = .25` was fitted to the original dark guns,
as documented in the existing code. Previously every finish inherited this
extra attenuation. `KnifePbrRenderer.GunEnvFactor` now returns 1 for known M4
finish materials only. Original M4, AK, AWP, knives and independent scope
materials keep their previous factors. An unavailable finish resolves to the
factory stem, so its lighting also falls back to the factory policy.

The selected factor multiplies both diffuse and specular environment light.
This does not change source RGB, exposure, direct light, roughness, AO, scene
light or the tone mapper. It does not make guns emissive: zero scene light still
means zero illumination. M4 appearance is deliberately brighter; it is not a
claim of exact CS2 lighting, pearlescence or wear compositing.

`gun-skins-lighting-0385.png` compares .25 and 1 on identical geometry and
texture sets using the existing PBR equations. The probe is orthographic and
uses face normals to isolate illumination; it is NOT an in-game screenshot or
a complete validation of the runtime tangent-space normal path. The selected
pixel mask is held constant for both measurements. Actual player tuning may
also affect the final image.

## Verification and reproduction

```powershell
$env:PYTHONPATH = 'tools;.tmp/skin-bake-deps'
python tools/build_gun_skins.py
python tools/gun_skins_selftest.py
python tools/gun_finish_lighting_probe.py
python tools/gun_skin_preview.py --gun ak47 --size 800 --out docs/gun-skins-hd-0385-front.png
python tools/gun_skin_preview.py --gun ak47 --size 800 --reverse --out docs/gun-skins-hd-0385-reverse.png
dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release --no-restore -t:Rebuild
dotnet build tools/PackageCheck/PackageCheck.csproj -c Release --no-restore
python tools/pack_scmod.py --edition both
# Run PackageCheck on both versioned packages, then:
python tools/verify_gun_native_release.py --version 0.38.5
```

Ten Python tests pass. PackageCheck adds one material-lighting assertion per
finish and verifies the updated AK display selection. The release verification
JSON records exact packaged checks/hashes and checks that only the twelve AK
maps, DLL and metadata change against 0.38.4. Full and Lite share the same DLL.

Variant order, paint IDs, v5 item meanings, schema 2, ammunition, durability,
charge, silencer state, migration converters, switching fixes and icon scaling
are unchanged. Existing package state/migration checks still run. No game
installation or world was modified, and no new in-game test was performed.
Device acceptance still needs AK inspect/handguard/magazine checks and M4
brightness comparison at the same position, time, camera angle and tuning.
