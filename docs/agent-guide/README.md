# Agent instruction maintenance

Updated 2026-09-28 from official documentation fetched that day and the existing project requirements.

`AGENTS.md` is the single short project instruction source. `CLAUDE.md` imports it with `@AGENTS.md`. Specialist guides here are linked for reading only when their task applies; they are not automatically imported. Immutable historical snapshots are under `history/` and are not startup instructions.

## Official behavior checked

- [OpenAI: AGENTS.md discovery](https://learn.chatgpt.com/docs/agent-configuration/agents-md): root-to-current-directory instruction discovery; default combined project instruction limit is 32 KiB. The former 52,670-byte root file exceeded that default and put important newer synchronization rules near the end.
- [Anthropic: memory and AGENTS.md](https://code.claude.com/docs/en/memory#agents-md): native AGENTS.md discovery requires Claude Code v2.1.277+. With default settings, an ancestor/current CLAUDE.md or CLAUDE.local.md selects CLAUDE instructions instead; `@AGENTS.md` imports are supported and remain valid on current versions without double-loading the same file.
- VPS running Claude binary observed: v2.1.263. We use the explicit import for this older installation; no upgrade or global setting change was performed.
- [Anthropic: effective instructions](https://code.claude.com/docs/en/memory#write-effective-instructions) recommends targeting fewer than 200 lines per CLAUDE.md, specific actionable rules, and removing contradictions. Imports still consume startup context: importing the entire historical archive would not solve bloat.
- [Anthropic: best practices](https://code.claude.com/docs/en/best-practices#write-an-effective-claudemd) recommends broadly applicable instructions and linking detailed, task-specific material.

The standard filename is uppercase plural `AGENTS.md`, not `agent.md`. A normal Markdown link is not equivalent to Claude's explicit `@` import. Codex reads AGENTS.md; it does not need to interpret the CLAUDE import syntax.

## Resolved historical contradictions

| Previous mixed statements | Current treatment |
|---|---|
| Git-only vs Syncthing | Both Windows and VPS may use Git under the same branch/commit/merge workflow; Syncthing shares source, excludes each host's `.git`, and retains one writer |
| Old Obsidian workspace paths | `E:/projects/ScCsgoKnives` and `E:/projects/CSMCReverse` |
| Automatic backup-first vs manual backups | No automatic player-world backups; retain existing backups |
| Fixed v5/schema6 vs capacity revision | Current documented layout6/schema7 compatibility contract |
| M4-only/research-only/Full-only old task scope | Historical requests are not current task commands; preserve established invariants and follow the user's active scope |
| Historical 1.5/1.6/1.7 labels vs public 1.3.0 | Verify actual release identity; do not revive old labels |
| Full/Lite same DLL vs split optional type ownership | Preserve gameplay semantics; validate actual conditional assembly differences |
| Preserve every old output forever vs authorized cleanup | Keep required immutable fixtures/rollback inputs; ordinary scoped old output may be removed when authorized |

## Maintaining the files

- Keep durable project constraints in root AGENTS.md; put changing task status in `docs/tasks/`, implementation/release evidence in existing release documents.
- Update one current rule rather than prepending another contradictory override. Preserve the reasoning/history in a linked document where needed.
- Never bulk-import `history/`. Read it only to resolve specific provenance; prior statements remain historical where later user decisions supersede them.
- Check local links, file sizes, exact archive hashes, and both Syncthing peers after editing. Check other instruction sources before diagnosing an instruction conflict.
- For Claude, start a fresh session or explicitly reread the updated files in an existing session; use `/context` to inspect loaded memory files where supported. Do not restart another agent's live session automatically.
- `/doctor prompt-audit` is documented for v2.1.283+, so do not assume it exists on the observed VPS v2.1.263. Runtime prompt loading on that live agent was not tested by interrupting it.

Original files are preserved byte-for-byte as `history/AGENTS-before-20260928.md` and `history/CLAUDE-before-20260928.md`. They contain completed tasks and superseded policies; they are evidence, not an open backlog.
