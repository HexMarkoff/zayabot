using System.Collections.Concurrent;
using ZayaDesertRequestCreatorBot.Models;

namespace ZayaDesertRequestCreatorBot.Services;

public sealed class UserStateService
{
    private readonly ConcurrentDictionary<long, UserState> _states = new();

    public UserState Get(long chatId)
    {
        return _states.GetOrAdd(chatId, _ => new UserState());
    }

    public void Reset(long chatId)
    {
        _states[chatId] = new UserState();
    }
}
