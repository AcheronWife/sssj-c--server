using System.Collections.Concurrent;
using System.Text.Json;
using Gcg2OfflineServer.GameData;
using Gcg2OfflineServer.Models;

namespace Gcg2OfflineServer.Services;

/// <summary>
/// 玩家仓库：内存缓存 + data/state.json 原子持久化。
/// 写入采用临时文件 + 原子重命名。
/// 并发安全：每个 account 独立锁，写入 debounce 合并。
/// </summary>
public class PlayerRepository : IDisposable
{
    private readonly string _stateFile;
    private readonly GameLogger _logger;
    private readonly ConcurrentDictionary<string, PlayerState> _players = new();
    private readonly ConcurrentDictionary<string, object> _accountLocks = new();
    private long _nextRoleId = 1;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ---- 写入 debounce ----
    private readonly object _saveLock = new();
    private Timer? _saveTimer;
    private bool _pendingSave;
    private const int SaveDelayMs = 500;

    public PlayerRepository(string dataDir, GameLogger logger)
    {
        _stateFile = Path.Combine(dataDir, "state.json");
        _logger = logger;
        Directory.CreateDirectory(dataDir);
        Load();
    }

    private object GetLock(string account) => _accountLocks.GetOrAdd(account, _ => new object());

    private void Load()
    {
        if (!File.Exists(_stateFile))
        {
            _logger.Info("state.json not found, starting with empty player store");
            return;
        }
        if (TryLoadFrom(_stateFile)) return;

        var bakFile = _stateFile + ".bak";
        if (File.Exists(bakFile) && TryLoadFrom(bakFile))
        {
            _logger.Warn("state.json corrupted, recovered from .bak backup");
            try { File.Copy(bakFile, _stateFile, overwrite: true); } catch { }
            return;
        }

        try
        {
            var corruptedName = _stateFile + $".corrupted.{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(_stateFile, corruptedName);
            _logger.Error($"state.json unrecoverable, moved to {corruptedName}, starting empty store");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to archive corrupted state.json: {ex.Message}");
        }
    }

    private bool TryLoadFrom(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("nextRoleId", out var nr))
                _nextRoleId = nr.GetInt64();
            if (root.TryGetProperty("players", out var playersEl))
            {
                foreach (var prop in playersEl.EnumerateObject())
                {
                    var p = prop.Value.Deserialize<PlayerState>(JsonOpts);
                    if (p != null)
                    {
                        _players[p.Account] = p;
                        if (p.RoleId >= _nextRoleId)
                            _nextRoleId = p.RoleId + 1;
                    }
                }
            }
            _logger.Info($"Loaded {_players.Count} players from {Path.GetFileName(path)} (nextRoleId={_nextRoleId})");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取玩家；不存在则创建带完整默认数据的玩家。
    /// 新玩家 lastLoginAt = null，用于判断是否新玩家。
    /// 线程安全：account 级锁。
    /// </summary>
    public PlayerState GetOrCreate(string account)
    {
        lock (GetLock(account))
        {
            if (_players.TryGetValue(account, out var existing))
            {
                if (existing.Girls == null || existing.Girls.Count == 0)
                {
                    _logger.Info($"Upgrading legacy player: account={account} (girls empty, reinitializing)");
                    var upgraded = GameDefaults.CreateDefaultPlayer(account, existing.RoleId);
                    upgraded.RegisterTime = existing.RegisterTime;
                    upgraded.LastLoginAt = existing.LastLoginAt;
                    _players[account] = upgraded;
                    EnsureDefaults(upgraded);
                    Save();
                    return upgraded;
                }
                EnsureDefaults(existing);
                Save();
                return existing;
            }

            var roleId = Interlocked.Increment(ref _nextRoleId) - 1;
            if (account == "test")
            {
                roleId = 10001;
            }
            var player = GameDefaults.CreateDefaultPlayer(account, roleId);
            if (account == "test")
            {
                player.Name = "Tester店长";
                player.Level = 80;
                var vigor = player.Money.FirstOrDefault(m => m.Id == 1);
                if (vigor != null) vigor.Count = 999;
                var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                if (gold != null) gold.Count = 9999999;
                var diamond = player.Money.FirstOrDefault(m => m.Id == 3);
                if (diamond != null) diamond.Count = 99999;
            }

            if (_players.TryAdd(account, player))
            {
                _logger.Info($"Created new player: account={account} roleId={player.RoleId} girls={player.Girls.Count} items={player.Inventory.Count}");
                EnsureDefaults(player);
                Save();
            }
            return _players[account];
        }
    }

    /// <summary>外部修改玩家后调用此方法持久化。线程安全。</summary>
    public void SavePlayer(PlayerState player)
    {
        lock (GetLock(player.Account))
        {
            _players[player.Account] = player;
            Save();
        }
    }

    /// <summary>标记登录时间，返回更新后的玩家。线程安全。</summary>
    public PlayerState? MarkLogin(string account)
    {
        lock (GetLock(account))
        {
            if (_players.TryGetValue(account, out var p))
            {
                p.LastLoginAt = DateTime.UtcNow.ToString("o");
                Save();
                return p;
            }
            return null;
        }
    }

    public PlayerState? Rename(string account, string name)
    {
        lock (GetLock(account))
        {
            if (_players.TryGetValue(account, out var p))
            {
                p.Name = name;
                Save();
                return p;
            }
            return null;
        }
    }

    /// <summary>请求 1027：批量设置 taskValues。线程安全。</summary>
    public PlayerState? SetTaskValues(string account, List<TaskChange> changes)
    {
        lock (GetLock(account))
        {
            if (_players.TryGetValue(account, out var p))
            {
                foreach (var c in changes)
                {
                    p.TaskValues[c.Id.ToString()] = c.Value;
                    _logger.Info($"task.value account={account} id={c.Id} value={c.Value}");
                }
                Save();
                return p;
            }
            return null;
        }
    }

    /// <summary>
    /// 纯读取玩家数据，不做任何修改或持久化。
    /// 调用方如需修改，应在 account 锁内操作后调用 SavePlayer。
    /// </summary>
    public PlayerState? Get(string account)
    {
        if (!_players.TryGetValue(account, out var p)) return null;
        EnsureDefaults(p);
        return p;
    }

    /// <summary>
    /// 在 account 锁内执行读写操作，确保并发安全。
    /// 这是 LuaDispatcher 等调用方修改玩家数据的推荐入口。
    /// </summary>
    public T? Modify<T>(string account, Func<PlayerState, T> action)
    {
        lock (GetLock(account))
        {
            if (!_players.TryGetValue(account, out var p)) return default;
            EnsureDefaults(p);
            var result = action(p);
            Save();
            return result;
        }
    }

    /// <summary>在 account 锁内执行读写操作（无返回值）。</summary>
    public void Modify(string account, Action<PlayerState> action)
    {
        lock (GetLock(account))
        {
            if (!_players.TryGetValue(account, out var p)) return;
            EnsureDefaults(p);
            action(p);
            Save();
        }
    }

    /// <summary>战力计算已禁用，FightPower 固定值，避免干扰引导任务。</summary>
    public void UpdateFightPower(string account)
    {
        // 战力计算已禁用
    }

    private void EnsureDefaults(PlayerState p)
    {
        var changed = false;
        // 战力固定值，避免计算干扰
        if (p.FightPower == 0) { p.FightPower = 999999; changed = true; }
        if (EnsureGirlCards(p)) changed = true;
        if (!p.TaskValues.ContainsKey("131074") || p.TaskValues["131074"] == 0)
        {
            p.TaskValues["131074"] = p.Girls.Count > 0 ? p.Girls[0].GirlId : 1;
            changed = true;
        }
        var dailyMissionTargets = new Dictionary<int, int>
        {
            [101] = 5, [102] = 2, [103] = 2, [104] = 1, [105] = 5,
            [106] = 1, [107] = 1, [108] = 1, [109] = 100, [110] = 1,
        };
        foreach (var (mid, target) in dailyMissionTargets)
        {
            var dTaskId = (5L << 16) | (uint)mid;
            var dKey = dTaskId.ToString();
            if (!p.TaskValues.ContainsKey(dKey) || p.TaskValues[dKey] < target)
            {
                p.TaskValues[dKey] = target;
                changed = true;
            }
        }
        var activePointTaskId = (5L << 16) | 30000;
        var activePointKey = activePointTaskId.ToString();
        if (!p.TaskValues.ContainsKey(activePointKey) || p.TaskValues[activePointKey] < 100)
        {
            p.TaskValues[activePointKey] = 100;
            changed = true;
        }
        var activeAwardTaskId = (5L << 16) | 30001;
        var activeAwardKey = activeAwardTaskId.ToString();
        if (!p.TaskValues.ContainsKey(activeAwardKey) || p.TaskValues[activeAwardKey] < 5)
        {
            p.TaskValues[activeAwardKey] = 5;
            changed = true;
        }

        for (int coffeeId = 1; coffeeId <= 4; coffeeId++)
        {
            var taskId = (23L << 16) | (uint)coffeeId;
            var key = taskId.ToString();
            if (!p.TaskValues.ContainsKey(key))
            {
                p.TaskValues[key] = 1L << 8;
                changed = true;
            }
        }
        // 咖啡馆迁移：确保旧玩家有咖啡和服务员列表
        p.Cafe ??= new CafeState();
        p.Cafe.Coffees ??= new List<CafeCoffee>();
        p.Cafe.Waiters ??= new List<long[]> { new long[0], new long[0], new long[0] };
        if (p.Cafe.Waiters.Count < 3)
        {
            while (p.Cafe.Waiters.Count < 3) p.Cafe.Waiters.Add(new long[0]);
            changed = true;
        }
        p.Cafe.Customers ??= new List<CafeCustomer>();
        p.Cafe.Pets ??= new List<object>();
        for (int coffeeId = 1; coffeeId <= 4; coffeeId++)
        {
            if (!p.Cafe.Coffees.Any(c => c.CoffeeType == coffeeId))
            {
                p.Cafe.Coffees.Add(new CafeCoffee { CoffeeType = coffeeId, Count = 10 });
                changed = true;
            }
        }

        // 货币迁移：确保旧玩家有所有新货币（咖啡币/竞技场币/公会币等）
        var newMoneyIds = new long[] { 5, 6, 7, 18, 19, 20 };
        foreach (var mid in newMoneyIds)
        {
            if (!p.Money.Any(m => m.Id == mid))
            {
                p.Money.Add(new MoneyEntry { Id = mid, Count = 9999999 });
                changed = true;
            }
        }

        p.Levels ??= new List<LevelState>();
        p.Formations ??= new List<FormationState>();
        foreach (var formation in p.Formations)
        {
            formation.FightCards ??= new List<FightCardState>();
            foreach (var card in formation.FightCards)
            {
                if (card.UsedCardGuid == 0 && card.MainCardGuid > 0)
                {
                    card.UsedCardGuid = card.MainCardGuid;
                    changed = true;
                }
                card.SecondaryCardGuids ??= new List<long>();
                card.RuneItemGuids ??= new List<long>();
            }
        }
        if (p.Formations.Count == 0 || !p.Formations.Any(f => f.Id == 1))
        {
            var characterCards = p.Inventory.Where(i => i.Genre == 1).Take(3).ToList();
            if (characterCards.Count > 0)
            {
                var cards = characterCards.Select(c => new FightCardState
                {
                    MainCardGuid = c.Guid,
                    UsedCardGuid = c.Guid,
                    WeaponGuid = 0,
                    SecondaryCardGuids = new List<long>(),
                    RuneItemGuids = new List<long>(),
                }).ToList();
                p.Formations.Add(new FormationState { Id = 1, Title = "初始阵容", FightCards = cards });
                changed = true;
            }
        }
        foreach (var level in p.Levels)
        {
            var chapter = level.Id >> 16;
            var index = (level.Id >> 8) & 0xFF;
            var diff = level.Id & 0xFF;
            var passCount = level.Star >> 3;
            if (passCount > 0)
            {
                SyncGuideMissionByLevel(p, chapter, index, diff);
            }
        }

        // 清理不存在于 chapter.txt 的关卡数据（旧版本残留，可能导致客户端解析出错）
        var validLevelIds = new HashSet<long>(ChapterConfig.AllLevels.Select(lv => (long)(lv.Chapter << 16) | (lv.Index << 8) | lv.Difficulty));
        var beforeCount = p.Levels.Count;
        p.Levels.RemoveAll(l => !validLevelIds.Contains(l.Id));
        if (p.Levels.Count != beforeCount) changed = true;

        // 解锁全部主线关卡（Chapter 1-16 + Chapter 100 誓约关卡）
        foreach (var lv in ChapterConfig.AllLevels)
        {
            var levelId = (long)(lv.Chapter << 16) | (lv.Index << 8) | lv.Difficulty;
            if (!p.Levels.Any(l => l.Id == levelId))
            {
                p.Levels.Add(new LevelState { Id = levelId, Star = (1 << 3) | 7 }); // 通关1次+三星
                changed = true;
            }
        }

        // 补全章节星级任务（TaskGroup=14）：全部章节，totalStars=满星，claimedMask=0xff（全部领取）
        var chapterStarGroups = p.Levels
            .Where(l => (l.Id & 0xFF) == 1 || (l.Id & 0xFF) == 2)
            .GroupBy(l => new { Chapter = (int)(l.Id >> 16), Diff = (int)(l.Id & 0xFF) });
        foreach (var g in chapterStarGroups)
        {
            var totalStars = g.Sum(l =>
            {
                var mask = l.Star & 0b111;
                return (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1);
            });
            var starTaskId = (14L << 16) | (uint)(g.Key.Diff | (g.Key.Chapter << 8));
            var starKey = starTaskId.ToString();
            var claimedMask = 0xFF; // 全部3档奖励已领取
            var expectedValue = (totalStars << 8) | claimedMask;
            if (!p.TaskValues.ContainsKey(starKey) || p.TaskValues[starKey] != expectedValue)
            {
                p.TaskValues[starKey] = expectedValue;
                changed = true;
            }
        }

        // 补全主线任务（TaskGroup=1）：1001-1010，客户端通过这些任务值判定剧情解锁
        // 注意：值=0表示未完成，客户端会触发对应剧情；值=1表示已完成，会跳过剧情
        var mainQuestIds = new HashSet<int>();
        for (int ch = 1; ch <= 10; ch++)
        {
            mainQuestIds.Add(1000 + ch);  // 1001-1010
        }
        foreach (var mqId in mainQuestIds)
        {
            var mqTaskId = (1L << 16) | (uint)mqId;
            var mqKey = mqTaskId.ToString();
            // 只确保任务键存在，值=0（未完成），让客户端自然触发剧情
            if (!p.TaskValues.ContainsKey(mqKey))
            {
                p.TaskValues[mqKey] = 0;
                changed = true;
            }
        }

        // FIRST_LEVEL_TASK_ID = (1<<16)|15 = 65551，值为6表示第一关已完成
        const long firstLevelTaskId = (1L << 16) | 15;
        var firstLevelKey = firstLevelTaskId.ToString();
        if (!p.TaskValues.ContainsKey(firstLevelKey) || p.TaskValues[firstLevelKey] < 6)
        {
            p.TaskValues[firstLevelKey] = 6;
            changed = true;
        }

        // 初始私信数据（topicId=10001, initiator=7）
        p.Phone ??= new PhoneState();
        p.Phone.Letters ??= new List<PhoneLetterState>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!p.Phone.Letters.Any(l => l.TopicId == 10001 && l.Initiator == 7))
        {
            p.Phone.Letters.Add(new PhoneLetterState
            {
                TopicId = 10001,
                Initiator = 7,
                CreateTime = now,
                ReplyIds = new List<long>(),
            });
            changed = true;
        }

        if (changed) Save();
    }

    // ---- 女孩角色卡/时装 taskId 补全 ----
    private const long GIRL_CARD_TASK_GROUP = 7;
    private const long GIRL_SUIT_TASK_GROUP_REPO = 4;
    private const long GIRL_TASK_STRIDE_REPO = 2000;
    private static readonly long[] PlayableGirlIds = { 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,201,202,203,204 };
    private static readonly Dictionary<string, long> CardModelOverrides = new()
    {
        ["1:71:4"]=7001, ["1:81:5"]=8001,
        ["2:71:4"]=7001, ["2:81:5"]=8001,
        ["3:71:4"]=7001, ["3:81:5"]=8001, ["3:82:5"]=8002,
        ["4:81:5"]=8001,
        ["5:71:4"]=7001,
        ["6:71:4"]=7001, ["6:81:5"]=8001,
        ["7:71:4"]=7001, ["7:81:5"]=8001, ["7:82:5"]=8002,
        ["8:71:4"]=7001, ["8:81:5"]=8001,
        ["9:71:4"]=7001, ["9:72:4"]=7002, ["9:82:5"]=8002,
        ["10:71:4"]=7001, ["10:81:5"]=8001, ["10:82:5"]=8002,
        ["11:71:4"]=7001, ["11:81:5"]=8001,
        ["12:71:3"]=7001, ["12:72:4"]=7002, ["12:81:5"]=8001,
        ["13:71:4"]=7001, ["13:81:5"]=8001,
        ["14:81:5"]=8001,
        ["15:71:4"]=7001, ["15:81:5"]=8001,
        ["16:71:4"]=7001, ["16:81:5"]=8001,
    };

    private static long MakeGirlTaskIdRepo(long taskGroup, long girlId, long offset)
    {
        long tg = taskGroup;
        long tid = girlId;
        if (girlId >= 201 && girlId <= 204)
        {
            tg = taskGroup == 3 ? 90 : taskGroup == 4 ? 91 : taskGroup == 7 ? 92 : taskGroup;
            tid = girlId - 200;
        }
        return (tg << 16) | ((tid - 1) * GIRL_TASK_STRIDE_REPO + offset);
    }

    private static long FixGirlModelOffsetRepo(long modelId)
    {
        if (modelId <= 0) return 1;
        if (modelId < 2000) return modelId;
        if (modelId >= 8001 && modelId <= 8003) return modelId - 6200;
        if (modelId >= 3000)
        {
            var suffix = modelId % 100;
            if (suffix <= 29) return suffix + (modelId / 1000) * 30 + 1500;
        }
        return 1;
    }

    private static long? CharacterCardModelId(InventoryEntry card)
    {
        if (card.Genre != 1 || !PlayableGirlIds.Contains(card.Detail) || card.Particular <= 0) return null;
        var key = $"{card.Detail}:{card.Particular}:{card.TemplateLevel}";
        return CardModelOverrides.TryGetValue(key, out var mid) ? mid : card.Particular;
    }

    private bool EnsureGirlCards(PlayerState p)
    {
        var changed = false;
        var ownedModelsByGirl = new Dictionary<long, List<long>>();
        var ownedCardCounts = new Dictionary<long, long>();

        foreach (var card in p.Inventory.Where(i => i.Genre == 1))
        {
            if (!PlayableGirlIds.Contains(card.Detail)) continue;
            var modelId = CharacterCardModelId(card);
            if (modelId == null) continue;

            if (!ownedModelsByGirl.TryGetValue(card.Detail, out var models))
            {
                models = new List<long>();
                ownedModelsByGirl[card.Detail] = models;
            }
            if (!models.Contains(modelId.Value)) models.Add(modelId.Value);

            var cardTaskId = MakeGirlTaskIdRepo(GIRL_CARD_TASK_GROUP, card.Detail, card.Particular * 20 + card.TemplateLevel);
            var count = Math.Max(1, card.Count);
            ownedCardCounts[cardTaskId] = ownedCardCounts.TryGetValue(cardTaskId, out var c) ? c + count : count;

            var suitOffset = FixGirlModelOffsetRepo(modelId.Value);
            var suitTaskId = MakeGirlTaskIdRepo(GIRL_SUIT_TASK_GROUP_REPO, card.Detail, suitOffset);
            var suitKey = suitTaskId.ToString();
            if (!p.TaskValues.ContainsKey(suitKey) || p.TaskValues[suitKey] <= 0)
            {
                p.TaskValues[suitKey] = 2;
                changed = true;
            }
        }

        foreach (var kv in ownedCardCounts)
        {
            var key = kv.Key.ToString();
            if (!p.TaskValues.ContainsKey(key) || p.TaskValues[key] != kv.Value)
            {
                p.TaskValues[key] = kv.Value;
                changed = true;
            }
        }

        foreach (var kv in ownedModelsByGirl)
        {
            var girl = p.Girls.FirstOrDefault(g => g.GirlId == kv.Key);
            if (girl == null)
            {
                p.Girls.Add(new GirlState
                {
                    GirlId = kv.Key, Level = 80, Exp = 0, ModelId = kv.Value[0],
                    MoodValue = 100, Vigor = 100, Flag = 0, BreakLevel = 7,
                });
                changed = true;
            }
            else if (!kv.Value.Contains(girl.ModelId))
            {
                girl.ModelId = kv.Value[0];
                changed = true;
            }
        }

        var allModelIds = new List<long>();
        for (int m = 1; m <= 100; m++) allModelIds.Add(m);
        for (int m = 7001; m <= 7100; m++) allModelIds.Add(m);
        for (int m = 8001; m <= 8100; m++) allModelIds.Add(m);
        for (int m = 3000; m <= 3100; m++) allModelIds.Add(m);
        var allGirlIds = new List<long>();
        for (int g = 1; g <= 50; g++) allGirlIds.Add(g);
        allGirlIds.AddRange(new[] { 201L, 202L, 203L, 204L });
        foreach (var girlId in allGirlIds)
        {
            foreach (var modelId in allModelIds)
            {
                var suitOffset = FixGirlModelOffsetRepo(modelId);
                var suitTaskId = MakeGirlTaskIdRepo(GIRL_SUIT_TASK_GROUP_REPO, girlId, suitOffset);
                var suitKey = suitTaskId.ToString();
                if (!p.TaskValues.ContainsKey(suitKey) || p.TaskValues[suitKey] <= 0)
                {
                    p.TaskValues[suitKey] = 2;
                    changed = true;
                }
            }
        }

        foreach (var girl in p.Girls)
        {
            var isLink = girl.GirlId >= 201 && girl.GirlId <= 204;
            var maxLevel = isLink ? 50 : 100;
            if (girl.Level < maxLevel)
            {
                girl.Level = maxLevel;
                girl.Exp = 0;
                changed = true;
            }
            var maxSecret = isLink ? 10 : 20;
            for (int secretId = 1; secretId <= maxSecret; secretId++)
            {
                var secretTaskId = MakeGirlTaskIdRepo(3, girl.GirlId, 100 + secretId - 1);
                var secretKey = secretTaskId.ToString();
                if (!p.TaskValues.ContainsKey(secretKey) || p.TaskValues[secretKey] <= 0)
                {
                    p.TaskValues[secretKey] = 1;
                    changed = true;
                }
            }
        }

        // 补全指引任务（TaskGroup=5，GUIDE_MISSION_TASK_GROUP=5）
        // 客户端通过这些任务判定新手引导是否完成，缺失会导致章节锁定
        // 指引任务值格式：progress * 2 + (claimed ? 1 : 0)，位0=已领取，位1+=进度
        // 注意：不要将引导任务设为已完成（progress=target），否则客户端会跳过关卡剧情直接进战斗
        // 只确保任务键存在（值=0），让客户端自然推进引导和触发剧情
        var guideMissions = new Dictionary<long, long>
        {
            { 40001, 0 },  // 通关 1-1-1
            { 40002, 0 },  // 通关 1-2-1
            { 40003, 0 },  // 通关 1-3-1
            { 40004, 0 },  // 通关 1-6-1
            { 40005, 0 },  // 通关 2-6-1
            { 40006, 0 },  // 通关 3-6-1
            { 40008, 0 },  // 武器强化等级10
            { 40014, 0 },  // 拥有4个角色卡
            { 40017, 0 },  // 拥有80个物品
            { 40018, 0 },  // 其他指引
            { 40021, 0 },  // 领取第2章3星奖励
            { 40022, 0 },  // 战力达到3900
            { 40025, 0 },  // 通关 1-1-2
            { 40026, 0 },  // 其他指引
            { 40027, 0 },  // 编队装备武器
        };
        foreach (var (gmId, gmTarget) in guideMissions)
        {
            var gmTaskId = (5L << 16) | (uint)gmId;
            var gmKey = gmTaskId.ToString();
            // 只确保任务键存在，值=0（未完成未领取），让客户端自然推进引导和触发剧情
            if (!p.TaskValues.ContainsKey(gmKey))
            {
                p.TaskValues[gmKey] = 0;
                changed = true;
            }
        }
        // 指引进度任务 41001：位掩码，5档进度奖励
        // 注意：值=0表示未领取，让客户端自然推进；不要设为31（全部已领取），否则会跳过剧情
        var guideProgressTaskId = (5L << 16) | 41001u;
        var guideProgressKey = guideProgressTaskId.ToString();
        if (!p.TaskValues.ContainsKey(guideProgressKey))
        {
            p.TaskValues[guideProgressKey] = 0;
            changed = true;
        }

        return changed;
    }

    private static readonly Dictionary<long, (long chapter, long index, long diff, long target)> GuideLevelMissions = new()
    {
        { 40001, (1, 1, 1, 1) },
        { 40002, (1, 2, 1, 1) },
        { 40003, (1, 3, 1, 1) },
        { 40004, (1, 6, 1, 1) },
        { 40005, (2, 6, 1, 1) },
        { 40006, (3, 6, 1, 1) },
        { 40025, (1, 1, 2, 1) },
    };

    private static void SyncGuideMissionByLevel(PlayerState p, long chapter, long index, long diff)
    {
        foreach (var kv in GuideLevelMissions)
        {
            var (c, i, d, target) = kv.Value;
            if (c == chapter && i == index && d == diff)
            {
                long taskId = (5L << 16) | (uint)kv.Key;
                var key = taskId.ToString();
                var current = p.TaskValues.TryGetValue(key, out var v) ? v : 0;
                var claimed = (current & 1) == 1;
                p.TaskValues[key] = target * 2 + (claimed ? 1 : 0);
            }
        }
    }

    // ---- 写入 debounce ----

    /// <summary>
    /// 请求持久化。实际写入会延迟 SaveDelayMs 毫秒执行，期间多次调用合并为一次。
    /// 必须在 account 锁内或 _saveLock 外调用（内部自行加锁）。
    /// </summary>
    private void Save()
    {
        lock (_saveLock)
        {
            _pendingSave = true;
            if (_saveTimer == null)
            {
                _saveTimer = new Timer(OnSaveTimer, null, SaveDelayMs, Timeout.Infinite);
            }
        }
    }

    private void OnSaveTimer(object? state)
    {
        lock (_saveLock)
        {
            if (!_pendingSave) return;
            _pendingSave = false;
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
        try
        {
            WriteStateFile();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to persist state: {ex.Message}");
        }
    }

    private void WriteStateFile()
    {
        var state = new
        {
            schemaVersion = 1,
            nextRoleId = _nextRoleId,
            players = _players.Values.ToDictionary(p => p.Account),
            updatedAt = DateTime.UtcNow.ToString("o")
        };
        var tmp = _stateFile + ".tmp";
        var bak = _stateFile + ".bak";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOpts));
        try
        {
            if (File.Exists(_stateFile))
                File.Replace(tmp, _stateFile, bak);
            else
                File.Move(tmp, _stateFile);
        }
        catch
        {
            try { File.Move(tmp, _stateFile, overwrite: true); } catch { }
        }
    }

    /// <summary>强制立即写入所有待保存数据（关闭时调用）。</summary>
    public void Flush()
    {
        lock (_saveLock)
        {
            if (!_pendingSave) return;
            _pendingSave = false;
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
        try
        {
            WriteStateFile();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to flush state: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Flush();
        _saveTimer?.Dispose();
    }
}
