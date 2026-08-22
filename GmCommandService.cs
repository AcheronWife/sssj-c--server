using Gcg2OfflineServer.Models;
using Gcg2OfflineServer.Services;

namespace Gcg2OfflineServer;

/// <summary>
/// GM 命令服务，处理 HTTP /gm 接口和游戏内 GM 命令。
/// 所有玩家修改通过 PlayerRepository.Modify 在 account 锁内执行。
/// </summary>
public class GmCommandService
{
    private readonly PlayerRepository _repo;
    private readonly GameLogger _logger;

    public GmCommandService(PlayerRepository repo, GameLogger logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public (bool ok, string result, object? data) Execute(string account, string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd))
            return (false, "缺少命令参数", null);

        try
        {
            var parts = cmd.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var player = _repo.GetOrCreate(account);
            var result = "";

            switch (command)
            {
                case "level":
                    if (parts.Length < 2) return (false, "用法: level <n>", null);
                    _repo.Modify(account, p => { p.Level = Math.Max(1, int.Parse(parts[1])); });
                    result = $"等级设置为 {_repo.Get(account)?.Level}";
                    break;
                case "exp":
                    if (parts.Length < 2) return (false, "用法: exp <n>", null);
                    _repo.Modify(account, p => { p.Exp = long.Parse(parts[1]); });
                    result = $"经验设置为 {_repo.Get(account)?.Exp}";
                    break;
                case "vigor":
                case "vigour":
                case "体力":
                    if (parts.Length < 2) return (false, "用法: vigor <n>", null);
                    _repo.Modify(account, p =>
                    {
                        var vig = p.Money.FirstOrDefault(m => m.Id == 1);
                        if (vig != null) vig.Count = long.Parse(parts[1]);
                    });
                    result = $"体力设置为 {_repo.Get(account)?.Money.FirstOrDefault(m => m.Id == 1)?.Count ?? 0}";
                    break;
                case "gold":
                case "金币":
                    if (parts.Length < 2) return (false, "用法: gold <n>", null);
                    _repo.Modify(account, p =>
                    {
                        var gold = p.Money.FirstOrDefault(m => m.Id == 2);
                        if (gold != null) gold.Count = long.Parse(parts[1]);
                    });
                    result = $"金币设置为 {_repo.Get(account)?.Money.FirstOrDefault(m => m.Id == 2)?.Count ?? 0}";
                    break;
                case "diamond":
                case "青辉石":
                    if (parts.Length < 2) return (false, "用法: diamond <n>", null);
                    _repo.Modify(account, p =>
                    {
                        var dia = p.Money.FirstOrDefault(m => m.Id == 3);
                        if (dia != null) dia.Count = long.Parse(parts[1]);
                    });
                    result = $"青辉石设置为 {_repo.Get(account)?.Money.FirstOrDefault(m => m.Id == 3)?.Count ?? 0}";
                    break;
                case "unlockall":
                case "解锁全部":
                    _repo.Modify(account, p =>
                    {
                        p.Levels.Clear();
                        // 普通难度：前20章，每章20关，3星
                        for (int ch = 1; ch <= 20; ch++)
                            for (int idx = 1; idx <= 20; idx++)
                                p.Levels.Add(new LevelState { Id = (ch << 16) | (idx << 8) | 1, Star = (1 << 3) | 7 });
                        // 困难难度：前10章，每章10关，3星
                        for (int ch = 1; ch <= 10; ch++)
                            for (int idx = 1; idx <= 10; idx++)
                                p.Levels.Add(new LevelState { Id = (ch << 16) | (idx << 8) | 2, Star = (1 << 3) | 7 });
                    });
                    result = "已解锁前20章普通+前10章困难所有关卡（3星）";
                    break;
                case "maxcards":
                case "全角色":
                    _repo.Modify(account, p =>
                    {
                        var nextGuid = p.Inventory.Count > 0 ? p.Inventory.Max(i => i.Guid) + 1 : 20001;
                        for (int charId = 1; charId <= 50; charId++)
                        {
                            if (!p.Inventory.Any(i => i.Genre == 1 && i.Detail == charId))
                            {
                                p.Inventory.Add(new InventoryEntry
                                {
                                    Guid = nextGuid++, Genre = 1, Detail = charId, Particular = 1,
                                    TemplateLevel = 3, Count = 1, CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                    EnhanceLevel = 1,
                                });
                            }
                        }
                    });
                    result = "已添加所有角色卡（1-50号，3星）";
                    break;
                case "addcard":
                    if (parts.Length < 2) return (false, "用法: addcard <id>", null);
                    var cid = int.Parse(parts[1]);
                    _repo.Modify(account, p =>
                    {
                        var ng = p.Inventory.Count > 0 ? p.Inventory.Max(i => i.Guid) + 1 : 20001;
                        p.Inventory.Add(new InventoryEntry
                        {
                            Guid = ng, Genre = 1, Detail = cid, Particular = 1, TemplateLevel = 3,
                            Count = 1, CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), EnhanceLevel = 1,
                        });
                    });
                    result = $"已添加角色卡 ID={cid}";
                    break;
                case "addgirl":
                    if (parts.Length < 2) return (false, "用法: addgirl <id>", null);
                    var gid = int.Parse(parts[1]);
                    _repo.Modify(account, p =>
                    {
                        if (!p.Girls.Any(g => g.GirlId == gid))
                        {
                            p.Girls.Add(new GirlState
                            {
                                GirlId = gid, Level = 1, Exp = 0, ModelId = 1,
                                MoodValue = 100, Vigor = 100,
                            });
                        }
                    });
                    result = $"已添加女孩 ID={gid}";
                    break;
                case "maxmoney":
                case "全货币":
                    _repo.Modify(account, p =>
                    {
                        var moneyIds = new long[] { 1, 2, 3, 5, 6, 7, 10, 12, 16, 18, 19, 20 };
                        foreach (var mid in moneyIds)
                        {
                            var m = p.Money.FirstOrDefault(x => x.Id == mid);
                            if (m == null) { p.Money.Add(new MoneyEntry { Id = mid, Count = 9999999 }); }
                            else { m.Count = 9999999; }
                        }
                    });
                    result = "所有货币已拉满到 9999999";
                    break;
                case "allitems":
                case "全物品":
                    _repo.Modify(account, p =>
                    {
                        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var nextGuid = p.Inventory.Count > 0 ? p.Inventory.Max(i => i.Guid) + 1 : 20001;
                        void AddItem(int genre, int detail, int count)
                        {
                            if (!p.Inventory.Any(i => i.Genre == genre && i.Detail == detail))
                            {
                                p.Inventory.Add(new InventoryEntry
                                {
                                    Guid = nextGuid++, Genre = genre, Detail = detail, Particular = 1,
                                    TemplateLevel = 1, Count = count, CreateTime = now,
                                    EnhanceLevel = 0, EnhanceExp = 0, BreakLevel = 0, LockOn = 0,
                                });
                            }
                        }
                        // 家具 genre=13
                        for (int d = 1; d <= 500; d++) AddItem(13, d, 99);
                        // 装饰 genre=14
                        for (int d = 1; d <= 200; d++) AddItem(14, d, 99);
                        // 消耗品 genre=15
                        for (int d = 1; d <= 100; d++) AddItem(15, d, 999);
                        // 头像框 genre=10
                        for (int d = 10000; d <= 10500; d++) AddItem(10, d, 1);
                        // 展示柜 genre=11
                        for (int d = 11000; d <= 11200; d++) AddItem(11, d, 1);
                        // 聊天泡泡 genre=12
                        for (int d = 12000; d <= 12200; d++) AddItem(12, d, 1);
                        // 模块 genre=3（装备/插件）
                        for (int d = 1; d <= 100; d++)
                            for (int tpl = 1; tpl <= 7; tpl++)
                                AddItem(3, d, 999);
                        p.NextItemGuid = nextGuid;
                    });
                    result = "已添加所有家具/装饰/消耗品/头像框/展示柜/聊天泡泡";
                    break;
                case "help":
                    return (true, "", new
                    {
                        commands = new[]
                        {
                            "level <n> - 设置等级", "exp <n> - 设置经验",
                            "vigor <n> - 设置体力", "gold <n> - 设置金币",
                            "diamond <n> - 设置青辉石", "unlockall - 一键解锁所有关卡",
                            "maxcards - 获得所有角色卡", "maxmoney - 所有货币拉满",
                            "allitems - 获得所有家具/装饰/消耗品", "addcard <id> - 添加指定角色卡",
                            "addgirl <id> - 添加指定女孩", "help - 显示此帮助",
                        }
                    });
                default:
                    return (false, $"未知命令: {command}，输入 help 查看帮助", null);
            }

            _logger.Info($"gm.exec account={account} cmd={cmd} result={result}");
            return (true, result, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }
}
