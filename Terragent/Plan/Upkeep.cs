using System.Collections.Generic;
using System.Text.Json;
using Terraria;
using Terraria.ModLoader;

namespace Terragent.Plan;

/// <summary>One supply the run has to keep in hand, as data/upkeep.json describes it.</summary>
/// <param name="Name">The key a progression node names in its keeps.</param>
/// <param name="Tier">Which entry wins when several for the same item are in force.</param>
/// <param name="Ceiling">How many to top up to.</param>
/// <param name="RestockAt">Carrying this many or fewer is the moment to stop and restock.</param>
/// <param name="Reserve">How many may never be spent.</param>
// A count, not a share of the ceiling: a share is truncated to an integer and has the
// reserve taken off before it is compared, so the number the file fires at is three
// steps of arithmetic away from the one written in it.
internal sealed record Upkeep(string Name, int ItemID, int Tier, int Ceiling,
    int RestockAt, int Reserve);

/// <summary>
/// Requirements that must hold continuously, rather than goals reached once.
/// </summary>
// What is kept, and how much, is data. When each one starts mattering is the
// progression's: a node names the upkeeps that come into force once it is done.
internal static class Upkeeps
{
    private static Upkeep[] _upkeeps = [];

    public static void Load(Mod mod)
    {
        JsonElement root = Data.Read(mod, "Data/upkeep.json");
        List<Upkeep> upkeeps = [];
        foreach (JsonElement entry in root.GetProperty("upkeeps").EnumerateArray())
        {
            upkeeps.Add(new Upkeep(
                Data.Text(entry, "name"),
                Data.Item(Data.Text(entry, "item")),
                Data.Number(entry, "tier", 1),
                Data.Number(entry, "ceiling", 0),
                Data.Number(entry, "restockAt", 0),
                Data.Number(entry, "reserve", 0)));
        }

        _upkeeps = [.. upkeeps];
    }

    /// <summary>The upkeeps in force, given what the run has finished.</summary>
    // Several entries may describe the same supply at different points of a run, and
    // the highest tier unlocked is the one that applies. Said outright in the file
    // rather than inferred from how far along each was unlocked, so reordering the
    // progression cannot quietly change which supply is in force.
    public static IReadOnlyList<Upkeep> For(ISet<string> unlocked)
    {
        Dictionary<int, Upkeep> best = [];
        foreach (Upkeep upkeep in _upkeeps)
        {
            if (unlocked.Contains(upkeep.Name)
                && (!best.TryGetValue(upkeep.ItemID, out Upkeep? held)
                    || upkeep.Tier > held.Tier))
            {
                best[upkeep.ItemID] = upkeep;
            }
        }

        List<Upkeep> standing = [];
        foreach (Upkeep upkeep in best.Values)
        {
            standing.Add(upkeep);
        }

        return standing;
    }

    /// <summary>Every standing requirement that is short.</summary>
    // All of them, not the first: one at a time, a supply that cannot be had holds the
    // slot. Glowsticks come out of pots, and a run that has found none never gets asked
    // about the torches behind them.
    public static List<Upkeep> Short(Inventory inventory, ISet<string> unlocked)
    {
        List<Upkeep> shorts = [];
        foreach (Upkeep standing in For(unlocked))
        {
            if (inventory.Carrying(standing.ItemID) <= standing.RestockAt)
            {
                shorts.Add(standing);
            }
        }

        return shorts;
    }

    /// <summary>How many of an item are never to be spent.</summary>
    // Asked of the data rather than written into whatever spends it. As a literal one
    // in Lamplighting.cs, the rule (keep one back to see by) lived only in the code.
    public static int Reserved(int itemID)
    {
        int most = 0;
        foreach (Upkeep upkeep in _upkeeps)
        {
            if (upkeep.ItemID == itemID && upkeep.Reserve > most)
            {
                most = upkeep.Reserve;
            }
        }

        return most;
    }
}
