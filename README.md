# Quota

Утилита для Windows, которая помогает контролировать месячную квоту Cursor: показывает использование, оставшийся процент, срок обновления периода и рекомендуемый дневной расход.

## AI / collaboration

Канон проекта для ChatGPT и Cursor: [`docs/AI_CONTEXT_INDEX.md`](docs/AI_CONTEXT_INDEX.md).

## Стек

- C#
- .NET 10
- WPF
- XAML

## Текущий статус

Приложение получает **реальные данные** через `CursorQuotaUsageProvider`:

- Connect RPC `GetCurrentPeriodUsage` на `https://api2.cursor.sh`
- авторизация из локального `state.vscdb`
- история снимков в SQLite (`%LOCALAPPDATA%\Quota\quota.db`)
- диагностический лог: `logs/quota.log`

`MockQuotaUsageProvider` оставлен для разработки и тестов.

## Данные и хранение

- **Квота Cursor** — `CursorQuotaUsageProvider` (`IQuotaUsageProvider`): Connect RPC к `https://api2.cursor.sh`, регистрация в `App.xaml.cs`.
- **Авторизация** — токены из `%APPDATA%\Cursor\User\globalStorage\state.vscdb` (Cursor IDE); логин/пароль Quota не использует.
- **История** — периодические снимки в SQLite (`%LOCALAPPDATA%\Quota\quota.db`).
- **Настройки UI** — `%LOCALAPPDATA%\Quota\ui-settings.json`.

Подробнее о каноне и решениях: [`docs/AI_CONTEXT_INDEX.md`](docs/AI_CONTEXT_INDEX.md).

## Сборка

Требуется установленный [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd D:\_APP\Quota
dotnet build
dotnet run
```

## Возможности

- отображение использованной и оставшейся квоты (модели и API отдельно);
- расчёт **рекомендуемого** дневного расхода (не жёсткий лимит);
- показатели за текущий день и статус темпа;
- история расхода по локальным снимкам;
- работа в системном трее;
- настройка автозапуска вместе с Windows (через реестр текущего пользователя).
