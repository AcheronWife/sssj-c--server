using Gcg2OfflineServer.Models;

namespace Gcg2OfflineServer.GameData;

/// <summary>
/// 新玩家默认数据。
/// 3个初始女孩（7,9,2），3张角色卡，1个编队。
/// </summary>
public static class GameDefaults
{
    // 全部武器数据 (detail, particular, rarity)
    public static readonly (int detail, int particular, int rarity)[] AllWeapons =
    {
        (1, 2, 1),
        (1, 2, 1),
        (1, 3, 4),
        (1, 4, 4),
        (1, 5, 4),
        (1, 6, 2),
        (1, 7, 2),
        (1, 8, 3),
        (1, 9, 3),
        (1, 11, 3),
        (1, 12, 2),
        (1, 13, 3),
        (1, 14, 2),
        (1, 15, 3),
        (1, 16, 3),
        (1, 17, 4),
        (1, 18, 4),
        (1, 19, 3),
        (1, 20, 4),
        (1, 21, 4),
        (1, 22, 4),
        (1, 23, 3),
        (1, 24, 3),
        (1, 500, 4),
        (1, 501, 3),
        (1, 502, 4),
        (2, 2, 1),
        (2, 2, 1),
        (2, 3, 3),
        (2, 4, 4),
        (2, 5, 3),
        (2, 6, 2),
        (2, 8, 3),
        (2, 9, 2),
        (2, 10, 4),
        (2, 11, 4),
        (2, 12, 3),
        (2, 14, 2),
        (2, 15, 2),
        (2, 16, 3),
        (2, 17, 3),
        (2, 18, 4),
        (2, 19, 4),
        (2, 20, 3),
        (2, 21, 4),
        (2, 22, 4),
        (2, 23, 3),
        (2, 24, 4),
        (2, 25, 3),
        (2, 26, 4),
        (2, 500, 4),
        (2, 501, 3),
        (2, 502, 1),
        (2, 503, 4),
        (2, 1000, 4),
        (2, 1001, 3),
        (2, 10000, 4),
        (2, 10001, 3),
        (3, 2, 1),
        (3, 2, 1),
        (3, 3, 3),
        (3, 4, 2),
        (3, 5, 2),
        (3, 6, 3),
        (3, 7, 3),
        (3, 8, 4),
        (3, 9, 4),
        (3, 10, 2),
        (3, 11, 3),
        (3, 12, 4),
        (3, 13, 2),
        (3, 14, 3),
        (3, 15, 3),
        (3, 16, 4),
        (3, 17, 4),
        (3, 18, 3),
        (3, 19, 4),
        (3, 20, 3),
        (3, 21, 4),
        (3, 22, 3),
        (3, 23, 4),
        (3, 500, 4),
        (3, 501, 3),
        (3, 502, 4),
        (3, 1002, 3),
        (4, 2, 1),
        (4, 2, 1),
        (4, 3, 2),
        (4, 4, 4),
        (4, 5, 3),
        (4, 6, 2),
        (4, 7, 2),
        (4, 8, 3),
        (4, 9, 4),
        (4, 10, 2),
        (4, 11, 3),
        (4, 12, 4),
        (4, 15, 3),
        (4, 16, 3),
        (4, 17, 3),
        (4, 18, 4),
        (4, 19, 4),
        (4, 20, 4),
        (4, 21, 3),
        (4, 22, 4),
        (4, 23, 4),
        (4, 24, 3),
        (4, 25, 4),
        (4, 500, 4),
        (4, 501, 3),
        (4, 502, 4),
        (5, 2, 1),
        (5, 2, 1),
        (5, 3, 4),
        (5, 4, 2),
        (5, 5, 3),
        (5, 6, 2),
        (5, 7, 3),
        (5, 8, 2),
        (5, 9, 3),
        (5, 10, 4),
        (5, 11, 2),
        (5, 12, 3),
        (5, 13, 4),
        (5, 14, 3),
        (5, 15, 3),
        (5, 16, 4),
        (5, 17, 4),
        (5, 18, 3),
        (5, 19, 3),
        (5, 20, 4),
        (5, 21, 4),
        (5, 22, 3),
        (5, 23, 3),
        (5, 24, 4),
        (5, 500, 4),
        (5, 501, 3),
        (5, 502, 4)
    };
    public const long MoneyVigour = 1;
    public const long MoneyGold = 2;
    public const long MoneyDiamond = 3;
    public const long MoneyCafeCoin = 5;       // 咖啡币/咖啡馆币
    public const long MoneyArenaCoin = 6;      // 竞技场币
    public const long MoneyClubCoin = 7;       // 公会/社团币
    public const long MoneyCardToken = 10;
    public const long MoneyPayDiamond = 12;
    public const long MoneyWeaponToken = 16;
    public const long MoneyTotalWarCoin = 18;  // 总力战币
    public const long MoneyJointFiringCoin = 19; // 联合演习币
    public const long MoneyEventCoin = 20;     // 活动币

    /// <summary>初始女孩 ID：7, 9, 2</summary>
    private static readonly (long girlId, long detail, long templateLevel)[] StarterCards =
    {
        (7, 7, 1),
        (9, 9, 3),
        (2, 2, 1),
    };

    public static PlayerState CreateDefaultPlayer(string account, long roleId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var player = new PlayerState
        {
            Account = account,
            RoleId = roleId,
            Name = string.Empty,
            Level = 1,
            Exp = 0,
            FightPower = 999999,
            ServerZone = 8,
            RegisterTime = now,
            LastLoginAt = null,
            Live2dEnableLevel = 0,
            Live2dHx = false,
            NextItemGuid = 10001,
        };

        // 初始货币：全货币拉满
        player.Money = new List<MoneyEntry>
        {
            new() { Id = MoneyVigour, Count = 100 },
            new() { Id = MoneyGold, Count = 50000 },
            new() { Id = MoneyDiamond, Count = 3000 },
            new() { Id = MoneyPayDiamond, Count = 0 },
            new() { Id = MoneyCafeCoin, Count = 0 },
            new() { Id = MoneyArenaCoin, Count = 0 },
            new() { Id = MoneyClubCoin, Count = 0 },
            new() { Id = MoneyCardToken, Count = 0 },
            new() { Id = MoneyWeaponToken, Count = 0 },
            new() { Id = MoneyTotalWarCoin, Count = 0 },
            new() { Id = MoneyJointFiringCoin, Count = 0 },
            new() { Id = MoneyEventCoin, Count = 0 },
        };

        // 初始阵容：3个女孩 + 3张角色卡（1级初始状态）
        var girls = new List<GirlState>();
        var inventory = new List<InventoryEntry>();
        long guid = 10001;
        var cardGuids = new List<long>();

        foreach (var (girlId, detail, templateLevel) in StarterCards)
        {
            girls.Add(new GirlState
            {
                GirlId = girlId,
                Level = 1,
                Exp = 0,
                ModelId = 1,
                MoodValue = 100,
                Vigor = 100,
                Flag = 0,
                BreakLevel = 0,
            });

            var cardGuid = guid++;
            cardGuids.Add(cardGuid);
            inventory.Add(new InventoryEntry
            {
                Guid = cardGuid,
                Genre = 1,
                Detail = detail,
                Particular = 1,
                TemplateLevel = 3,
                Count = 1,
                CreateTime = now,
                EnhanceLevel = 1,
                EnhanceExp = 0,
                BreakLevel = 0,
                LockOn = 0,
            });
        }

        player.Girls = girls;
        player.Inventory = inventory;

        // 初始编队：3个初始女孩
        player.Formations = new List<FormationState>
        {
            new()
            {
                Id = 1,
                Title = "编队1",
                FightCards = cardGuids.Select(g => new FightCardState { WeaponGuid = 0, MainCardGuid = g }).ToList(),
            },
        };

        // 咖啡馆初始状态
        player.Cafe = new CafeState
        {
            Coffees = new List<CafeCoffee>(),
            Waiters = new List<long[]> { new long[0], new long[0], new long[0] },
            Customers = new List<CafeCustomer>(),
            LastCustomerTime = 0,
            Pets = new List<object>(),
        };

        // 手机/抽卡/签到初始状态
        player.Phone = new PhoneState { Letters = new List<PhoneLetterState>() };
        player.Gacha = new GachaState { Pending = null };
        player.DailySignUp = new DailySignUpState
        {
            Cycle = DateTime.UtcNow.AddHours(4).ToString("yyyy-MM"),
            LastOperationalDate = DateTime.UtcNow.AddHours(4).ToString("yyyy-MM-dd"),
        };
        player.EightDaySignUp = new EightDaySignUpState { CumulativeDays = 0, LastOperationalDate = null };

        // 初始任务值
        player.TaskValues = new Dictionary<string, long>
        {
            ["1321721"] = 1, // 今日已签到 (20<<16)|11001
            ["1321722"] = 30, // 累计签到30天
            ["131074"] = StarterCards[0].girlId, // 默认看板娘 (2<<16)|2
        };

        // 解锁初始女孩的默认时装（modelId=1）
        foreach (var girl in player.Girls)
        {
            var suitTaskId = (4L << 16) | ((girl.GirlId - 1) * 2000 + 1);
            player.TaskValues[suitTaskId.ToString()] = 2; // 2=已解锁且已装备
        }

        return player;
    }
}
