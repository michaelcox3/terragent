using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.World;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>Walking to the edge of what has been seen.</summary>
// The job that exists because every other job needs a site and a fresh world has none. In
// a world nobody has walked, the nearest tree is not far away, it is unknown, and no
// amount of looking at the map finds it.
//
// Its work is arriving, like a pickup. What it produces is map, and it is finished the
// moment the thing the run was actually after comes into view.
internal sealed class Explore(
    ITerrain terrain,
    ISites sites,
    IBody body,
    IReadOnlyList<int> tiles,
    Layer? band,
    string looking) : IJob
{
    private readonly ITerrain _terrain = terrain;
    private readonly ISites _sites = sites;
    private readonly IBody _body = body;

    /// <summary>How many columns out to look for the edge of the map.</summary>
    private const int ColumnsOut = 100;

    /// <summary>How far past a footing the dark has to start for it to be an edge.</summary>
    private const int LookAhead = 2;

    /// <summary>How far above and below the body to sweep at each column.</summary>
    // A hill of forty tiles is an ordinary hill, and the ground a hundred tiles away is
    // rarely at the height you are standing at.
    private const int RowsEitherSide = 40;

    /// <summary>How many edges of the dark are enough to choose between.</summary>
    // A cap and not a budget. The sweep runs outward, so these are the near ones, and the
    // hundred and first is not going to win a ratio the first forty already lost.
    private const int MostEdges = 40;

    /// <summary>The two ways out of anywhere.</summary>
    private static readonly int[] LeftThenRight = [-1, 1];

    /// <summary>Rows to try either side of the body's own, nearest first.</summary>
    private static readonly int[] RowsNearestFirst = NearestRowsFirst();

    private static int[] NearestRowsFirst()
    {
        List<int> rows = [0];
        for (int away = 1; away <= RowsEitherSide; away++)
        {
            rows.Add(away);
            rows.Add(-away);
        }

        return [.. rows];
    }

    // Only somewhere to be, so there is nothing to be the same attempt about.
    /// <summary>The band it walks to, which is all that decides whether two of these are one.</summary>
    // Not what it is looking for. Iron and lead are both underground, and a run short of
    // either walks the same way to find out; telling them apart by the ore in the label
    // made two identical walks that each paid for their own sweep of the frontier.
    public Layer? Band => band;

    public string Label => band is { } layer
        ? $"Exploring the {layer} for {looking}"
        : $"Exploring for {looking}";

    /// <summary>Two walks to the same band are one piece of work.</summary>
    // By the band and not by the label, which is the only job where those differ. A label
    // is made of what a job is about, and this one is about the ores that sent it: two
    // objectives short of iron and of lead word one walk two ways.
    public bool Equals(IJob? other) => other is Explore same && same.Band == Band;

    public override bool Equals(object? other) => Equals(other as IJob);

    public override int GetHashCode() => System.HashCode.Combine(nameof(Explore), Band);

    /// <summary>Done the moment a tile the run is after can be seen.</summary>
    // Tiles only. Revealing map is what makes a tree findable and does nothing whatever to
    // make a slime appear, so hunting is a different job.
    public bool Done => _sites.Nearest(_body.Footing, tiles) is not null;

    // Arriving is the work, so there is nothing here that can stop being work.
    public bool Workable(ITarget target) => true;

    public Offer? Nearest(Point from)
    {
        // The band first, when what is wanted has one and the body is not in it. A
        // frontier is ground with something unseen to one side, so the edge of a revealed
        // strip runs sideways and walking it turns up more of the same row. Everything
        // below is unknown and unknown below is never a frontier, which is how a run after
        // iron paces the surface until the sun goes down.
        //
        // The body's own column, so the way down is a shaft rather than a journey. The
        // whole depth in one destination, since the search prices its own digging and cuts
        // as much of it as its budget allows.
        if (band is { } layer && Layers.At(from.Y) != layer)
        {
            Offer? shaft = OfferUnlessArrived(from,
                new Point(from.X, Layers.EntryRow(layer)));
            if (shaft is not null)
            {
                return shaft;
            }
        }

        Offer? worth = BestEdgeOfTheDark(from);
        if (worth is not null)
        {
            return worth;
        }

        // Nothing standable borders the dark, which is every body enclosed in rock: the
        // edge of what is revealed runs through solid ground, where there is nowhere to
        // stand, so the sweep above matches nothing anywhere and exploring stops offering
        // at exactly the moment it is the only thing left.
        //
        // So aim at the dark itself. A point in rock is not a place to walk to, it is a
        // place to dig to, and the search prices its own digging: it tunnels as far as its
        // budget reaches and arriving asks for the next stretch.
        return OfferNearestUnseenCell(from);
    }

    /// <summary>The edge of the dark that opens the most ground for the least walking.</summary>
    // Nearest used to be the whole of the judgement, and nearest is not a measure of
    // anything: it read as the nearest column and then the nearest row within it, so forty
    // rows straight down at one column across beat two columns across at eye level, and
    // left and down won every tie. A run spent itself cutting a staircase under its own
    // feet, one tile at a time, because the tile one column left is always dark and always
    // the answer.
    //
    // What is worth walking to is how much dark a trip opens against what the trip costs.
    // A single dark tile beside a lit tunnel opens almost nothing however close it is, and
    // a cave mouth twenty tiles along opens a room. The ratio also keeps the body honest
    // about distance without a rule saying so: twice as far has to show twice the dark.
    //
    // Arrived at rather than approached, and with nothing to spare on the arrival. A point
    // in the dark is somewhere the body may only get near, so the job never finishes; and
    // the light travels with the body, so three tiles short of an edge lights nothing past
    // it. Getting there is what spends the dark and makes the next answer a different one.
    private Offer? BestEdgeOfTheDark(Point from)
    {
        List<Point> edges = [];

        // Outward a column at a time, so the ones near the body are found first and the
        // sweep can stop soon after it has enough to choose between. The order no longer
        // decides anything, which is the point: the score does.
        for (int ring = 1; ring <= ColumnsOut && edges.Count < MostEdges; ring++)
        {
            foreach (int way in LeftThenRight)
            {
                // A column at that distance, not the one tile level with the body. The
                // edge of a revealed map is roughly upright, so a sweep across it crosses
                // it, and a sweep along the body's own row only does on flat ground.
                foreach (int down in RowsNearestFirst)
                {
                    Point at = new(from.X + (ring * way), from.Y + down);
                    if (BordersTheDark(at, way) && InTheBand(at))
                    {
                        edges.Add(at);
                    }
                }
            }
        }

        return edges.Count == 0 ? null
            : OfferUnlessArrived(from,
                edges.MaxBy(at =>
                    Prospect.Unseen(_terrain, at) / Prospect.Reaching(_terrain, from, at)),
                within: 0);
    }

    /// <summary>Somewhere to head toward the unseen ground, standable or not.</summary>
    // For a body with rock on every side, where the sweep above matches nothing because
    // nothing it can stand on borders anything. A point in rock is not a place to walk to,
    // it is a place to dig to, and the search prices its own digging: it tunnels as far as
    // its budget reaches and arriving asks for the next stretch.
    //
    // Gathered and weighed rather than answered with the first one seen. Taking the first
    // made the compass the whole of the decision: the rings are walked from the left column
    // downward, everything above a body is ground it has already come through, so the first
    // unseen cell in that walk is the one just under its own row, on its left. Every time,
    // in every world, for no reason anybody chose.
    //
    // Worth against cost as the sweep does it, then the flattest of what ties, since in
    // solid rock every direction is equally dark and the ratio has nothing to say. A run
    // that has to tunnel blind may as well tunnel along the band it was sent to.
    private Offer? OfferNearestUnseenCell(Point from)
    {
        List<Point> dark = [];
        for (int ring = 1; ring <= ColumnsOut && dark.Count < MostEdges; ring++)
        {
            for (int across = -ring; across <= ring; across++)
            {
                for (int down = -ring; down <= ring; down++)
                {
                    // The perimeter only. The inside of the square belongs to a ring
                    // already walked, and looking at it again is the whole box per ring.
                    if (across != -ring && across != ring && down != -ring && down != ring)
                    {
                        down = ring - 1;
                        continue;
                    }

                    Point at = new(from.X + across, from.Y + down);
                    if (!_terrain.IsKnown(at.X, at.Y) && InTheBand(at)
                        && OfferUnlessArrived(from, at) is not null)
                    {
                        dark.Add(at);
                    }
                }
            }
        }

        return dark.Count == 0 ? null
            : OfferUnlessArrived(from, dark
                .OrderByDescending(at =>
                    Prospect.Unseen(_terrain, at) / Prospect.Reaching(_terrain, from, at))
                .ThenBy(at => System.Math.Abs(at.Y - from.Y))
                .First());
    }

    // Nothing to do on arrival. Walking there is what reveals the map.
    public void Work(ITarget target)
    {
    }

    /// <summary>Somewhere to head, or null when the body is already there.</summary>
    // The place itself, however far off, because how far one search may plan is not this
    // job's business. The search runs to its budget and hands back the way to the nearest
    // footing it proved it could stand on, the follower walks that and asks again, and the
    // journey takes as many searches as it takes.
    //
    // Whether it has got there is asked of the destination just built, never of a distance
    // worked out some other way. Arrival counts both of the columns the body straddles, so
    // a point three columns off is outside a radius of three and inside that box: two tests
    // that each look right then answer "worth going" and "already there" on the same tick,
    // and the run stands still choosing the same spot sixty times a second.
    private static Offer? OfferUnlessArrived(Point from, Point site,
        int within = Destination.Slack)
    {
        Destination to = new(site, within);
        return to.Reached(from) ? null : new Offer(new TileTarget(site), to);
    }

    /// <summary>Whether somewhere is in the band this job was sent to, if it has one.</summary>
    // The frontier sweep knew nothing about the band and the band rule knew nothing about
    // the frontier, so the two of them took turns. Standing below the surface line the
    // sweep would pick a frontier above it, the body would climb to it, and arriving there
    // the band rule would find itself out of the band and dig a fresh shaft back down. Mine
    // down, pillar up, mine down somewhere else, for as long as it was left running.
    private bool InTheBand(Point at) =>
        band is not { } layer || Layers.At(at.Y) == layer;

    /// <summary>Whether this is ground to stand on with something unseen beyond it.</summary>
    // Beyond in the direction being walked, not in any direction. Every cave wall has the
    // unknown behind it, and standing next to one reveals nothing.
    private bool BordersTheDark(Point at, int way) =>
        _terrain.Standable(at) && !_terrain.IsKnown(at.X + (LookAhead * way), at.Y);

}
