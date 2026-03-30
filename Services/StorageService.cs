using System.Text.Json;
using ZayaDesertRequestCreatorBot.Models;

namespace ZayaDesertRequestCreatorBot.Services;

public sealed class StorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _storagePath;

    public StorageService(string storagePath)
    {
        _storagePath = storagePath;
    }

    public async Task<IReadOnlyList<Dessert>> LoadDessertsAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_storagePath))
            {
                return [];
            }

            await using var stream = File.OpenRead(_storagePath);
            var desserts = await JsonSerializer.DeserializeAsync<List<Dessert>>(stream, JsonOptions, cancellationToken);
            return desserts ?? [];
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveDessertsAsync(IReadOnlyList<Dessert> desserts, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(_storagePath);
            await JsonSerializer.SerializeAsync(stream, desserts, JsonOptions, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
