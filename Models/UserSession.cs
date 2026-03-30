namespace ZayaDesertRequestCreatorBot.Models;

public enum SessionState
{
    Idle = 0,
    AwaitingDessertsConfig = 1,
    AwaitingNitrogenChoice = 2,
    AwaitingStockInput = 3
}

public sealed class UserSession
{
    public SessionState State { get; set; } = SessionState.Idle;
    public DayType? SelectedDayType { get; set; }
    public bool? HasNitrogen { get; set; }
}
