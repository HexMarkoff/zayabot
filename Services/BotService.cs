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
    private readonly TelegramBotClient _botClient;
    private readonly DessertService _dessertService;
    private readonly ParserService _parserService;
    private readonly UserStateService _userStateService;
    private readonly StepHandler _stepHandler;
    private readonly ILogger<BotService> _logger;
    private readonly ConcurrentDictionary<long, UserSession> _sessions = new();

    public BotService(
        TelegramBotClient botClient,
        DessertService dessertService,
        ParserService parserService,
        UserStateService userStateService,
        StepHandler stepHandler,
        ILogger<BotService> logger)
    {
        _botClient = botClient;
        _dessertService = dessertService;
        _parserService = parserService;
        _userStateService = userStateService;
        _stepHandler = stepHandler;
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
            new BotCommand { Command = "status", Description = "Сделать заявку" }
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
            await _stepHandler.HandleInputAsync(chatId, rawText, cancellationToken);
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
                return;

            case "/desserts":
                await ShowDessertsMenuAsync(chatId, cancellationToken);
                return;

            case "/status":
                await AskDayTypeAsync(chatId, cancellationToken);
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

        var session = _sessions.GetOrAdd(chatId.Value, _ => new UserSession());
        var data = callbackQuery.Data ?? string.Empty;

        switch (data)
        {
            case "dessert_add":
                await _stepHandler.StartAddAsync(chatId.Value, cancellationToken);
                break;
            case "dessert_edit":
                await _stepHandler.StartEditAsync(chatId.Value, cancellationToken);
                break;
            case "dessert_delete":
                await _stepHandler.StartDeleteAsync(chatId.Value, cancellationToken);
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
                $"Доп: {d.SafetyStock}"));

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
