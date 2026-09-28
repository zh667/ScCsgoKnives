# Windows / VPS collaboration

Current workflow, agreed 2026-09-28. Roles are defaults; the user's current task can assign work differently.

## Roles and locations

- Windows desktop agent is the main user-facing entry for screenshots, recordings, planning, review and acceptance.
- VPS agent investigates source code and implements scoped changes. Windows visual analysis supplies evidence and hypotheses, not an unquestionable diagnosis.
- Windows worker runs resource parsing, conversion, full builds and authorized device diagnostics. It is a process executor, not another AI.
- Windows project: `E:/projects/ScCsgoKnives`; source assets: `E:/projects/CSMCReverse`.
- VPS active project: `/home/dev/source-sync/ScCsgoKnives`; source/research text: `/home/dev/source-sync/CSMCReverse`.
- `/home/dev/workspaces/*` are legacy trees, not current edit/build roots. Their bulk resources and old outputs were partly removed during authorized cleanup.

## Handoff

1. Put a task in `docs/tasks/<task-id>.md`, using [TASK_TEMPLATE.md](TASK_TEMPLATE.md). Keep observed facts, suspected causes and unresolved questions separate.
2. Record the current owner, allowed files, input package/version/hash and acceptance criteria. A task file is coordination information, not a lock or new user authorization.
3. Before switching writers, the current writer stops edits; verify both Syncthing peers are idle with no errors, pending files/deletes or conflict files. Never concurrently edit the same files across peers.
4. VPS reads the task and required specialist instructions, verifies hypotheses against code, then implements only the authorized scope.
5. Record changed paths, job IDs, test results and remaining gaps. Windows reviews the actual diff and build inputs, then performs the authorized visual/device checks.
6. Do not claim automatic dispatch, completion notifications, agent-to-agent messaging or deployment is configured merely because a document exists. Use only an available and authorized mechanism; otherwise give the user the exact task path to pass along.

## Pictures and recordings

- Keep original screenshots/video on Windows. The task stores absolute paths, capture/build identity, reproduction steps and timestamps. Do not sync large media into VPS source directories.
- For video, create timestamped keyframes/crops on Windows. Use denser frames, logs and audio around the failure when needed. Sparse stills do not establish frame pacing, transient animation correctness or audio sync.
- VPS may call MCP `read_media_file` through windows-files/windows-worker. Current bridge accepts PNG/JPEG/WebP/GIF up to 2 MiB per image; create bounded previews locally if needed.
- Image bytes must enter the model/tool pipeline for visual analysis. “No raw media copied to VPS disk” does not mean no transfer or no retention in conversation history.
- CLI JSON/base64 output and a successful file read are not proof the agent saw an image. Claim visual inspection only after the current session actually receives usable image content and inspects it.
- Model parsing through the worker was verified; VPS model-session image understanding and native video input were not fully accepted. Report that boundary instead of guessing.
- For each proposed fix, request or collect evidence that can disprove the hypothesis. Desktop findings remain provisional until runtime/code checks confirm the cause.

## Windows execution

Check `win-worker health` on VPS first. CLI operations include `stat`, `read`, `submit`, `job`, `log`; MCP counterparts include `get_file_info`, `read_text_file`, `submit_job`, `job_status`, `job_log`.

Submit argv arrays or small scripts with an explicit Windows `cwd`, timeout and stable unique `request_id`. Query the original job after reconnecting; do not blindly resubmit a mutation. Use the project's `tools/dev.ps1` wrapper for builds/tests/converters. Pin inputs before a release build and keep outputs on Windows.

Normal Linux `open()` cannot read an `E:` path. If the worker is unavailable, report the error and continue independent source work. Do not silently download the raw resource tree, substitute placeholders or use stale VPS copies. Windows must be online; the bridge does not supply general GUI automation.

Source-sync policy is `.stignore.shared`; `.psh` and `.xdb` are included. `.git`, packages, models, textures, media, caches, temporary stages and bulk AnimationData remain local. Unknown extensions are excluded by default. Small JSON/CSV can still grow large; the policy is not a disk quota. Put shareable summaries in `docs/`, not excluded `output/`.

For deployment details see [source-sync-2026-09-28.md](../source-sync-2026-09-28.md) and [windows-resource-execution-2026-09-28.md](../windows-resource-execution-2026-09-28.md). Older “legacy resources preserved” statements describe the original migration, not post-cleanup inventory. Windows cleanup receipts are in `E:/projects/vps-offload-20260928/README.md`.
