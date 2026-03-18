using System.ComponentModel.DataAnnotations;

namespace EntglDb.Demo.Game;

public class BattleLog
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string HeroId { get; set; } = string.Empty;
    public string HeroName { get; set; } = string.Empty;
    public string MonsterName { get; set; } = string.Empty;
    public bool Victory { get; set; }
    public int XpGained { get; set; }
    public int GoldGained { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
