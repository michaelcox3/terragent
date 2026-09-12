using System.Collections.Generic;
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

    /// <summary>How far out to look for the edge of the map, in tiles.</summary>
    private const int Far = 100;

    /// <summary>How far past a footing to check for the dark.</summary>
    private const int Look = 2;

    /// <summary>How far above and below the body to sweep at each distance.</summary>
    // A hill of forty tiles is an ordinary hill, and the ground a hundred tiles away is
    // rarely at the height you are standing at.
    private const int Climb = 40;

    /// <summary>The two ways out of anywhere.</summary>
    private static readonly int[] Sideways = [-1, 1];

    /// <summary>Rows to try either side of the body's own, nearest first.</summary>
    private static readonly int[] Rows = Spread();

    private static int[] Spread()
    {
        List<int> rows = [0];
        for (int away = 1; away <= Climb; away++)
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
            Offer? shaft = Aim(from, new Point(from.X, Layers.EntryRow(layer)));
            if (shaft is not null)
            {
                return shaft;
            }
        }

        // Nearest first, and that ordering is the whole of the job's judgement. Furthest
        // first is a rule that undoes itself: walking toward the far edge makes it the near
        // one, so the edge behind becomes furthest and the body turns round. A run watched
        // doing this paced between two frontiers twenty tiles apart until it was killed.
        //
        // Nearest survives the body moving toward it, because moving toward the nearest
        // thing leaves it the nearest thing. It also ends: arriving lights the dark behind
        // it, that ground stops being a frontier, and the next one out is what gets picked.
        // Every trip spends some of the dark, and there is only so much of it.
        for (int ring = 1; ring <= Far; ring++)
        {
            foreach (int way in Sideways)
            {
                // A column at that distance, not the one tile level with the body. The
                // edge of a revealed map is roughly upright, so a sweep across it crosses
                // it, and a sweep along the body's own row only does on flat ground.
                //
                // Outward from the body's own row rather than down from the top of the
                // sweep. Top down takes the shallowest frontier of the eighty it looks at,
                // which is the one furthest from where the body is standing and the most
                // rows of climbing to reach.
                foreach (int down in Rows)
                {
                    Point at = new(from.X + (ring * way), from.Y + down);
                    if (!Frontier(at, way) || !Inside(at))
                    {
                        continue;
                    }

                    // Past the edge and into the dark, not the last lit tile before it.
                    // Stopping on the near side leaves the cells beyond unseen, so the same
                    // ground is still a frontier on the next tick and the run stands there
                    // picking it again.
                    Point dark = new(at.X + (Look * way), at.Y);
                    Offer? beyond = Aim(from, dark);
                    if (beyond is not null)
                    {
                        return beyond;
                    }
                }
            }
        }

        // Nothing standable borders the dark, which is every body enclosed in rock: the
        // edge of what is revealed runs through solid ground, where there is nowhere to
        // stand, so the sweep above matches nothing anywhere and exploring stops offering
        // at exactly the moment it is the only thing left.
        //
        // So aim at the dark itself. A point in rock is not a place to walk to, it is a
        // place to dig to, and the search prices its own digging: it tunnels as far as its
        // budget reaches and arriving asks for the next stretch.
        return Digging(from);
    }

    /// <summary>Somewhere to head toward the nearest unseen cell, standable or not.</summary>
    // Nearest, as the sweep above is, and for the same reason: it is the only ordering that
    // survives the body walking toward its own answer. The difference is that this one will
    // take ground nobody can stand on, so it cuts through a wall where the sweep looks for
    // a way round one.
    private Offer? Digging(Point from)
    {
        for (int ring = 1; ring <= Far; ring++)
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
                    if (_terrain.IsKnown(at.X, at.Y) || !Inside(at))
                    {
                        continue;
                    }

                    Offer? toward = Aim(from, at);
                    if (toward is not null)
                    {
                        return toward;
                    }
                }
            }
        }

        return null;
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
    private static Offer? Aim(Point from, Point site)
    {
        Destination to = new(site, Destination.Slack);
        return to.Reached(from) ? null : new Offer(new TileTarget(site), to);
    }

    /// <summary>Whether somewhere is in the band this job was sent to, if it has one.</summary>
    // The frontier sweep knew nothing about the band and the band rule knew nothing about
    // the frontier, so the two of them took turns. Standing below the surface line the
    // sweep would pick a frontier above it, the body would climb to it, and arriving there
    // the band rule would find itself out of the band and dig a fresh shaft back down. Mine
    // down, pillar up, mine down somewhere else, for as long as it was left running.
    private bool Inside(Point at) =>
        band is not { } layer || Layers.At(at.Y) == layer;

    /// <summary>Whether this is ground to stand on with something unseen beyond it.</summary>
    // Beyond in the direction being walked, not in any direction. Every cave wall has the
    // unknown behind it, and standing next to one reveals nothing.
    private bool Frontier(Point at, int way) =>
        _terrain.Standable(at) && !_terrain.IsKnown(at.X + (Look * way), at.Y);

}
