# Archived: CS2 muzzle particle systems (deathmatch round 6, R6-5)

Not built, not shipped. Delivered once as candidate dmr6d (2026-10-05). The user rejected the look ("这个枪烟枪火改的太难看了，
能回归之前的版本吗？", 2026-10-05), so the core went back to the earlier muzzle flash (candidate dmr6e).

- `Cs2MuzzleFx.cs`: the core loader of `AnimationData/cs2_muzzle_fx.json` (format `ScCsgoKnives.Cs2MuzzleFx/2`), was `src/ScCsgoKnives/Animation/`.
- `ScMuzzleParticles.cs`: the interpreter of CS2's particle programs, was `src/ScCsgoKnives/World/`.
- `MuzzleFxRegression.cs`: its PackageCheck regression, was `tools/PackageCheck/`.
- The data generator stays at `tools/cs2_muzzle_fx.py`. The calibration record is in
  `docs/tasks/deathmatch-addon-round6-20261005.md` §4.

Bringing it back also needs:
- the shot hooks in `SubsystemScGunBlockBehavior` (local and remote shots, Update, Draw, LightAt);
- `CsmcFirstPersonRenderer.TryGetPlayerViewModelMuzzle`;
- the csproj EmbeddedResource line;
- `completion_140.EMBEDDED_ADDITIONS`;
- the asset record in `completion_140.CORE_RECORDS`;
- the PackageCheck registration.
