using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Terragent.World;

/// <summary>What a tile demands of a pickaxe, and what it leaves behind.</summary>
// Terraria keeps the pickaxe requirements buried in Player.PickTile as a run of
// hard-coded comparisons, and keeps no table at all for what a tile drops that the agent
// can read. Both are written out in Data/mining.json instead, so changing a number is
// changing a file.
//
// Load-once tables. Static because they are the same for every character and reading them
// twice would be reading the same file twice.
internal static class Mining
{
    private static readonly Dictionary<int, int> Requires = [];

    private static readonly HashSet<int> Underground = [];

    private static readonly Dictionary<int, int> Drops = [];

    /// <summary>The column a tile was found in, and the two a branch could hang off.</summary>
    private static readonly int[] Sides = [0, -1, 1];

    /// <summary>Read the tables out of the mod, once, at load.</summary>
    public static void Load(Mod mod)
    {
        Requires.Clear();
        Underground.Clear();
        Drops.Clear();

        JsonElement root = Data.Read(mod, "Data/mining.json");

        foreach (JsonElement entry in root.GetProperty("requires").EnumerateArray())
        {
            int tile = Data.Tile(Data.Text(entry, "tile"));
            Requires[tile] = Data.Number(entry, "pick", 1);
            if (entry.TryGetProperty("belowSurfaceOnly", out JsonElement below)
                && below.GetBoolean())
            {
                Underground.Add(tile);
            }
        }

        foreach (JsonElement entry in root.GetProperty("spills").EnumerateArray())
        {
            Drops[Data.Tile(Data.Text(entry, "tile"))] = Data.Item(Data.Text(entry, "item"));
        }
    }

    /// <summary>The pickaxe power this tile demands at this depth.</summary>
    // Depth matters for the two evil ores, which grow above ground in the chasms as well
    // as below and are ordinary rock up there. Everything unlisted yields to anything.
    public static int Needs(int tileID, int y)
    {
        if (!Requires.TryGetValue(tileID, out int power))
        {
            return 1;
        }

        return Underground.Contains(tileID) && y <= Main.worldSurface ? 1 : power;
    }

    /// <summary>Which tiles leave this item when broken, empty when nothing does.</summary>
    // The other way round from Spills, and the direction a job asks in: the run wants wood
    // and has to be told a tree is where wood comes from.
    public static IReadOnlyList<int> Yields(int itemID)
    {
        List<int> tiles = [];
        foreach (KeyValuePair<int, int> spill in Drops)
        {
            if (spill.Value == itemID)
            {
                tiles.Add(spill.Key);
            }
        }

        // What an item places, it also comes out of. Ore, stone, dirt, sand and clay all
        // round trip, and the game answers that one itself, so the file only carries the
        // pairs that do not: a tree gives wood and no wood places a tree.
        int placed = ContentSamples.ItemsByType[itemID].createTile;
        if (placed > 0 && !tiles.Contains(placed))
        {
            tiles.Add(placed);
        }

        return tiles;
    }

    /// <summary>Whether this tile comes down to an axe rather than a pickaxe.</summary>
    // The game's own flag. Trees and cactus are axe work, and swinging a pickaxe at one
    // does nothing at all rather than doing it slowly.
    public static bool NeedsAxe(int tileID) =>
        tileID >= 0 && tileID < Main.tileAxe.Length && Main.tileAxe[tileID];

    /// <summary>The foot of the tree a found tile belongs to, or null when it is not one.</summary>
    // A tree is one tile type from root to leaf, and the tile that gets found is as often a
    // branch as the trunk. A branch ends in mid air; a trunk stands on something solid, and
    // that is the test. The neighbouring columns are tried too, because a branch sticks out
    // sideways from the trunk that owns it.
    //
    // Cutting the trunk at its foot fells the whole tree. Cutting it halfway fells only
    // what is above the cut, and swinging at a branch does nothing at all.
    public static Point? TreeBase(ITerrain terrain, Point found)
    {
        int column = 0;
        int foot = 0;
        int tallest = 0;

        foreach (int side in Sides)
        {
            int at = found.X + side;
            if (terrain.TypeAt(at, found.Y) != TileID.Trees)
            {
                continue;
            }

            int bottom = found.Y;
            while (terrain.TypeAt(at, bottom + 1) == TileID.Trees)
            {
                bottom++;
            }

            if (terrain.KindAt(at, bottom + 1) is not TileKind.Solid)
            {
                continue;
            }

            int height = 0;
            while (terrain.TypeAt(at, bottom - height - 1) == TileID.Trees)
            {
                height++;
            }

            if (height > tallest)
            {
                column = at;
                foot = bottom;
                tallest = height;
            }
        }

        // One tile of tree above the foot is a root or a stump, not worth an axe.
        return tallest < 2 ? null : new Point(column, foot);
    }

    /// <summary>What breaking this tile leaves on the ground, or nothing.</summary>
    // Not every tile, only the ones a run goes out of its way for. Wood from a tree, stone
    // from stone, and a glowstick from a pot, which is the only light that works wet.
    public static int Spills(int tileID) =>
        Drops.TryGetValue(tileID, out int item) ? item : 0;
}
