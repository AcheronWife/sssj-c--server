using System.Text.Json.Serialization;

namespace Gcg2OfflineServer.Models;

/// <summary>
/// 玩家持久化状态。
/// </summary>
public class PlayerState
{
    [JsonPropertyName("account")]
    public string Account { get; set; } = string.Empty;

    [JsonPropertyName("roleId")]
    public long RoleId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public long Level { get; set; } = 1;

    [JsonPropertyName("exp")]
    public long Exp { get; set; }

    [JsonPropertyName("fightPower")]
    public long FightPower { get; set; }

    [JsonPropertyName("serverZone")]
    public long ServerZone { get; set; } = 8;

    [JsonPropertyName("registerTime")]
    public long RegisterTime { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public string? LastLoginAt { get; set; }

    [JsonPropertyName("live2dEnableLevel")]
    public long Live2dEnableLevel { get; set; }

    [JsonPropertyName("live2dHX")]
    public bool Live2dHx { get; set; }

    [JsonPropertyName("taskValues")]
    public Dictionary<string, long> TaskValues { get; set; } = new();

    [JsonPropertyName("inventory")]
    public List<InventoryEntry> Inventory { get; set; } = new();

    [JsonPropertyName("nextItemGuid")]
    public long NextItemGuid { get; set; } = 1;

    [JsonPropertyName("money")]
    public List<MoneyEntry> Money { get; set; } = new();

    [JsonPropertyName("girls")]
    public List<GirlState> Girls { get; set; } = new();

    [JsonPropertyName("formations")]
    public List<FormationState> Formations { get; set; } = new();

    [JsonPropertyName("levels")]
    public List<LevelState> Levels { get; set; } = new();

    [JsonPropertyName("cafe")]
    public CafeState Cafe { get; set; } = new();

    [JsonPropertyName("phone")]
    public PhoneState Phone { get; set; } = new();

    [JsonPropertyName("gacha")]
    public GachaState Gacha { get; set; } = new();

    [JsonPropertyName("dailySignUp")]
    public DailySignUpState DailySignUp { get; set; } = new();

    [JsonPropertyName("eightDaySignUp")]
    public EightDaySignUpState EightDaySignUp { get; set; } = new();

    /// <summary>是否为新创建的玩家（lastLoginAt == null）。</summary>
    [JsonIgnore]
    public bool IsNewPlayer => LastLoginAt == null;
}

public class InventoryEntry
{
    [JsonPropertyName("guid")]
    public long Guid { get; set; }

    [JsonPropertyName("genre")]
    public long Genre { get; set; }

    [JsonPropertyName("detail")]
    public long Detail { get; set; }

    [JsonPropertyName("particular")]
    public long Particular { get; set; }

    [JsonPropertyName("templateLevel")]
    public long TemplateLevel { get; set; }

    [JsonPropertyName("count")]
    public long Count { get; set; }

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("enhanceLevel")]
    public long EnhanceLevel { get; set; }

    [JsonPropertyName("enhanceExp")]
    public long EnhanceExp { get; set; }

    [JsonPropertyName("breakLevel")]
    public long BreakLevel { get; set; }

    [JsonPropertyName("lockOn")]
    public long LockOn { get; set; }
}

public class MoneyEntry
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("count")]
    public long Count { get; set; }
}

public class GirlState
{
    [JsonPropertyName("girlId")]
    public long GirlId { get; set; }

    [JsonPropertyName("level")]
    public long Level { get; set; }

    [JsonPropertyName("exp")]
    public long Exp { get; set; }

    [JsonPropertyName("modelId")]
    public long ModelId { get; set; }

    [JsonPropertyName("moodValue")]
    public long MoodValue { get; set; }

    [JsonPropertyName("vigor")]
    public long Vigor { get; set; }

    [JsonPropertyName("flag")]
    public long Flag { get; set; }

    [JsonPropertyName("breakLevel")]
    public long BreakLevel { get; set; }
}

public class FightCardState
{
    [JsonPropertyName("mainCardGuid")]
    public long MainCardGuid { get; set; }

    [JsonPropertyName("secondaryCardGuids")]
    public List<long> SecondaryCardGuids { get; set; } = new();

    [JsonPropertyName("usedCardGuid")]
    public long UsedCardGuid { get; set; }

    [JsonPropertyName("weaponGuid")]
    public long WeaponGuid { get; set; }

    [JsonPropertyName("runeItemGuids")]
    public List<long> RuneItemGuids { get; set; } = new();
}

public class FormationState
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("fightCards")]
    public List<FightCardState> FightCards { get; set; } = new();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public class LevelState
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("star")]
    public long Star { get; set; }
}

public class CafeState
{
    [JsonPropertyName("coffees")]
    public List<CafeCoffee> Coffees { get; set; } = new();

    [JsonPropertyName("waiters")]
    public List<long[]> Waiters { get; set; } = new() { new long[0], new long[0], new long[0] };

    [JsonPropertyName("customers")]
    public List<CafeCustomer> Customers { get; set; } = new();

    [JsonPropertyName("lastCustomerTime")]
    public long LastCustomerTime { get; set; }

    [JsonPropertyName("pets")]
    public List<object> Pets { get; set; } = new();
}

public class CafeCoffee
{
    [JsonPropertyName("coffeetype")]
    public int CoffeeType { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class CafeCustomer
{
    [JsonPropertyName("customertype")]
    public int CustomerType { get; set; }

    [JsonPropertyName("customeridx")]
    public int CustomerIdx { get; set; }

    [JsonPropertyName("starttime")]
    public long StartTime { get; set; }
}

public class PhoneLetterState
{
    [JsonPropertyName("topicId")]
    public long TopicId { get; set; }

    [JsonPropertyName("initiator")]
    public long Initiator { get; set; }

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("replyIds")]
    public List<long> ReplyIds { get; set; } = new();
}

public class PhoneState
{
    [JsonPropertyName("letters")]
    public List<PhoneLetterState> Letters { get; set; } = new();
}

public class GachaState
{
    [JsonPropertyName("pending")]
    public object? Pending { get; set; }
}

public class DailySignUpState
{
    [JsonPropertyName("cycle")]
    public string Cycle { get; set; } = string.Empty;

    [JsonPropertyName("lastOperationalDate")]
    public string? LastOperationalDate { get; set; }
}

public class EightDaySignUpState
{
    [JsonPropertyName("cumulativeDays")]
    public long CumulativeDays { get; set; }

    [JsonPropertyName("lastOperationalDate")]
    public string? LastOperationalDate { get; set; }
}

public class TaskChange
{
    public long Id { get; set; }
    public long Value { get; set; }
}
