using Terraria.ID;

namespace Terragent.World;

/// <summary>Which item puts down which crafting station, and the other way about.</summary>
// Apart from Sense.Stations, which remembers where stations are: which item puts down
// which station is a fact about Terraria, and Recipes must not reach into perception.
internal static class StationKinds
{
    /// <summary>The item that puts this station down, or zero if we know of none.</summary>
    // Keyed on the tile, not a progression node: stations are carried tools, not goals.
    public static int ItemFor(int tileID) => tileID switch
    {
        TileID.WorkBenches => ItemID.WorkBench,
        TileID.Furnaces => ItemID.Furnace,
        TileID.Anvils => ItemID.IronAnvil,
        _ => 0,
    };

    /// <summary>Which tile an item puts down, the other way round from ItemFor.</summary>
    // The recipe tree names the item a station is carried as; whatever asks whether
    // one is nearby asks about the tile.
    public static int TileFor(int itemID) => itemID switch
    {
        ItemID.WorkBench => TileID.WorkBenches,
        ItemID.Furnace => TileID.Furnaces,
        ItemID.IronAnvil => TileID.Anvils,
        _ => 0,
    };

    /// <summary>Remember where the stations are, so finding one is a lookup.</summary>
    // Named here and watched by AgentMod: which tiles are stations is a fact about
    // Terraria, and a map of where they were seen is memory.
    public static readonly int[] Watched =
        [TileID.WorkBenches, TileID.Furnaces, TileID.Anvils];
}
