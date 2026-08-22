namespace Gcg2OfflineServer.GameData;

/// <summary>
/// 章节星级奖励配置。
/// 第1-3章每章6关，第4章起每章12关，星级奖励阈值翻倍。
/// </summary>
public static class ChapterStarAwardData
{
    public const int TaskGroup = 14;

    public class StarAward
    {
        public int Chapter { get; set; }
        public int Difficulty { get; set; }
        public int Position { get; set; } // 1-3
        public int RequiredStars { get; set; }
        public List<int[]> Awards { get; set; } = new();
    }

    public static StarAward? Get(int chapter, int difficulty, int position)
    {
        if (chapter <= 0 || chapter > 16) return null;
        if (difficulty != 1 && difficulty != 2) return null;
        if (position <= 0 || position > 3) return null;

        var longChapter = chapter >= 4;
        var requiredStars = (longChapter ? 12 : 6) * position;
        var diamondCount = (longChapter ? 20 : 10) * position;
        var goldCount = position switch
        {
            1 => (difficulty == 1 && !longChapter) ? 1500 : 3000,
            2 => (difficulty == 1 && !longChapter) ? 2000 : 4000,
            _ => (difficulty == 1 && !longChapter) ? 3000 : 6000,
        };
        var material = difficulty == 1
            ? new[] { 7, 1, 4, longChapter ? 2 : 1, 1 }
            : new[] { 7, 3, 1, longChapter ? 3 : 2, longChapter ? 1 : 2 };

        return new StarAward
        {
            Chapter = chapter,
            Difficulty = difficulty,
            Position = position,
            RequiredStars = requiredStars,
            Awards = new List<int[]>
            {
                new[] { 15, 2, 1, 1, diamondCount },
                new[] { 15, 1, 1, 1, goldCount },
                material,
            },
        };
    }

    public static long MakeTaskId(int chapter, int difficulty)
    {
        var taskId = difficulty | (chapter << 8);
        return ((long)TaskGroup << 16) | (uint)taskId;
    }

    public static int CompletedStarCount(int starValue)
    {
        var mask = starValue & 0b111;
        return (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1);
    }

    public static int ChapterTotalStars(IEnumerable<Models.LevelState> levels, int chapter, int difficulty)
    {
        return levels.Where(l =>
        {
            var lc = (int)(l.Id >> 16);
            var ld = (int)(l.Id & 0xff);
            return lc == chapter && ld == difficulty;
        }).Sum(l => CompletedStarCount((int)l.Star));
    }

    public static long MakeTaskValue(int totalStars, int claimedMask)
    {
        return (Math.Max(0, totalStars) << 8) | (claimedMask & 0xff);
    }

    public static int TaskProgress(long taskValue) => (int)(taskValue >> 8);
    public static int ClaimedMask(long taskValue) => (int)(taskValue & 0xff);

    public static bool HasClaimed(long taskValue, int position)
    {
        return (ClaimedMask(taskValue) & (1 << (position - 1))) != 0;
    }

    public static long MarkClaimed(long taskValue, int position)
    {
        return taskValue | (uint)(1 << (position - 1));
    }
}
