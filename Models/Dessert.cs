namespace ZayaDesertRequestCreatorBot.Models;

public sealed class Dessert
{
    public string Name { get; set; } = string.Empty;
    public double BaseSales { get; set; }
    public double WeekendMultiplier { get; set; }
    public double HolidayMultiplier { get; set; }
    public int SafetyStock { get; set; }
    public bool IsCryo { get; set; }
}
