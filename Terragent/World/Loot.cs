using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace Terragent.World;

/// <summary>What killing something leaves behind.</summary>
// Asked of the game's own drop database rather than written down, so it cannot go stale
// the way a copied table does when a version changes the odds. It is the same source the
// Bestiary reads.
//
// Built backwards, item to creatures, because that is the direction a run asks in: it
// wants gel and has to be told a slime is where gel comes from.
internal static class Loot
{
    private static Dictionary<int, List<int>>? _index;

    /// <summary>Every creature that can drop this item.</summary>
    public static IReadOnlyList<int> Droppers(int itemID) =>
        Index().TryGetValue(itemID, out List<int>? from) ? from : [];

    /// <summary>Whether anything in the game drops this item.</summary>
    public static bool Dropped(int itemID) => Index().ContainsKey(itemID);

    /// <summary>The whole table, item to the creatures that leave it, built once.</summary>
    // Lazily, because the drop database is filled during load and asking too early gets an
    // empty answer that would then be cached for the run.
    private static Dictionary<int, List<int>> Index()
    {
        if (_index is not null)
        {
            return _index;
        }

        _index = [];
        List<DropRateInfo> rates = [];
        for (int type = 1; type < NPCID.Count; type++)
        {
            rates.Clear();
            try
            {
                DropRateInfoChainFeed feed = new(1f);
                foreach (IItemDropRule rule in Main.ItemDropsDB.GetRulesForNPCID(type))
                {
                    rule.ReportDroprates(rates, feed);
                }
            }
            catch (System.Exception)
            {
                // A rule that will not report is not worth ending a run over. Modded ones
                // can throw here, and one creature missing from the table is a creature
                // the agent will not go hunting.
                continue;
            }

            foreach (DropRateInfo rate in rates)
            {
                if (!_index.TryGetValue(rate.itemId, out List<int>? from))
                {
                    from = [];
                    _index[rate.itemId] = from;
                }

                if (!from.Contains(type))
                {
                    from.Add(type);
                }
            }
        }

        return _index;
    }

}
