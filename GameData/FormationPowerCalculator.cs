using Gcg2OfflineServer.Models;

namespace Gcg2OfflineServer.GameData;

/// <summary>
/// 编队战力计算。
/// </summary>
public static class FormationPowerCalculator
{
    private static readonly double[] RARITY_WEIGHT = { 0, 0.9, 1.0, 1.08, 1.1664 };
    private static readonly double[] RUNE_RARITY_WEIGHT = { 0, 0.85, 1.0, 1.25, 1.56 };
    private const double CARD_RARITY_BASE = 395.7;
    private const double WEAPON_RARITY_BASE = 395.7;
    private const double DEPUTY_RARITY_BASE = 395.7;
    private const double RUNE_RARITY_BASE = -0.0857;

    private static int NormalizeRarity(InventoryEntry item)
    {
        int rarity = (int)item.TemplateLevel;
        if (rarity < 1) rarity = 1;
        if (rarity > 4) rarity = 4;
        return rarity;
    }

    private static double Polynomial(int level, double rarityBase)
    {
        return rarityBase + 10.192 * level - 0.0168 * level * level + 0.0005 * level * level * level;
    }

    private static double CardPower(InventoryEntry item, bool isMain)
    {
        int rarity = NormalizeRarity(item);
        double baseVal = isMain ? CARD_RARITY_BASE : DEPUTY_RARITY_BASE;
        double roleWeight = isMain ? 1.875 : 0.675;
        return (Polynomial((int)item.EnhanceLevel, baseVal) + (int)item.BreakLevel * 20) * roleWeight * RARITY_WEIGHT[rarity];
    }

    private static double WeaponPower(InventoryEntry item)
    {
        int rarity = NormalizeRarity(item);
        return (Polynomial((int)item.EnhanceLevel, WEAPON_RARITY_BASE) + (int)item.BreakLevel * 8) * 0.675 * RARITY_WEIGHT[rarity];
    }

    private static double RunePower(InventoryEntry item)
    {
        int rarity = NormalizeRarity(item);
        return (RUNE_RARITY_BASE + 10.411 * (int)item.EnhanceLevel) * 0.47 * RUNE_RARITY_WEIGHT[rarity] + (int)item.BreakLevel * 28.63;
    }

    private static long Round(double value)
    {
        return (long)Math.Floor(value + 0.5);
    }

    /// <summary>
    /// 计算玩家最高编队战力
    /// </summary>
    public static long CalculateMaxFormationPower(PlayerState player)
    {
        if (player.Inventory == null || player.Formations == null) return 0;

        var inventory = new Dictionary<long, InventoryEntry>();
        foreach (var item in player.Inventory)
        {
            inventory[item.Guid] = item;
        }

        long maximum = 0;
        foreach (var formation in player.Formations)
        {
            if (formation.FightCards == null) continue;
            long total = 0;
            foreach (var fightCard in formation.FightCards)
            {
                if (!inventory.TryGetValue(fightCard.MainCardGuid, out var main) || main.Genre != 1)
                    continue;

                double groupPower = CardPower(main, true);

                if (inventory.TryGetValue(fightCard.WeaponGuid, out var weapon) && weapon.Genre == 2)
                {
                    groupPower += WeaponPower(weapon);
                }

                int deputyCount = 0;
                foreach (var guid in fightCard.SecondaryCardGuids ?? new List<long>())
                {
                    if (deputyCount >= 3) break;
                    if (inventory.TryGetValue(guid, out var deputy) && deputy.Genre == 1)
                    {
                        groupPower += CardPower(deputy, false);
                        deputyCount++;
                    }
                }

                int runeCount = 0;
                foreach (var guid in fightCard.RuneItemGuids ?? new List<long>())
                {
                    if (runeCount >= 4) break;
                    if (inventory.TryGetValue(guid, out var rune))
                    {
                        groupPower += RunePower(rune);
                        runeCount++;
                    }
                }

                total += Round(groupPower);
            }
            if (total > maximum) maximum = total;
        }
        return maximum;
    }
}
