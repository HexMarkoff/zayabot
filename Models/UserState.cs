namespace ZayaDesertRequestCreatorBot.Models;

public enum DessertAction
{
    None = 0,
    Add = 1,
    Edit = 2,
    Delete = 3
}

public enum Step
{
    None = 0,
    Name = 1,
    BaseSales = 2,
    WeekendMultiplier = 3,
    HolidayMultiplier = 4,
    SafetyStock = 5,
    IsCryo = 6
}

public sealed class UserState
{
    public DessertAction CurrentAction { get; set; } = DessertAction.None;
    public Step CurrentStep { get; set; } = Step.None;
    public int? EditingDessertId { get; set; }
    public Dessert? TempDessert { get; set; }
}
