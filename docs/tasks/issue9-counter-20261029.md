# Issue 9 and third-person counter/material repair

Status: review (code and candidate ready; gameplay visual acceptance pending)
Current writer: Codex, isolated Windows worktree `E:/projects/ScCsgoKnives-issue9-counter`
User-authorized scope: 2026-10-29 “那你直接修吧”, following the diagnosis of issue #9 and CT/T teammate weapons.
Baseline: `4cd1560` (1.5.0). Branch: `fix/issue9-counter-20261029`.
Original synchronized checkout remains on main with its unrelated untracked files untouched.

## Requirements and acceptance

| Requirement | Implementation/check | Status |
|---|---|---|
| Stable creative category, translated label | One category key across core/tactical blocks, CSV and ordering; zh-CN/en-US lookup and real inventory filter tests | Passed automatically |
| Localized gun/knife item names including skin/counter suffixes | Existing variant language entries and localized finish labels; all 35 guns/22 knives/44 gun finishes | Passed automatically |
| CT/T teammate counter stays on the weapon | Actual world-animation parent, HD/legacy; idle/draw/reload, native aim/run/crouch/jump samples, two camera frames | 9,072 native samples passed; game visuals pending |
| Player third-person counter follows actions; drops stay correct | Actor adapter uses parent bone; vanilla animated frames and idle drop frames checked | Implemented; targeted checks passed; game visuals pending |
| Consistent gun skin/material appearance | Resolved albedo/normal/ORM, world-camera lighting, simple-material fallback; four native GPU previews | Passed native GPU checks; phone appearance/performance pending |
| No state/compatibility regression | No ID/save schema/gameplay changes; Full/Lite and save switching, network/appearance checks | No new failures; baseline exceptions below |

## Evidence and proposed changes

- Issue: https://github.com/zh667/ScCsgoKnives/issues/9 . Both locales already contain 35 gun and 22 knife entries. Knife base names already use LanguageControl; gun names bypass it.
- Engine CreativeInventoryWidget translates category labels through `BlocksManager`; filtering compares category keys. All CS category producers and the category sorter must use the same stable key.
- ComponentTacticalModel draws each weapon group using ScAgentActions.WorldPartFor, but its counter uses an idle first-person attachment with the world weapon root. The counter must follow its own configured parent bone.
- NPC shader currently samples only albedo and uses PointClamp for HD geometry. First person uses PBR maps and LinearWrap.
- Existing original assets are the source boundary. No AI assets, geometry redesign, provider modifications, installation or original-world writes.

## Verification and delivery

Use `tools/dev.ps1` for tooling. Start with source builds and targeted native/offline tests, then freeze one package candidate for applicable release gates. Keep Full/Lite/addon ownership and version 1.5.0. No merge into main or public release without specific approval.
Real Windows/Android gameplay and user visual acceptance are separate from offline checks; report their actual status.

## Results

- Implemented shared `ScCsgoWeapons` identity in constructors, the ammo override, compatibility/tactical blocks, category ordering **and the block CSV loaded after construction**. Updated the package pipeline to include locale/CSV bytes instead of keeping stale baseline members.
- Gun names resolve exact variant entries without LanguageControl's `:0` fallback (a missing name must not turn another gun into AK-47). Both locales cover all 44 gun finishes and knife finish/counter suffixes. Original Chinese emergency fallbacks remain where previously exposed when language data is absent.
- CT/T teammates and the optional CT/T player appearance adapter use the attachment's actual world-animation parent. Vanilla player third-person counters follow the current weapon action; drops retain their idle frame.
- NPC/CT/T player guns use the resolved albedo/normal/ORM stem, world-space lighting and existing full/simple shaders. World caches lack vertex normals: normals are reconstructed once per shared mesh with split vertices preserved and opposite-winding triangle copies deduplicated. Geometry/UV/cache bytes are unchanged; this is not a claim that reconstructed normals equal the source's authored smooth normals.
- Player rendering uses the bounded NPC geometry cache; it must not retain successive copies evicted by the general 12-entry held-weapon cache. GPU buffers remain renderer-owned, with device reset and world-exit checks.
- Initial focused native run: 10,917 checks passed. Expanded provisional candidate run: 10,926 passed, including 9,072 CT/T parent-frame samples across HD/legacy guns, posture/action/time/camera combinations. Maximum point error 0.0000027 m; old root-only path exceeded 1 cm in 208 samples, up to 1.398 m. These are offline native transforms, not measured in-game surface contact.
- First full gate exposed stale category assertions and the CSV override; both addressed. Main smoke-view failures were reproduced against unchanged 1.5.0 Full and Lite. The four tactical-suite failures are the existing `KNOWN_TACTICAL` cases in `video_fix_140.py`; none are claimed as passing.
- Harness setup failures retained: source CT/T cache had no crouch clips, so the probe now reads actual published actor/animation resources; GLB input required a seekable stream; Python's default GBK decoding failed on dotnet output (rerun with `PYTHONUTF8=1`). Source/core builds succeeded; final candidate gates are in progress.
- Final native presentation runner: **10,926 passed, 0 failed**, running API 1.9.3.1 and the DLLs extracted from the final Full candidate. It checks localized names/category changes, parent frames, materials, device reset, independent camera queues, world disposal/re-entry, and skin/counter item transactions. Renderer crowd checks use a synthetic mesh and include GPU readback; they are not game FPS or phone performance measurements.
- Main suites: Full 14,203 / Lite 14,512 cases, each with exactly the same 3 smoke-view failures as its unchanged baseline. AI 92 per edition, video-feedback 292 per edition, C4 140 per edition passed. The broad tactical suite has 142 cases, with the 4 explicitly known failures retained.
- Save/compatibility: family 251, 1.4.0 switching 249, initial official 1.5.0 switching 251, **current 2026-10-21 Full/Lite switching 253** passed; native hooks, inventory and six optional-provider combinations passed. No claim of exhaustive switching against every historical same-version intermediate package.
- Multiplayer offline checks: CompatNet and non-CompatNet transport 28 each, appearance synchronization 19 per edition passed. No two-process gameplay session was run.
- API 1.9.4, 1.9.5, 1.9.5.1: official dependency hashes verified; binary contract and Full/Lite main/AI/video-feedback suites have no new failures or missing cases relative to 1.9.3.1. These are native/headless checks, not full-game sessions.
- Final localization-only correction: MP5-SD paint 923 is **Oxide Oasis**, verified in `E:/projects/CSMCReverse/local_cs2_analysis/all_weapons/10_paints/selected_20260908/evidence/resource/csgo_english.txt:37996`. Repack changed only `Assets/Lang/en-US.json` in Full/Lite; **every other archive member, including all binaries, was proved identical**. Binary/API/save evidence therefore remains applicable; the presentation runner was rerun against the final package hash.
- Candidate packages and manifest: `E:/projects/ScCsgoKnives-issue9-counter/output/`. Compact evidence: [verification.json](issue9-counter-20261029/verification.json). Actual raw logs/results remain in `.tmp/dev-temp/issue9-candidate/` and are SHA-256 referenced by the summary. Source/build/package identity and per-step elapsed seconds are recorded there. The final normal build took 30.70 s, package 44.58 s, baseline comparison 100.52 s, common gates 127.47 s, compatibility 33.14 s and newer-API suites 336.94 s; source investigation time was not separately measured. Earlier failed runs and harness problems are described above.
- No formal package in the original checkout was replaced, no package installed, and no player world/original asset/third-party provider changed. This is a review candidate, not a published release; full-game and Android visuals/performance and release-time gameplay gates remain unexecuted.
- Cleanup of 2,039,349,154 bytes of reproducible build copies was rejected by automatic approval before execution (`blocked by policy`). No deletion occurred; [cleanup receipt](issue9-counter-20261029/cleanup.md) lists the retained paths.

## User observation points

1. Switch Chinese/English and open the creative CS category: one category, translated base/finish/counter names, all entries still filter correctly.
2. Give CT and T teammates a counter-equipped weapon: watch idle, movement and reload; the module must remain on the gun. Also check a CT/T player appearance and a dropped gun.
3. Compare the same paint in first/third person, then toggle simplified materials. Camera/light differences are expected; incorrect UV tiling, detached counters and unrelated geometry are not.
