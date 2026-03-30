using System.Globalization;
using ZayaDesertRequestCreatorBot.Models;

namespace ZayaDesertRequestCreatorBot.Services;

public sealed class ParserService
{
    public IReadOnlyList<Dessert> ParseDessertSettings(string input)
    {
        var result = new List<Dessert>();
        var lines = SplitLines(input);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is not (5 or 6))
            {
                continue;
            }

            var name = parts[0];
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!TryParseDouble(parts[1], out var baseSales) ||
                !TryParseDouble(parts[2], out var weekendMultiplier) ||
                !TryParseDouble(parts[3], out var holidayMultiplier) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var safetyStock))
            {
                continue;
            }

            var isCryo = false;
            if (parts.Length == 6 && !TryParseBool(parts[5], out isCryo))
            {
                continue;
            }

            result.Add(new Dessert
            {
                Name = name,
                BaseSales = baseSales,
                WeekendMultiplier = weekendMultiplier,
                HolidayMultiplier = holidayMultiplier,
                SafetyStock = safetyStock,
                IsCryo = isCryo
            });
        }

        return result
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();
    }

    public IReadOnlyDictionary<string, int> ParseStocks(string input)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lines = SplitLines(input);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var lastSpace = line.LastIndexOf(' ');
            if (lastSpace <= 0 || lastSpace == line.Length - 1)
            {
                continue;
            }

            var name = line[..lastSpace].Trim();
            var countPart = line[(lastSpace + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(name) ||
                !int.TryParse(countPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                continue;
            }

            result[name] = count;
        }

        return result;
    }

    private static IEnumerable<string> SplitLines(string input)
    {
        return input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryParseDouble(string input, out double value)
    {
        var normalized = input.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseBool(string input, out bool value)
    {
        switch (input.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "y":
            case "да":
            case "+":
                value = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
            case "нет":
            case "-":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
