# AI Context Index

**ENTRY POINT:** этот файл.

После открытия читать в порядке:

1. [MASTER_CONTEXT.md](./MASTER_CONTEXT.md)
2. [HANDOFF_TO_CHATGPT.md](./HANDOFF_TO_CHATGPT.md)
3. [DECISIONS_CHANGELOG.md](./DECISIONS_CHANGELOG.md)

Затем — профильные материалы и код по задаче.

---

## Shared source of truth

- **GitHub repository** (`origin`, branch `main`) — единый канон для ChatGPT, Cursor и пользователя.
- **Historical / stale docs** не перекрывают `MASTER_CONTEXT.md`.
- **Exact Git HEAD** не хранить в `HANDOFF_TO_CHATGPT.md` — читать из Git / GitHub.
- **Local uncommitted WIP** не считать каноном; фиксировать в HANDOFF только если важно следующему AI.

---

## THEN BY TASK

| Область | Authoritative docs | Code / paths |
|--------|-------------------|--------------|
| **Product canon & invariants** | `MASTER_CONTEXT.md`, `DECISIONS_CHANGELOG.md` | — |
| **Current semantic snapshot** | `HANDOFF_TO_CHATGPT.md` | — |
| **Collaboration process** | `AI_COLLABORATION_PROTOCOL.md` | `AGENTS.md` |
| **Overview (human)** | `README.md` | — |
| **WPF UI / layout** | `MASTER_CONTEXT.md` § UI | `MainWindow.xaml`, `App.xaml`, `Controls/**` |
| **ViewModel / presentation** | `MASTER_CONTEXT.md` § Architecture | `ViewModels/MainViewModel.cs` |
| **Quota fetch & Cursor API** | `MASTER_CONTEXT.md` § Data sources, § Reset countdown | `Services/CursorQuotaUsageProvider.cs`, `Services/CursorApi/**`, `Services/CursorPlanUsageMapper.cs`, `Helpers/BillingCycleTimestamp.cs` |
| **Network recovery (HTTP 403)** | `MASTER_CONTEXT.md` § Network recovery | `Services/CursorNetworkRecoveryService.cs`, `Services/CursorHttpTransport.cs`, `Services/CursorHttpRetry.cs` |
| **Refresh failure UX** | `MASTER_CONTEXT.md` § Refresh failure diagnostics | `Services/CursorRefreshFailureDescriber.cs`, `ViewModels/MainViewModel.cs` |
| **Tray display** | `MASTER_CONTEXT.md` § Tray menu | `Helpers/TrayDisplayFormatter.cs`, `Services/TrayIconService.cs` |
| **Auth (Cursor IDE tokens)** | `MASTER_CONTEXT.md` § Security | `Services/CursorAuthService.cs`, `Services/CursorAuthStateReader.cs` |
| **Quota & daily plan math** | `DECISIONS_CHANGELOG.md` (2026-09 fixed 21-day phase), `MASTER_CONTEXT.md` § Domain | `Services/QuotaCalculator.cs`, `Services/DailyPlanCalculator.cs`, `Helpers/DailyTargetProgressCalculator.cs` |
| **Usage history & charts** | `MASTER_CONTEXT.md` § Features | `Services/UsageHistoryService.cs`, `Services/QuotaSnapshotRepository.cs`, `Controls/UsageHistoryChart.*` |
| **Local persistence** | `MASTER_CONTEXT.md` § Persistence | `Services/QuotaSnapshotRepository.cs`, `Services/UiSettingsService.cs`, `Models/QuotaSnapshot.cs` |
| **Localization** | `MASTER_CONTEXT.md` § i18n | `Localization/**`, `Resources/Strings*.resx` |
| **Tray & startup** | `MASTER_CONTEXT.md` § Platform | `Services/TrayIconService.cs`, `Services/StartupService.cs` |
| **Theming** | — | `Services/ThemeService.cs`, `App.xaml` |
| **Diagnostics / logging** | `MASTER_CONTEXT.md` § Security, § Reset countdown | `Services/QuotaDiagnosticLogger.cs` |
| **Tests** | `MASTER_CONTEXT.md` § Testing | `Quota.Tests/**` |
| **Build & release** | `README.md`, `MASTER_CONTEXT.md` § Stack | `Quota.csproj`, `dotnet build -c Release` |

---

## Cursor persistent instructions

См. корневой [`AGENTS.md`](../AGENTS.md) — краткое обязательное правило перед существенной работой.

Полный процесс: [`AI_COLLABORATION_PROTOCOL.md`](./AI_COLLABORATION_PROTOCOL.md).
