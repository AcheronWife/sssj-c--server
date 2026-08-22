using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Gcg2OfflineServer.GameData;

/// <summary>
/// 章节/关卡配置，从 resources/map/chapter.txt 加载（TSV格式）。
/// 关卡配置：Index 从 0 开始，每章关卡数不固定。
/// </summary>
public static class ChapterConfig
{
    public class LevelInfo
    {
        public int Chapter { get; set; }
        public int Index { get; set; }
        public int Difficulty { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PreCost { get; set; }
        public int Vigour { get; set; }
        public int RandDropNum { get; set; }
        public int MasterExp { get; set; }
        public int CardExp { get; set; }
        public List<int[]> FirstAwards { get; set; } = new();
        public List<(int[] award, int weight)> FixedAwards { get; set; } = new();
        public List<(int[] award, int weight)> RandomAwards { get; set; } = new();
        public long Id => ((long)Chapter << 16) | ((long)Index << 8) | (uint)Difficulty;
    }

    private static readonly ConcurrentDictionary<string, LevelInfo> _levels = new();
    private static bool _loaded;
    private static readonly object _loadLock = new();

    /// <summary>从 resources/map/chapter.txt 加载关卡配置（懒加载，线程安全）。</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            LoadFromFile();
            _loaded = true;
        }
    }

    private static void LoadFromFile()
    {
        // 优先从程序运行目录找，回退到项目源码目录
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "resources", "map", "chapter.txt"),
            Path.Combine(Directory.GetCurrentDirectory(), "resources", "map", "chapter.txt"),
            @"C:\Users\23684\Desktop\gcg2-csharp-server\resources\map\chapter.txt",
        };

        var filePath = searchPaths.FirstOrDefault(File.Exists);
        if (filePath == null)
        {
            Console.WriteLine("[WARN] chapter.txt not found, chapter config empty");
            return;
        }

        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 4) return;

        // 第3行（index 2）是表头
        var headers = lines[2].Split('\t');
        var headerIndex = new Dictionary<string, int>();
        for (int i = 0; i < headers.Length; i++)
            headerIndex[headers[i].Trim()] = i;

        int GetCol(string name) => headerIndex.TryGetValue(name, out var idx) ? idx : -1;
        int ColInt(string[] values, string name)
        {
            var col = GetCol(name);
            if (col < 0 || col >= values.Length) return 0;
            return int.TryParse(values[col], out var v) ? v : 0;
        }
        string ColStr(string[] values, string name)
        {
            var col = GetCol(name);
            if (col < 0 || col >= values.Length) return "";
            return values[col];
        }

        var awardRegex = new Regex(@"\[([0-9-]+)\]");
        List<int[]> ParseAwards(string value)
        {
            if (string.IsNullOrEmpty(value)) return new List<int[]>();
            return awardRegex.Matches(value)
                .Select(m => m.Groups[1].Value.Split('-').Select(int.Parse).ToArray())
                .Where(a => a.Length >= 5)
                .ToList();
        }

        for (int i = 3; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = line.Split('\t');

            var chapter = ColInt(values, "Chapter");
            var index = ColInt(values, "Index");
            var difficulty = ColInt(values, "Difficult");
            if (chapter <= 0 || difficulty <= 0) continue;
            // Index 可以是 0（第1章第0关）

            var firstAwards = ParseAwards(ColStr(values, "FirstAward"));
            var fixedAwardsRaw = ParseAwards(ColStr(values, "FixedAward"));
            var randomAwardsRaw = ParseAwards(ColStr(values, "RandomAward"));

            var fixedAwards = fixedAwardsRaw.Select(a => (a, a.Length > 5 ? a[5] : 10000)).ToList();
            var randomAwards = randomAwardsRaw.Select(a => (a, a.Length > 5 ? a[5] : 0)).ToList();

            var level = new LevelInfo
            {
                Chapter = chapter,
                Index = index,
                Difficulty = difficulty,
                Name = ColStr(values, "name"),
                PreCost = ColInt(values, "PreCost"),
                Vigour = ColInt(values, "Vigour"),
                RandDropNum = ColInt(values, "RandDropNum"),
                MasterExp = ColInt(values, "MasterExp"),
                CardExp = ColInt(values, "CardExp"),
                FirstAwards = firstAwards,
                FixedAwards = fixedAwards,
                RandomAwards = randomAwards,
            };

            _levels[Key(chapter, index, difficulty)] = level;
        }

        Console.WriteLine($"[INFO] Loaded {_levels.Count} chapter levels from chapter.txt");
    }

    private static string Key(int chapter, int index, int difficulty) => $"{chapter}:{index}:{difficulty}";

    public static LevelInfo? Get(int chapter, int index, int difficulty)
    {
        EnsureLoaded();
        return _levels.TryGetValue(Key(chapter, index, difficulty), out var level) ? level : null;
    }

    public static LevelInfo? Get(long levelId)
    {
        var chapter = (int)(levelId >> 16);
        var index = (int)((levelId >> 8) & 0xFF);
        var difficulty = (int)(levelId & 0xFF);
        return Get(chapter, index, difficulty);
    }

    public static IReadOnlyList<LevelInfo> AllLevels
    {
        get { EnsureLoaded(); return _levels.Values.ToList(); }
    }

    /// <summary>计算实际体力消耗（35级以下半价）。</summary>
    public static int EffectiveEnergyCost(LevelInfo level, int playerLevel)
    {
        var baseCost = level.PreCost + level.Vigour;
        return playerLevel < 35 ? (int)Math.Ceiling(baseCost / 2.0) : baseCost;
    }

    public static (int chapterId, int difficulty, int index) ParseLevelId(long id)
    {
        var chapterId = (int)(id >> 16);
        var difficulty = (int)((id >> 8) & 0xFF);
        var index = (int)(id & 0xFF);
        return (chapterId, difficulty, index);
    }
}
