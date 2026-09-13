using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terragent.World;

/// <summary>The agent's map: what the player has actually seen, and nothing else.</summary>
// The only file allowed to read Main.tile. In singleplayer the whole world is one field
// access away, so the boundary has to be structural: everything above plans over what
// this returns, and what this returns is gated on the player's own map.
//
// Apart from ITerrain.cs, because that file is compiled into the headless tests and this
// one names Terraria.
internal sealed class Terrain : ITerrain
{
    /// <summary>A cell the player has never had on their map.</summary>
    public const int Unknown = -2;

    /// <summary>A cell seen to hold no block.</summary>
    public const int Empty = -1;

    private static HashSet<int>? _stations;

    public bool IsKnown(int x, int y) => InBounds(x, y) && Main.Map.IsRevealed(x, y);

    public float Brightness(int x, int y) => Lighting.Brightness(x, y);

    public int TypeAt(int x, int y)
    {
        if (!IsKnown(x, y))
        {
            return Unknown;
        }

        Tile tile = Main.tile[x, y];
        return tile.HasTile ? tile.TileType : Empty;
    }

    public TileKind KindAt(int x, int y)
    {
        int type = TypeAt(x, y);
        return type switch
        {
            Unknown => TileKind.Unknown,
            Empty => TileKind.Empty,
            _ when Main.tileSolidTop[type] => TileKind.Platform,

            // Rock at the bottom of its cell: holds the character up like a platform and
            // blocks the body unlike one.
            _ when Main.tileSolid[type] && Underfoot(x, y) => TileKind.Slab,
            _ when Main.tileSolid[type] => TileKind.Solid,
            _ => TileKind.Empty,
        };
    }

    public bool Diggable(int x, int y, int pickPower)
    {
        TileKind kind = KindAt(x, y);
        if (kind is TileKind.Unknown)
        {
            // Never. A cell nobody has seen cannot be priced, cannot be watched, and
            // cannot be finished: a swing at unseen air breaks nothing and reveals
            // nothing, so the follower waits for it to go and waits for ever. A run cut
            // fifty rows into fog that turned out to be a pool, drowned its own light and
            // stood swinging at water it could not see.
            //
            // Tunnelling still works, because breaking the face in front of you lights the
            // next one and the map takes it. So a plan reaches the edge of what is known,
            // and the next plan starts from there knowing one cell more. Shorter routes
            // that are all verified, rather than one long one that might be anything.
            return false;
        }

        // A plant goes to any swing, whatever is in hand and however weak it is.
        if (Clutter(x, y))
        {
            return true;
        }

        // A slab has to break too, or every descent through smoothed ground is sealed.
        bool rock = kind is TileKind.Solid or TileKind.Slab;

        // Nothing broken blind. A cell with an unseen neighbour might have water behind it,
        // and there is no way to find out but to break it, at which point the body is
        // standing in it: a run dug two tiles a row through known rock, came through the
        // last one into a pool, and bobbed there with a torch that does not light wet.
        //
        // Light reaches into stone, so this is a rule about seeing rather than a ban on
        // digging. Beside a lit tunnel the map holds seven tiles of rock; in the dark at the
        // bottom of a shaft it holds one. So the way on is to light the ground and then cut
        // it, which is what it is for.
        return rock
            && Surrounded(x, y)
            && pickPower >= Mining.Needs(TypeAt(x, y), y)
            && CanKill(x, y)
            && !SupportsStation(x, y);
    }

    /// <summary>Whether every cell touching this one is on the map.</summary>
    private bool Surrounded(int x, int y)
    {
        for (int across = -1; across <= 1; across++)
        {
            for (int down = -1; down <= 1; down++)
            {
                if (!IsKnown(x + across, y + down))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Fog is not buildable either: a plan through unseen cells replans every step.
    public bool Buildable(int x, int y) => IsKnown(x, y) && TypeAt(x, y) == Empty;

    // tileCut is Terraria's own list of what a swing destroys: grass, plants, vines,
    // cobwebs. Not "anything that is not solid", which was the first reading and is far
    // too wide. A tree trunk is not solid either, and a tree wants an axe, so that reading
    // made every tree diggable with a pickaxe and a run stood swinging at one.
    public bool Clutter(int x, int y) =>
        IsKnown(x, y)
        && TypeAt(x, y) != Empty
        && Main.tileCut[TypeAt(x, y)]
        && CanKill(x, y)
        && !SupportsStation(x, y);

    /// <summary>Whether this cell holds water, which for this purpose includes honey.</summary>
    // Honey counts as water: both put a torch out and stop the map revealing.
    /// <summary>Whether this tile falls once what is under it goes.</summary>
    // The game keeps a table for the sand family. Silt, slush and desert fossil fall the
    // same way and are not in it, so they are named.
    public bool Falls(int x, int y)
    {
        int type = TypeAt(x, y);
        return type != Empty
            && (Main.tileSand[type]
                || type is TileID.Silt or TileID.Slush or TileID.DesertFossil);
    }

    public bool HasWater(int x, int y) =>
        IsKnown(x, y) && Main.tile[x, y].LiquidAmount > 0
        && Main.tile[x, y].LiquidType != LiquidID.Lava;

    /// <summary>Whether this cell holds lava.</summary>
    // Apart from water because lava glows and keeps the map revealing. What is wrong with
    // it is the damage, which the agent does not take yet.
    public bool HasLava(int x, int y) =>
        IsKnown(x, y) && Main.tile[x, y].LiquidAmount > 0
        && Main.tile[x, y].LiquidType == LiquidID.Lava;

    public bool Wet(int x, int y) => HasWater(x, y) || HasLava(x, y);

    private static bool InBounds(int x, int y) =>
        x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY;

    /// <summary>Whether a tile is only partly filled, and filled at the bottom.</summary>
    private static bool Underfoot(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        return tile.HasTile && (tile.IsHalfBlock || tile.TopSlope);
    }

    /// <summary>Whether the game itself permits breaking this tile.</summary>
    private bool CanKill(int x, int y) => IsKnown(x, y) && WorldGen.CanKillTile(x, y);

    /// <summary>Whether a crafting station stands on this tile, or is this tile.</summary>
    // Terraria drops a station the moment what it rests on goes, so the tile under one is
    // not for a route to dig through.
    private bool SupportsStation(int x, int y) =>
        IsStation(TypeAt(x, y)) || IsStation(TypeAt(x, y - 1));

    /// <summary>Tile types that some recipe requires the character to be standing at.</summary>
    // Read from the recipe table so it stays true whatever is loaded.
    private static bool IsStation(int tileID)
    {
        if (tileID is Unknown or Empty)
        {
            return false;
        }

        if (_stations is null)
        {
            _stations = [];
            for (int n = 0; n < Terraria.Recipe.numRecipes; n++)
            {
                foreach (int station in Main.recipe[n].requiredTile)
                {
                    if (station > 0)
                    {
                        _stations.Add(station);
                    }
                }
            }
        }

        return _stations.Contains(tileID);
    }

}
