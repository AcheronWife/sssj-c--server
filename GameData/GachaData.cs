using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Gcg2OfflineServer.GameData;

/// <summary>
/// 抽卡数据模块，从 resources/gacha/ 加载卡池配置。
/// 保留简化抽卡逻辑作为回退。
/// </summary>
public static class GachaData
{
    public class Gdpl
    {
        public int Genre { get; set; }
        public int Detail { get; set; }
        public int Particular { get; set; }
        public int Level { get; set; }
        public int[] ToArray() => new[] { Genre, Detail, Particular, Level };
    }

    public class GachaCard
    {
        public int Id { get; set; }
        public Gdpl Gdpl { get; set; } = new();
        public int Rarity { get; set; }
        public int Rate { get; set; }
        public int UpFlag { get; set; }
        public int Sort { get; set; }
    }

    public class GachaPool
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CashType { get; set; }
        public int MoneyCostOne { get; set; }
        public int MoneyCostTen { get; set; }
        public int JudgeType { get; set; }
        public int ProtectNum { get; set; } // 四星保底次数
        public int ProtectUpNum { get; set; } // UP保底次数
        public int SetUpNum { get; set; }
        public int UpBoxNum { get; set; }
        public int StarSeedId { get; set; }
        public int HedgeSeedId { get; set; }
        public Dictionary<int, List<GachaCard>> NormalCards { get; set; } = new();
        public Dictionary<int, List<GachaCard>> HedgeCards { get; set; } = new();
    }

    public class GachaAward
    {
        public int[] TbGDPL { get; set; } = Array.Empty<int>();
        public int NId { get; set; }
        public int NTimes { get; set; }
        public bool IsUp { get; set; }
        public int NUpTimes { get; set; }
        public int NTotalTimes { get; set; }
        public bool BFirstGet { get; set; }
        public bool BHasCard { get; set; }
    }

    private static readonly ConcurrentDictionary<int, GachaPool> _pools = new();
    private static readonly Dictionary<int, Dictionary<int, List<(int until, int weight)>>> _starSeeds = new();
    private static bool _loaded;
    private static readonly object _loadLock = new();
    private static readonly Random _rng = new();

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            try { LoadFromFiles(); } catch (Exception ex) { Console.WriteLine($"[WARN] Gacha load failed: {ex.Message}"); }
            _loaded = true;
        }
    }

    private static string[] SearchPaths(params string[] relative)
    {
        var basePaths = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            @"C:\Users\23684\Desktop\gcg2-csharp-server",
        };
        return basePaths.Select(b => Path.Combine(new[] { b }.Concat(relative).ToArray())).ToArray();
    }

    private static string? FindFile(params string[] relative)
    {
        return SearchPaths(relative).FirstOrDefault(File.Exists);
    }

    private static Dictionary<string, string>[] ParseTsv(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length < 4) return Array.Empty<Dictionary<string, string>>();
        var headers = lines[2].Split('\t');
        var result = new List<Dictionary<string, string>>();
        for (int i = 3; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = lines[i].Split('\t');
            var row = new Dictionary<string, string>();
            for (int j = 0; j < headers.Length; j++)
                row[headers[j].Trim()] = j < values.Length ? values[j] : "";
            result.Add(row);
        }
        return result.ToArray();
    }

    private static int Int(Dictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : 0;
    }

    private static int[]? ParseGdpln(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var m = Regex.Match(value, @"\[([0-9-]+)\]");
        if (!m.Success) return null;
        var parts = m.Groups[1].Value.Split('-').Select(int.Parse).ToArray();
        if (parts.Length < 4) return null;
        return parts.Length >= 5 ? parts[..5] : new[] { parts[0], parts[1], parts[2], parts[3], 1 };
    }

    private static Gdpl? ParseGdpl(string? value)
    {
        var arr = ParseGdpln(value);
        if (arr == null) return null;
        return new Gdpl { Genre = arr[0], Detail = arr[1], Particular = arr[2], Level = arr[3] };
    }

    private static List<(int until, int weight)> ParseThresholdWeights(string? value)
    {
        if (string.IsNullOrEmpty(value)) return new List<(int, int)>();
        return Regex.Matches(value, @"\[(\d+),(\d+)\]")
            .Select(m => (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)))
            .ToList();
    }

    private static void LoadFromFiles()
    {
        var gachaFile = FindFile("resources", "gacha", "gacha.txt");
        var starSeedFile = FindFile("resources", "gacha", "cardstarseed.txt");
        var hedgeSeedFile = FindFile("resources", "gacha", "hedgecardseed.txt");
        var poolsDir = SearchPaths("resources", "gacha", "pools").FirstOrDefault(Directory.Exists);

        if (gachaFile == null || poolsDir == null)
        {
            Console.WriteLine("[WARN] Gacha resources not found, using simplified gacha");
            return;
        }

        // 加载星级种子
        if (starSeedFile != null)
        {
            foreach (var row in ParseTsv(File.ReadAllText(starSeedFile)))
            {
                var id = Int(row, "ID");
                var byStar = new Dictionary<int, List<(int, int)>>();
                for (int star = 1; star <= 4; star++)
                    byStar[star] = ParseThresholdWeights(row.TryGetValue($"{star}StarCardSeed", out var v) ? v : null);
                _starSeeds[id] = byStar;
            }
        }

        // 加载卡池文件
        var poolFiles = new Dictionary<string, string>();
        foreach (var f in Directory.GetFiles(poolsDir, "*.txt"))
            poolFiles[Path.GetFileNameWithoutExtension(f).ToLower()] = File.ReadAllText(f);

        // 解析卡池
        var latestRows = new Dictionary<int, Dictionary<string, string>>();
        foreach (var row in ParseTsv(File.ReadAllText(gachaFile)))
        {
            if (Int(row, "Type") != 1) continue; // 只加载角色卡池
            var id = Int(row, "ID");
            if (!latestRows.ContainsKey(id)) latestRows[id] = row;
        }

        foreach (var kv in latestRows)
        {
            var row = kv.Value;
            var poolName = row.TryGetValue("Pool", out var pn) ? pn.Trim().ToLower() : "";
            if (!poolFiles.ContainsKey(poolName)) continue;

            var normalCards = ParseCards(poolFiles[poolName]);
            var pool = new GachaPool
            {
                Id = Int(row, "ID"),
                Name = row.TryGetValue("Name", out var n) ? n : "",
                CashType = Int(row, "CashType"),
                MoneyCostOne = Int(row, "CostOne"),
                MoneyCostTen = Int(row, "CostTen"),
                JudgeType = Int(row, "JudgeType"),
                ProtectNum = Int(row, "ProtectNum"),
                ProtectUpNum = Int(row, "ProtectUpNum"),
                SetUpNum = Int(row, "SetUpNum"),
                UpBoxNum = Int(row, "UpBoxNum"),
                StarSeedId = Int(row, "CardStarSeed"),
                HedgeSeedId = Int(row, "HedgeCardStarSeed"),
                NormalCards = GroupCards(normalCards),
            };
            _pools[pool.Id] = pool;
        }

        Console.WriteLine($"[INFO] Loaded {_pools.Count} gacha pools from resources");
    }

    private static List<GachaCard> ParseCards(string content)
    {
        var result = new List<GachaCard>();
        foreach (var row in ParseTsv(content))
        {
            var gdpl = ParseGdpl(row.TryGetValue("GDPLN", out var g) ? g : null);
            var rate = Int(row, "Rate");
            if (gdpl == null || rate <= 0) continue;
            result.Add(new GachaCard
            {
                Id = Int(row, "ID"),
                Gdpl = gdpl,
                Rarity = Int(row, "Rarity") != 0 ? Int(row, "Rarity") : gdpl.Level,
                Rate = rate,
                UpFlag = Int(row, "UpFlag"),
                Sort = Int(row, "Sort"),
            });
        }
        return result;
    }

    private static Dictionary<int, List<GachaCard>> GroupCards(List<GachaCard> cards)
    {
        var result = new Dictionary<int, List<GachaCard>>();
        foreach (var card in cards)
        {
            var star = card.Rarity >= 4 ? 4 : card.Rarity;
            if (!result.ContainsKey(star)) result[star] = new List<GachaCard>();
            result[star].Add(card);
        }
        return result;
    }

    public static GachaPool? GetPool(int id)
    {
        EnsureLoaded();
        return _pools.TryGetValue(id, out var pool) ? pool : null;
    }

    public static bool HasPool(int id) => GetPool(id) != null;

    /// <summary>简化版抽卡（保底机制），保留原有逻辑。</summary>
    public static List<GachaAward> RollSimplified(int count, HashSet<string> ownedCards)
    {
        var awards = new List<GachaAward>();
        var charIds = Enumerable.Range(1, 50).ToArray();
        for (int i = 0; i < count; i++)
        {
            var charId = charIds[_rng.Next(charIds.Length)];
            var rarity = _rng.Next(100) < 3 ? 3 : (_rng.Next(100) < 15 ? 2 : 1);
            var tplLevel = rarity == 3 ? 3 : (rarity == 2 ? 2 : 1);
            var key = $"1:{charId}:1:{tplLevel}";
            var firstGet = !ownedCards.Contains(key);
            ownedCards.Add(key);
            awards.Add(new GachaAward
            {
                TbGDPL = new[] { 1, charId, 1, tplLevel },
                NId = 0,
                NTimes = i + 1,
                IsUp = false,
                NUpTimes = 0,
                NTotalTimes = i + 1,
                BFirstGet = firstGet,
                BHasCard = !firstGet,
            });
        }
        return awards;
    }
}
