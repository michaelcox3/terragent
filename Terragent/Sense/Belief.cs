using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;

namespace Terragent.Sense;

/// <summary>
/// The agent's map: what the player has actually seen, and nothing else.
/// </summary>
internal sealed class Belief(Player player) : ITerrain
{
    // Given once, not read from Main.LocalPlayer, so more than one agent can run.
    private readonly Player _player = player;

    /// <summary>A cell the player has never had on their map.</summary>
    public const int Unknown = -2;

    /// <summary>A cell seen to hold no block.</summary>
    public const int Empty = -1;

    public bool InBounds(int x, int y) =>
        x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY;

    /// <summary>Whether this cell is on the player's map.</summary>
    public bool IsKnown(int x, int y) => InBounds(x, y) && Main.Map.IsRevealed(x, y);

    /// <summary>Whether this cell has a background wall behind it.</summary>
    // A torch hangs on a wall as readily as it sits on a floor, so a walled cave is
    // somewhere to put one even with nothing solid adjacent.
    public bool Walled(int x, int y) =>
        IsKnown(x, y) && Main.tile[x, y].WallType != WallID.None;

    /// <summary>Tile id, <see cref="Empty"/>, or <see cref="Unknown"/>.</summary>
    public int TypeAt(int x, int y)
    {
        if (!IsKnown(x, y))
        {
            return Unknown;
        }

        Tile tile = Main.tile[x, y];
        return tile.HasTile ? tile.TileType : Empty;
    }

    /// <summary>
    /// Whether a tile is only partly filled, and filled at the bottom.
    /// </summary>
    public bool Underfoot(int x, int y)
    {
        if (!IsKnown(x, y))
        {
            return false;
        }

        Tile tile = Main.tile[x, y];
        return tile.HasTile
            && (tile.IsHalfBlock || tile.TopSlope);
    }

    /// <summary>
    /// The tile's <c>frameX</c>, which is where Terraria keeps what a tile <i>is</i>
    /// when the type alone cannot say.
    /// </summary>
    public int FrameAt(int x, int y) => IsKnown(x, y) ? Main.tile[x, y].TileFrameX : -1;

    /// <summary>
    /// Which way a floor slope faces, or null when the tile is a half block.
    /// </summary>
    public bool? SlopeIsLowOnLeft(int x, int y)
    {
        if (!IsKnown(x, y))
        {
            return null;
        }

        Tile tile = Main.tile[x, y];
        return tile.TopSlope ? tile.LeftSlope : null;
    }

    public TileKind KindAt(int x, int y)
    {
        int type = TypeAt(x, y);
        return type switch
        {
            Unknown => TileKind.Unknown,
            Empty => TileKind.Empty,
            _ when Main.tileSolidTop[type] => TileKind.Platform,

            // Rock at the bottom of its cell: holds the character up like a platform
            // and blocks the body unlike one.
            _ when Main.tileSolid[type] && Underfoot(x, y) => TileKind.Slab,
            _ when Main.tileSolid[type] => TileKind.Solid,
            _ => TileKind.Empty,
        };
    }

    /// <summary>Where each watched kind has been seen, as this character saw it.</summary>
    // One per belief, because two agents do not share a memory. The sweep below fills
    // it in.
    public TileIndex Sites { get; } = new();

    /// <summary>Where this belief's own character stands, for the sweep.</summary>
    // Private: Character answers where the body is. This only tells the sweep where to
    // look.
    private Point Footing => Body.Footing(_player.position, _player.height);

    /// <summary>
    /// Whether the game would accept this object placed with its origin here.
    /// </summary>
    public bool CanPlace(int x, int y, int tileID, int style = 0)
    {
        TileObjectData data = TileObjectData.GetTileData(tileID, style);
        if (data is null)
        {
            return false;
        }

        int left = x - data.Origin.X;
        int top = y - data.Origin.Y;
        for (int dx = 0; dx < data.Width; dx++)
        {
            for (int dy = 0; dy < data.Height; dy++)
            {
                // The anchor row below the footprint must be seen too, or a bench lands
                // on an unseen slope.
                if (!IsKnown(left + dx, top + dy) || !IsKnown(left + dx, top + data.Height))
                {
                    return false;
                }
            }
        }

        return TileObject.CanPlace(x, y, tileID, style, 1, out _, onlyCheck: true);
    }

    /// <summary>The footprint an object placed here would occupy.</summary>
    public Rectangle Footprint(int x, int y, int tileID, int style = 0)
    {
        TileObjectData data = TileObjectData.GetTileData(tileID, style);
        return data is null
            ? new Rectangle(x, y, 1, 1)
            : new Rectangle(x - data.Origin.X, y - data.Origin.Y, data.Width, data.Height);
    }

    private int _sweptTo;

    /// <summary>How many cells a tick may look at to keep the index true.</summary>
    // The box is about forty thousand cells, so a full pass takes twenty ticks: a third
    // of a second to notice a change, well inside any walk.
    private const int SweepBudget = 2000;

    /// <summary>How far around the character the index is kept fresh, in tiles.</summary>
    // Beyond this, memory outlives the walk: a tile that changed out of sight costs one
    // wasted approach and is corrected on arrival.
    private const int SweepAcross = 120;

    private const int SweepDown = 80;

    /// <summary>
    /// Look at a slice of the ground nearby, and tell the index what is there.
    /// </summary>
    // Rolling rather than reacting: tiles enter when the map lights them, which is not
    // an event, and leave when anything at all breaks them. A sweep needs no hook for
    // each, and being wrong for a third of a second costs one walk.
    private bool _primed;

    public void Sweep()
    {
        Point at = Footing;
        int left = at.X - SweepAcross;
        int top = at.Y - SweepDown;
        int wide = (SweepAcross * 2) + 1;
        int deep = (SweepDown * 2) + 1;
        int cells = wide * deep;

        // The first pass is the whole box. An empty index reads as "nothing near here",
        // not "not looked yet", and costs the objective a backoff before it retries.
        int budget = _primed ? SweepBudget : cells;
        _primed = true;

        for (int n = 0; n < budget; n++)
        {
            _sweptTo = (_sweptTo + 1) % cells;
            int x = left + (_sweptTo % wide);
            int y = top + (_sweptTo / wide);
            if (!IsKnown(x, y))
            {
                continue;
            }

            Recheck(x, y);
        }
    }

    /// <summary>Look at one cell now, and tell the index what it holds.</summary>
    private void Recheck(int x, int y)
    {
        int type = TypeAt(x, y);
        if (TileIndex.Watching(type))
        {
            Sites.Holds(type, x, y);
        }
        else
        {
            Sites.Gone(x, y);
        }
    }

    /// <summary>
    /// Nearest remembered tile of a kind, checked against the world before it is
    /// handed out.
    /// </summary>
    // Between felling a tree and the sweep reaching its cell the index still remembers
    // it standing, and the caller cannot tell "gone" from "wrong shape". Corrected
    // rather than skipped, or every call until the sweep arrives picks the same dead one.
    public bool NearestSite(Point from, int tileID, System.Func<Point, bool> skip,
        out int foundX, out int foundY)
    {
        while (Sites.Nearest(from, tileID, skip, out foundX, out foundY))
        {
            // Unknown is not disproof: remembering ground no longer lit is what the index
            // is for, so only a cell the map can see now may overrule it.
            if (!IsKnown(foundX, foundY) || TypeAt(foundX, foundY) == tileID)
            {
                return true;
            }

            Recheck(foundX, foundY);
        }

        return false;
    }

    /// <summary>How far the agent notices things: the game's own idea of on screen.</summary>
    // NPC.sWidth and sHeight rather than the rendered screen: they are fixed whatever
    // the monitor, and are what Terraria measures spawning and despawning against.
    // Sixty tiles across and thirty seven down, and a rectangle on purpose: a square of
    // sixty noticed targets a screen and a half below and went after them.
    private Rectangle Around() =>
        new((int)_player.Center.X - (NPC.sWidth / 2), (int)_player.Center.Y - (NPC.sHeight / 2),
            NPC.sWidth, NPC.sHeight);

    /// <summary>Creatures the player can actually see.</summary>
    public List<NPC> VisibleEnemies()
    {
        List<NPC> seen = [];
        Rectangle screen = Around();

        foreach (NPC npc in Main.npc)
        {
            if (!npc.active || npc.friendly || npc.townNPC || npc.life <= 0)
            {
                continue;
            }

            // Critters are neither friendly nor town NPCs, and are not worth a fight.
            if (NPCID.Sets.CountsAsCritter[npc.type] || NPCID.Sets.TownCritter[npc.type])
            {
                continue;
            }

            if (screen.Intersects(npc.Hitbox))
            {
                seen.Add(npc);
            }
        }

        return seen;
    }

    /// <summary>Whether a shot from here would reach there without hitting tiles.</summary>
    // The game's own line test; a shot the ground would stop is not worth taking.
    public bool CanSee(Vector2 from, int fromWidth, int fromHeight, Vector2 to,
        int toWidth, int toHeight) =>
        Collision.CanHitLine(from, fromWidth, fromHeight, to, toWidth, toHeight);

    /// <summary>How lit a cell is, from nothing at zero to full daylight at one.</summary>
    public float Brightness(int x, int y) => Lighting.Brightness(x, y);

    /// <summary>Whether the game will let this tile be broken at all.</summary>
    public bool CanKill(int x, int y) => IsKnown(x, y) && WorldGen.CanKillTile(x, y);

    /// <summary>
    /// Whether this tile can be mined with this pickaxe: strong enough, and permitted.
    /// </summary>
    public bool Diggable(int x, int y, int pickPower)
    {
        TileKind kind = KindAt(x, y);
        if (kind is TileKind.Unknown)
        {
            return true;
        }

        // A slab must break too, or every descent through smoothed ground is sealed.
        bool rock = kind is TileKind.Solid or TileKind.Slab;

        return rock
            && Tiles.CanBreak(TypeAt(x, y), pickPower, y)
            && CanKill(x, y)
            && !SupportsStation(x, y);
    }

    /// <summary>Whether a crafting station stands on this tile, or is this tile.</summary>
    // Terraria drops a station the moment what it rests on goes, so the tile under one
    // is not for a route to dig through.
    public bool SupportsStation(int x, int y) =>
        IsStation(TypeAt(x, y)) || IsStation(TypeAt(x, y - 1));

    /// <summary>The same question about a cell, for handing to a search as a predicate.</summary>
    public bool SupportsStation(Point cell) => SupportsStation(cell.X, cell.Y);

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
            for (int i = 0; i < Recipe.numRecipes; i++)
            {
                foreach (int stationTileID in Main.recipe[i].requiredTile)
                {
                    if (stationTileID > 0)
                    {
                        _stations.Add(stationTileID);
                    }
                }
            }
        }

        return _stations.Contains(tileID);
    }

    private static HashSet<int>? _stations;

    private HashSet<Point>? _npcTiles;

    private uint _npcTilesAt = uint.MaxValue;

    /// <summary>Every tile an active NPC's hitbox covers.</summary>
    // All of them, the Guide included: Terraria silently refuses a placement inside any
    // NPC, and the follower reads the refusal as a block not yet landed. Cached per
    // frame because the search asks this of every cell it considers building in.
    private HashSet<Point> TilesWithNpcs()
    {
        if (_npcTiles is not null && _npcTilesAt == Main.GameUpdateCount)
        {
            return _npcTiles;
        }

        _npcTiles = [];
        _npcTilesAt = Main.GameUpdateCount;
        foreach (NPC npc in Main.npc)
        {
            if (!npc.active)
            {
                continue;
            }

            Rectangle box = npc.Hitbox;
            for (int x = box.Left / 16; x <= (box.Right - 1) / 16; x++)
            {
                for (int y = box.Top / 16; y <= (box.Bottom - 1) / 16; y++)
                {
                    _npcTiles.Add(new Point(x, y));
                }
            }
        }

        return _npcTiles;
    }

    /// <summary>Whether a block could go here: the cell holds nothing at all.</summary>
    // Not KindAt: a torch is Empty to the body, but Terraria silently refuses to build
    // over it, and the follower cannot tell a refusal from a block not yet landed. Fog
    // is not buildable either; a plan through unseen cells replans every step.
    public bool Buildable(int x, int y) =>
        IsKnown(x, y) && TypeAt(x, y) == Empty && !TilesWithNpcs().Contains(new Point(x, y));

    /// <summary>Whether this cell holds water, which for this purpose includes honey.</summary>
    // Honey counts as water: both put a torch out and stop the map revealing.
    public bool HasWater(int x, int y) =>
        IsKnown(x, y) && Main.tile[x, y].LiquidAmount > 0
        && Main.tile[x, y].LiquidType != LiquidID.Lava;

    /// <summary>Whether this cell holds lava.</summary>
    // Apart from water because lava glows and keeps the map revealing; what is wrong
    // with it is the damage, which the agent does not take yet.
    public bool HasLava(int x, int y) =>
        IsKnown(x, y) && Main.tile[x, y].LiquidAmount > 0
        && Main.tile[x, y].LiquidType == LiquidID.Lava;

    /// <summary>
    /// Whether this cell holds any liquid at all, which is what a torch minds.
    /// </summary>
    public bool Wet(int x, int y) => HasWater(x, y) || HasLava(x, y);

    /// <summary>What is standing on this tile, if the game is protecting it.</summary>
    public Point? Protector(int x, int y) =>
        !CanKill(x, y) && KindAt(x, y - 1) is TileKind.Solid ? new Point(x, y - 1) : null;

    /// <summary>
    /// Items lying on the ground that the player can see.
    /// </summary>
    public List<Item> VisibleDrops(int itemID = 0)
    {
        List<Item> seen = [];
        Rectangle screen = Around();

        foreach (Item item in Main.item)
        {
            if (item is null || !item.active || item.IsAir)
            {
                continue;
            }

            if (itemID != 0 && item.type != itemID)
            {
                continue;
            }

            if (screen.Intersects(item.Hitbox))
            {
                seen.Add(item);
            }
        }

        return seen;
    }

    /// <summary>
    /// How much of the world is on the player's map, sampled.
    /// </summary>
    public float Coverage(int stride = 8)
    {
        int seen = 0;
        int total = 0;
        for (int y = 0; y < Main.maxTilesY; y += stride)
        {
            for (int x = 0; x < Main.maxTilesX; x += stride)
            {
                total++;
                if (Main.Map.IsRevealed(x, y))
                {
                    seen++;
                }
            }
        }

        return total == 0 ? 0f : (float)seen / total;
    }
}
