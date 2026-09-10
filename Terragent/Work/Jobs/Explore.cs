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

    /// <summary>How near counts as having got there.</summary>
    // Loose, because the point is to be over there rather than on that tile, and the last
    // few tiles of a walk into the dark reveal as much as the destination does.
    private const int Roughly = 3;

    /// <summary>How far to go at once on the way to a band, in tiles.</summary>
    // Short, because the ground between here and the caverns is solid and a search that
    // has to cut all of it in one route runs out of budget having dug nowhere.
    private const int Leg = 20;

    /// <summary>Footings a leg may look at before giving up on it.</summary>
    // Generous, not thrifty. The leg already bounds how far the body goes; the budget
    // bounds how hard the search may look, and digging is where it has to look hardest.
    // The estimate prices what is left as walking, and a dug tile costs ten times a step,
    // so every footing in a revealed cavern looks cheaper than the first tile of a shaft
    // and the search works through all of them before it starts cutting.
    private const int Reach = 20000;

    /// <summary>How far past a footing to check for the dark.</summary>
    private const int Look = 2;

    /// <summary>How far above and below the body to sweep at each distance.</summary>
    // A hill of forty tiles is an ordinary hill, and the ground a hundred tiles away is
    // rarely at the height you are standing at.
    private const int Climb = 40;

    /// <summary>The two ways out of anywhere.</summary>
    private static readonly int[] Sideways = [-1, 1];

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

    /// <summary>The furthest known ground that borders somewhere unseen.</summary>
    // Furthest rather than nearest, and this is the whole of the job's judgement. The
    // nearest unseen cell is usually the far side of the wall you are standing against,
    // and walking to it reveals nothing. The far edge of what is known is where the map
    // actually grows.
    // Arriving is the work, so there is nothing here that can stop being work.
    public bool Workable(ITarget target) => true;

    public Offer? Nearest(Point from)
    {
        // The band first, when what is wanted has one and the body is not in it. A
        // frontier is ground with something unseen to one side, so the edge of a revealed
        // strip runs sideways and walking it turns up more of the same row. Everything
        // below is unknown and unknown below is never a frontier, which is how a run after
        // iron paces the surface until the sun goes down.
        if (band is { } layer && Layers.At(from.Y) != layer)
        {
            // The body's own column, so the way down is a shaft rather than a journey, and
            // one leg of it at a time. The search prices its own digging and will cut it.
            Point down = new(from.X, Layers.EntryRow(layer));
            Point leg = Destination.Toward(from, down, Leg);
            return new Offer(
                new TileTarget(leg),
                new Destination(leg, Roughly, Budget: Reach));
        }

        for (int ring = Far; ring > 0; ring--)
        {
            foreach (int way in Sideways)
            {
                // A column at that distance, not the one tile level with the body. The
                // edge of a revealed map is roughly upright, so a sweep across it crosses
                // it, and a sweep along the body's own row only does on flat ground.
                for (int down = -Climb; down <= Climb; down++)
                {
                    Point at = new(from.X + (ring * way), from.Y + down);
                    if (Frontier(at, way))
                    {
                        // The furthest frontier says which way to head, not how far to go
                        // in one search. Underground that is a hundred tiles of solid rock
                        // and the search comes back with nothing; walked in legs, each one
                        // is short and arriving asks for the next.
                        Point leg = Destination.Toward(from, at, Leg);
                        return new Offer(
                            new TileTarget(leg),
                            new Destination(leg, Roughly, Budget: Reach));
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
        // place to dig to, and the search prices its own digging: a leg of that is a short
        // tunnel, and arriving asks for the next one.
        return Digging(from);
    }

    /// <summary>A leg toward the nearest cell nobody has seen, standable or not.</summary>
    // Nearest rather than furthest, which is the opposite of the sweep above and right for
    // the opposite reason. Walking to the near side of a wall reveals nothing, so that
    // sweep looks past it; cutting through the wall reveals everything behind it, so this
    // one heads straight at it.
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
                    if (_terrain.IsKnown(at.X, at.Y))
                    {
                        continue;
                    }

                    Point leg = Destination.Toward(from, at, Leg);
                    return new Offer(
                        new TileTarget(leg),
                        new Destination(leg, Roughly, Budget: Reach));
                }
            }
        }

        return null;
    }

    // Nothing to do on arrival. Walking there is what reveals the map.
    public void Work(ITarget target)
    {
    }

    /// <summary>Whether this is ground to stand on with something unseen beyond it.</summary>
    // Beyond in the direction being walked, not in any direction. Every cave wall has the
    // unknown behind it, and standing next to one reveals nothing.
    private bool Frontier(Point at, int way) =>
        _terrain.Standable(at) && !_terrain.IsKnown(at.X + (Look * way), at.Y);

}
