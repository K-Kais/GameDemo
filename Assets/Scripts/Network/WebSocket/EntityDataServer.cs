using System;



public sealed class EntityDataServer
{
    public string PlayerId { get; }
    public string UserName { get; set; }
    public string CurrentMapId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float DirX { get; set; }
    public float DirY { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; } = DateTime.UtcNow;

    public EntityDataServer(string playerId, string userName)
    {
        PlayerId = playerId;
        UserName = userName;
    }
}