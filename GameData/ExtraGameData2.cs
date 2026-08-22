using System;
using System.Collections.Generic;
using System.Linq;

namespace Gcg2OfflineServer.GameData;

// ==================== 女孩礼物 ====================
public static class GirlGiftData
{
    public const int GiftGenre = 5;
    public const int LinkGirlMinId = 201;
    public const int LinkGirlMaxId = 204;
    public const int MaxAffectionLevel = 100;
    public const int LinkGirlMaxAffectionLevel = 50;

    const double FavoriteGiftScale = 1.25;
    const double LinkFavoriteGiftScale = 2;

    public static bool IsLinkGirl(int girlId) => girlId >= LinkGirlMinId && girlId <= LinkGirlMaxId;
    public static int AffectionMaxLevel(int girlId) => IsLinkGirl(girlId) ? LinkGirlMaxAffectionLevel : MaxAffectionLevel;

    static string GdplKey(int[] g) => $"{g[0]}-{g[1]}-{g[2]}-{g[3]}";

    static readonly Dictionary<string, int> GiftExperienceMap = new();
    static readonly Dictionary<int, int[][]> FavoriteGifts = new();

    static GirlGiftData()
    {
        for (int girlId = 1; girlId <= 16; girlId++)
        {
            GiftExperienceMap[GdplKey(new[] { 5, 1, girlId, 2 })] = 60;
            GiftExperienceMap[GdplKey(new[] { 5, 1, girlId, 4 })] = 300;
            FavoriteGifts[girlId] = new[] { new[] { 5, 1, girlId, 2 }, new[] { 5, 1, girlId, 4 } };
        }
        foreach (var girlId in new[] { 201, 202, 203, 204 })
        {
            GiftExperienceMap[GdplKey(new[] { 5, 1, girlId, 4 })] = 500;
            FavoriteGifts[girlId] = new[] { new[] { 5, 1, girlId, 4 } };
        }
        var generic = new (int p, int l, int exp)[]
        {
            (1,1,30),(1,2,60),(1,3,150),(1,4,300),(1,5,150),(1,6,300),(1,7,300),(1,8,300),
        };
        foreach (var (p, l, exp) in generic)
            GiftExperienceMap[GdplKey(new[] { 5, 2, 1, l })] = exp;
    }

    public static int? GiftBaseExperience(int[] gdpl)
        => GiftExperienceMap.TryGetValue(GdplKey(gdpl), out var e) ? e : null;

    public static bool IsFavoriteGift(int girlId, int[] gdpl)
        => FavoriteGifts.TryGetValue(girlId, out var favs) && favs.Any(f => GdplKey(f) == GdplKey(gdpl));

    public static int? GiftExperience(int girlId, int[] gdpl)
    {
        var baseExp = GiftBaseExperience(gdpl);
        if (baseExp == null) return null;
        if (!IsFavoriteGift(girlId, gdpl)) return baseExp;
        return (int)Math.Floor(baseExp.Value * (IsLinkGirl(girlId) ? LinkFavoriteGiftScale : FavoriteGiftScale));
    }

    static readonly int[] GirlExpToNext =
    {
        0,100,105,109,116,123,132,142,153,165,178,192,207,224,241,259,278,298,319,341,
        364,387,412,437,463,490,518,547,577,607,638,670,703,736,771,806,842,878,916,954,
        993,1032,1073,1114,1156,1198,1242,1286,1331,1376,1422,1469,1517,1565,1614,1664,1714,
        1765,1817,1869,1923,1976,2031,2086,2142,2198,2255,2313,2372,2431,2491,2551,2612,2674,
        2736,2799,2863,2927,2992,3058,3124,3191,3258,3326,3395,3464,3534,3605,3676,3748,3821,
        3894,3967,4042,4116,4192,4268,4345,4422,4500,0,
    };

    static readonly int[] LinkGirlExpToNext =
    {
        0,300,315,330,345,360,375,390,410,430,450,470,490,510,535,560,585,610,635,665,
        695,725,755,790,825,860,895,935,975,1015,1060,1105,1150,1200,1250,1300,1355,1410,
        1470,1530,1595,1660,1730,1800,1875,1950,2030,2115,2200,2290,0,
    };

    public record AffectionGain(int AddedExperience, int OldExperience, int NewExperience,
        int OldLevel, int NewLevel, bool ReachedMaxLevel);

    public static AffectionGain? AddAffection(int girlId, int level, int experience, int value)
    {
        if (value <= 0) return null;
        int maxLevel = AffectionMaxLevel(girlId);
        if (level >= maxLevel) return null;
        var table = IsLinkGirl(girlId) ? LinkGirlExpToNext : GirlExpToNext;
        int total = experience + value;
        int virtualLevel = level;
        int required = table[virtualLevel];
        while (total >= required)
        {
            total -= required;
            virtualLevel++;
            if (virtualLevel >= maxLevel) { value -= total; total = 0; break; }
            required = table[virtualLevel];
        }
        return new AffectionGain(value, experience, total, level, virtualLevel, virtualLevel >= maxLevel);
    }

    public static int? LevelAwardIndex(int level)
    {
        if (level <= 0 || level % 10 != 0) return null;
        int idx = level / 10;
        return idx >= 1 && idx <= 20 ? idx : null;
    }
}

// ==================== 女孩训练 ====================
public static class GirlTrainingData
{
    public const int DefaultOutdoorId = 72;
    public const int MaxConcurrent = 4;

    public record TrainingConfig(int Type, int DurationSeconds, int LoveReward, int CrystalReward, int MaximumPositions, int VigorCost);

    static readonly Dictionary<int, TrainingConfig> Configs = new()
    {
        {1, new(1, 3600, 60, 1000, 3, 0)},
        {2, new(2, 7200, 120, 1800, 3, 0)},
        {3, new(3, 14400, 240, 3200, 3, 0)},
        {4, new(4, 28800, 480, 5000, 3, 0)},
    };

    public static TrainingConfig? GetConfig(int position)
    {
        if (position <= 0) return null;
        int type = position / 10;
        int slot = position % 10;
        if (!Configs.TryGetValue(type, out var cfg)) return null;
        if (slot < 1 || slot > cfg.MaximumPositions) return null;
        return cfg;
    }
}

// ==================== 短信 ====================
public static class PhoneMessageData
{
    public record PhoneLetter(int TopicId, int Initiator, int[] ReplyPositions);

    static readonly Dictionary<int, PhoneLetter> Letters = new()
    {
        {10001, new(10001, 7, new[] {2})},
        {1, new(1, 111, Array.Empty<int>())},
    };

    public static PhoneLetter? GetLetter(int topicId) => Letters.TryGetValue(topicId, out var l) ? l : null;

    public static int? MakeReplyId(PhoneLetter def, int selectionId, int[] completedReplyIds)
    {
        if (selectionId <= 0 || selectionId > 9) return null;
        var position = def.ReplyPositions.FirstOrDefault(p =>
            !completedReplyIds.Any(rid => rid / 10 == p));
        return position == 0 ? null : position * 10 + selectionId;
    }
}

// ==================== 引导 ====================
public static class GuideData
{
    public const int LuaCommandWriteGuideLog = 102;

    public record GuideLogAck(int NTimming, object GuideId, int StepId, string GuideType);

    public static GuideLogAck? ParseGuideLog(Dictionary<string, object>? parameters)
    {
        if (parameters == null) return null;
        if (!parameters.TryGetValue("tbParam", out var tbParamObj) || tbParamObj is not Dictionary<string, object> guide)
            guide = parameters;
        if (!guide.TryGetValue("GuideID", out var guideId) || guideId == null) return null;
        if (!guide.TryGetValue("StepID", out var stepIdObj) || !int.TryParse(stepIdObj?.ToString(), out var stepId)) return null;
        if (!guide.TryGetValue("GuideType", out var guideTypeObj) || guideTypeObj == null) return null;
        if (!guide.TryGetValue("nTimming", out var nTimmingObj) || !int.TryParse(nTimmingObj?.ToString(), out var nTimming)) return null;
        return new GuideLogAck(nTimming, guideId, stepId, guideTypeObj.ToString()!);
    }
}

// ==================== 角色卡分解+强化 ====================
public static class CardEnhancementData
{
    public const int LuaCommandDecompose = 1;
    public const int MaxDecomposeCount = 40;
    public const int LuaCommandLevelUpCommon = 5;

    const double DecomposeExpRate = 0.3;

    static readonly Dictionary<int, int> BaseGoldByRarity = new() { {1,500},{2,1000},{3,2000},{4,10000},{5,100} };
    static readonly Dictionary<int, int> TokenCountByRarity = new() { {1,0},{2,10},{3,40},{4,200},{5,0} };

    public static readonly int[] ExpToNextLevel =
    {
        133,187,263,406,562,730,907,1092,1286,1485,1691,1904,2121,2345,2573,2806,3043,3285,
        3532,3782,4035,4294,4554,4820,5088,5359,5635,5912,6193,6478,6764,7054,7346,7641,7940,
        8240,8542,8849,9156,13394,14165,14963,15784,16630,17503,18399,19321,20269,21243,22242,
        23267,24320,25398,26502,27634,28792,29977,31189,32429,33695,34990,36312,37661,39040,
        40445,41879,43341,44832,46351,
    };

    public record DecomposeReward(int Gold, int TokenCount);

    public static int CumulativeExperience(int level)
    {
        if (level <= 1) return 0;
        int count = Math.Min(level - 1, ExpToNextLevel.Length);
        int sum = 0;
        for (int i = 0; i < count; i++) sum += ExpToNextLevel[i];
        return sum;
    }

    public static DecomposeReward? DecompositionReward(int level, int rarity)
    {
        if (!BaseGoldByRarity.TryGetValue(rarity, out var baseGold) || !TokenCountByRarity.TryGetValue(rarity, out var token))
            return null;
        return new DecomposeReward(
            (int)Math.Floor(CumulativeExperience(level) * DecomposeExpRate + rarity * baseGold), token);
    }

    public record ExpMaterial(int Genre, int Detail, int Particular, int TemplateLevel, int Experience, int CoinCost);

    static readonly int[] ExpCardParticulars = { 1, 2, 3, 5, 6, 4 };
    static readonly int[] ExpCardBaseExperience = { 500, 2500, 10000, 50000 };
    const double ClientSelectableExpRate = 1.5;

    public static readonly Dictionary<int, ExpMaterial> ExpMaterials = new();

    static CardEnhancementData()
    {
        int id = 1;
        foreach (var baseExp in ExpCardBaseExperience)
            foreach (var particular in ExpCardParticulars)
            {
                int tierIndex = Array.IndexOf(ExpCardBaseExperience, baseExp);
                ExpMaterials[id++] = new ExpMaterial(7, 1, particular, tierIndex + 1,
                    (int)Math.Floor(baseExp * ClientSelectableExpRate), baseExp);
            }
    }

    public static (int level, int experience) AddExperience(int level, int currentExperience, int addedExperience)
    {
        int nextLevel = level;
        int experience = currentExperience + addedExperience;
        while (nextLevel <= ExpToNextLevel.Length)
        {
            int required = ExpToNextLevel[nextLevel - 1];
            if (experience < required) break;
            experience -= required;
            nextLevel++;
        }
        return (nextLevel, experience);
    }
}

// ==================== 武器分解+强化 ====================
public static class WeaponEnhancementData
{
    public const int LuaCommandDecompose = 2;
    public const int MaxDecomposeCount = 40;

    const double DecomposeExpRate = 0.3;

    static readonly Dictionary<int, int> BaseGoldByRarity = new() { {1,500},{2,1000},{3,2000},{4,10000},{5,100} };
    static readonly Dictionary<int, int> TokenCountByRarity = new() { {1,0},{2,5},{3,25},{4,250},{5,0} };

    public record DecomposeReward(int Gold, int TokenCount);

    static readonly int[] ExpToNextLevel =
    {
        133,187,263,406,562,730,907,1092,1286,1485,1691,1904,2121,2345,2573,2806,3043,3285,
        3532,3782,4035,4294,4554,4820,5088,5359,5635,5912,6193,6478,6764,7054,7346,7641,7940,
        8240,8542,8849,9156,13394,14165,14963,15784,16630,17503,18399,19321,20269,21243,22242,
        23267,24320,25398,26502,27634,28792,29977,31189,32429,33695,34990,36312,37661,39040,
        40445,41879,43341,44832,46351,48917,51109,53396,55782,58271,60869,63578,66405,69354,72319,0,
    };

    static readonly Dictionary<int, int> RarityExperience = new() { {1,500},{2,2000},{3,5000},{4,10000} };

    public record ExpMaterial(int Genre, int Detail, int Particular, int TemplateLevel, int Experience, int CoinCost);

    public static readonly ExpMaterial[] ExpMaterials =
        new[] { 500, 2500, 10000, 50000 }.Select((v, i) => new ExpMaterial(7, 3, 1, i + 1, v, v)).ToArray();

    public static int ExperienceBeforeLevel(int level)
    {
        int result = 0;
        for (int i = 1; i < level && i <= ExpToNextLevel.Length; i++)
            result += ExpToNextLevel[i - 1];
        return result;
    }

    public static DecomposeReward? DecompositionReward(int level, int rarity)
    {
        if (!BaseGoldByRarity.TryGetValue(rarity, out var baseGold) || !TokenCountByRarity.TryGetValue(rarity, out var token))
            return null;
        return new DecomposeReward(
            (int)Math.Floor(ExperienceBeforeLevel(level) * DecomposeExpRate + rarity * baseGold), token);
    }

    public static int MaximumLevel(int rarity, int breakLevel)
    {
        if (rarity < 1 || rarity > 4) return 0;
        int maxBreak = rarity <= 2 ? 3 : 4;
        return 40 + Math.Min(Math.Max(0, breakLevel), maxBreak) * 10;
    }

    public static (int experience, int coinCost)? SacrificedWeaponValue(int level, int rarity)
    {
        if (!RarityExperience.TryGetValue(rarity, out var rarityExp) || level <= 0) return null;
        return (
            (int)Math.Floor(Math.Min(ExperienceBeforeLevel(level) * 0.3, 150000)) + rarityExp,
            rarityExp);
    }

    public static (int level, int experience) AddExperience(int level, int currentExperience, int addedExperience, int maximumLevel)
    {
        int destLevel = level;
        int destExp = currentExperience + addedExperience;
        if (destLevel >= maximumLevel) return (maximumLevel, destExp);
        while (destLevel < maximumLevel)
        {
            int required = ExpToNextLevel[destLevel - 1];
            if (required <= 0 || destExp < required) break;
            destExp -= required;
            destLevel++;
        }
        if (destLevel >= maximumLevel)
        {
            int retained = ExpToNextLevel[maximumLevel - 1];
            destExp = Math.Min(destExp, retained);
        }
        return (destLevel, destExp);
    }
}

// ==================== 背景Lua数据（登录时的空响应） ====================
public static class BackgroundLuaData
{
    public const int LuaCommandFriendList = 80;
    public const int LuaCommandVisitingCardData = 94;
    public const int LuaCommandRandomEvent = 170;
    public const int LuaCommandGirlTestOpenPeriod = 202;
    public const int LuaCommandPromiseIsOpen = 230;
    public const int LuaCommandPromiseGirls = 231;
    public const int LuaCommandAssistList = 250;
    public const int LuaCommandClubInfo = 10002;

    static readonly HashSet<int> BackgroundCommands = new()
    {
        LuaCommandFriendList, LuaCommandVisitingCardData, LuaCommandRandomEvent,
        LuaCommandGirlTestOpenPeriod, LuaCommandPromiseIsOpen, LuaCommandPromiseGirls,
        LuaCommandAssistList, LuaCommandClubInfo,
    };

    public static bool IsBackgroundCommand(int command) => BackgroundCommands.Contains(command);

    public static object? MakeResponse(int command, Dictionary<string, object>? parameters)
    {
        switch (command)
        {
            case LuaCommandFriendList:
                int reqType = 1;
                if (parameters != null && parameters.TryGetValue("reqfriendtype", out var rt) && int.TryParse(rt?.ToString(), out var rtVal))
                    reqType = rtVal;
                return new { reqfriendtype = reqType, HYList = Array.Empty<object>(), FXList = Array.Empty<object>(), SQList = Array.Empty<object>(), HMDList = Array.Empty<object>(), BindList = Array.Empty<object>() };
            case LuaCommandVisitingCardData:
                return new { VisitingCardID = 0, PlayerListSkinID = 0, ChatBubbleID = 0 };
            case LuaCommandRandomEvent:
                return new { };
            case LuaCommandGirlTestOpenPeriod:
                return new { Result = 0 };
            case LuaCommandPromiseIsOpen:
                return new { PromiseIsOpen = false };
            case LuaCommandPromiseGirls:
                return Array.Empty<object>();
            case LuaCommandAssistList:
                return Array.Empty<object>();
            case LuaCommandClubInfo:
                return new { };
            default:
                return null;
        }
    }
}
