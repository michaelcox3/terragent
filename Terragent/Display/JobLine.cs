using Terraria;
using Terraria.ID;

namespace Terragent.Display;

/// <summary>What the panel calls one job.</summary>
// Here rather than in the Executor: a planner that formats its own display cannot
// have its words changed without touching the thing that decides what to do.
internal static class JobLine
{
    /// <summary>What the panel calls one job: what would be done, not what is wanted.</summary>
    // Nouns did not survive crafting being a job: "1 Iron Pickaxe" beside "51 Iron Ore"
    // reads as two things to fetch, and only one of them is somewhere to walk. Naming
    // the action says which is which.
    internal static string Doing(Executor.Job job)
    {
        string what = $"{job.ItemNeeded.Count} {Lang.GetItemNameValue(job.ItemNeeded.ItemID)}";
        string line = Verb(job, what);

        // Where the board expects to find it when nothing has been seen, because "Mine
        // 51 Iron Ore" beside a shaft says nothing about why the shaft is being dug.
        if (job.Where is null && job.Expected is { } row)
        {
            line = $"{line}, expected near row {row}";
        }

        return line;
    }

    private static string Verb(Executor.Job job, string what)
    {
        return job.Source.From switch
        {
            Origin.Drop => $"Pick up {what}",
            Origin.Craft => $"Craft {what}",
            Origin.Creature => $"Kill {Fighting.Hunted(job.ItemNeeded.ItemID)} for {what}",
            Origin.Tile when Tiles.NeedsAxe(job.Source.TileID) => $"Chop {what}",

            // "Mine 10 Glowstick" when the tile is a pot says nothing about pots, so
            // the name comes from the tile itself. ItemIdOf is a table of ores and
            // answers None for everything else, naming only the case that needs none.
            Origin.Tile when Tiles.ItemFrom(job.Source.TileID) != job.ItemNeeded.ItemID =>
                $"Break {TileID.Search.GetName(job.Source.TileID)} for {what}",
            Origin.Tile => $"Mine {what}",
            _ => $"Find {what}",
        };
    }
}
