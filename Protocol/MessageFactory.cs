using System.Text;
using Gcg2OfflineServer.Models;
using static Gcg2OfflineServer.Protocol.ProtobufWriter;

namespace Gcg2OfflineServer.Protocol;

/// <summary>
/// 消息体编码工厂。
/// 所有方法返回 Protobuf 编码后的 byte[]，作为 GamePacket 的 payload。
/// </summary>
public static class MessageFactory
{
    // ---- VERIFY_RSP (1103) ----

    public static byte[] MakeVerifyResponse(PlayerState player, ServerListConfig serverList, bool createRole)
    {
        return Concat(
            FieldVarint(1, player.RoleId),
            FieldVarint(2, serverList.Id),
            FieldBytes(3, new byte[16]),
            FieldVarint(5, serverList.Aid),
            FieldVarint(6, serverList.Sid),
            FieldVarint(7, 0),
            FieldVarint(8, DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            FieldVarint(9, createRole ? 1 : 0),
            FieldBytes(10, player.Account)
        );
    }

    // ---- TASK_VALUE_RSP (1026) ----

    public static byte[] MakeTaskValueSync(Dictionary<string, long> taskValues)
    {
        var parts = taskValues
            .OrderBy(kv => long.TryParse(kv.Key, out var n) ? n : 0)
            .Select(kv =>
            {
                var task = Concat(
                    FieldVarint(1, long.TryParse(kv.Key, out var n) ? n : 0),
                    FieldVarint(2, kv.Value)
                );
                return FieldBytes(1, task);
            })
            .ToArray();
        return parts.Length > 0 ? Concat(parts) : Array.Empty<byte>();
    }

    // ---- LIVE2D_ENABLE_LEVEL_NTF (1036) ----

    public static byte[] MakeLive2dEnableLevel(long level) => FieldVarint(1, level);

    // ---- LIVE2D_HX_STATE_NTF (1037) ----

    public static byte[] MakeLive2dHxState(bool active) => FieldVarint(1, active ? 1 : 0);

    // ---- PLAYER_NTF (1005) ----

    public static byte[] MakePlayerNotification(PlayerState player)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var parts = new List<byte[]>
        {
            FieldBytes(1, player.Name),
            FieldVarint(2, player.Level),
            FieldVarint(3, player.Exp),
            FieldVarint(4, player.FightPower),
        };
        parts.AddRange(player.Money.Select(m => FieldBytes(5, MakeMoneyInfo(m))));
        parts.Add(FieldVarint(6, player.RegisterTime));
        parts.Add(FieldVarint(7, now));
        parts.Add(FieldVarint(8, now));
        parts.Add(FieldVarint(9, player.ServerZone));
        parts.Add(FieldBytes(10, MakeGirlList(player.Girls)));
        parts.AddRange(player.Formations.Select(f => FieldBytes(11, MakeFormationInfo(f))));
        parts.Add(FieldBytes(12, MakeLevelList(player.Levels)));
        parts.Add(FieldVarint(20, player.RoleId));
        return Concat(parts.ToArray());
    }

    // ---- ITEM_NTF (1104) ----

    public static byte[] MakeItemNotification(PlayerState player)
    {
        var parts = player.Inventory
            .Select(item => FieldBytes(1, MakeItemInfo(item)))
            .ToList();
        parts.Add(FieldVarint(2, player.Inventory.Count));
        return Concat(parts.ToArray());
    }

    // ---- PHONE_MSG_NTF (1035) ----

    public static byte[] MakePhoneMessageNotification(PlayerState player)
    {
        var byInitiator = player.Phone.Letters
            .GroupBy(l => l.Initiator)
            .ToList();

        if (byInitiator.Count == 0)
            return Array.Empty<byte>();

        var parts = byInitiator.Select(g =>
        {
            var topics = g.ToList();
            var latestCreateTime = topics.Max(l => l.CreateTime);
            if (latestCreateTime < player.RegisterTime)
                latestCreateTime = player.RegisterTime;

            var letterParts = new List<byte[]>
            {
                FieldVarint(1, g.Key),
                FieldVarint(3, latestCreateTime),
            };
            letterParts.AddRange(topics.Select(t =>
            {
                var topicParts = new List<byte[]> { FieldVarint(1, t.TopicId) };
                topicParts.AddRange(t.ReplyIds.Select(rid => FieldVarint(2, rid)));
                return FieldBytes(4, Concat(topicParts.ToArray()));
            }));
            letterParts.Add(FieldVarint(5, 0));

            return FieldBytes(1, Concat(letterParts.ToArray()));
        }).ToArray();

        return Concat(parts);
    }

    // ---- HOUSE_INFO_RSP (1049) ----

    public static byte[] MakeHouseInfoResponse(long roleId)
    {
        var roomIds = new[] { 1L, 2L, 6L };
        var houseCacheParts = new List<byte[]> { FieldVarint(1, roleId) };
        houseCacheParts.AddRange(roomIds.Select(rid => FieldBytes(4, FieldVarint(1, rid))));
        var houseCache = Concat(houseCacheParts.ToArray());

        return Concat(
            FieldVarint(1, roleId),
            FieldBytes(2, houseCache),
            FieldBytes(3, Array.Empty<byte>())
        );
    }

    // ---- S2C Lua 调用 (1024) ----

    public static byte[] MakeServerLuaCall(string method, object parameters)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(parameters);
        return Concat(
            FieldBytes(1, method),
            FieldBytes(2, json)
        );
    }

    // ---- 解析辅助 ----

    public static string ParseRenameName(byte[] payload)
    {
        var fields = ProtobufReader.Decode(payload);
        return ProtobufReader.FirstString(fields, 3);
    }

    public static List<TaskChange> ParseTaskChanges(byte[] payload)
    {
        var changes = new List<TaskChange>();
        var fields = ProtobufReader.Decode(payload);
        foreach (var f in fields)
        {
            if (f.FieldNumber == 1 && f.WireType == 2)
            {
                var taskFields = ProtobufReader.Decode(f.BytesValue);
                var id = ProtobufReader.FirstNumber(taskFields, 1);
                var value = ProtobufReader.FirstNumber(taskFields, 2);
                if (id > 0)
                    changes.Add(new TaskChange { Id = id, Value = value });
            }
        }
        return changes;
    }

    // ---- 内部辅助 ----

    private static byte[] MakeMoneyInfo(MoneyEntry money)
        => Concat(FieldVarint(1, money.Id), FieldVarint(2, money.Count));

    private static byte[] MakeGirlInfo(GirlState girl)
        => Concat(
            FieldVarint(1, girl.GirlId),
            FieldVarint(2, girl.Level),
            FieldVarint(3, girl.Exp),
            FieldVarint(4, girl.ModelId),
            FieldVarint(5, girl.MoodValue),
            FieldVarint(6, girl.Vigor),
            FieldVarint(7, girl.Flag)
        );

    private static byte[] MakeGirlList(List<GirlState> girls)
        => Concat(girls.Select(g => FieldBytes(1, MakeGirlInfo(g))).ToArray());

    private static byte[] MakeFightCard(FightCardState card)
    {
        var parts = new List<byte[]>
        {
            FieldVarint(1, card.MainCardGuid),
        };
        parts.AddRange(card.SecondaryCardGuids.Select(g => FieldVarint(2, g)));
        parts.Add(FieldVarint(3, card.UsedCardGuid));
        parts.Add(FieldVarint(4, card.WeaponGuid));
        parts.AddRange(card.RuneItemGuids.Select(g => FieldVarint(5, g)));
        return Concat(parts.ToArray());
    }

    private static byte[] MakeFormationInfo(FormationState formation)
    {
        var parts = new List<byte[]> { FieldVarint(1, formation.Id) };
        parts.AddRange(formation.FightCards.Select(c => FieldBytes(2, MakeFightCard(c))));
        parts.Add(FieldBytes(3, formation.Title));
        return Concat(parts.ToArray());
    }

    public static byte[] MakeFormationUpdateNotification(FormationState formation)
        => FieldBytes(1, MakeFormationInfo(formation));

    // ---- 增量更新通知（战斗结算后发送） ----
    public static byte[] MakeMoneyUpdateNotification(MoneyEntry money)
        => FieldBytes(1, MakeMoneyInfo(money));

    public static byte[] MakeItemUpdateNotification(List<InventoryEntry> items)
        => items.Count > 0 ? Concat(items.Select(i => FieldBytes(1, MakeItemInfo(i))).ToArray()) : Array.Empty<byte>();

    public static byte[] MakeGirlUpdateNotification(List<GirlState> girls)
        => girls.Count > 0 ? Concat(girls.Select(g => FieldBytes(1, MakeGirlInfo(g))).ToArray()) : Array.Empty<byte>();

    public static byte[] MakePlayerUpdateNotification(PlayerState player)
        => Concat(
            FieldVarint(1, player.Level),
            FieldVarint(2, player.Exp)
        );

    private static byte[] MakeLevel(LevelState level)
        => Concat(FieldVarint(1, level.Id), FieldVarint(2, level.Star));

    private static byte[] MakeLevelList(List<LevelState> levels)
        => Concat(levels.Select(l => FieldBytes(1, MakeLevel(l))).ToArray());

    // 武器被动技能数据（从 resources/weapon/weapons.txt 提取）
    // key = "detail:particular"，value = passiveSkill1
    private static readonly Dictionary<string, int> WeaponPassiveSkills = new()
    {
        ["1:3"] = 40239,
        ["1:4"] = 40377,
        ["1:5"] = 40156,
        ["1:6"] = 40151,
        ["1:7"] = 40152,
        ["1:8"] = 40123,
        ["1:9"] = 40229,
        ["1:11"] = 40153,
        ["1:12"] = 40191,
        ["1:13"] = 40199,
        ["1:14"] = 40220,
        ["1:15"] = 40255,
        ["1:16"] = 40268,
        ["1:17"] = 40270,
        ["1:18"] = 40290,
        ["1:19"] = 40321,
        ["1:20"] = 40327,
        ["1:21"] = 40334,
        ["1:22"] = 40363,
        ["1:23"] = 40366,
        ["1:24"] = 40386,
        ["1:501"] = 25023,
        ["2:3"] = 40231,
        ["2:4"] = 40164,
        ["2:5"] = 40160,
        ["2:6"] = 40106,
        ["2:8"] = 40166,
        ["2:9"] = 40149,
        ["2:10"] = 40243,
        ["2:11"] = 40356,
        ["2:12"] = 40202,
        ["2:14"] = 40125,
        ["2:15"] = 40222,
        ["2:16"] = 40257,
        ["2:17"] = 40272,
        ["2:18"] = 40274,
        ["2:19"] = 40293,
        ["2:20"] = 40308,
        ["2:21"] = 40346,
        ["2:22"] = 40347,
        ["2:23"] = 40342,
        ["2:24"] = 40368,
        ["2:25"] = 40390,
        ["2:26"] = 40407,
        ["2:501"] = 25021,
        ["2:1000"] = 40388,
        ["3:3"] = 40127,
        ["3:4"] = 40139,
        ["3:5"] = 40183,
        ["3:6"] = 40179,
        ["3:7"] = 40234,
        ["3:8"] = 40246,
        ["3:9"] = 40181,
        ["3:10"] = 40193,
        ["3:11"] = 40204,
        ["3:12"] = 40205,
        ["3:13"] = 40224,
        ["3:14"] = 40260,
        ["3:15"] = 40278,
        ["3:16"] = 40280,
        ["3:17"] = 40392,
        ["3:18"] = 40315,
        ["3:19"] = 40328,
        ["3:20"] = 40344,
        ["3:21"] = 40370,
        ["3:22"] = 40371,
        ["3:23"] = 40370,
        ["3:500"] = 25019,
        ["4:3"] = 40195,
        ["4:4"] = 40211,
        ["4:5"] = 40170,
        ["4:6"] = 40141,
        ["4:7"] = 40168,
        ["4:8"] = 40209,
        ["4:9"] = 40116,
        ["4:10"] = 40226,
        ["4:11"] = 40235,
        ["4:12"] = 40241,
        ["4:15"] = 40129,
        ["4:16"] = 40262,
        ["4:17"] = 40282,
        ["4:18"] = 40284,
        ["4:19"] = 40300,
        ["4:20"] = 40326,
        ["4:21"] = 40330,
        ["4:22"] = 40381,
        ["4:23"] = 40349,
        ["4:24"] = 40373,
        ["4:25"] = 40398,
        ["4:501"] = 25017,
        ["5:3"] = 50633,
        ["5:4"] = 40173,
        ["5:5"] = 40175,
        ["5:6"] = 40171,
        ["5:7"] = 40177,
        ["5:8"] = 40197,
        ["5:9"] = 40213,
        ["5:10"] = 40215,
        ["5:11"] = 40227,
        ["5:12"] = 40237,
        ["5:13"] = 40249,
        ["5:14"] = 40264,
        ["5:15"] = 40286,
        ["5:16"] = 40288,
        ["5:17"] = 40359,
        ["5:18"] = 40323,
        ["5:19"] = 40324,
        ["5:20"] = 40332,
        ["5:21"] = 40338,
        ["5:22"] = 40351,
        ["5:23"] = 40394,
        ["5:24"] = 40338,
        ["5:500"] = 25015,
    };

    private static byte[] MakeMiniSkill(int skillId, int skillLevel, int skillType)
    {
        return Concat(
            FieldVarint(1, skillId),
            FieldVarint(2, skillLevel),
            FieldVarint(3, skillType)
        );
    }

    private static byte[] MakeItemInfo(InventoryEntry item)
    {
        var parts = new List<byte[]>
        {
            FieldVarint(1, item.Guid),
            FieldVarint(2, item.Genre),
            FieldVarint(3, item.Detail),
            FieldVarint(4, item.Particular),
            FieldVarint(5, item.TemplateLevel),
            FieldVarint(6, item.Count),
            FieldVarint(7, item.CreateTime),
            FieldVarint(8, item.EnhanceLevel),
            FieldVarint(9, item.EnhanceExp),
            FieldVarint(10, item.BreakLevel),
        };

        // 武器被动技能 field 11（客户端需要这个才识别武器）
        if (item.Genre == 2)
        {
            var key = $"{item.Detail}:{item.Particular}";
            if (WeaponPassiveSkills.TryGetValue(key, out var skillId) && skillId > 0)
            {
                var skillLevel = (int)item.BreakLevel + 1;
                parts.Add(FieldBytes(11, MakeMiniSkill(skillId, skillLevel, 2)));
            }
        }

        parts.Add(FieldVarint(13, item.LockOn));
        return Concat(parts.ToArray());
    }
}

public class ServerListConfig
{
    public long Id { get; set; }
    public long Aid { get; set; }
    public long Sid { get; set; }
    public string Name { get; set; } = string.Empty;
    public long State { get; set; }
    public long Level { get; set; }
}
