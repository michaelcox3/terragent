using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace Terragent.World;

/// <summary>
/// What a creature drops, asked of the game rather than assumed.
/// </summary>
internal static class Drops
{
    private static readonly Dictionary<int, HashSet<int>> _cache = [];

    private static readonly Dictionary<int, List<int>> _sources = [];

    /// <summary>
    /// Every creature that can drop this item.
    /// </summary>
    public static IReadOnlyList<int> Droppers(int itemID)
    {
        if (_sources.TryGetValue(itemID, out List<int>? known))
        {
            return known;
        }

        List<int> found = [];
        for (int type = 1; type < NPCID.Count; type++)
        {
            if (ItemsFrom(type).Contains(itemID))
            {
                found.Add(type);
            }
        }

        _sources[itemID] = found;
        return found;
    }

    /// <summary>
    /// How many of an item one kill yields on average, counting the odds.
    /// </summary>
    // Straight out of the game's drop table, rather than a copy that goes stale the
    // moment a version changes the numbers.
    public static float PerKill(int npcNetID, int itemID)
    {
        float total = 0f;
        try
        {
            List<DropRateInfo> rates = [];
            DropRateInfoChainFeed feed = new(1f);
            foreach (IItemDropRule rule in Main.ItemDropsDB.GetRulesForNPCID(npcNetID))
            {
                rule.ReportDroprates(rates, feed);
            }

            foreach (DropRateInfo rate in rates)
            {
                if (rate.itemId == itemID)
                {
                    total += rate.dropRate * (rate.stackMin + rate.stackMax) / 2f;
                }
            }
        }
        catch (System.Exception)
        {
            // A mod's rule that will not report is not worth ending a run over.
        }

        return total;
    }

    /// <summary>Whether killing this creature can yield this item.</summary>
    public static bool Yields(int npcNetID, int itemID) => ItemsFrom(npcNetID).Contains(itemID);

    /// <summary>
    /// Everything this creature can drop.
    /// </summary>
    public static IReadOnlySet<int> ItemsFrom(int npcNetID)
    {
        if (_cache.TryGetValue(npcNetID, out HashSet<int>? known))
        {
            return known;
        }

        HashSet<int> items = [];
        try
        {
            List<DropRateInfo> rates = [];
            DropRateInfoChainFeed feed = new(1f);
            foreach (IItemDropRule rule in Main.ItemDropsDB.GetRulesForNPCID(npcNetID))
            {
                rule.ReportDroprates(rates, feed);
            }

            foreach (DropRateInfo rate in rates)
            {
                items.Add(rate.itemId);
            }
        }
        catch (System.Exception)
        {
            // A mod's rule that will not report is not worth ending a run over.
        }

        _cache[npcNetID] = items;
        return items;
    }
}
