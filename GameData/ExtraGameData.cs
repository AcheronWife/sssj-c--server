using System;
using System.Collections.Generic;
using System.Linq;

namespace Gcg2OfflineServer.GameData;

// ==================== 每日任务 ====================
public static class DailyMissionData
{
    public const int TaskGroup = 5;
    public const int ActivePointTaskId = 30000;
    public const int ActiveAwardTaskId = 30001;
    public const int ActivePointMax = 100;

    public record DailyMission(int Id, int Target, int ActivePoints);
    public record ActiveAward(int Id, int RequiredPoints, int[][] Awards);

    public static readonly DailyMission[] Missions =
    {
        new(101, 5, 20), new(102, 2, 10), new(103, 2, 10), new(104, 1, 15),
        new(105, 5, 15), new(106, 1, 15), new(107, 1, 10), new(108, 1, 20),
        new(109, 100, 20), new(110, 1, 15),
    };

    public static readonly ActiveAward[] ActiveAwards =
    {
        new(1, 10, new[] { new[] {15,1,1,1,10000}, new[] {9,16,1,1,1}, new[] {15,52,1,1,100} }),
        new(2, 25, new[] { new[] {15,1,1,1,10000}, new[] {7,1,4,3,1}, new[] {7,3,1,3,1}, new[] {15,52,1,1,200} }),
        new(3, 50, new[] { new[] {15,1,1,1,20000}, new[] {5,2,1,3,1}, new[] {15,10,1,1,200}, new[] {15,52,1,1,300} }),
        new(4, 75, new[] { new[] {15,1,1,1,20000}, new[] {7,7,1,4,1}, new[] {15,4,1,1,60}, new[] {15,52,1,1,400} }),
        new(5, 100, new[] { new[] {15,1,1,1,50000}, new[] {15,20,1,1,20}, new[] {15,2,1,1,50}, new[] {15,52,1,1,600} }),
    };

    public static int MakeTaskId(int taskId) => (TaskGroup << 16) | taskId;
    public static int Progress(int taskValue) => Math.Max(0, taskValue / 2);
    public static bool HasClaimed(int taskValue) => (taskValue & 1) == 1;
    public static int MakeTaskValue(int progress, bool claimed) => Math.Max(0, Math.Min(0x3fffffff, progress)) * 2 + (claimed ? 1 : 0);

    public static string OperationalDate() => SignUpData.OperationalDate();
}

// ==================== 签到（每日+八日） ====================
public static class SignUpData
{
    // 每日签到
    public const int DailyTaskGroup = 20;
    public const int DailyTodayTask = 11001;
    public const int DailyTotalTask = 11002;
    public const int MonthDiamondTask = 11004;
    public const int MonthEnergyTask = 11005;

    public static readonly int[] DailyRewards =
    {
        // [genre, detail, particular, templateLevel, count] 扁平存储
        15,1,1,1,5000, 10,1,1,2,1, 9,12,1,1,1, 7,1,4,3,5, 5,2,1,3,1, 7,2,4,4,1, 15,2,1,1,50,
        15,1,1,1,10000, 10,1,1,2,1, 7,7,1,4,1, 7,3,1,3,5, 5,2,1,3,1, 7,2,4,4,1, 2,2,10000,1,1,
        15,1,1,1,15000, 10,1,1,2,1, 9,12,1,1,1, 7,1,4,3,5, 5,2,1,3,1, 7,2,4,4,1, 15,2,1,1,100,
        15,1,1,1,20000, 10,1,1,2,1, 7,7,1,4,1, 7,3,1,3,5, 5,2,1,3,1, 7,2,4,4,1, 7,4,1,4,1,
        15,1,1,1,25000, 10,1,1,2,1, 9,12,1,1,1,
    };

    public static int MakeDailyTaskId(int taskId) => (DailyTaskGroup << 16) | taskId;

    /// <summary>运营日：上海时间凌晨4点换日（UTC+4）</summary>
    public static string OperationalDate()
    {
        var shifted = DateTime.UtcNow.AddHours(4);
        return shifted.ToString("yyyy-MM-dd");
    }

    public static int DaysInMonth(string operationalDate)
    {
        var parts = operationalDate.Split('-');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month))
            return 30;
        return DateTime.DaysInMonth(year, month);
    }

    // 八日签到
    public const int EightDayActivityId = 29;
    public const int EightDayTaskGroup = 18;

    public record EightDayReward(int AchievementId, int RequiredDays, int[][] Awards);

    public static readonly EightDayReward[] EightDayRewards =
    {
        new(23, 1, new[] { new[] {15,2,1,1,30} }),
        new(24, 2, new[] { new[] {1,2,4,3,1} }),
        new(25, 3, new[] { new[] {2,2,8,1,1} }),
        new(26, 4, new[] { new[] {10,1,1,3,1} }),
        new(27, 5, new[] { new[] {5,2,1,2,3}, new[] {15,1,1,1,10000} }),
        new(28, 6, new[] { new[] {9,12,1,1,1} }),
        new(29, 7, new[] { new[] {11,5,15,2,1}, new[] {15,1,1,1,20000} }),
        new(30, 8, new[] { new[] {1,9,9,4,1} }),
    };

    public static int MakeEightDayTaskId(int achievementId) => (EightDayTaskGroup << 16) | achievementId;
    public static int EightDayProgress(int taskValue) => taskValue >>> 1;
    public static bool HasClaimedEightDay(int taskValue) => (taskValue & 1) == 1;
    public static int MakeEightDayTaskValue(int cumulativeDays, bool claimed) => (Math.Max(0, cumulativeDays) << 1) | (claimed ? 1 : 0);
}

// ==================== 商店 ====================
public static class ShopData
{
    public const int LuaCommandShopGoodsList = 11;

    public static object MakeShopGoodsListResponse(int shopId) => new
    {
        shopid = shopId,
        isopen = 1,
        refreshcount = 0,
        goodslist = Array.Empty<object>(),
    };
}

// ==================== IB商店/充值 ====================
public static class IbShopData
{
    public const int LuaCommandDoRecharge = 10000;
    public const int LuaCommandPayResultSuccess = 450;
    public const int IbShopTaskGroup = 42;
    public const int IbShopTaskIdBase = 10000;

    public const int FreeGiftPackId = 12;
    public static readonly int[] FreeGiftPackAward = { 14, 3, 5, 1, 1 };
    public const int FreeGiftPackDailyLimit = 1;

    public const int MonthCardItemId = 1;
    public const int MonthCardDiamonds = 300;
    public const int MonthCardDays = 30;
    public const int MonthCardTaskGroup = 1;
    public const int MonthCardTaskId = 36;
    public const int MonthCardLimitDays = 330;

    public const int ErrorUnknownItem = 20074;
    public const int ErrorLimitReached = 20075;

    public static int MakeIbShopTaskId(int shopId) => (IbShopTaskGroup << 16) | (IbShopTaskIdBase + shopId);
    public static int MakeIbItemTaskId(int itemId) => (IbShopTaskGroup << 16) | itemId;
    public static int MakeMonthCardTaskId() => (MonthCardTaskGroup << 16) | MonthCardTaskId;
}

// ==================== 悬赏战斗 ====================
public static class BountyData
{
    public const int LuaCommandJoin = 50;
    public const int LuaCommandPass = 51;
    public const int LuaCommandFail = 52;
    public const int TaskGroup = 9;
    public const int DailyRewardCount = 2;

    public record BountyLevelConfig(
        int ActivityId, int EventType, int Difficulty, string Name,
        int MapId, int EnergyCost, int RecommendedPower, int MasterExp,
        int[][] FixedAwards, int[][] DailyAwards);

    static readonly string[] DifficultyNames = { "简单", "普通", "困难", "噩梦", "地狱", "炼狱" };
    static readonly int[] EnergyCosts = { 10, 15, 20, 25, 30, 30 };
    static readonly int[] CommonPower = { 3980, 4940, 8710, 11860, 14850, 17600 };
    static readonly int[] BreakPower = { 6690, 8950, 10900, 13670, 17410, 19000 };

    static readonly Dictionary<int, int[]> MapIds = new()
    {
        {1, new[]{1000,1010,1011,1012,1030,1066}}, {2, new[]{1036,1037,1038,1039,1040,1068}},
        {3, new[]{1002,1016,1017,1018,1032,1069}}, {4, new[]{1041,1042,1043,1044,1045,1070}},
        {5, new[]{1046,1047,1048,1049,1050,1071}}, {6, new[]{1051,1052,1053,1054,1055,1072}},
        {7, new[]{1001,1013,1014,1015,1031,1067}}, {8, new[]{1022,1023,1024,1025,1033,1073}},
        {9, new[]{1003,1019,1020,1021,1034,1074}}, {10, new[]{1026,1027,1028,1029,1035,1075}},
        {11, new[]{1056,1057,1058,1059,1060,1076}}, {12, new[]{1061,1062,1063,1064,1065,1077}},
    };

    record ActivityDef(int EventType, string Label, int? Attribute, int[] OpenDays);

    static readonly Dictionary<int, ActivityDef> Activities = new()
    {
        {1, new(1, "晶币猎取", null, new[]{1,2,3,4,5,6,7})},
        {2, new(2, "生物士兵锻炼", 1, new[]{1,6,7})}, {3, new(2, "机械士兵锻炼", 3, new[]{3,6,7})},
        {4, new(2, "幽能士兵锻炼", 2, new[]{2,6,7})}, {5, new(2, "防疫士兵锻炼", 5, new[]{4,6,7})},
        {6, new(2, "侵蚀士兵锻炼", 6, new[]{5,6,7})},
        {7, new(4, "零件搜集", null, new[]{1,2,3,4,5,6,7})},
        {8, new(5, "生物界限突破", 1, new[]{1,6,7})}, {9, new(5, "机械界限突破", 3, new[]{3,6,7})},
        {10, new(5, "幽能界限突破", 2, new[]{2,6,7})}, {11, new(5, "防疫界限突破", 5, new[]{4,6,7})},
        {12, new(5, "侵蚀界限突破", 6, new[]{5,6,7})},
    };

    static readonly int[] CoinFixed = { 6400, 10400, 14400, 18400, 23200, 26400 };
    static readonly int[] CoinDaily = { 10000, 15000, 20000, 25000, 35000, 40000 };
    static readonly int[][] TrainFixed = { new[]{7,0,0}, new[]{4,2,0}, new[]{2,4,0}, new[]{4,5,0}, new[]{0,3,1}, new[]{0,2,2} };
    static readonly int[][] TrainDaily = { new[]{8,0,0}, new[]{4,2,0}, new[]{3,4,0}, new[]{3,6,0}, new[]{0,4,1}, new[]{0,1,2} };
    static readonly int[][] BreakFixed = { new[]{4,1,0}, new[]{6,2,0}, new[]{10,3,1}, new[]{14,5,2}, new[]{18,6,3}, new[]{20,7,3} };
    static readonly int[][] BreakDaily = { new[]{3,1,0}, new[]{5,1,0}, new[]{9,2,0}, new[]{12,4,1}, new[]{16,5,2}, new[]{20,7,3} };

    static int[][] Rewards(int genre, int detail, int particular, int[] counts)
    {
        var result = new List<int[]>();
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > 0) result.Add(new[] { genre, detail, particular, i + 1, counts[i] });
        return result.ToArray();
    }

    static readonly Dictionary<string, BountyLevelConfig> Levels = new();

    static BountyData()
    {
        foreach (var activityId in Activities.Keys)
        {
            for (int diff = 1; diff <= 6; diff++)
            {
                var act = Activities[activityId];
                int idx = diff - 1;
                int[][] fixedAwards, dailyAwards;
                if (act.EventType == 1)
                {
                    fixedAwards = new[] { new[] { 15, 1, 1, 1, CoinFixed[idx] } };
                    dailyAwards = new[] { new[] { 15, 1, 1, 1, CoinDaily[idx] } };
                }
                else if (act.EventType == 2)
                {
                    fixedAwards = Rewards(7, 1, act.Attribute!.Value, TrainFixed[idx]);
                    dailyAwards = Rewards(7, 1, act.Attribute!.Value, TrainDaily[idx]);
                }
                else if (act.EventType == 4)
                {
                    fixedAwards = Rewards(7, 3, 1, TrainFixed[idx]);
                    dailyAwards = Rewards(7, 3, 1, TrainDaily[idx]);
                }
                else
                {
                    fixedAwards = Rewards(7, 2, act.Attribute!.Value, BreakFixed[idx]);
                    dailyAwards = Rewards(7, 2, act.Attribute!.Value, BreakDaily[idx]);
                }
                Levels[$"{activityId}:{diff}"] = new BountyLevelConfig(
                    activityId, act.EventType, diff, $"{act.Label}-{DifficultyNames[idx]}",
                    MapIds[activityId][idx], EnergyCosts[idx],
                    act.EventType == 5 ? BreakPower[idx] : CommonPower[idx],
                    EnergyCosts[idx], fixedAwards, dailyAwards);
            }
        }
    }

    public static BountyLevelConfig? GetLevel(int activityId, int difficulty)
        => Levels.TryGetValue($"{activityId}:{difficulty}", out var l) ? l : null;

    public static int EffectiveEnergyCost(BountyLevelConfig level, int playerLevel)
        => playerLevel < 35 ? (int)Math.Ceiling(level.EnergyCost / 2.0) : level.EnergyCost;

    public static int MakePassTaskId(int activityId) => (TaskGroup << 16) | (100 + activityId);
    public static int MakeDailyTaskId(int eventType) => (TaskGroup << 16) | (4000 + eventType);

    public static string OperationalDate()
    {
        var shifted = DateTime.UtcNow.AddHours(-4 + 8); // UTC-4 → Shanghai
        return shifted.ToString("yyyy-MM-dd");
    }

    static int ShanghaiWeekday()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return (int)now.DayOfWeek == 0 ? 7 : (int)now.DayOfWeek;
    }

    public static bool IsOpen(int activityId)
        => Activities.TryGetValue(activityId, out var act) && act.OpenDays.Contains(ShanghaiWeekday());
}

// ==================== 咖啡馆 ====================
public static class CafeData
{
    public const int LuaCommandCafeData = 112;
    public const int LuaCommandSetWaiterList = 113;
    public const int LuaCommandGenerateCustomer = 115;
    public const int LuaCommandMakeCoffee = 119;
    public const int LuaCommandAddGuestWeight = 124;
    public const int LuaCommandFurnitureCount = 241;

    public static readonly Dictionary<string, int> InitialCoffeeTaskValues = new()
    {
        [$"{(23 << 16) | 1}"] = 1 << 8,
        [$"{(23 << 16) | 2}"] = 1 << 8,
        [$"{(23 << 16) | 3}"] = 1 << 8,
        [$"{(23 << 16) | 4}"] = 1 << 8,
    };

    public record CafeCoffee(int coffeetype, int count);

    public static object MakeInitialCafeData(long nowSeconds, CafeCoffee[]? coffees = null) => new
    {
        basetime = nowSeconds,
        level = 1,
        hot = 0,
        comfort = 0,
        seatlist = Array.Empty<object>(),
        customerqueue = Array.Empty<object>(),
        coffeelist = coffees ?? Array.Empty<CafeCoffee>(),
        roomgirlslist = new object[] { Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>() },
        weightlist = Array.Empty<object>(),
        visitedList = Array.Empty<object>(),
        boxstatelist = Array.Empty<object>(),
        petstatelist = Array.Empty<object>(),
        petlocklist = Array.Empty<object>(),
        nextpetid = 2,
    };

    public static object MakeCoffeeResponse(CafeCoffee[] coffees) => new { coffeelist = coffees };

    public static object MakeCustomerQueue(long nowSeconds) => new
    {
        basetime = nowSeconds,
        customerqueue = new[] { new { customertype = 201, customeridx = 1, starttime = nowSeconds } },
    };
}

// ==================== 武器技能 ====================
public static class WeaponSkillData
{
    public const int WeaponGenre = 2;
    public const int SkillTypePassive = 2;

    public record WeaponTemplate(int Rarity, int PassiveSkill1, int PassiveSkill2);
    public record WeaponSkillInfo(int SkillId, int SkillLevel, int SkillType);
    public record WeaponEntry(int Genre, int Detail, int Particular, int Level, int Rarity);

    static Dictionary<string, WeaponTemplate>? _templates;
    static List<WeaponEntry>? _allWeapons;
    static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _templates = new Dictionary<string, WeaponTemplate>();
        _allWeapons = new List<WeaponEntry>();

        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "resources", "weapon", "weapons.txt"),
            Path.Combine(Directory.GetCurrentDirectory(), "resources", "weapon", "weapons.txt"),
            @"C:\Users\23684\Desktop\gcg2-csharp-server\resources\weapon\weapons.txt",
        };
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var lines = File.ReadAllLines(path).Skip(3);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cols = line.Split('\t');
                    if (cols.Length < 7) continue;
                    if (!int.TryParse(cols[0], out var genre) || !int.TryParse(cols[1], out var detail)
                        || !int.TryParse(cols[2], out var particular) || !int.TryParse(cols[3], out var level)
                        || !int.TryParse(cols[4], out var rarity) || !int.TryParse(cols[5], out var ps1)
                        || !int.TryParse(cols[6], out var ps2)) continue;
                    _templates[$"{genre}:{detail}:{particular}:{level}"] = new WeaponTemplate(rarity, ps1, ps2);
                    _allWeapons!.Add(new WeaponEntry(genre, detail, particular, level, rarity));
                }
                Console.WriteLine($"[INFO] Loaded {_templates.Count} weapon templates");
                return;
            }
            catch (Exception ex) { Console.WriteLine($"[WARN] weapon load error: {ex.Message}"); }
        }
        Console.WriteLine("[WARN] weapons.txt not found, weapon skills empty");
    }

    public static WeaponSkillInfo[] WeaponPassiveSkills(int genre, int detail, int particular, int level, int breakLevel)
    {
        if (genre != WeaponGenre) return Array.Empty<WeaponSkillInfo>();
        EnsureLoaded();
        if (_templates == null || !_templates.TryGetValue($"{genre}:{detail}:{particular}:{level}", out var tpl))
            return Array.Empty<WeaponSkillInfo>();
        int skillLevel = breakLevel + 1;
        return new[] { tpl.PassiveSkill1, tpl.PassiveSkill2 }
            .Where(id => id > 0)
            .Select(id => new WeaponSkillInfo(id, skillLevel, SkillTypePassive))
            .ToArray();
    }

    public static IReadOnlyList<WeaponEntry> GetAllWeapons()
    {
        EnsureLoaded();
        return _allWeapons ?? (IReadOnlyList<WeaponEntry>)Array.Empty<WeaponEntry>();
    }
}

// ==================== 角色卡数据 ====================
public static class CharacterCardData
{
    public static readonly int[] PlayableGirlIds =
        { 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,201,202,203,204 };

    static readonly HashSet<int> PlayableGirlIdSet = new(PlayableGirlIds);

    static readonly Dictionary<string, int> ModelOverrides = new()
    {
        ["1:71:4"]=7001, ["1:81:5"]=8001, ["2:71:4"]=7001, ["2:81:5"]=8001,
        ["3:71:4"]=7001, ["3:81:5"]=8001, ["3:82:5"]=8002, ["4:81:5"]=8001,
        ["5:71:4"]=7001, ["6:71:4"]=7001, ["6:81:5"]=8001, ["7:71:4"]=7001,
        ["7:81:5"]=8001, ["7:82:5"]=8002, ["8:71:4"]=7001, ["8:81:5"]=8001,
        ["9:71:4"]=7001, ["9:72:4"]=7002, ["9:82:5"]=8002, ["10:71:4"]=7001,
        ["10:81:5"]=8001, ["10:82:5"]=8002, ["11:71:4"]=7001, ["11:81:5"]=8001,
        ["12:71:3"]=7001, ["12:72:4"]=7002, ["12:81:5"]=8001, ["13:71:4"]=7001,
        ["13:81:5"]=8001, ["14:81:5"]=8001, ["15:71:4"]=7001, ["15:81:5"]=8001,
        ["16:71:4"]=7001, ["16:81:5"]=8001,
    };

    public static bool IsPlayableGirlId(int girlId) => PlayableGirlIdSet.Contains(girlId);

    public static int? CharacterCardModelId(int genre, int detail, int particular, int templateLevel)
    {
        if (genre != 1 || !IsPlayableGirlId(detail) || particular <= 0) return null;
        return ModelOverrides.TryGetValue($"{detail}:{particular}:{templateLevel}", out var id) ? id : particular;
    }

    // 从抽卡池文件读取所有角色卡（去重），返回 (girlId, particular, star) 列表
    static List<(int girlId, int costumeId, int star)>? _allCards;
    public static List<(int girlId, int costumeId, int star)> GetAllCards()
    {
        if (_allCards != null) return _allCards;
        _allCards = new();
        var seen = new HashSet<int>();
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "resources", "gacha", "pools"),
            Path.Combine(Directory.GetCurrentDirectory(), "resources", "gacha", "pools"),
            @"C:\Users\23684\Desktop\gcg2-csharp-server\resources\gacha\pools",
        };
        var poolDir = searchPaths.FirstOrDefault(Directory.Exists);
        if (poolDir == null)
        {
            Console.WriteLine("[WARN] gacha pools dir not found, GetAllCards empty");
            return _allCards;
        }
        Console.WriteLine($"[INFO] GetAllCards loading from: {poolDir}");
        foreach (var file in Directory.GetFiles(poolDir, "card_*_zh.txt"))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 3; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 1) continue;
                if (!int.TryParse(parts[0], out var cardId)) continue;
                if (cardId <= 0 || seen.Contains(cardId)) continue;
                seen.Add(cardId);
                // 角色卡ID编码：女孩ID*100 + 服装ID*10 + 星级
                int girlId = cardId / 100;
                int costumeId = (cardId / 10) % 10;
                int star = cardId % 10;
                if (girlId <= 0 || costumeId <= 0 || star <= 0) continue;
                _allCards.Add((girlId, costumeId, star));
            }
        }
        Console.WriteLine($"[INFO] GetAllCards loaded {_allCards.Count} cards from gacha pools");
        return _allCards;
    }
}
