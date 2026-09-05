#if TESTING
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terragent;

/// <summary>
/// Somewhere to run a test: tiles written into the sky and taken back afterwards.
/// </summary>
// Shared because writing into somebody's world is the part with the sharp edges, and
// they were all found the hard way. An unrevealed cell is fog rather than air. Placing
// a tile re-frames its neighbours, so shapes go on afterwards. Everything written has
// to be remembered so it can be undone.
internal sealed class Pen
{
    // Above the surface, so a test never lands on terrain, and clamped below the space
    // layer, where gravity changes and the character's jump stops meaning what it does
    // everywhere else.
    private const int Altitude = 220;

    private readonly HashSet<Point> _built = [];

    private readonly List<(Point At, bool Half, bool LowLeft)> _shapes = [];

    private readonly List<(Point At, int TileID)> _furniture = [];

    private static Dictionary<bool, SlopeType> _floorSlope = [];

    /// <summary>Every cell this pen has written, for scoping and for teardown.</summary>
    public IReadOnlyCollection<Point> Built => _built;

    /// <summary>The row a pen of this height should start at.</summary>
    public static int Top(int rows, int margin) =>
        System.Math.Max((int)(Main.worldSurface * 0.35) + margin + Body.Height + 4,
            (int)Main.worldSurface - Altitude);

    /// <summary>Put a tile down, or clear the cell when the type is negative.</summary>
    public void Put(int x, int y, int tileID)
    {
        _built.Add(new Point(x, y));
        WorldGen.KillTile(x, y, fail: false, effectOnly: false, noItem: true);
        if (tileID >= 0)
        {
            WorldGen.PlaceTile(x, y, tileID, mute: true, forced: true);
        }

        Reveal(x, y);
    }

    /// <summary>Fill a cell with a liquid.</summary>
    public void Flood(int x, int y, int liquid = LiquidID.Water)
    {
        Tile tile = Main.tile[x, y];
        tile.LiquidAmount = 255;
        tile.LiquidType = liquid;
    }

    /// <summary>Ask for a cell to be a half block or a floor slope, later.</summary>
    // Later because placing a tile runs the game's framing over its neighbours, which
    // squares a shape back off. They go on once every neighbour exists; see Cut.
    public void WantShape(int x, int y, bool half, bool lowOnLeft) =>
        _shapes.Add((new Point(x, y), half, lowOnLeft));

    /// <summary>Ask for a station to stand here, once the ground exists.</summary>
    // Later for two reasons. A work bench is two tiles wide, so laying the grid left to
    // right places it and then kills its right half on the next cell along; and it
    // needs a floor under it before the game will accept it at all.
    public void WantStation(int x, int y, int tileID) =>
        _furniture.Add((new Point(x, y), tileID));

    /// <summary>Stand the stations asked for, and say if any did not take.</summary>
    public void Stand()
    {
        foreach ((Point at, int tileID) in _furniture)
        {
            WorldGen.PlaceTile(at.X, at.Y, tileID, mute: true, forced: true);
            for (int dx = -1; dx <= 2; dx++)
            {
                _built.Add(new Point(at.X + dx, at.Y));
                Reveal(at.X + dx, at.Y);
            }

            if (AgentPlayer.Local.Seen.TypeAt(at.X, at.Y) != tileID)
            {
                Log.Sample("pen", "a station did not take",
                    ("at", $"({at.X}, {at.Y})"),
                    ("got", $"{AgentPlayer.Local.Seen.TypeAt(at.X, at.Y)}"));
            }
        }

        _furniture.Clear();
    }

    /// <summary>Cut the shapes asked for, and say if any did not take.</summary>
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
                tile.Slope = FloorSlope(lowLeft);
            }

            Reveal(at.X, at.Y);
            if (!AgentPlayer.Local.Seen.Underfoot(at.X, at.Y))
            {
                Log.Sample("pen", "a shaped tile did not take",
                    ("at", $"({at.X}, {at.Y})"), ("got", $"{tile.BlockType}"));
            }
        }

        _shapes.Clear();
    }

    /// <summary>Show a cell to the belief. Air the map has not seen is fog.</summary>
    public void Reveal(int x, int y) => Main.Map.Update(x, y, 255);

    /// <summary>Take it all back.</summary>
    public void Clear()
    {
        foreach (Point cell in _built)
        {
            WorldGen.KillTile(cell.X, cell.Y, fail: false, effectOnly: false,
                noItem: true);
            Main.tile[cell.X, cell.Y].LiquidAmount = 0;
        }

        _built.Clear();
        _shapes.Clear();
        _furniture.Clear();
        Main.refreshMap = true;
    }

    /// <summary>Stand the character in a column pair, middle on the seam.</summary>
    public static void Place(Player player, Point footing)
    {
        player.Teleport(new Vector2(Body.SeamX(footing) - (player.width / 2f),
            (footing.Y * 16f) - player.height), 0, 0);
        player.velocity = Vector2.Zero;
    }

    /// <summary>A slope with the rock underfoot rather than overhead.</summary>
    // Found by asking rather than by writing down which of the four it is: two are
    // floors and two are wedges hanging from the ceiling, and getting the pair the
    // wrong way round builds something that looks like a hillside and behaves like a
    // cave roof.
    private static SlopeType FloorSlope(bool lowOnLeft)
    {
        if (_floorSlope.TryGetValue(lowOnLeft, out SlopeType known))
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
        _floorSlope[lowOnLeft] = found;
        return found;
    }
}
#endif
