namespace EntglDb.Demo.Game;

public class Monster
{
    public string Name { get; set; } = string.Empty;
    public int Hp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int XpReward { get; set; }
    public int GoldReward { get; set; }
    public string Emoji { get; set; } = string.Empty;
}

public static class MonsterFactory
{
    private static readonly Random Rng = new();

    private static readonly Monster[] Templates =
    [
        new() { Name = "Slime",           Emoji = "🟢", Hp = 20,  Attack = 5,  Defense = 1,  XpReward = 10,  GoldReward = 5 },
        new() { Name = "Goblin",          Emoji = "👺", Hp = 35,  Attack = 10, Defense = 3,  XpReward = 20,  GoldReward = 12 },
        new() { Name = "Skeleton",        Emoji = "💀", Hp = 40,  Attack = 12, Defense = 5,  XpReward = 25,  GoldReward = 15 },
        new() { Name = "Wolf",            Emoji = "🐺", Hp = 30,  Attack = 14, Defense = 2,  XpReward = 18,  GoldReward = 8 },
        new() { Name = "Orc",             Emoji = "👹", Hp = 60,  Attack = 16, Defense = 8,  XpReward = 40,  GoldReward = 25 },
        new() { Name = "Dark Mage",       Emoji = "🧙", Hp = 45,  Attack = 22, Defense = 4,  XpReward = 50,  GoldReward = 35 },
        new() { Name = "Troll",           Emoji = "🧌", Hp = 80,  Attack = 18, Defense = 12, XpReward = 60,  GoldReward = 40 },
        new() { Name = "Dragon Whelp",    Emoji = "🐉", Hp = 100, Attack = 25, Defense = 15, XpReward = 80,  GoldReward = 60 },
        new() { Name = "Lich",            Emoji = "☠️", Hp = 90,  Attack = 30, Defense = 10, XpReward = 100, GoldReward = 80 },
        new() { Name = "Ancient Dragon",  Emoji = "🔥", Hp = 200, Attack = 40, Defense = 25, XpReward = 200, GoldReward = 150 },
    ];

    public static Monster Spawn(int heroLevel)
    {
        // Select monsters appropriate to hero level (with some variance)
        int maxIndex = Math.Min(heroLevel + 2, Templates.Length);
        int minIndex = Math.Max(0, heroLevel - 2);
        var template = Templates[Rng.Next(minIndex, maxIndex)];

        // Scale slightly with hero level
        double scale = 1.0 + (heroLevel - 1) * 0.1;
        return new Monster
        {
            Name = template.Name,
            Emoji = template.Emoji,
            Hp = (int)(template.Hp * scale),
            Attack = (int)(template.Attack * scale),
            Defense = (int)(template.Defense * scale),
            XpReward = (int)(template.XpReward * scale),
            GoldReward = (int)(template.GoldReward * scale),
        };
    }
}
