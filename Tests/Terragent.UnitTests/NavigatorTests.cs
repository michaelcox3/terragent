using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Terragent.UnitTests;

/// <summary>
/// Every pathfinding case in <see cref="Scenarios"/>, run against the search headless.
/// </summary>
public class NavigatorTests
{
    /// <summary>Copper: 45 ticks a tile against 4 to walk one. Digging should hurt.</summary>
    private const float Walk = 4f;

    private const float Mine = 45f;

    private const int PickPower = 35;

    /// <summary>Ticks to place a block. How many are carried is per scenario.</summary>
    private const float Place = 30f;

    /// <summary>
    /// What the base character's jump reaches, from the game's own constants: fifteen
    /// powered frames at 5.01, gravity 0.4, run acceleration 0.08 up to 3.
    /// </summary>
    private static readonly Leap Jump = Leap.Of(15, 5.01f, 0.4f, 0.08f, 3f);

    /// <summary>What a scenario's moves cost. No scenario carries a glowstick.</summary>
    private static readonly Costs Prices = new(Walk, Mine, Place, 10f, 1.5f, 1f);

    public static IEnumerable<object[]> CaseNames => Scenarios.All.Select(test => new object[] { test.Name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ScenarioGetsExactlyTheRightRoute(string name)
    {
        Case test = Scenarios.All.First(candidate => candidate.Name == name);
        Grid grid = new(test.Border, test.Rows);
        Point from = Standing(grid, grid.Find('@'));

        // The goal stays a tile. A footing covers two columns and arriving means the
        // body is over the marked one, which Navigator.Reached decides, so the goal is
        // not a footing and must not be turned into one.
        Point to = Floor(grid, grid.Find(test.Goal));

        List<Step>? route = new Navigator(grid).FindRoute(Prices, PickPower, Jump, from, to,
            blocks: test.Blocks)?.Steps as List<Step>;
        string? complaint = Judge(test, grid, route);

        if (complaint is null)
        {
            return;
        }

        string picture = string.Join("\n", grid.Draw(route).Split('\n').Select(line => "      " + line));
        Assert.Fail(
            $"{complaint}\n"
            + $"      expects exactly [{test.Does}]: {test.Why}\n"
            + $"      from ({from.X},{from.Y}) to ({to.X},{to.Y})\n\n{picture}");
    }

    /// <summary>
    /// Picking up a drop, asked without a game: Terraria's overlap test, on footings.
    /// </summary>
    // From a fresh run on 2026-09-03: twelve lead ore lying in a one-wide, one-deep hole
    // at the bottom of the agent's own shaft. Judged by the tile the ore's centre was in,
    // the footing over the hole counted as arrived, and the agent stood on it for the
    // rest of the run. Terraria picks an item up when the body's box overlaps the item's,
    // and lands a falling item on the first ground under any part of its box.
    [Fact]
    public void RouteToOreLyingInAHoleDigsDownToIt()
    {
        Grid grid = new(true,
            "##..##",
            "##..##",
            "##.@##",
            "##.###",
            "######");
        Point over = Standing(grid, grid.Find('@'));

        // Twelve pixels of ore, lying at the bottom of the hole in column 2.
        Rectangle ore = new(34, 52, 12, 12);

        Assert.False(Body.Touches(over, ore),
            "the footing over the hole should not touch ore lying in it");
        Assert.True(Body.Touches(new Point(2, 4), ore),
            "a footing one row down, the hole widened, should touch it");
        Assert.True(Body.Touches(over, new Rectangle(50, 36, 12, 12)),
            "ore on the floor beside the feet should be touched");
        Assert.False(Body.Touches(over, new Rectangle(34, -8, 12, 12)),
            "ore a row above the head should not be touched");

        Assert.Equal(48, grid.Landing(new Rectangle(42, 0, 12, 12)).Bottom);
        Assert.Equal(64, grid.Landing(new Rectangle(34, 0, 12, 12)).Bottom);
        Assert.Equal(new Rectangle(50, 36, 12, 12), grid.Landing(new Rectangle(50, 36, 12, 12)));

        Route? route = new Navigator(grid).FindRoute(Prices, PickPower, Jump,
            over, new Point(2, 3), arrived: node => Body.Touches(node, ore));
        Assert.NotNull(route);
        Assert.True(route.Steps.Count > 0,
            "the search should not call the footing over the hole arrived");
        Assert.True(
            route.Steps.Any(step => step.Removes.Contains(new Point(3, 3)))
            && route.Steps[^1].To == new Point(2, 4),
            "the route should dig the floor beside the hole and drop a row");
    }

    /// <summary>
    /// The floor tile a marker stands on. Markers sit in the air, on ground.
    /// </summary>
    /// <remarks>
    /// Strict on purpose. Walking downward to the first solid tile turns a marker drawn
    /// in mid-air into a start several rows lower, and two of the grids here were wrong
    /// that way. A picture that lies about where the character is, in a suite whose
    /// entire subject is what fits where, is worse than no picture.
    /// </remarks>
    private static Point Floor(Grid grid, Point marker)
    {
        Point floor = new(marker.X, marker.Y + 1);
        if (grid.KindAt(floor.X, floor.Y)
            is not (TileKind.Solid or TileKind.Platform or TileKind.Slab))
        {
            throw new System.ArgumentException(
                $"marker at ({marker.X},{marker.Y}) is floating: the tile below it is "
                + $"{grid.KindAt(floor.X, floor.Y)}, and markers stand on ground");
        }

        return floor;
    }

    /// <summary>
    /// The footing a start marker describes: which two columns the body is in.
    /// </summary>
    /// <remarks>
    /// The marked column, and whichever neighbour has room. Scenarios are drawn with
    /// the character's own column marked, which does not say which way it straddles, and
    /// it always straddles: 20 pixels of body will not fit inside a 16 pixel tile at any
    /// alignment. Choosing the side with room is what the arena does when it puts the
    /// character down, so both levels start from the same body.
    /// </remarks>
    private static Point Standing(Grid grid, Point marker)
    {
        Point floor = Floor(grid, marker);
        foreach (int left in new[] { floor.X, floor.X - 1 })
        {
            Point footing = new(left, floor.Y);
            bool room = grid.Standable(footing);
            foreach (Point cell in Body.Cells(footing))
            {
                room &= grid.Passable(cell.X, cell.Y);
            }

            if (room)
            {
                return footing;
            }
        }

        // Neither side is clear, which does not mean the character is not there. The
        // fog scenarios start with unseen tiles beside the marker, and unseen is not
        // occupied: the search skips the cells the body already fills rather than
        // charging to break them, so a start that overlaps fog costs nothing.
        return floor;
    }

    private static string? Judge(Case test, Grid grid, List<Step>? route)
    {
        if (test.Unreachable)
        {
            return route is null ? null : $"found a route of {route.Count} steps";
        }

        if (route is null)
        {
            return "no route found";
        }

        if (route.Count > test.MaxSteps)
        {
            return $"{route.Count} steps, wanted at most {test.MaxSteps}";
        }

        Move used = Move.None;
        foreach (Step step in route)
        {
            used |= Spending(step);
        }

        // Mining and building exactly; walking and jumping only as permissions. See the
        // note on Case: spending is the contract, free movement is the search's
        // business, and demanding agreement on the latter fails scenarios for jumps
        // that are correct.
        const Move Spent = Move.Mine | Move.Build;
        Move extra = (used & ~test.Does) & (Spent | Move.Walk | Move.Jump);
        extra &= Spent | (~test.Does & (Move.Walk | Move.Jump));
        Move missing = (test.Does & ~used) & Spent;
        if (extra != Move.None || missing != Move.None)
        {
            List<string> complaints = [];
            if (missing != Move.None)
            {
                complaints.Add($"never {Doing(missing)}");
            }

            if (extra != Move.None)
            {
                complaints.Add($"{Doing(extra)} and should not");
            }

            return "the route " + string.Join(", and ", complaints);
        }

        return Excavation(grid, route);
    }

    /// <summary>What one step spends, in the terms a scenario is written in.</summary>
    /// <remarks>
    /// <b>Walking means moving without paying for it.</b> A kind now says only how the
    /// character propels itself, so every horizontal move is a walk, including one that
    /// tunnels the whole way. That would make "walks" true of a route that never takes a
    /// free step, turning the flag into a synonym for "is not entirely vertical". A step
    /// that breaks something is priced as mining, and the scenarios keep meaning what
    /// they meant.
    /// </remarks>
    private static Move Spending(Step step)
    {
        Move spent = Move.None;
        if (step.Removes.Count > 0)
        {
            spent |= Move.Mine;
        }

        if (step.Puts is not null)
        {
            spent |= Move.Build;
        }

        if (step.Kind is StepKind.Jump)
        {
            spent |= Move.Jump;
        }
        else if (step.Removes.Count == 0 && step.Puts is null)
        {
            spent |= Move.Walk;
        }

        return spent;
    }

    private static string Doing(Move moves)
    {
        List<string> parts = [];
        if (moves.HasFlag(Move.Walk))
        {
            parts.Add("walks");
        }

        if (moves.HasFlag(Move.Jump))
        {
            parts.Add("jumps");
        }

        if (moves.HasFlag(Move.Mine))
        {
            parts.Add("mines");
        }

        if (moves.HasFlag(Move.Build))
        {
            parts.Add("builds");
        }

        return string.Join(" and ", parts);
    }

    /// <summary>Whether the route breaks the right blocks.</summary>
    /// <remarks>
    /// <b>A route is a plan to excavate, not only a plan to walk.</b> Checking that a
    /// route mines <i>somewhere</i> would have passed every version of the shaft bug:
    /// cutting one column of a two column shaft is mining, and produces a hole the
    /// character cannot enter. What matters is which cells, and the route records them,
    /// so the cells are what the tests read.
    /// <para>
    /// Two invariants hold for every case, and then a case may name cells with 'd' that
    /// it specifically requires. The invariants are the ones worth having: a break
    /// recorded against empty air is a plan paying for nothing, and a break the pickaxe
    /// cannot make is a plan that ends in an agent mining forever.
    /// </para>
    /// </remarks>
    private static string? Excavation(Grid grid, List<Step> route)
    {
        foreach (Step step in route)
        {
            foreach (Point cell in step.Removes)
            {
                if (grid.KindAt(cell.X, cell.Y) is TileKind.Empty)
                {
                    return $"the route pays to break ({cell.X}, {cell.Y}), which is air";
                }

                // Holds for every case, which is worth more than any grid built to
                // catch it. A tree standing on a block, or Ebonstone against a copper
                // pick, is refused by the game in silence: the swing animates, the tool
                // timer runs, and nothing happens. A plan containing such a block is a
                // plan that ends in an agent mining forever.
                if (!grid.Diggable(cell.X, cell.Y, PickPower))
                {
                    return $"the route plans to break ({cell.X}, {cell.Y}), which "
                           + "this pickaxe cannot break";
                }
            }
        }

        HashSet<Point> cut = Grid.Excavation(route);
        foreach (Point wanted in grid.Required())
        {
            if (!cut.Contains(wanted))
            {
                return $"({wanted.X}, {wanted.Y}) is marked d but the route never breaks it";
            }
        }

        // Every block placed must have something to hold on to. Terraria refuses a
        // placement with no orthogonal neighbour and says nothing about it, so a route
        // containing one is a route that ends with the character jumping and pressing
        // at thin air. Walked forward through the route, because the second block of a
        // tower anchors on the first.
        HashSet<Point> standing = [];
        foreach (Step step in route)
        {
            if (step.Puts is not { } cell)
            {
                continue;
            }

            bool anchored = false;
            foreach (Point side in new[]
                     {
                         new Point(cell.X - 1, cell.Y), new Point(cell.X + 1, cell.Y),
                         new Point(cell.X, cell.Y - 1), new Point(cell.X, cell.Y + 1),
                     })
            {
                // A slope or half block is rock too, and the game anchors on it.
                anchored |= standing.Contains(side)
                    || grid.KindAt(side.X, side.Y) is TileKind.Solid or TileKind.Platform or TileKind.Slab;
            }

            if (!anchored)
            {
                return $"the route places a block at ({cell.X}, {cell.Y}) with nothing "
                       + "beside or beneath it to anchor to";
            }

            standing.Add(cell);
        }

        HashSet<Point> put = Grid.Pillars(route);
        foreach (Point wanted in grid.RequiredPlacements())
        {
            if (!put.Contains(wanted))
            {
                return $"({wanted.X}, {wanted.Y}) is marked p but the route never "
                       + "places a block there";
            }
        }

        return null;
    }
}
