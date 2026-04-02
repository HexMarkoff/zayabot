using Telegram.Bot.Types;

namespace ZayaDesertRequestCreatorBot.Services;

public static class AdminHelper
{
    private const string AdminUsername = "hmarkoff";

    public static bool IsAdmin(User? user) =>
        user?.Username is { } u &&
        string.Equals(u, AdminUsername, StringComparison.OrdinalIgnoreCase);
}
