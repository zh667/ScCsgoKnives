# Project conventions

- User-reported 1.2.0 migration failure, 2026-09-25 follow-up: release 1.5.3 replaces
  1.5.2's blanket local-reference refusal with verified-backup-first preservation.
  Recognized v5 tables may retain missing/quarantined/model-conflicting items while
  healthy guns remain usable. Preserve every item value and raw row; reserve the
  allocation watermark above ALL old references before gameplay, including inactive
  slots, drops, projectiles, moving blocks, recovery and pending kills. Unknown formats,
  missing whole tables, nontext records, stacked instances or backup failure still refuse.
  Invalid model references must not witness duplicates (or strip healthy gun growth),
  and cannot be exported as the wrong model in travel packets. Keep prior packet evidence
  with an explicit Error, not silently replace it with guessed state. Protected local
  anomalies have a saved notice and a verified full-world snapshot; changed anomalies
  require a new snapshot. New world and release formats/IDs remain v5/schema6/rules7.
  The supplied actual 1.2.0 save already has Slot10 M249 -> AK record 3 and Slot11
  M4A1-S -> AK record 4; freeze the minimal extracted fixture and its provenance under
  tools/fixtures/migration-120-20260925. Do not claim these two original guns recovered.
  See docs/release-single-1.5.3.md. Do not install or rewrite player worlds.

- User correction 2026-09-25 supersedes the global base RPM reduction: every Lv0 gun
  uses extracted CS2 cadence, including burst and R8 alternate timings. Zeus base
  recharge is 30 seconds. Reduce earned speed bonuses only: ordinary Lv50 x1.325,
  auto-snipers x1.2, bolt actions x2.625; Zeus frequency retains 65% of its old bonus.
  Saved charge seconds/cycles remain unchanged; freeze historical LegacyCycle at 10s.
  Release 1.5.2 keeps v5/schema6/rules7 and all IDs. Before gameplay validate saved
  item references using the saved block map, after authorized travel conversion;
  missing records must never initialize a replacement gun or allow ID reuse.
  Same-schema supported upgrades now create verified full-world backups, recorded
  in GunReleaseBackup. Preserve actual player XML/log audit copies locally.
  World7's missing records followed a session with an invalid nested ZIP renamed
  scmod (mod disabled); no verified source for those lost records was found.
  See docs/release-single-1.5.2.md. Deliver Full/Lite single scmods including tactical;
  use unchanged compressed resource streams, do not install or edit player worlds.

- User-directed 2026-09-24 follow-up supersedes the ZIP delivery shape: deliver
  Full/Lite as one directly importable scmod each. Bundle metadata is 1.5.1;
  core/tactical gameplay stays at verified 1.5.0/1.4.0 bytes. ScCsgoBundle is a small
  identity adapter: register an archive-free tactical identity only after native
  loading, preserve UsedMods and refuse a duplicate standalone tactical mod.
  Do not inject aliases during assembly enumeration or duplicate hooks/resources.
  Lite additionally uses lossless standard Deflate recompression, no extra quality loss.
  See docs/release-single-1.5.1.md; no installation or world edits are authorized.

- Release correction 2026-09-24: the first 1.5.1 single-scmod builds wrote the resource
  marker XML declaration as `encoding='utf8'`, which the native Survivalcraft XML reader
  rejects on world entry although desktop parsing accepts it. Current replacement builds
  use standard `utf-8`; run the cold native resource/world gate and both packaged checks
  before delivery. The old hashes are invalid release artifacts.

- User-directed 2026-09-24 release: package Full and Lite comprehensive ZIP bundles,
  each containing current core and tactical scmods (including built-in appearance).
  Packaging is now authorized; installation is not. Preserve both PackageNames,
  v5/schema6/rules7 and original assets. Target Lite below 100,000,000 bytes through
  derived 512 textures/model optimization; keep gameplay DLLs identical across editions.

- User-directed 2026-09-24: implement the gun balance/material plan in source only;
  do not package or install yet. Keep v5/schema6/rules7 and frozen IDs. Runtime rate
  x0.65, auto-snipers additionally x0.75, early shotgun compensation, independent
  repair bases and one final ceiling are implemented. HE/fire buffs exclude creature
  ignition and preserve chicken explosions. Actual user 1.0.0/schema4 and
  1.2.0/schema6 packages are mandatory upgrade baselines, superseding old assumptions
  that only 0.28.2 was public. Version conversion keeps ongoing charge seconds;
  gameplay level-ups keep the remaining fraction. See docs/gun-balance-implementation-2026-09-24.md
  and tools/BalanceCheck. Never overwrite immutable historical fixtures to make tests pass.

- User-directed 2026-09-24: audit/fix Sushi inventory compatibility in source only; do not
  package or install yet. Build with `-p:SkipScmodPackaging=true`. Actual Sushi sync channels
  are a Dictionary, not IList; scan unselected channels and normalize creative person-box
  proxies to their real inventories. Keep v5/schema6, records and third-party packages.
  Never refund to a retuned/recreated channel by number alone: Sushi has no persistent
  channel UUID. Retain ambiguous compensation for verified recovery. See
  docs/sushi-inventory-fixes-2026-09-24.md for the actual-DLL regressions and boundaries.

- User-directed 2026-09-21: Full 1.4.9 / tactical 1.3.2 adds player CT/T third-person
  gloves and moves appearance to workbench 功能 → 人物外观／更换手套. Use transparent
  thumbnails, large preview and explicit apply; preview/cancel must not mutate saved choice.
  Use CS2 worldmodel gloves with their own inverse binds and full finger hierarchy, and
  per-component mesh orders, never shared Model.IsVisible. Retain repeated-role reset guards.
  World gloves must reuse the native model's cached smooth lighting, not the root/foot cell:
  the latter causes the reported black hands and flicker along terrain boundaries.
  Keep all prior assets, IDs, v5/schema6 and other mods unchanged. NMM HUD/model-selection
  previews and NPC gloves remain original; no new network transport or full-game claim.
  Free public release without paid content is the stated publishing scenario. See
  docs/legal-assessment-2026-09-21.md; it is a risk assessment, not clearance or permission
  to remove resources, rewrite Git history or contact rights holders. Release evidence and
  recoverable installation are documented in docs/release-gloves-1.4.9.md.

- User-directed 2026-09-21: Full 1.4.8 / tactical 1.3.0 adds CT SAS / T Phoenix
  first-person arms and five optional glove finishes at minimum legal wear 0.06.
  This supersedes the older keep-default-arms instruction when a CS role is selected.
  No role and no glove override retain the old core arms. Resolve the actual drawing
  player's native NMM role and saved per-player glove choice; no global selected role.
  Ship resources/menu/adapter inside tactical, not a separate glove or appearance mod.
  Keep NMM/Neorxna optional, third-party packages untouched, item IDs and v5/schema6
  unchanged. Glove replacement currently covers first-person CS items and empty hands;
  third-person gloves stayed original in 1.4.8 (superseded above). Preserve mesh-specific inverse binds, sleeve twist
  synthesis and released grenade/C4 viewmodel guards. Source GLSL composition uses fixed
  pattern offsets, not an asserted economy seed. See docs/release-firstperson-1.4.8.md
  for diagnostic evidence, installation manifest and actual-game acceptance limits.

- User-directed 2026-09-21: Full 1.4.7 / tactical 1.2.3 unifies ammunition,
  grenade, C4 and tactical recipes with station/help. Three/five enemy squad
  beacons are survival-craftable, single-use challenges; failed whole-squad
  creation rolls back both entities and the item. Keep manual spawn independent
  of natural population gates. Installed SlowerCreatureSpawnsMod caps ordinary
  creatures at 2, so native whole-squad spawning cannot fit; do not rewrite that
  mod or global budgets. Base recipe constructor and four-argument craft APIs
  stay binary-compatible. Batch multipliers apply to survival stacks, creative
  slots remain infinite sources. Preserve item IDs, v5/schema6 and third-party
  packages. See docs/release-crafting-1.4.7.md and installed audit evidence.

- User-directed video feedback 2026-09-20: Full 1.4.6 / tactical 1.2.2 correct T/CT
  hand/prop contact and weapon-specific holds. Derived world actor and prop tracks share
  source frames; wpn is attached relative to spine_2, not an inverse spinning knife wrist.
  Import resolved draw endpoints for holds, never raw additive idle clips. Preserve knife
  joint partitions and cross-joint triangles, FPP resources, eye textures, 48 GPU joints,
  per-entity clocks and camera guards. No third-party mod edits. Native contact fixtures,
  geometry renders and packaged checks are documented in docs/release-thirdperson-1.4.6.md;
  they are not full-game/Android/network acceptance. Recycle only exact superseded output
  packages with the manifest; installed Mods and all worlds/fixtures remain untouched.

- User-directed 2026-09-20 installation simplification: tactical 1.2.1 includes the CS player
  appearance adapter and NekoMeko resource descriptors. No separate CS appearance scmod is shipped.
  Keep NekoMeko Model 1.1 / Neorxna 1.4 optional for tactical gameplay; load the adapter's .bin only
  when both enabled providers are present. Preserve ScCsgoAppearance assembly/type identity and
  model/skin save keys. Core Full 1.4.5 is byte-identical. The old standalone coexistence fixture
  under tools/fixtures is protected validation data, not an installable release. See
  docs/release-tactical-1.2.1.md and its evidence. Recycle superseded output packages with a manifest.

- User-directed 2026-09-20 follow-up: third-person actions ship in Full 1.4.5 / tactical 1.2.0 /
  appearance 1.1.0. Player action clocks advance outside FPP drawing while retaining grenade/C4
  post-release viewmodel values. Per-entity presentation snapshots never mutate ammo/inventories.
  CT/T use derived world upper-body draw/reload clips with native gait; inspect is authored.
  Third-person weapon parts reuse CS2 weapon clips, with helper-shell visibility guards and fallback
  joints preserved. Repeated cameras must not advance NPC/player pose twice. No network transport
  is implemented; do not claim multiplayer or full-game/Android acceptance from native diagnostics.
  See docs/release-thirdperson-1.4.5.md. Preserve the baked eye fix when rebuilding agent assets.

- User-directed 2026-09-20: optional NekoMeko player CT SAS / T Phoenix appearance is implemented
  as ScCsgoAppearance 1.0.0 with Full core 1.4.4 and tactical 1.1.4. Preserve native NMM selection,
  per-player saved keys and return to default; keep CS2 weapon first-person arms. CT lenses and T
  eye shader features are baked into shared derived textures; never edit the original CS2 exports.
  User now authorizes removing superseded packages and confirmed disposable reports/logs, with an
  exact manifest and recoverable recycling. This supersedes historical preserve-every-old-package
  instructions, not world/backup/compatibility-fixture protection. After this first release, implement
  third-person reload/equip/inspect for summoned CT/T and player third person, keeping action state
  per entity for future networking. See docs/release-appearance-1.0.0.md.

- User-directed 2026-09-20: CS bullets must not knock players back or add native locomotion
  stun (which locks movement/look). Deliver core Full 1.4.3 plus tactical DLC 1.1.3. Apply at the
  native bullet effect stage for player/companion/enemy fire, preserving damage, armor, non-player
  control, existing unrelated stun/impulse and intentional Zeus electric control. Keep NPC shots
  distinct from the chicken-exploding GunAttack marker. See docs/release-1.4.3-player-bullet-control.md.

- User-directed 2026-09-20 correction: DLC 1.1.2 removes manual three/five-member squad
  population/player-distance gates and handles non-collidable snow over real support. Keep natural
  spawn budgets. Remove hostage creative/crafting/use routes without reusing data 0 or destroying
  legacy inventories. CT/T acquire local hostiles, assist owner attacks, and use melee when unarmed.
  Navigation owns moving-body rotation; stationary aim uses locomotion orders, with bounded stuck
  retries. Core 1.4.2 and gun layout v5/schema6 remain unchanged. See docs/release-tactical-1.1.2.md.

- User-directed continuation 2026-09-20: fix companion inventory gestures and hostage rendering,
  make tactical supplies real 3D items, put all CS items in CS武器, add creative three/five-member
  hostile squad beacons and improve skeletal death collapse. Deliver core Full 1.4.2 / DLC 1.1.1.
  Preserve old packages, item data, gun layout v5/schema6 and CS2 source exports. Hostage axis
  conversion must sit under an identity model root; validate native loaded/skinned vertices above
  ground, not merely animation height. Run native drag start/move/release both ways, not just layout.
  Native GPU diagnostics are not full-game or Android/mod-combination acceptance. See
  docs/release-1.4.2-tactical-fixes.md for evidence and remaining boundaries.

- User-directed 2026-09-20 follow-up: implement naturally spawning hostile T squads in the optional DLC,
  independent of existing friendly CT/T/hostage recruitment. Day 1–29 uses sniper/rifle/close roles;
  day 30 adds machine gun and pistol/C4. Enemy guns have finite ammo and no counters/growth.
  Enemy C4 is 40 seconds; hold the rebindable C4 action (default E) or independent mobile button
  to defuse in 10 seconds, or 5 with a crafted carried kit. Preserve native unload equipment/health,
  cancel defusal on invalid context and require release before restarting. Keep gun layout v5/schema6
  and previous packages. Deliver Full core 1.4.1 plus DLC 1.1.0; see the corresponding release document
  for first-build tuning, test evidence and actual game acceptance boundaries.

- User-directed 2026-09-20: implement the optional tactical DLC with adapted shield geometry/poses
  (no old shield model is available), native CS2 CT/T/hostage companions, actual gun/ammunition
  inventories, defensive AI and workbench recruitment/repair. Deliver Full core 1.4.0 plus independent
  DLC 1.0.0; preserve previous packages and gun layout/schema. Keep the base usable without the DLC.
  Companion GPU skin palettes must fit mobile limits; retain source exports and validate derived poses.
  Player appearance replacement and armor/helmet remain future features. See
  docs/release-1.4.0-tactical-expansion.md for implemented protection and compatibility boundaries.

- User-directed 2026-09-19/20: FPS-style controller defaults without stealing vanilla inventory,
  clothing, crouch or hotbar controls; player-local split-screen weapon projection, scope masks
  and muzzle effects. Remove CS2 locomotion root travel from derived chicken animations while
  preserving the source export and skeletal motion. Deliver 1.3.3 Full only, preserving previous
  packages, IDs and save schema. See docs/release-1.3.3-splitscreen-controls-chicken.md.

- User-directed 2026-09-19 follow-up: CS chicken egg uses native ThrowableBlockBehavior aim/release
  and projectile collision spawning, not short-range OnUse. Optional ControllerHaptics 1.2.0
  player-aware pulses for committed shots, completed reloads, knife body hits and grenade release.
  Never require or bundle that provider. Deliver 1.3.2 Full; preserve earlier packages and IDs/save schema.
  See docs/release-1.3.2-egg-haptics.md for verification and hardware acceptance limits.

- User-directed 2026-09-19 correction: native egg hand scale/offset, chicken model x1.6 with
  matching collision size, lower-right HUD ignores invisible touch pads and uses original CS2
  weapon silhouettes with green/yellow/red ammo thresholds. Include creative skin/counter
  templates in read-only inventory wear labels. Reset craft quantity to 1 on recipe change.
  Deliver 1.3.1 Full only; preserve 1.3.0. See docs/release-1.3.1-feedback.md.

- User-directed 2026-09-19: independent button-only touch fire (Sushi key/mouse injection works with
  the CS touch overlay hidden), bottom-left inventory gun wear, lower-right CS2 magazine HUD,
  all knife damage x3 (21 light / 36 heavy), and passive CS2 chicken with interact-to-follow.
  Chicken killed by CS guns explodes using HE 48 damage / 6 m; melee and explosion collateral do not.
  Deliver 1.3.0 Full only; keep existing gun IDs, save schema and previous packages unchanged.
  See docs/release-1.3.0-input-hud-chicken.md for verification and actual-device acceptance limits.

- User-directed 2026-09-19: opening inventory cancels reload. Fix the actual UpdateGun modal cancellation
  and input gate, preserve transaction validity, and keep action milestones on the animation clock.
  See docs/menu-reload-fix-2026-09-19.md. Include in the same 1.2.2 Full-only delivery;
  do not claim the independent knife missing-audio report is resolved.

- User-directed 2026-09-18: implement the first/second-priority items in
  docs/player-feedback-analysis-2026-09-18.md, including smoke, flash and authentic CS2 casing effects.
  SCAR-20/G3SG1 now grow linearly to x1.5 fire rate; only AWP/SSG08 retain x3.5.
  Keep IDs/save formats and verified backups. Penetration remains deferred.
  Later user feedback authorizes visible casing/quiet impact, larger grounded smoke and gem-pattern fixes.
  Follow-up: finish smoke first, reduce overly strong gem veins, vary Fade coverage by knife and enlarge casing display.
  Deliver Full only for this correction, preserving previous packages and source extractions.

- User-directed 2026-09-17: ordinary-gun fire-rate growth is linear from x1 at Lv0 to x1.5 at Lv50;
  scoped snipers retain the existing x3.5 curve and Zeus recharge is unchanged. Ignore the earlier 35% suggestion.
  Improve decoy attraction using actual installed mod creature data, emphasize workbench levels with parenthesized
  kills, and lower first-person assembly supplies/ammunition. Preserve IDs, records and other combat growth.

- User-directed 2026-09-16: implement docs/player-feedback-analysis-2026-09-16.md,
  verify new features and regressions, and deliver Full only for this iteration.
  C4 timer supports both a mobile button and configurable keyboard input (including touch key mappers).
  Preserve gun item layout/schema and installed-source-world backups; no automatic downgrade or backup deletion.

- User-directed 2026-09-15: resume public versioning at 1.1.0 after the supplied 1.0.0.
  Deliver Full and the existing Optimized512 edition as separate self-contained scmods,
  each bundling gameplay DLL, resource DLL, assets and markers; no separate resource mod
  dependency. Both editions use the same gameplay DLL. Pause the proposed new mobile
  performance refactors; use the established derived 512 WebP Q85/model workflow only.
  This supersedes the earlier Full-only and split-resource delivery instructions.

- User-directed continuation: use the existing official light-icon + local skin bake workflow for all33.
  Workshop Tools and exact Factory New composition are NOT required. Document approximate material/wear
  behavior honestly; do not call light exact FN or fake Sakkaku wear. Existing skins stay byte-identical.

- User-directed 2026-09-10: implement docs/owned-gun-stats-and-skins-plan-2026-09-10.md.
  Glock Gamma Doppler is EMERALD ONLY (paint 1119), no phases 1-4. Total 33 requested finishes / 66 new
  ordinary-counter entries. MAC-10 Sakkaku uses its best legal wear 0.21, not Factory New; P250 Whiteout 0.06.
  The later user instruction above replaces strict minimum-wear generation with the existing light-icon workflow.
  Preserve all 35 weapon IDs and existing skin IDs, names must be shared across every item/template/UI route.

- User-directed 2026-09-10 (0.41.12): remove third-party touch-adaptation claims; restore Chinese key captions
  while preserving stored key IDs and all ten weapon actions. Close the hidden Ghoul test UI (including title taps),
  but retain generic cross-world compatibility. Settings/bindings/layout use a frozen in-memory game background,
  not repeated paused-world rendering. Continue Full-only core + unchanged required resource pack 1.0.0.

- User-directed 2026-09-10 (0.41.11): for this release output full-resolution only, no Lite/Mini.
  Split textures/models/audio and large embedded animations/meshes into required resource pack zh667.ScCsgoResources;
  preserve asset paths, paint IDs and save schema. Use pack_scmod.py --edition full --split-resources.
  Match key captions to SushiTouch 2.1's SushiButtonConfigWidget, not its HUD abbreviations.
  M4A1-S skins use the same environment intensity as the factory gun (remove old 4x IBL special case).

- User-directed 2026-09-10 (0.41.10; 0.41.8/9 were test candidates): extend counter growth to Lv30, still 100 kills/level (3000 total).
  Lv0-10 stays unchanged, including zero spread/recoil and unlimited loaded-world bullet range from Lv10.
  Lv11-20 add base damage/capacity/rate +30%/+20%/+10% per level; Lv21-30 +50%/+30%/+15%.
  Durability stays +5% of base per level. Lv30: damage x10, capacity x6.5 (floor), fire rate x3.5, life x2.5.
  Skin base multiplier stays x1.5; Zeus remains one charge, 5s/2.5s/1s at Lv10/20/30. Reload clips are not sped up.
  Record schema 4 expands valid levels; keep schema 1/2/3 readers and verified world backup before conversion.
  Never bump item layout v5 or change weapon IDs. See docs/growth30-ghoul-test-0418.md.
  Cross-world gun snapshots are generic for providers retaining player XML: native creative slots omit Count,
  survival slots require it. Packet v3 supports arbitrary world paths; no guessing missing records or overwriting
  another holder. New identity seeds use a saved world UUID; keep older gun identities unchanged.

- User-directed 2026-09-09 (0.40.7): damage grows +10% of the applicable base per level, +100% at Lv10.
  A valid skin supplies a separate 1.5x base-damage multiplier (skin Lv10 = 3x factory Lv0). Other growth
  values remain unchanged. Counter installation unlocks leveling and starts counting at zero; no prior kills
  are backfilled. Applying, replacing or stripping a skin must preserve counter/kill/level/instance state.
  This supersedes old +50% damage and Zeus 225-at-max text, not the +50% durability/capacity rules.

- Narrow user authorization on 2026-09-08: fix the reported survival weapon-workbench missing drop and first-person muzzle particles being overwritten over water. This does not resume other blocked milestones or permit gun save-format changes. See `docs/workbench-muzzle-fixes-0371.md`.

- User-directed handover of 2026-09-09: the counter/growth/attribute/touch work line is ACTIVE and 0.40.0 implements it
  (attribute page, mobile mod settings and touch layout, kill-feed and gun-crosshair switches, gun-only StatTrak counter,
  ten-level growth at 100 kills a level to 1000, and the Zeus rebuild). Record schema is 3, with the schema 1 and 2
  converters kept; the item layout stamp is still 5 and gun variant order is still frozen. A world saved by 0.40.0 is
  refused by 0.39.1 and earlier, which is the required forward protection, not damage. Windows review 0.40.1 imports the
  official CS2 StatTrak module mesh/digit atlas and adds actual first-person/third-person/item rendering; see
  `docs/gun-stattrak-attachments-2026-09-09.md` for the exact missing paths and
  `docs/gun-counter-review-0401.md` for review fixes and remaining device acceptance. Historical five-level
  XP, 100-kill or 1600-kill caps, +10% damage, +100% durability and the Zeus 54/81 or 3-second charge are obsolete.

- Execution override, user-directed at 2026-09-08 00:22:15 +08:00 (Asia/Shanghai): only M4 gun durability is ACTIVE. M0 general work, M1, M1b, M2/F07, M3, M5, standalone scope/inspect work and their separate device acceptance are BLOCKED until the user explicitly resumes them. Keep existing features. M4-required identity/transaction/save validation, charge state, box integration and narrow fixes for regressions caused by M4 are in scope. Follow `docs/vps-m4-durability-plan-2026-09-08.md`. Later user authorization on 2026-09-08 adds migration from the only published source version 0.28.2: back up the whole world, preserve model/ammo/silencer and give full durability once. Do not guess internal 0.29–0.36 test formats or reclaim IDs. See `docs/official-0282-compatibility-0370.md`. Do not resume other milestones automatically after M4.

- Use CS2 resources and real skinned hands for all first-person weapons. Do not restore the CS:MC / block-hand runtime route or expose a switch that re-enables it. This is the user's standing preference from 2026-09-06.
- Keep gun and knife variant ordering stable: existing worlds store those indices.
- Before removing assets, verify first-person, inventory, dropped-item and shared-effect references. Preserve CS2 source extractions and resources shared with other projects. Record removed paths in a manifest; do not delete by a broad “CS” name match.
- Deliver versioned `.scmod` packages in the project-root `output/` directory. Package only assets present in the current source manifest, so incremental build leftovers do not return to the package.
- Report packaged-DLL checks separately from actual game testing; an offline render is not an in-game screenshot.

- Do not add an overload to a method a tool or regression binds to by name alone: `Type.GetMethod("Required")` throws
  AmbiguousMatchException the moment a second `Required` exists. Give the new shape its own name (`RequiredFor`).
- Self-tests run before BlocksManager has any blocks. A check that needs an item value builds it from a constant, never
  from `BlocksManager.GetBlockIndex`; production helpers that only need the data half tolerate a missing registry.

- Guns already have custom durability and a v5 instance registry since 0.35.0; M4 is still under correction and acceptance. The user's 2026-09-07 instructions supersede the prior all-items-no-durability preference for guns only. Complete gun wear, broken-but-retained guns and workbench repair per the current M4 plan. Keep `ScNoDurabilityBlock` protection against vanilla wear, including on guns; custom wear must never use vanilla damage bits. Test normal gameplay in new worlds, authorized migration in untouched 0.28.2 world copies, and persistence/upgrade protection from the 0.37.0 baseline per the policy below. Unsupported internal test formats remain out of scope; item transfers remain required.
- Follow the current community-feedback plan one milestone at a time. Third-person character and throwing poses are authored for vanilla limbs; only weapon-part animation and event timing reuse CS2 clips. Do not claim full CS2 character animation reproduction. Do not add distant-audio variants. Guns use the planned 1.5x survival damage; knife reach is planned as 2.2 light / 1.8 heavy. Headshot non-kills are yellow; all kills are red. These are implementation requirements, not claims that runtime changes already exist.

- Release Full and Lite together from the same DLL. Keep original-quality source textures; derive Lite 512px textures only while packaging, renormalizing normal maps. Preserve PackageName, item indices, animations and gameplay across editions. Install only one edition per game. This is the user’s preference from 2026-09-06.

# Gun save compatibility (user-directed 2026-09-08)

- Before changing gun identifiers, item encoding, registry/schema, load/save, durability/charge semantics or related inventory transactions, read [the complete compatibility policy](docs/gun-save-compatibility-policy-2026-09-08.md). It supersedes historical new-world-only/no-cross-version rules for guns. Only 0.28.2 has been publicly released; 0.37.0 is the protected forward baseline, not a claim of public release. Other milestones remain BLOCKED.
- Freeze existing gun variant IDs and v5 item meanings; do not reorder/reuse IDs or renumber instance references. Prefer registry extensions. Keep mod version, item layout (5) and record schema (1) separate; change data versions only when their structure/meaning changes, with explicit converters.
- Preserve supported old worlds' gun identity, ammo, silencer, durability, charge and recovery state. Full durability is a one-time initialization for the authorized 0.28.2 migration only. Never guess internal test formats, silently reset state, or treat question marks/new-world instructions as normal release compatibility.
- For required conversions: validate detached data and capacity, verify a full source-world backup before activation, convert completely, save items/records/version markers consistently, and make retry idempotent. Keep prior converters for skipped-version upgrades. Explicitly define parameter-change treatment, including fresh templates; do not discard excess ammunition.
- Unknown future layout/schema must be rejected before normal loading/autosave without rewriting source data or stamps. Existing gun disabling alone is not proof of this protection. Preserve corrupt raw data; only show a model when its identity is reliable. Do not promise arbitrary old DLLs can safely downgrade.
- Data-affecting releases require immutable 0.28.2/0.37.0 and later format fixtures, applicable holder/state checks, two save/reload rounds, retry/failure/unknown-format checks and documented evidence. Keep implemented behavior, planned protection, offline results and device acceptance distinct. Do not bump format or mark pending policy requirements complete merely because documentation was added.

# Two-peer Git workflow (user-directed 2026-09-08)

- Source, committed documentation and packaged assets travel through Git only. Each peer owns an independent checkout; do not synchronize a live repository or `.git` with Syncthing. External CS2 extractions, videos and logs may use a separate resource transfer directory. This rule does not itself stop an existing Syncthing service.
- Before work, run `git status` and preserve existing uncommitted work. Do not overwrite it, silently stash it, or commit another peer's work. Local status cannot tell you whether the other peer is working; coordinate ownership before touching the same files.
- Once the working tree is clean, run `git pull --ff-only origin main`. On divergence, inspect both histories and resolve deliberately; never force-reset to hide it. With unrelated uncommitted work, preserve it and use an isolated clean checkout if necessary.
- Complete, validate, commit and push the work before handing over. The receiving peer then pulls the commit. Include required plans/resources in explicit commits; untracked files do not travel through Git.
- Retire the old `tools/sync_git_from_origin.sh` reset-based handoff. Neither `git reset --mixed` nor `git restore` is a routine synchronization command. Restore/reset is reserved for explicit, backed-up recovery.

# PackageCheck runs headless

- User-directed 2026-09-10: keep development temporary files off the Windows system drive.
  Run shell build/test/Python tools through `./tools/dev.ps1 <command> <args>` on Windows.
  This scopes TEMP/TMP/TMPDIR to the project `.tmp/dev-temp` (E: in this checkout), not the whole OS.
  PackageCheck also selects this root automatically even when invoked directly, isolates each run,
  loads the package DLL from a stream and cleans up test fixtures on normal completion.
  `SC_CSGO_DEV_TEMP` may override the root; `SC_CSGO_KEEP_TEST_TEMP=1` retains a check's fixtures for diagnosis.
  Never clean the user's global Temp, game worlds or backups as part of this workflow.

`tools/PackageCheck` needs no window or GPU. Its only host requirement was `Engine.Dispatcher.Initialize()`,
which `Program.cs` calls before loading the package (0.29.0): without it the finalizers of engine objects the
regressions create through `GetUninitializedObject` post to an uninitialized Dispatcher and kill the process
mid-run. On the VPS run
`dotnet tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll --scmod <pkg> --sha256 <sha> --vanilla-content <Content.zip> --json <out>`.
Do not add a "skip GPU tests" switch; nothing in the check set draws.

# Textures and threads

- `Engine.Graphics.Texture2D.Load` creates the GL object on the calling thread with no dispatch and no check, and `ContentManager` caches the result. Any texture a placeable block needs in `GenerateTerrainVertices` (terrain worker thread) must be resolved on the main thread first, in `Block.Initialize()`, and read from a field afterwards. A worker thread that first-touches `ContentManager.Get<Texture2D>` gets a broken texture that then draws black everywhere for the session (0.26.1-0.28.2 supply icons and the placed bench).
