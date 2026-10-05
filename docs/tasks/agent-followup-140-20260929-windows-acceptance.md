# Task agent-followup-140-acceptance: Windows visual and real-game acceptance of the 1.4.0 packages now in output/ (from f5-01)

Status: ready
Current writer: unassigned (VPS agent stopped writing after this note)
User-authorized scope: agent-followup-140-20260928 (F1–F5). The user had these packages replace output/ 1.4.0. Capture real-game evidence and report findings; installing into Mods/devices and writing original worlds still need a separate user decision.
Allowed paths: `output/` 1.4.0 packages and `.tmp/followup-140-20260928/f5-01/` evidence (read), a new capture folder under `.tmp/`, this note and the VPS results document (review section).
Out of scope: further output/ changes, installation, player worlds, public version changes, source edits without a file handoff.

## Reproduction and evidence

- Input packages (moved into `output/` on 2026-09-29 at the user's request, job 97c63f669bad438394a2d10673b9bb9b; same bytes):
  - Full: `[API1.9]CS武器1.4.0-全量包.scmod` 524897105 bytes, sha256 b60804f8fef2ebd621ced3a5dc903b62ccf2e66f05ea18a1bf055dd5852eaca5
  - Lite: `[API1.9]CS武器1.4.0-轻量包.scmod` 35462777 bytes, sha256 dd365605e2e7808760ae05f44544c656460561f8423cbbdeb71526d4cd14ac1b
  - Agents: `[API1.9]CS武器1.4.0-探员包.scmod` 37421897 bytes, sha256 9052dc954d160132e40fe6da757bfab8aac821364092b909be498d5e357d537c
- Offline evidence already viewed on the VPS: `f5-01/ui/*.png` (settings, damage HUD, throw preview), `f5-01/motion/*.png` (jump and corpse time series), `f5-01/hotspots/hotspots.json`. These are native offline renders, not game captures.
- Needed from Windows, in an isolated game copy/test world (not the player's worlds):
  1. F3 jump: timestamped recordings at 30/60/120 fps: standing jump, running jump, landing, side view; CT and T; rifle/shield/empty hands.
  2. F3 death: corpses on flat ground, a 1-block step, a wall corner and a slope; confirm drops/rewards happen once and bodies do not sink or jitter. Judge whether 3 s CorpseDuration is long enough.
  3. F2: enemies no longer start fights from far away; damage-direction wedges appear only when HP actually drops, in the right direction after turning, and never on split-screen partners.
  4. F4: preview visible only while preparing a throw; it disappears on cancel, weapon switch and menus; the real throw lands where predicted for HE/flash/smoke/molotov/decoy.
  5. F1: settings in the main menu (new-world defaults) and inside a world (that world's rules); Save/Cancel/Defaults; world rules survive save/exit/reload twice.
  6. The 360-px settings layout overlap (fixed contact note over the first list row) predates this task; decide whether to fix it.
- Unknowns: real frame time on PC/phone; batched companion wake-up cost (not measurable offline).

## Results and review

- Tests passed offline: see `agent-followup-140-20260928-vps-results.md` (round f5-01).
- Real-game / Android acceptance: not yet done.
- Next owner: Windows agent for acceptance.

This note is not an automatic lock, message dispatch or deployment authorization.
