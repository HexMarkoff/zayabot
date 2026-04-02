namespace ZayaDesertRequestCreatorBot.Models;

public class UserAction
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
