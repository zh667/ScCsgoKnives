# Task video-feedback-acceptance: Windows visual and real-game acceptance of the 1.4.0 packages now in output/ (from r2-06)

Status: ready
Current writer: unassigned (VPS agent stopped writing after this note)
User-authorized scope: video-feedback-20260929 (S0, R1–R4). The user had these packages replace output/ 1.4.0 ("可以放到output里，剪切过去只留一份"). Capture real-game evidence and report findings; installing into Mods/devices and writing original worlds still need a separate user decision.
Allowed paths: `output/` 1.4.0 packages and `.tmp/video-fix-140-20260929/r2-06/` evidence (read), a new capture folder under `.tmp/`, this note and the VPS results document (review section).
Out of scope: further output/ changes, installation, player worlds, third-party packages, public version changes, source edits without a file handoff, deleting stages without the user's decision.

## Reproduction and evidence

- Input packages (moved into `output/` on 2026-09-29, job 137478818f664ced8a36be1dd537342e; same bytes as the r2-06 candidate):
  - Full: `[API1.9]CS武器1.4.0-全量包.scmod` 525401431 bytes, sha256 9219228aa6e8203cf51c850dde2e9ad354497949bdb5170fd4e47191e0be6c66
  - Lite: `[API1.9]CS武器1.4.0-轻量包.scmod` 35478140 bytes, sha256 6d9ed6ea31e03a9b43411dd54b5fe53c9f93ad967ab455a4aa7746a41a00f319
  - Agents: `[API1.9]CS武器1.4.0-探员包.scmod` 37704606 bytes, sha256 e92811fa9d9d728df949793669ece2d331fdac9847136f2c43e417522bc4fa0f
- Offline evidence (native offline renders and headless tests, not game captures), under `.tmp/video-fix-140-20260929/r2-06/`: `throw/throw.json` and `throw/sheet-*.jpg`, `motion/motion.json`, `ui/*.png` and `ui/preview.txt`, `vf-full.json` / `vf-lite.json` (per-frame input logs in each check's detail), `ai-full.json` / `ai-lite.json`, `hotspots/hotspots.json`, `packages.json`.
- Needed from Windows, in an isolated game copy/test world (not the player's worlds), recordings with timestamps:
  1. R1 fall: 2 s, 5 s and 10 s falls, a fall with the flight toggle in the middle, landing; CT and T; standing and moving take-off. The legs must settle into one falling pose and not cycle.
  2. R1 R8: with an on-screen key/mouse display. Short clicks must never fire; a held trigger fires after the cock and keeps the 0.5 s rule; releasing early cancels; the fan (alternate) after a cocked shot waits its own cycle. Check that the hammer and the shot animation are both visible.
  3. R2: six throwables, strong and weak, as CT, T, vanilla male and vanilla female, seen from the side, front and back in third person. Include the last one of a stack and a creative stack: after the release the hand must be empty until the action ends. Also crouching, running and jumping, which were not checked offline.
  4. R2 origin: in third person the grenade must leave the thrower, and land where the preview says.
  5. R3: enemies start fights at about 32 blocks (snipers 36), not from 64; a summoned squad at the farthest position faces the summoner. Survival: the direction mark appears on real damage only. Creative: the weaker preview mark appears and can be switched off in the settings.
  6. R4: the orange ribbon and ball in daylight, at night, over sand/grass/snow, near and far, on a phone-sized window; a cut path must not look like a landing point. Judge the width (4 px at 1080p) and the corner at the apex in the thrower's own view.
  7. S0: in a world without the Slower Creature Spawns package (or with a creature limit of at least 3), natural squads appear after the waiting days; with the package installed the settings page says why they cannot.
- Unknowns: whether the incendiary/smoke and decoy/flashbang third-person meshes are meant to be the same geometry (sizes are identical in `throw.json`); behaviour with third-party character skeletons; real frame time on PC/phone.

## Results and review

- Tests passed offline: see `video-feedback-20260929-vps-results.md` (round r2-06).
- Real-game / Android acceptance: not yet done.
- Pending user decisions: S0 strategy A/B and terrain relaxation; pruning of superseded stages (about 19.5 GB under `.tmp/video-fix-140-20260929/`).
- Next owner: Windows agent for acceptance.

This note is not an automatic lock, message dispatch or deployment authorization.
