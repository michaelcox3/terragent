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

    /// <summary>How far past a footing to check for the dark.</summary>
    private const int Look = 2;

    /// <summary>How far above and below the body to sweep at each distance.</summary>
    // A hill of forty tiles is an ordinary hill, and the ground a hundred tiles away is
    // rarely at the height you are standing at.
    private const int Climb = 40;

    /// <summary>The two ways out of anywhere.</summary>
    private static readonly int[] Sideways = [-1, 1];

    // Only somewhere to be, so there is nothing to be the same attempt about.
    public string Label => $"Exploring for {looking}";

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
                        return new Offer(new TileTarget(at),
                            new Destination(at, Roughly));
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

    /// <summary>Whether this is ground to stand on with something unseen beyond it.</summary>
    // Beyond in the direction being walked, not in any direction. Every cave wall has the
    // unknown behind it, and standing next to one reveals nothing.
    private bool Frontier(Point at, int way) =>
        _terrain.Standable(at) && !_terrain.IsKnown(at.X + (Look * way), at.Y);

}
