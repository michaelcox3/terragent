using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Terragent.World;

/// <summary>
/// Getting an item out of a tile: which tile has it, what breaks it, and whether this
/// pickaxe is strong enough.
/// </summary>
// The other half is <see cref="Drops"/>, which answers the same question about
// creatures. Two ways in, two files, and the choice between them is Sources.How.
internal static class Tiles
{
    /// <summary>Every tile kind the run has a reason to remember the whereabouts of.</summary>
    // Named here and watched by AgentMod: what is worth digging is a fact about
    // Terraria, and a map of where it was seen is memory. Values, not Keys: the tile is
    // what gets indexed, and ItemID.Wood is not TileID.Trees. Ores come from createTile.
    internal static IEnumerable<int> Watched => _spills.Values.Concat(Ores().Values);


    /// <summary>What a tile demands of a pickaxe, and where that demand applies.</summary>
    private readonly record struct Demand(int Pick, bool BelowSurfaceOnly);

    private static Dictionary<int, Demand> _demands = [];

    private static Dictionary<int, int> _spills = [];

    public static void Load(Mod mod)
    {
        Dictionary<int, Demand> demands = [];
        JsonElement root = Data.Read(mod, "Data/mining.json");
        foreach (JsonElement entry in root.GetProperty("requires").EnumerateArray())
        {
            demands[Data.Tile(Data.Text(entry, "tile"))] = new Demand(
                Data.Number(entry, "pick", 0),
                entry.TryGetProperty("belowSurfaceOnly", out JsonElement only)
                && only.GetBoolean());
        }

        _demands = demands;

        Dictionary<int, int> spills = [];
        foreach (JsonElement entry in root.GetProperty("spills").EnumerateArray())
        {
            spills[Data.Item(Data.Text(entry, "item"))] = Data.Tile(Data.Text(entry, "tile"));
        }

        _spills = spills;
    }

    /// <summary>
    /// Whether this pickaxe is strong enough for this tile, here.
    /// </summary>
    // The numbers are in Data/mining.json because vanilla keeps them inline in
    // Player.PickTile and ModTile.MinPick is never set for a vanilla tile. An unknown
    // tile reads as breakable: the agent tries, and the immovable memo catches it.
    public static bool CanBreak(int tileID, int pickPower, int y)
    {
        if (!_demands.TryGetValue(tileID, out Demand demand))
        {
            return true;
        }

        return (demand.BelowSurfaceOnly && y <= Main.worldSurface)
               || pickPower >= demand.Pick;
    }

    /// <summary>Whether this tile comes down with an axe rather than a pickaxe.</summary>
    // A fact about the tile, not the item that comes out of it: whoever is about to
    // swing at a tile looks up what to swing.
    public static bool NeedsAxe(int tileID) =>
        tileID >= 0 && tileID < Main.tileAxe.Length && Main.tileAxe[tileID];

    private static Dictionary<int, int>? _ores;

    /// <summary>The tile this item is mined out of, or null when no tile has it.</summary>
    // Ores are inverted from createTile, filtered by TileID.Sets.Ore so a mod's ores
    // come for free. Unfiltered, every placeable item would qualify and a Work Bench
    // would be mined rather than made, since a tile beats a craft. Everything else
    // (wood off a tree, a glowstick out of a pot) is listed in Data/mining.json,
    // because nothing in the game relates a tile to what falls out of it.
    public static int? MinedFrom(int itemID) =>
        _spills.TryGetValue(itemID, out int spilt) ? spilt
        : Ores().TryGetValue(itemID, out int ore) ? ore
        : null;

    /// <summary>Tiles the agent knows how to turn into an item.</summary>
    public static int ItemFrom(int tileID) => tileID switch
    {
        TileID.Stone => ItemID.StoneBlock,
        TileID.Dirt => ItemID.DirtBlock,
        TileID.Iron => ItemID.IronOre,
        TileID.Lead => ItemID.LeadOre,
        TileID.Copper => ItemID.CopperOre,
        TileID.Tin => ItemID.TinOre,
        TileID.Silver => ItemID.SilverOre,
        TileID.Tungsten => ItemID.TungstenOre,
        TileID.Gold => ItemID.GoldOre,
        TileID.Platinum => ItemID.PlatinumOre,
        _ => ItemID.None,
    };

    /// <summary>
    /// Find the trunk of the tree a found tile belongs to.
    /// </summary>
    public static bool BaseOfTree(ITerrain belief, ref int x, ref int y)
    {
        int bestColumn = 0;
        int bestBottom = 0;
        int bestHeight = 0;

        foreach (int side in new[] { 0, -1, 1 })
        {
            int column = x + side;
            if (belief.TypeAt(column, y) != TileID.Trees)
            {
                continue;
            }

            int bottom = y;
            while (belief.TypeAt(column, bottom + 1) == TileID.Trees)
            {
                bottom++;
            }

            // A trunk stands on the ground. A branch ends in mid-air.
            if (belief.KindAt(column, bottom + 1) is not TileKind.Solid)
            {
                continue;
            }

            int height = 0;
            while (belief.TypeAt(column, bottom - height - 1) == TileID.Trees)
            {
                height++;
            }

            if (height > bestHeight)
            {
                bestColumn = column;
                bestBottom = bottom;
                bestHeight = height;
            }
        }

        // One tile of tree above the base is a root or a stub, not something worth
        // chopping.
        if (bestHeight < 2)
        {
            return false;
        }

        x = bestColumn;
        y = bestBottom;
        return true;
    }

    /// <summary>Every item that comes out of an ore, and the ore it comes out of.</summary>
    private static Dictionary<int, int> Ores()
    {
        if (_ores is not null)
        {
            return _ores;
        }

        _ores = [];
        Item probe = new();
        for (int type = 1; type < ItemID.Count; type++)
        {
            probe.SetDefaults(type);

            // Minus one is "places nothing". Zero is Dirt, and a real answer.
            if (probe.createTile != -1 && TileID.Sets.Ore[probe.createTile])
            {
                _ores[type] = probe.createTile;
            }
        }

        return _ores;
    }

    /// <summary>
    /// Roughly how many swings a tile takes, for deciding one is not coming loose.
    /// </summary>
    public static int ExpectedSwings(int pickPower) => Costs.SwingsPerTile(pickPower);
}
