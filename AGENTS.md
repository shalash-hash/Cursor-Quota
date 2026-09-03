# Quota — Cursor Agent Instructions

Before **substantive** work on this repository:

1. Read [`docs/AI_CONTEXT_INDEX.md`](docs/AI_CONTEXT_INDEX.md)
2. Read [`docs/MASTER_CONTEXT.md`](docs/MASTER_CONTEXT.md)
3. Read [`docs/HANDOFF_TO_CHATGPT.md`](docs/HANDOFF_TO_CHATGPT.md)
4. Read [`docs/DECISIONS_CHANGELOG.md`](docs/DECISIONS_CHANGELOG.md)
5. Read task-specific docs and relevant code paths from the index

Then:

- Classify the task: **A** (aligned) / **B** (implementation-only) / **C** (new decision) / **D** (ambiguous — ask user)
- Do **not** silently revert product decisions or rewrite canon to match code
- For **C**: update `DECISIONS_CHANGELOG.md` and affected canonical docs; include user-visible decision
- For **D**: stop and ask the user

**Full process:** [`docs/AI_COLLABORATION_PROTOCOL.md`](docs/AI_COLLABORATION_PROTOCOL.md)

**GitHub `main`** is shared source of truth. Read exact HEAD from Git, not from HANDOFF.

**Security:** never log or commit Cursor access/refresh tokens; never store tokens in `quota.db` or `ui-settings.json`.

**Commits/push:** only when the user explicitly asks.
