using ZayaDesertRequestCreatorBot.Models;

namespace ZayaDesertRequestCreatorBot.Services;

public sealed class DessertService
{
    private readonly StorageService _storageService;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IReadOnlyList<Dessert> _desserts = [];

    public DessertService(StorageService storageService)
    {
        _storageService = storageService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _desserts = await _storageService.LoadDessertsAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<Dessert>> GetDessertsAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return _desserts.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<int> UpsertDessertsAsync(IReadOnlyList<Dessert> desserts, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _desserts = desserts.ToList();
            await _storageService.SaveDessertsAsync(_desserts, cancellationToken);
            return _desserts.Count;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AddDessertAsync(Dessert dessert, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var list = _desserts.ToList();
            list.Add(dessert);
            _desserts = list;
            await _storageService.SaveDessertsAsync(_desserts, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> UpdateDessertAsync(int index, Dessert dessert, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var list = _desserts.ToList();
            if (index < 0 || index >= list.Count)
            {
                return false;
            }

            list[index] = dessert;
            _desserts = list;
            await _storageService.SaveDessertsAsync(_desserts, cancellationToken);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteDessertAsync(int index, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var list = _desserts.ToList();
            if (index < 0 || index >= list.Count)
            {
                return false;
            }

            list.RemoveAt(index);
            _desserts = list;
            await _storageService.SaveDessertsAsync(_desserts, cancellationToken);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public static IReadOnlyList<(string Name, int OrderAmount)> BuildOrder(
        IReadOnlyList<Dessert> desserts,
        IReadOnlyDictionary<string, int> stocks,
        DayType dayType)
    {
        var result = new List<(string Name, int OrderAmount)>(desserts.Count);

        foreach (var dessert in desserts)
        {
            var dayMultiplier = dayType switch
            {
                DayType.Weekday => 1.0,
                DayType.Weekend => dessert.WeekendMultiplier,
                DayType.Holiday => dessert.HolidayMultiplier,
                _ => 1.0
            };

            stocks.TryGetValue(dessert.Name, out var stock);
            var rawValue = (dessert.BaseSales * dayMultiplier) - stock + dessert.SafetyStock;
            var finalValue = Math.Max(0, (int)Math.Ceiling(rawValue));

            if(finalValue > 0) result.Add((dessert.Name, finalValue));
        }

        return result;
    }
}
