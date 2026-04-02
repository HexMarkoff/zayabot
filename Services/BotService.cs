using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using ZayaDesertRequestCreatorBot.Models;

namespace ZayaDesertRequestCreatorBot.Services;

public sealed class BotService
{
    private const string CbAdminStats = "admin_stats";
    private const string CbAdminUsers = "admin_users";
    private const string CbAdminLast = "admin_last";
    private const string CbAdminBack = "admin_back";

    private const int TelegramMaxMessageLength = 4090;

    private readonly TelegramBotClient _botClient;
    private readonly DessertService _dessertService;
    private readonly ParserService _parserService;
    private readonly UserStateService _userStateService;
    private readonly StepHandler _stepHandler;
    private readonly LogService _logService;
    private readonly ILogger<BotService> _logger;
    private readonly ConcurrentDictionary<long, UserSession> _sessions = new();

    public BotService(
        TelegramBotClient botClient,
        DessertService dessertService,
        ParserService parserService,
        UserStateService userStateService,
        StepHandler stepHandler,
        LogService logService,
        ILogger<BotService> logger)
    {
        _botClient = botClient;
        _dessertService = dessertService;
        _parserService = parserService;
        _userStateService = userStateService;
        _stepHandler = stepHandler;
        _logService = logService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _dessertService.InitializeAsync(cancellationToken);

        var me = await _botClient.GetMe(cancellationToken);
        _logger.LogInformation("Bot started: @{Username}", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        var commands = new[]
        {
            new BotCommand { Command = "start", Description = "Запуск бота" },
            new BotCommand { Command = "desserts", Description = "Список десертов" },
            new BotCommand { Command = "status", Description = "Сделать заявку" },
            new BotCommand { Command = "admin", Description = "Админ-панель" }
        };

        await _botClient.SetMyCommands(commands);

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cancellationToken);
    }

    private Task HandleErrorAsync(ITelegramBotClient _, Exception exception, CancellationToken __)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException =>
                $"Telegram API Error: [{apiRequestException.ErrorCode}] {apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogError("{ErrorMessage}", errorMessage);
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.CallbackQuery is not null)
            {
                await HandleCallbackAsync(update.CallbackQuery, cancellationToken);
                return;
            }

            if (update.Message is { Text: not null } message)
            {
                await HandleMessageAsync(message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle update");
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var rawText = message.Text!;
        var text = rawText.Trim();
        var session = _sessions.GetOrAdd(chatId, _ => new UserSession());
        var userState = _userStateService.Get(chatId);

        if (userState.CurrentAction != DessertAction.None)
        {
            await _stepHandler.HandleInputAsync(chatId, rawText, message.From, cancellationToken);
            return;
        }

        switch (text)
        {
            case "/start":
                session.State = SessionState.Idle;
                session.SelectedDayType = null;
                session.HasNitrogen = null;
                await _botClient.SendMessage(
                    chatId,
                    "Привет! Я помогу рассчитать заявку десертов.\n\n" +
                    "Команды:\n" +
                    "/desserts — управление десертами\n" +
                    "/status — выбрать тип дня и ввести остатки",
                    cancellationToken: cancellationToken);
                await TryLogAsync(message.From, "Start", cancellationToken);
                return;

            case "/desserts":
                await TryLogAsync(message.From, "DessertsEdit", cancellationToken);
                await ShowDessertsMenuAsync(chatId, cancellationToken);
                return;

            case "/status":
                await TryLogAsync(message.From, "Status", cancellationToken);
                await AskDayTypeAsync(chatId, cancellationToken);
                return;

            case "/admin":
                if (!AdminHelper.IsAdmin(message.From))
                {
                    await _botClient.SendMessage(chatId, "Нет доступа ❌", cancellationToken: cancellationToken);
                    return;
                }

                await _botClient.SendMessage(
                    chatId,
                    "Админ-панель",
                    replyMarkup: BuildAdminRootKeyboard(),
                    cancellationToken: cancellationToken);
                return;
        }

        if (session.State == SessionState.AwaitingStockInput && session.SelectedDayType.HasValue)
        {
            var desserts = await _dessertService.GetDessertsAsync(cancellationToken);
            if (desserts.Count == 0)
            {
                session.State = SessionState.Idle;
                session.SelectedDayType = null;
                session.HasNitrogen = null;
                await _botClient.SendMessage(chatId, "Сначала добавьте десерты через /desserts.", cancellationToken: cancellationToken);
                return;
            }

            var hasNitrogen = session.HasNitrogen ?? true;
            var filteredDesserts = hasNitrogen
                ? desserts
                : desserts.Where(x => !x.IsCryo).ToList();

            var parsedStocks = _parserService.ParseStocks(text);
            var order = DessertService.BuildOrder(filteredDesserts, parsedStocks, session.SelectedDayType.Value);
            var response = string.Join('\n', order.Select(x => $"{x.Name} {x.OrderAmount}"));

            session.State = SessionState.Idle;
            session.SelectedDayType = null;
            session.HasNitrogen = null;

            var keyboard = new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("Новая заявка", "new_request"));

            await _botClient.SendMessage(
                chatId,
                response,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            await TryLogAsync(message.From, "CreateRequest", cancellationToken);
            return;
        }
    }

    private async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId is null)
        {
            return;
        }

        var data = callbackQuery.Data ?? string.Empty;

        if (data.StartsWith("admin_", StringComparison.Ordinal))
        {
            if (!AdminHelper.IsAdmin(callbackQuery.From))
            {
                await _botClient.AnswerCallbackQuery(
                    callbackQuery.Id,
                    text: "Нет доступа ❌",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await HandleAdminCallbackAsync(callbackQuery, cancellationToken);
            }
            catch (ApiRequestException ex)
            {
                _logger.LogWarning(ex, "Admin callback Telegram API error");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin callback failed");
            }

            await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            return;
        }

        var session = _sessions.GetOrAdd(chatId.Value, _ => new UserSession());
        var from = callbackQuery.From;

        switch (data)
        {
            case "dessert_cancel":
            {
                var cancelled = await _stepHandler.TryCancelDessertWizardAsync(chatId.Value, cancellationToken);
                if (cancelled)
                {
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                }
                else
                {
                    await _botClient.AnswerCallbackQuery(
                        callbackQuery.Id,
                        text: "Сейчас нечего отменять.",
                        showAlert: true,
                        cancellationToken: cancellationToken);
                }

                return;
            }
            case "cryo_yes":
            {
                var ok = await _stepHandler.TryApplyCryoCallbackAsync(chatId.Value, true, from, cancellationToken);
                await AnswerCryoOrCancelCallbackAsync(callbackQuery, ok, cancellationToken);
                return;
            }
            case "cryo_no":
            {
                var ok = await _stepHandler.TryApplyCryoCallbackAsync(chatId.Value, false, from, cancellationToken);
                await AnswerCryoOrCancelCallbackAsync(callbackQuery, ok, cancellationToken);
                return;
            }
            case "cryo_keep":
            {
                var ok = await _stepHandler.TryApplyCryoCallbackAsync(chatId.Value, null, from, cancellationToken);
                await AnswerCryoOrCancelCallbackAsync(callbackQuery, ok, cancellationToken);
                return;
            }
            case "dessert_add":
                await _stepHandler.StartAddAsync(chatId.Value, from, cancellationToken);
                break;
            case "dessert_edit":
                await _stepHandler.StartEditAsync(chatId.Value, from, cancellationToken);
                break;
            case "dessert_delete":
                await _stepHandler.StartDeleteAsync(chatId.Value, from, cancellationToken);
                break;
            case "day_weekday":
                await SetDayAndAskNitrogenAsync(chatId.Value, session, DayType.Weekday, cancellationToken);
                break;
            case "day_weekend":
                await SetDayAndAskNitrogenAsync(chatId.Value, session, DayType.Weekend, cancellationToken);
                break;
            case "day_holiday":
                await SetDayAndAskNitrogenAsync(chatId.Value, session, DayType.Holiday, cancellationToken);
                break;
            case "nitrogen_yes":
                await SetNitrogenAndAskStocksAsync(chatId.Value, session, hasNitrogen: true, cancellationToken);
                break;
            case "nitrogen_no":
                await SetNitrogenAndAskStocksAsync(chatId.Value, session, hasNitrogen: false, cancellationToken);
                break;
            case "new_request":
                await AskDayTypeAsync(chatId.Value, cancellationToken);
                break;
        }

        await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
    }

    private async Task AnswerCryoOrCancelCallbackAsync(
        CallbackQuery callbackQuery,
        bool success,
        CancellationToken cancellationToken)
    {
        if (success)
        {
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        }
        else
        {
            await _botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                text: "Действие недоступно.",
                showAlert: true,
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleAdminCallbackAsync(CallbackQuery query, CancellationToken cancellationToken)
    {
        var message = query.Message;
        if (message is null)
        {
            return;
        }

        var chatId = message.Chat.Id;
        var messageId = message.MessageId;
        var data = query.Data ?? string.Empty;

        switch (data)
        {
            case CbAdminBack:
                await _botClient.EditMessageText(
                    chatId,
                    messageId,
                    "Админ-панель",
                    replyMarkup: BuildAdminRootKeyboard(),
                    cancellationToken: cancellationToken);
                return;

            case CbAdminStats:
            {
                var all = await _logService.GetAllLogs(cancellationToken);
                var usersCount = (await _logService.GetUniqueUsers(cancellationToken)).Count;
                var requests = all.Count(a => a.Action == "CreateRequest");
                var text =
                    "Статистика:\n" +
                    $"Пользователей: {usersCount}\n" +
                    $"Действий: {all.Count}\n" +
                    $"Заявок: {requests}";
                await _botClient.EditMessageText(
                    chatId,
                    messageId,
                    text,
                    replyMarkup: BuildAdminBackKeyboard(),
                    cancellationToken: cancellationToken);
                return;
            }

            case CbAdminUsers:
            {
                var users = await _logService.GetUniqueUsers(cancellationToken);
                var lines = users.Count == 0
                    ? "Пока нет пользователей в логах."
                    : string.Join(
                        '\n',
                        users.Select(u =>
                        {
                            var name = string.IsNullOrWhiteSpace(u.Username) ? "—" : u.Username;
                            return $"{name} — {u.UserId}";
                        }));
                var text = TruncateForTelegram("Пользователи:\n" + lines);
                await _botClient.EditMessageText(
                    chatId,
                    messageId,
                    text,
                    replyMarkup: BuildAdminBackKeyboard(),
                    cancellationToken: cancellationToken);
                return;
            }

            case CbAdminLast:
            {
                var last = await _logService.GetLastLogs(10, cancellationToken);
                var lines = last.Count == 0
                    ? "Записей пока нет."
                    : string.Join(
                        '\n',
                        last.Select(a =>
                        {
                            var name = string.IsNullOrWhiteSpace(a.Username) ? "—" : a.Username;
                            var time = a.Timestamp.ToLocalTime().ToString("HH:mm");
                            return $"{name} — {a.Action} — {time}";
                        }));
                var text = TruncateForTelegram("Последние действия:\n" + lines);
                await _botClient.EditMessageText(
                    chatId,
                    messageId,
                    text,
                    replyMarkup: BuildAdminBackKeyboard(),
                    cancellationToken: cancellationToken);
                return;
            }
        }
    }

    private static InlineKeyboardMarkup BuildAdminRootKeyboard()
    {
        return new InlineKeyboardMarkup(
        [
            [InlineKeyboardButton.WithCallbackData("📊 Статистика", CbAdminStats)],
            [InlineKeyboardButton.WithCallbackData("👥 Пользователи", CbAdminUsers)],
            [InlineKeyboardButton.WithCallbackData("📜 Последние действия", CbAdminLast)]
        ]);
    }

    private static InlineKeyboardMarkup BuildAdminBackKeyboard()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("◀️ Назад", CbAdminBack) }
        });
    }

    private static string TruncateForTelegram(string text)
    {
        if (text.Length <= TelegramMaxMessageLength)
        {
            return text;
        }

        return text[..TelegramMaxMessageLength] + "…";
    }

    private async Task TryLogAsync(User? user, string action, CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return;
        }

        try
        {
            await _logService.AddLog(
                new UserAction
                {
                    UserId = user.Id,
                    Username = user.Username ?? string.Empty,
                    Action = action
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось записать действие в лог: {Action}", action);
        }
    }

    private async Task AskDayTypeAsync(long chatId, CancellationToken cancellationToken)
    {
        var desserts = await _dessertService.GetDessertsAsync(cancellationToken);
        if (desserts.Count == 0)
        {
            await _botClient.SendMessage(chatId, "Сначала добавьте десерты через /desserts.", cancellationToken: cancellationToken);
            return;
        }

        var keyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("Будний", "day_weekday")],
            [InlineKeyboardButton.WithCallbackData("Выходной", "day_weekend")],
            [InlineKeyboardButton.WithCallbackData("Праздник", "day_holiday")]
        ]);

        await _botClient.SendMessage(chatId, "Какой день завтра?", replyMarkup: keyboard, cancellationToken: cancellationToken);
    }

    private async Task SetDayAndAskNitrogenAsync(long chatId, UserSession session, DayType dayType, CancellationToken cancellationToken)
    {
        session.State = SessionState.AwaitingNitrogenChoice;
        session.SelectedDayType = dayType;
        session.HasNitrogen = null;

        var keyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("Да", "nitrogen_yes"),
                InlineKeyboardButton.WithCallbackData("Нет", "nitrogen_no")
            ]
        ]);

        await _botClient.SendMessage(chatId, "Есть азот?", replyMarkup: keyboard, cancellationToken: cancellationToken);
    }

    private async Task SetNitrogenAndAskStocksAsync(
        long chatId,
        UserSession session,
        bool hasNitrogen,
        CancellationToken cancellationToken)
    {
        if (!session.SelectedDayType.HasValue)
        {
            await AskDayTypeAsync(chatId, cancellationToken);
            return;
        }

        session.State = SessionState.AwaitingStockInput;
        session.HasNitrogen = hasNitrogen;

        await _botClient.SendMessage(
            chatId,
            "Введи остатки десертов в формате:\nНазвание количество",
            cancellationToken: cancellationToken);
    }

    private async Task ShowDessertsMenuAsync(long chatId, CancellationToken cancellationToken)
    {
        var desserts = await _dessertService.GetDessertsAsync(cancellationToken);
        var listText = desserts.Count == 0
            ? "Список десертов пуст."
            : string.Join("\n\n", desserts.Select(d =>
                $"Название: {d.Name}\n" +
                $"В день: {d.BaseSales}\n" +
                $"Выходной: {d.WeekendMultiplier}\n" +
                $"Праздник: {d.HolidayMultiplier}\n" +
                $"Доп: {d.SafetyStock}\n" +
                $"Крио: {(d.IsCryo ? "да" : "нет")}"));

        var keyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("➕ Добавить", "dessert_add"),
                InlineKeyboardButton.WithCallbackData("✏️ Редактировать", "dessert_edit")
            ],
            [
                InlineKeyboardButton.WithCallbackData("❌ Удалить", "dessert_delete")
            ]
        ]);

        await _botClient.SendMessage(chatId, listText, replyMarkup: keyboard, cancellationToken: cancellationToken);
    }
}
