# Zaya Desert Request Creator Bot

Telegram-бот на C# (.NET 8) для автоматизации заявки десертов в ресторане.

## Что умеет

- Хранит список десертов и параметры в JSON (`data/desserts.json`)
- Команда `/setdesserts` обновляет список десертов
- Команда `/status` предлагает выбрать тип дня кнопками
- Принимает остатки в формате `Название количество`
- Считает заявку по формуле:
  - `order = (BaseSales * dayMultiplier) - stock + SafetyStock`
  - округление вверх (`Math.Ceiling`)
  - минимум `0`
- После расчета показывает кнопку `Новая заявка`

## Формат ввода `/setdesserts`

Построчно:

`Название|BaseSales|WeekendMultiplier|HolidayMultiplier|SafetyStock`

Пример:

`Японский чизкейк|5|1.5|2.0|2`

## Формат ввода остатков

Построчно:

`Название количество`

Пример:

`Японский чизкейк 2`

## Запуск

1. Установить .NET 8 SDK.
2. Создать Telegram-бота у [@BotFather](https://t.me/BotFather) и получить токен.
3. Задать токен одним из способов:
   - Переменная окружения `TELEGRAM_BOT_TOKEN`
   - Либо в `appsettings.json` в поле `TelegramBotToken`
4. В папке проекта выполнить:

```bash
dotnet restore
dotnet run
```

5. Открыть бота в Telegram и выполнить `/start`.
