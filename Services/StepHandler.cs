using System.Globalization;
using Telegram.Bot;
using ZayaDesertRequestCreatorBot.Models;

namespace ZayaDesertRequestCreatorBot.Services;

public sealed class StepHandler
{
    private readonly TelegramBotClient _botClient;
    private readonly DessertService _dessertService;
    private readonly UserStateService _userStateService;

    public StepHandler(TelegramBotClient botClient, DessertService dessertService, UserStateService userStateService)
    {
        _botClient = botClient;
        _dessertService = dessertService;
        _userStateService = userStateService;
    }

    public async Task StartAddAsync(long chatId, CancellationToken cancellationToken)
    {
        var state = _userStateService.Get(chatId);
        state.CurrentAction = DessertAction.Add;
        state.CurrentStep = Step.Name;
        state.EditingDessertId = null;
        state.TempDessert = new Dessert();

        await _botClient.SendMessage(chatId, "Введите название десерта:", cancellationToken: cancellationToken);
    }

    public async Task StartEditAsync(long chatId, CancellationToken cancellationToken)
    {
        var desserts = await _dessertService.GetDessertsAsync(cancellationToken);
        if (desserts.Count == 0)
        {
            await _botClient.SendMessage(chatId, "Список пуст.", cancellationToken: cancellationToken);
            return;
        }

        var state = _userStateService.Get(chatId);
        state.CurrentAction = DessertAction.Edit;
        state.CurrentStep = Step.None;
        state.EditingDessertId = null;
        state.TempDessert = null;

        await _botClient.SendMessage(
            chatId,
            $"{BuildNumberedList(desserts)}\n\nВведите номер десерта для редактирования:",
            cancellationToken: cancellationToken);
    }

    public async Task StartDeleteAsync(long chatId, CancellationToken cancellationToken)
    {
        var desserts = await _dessertService.GetDessertsAsync(cancellationToken);
        if (desserts.Count == 0)
        {
            await _botClient.SendMessage(chatId, "Список пуст.", cancellationToken: cancellationToken);
            return;
        }

        var state = _userStateService.Get(chatId);
        state.CurrentAction = DessertAction.Delete;
        state.CurrentStep = Step.None;
        state.EditingDessertId = null;
        state.TempDessert = null;

        await _botClient.SendMessage(
            chatId,
            $"{BuildNumberedList(desserts)}\n\nВведите номер десерта для удаления:",
            cancellationToken: cancellationToken);
    }

    public async Task<bool> HandleInputAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var state = _userStateService.Get(chatId);
        if (state.CurrentAction == DessertAction.None)
        {
            return false;
        }

        if (state.CurrentAction == DessertAction.Delete)
        {
            await HandleDeleteAsync(chatId, rawText, cancellationToken);
            return true;
        }

        if (state.CurrentAction == DessertAction.Edit && state.EditingDessertId is null)
        {
            await SelectDessertForEditAsync(chatId, rawText, cancellationToken);
            return true;
        }

        await HandleStepAsync(chatId, rawText, cancellationToken);
        return true;
    }

    private async Task HandleDeleteAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var desserts = await _dessertService.GetDessertsAsync(cancellationToken);
        if (!TryParseIndex(rawText, desserts.Count, out var index))
        {
            await _botClient.SendMessage(chatId, "Неверный номер. Введите номер из списка.", cancellationToken: cancellationToken);
            return;
        }

        await _dessertService.DeleteDessertAsync(index, cancellationToken);
        _userStateService.Reset(chatId);
        await _botClient.SendMessage(chatId, "Удалено ✅", cancellationToken: cancellationToken);
    }

    private async Task SelectDessertForEditAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var desserts = await _dessertService.GetDessertsAsync(cancellationToken);
        if (!TryParseIndex(rawText, desserts.Count, out var index))
        {
            await _botClient.SendMessage(chatId, "Неверный номер. Введите номер из списка.", cancellationToken: cancellationToken);
            return;
        }

        var current = desserts[index];
        var state = _userStateService.Get(chatId);
        state.EditingDessertId = index;
        state.TempDessert = new Dessert
        {
            Name = current.Name,
            BaseSales = current.BaseSales,
            WeekendMultiplier = current.WeekendMultiplier,
            HolidayMultiplier = current.HolidayMultiplier,
            SafetyStock = current.SafetyStock,
            IsCryo = current.IsCryo
        };
        state.CurrentStep = Step.Name;

        await _botClient.SendMessage(chatId, $"Название (сейчас: {current.Name}):", cancellationToken: cancellationToken);
    }

    private async Task HandleStepAsync(long chatId, string rawText, CancellationToken cancellationToken)
    {
        var state = _userStateService.Get(chatId);
        var temp = state.TempDessert ?? new Dessert();
        var isEdit = state.CurrentAction == DessertAction.Edit;
        var text = rawText.Trim();

        switch (state.CurrentStep)
        {
            case Step.Name:
                if (isEdit && string.IsNullOrWhiteSpace(rawText))
                {
                    // keep current value
                }
                else if (string.IsNullOrWhiteSpace(text))
                {
                    await _botClient.SendMessage(chatId, "Название не может быть пустым. Введите название десерта:", cancellationToken: cancellationToken);
                    return;
                }
                else
                {
                    temp.Name = text;
                }

                state.TempDessert = temp;
                state.CurrentStep = Step.BaseSales;
                await _botClient.SendMessage(chatId, BuildPrompt("Базовые продажи в день", temp.BaseSales, isEdit), cancellationToken: cancellationToken);
                return;

            case Step.BaseSales:
                if (!TryReadDouble(isEdit, rawText, temp.BaseSales, out var baseSales))
                {
                    await _botClient.SendMessage(chatId, "Введите число для базовых продаж:", cancellationToken: cancellationToken);
                    return;
                }

                temp.BaseSales = baseSales;
                state.CurrentStep = Step.WeekendMultiplier;
                await _botClient.SendMessage(chatId, BuildPrompt("Множитель на выходной", temp.WeekendMultiplier, isEdit), cancellationToken: cancellationToken);
                return;

            case Step.WeekendMultiplier:
                if (!TryReadDouble(isEdit, rawText, temp.WeekendMultiplier, out var weekendMultiplier))
                {
                    await _botClient.SendMessage(chatId, "Введите число для выходного множителя:", cancellationToken: cancellationToken);
                    return;
                }

                temp.WeekendMultiplier = weekendMultiplier;
                state.CurrentStep = Step.HolidayMultiplier;
                await _botClient.SendMessage(chatId, BuildPrompt("Множитель на праздник", temp.HolidayMultiplier, isEdit), cancellationToken: cancellationToken);
                return;

            case Step.HolidayMultiplier:
                if (!TryReadDouble(isEdit, rawText, temp.HolidayMultiplier, out var holidayMultiplier))
                {
                    await _botClient.SendMessage(chatId, "Введите число для праздничного множителя:", cancellationToken: cancellationToken);
                    return;
                }

                temp.HolidayMultiplier = holidayMultiplier;
                state.CurrentStep = Step.SafetyStock;
                await _botClient.SendMessage(chatId, BuildPrompt("Дополнительное количество (страховой запас)", temp.SafetyStock, isEdit), cancellationToken: cancellationToken);
                return;

            case Step.SafetyStock:
                if (!TryReadInt(isEdit, rawText, temp.SafetyStock, out var safetyStock))
                {
                    await _botClient.SendMessage(chatId, "Введите целое число для страхового запаса:", cancellationToken: cancellationToken);
                    return;
                }

                temp.SafetyStock = safetyStock;
                state.TempDessert = temp;
                await SaveAndFinishAsync(chatId, state, cancellationToken);
                return;
        }
    }

    private async Task SaveAndFinishAsync(long chatId, UserState state, CancellationToken cancellationToken)
    {
        if (state.TempDessert is null)
        {
            _userStateService.Reset(chatId);
            return;
        }

        if (state.CurrentAction == DessertAction.Add)
        {
            await _dessertService.AddDessertAsync(state.TempDessert, cancellationToken);
            _userStateService.Reset(chatId);
            await _botClient.SendMessage(chatId, "Десерт добавлен ✅", cancellationToken: cancellationToken);
            return;
        }

        if (state.CurrentAction == DessertAction.Edit && state.EditingDessertId.HasValue)
        {
            await _dessertService.UpdateDessertAsync(state.EditingDessertId.Value, state.TempDessert, cancellationToken);
            _userStateService.Reset(chatId);
            await _botClient.SendMessage(chatId, "Десерт обновлен ✅", cancellationToken: cancellationToken);
            return;
        }

        _userStateService.Reset(chatId);
    }

    private static string BuildPrompt(string label, double current, bool isEdit)
    {
        return isEdit
            ? $"{label} (сейчас: {current.ToString(CultureInfo.InvariantCulture)}):"
            : $"Введите {char.ToLowerInvariant(label[0])}{label[1..]}:";
    }

    private static string BuildPrompt(string label, int current, bool isEdit)
    {
        return isEdit
            ? $"{label} (сейчас: {current}):"
            : $"Введите {char.ToLowerInvariant(label[0])}{label[1..]}:";
    }

    private static bool TryParseIndex(string rawText, int count, out int index)
    {
        if (int.TryParse(rawText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
            number >= 1 &&
            number <= count)
        {
            index = number - 1;
            return true;
        }

        index = -1;
        return false;
    }

    private static bool TryReadDouble(bool isEdit, string rawText, double currentValue, out double value)
    {
        if (isEdit && string.IsNullOrWhiteSpace(rawText))
        {
            value = currentValue;
            return true;
        }

        var normalized = rawText.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInt(bool isEdit, string rawText, int currentValue, out int value)
    {
        if (isEdit && string.IsNullOrWhiteSpace(rawText))
        {
            value = currentValue;
            return true;
        }

        return int.TryParse(rawText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string BuildNumberedList(IReadOnlyList<Dessert> desserts)
    {
        return string.Join('\n', desserts.Select((d, index) => $"{index + 1}. {d.Name}"));
    }
}
