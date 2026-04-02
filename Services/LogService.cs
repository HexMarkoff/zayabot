using System.Text.Json;
using ZayaDesertRequestCreatorBot.Models;

namespace ZayaDesertRequestCreatorBot.Services;

public sealed class LogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _logFilePath;

    public LogService(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    public async Task AddLog(UserAction action, CancellationToken cancellationToken = default)
    {
        if (action.Timestamp == default)
        {
            action.Timestamp = DateTime.UtcNow;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await LoadListUnsafeAsync(cancellationToken).ConfigureAwait(false);
            list.Add(action);
            await SaveListUnsafeAsync(list, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<UserAction>> GetAllLogs(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await LoadListUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return list.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<UserAction>> GetLastLogs(int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return [];
        }

        var all = await GetAllLogs(cancellationToken).ConfigureAwait(false);
        return all
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToList();
    }

    public async Task<IReadOnlyList<LoggedUserSummary>> GetUniqueUsers(CancellationToken cancellationToken = default)
    {
        var all = await GetAllLogs(cancellationToken).ConfigureAwait(false);
        return all
            .GroupBy(a => a.UserId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.Timestamp).First();
                var name = string.IsNullOrWhiteSpace(latest.Username) ? null : latest.Username;
                return new LoggedUserSummary(latest.UserId, name);
            })
            .OrderBy(x => x.UserId)
            .ToList();
    }

    private async Task<List<UserAction>> LoadListUnsafeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_logFilePath))
        {
            await SaveListUnsafeAsync([], cancellationToken).ConfigureAwait(false);
            return [];
        }

        await using var stream = File.OpenRead(_logFilePath);
        var logs = await JsonSerializer.DeserializeAsync<List<UserAction>>(stream, JsonOptions, cancellationToken)
                   .ConfigureAwait(false);
        return logs ?? [];
    }

    private async Task SaveListUnsafeAsync(List<UserAction> logs, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_logFilePath);
        await JsonSerializer.SerializeAsync(stream, logs, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
