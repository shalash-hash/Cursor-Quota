# MASTER CONTEXT — Quota (Cursor Quota)

Единый компактный канон проекта. Это **карта и инварианты**, не копия всего repository.

**Repository:** `https://github.com/shalash-hash/Cursor-Quota.git`  
**Default branch:** `main`

---

## 1. Что это за продукт

**Quota** — настольная утилита для **Windows**, помогающая контролировать месячную квоту **Cursor IDE**:

- текущее использование (модели + API);
- оставшийся ресурс и срок до сброса периода;
- рекомендуемый дневной расход («дневной план»);
- история расхода по снимкам;
- системный tray, автозапуск с Windows, тёмная тема, локализация.

Продукт **не** логинится в Cursor сам — использует токены, уже сохранённые **Cursor IDE** локально (`state.vscdb`). Логин и пароль пользователя Quota **не знает**.

---

## 2. Stack & platform

| Слой | Технология |
|------|------------|
| Language | C# |
| Runtime | .NET 10 (`net10.0-windows`) |
| UI | WPF + XAML |
| Tray | Windows Forms (`UseWindowsForms`) |
| Local DB | SQLite (`Microsoft.Data.Sqlite`) |
| Tests | xUnit (`Quota.Tests`) |

Сборка: `dotnet build -c Release` → `bin/Release/net10.0-windows/Quota.exe`

---

## 3. Architecture (high level)

```
App.xaml.cs (composition root)
  → MainViewModel
       → IQuotaUsageProvider (CursorQuotaUsageProvider | MockQuotaUsageProvider)
       → QuotaCalculator + DailyPlanCalculator
       → QuotaSnapshotRepository / UsageHistoryService
       → UiSettingsService / ThemeService / LocalizationService
  → MainWindow.xaml
  → TrayIconService
```

**Refresh loop:** `QuotaRefreshScheduler` (~1 min) → `MainViewModel.RefreshAsync` → fetch → calculate → UI + snapshot persist.

**Auth chain:** `GetUsageAsync` → `CursorAuthService.GetAccessTokenAsync` → read `state.vscdb` → optional OAuth refresh.

---

## 4. Main user flows

1. **Запуск** — окно сразу в tray; открытие из tray.
2. **Главный экран** — общая квота, дневной план, карточки «Модели Cursor» и «API», темп, расход сегодня/вчера.
3. **История расхода** — переключение view; период (сегодня / неделя / месяц / год / всё); два графика (дневной + накопленный).
4. **Ошибка авторизации** — если Cursor IDE вышел из аккаунта или токены недоступны.
5. **Настройки** — язык, тёмная тема, автозапуск, размер окна.

---

## 5. Data & domain concepts

### Пулы квоты

- **Models (First Party / Cursor Models)** — основной пул; лимит часто **оценивается** из `total_spend_cents` и процента (`QuotaMonetaryHelper.EstimateLimitUsd`).
- **API** — отдельный included amount из plan info.

### Combined total

**Общая квота** считается по **сумме долларовых лимитов** пулов (например ~$450 + $20), **не** как max/sum процентов по пулам.

### Billing day

**Сутки** привязаны к **времени начала billing cycle** Cursor (`BillingCycleCalendar`), не к полуночи ОС.

### Daily plan (дневной план)

**Рекомендация, не лимит.** План пересчитывается: отстаёте — растёт, опережаете — уменьшается.

**Два пула** — Cursor Models и API. **Граница цикла** — `realResetDate` (`usage.PeriodEnd`).

**`acceleratedEndInclusive`** = `min(cycleStart + 20 calendar days, realResetDate)` — последний **включительный** день фазы 1. `cycleStart` — **день 1**; ровно **21 календарный день** ⇒ дни `cycleStart` … `cycleStart + 20 days`.

**Пример** (цикл 06.09 → 06.10): Phase 1 = **06.09–26.09**; Reserve = **с 27.09** до `realResetDate`.

**Фаза 1 — ускоренная** (`today <= acceleratedEndInclusive`):

- Hill-план **только для Cursor Models** (`CalculateCursorModelDailyPlan` → `CalculateHillDailyPlan`).
- **API = 0**, не входит в combined plan.
- `combinedDailyPlan = modelsDailyPlan`.

**Фаза 2 — резерв** (`today > acceleratedEndInclusive` … `realResetDate`):

- Длина хвоста **зависит от реального цикла** (не фиксированные 5 дней).
- Models и API: линейное распределение остатка до reset через `BillingCycleCalendar.CountRemainingDays` (то же, что `CalculateRemainingPlanDays`).
- `combinedDailyPlan = modelsDailyPlan + apiDailyPlan`.
- Если пул исчерпан → его компонент = 0.

**`realResetDate` (`PeriodEnd`):** момент rollover Cursor (время из `billingCycleEnd`). Граница **exclusive** в точный instant; календарный день reset до rollover ещё считается одним расходным днём (`CountRemainingDays` → `Ceiling(periodEnd − now)`).

**Короткий цикл (< 21 дня):** `acceleratedEndInclusive` ограничен `realResetDate`; резервная фаза может отсутствовать.

**Опережение / отставание:** сравнение факта и плана в **USD** (`DailyTargetProgressCalculator`, `QuotaMonetaryHelper`). Проценты — производное для отображения.

**Combined Models + API:** каноническая единица расчёта — **USD**; pool-проценты не смешивать при сравнении fact vs plan (ahead/behind, plan completed). Combined % для «сегодня» и дневного плана — производное от combined USD / combined limit.

### Usage history

Периодические **snapshots** в SQLite → агрегация по bucket'ам → графики (без изменения расчёта при UI-правках).

---

## 6. Persistence & paths (runtime)

| Данные | Путь |
|--------|------|
| UI settings | `%LOCALAPPDATA%\Quota\ui-settings.json` |
| Snapshot DB | `%LOCALAPPDATA%\Quota\quota.db` |
| Diagnostic log | `logs/quota.log` (рядом с exe / project; **в .gitignore**) |
| Cursor auth | `%APPDATA%\Cursor\User\globalStorage\state.vscdb` (`cursorAuth/accessToken`, `cursorAuth/refreshToken`) |

**Не хранить в репозитории и не писать в quota.db / ui-settings:** access/refresh tokens, пароли, секреты.

---

## 7. Security & compatibility policy

- **Источник истины для авторизации:** `state.vscdb` Cursor IDE (не процесс IDE).
- При каждом refresh перечитывать токены из `state.vscdb`; не использовать устаревший in-memory JWT после logout в IDE.
- **Не логировать** access token, refresh token, полные JWT.
- OAuth refresh — только когда access истёк и refresh есть в актуальном `state.vscdb`.
- Private GitHub repo **не** разрешает коммитить secrets.

---

## 8. Testing

- Проект: `Quota.Tests` (xUnit).
- Покрытие: калькуляторы, monetary helpers, auth sync, history, localization, mappers.
- Запуск: `dotnet test Quota.Tests/Quota.Tests.csproj -c Release`
- `InternalsVisibleTo` для тестов auth internals.

---

## 9. Current development stage

**Рабочий продукт** с live Cursor API, историей, tray, i18n (много `.resx`), auth sync, раздельными графиками истории.

Активная зона зрелости: UX polish, документация, стабильность edge cases API/Cursor.

---

## 10. Authoritative profile docs

| Doc | Status |
|-----|--------|
| `docs/MASTER_CONTEXT.md` | **CURRENT** — этот файл |
| `docs/DECISIONS_CHANGELOG.md` | **CURRENT** — концептуальные решения |
| `docs/HANDOFF_TO_CHATGPT.md` | **CURRENT** — семантический снимок (обновлять при handoff) |
| `docs/AI_COLLABORATION_PROTOCOL.md` | **CURRENT** — процесс работы |
| `README.md` | **CURRENT** — human overview |

Отдельных `ARCHITECTURE.md` / `ROADMAP.md` в репозитории **нет**.

---

## 11. Conflict resolution order

При противоречии:

1. **Последнее явное решение пользователя**
2. **`MASTER_CONTEXT.md`**
3. **Более новый профильный authoritative doc** (например `DECISIONS_CHANGELOG.md`)
4. **Текущий код** = факт реализации, но **не** автоматическое product decision
5. **Historical / stale docs** (в т.ч. устаревшие секции README)

Различие документа и кода **не** означает автоматический rollback. Несогласованность product-level → класс **D**, спросить пользователя.

---

## 12. Known gaps

| Gap | Severity |
|-----|----------|
| Нет отдельного `ARCHITECTURE.md` / deployment guide | Low |
| `MainWindow` MinHeight 480 vs `UiSettingsService.MinWindowHeight` 420 | Minor inconsistency |
| Зависимость от неофициального Cursor RPC API — возможны breaking changes | Product risk |

---

## 13. What NOT to assume

- Имя файла ≠ актуальность содержимого.
- Процент в UI может не совпадать с «сырым» API без учёта dollar-based combined logic.
- Перезапуск Quota **не** требуется для обнаружения re-login в Cursor (после auth-sync decision).
