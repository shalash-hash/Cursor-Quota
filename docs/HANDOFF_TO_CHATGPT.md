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
| Tracking | `main` up to date with `origin/main` after last push |
| Last doc review | 2026-09-05 |

---

## Tracked changes status

At last doc review: **committed and pushed** — reset countdown, network recovery, refresh diagnostics, tray menu, prior bonus/Model C WIP.

---

## Recently implemented (product-relevant)

- **Reset countdown:** `billingCycleEnd` Unix ms (same source as Cursor Plan & Usage); `BillingCycleTimestamp.ComputeRemaining`; formatted h+m under 24h; `RESET_TIME_DIAGNOSTIC` in log.
- **Network recovery:** HTTP 403 → transport reset + recovery loop (1s/10s); scheduler pause; `CursorHttpTransport` / `CursorHttpRetry`.
- **Refresh failure UI:** structured error + reason under «Последнее обновление»; `REFRESH_FAILED` log; `CursorRefreshFailureDescriber`.
- **Tray menu:** combined base spend; Models bonus line; API as `$used из $limit — X%`.
- **Daily plan:** fixed 21-calendar-day Models accelerated phase; reserve tail until real reset.
- **Auth:** `CursorAuthService` re-reads `state.vscdb` on every refresh.
- **Bonus quota (Model C):** `totalSpend` = combined actual; `modelsActual = totalSpend − apiUsed`; combined card base-only; `remainingBonus=false` → Unknown.
- **History UI:** separate stretchable chart cards.

---

## Important product decisions (pointer)

Full log: [`DECISIONS_CHANGELOG.md`](./DECISIONS_CHANGELOG.md)

Key invariants: reset countdown from `billingCycleEnd` ms (Cursor DKf formula); combined **base** quota in dollars; billing day from cycle start; 21-day Models phase + variable reserve; `PeriodEnd` exclusive at rollover; daily plan is recommendation; `bonusSpend` (raw) ≠ Models bonus used; `remainingBonus=false` ≠ exhausted; HTTP 403 recovery without spamming API every second on normal path; no tokens in logs/DB/settings.

---

## Implementation notes for next AI

- Composition root: `App.xaml.cs` — all services wired manually (no DI container).
- `MockQuotaUsageProvider` exists for dev/tests; production uses `CursorQuotaUsageProvider`.
- Tests: `dotnet test Quota.Tests/Quota.Tests.csproj -c Release` (**235** tests).
- Release exe: `bin/Release/net10.0-windows/Quota.exe`
- User often requests: run tests → rebuild Release → stop old Quota → start new exe.

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

None at last doc review. If WIP appears later: describe here only what matters for the next AI.

---

## Safety / constraints

- Do not commit secrets, tokens, `logs/`, `bin/`, `obj/`.
- Do not change quota math / auth behavior without explicit user request and canon update (class C).
- Trust `MASTER_CONTEXT` and `DECISIONS_CHANGELOG` over stale chat history.
