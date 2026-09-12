#if TESTING
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terragent.Tests;

/// <summary>Somewhere to run a scenario: tiles written into the sky and taken back after.</summary>
// Writing into somebody's world is the part with the sharp edges, and they were all found
// the hard way. An unrevealed cell is fog and not air, so empty space has to be shown as
// well as left empty. Placing a tile reframes its neighbours, so shapes go on once every
// neighbour exists. Everything written is remembered so it can be undone.
//
// Test scaffolding, so it is compiled only in Debug behind TESTING and never ships.
internal sealed class Pen
{
    /// <summary>Rows above the surface to build at, high enough to be empty sky.</summary>
    // Clamped below the space layer, where gravity changes and a jump stops meaning what
    // it means everywhere else.
    private const int Altitude = 220;

    private readonly HashSet<Point> _built = [];

    /// <summary>How far outside the written cells to sweep when taking it all back.</summary>
    // The agent builds too, and nothing here knows where. A scenario that pillars or
    // bridges leaves blocks nobody recorded, every grid is stamped in the same place, and
    // the next scenario is then run in somebody else's rubble: one of them found a footing
    // to jump to that its own grid never had, and the body sailed off into open sky.
    private const int Spill = 12;

    /// <summary>The box everything written so far sits in, for taking it back.</summary>
    private Rectangle _box;

    private readonly List<(Point At, bool Half, bool LowLeft)> _shapes = [];

    private readonly List<(Point At, int TileID)> _furniture = [];

    private static readonly Dictionary<bool, SlopeType> Slopes = [];

    /// <summary>Every cell this pen has written, for teardown.</summary>
    public IReadOnlyCollection<Point> Built => _built;

    /// <summary>The row a grid of this height should start at.</summary>
    public static int Top(int margin, int height) =>
        System.Math.Max(
            (int)(Main.worldSurface * 0.35) + margin + height + 4,
            (int)Main.worldSurface - Altitude);

    /// <summary>Put a tile down, or clear the cell when the type is negative.</summary>
    public void Put(int x, int y, int tileID)
    {
        Touch(x, y);
        WorldGen.KillTile(x, y, fail: false, effectOnly: false, noItem: true);
        if (tileID >= 0)
        {
            WorldGen.PlaceTile(x, y, tileID, mute: true, forced: true);
        }

        Reveal(x, y);
    }

    /// <summary>Fill a cell with a liquid.</summary>
    public void Flood(int x, int y, int liquid)
    {
        Touch(x, y);
        Tile tile = Main.tile[x, y];
        tile.LiquidAmount = 255;
        tile.LiquidType = liquid;
        Reveal(x, y);
    }

    /// <summary>Ask for a cell to be a half block or a floor slope, later.</summary>
    // Later because placing a tile runs the game's framing over its neighbours, which
    // squares a shape back off. They go on once every neighbour exists.
    public void WantShape(int x, int y, bool half, bool lowOnLeft) =>
        _shapes.Add((new Point(x, y), half, lowOnLeft));

    /// <summary>Ask for a station to stand here, once the ground exists.</summary>
    // Later for two reasons. A work bench is two tiles wide, so laying the grid left to
    // right places one and then kills its right half on the next cell along, and it needs
    // a floor under it before the game will take it at all.
    public void WantStation(int x, int y, int tileID) =>
        _furniture.Add((new Point(x, y), tileID));

    /// <summary>Stand the stations that were asked for.</summary>
    public void Stand()
    {
        foreach ((Point at, int tileID) in _furniture)
        {
            WorldGen.PlaceTile(at.X, at.Y, tileID, mute: true, forced: true);
            for (int across = -1; across <= 2; across++)
            {
                _built.Add(new Point(at.X + across, at.Y));
                Reveal(at.X + across, at.Y);
            }
        }

        _furniture.Clear();
    }

    /// <summary>Cut the shapes that were asked for.</summary>
    public void Cut()
    {
        foreach ((Point at, bool half, bool lowLeft) in _shapes)
        {
            Tile tile = Main.tile[at.X, at.Y];
            if (half)
            {
                tile.IsHalfBlock = true;
            }
            else
            {
                tile.Slope = Floor(lowLeft);
            }

            Reveal(at.X, at.Y);
        }

        _shapes.Clear();
    }

    /// <summary>Show a cell to the map, since air nobody has seen is fog.</summary>
    public void Reveal(int x, int y)
    {
        Touch(x, y);
        Main.Map.Update(x, y, 255);
    }

    /// <summary>Remember a cell, and that the box has to reach it.</summary>
    private void Touch(int x, int y)
    {
        _built.Add(new Point(x, y));
        _box = _box.IsEmpty
            ? new Rectangle(x, y, 1, 1)
            : Rectangle.Union(_box, new Rectangle(x, y, 1, 1));
    }

    /// <summary>Take it all back.</summary>
    public void Clear()
    {
        // The whole box and a margin, not only the cells written. What the agent put down
        // is in here too, and it is exactly what a stale grid is made of.
        //
        // Nothing written means nothing to take back. An empty box sits at the origin, and
        // sweeping a margin around that walks off the corner of the world.
        if (!_box.IsEmpty)
        {
            for (int x = System.Math.Max(1, _box.Left - Spill);
                x < System.Math.Min(Main.maxTilesX - 1, _box.Right + Spill); x++)
            {
                for (int y = System.Math.Max(1, _box.Top - Spill);
                    y < System.Math.Min(Main.maxTilesY - 1, _box.Bottom + Spill); y++)
                {
                    WorldGen.KillTile(x, y, fail: false, effectOnly: false, noItem: true);
                    Main.tile[x, y].LiquidAmount = 0;
                }
            }
        }

        _box = Rectangle.Empty;
        _built.Clear();
        _shapes.Clear();
        _furniture.Clear();
        Main.refreshMap = true;
    }

    /// <summary>Stand the character in a column pair, its middle on the seam.</summary>
    public static void Place(Player player, Point footing)
    {
        player.Teleport(
            new Vector2(
                World.Hitbox.SeamX(footing) - (player.width / 2f),
                (footing.Y * 16f) - player.height),
            Style: 0,
            extraInfo: 0);
        player.velocity = Vector2.Zero;
    }

    /// <summary>A slope with the rock underfoot rather than overhead.</summary>
    // Found by asking rather than by writing down which of the four it is. Two are floors
    // and two hang from the ceiling, and getting the pair the wrong way round builds
    // something that looks like a hillside and behaves like a cave roof.
    private static SlopeType Floor(bool lowOnLeft)
    {
        if (Slopes.TryGetValue(lowOnLeft, out SlopeType known))
        {
            return known;
        }

        Tile scratch = Main.tile[0, 0];
        SlopeType was = scratch.Slope;
        SlopeType found = SlopeType.SlopeDownLeft;
        foreach (SlopeType candidate in new[]
        {
            SlopeType.SlopeDownLeft, SlopeType.SlopeDownRight,
            SlopeType.SlopeUpLeft, SlopeType.SlopeUpRight,
        })
        {
            scratch.Slope = candidate;
            if (scratch.TopSlope && (lowOnLeft ? scratch.LeftSlope : scratch.RightSlope))
            {
                found = candidate;
                break;
            }
        }

        scratch.Slope = was;
        Slopes[lowOnLeft] = found;
        return found;
    }
}
#endif
