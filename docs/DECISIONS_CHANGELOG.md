# Decisions Changelog

Журнал **концептуальных** решений. Не дублирует `git log`.

Формат записей: Date | Decision | Supersedes | Reason | Affected docs | Affected code | Status

---

## 2026-03 — Combined total quota uses dollar sum of pool limits

| Field | Value |
|-------|-------|
| **Decision** | Общий процент/прогресс «всей квоты» считается от **суммы долларовых лимитов** (Models + API), не от max/суммы процентов по пулам. |
| **Supersedes** | Наивное объединение процентов пулов |
| **Reason** | Соответствие реальным лимитам Cursor ($450 + $20 и т.п.) |
| **Affected docs** | MASTER_CONTEXT § Combined total |
| **Affected code** | `QuotaMonetaryHelper.ResolveCombined*`, `CursorPlanUsageMapper`, `MainViewModel` |
| **Status** | Active |

---

## 2026-03 — Billing day anchored to Cursor cycle start time

| Field | Value |
|-------|-------|
| **Decision** | «Сегодня» / «вчера» для расхода — сутки от **времени начала billing cycle**, не от полуночи. |
| **Supersedes** | Calendar-midnight day boundaries |
| **Reason** | Cursor billing period не совпадает с локальной полуночью |
| **Affected docs** | MASTER_CONTEXT § Billing day |
| **Affected code** | `BillingCycleCalendar`, `QuotaSnapshotRepository`, spend aggregation |
| **Status** | Active |

---

## 2026-03 — Last partial day before reset counts as full day

| Field | Value |
|-------|-------|
| **Decision** | Неполный последний день до сброса учитывается как **полный день** в расчёте оставшихся дней плана. |
| **Supersedes** | Strict fractional last day |
| **Reason** | Согласованность дневного плана с UX ожиданиями |
| **Affected docs** | MASTER_CONTEXT § Daily plan |
| **Affected code** | `BillingCycleCalendar.CountRemainingDays`, `DailyPlanCalculator` |
| **Status** | Active |

---

## 2026-02 — Accelerated model plan early cycle; API reserve last 5 days

| Field | Value |
|-------|-------|
| **Decision** | Ранний цикл: ускоренный план для моделей; API — резерв в конце периода. Детали уточнены записью 2026-03 ниже. |
| **Supersedes** | Single linear daily plan for all pools |
| **Reason** | Разная семантика пулов Cursor |
| **Affected docs** | MASTER_CONTEXT § Daily plan |
| **Affected code** | `DailyPlanCalculator`, `QuotaCalculator` |
| **Status** | **Superseded** (см. 2026-09 — Fixed 21-day Models accelerated phase) |

---

## 2026-03 — Cursor Models hill plan & API reserve (realReset−5 model)

| Field | Value |
|-------|-------|
| **Decision** | **Два отдельных пула:** Cursor Models и API. **Граница цикла** — реальный reset Cursor (`periodEnd`). **Ускоренная фаза Models:** от `cycleStart` до `realReset − 5 календарных дней` (`GetCursorPlanEnd`); в этой фазе дневной план стремится израсходовать **только** остаток **Cursor Models** (hill-план с догоняющей/тормозящей коррекцией: отстаёте — план растёт, опережаете — уменьшается). **API в ускоренную фазу не входит** — `CalculateApiDailyPlan` = 0, combined plan = только Models. **Резерв API:** последние **5 дней** до real reset; Models-план в резерве = 0 (остаток Models без новой дневной рекомендации), API распределяется линейно на оставшиеся дни до reset. **Дневной план — рекомендация**, не жёсткий лимит. Продуктовая цель — основной расход Models в первые ~3 недели цикла; в коде граница ускоренной фазы = `realReset − 5`, не фиксированные 21 суток. |
| **Supersedes** | 2026-02 — Accelerated model plan early cycle; API reserve last 5 days (неточная формулировка «~21 день + линейный floor» без разделения пулов) |
| **Reason** | Основной объём Models — в начале цикла; API — резерв на хвост; API не должен участвовать в ускоренном combined-плане |
| **Affected docs** | MASTER_CONTEXT § Daily plan |
| **Affected code** | `DailyPlanCalculator`, `QuotaCalculator`, `DailyTargetProgressCalculator` |
| **Status** | **Superseded** (см. 2026-09 — Fixed 21-day Models accelerated phase) |

---

## 2026-09 — Fixed 21-day Models accelerated phase + variable reserve tail

| Field | Value |
|-------|-------|
| **Decision** | **Фаза 1 (ускоренная):** ровно первые **21 календарный день** от `cycleStart` (день 1 = `cycleStart`); `acceleratedEndInclusive = min(cycleStart + 20 days, realResetDate)`; Phase 1 при `today <= acceleratedEndInclusive`. Пример 06.09→06.10: Phase 1 = 06.09–26.09, Reserve с 27.09. Дневной план **только Cursor Models** (hill); `apiDailyPlan = 0`. **Фаза 2 (резерв):** `today > acceleratedEndInclusive` до `realResetDate`. Остаток Models и API: `remaining / CalculateRemainingPlanDays(today, realResetDate)` (= `BillingCycleCalendar.CountRemainingDays`, `Ceiling(periodEnd − now)`). `realResetDate` — exclusive instant rollover; календарный день reset до rollover ещё 1 расходный день. Combined = сумма пулов. Ahead/behind и plan completed — см. запись «Combined daily plan comparisons use USD canon». |
| **Supersedes** | 2026-03 — Cursor Models hill plan & API reserve (realReset−5, API-only last 5 days, Models=0 в резерве) |
| **Reason** | Основной расход Models за первые 3 недели; остаток Models + API распределяются в хвосте до reset |
| **Affected docs** | MASTER_CONTEXT § Daily plan |
| **Affected code** | `DailyPlanCalculator`, `QuotaCalculator`, tests |
| **Status** | **Active** |

---

## 2026-09 — Combined daily plan comparisons use USD canon

| Field | Value |
|-------|-------|
| **Decision** | Для combined Models + API: ahead/behind, **plan completed** и **today %** сравнивают/выводят факт в **USD** (`ResolveTodayUsageUsd`, `ResolveCombinedTodayPercent`, `ResolveDailyPlanUsd`). Pool-проценты не смешивать как источник истины. Combined % — только для отображения (USD / combined limit). |
| **Supersedes** | Percent-based `TodayTotalUsedPercent >= Total.DailyTarget` для combined plan completed |
| **Reason** | Models и API имеют разные процентные базы; смешанные % давали ложное «опережение» и «план выполнен» |
| **Affected docs** | MASTER_CONTEXT § Daily plan |
| **Affected code** | `QuotaMonetaryHelper`, `DailyTargetProgressCalculator`, `QuotaCalculator`, `MainViewModel` |
| **Status** | Active |

---

## 2026-03 — Combined daily target ahead/behind vs plan (relative %)

| Field | Value |
|-------|-------|
| **Decision** | На карточке дневного плана ahead/behind показывается как **относительный %** к плану: `(today − plan) / plan × 100`, не разница в процентных пунктах. |
| **Supersedes** | Percentage-point difference display |
| **Reason** | Понятнее пользователю («на 20% выше плана») |
| **Affected docs** | MASTER_CONTEXT § Daily plan |
| **Affected code** | `DailyTargetProgressCalculator`, `MainViewModel` |
| **Status** | Active |

---

## 2026-03 — First launch language from Windows UI languages

| Field | Value |
|-------|-------|
| **Decision** | При первом запуске язык = первый из `GetUserPreferredUILanguages`; ручной выбор сохраняется в `ui-settings.json` (`languageChosenByUser`). |
| **Supersedes** | Fixed default locale only |
| **Reason** | Локализация без лишнего шага для пользователя |
| **Affected docs** | MASTER_CONTEXT § Main user flows |
| **Affected code** | `LocalizationService`, `UiSettings`, `SystemCultureHelper` |
| **Status** | Active |

---

## 2026-03 — Usage history from local snapshots

| Field | Value |
|-------|-------|
| **Decision** | История расхода строится из **локальных SQLite snapshots** (периодический save при refresh), не из отдельного history API. |
| **Supersedes** | — |
| **Reason** | API даёт текущий период; история нужна локально |
| **Affected docs** | MASTER_CONTEXT § Usage history |
| **Affected code** | `QuotaSnapshotRepository`, `UsageHistoryService`, `UsageHistoryChart` |
| **Status** | Active |

---

## 2026-03 — Cursor auth: state.vscdb is source of truth

| Field | Value |
|-------|-------|
| **Decision** | На каждом refresh перечитывать `cursorAuth/accessToken` и `cursorAuth/refreshToken` из `state.vscdb`; in-memory cache синхронизировать; logout в IDE → ошибка auth без использования stale JWT. OAuth refresh только при expired access и наличии refresh в DB. |
| **Supersedes** | Cache access token until JWT expiry without re-read |
| **Reason** | Sign out / account switch в Cursor без перезапуска Quota |
| **Affected docs** | MASTER_CONTEXT § Security |
| **Affected code** | `CursorAuthService`, `CursorAuthStateReader`, `CursorQuotaUsageProvider` |
| **Status** | Active |

---

## 2026-03 — Models card shows remaining USD

| Field | Value |
|-------|-------|
| **Decision** | В карточке «Модели Cursor» показывать `Осталось: $X` = `max(0, ModelsEstimatedLimitUsd − ModelsUsedUsd)` из точных decimal, не из UI strings. |
| **Supersedes** | — |
| **Reason** | Быстрая оценка остатка в долларах |
| **Affected docs** | MASTER_CONTEXT § Main user flows |
| **Affected code** | `QuotaMonetaryHelper.ResolveModelsRemainingUsd`, `MainViewModel`, `MainWindow.xaml` |
| **Status** | Active |

---

## 2026-03 — Usage history charts: separate cards, stretchable

| Field | Value |
|-------|-------|
| **Decision** | Два графика истории — **отдельные карточки** с собственными легендами; высота **растягивается** с окном (не фиксированная). |
| **Supersedes** | Single combined chart panel |
| **Reason** | Визуальное разделение daily vs cumulative |
| **Affected docs** | MASTER_CONTEXT § Usage history |
| **Affected code** | `MainWindow.xaml`, `UsageHistoryChart` + `UsageHistoryChartSection` |
| **Status** | Active |

---

## 2026-09 — Bonus quota as separate layer with frozen Models base limit

| Field | Value |
|-------|-------|
| **Decision** | Bonus — отдельный quota layer с `BonusSource` (Models/API/Unknown). `bonusSpend` ≠ bonus allowance. Base Models limit оценивается до 100% и **замораживается** после; excess = Models bonus used. Unknown bonus total нельзя выдумывать в UI/daily plan. |
| **Supersedes** | `ModelsEstimatedLimitUsd` рос вместе с spend после 100% |
| **Reason** | Live Cursor API: bonus после исчерпания base ~$450 — free provider usage (~$10), не увеличение лимита |
| **Affected docs** | MASTER_CONTEXT § Пулы квоты, HANDOFF |
| **Affected code** | `QuotaBonusHelper`, `ModelsBaseLimitResolver`, `QuotaUsageEnricher`, `QuotaSnapshotRepository`, `MainViewModel`, `QuotaMonetaryHelper` |
| **Status** | Active |

---

## 2026-09-04 — Bonus semantics: base fraction vs bonus display; remainingBonus≠exhausted

| Field | Value |
|-------|-------|
| **Decision** | Combined card: progress / `$used из $limit` / remaining — только **base** pools ($450+$20). Models bonus — отдельная строка, не в denominator. `remainingBonus=false` → `BonusAvailability.Unknown`, **не** «Бонус исчерпан». Raw `bonusSpend` ≠ `ModelsBonusUsedUsd`. |
| **Supersedes** | `remainingBonus=false` → Exhausted; `ResolveCombinedDisplay.UsedUsd` включал full models spend |
| **Reason** | Live diagnostics: bonus spend растёт при `remainingBonus=false`; combined used включал bonus, remaining — нет |
| **Affected docs** | MASTER_CONTEXT, HANDOFF |
| **Affected code** | `QuotaBonusHelper`, `QuotaMonetaryHelper`, `MainViewModel`, `BonusAvailability`, strings, tests |
| **Status** | Active |

---

## 2026-09-04 — Model C: totalSpend includes API; fix double counting

| Field | Value |
|-------|-------|
| **Decision** | Raw `totalSpend` = combined actual period spend (может включать API после spillover). `modelsActualUsed = totalSpend − apiUsed` (или direct `autoSpend`). `combinedActualUsed = totalSpend` — **не** `models + api` повторно. Daily: `combinedToday = ΔtotalSpend`; `modelsToday = ΔtotalSpend − Δapi`. `totalPercentUsed` — диагностика only; bonus allowance **UNKNOWN** (не хардкодить $25). |
| **Supersedes** | `ModelsUsedUsd = totalSpend`; `combined = models + api` |
| **Reason** | Snapshot/API diagnostics: API рост всегда двигает `totalSpend`; `modelsBonus` завышался на `apiUsed` |
| **Affected docs** | MASTER_CONTEXT § Raw totalSpend, HANDOFF |
| **Affected code** | `QuotaSpendResolver`, `CursorPlanUsageMapper`, `QuotaUsageEnricher`, `QuotaMonetaryHelper`, `QuotaSnapshotRepository`, `UsageHistoryService`, `TrayDisplayFormatter`, tests |
| **Status** | Active |

---

## 2026-09 — AI collaboration bootstrap

| Field | Value |
|-------|-------|
| **Decision** | GitHub + `docs/MASTER_CONTEXT.md` как shared canon; процесс в `AI_COLLABORATION_PROTOCOL.md`; Cursor entry `AGENTS.md`. |
| **Supersedes** | Ad-hoc chat-only context |
| **Reason** | Стабильный handoff USER ↔ ChatGPT ↔ Cursor |
| **Affected docs** | `docs/*`, `AGENTS.md` |
| **Affected code** | — |
| **Status** | Active |

---

## 2026-09-05 — Reset countdown aligned with Cursor billingCycleEnd

| Field | Value |
|-------|-------|
| **Decision** | Countdown до сброса квоты = `billingCycleEnd` Unix ms из `GetCurrentPeriodUsage` (fallback `GetPlanInfo.billingCycleEnd`). Расчёт: `remainingMs = endMs − UtcNow` (как Cursor UI `DKf`). UI: ≥24h дни; &lt;24h часы+минуты (floor); &lt;1h минуты; &lt;60s секунды. Локальный таймер: ≥24h — 1 мин; &lt;24h — 1 с. Не другой endpoint, не +N часов. |
| **Supersedes** | `PeriodEnd − DateTime.Now` + отображение только целых часов |
| **Reason** | Визуальное расхождение с Cursor Plan & Usage (~2h) из-за усечения минут, не другого timestamp |
| **Affected docs** | MASTER_CONTEXT § Reset countdown, HANDOFF |
| **Affected code** | `BillingCycleTimestamp`, `RemainingTimeFormatter`, `QuotaUsage.PeriodEndUnixMilliseconds`, `CursorQuotaUsageProvider`, `MainViewModel`, `QuotaDiagnosticLogger`, strings, tests |
| **Status** | Active |

---

## 2026-09-05 — HTTP 403 network recovery mode

| Field | Value |
|-------|-------|
| **Decision** | HTTP 403 от Cursor API → `CursorHttpTransport.Reset()` + **network recovery loop** (1 с × 30 с, затем 10 с). Обычный scheduler refresh **паузится** во время recovery. Успешный fetch выходит из recovery. |
| **Supersedes** | Single failed refresh until next scheduler tick |
| **Reason** | VPN / transient path failures к `api2.cursor.sh` |
| **Affected docs** | MASTER_CONTEXT § Network recovery |
| **Affected code** | `CursorHttpTransport`, `CursorHttpRetry`, `CursorNetworkRecoveryService`, `MainViewModel`, `QuotaRefreshScheduler`, tests |
| **Status** | Active |

---

## 2026-09-05 — Refresh failure diagnostics in UI

| Field | Value |
|-------|-------|
| **Decision** | При failed refresh показывать пользователю «Не удалось обновить данные» и «Причина: …»; structured `REFRESH_FAILED` в log. Очищать при success. |
| **Supersedes** | Silent failure / generic error only |
| **Reason** | Диагностика без чтения log-файла |
| **Affected docs** | MASTER_CONTEXT § Refresh failure diagnostics |
| **Affected code** | `CursorRefreshFailureDescriber`, `MainViewModel`, `MainWindow.xaml`, `QuotaDiagnosticLogger`, strings, tests |
| **Status** | Active |

---

## 2026-09-05 — Tray menu: combined base spend and API line

| Field | Value |
|-------|-------|
| **Decision** | Tray: combined `$used из ~$limit` через `ResolveCombinedDisplay` (base pools); Models bonus отдельной строкой; API — `$used из $limit — X%` в одной строке. |
| **Supersedes** | Models actual vs Models base in tray; API % без spend |
| **Reason** | Согласованность с главным экраном; меньше путаницы при bonus |
| **Affected docs** | MASTER_CONTEXT § Tray menu |
| **Affected code** | `TrayDisplayFormatter`, `Strings*.resx`, tests |
| **Status** | Active |

