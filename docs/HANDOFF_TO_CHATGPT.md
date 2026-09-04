# Handoff to ChatGPT

Семантический снимок для продолжения работы. **Не** полная история чатов.

**Exact current HEAD:** READ FROM GIT / GITHUB (`git rev-parse HEAD` on `main`).

---

## Repository

| Field | Value |
|-------|-------|
| Root | `D:\_APP\Quota` |
| Branch | `main` |
| Remote | `origin` → `https://github.com/shalash-hash/Cursor-Quota.git` |
| Tracking | `main` up to date with `origin/main` (at last bootstrap) |
| Last doc review | 2026-09-03 (bootstrap final check) |

---

## Tracked changes status

At last doc review: **uncommitted** — bootstrap docs + 21-day daily plan refactor. Last pushed commit: models remaining USD (see GitHub for exact SHA).

---

## Recently implemented (product-relevant)

- **Daily plan:** fixed 21-calendar-day Models accelerated phase (`cycleStart` = day 1 → last Phase 1 = `cycleStart + 20`; Reserve from `cycleStart + 21`); reserve tail spreads remaining Models + API until real reset via `CountRemainingDays` (replaces realReset−5 model and old `+1` spread formula).
- **Auth:** `CursorAuthService` re-reads `state.vscdb` on every refresh.
- **History UI:** separate stretchable chart cards.
- **Models card:** `Осталось: $X` in dollars; **base quota** vs **bonus** shown separately after 100%.
- **Bonus quota (Model C):** raw `totalSpend` = combined actual period spend (может включать API после spillover). `modelsActual = totalSpend − apiUsed`; `ModelsBonusUsedUsd = max(0, modelsActual − frozen base)`; **не** `totalSpend − base`. `combinedActual = totalSpend` (не `models + api` повторно). `bonusSpend` raw ≠ Models bonus. Combined card — base-only fraction; `remainingBonus=false` → Unknown.
- **Ahead/behind:** relative % vs daily plan.

---

## Important product decisions (pointer)

Full log: [`DECISIONS_CHANGELOG.md`](./DECISIONS_CHANGELOG.md)

Key invariants: combined **base** quota in dollars (bonus excluded from main fraction); billing day from cycle start; **21 calendar days Models phase** (06.09–26.09 for 06.09→06.10 cycle) + variable reserve tail; `PeriodEnd` exclusive at rollover instant; daily plan is recommendation (bonus with unknown $ allowance excluded from $ plan); ahead/behind = relative %; `bonusSpend` (raw) ≠ Models bonus used; `remainingBonus=false` ≠ exhausted; `state.vscdb` auth each refresh; no tokens in logs/DB/settings.

---

## Implementation notes for next AI

- Composition root: `App.xaml.cs` — all services wired manually (no DI container).
- `MockQuotaUsageProvider` exists for dev/tests; production uses `CursorQuotaUsageProvider`.
- Tests: `dotnet test Quota.Tests/Quota.Tests.csproj -c Release` (173 tests at last run).
- Release exe: `bin/Release/net10.0-windows/Quota.exe`
- User often requests: run tests → rebuild Release → stop old Quota → start new exe. **Do not commit/push unless explicitly asked.**

---

## Authoritative docs (read order)

1. `docs/AI_CONTEXT_INDEX.md`
2. `docs/MASTER_CONTEXT.md`
3. This file
4. `docs/DECISIONS_CHANGELOG.md`
5. `docs/AI_COLLABORATION_PROTOCOL.md`
6. `AGENTS.md` (Cursor)

---

## Local-only WIP

**Daily plan refactor** (21-day accelerated phase + reserve tail) and doc sync. Not committed.

If WIP appears later: describe here only what matters for the next AI (semantic intent, not file dumps).

---

## Safety / constraints

- Do not commit secrets, tokens, `logs/`, `bin/`, `obj/`.
- Do not change quota math / auth behavior without explicit user request and canon update (class C).
- Trust `MASTER_CONTEXT` and `DECISIONS_CHANGELOG` over stale chat history.
