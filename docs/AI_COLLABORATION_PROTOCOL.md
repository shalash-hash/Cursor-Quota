# AI Collaboration Protocol

Универсальный процесс для **USER ↔ ChatGPT ↔ Cursor ↔ GitHub**.

**GitHub (`main`)** = shared source of truth для канона, решений и состояния реализации.

---

## Workflow

```
USER REQUEST
    ↓
READ CANON (AI_CONTEXT_INDEX → MASTER → HANDOFF → DECISIONS → task docs)
    ↓
COMPATIBILITY CHECK (classify A / B / C / D)
    ↓
DISCUSSION / DECISION (if C or D)
    ↓
IMPLEMENTATION (Cursor)
    ↓
TEST
    ↓
UPDATE CANON IF NEEDED (C only, or doc drift fix agreed with user)
    ↓
UPDATE HANDOFF IF NEEDED (semantic snapshot / local WIP)
    ↓
COMMIT / PUSH (only when user asks)
    ↓
CHATGPT READS GITHUB
```

---

## Before substantive work

**Cursor** and **ChatGPT** must read:

1. `docs/AI_CONTEXT_INDEX.md`
2. `docs/MASTER_CONTEXT.md`
3. `docs/HANDOFF_TO_CHATGPT.md`
4. `docs/DECISIONS_CHANGELOG.md`
5. Task-specific docs and relevant code

**Exact HEAD:** from Git / GitHub — not from HANDOFF text.

---

## Compatibility classification

| Class | Meaning | Action |
|-------|---------|--------|
| **A** | Fully aligned with canon | Implement |
| **B** | Implementation-only; canon unchanged | Implement; no DECISIONS entry |
| **C** | **New decision** — changes product semantics, architecture principle, UX contract, data contract, security policy | Get explicit user decision; update `DECISIONS_CHANGELOG.md` + affected docs; then implement |
| **D** | Conflict or ambiguity | **Stop.** Ask user. No silent choice. |

### Class C checklist

- New explicit user decision overrides stale docs.
- Mark **NEW DECISION** in discussion.
- Record: supersedes, reason, affected docs/code in `DECISIONS_CHANGELOG.md`.
- Update `MASTER_CONTEXT.md` if invariants changed.
- Update `HANDOFF_TO_CHATGPT.md` if next AI needs semantic context.

### Usually NOT canon changes (class B)

- Typos, formatting
- Refactor without behavior change
- Test stabilization
- Local bugfix preserving semantics
- Dependency bump without architectural impact

Still verify compatibility before work.

---

## ChatGPT-side rule

Before a substantial **Cursor prompt**, ChatGPT reads GitHub canon (see above).

If user and ChatGPT agree on a **conceptual decision**, the Cursor prompt must include:

**`UPDATE CANONICAL DOCUMENTATION`**

with what to update (`DECISIONS`, `MASTER`, `HANDOFF`).

Decisions must not live only in chat history.

---

## Cursor-side rule

See root [`AGENTS.md`](../AGENTS.md).

Summary:

- Read canon before substantive work.
- Classify A/B/C/D.
- No silent rollback of product decisions.
- No silent rewrite of MASTER to match code without user/C decision.
- Conceptual change → update docs in same session (or explicit follow-up task).

---

## Local WIP

GitHub does **not** see uncommitted work.

- Distinguish **canon on GitHub** vs **local WIP**.
- Record important WIP only in `HANDOFF_TO_CHATGPT.md`.
- Do not commit logs, binaries, secrets, dumps for handoff purposes.

---

## Git / security (all future commits)

**Never commit:**

- secrets, tokens, passwords, private keys
- `logs/`, `bin/`, `obj/`, `.vs/`
- local credential files
- large binaries without need

Private repo ≠ permission to store secrets.

---

## Conflict resolution

1. Latest explicit **user** decision  
2. `MASTER_CONTEXT.md`  
3. Newer profile authoritative doc  
4. **Code** = implementation fact, not automatic product law  
5. Historical / stale docs  

Code vs doc mismatch on **product** → class **D**, ask user.

---

## Commit / push policy

- **Default:** no commit/push unless user explicitly requests.
- After push: ChatGPT refreshes context from GitHub.
- HANDOFF updated when handing off between tools or sessions with meaningful semantic state.
