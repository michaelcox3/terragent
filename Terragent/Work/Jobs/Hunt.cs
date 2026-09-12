using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.World;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>Being where a kind of creature turns up, until one does.</summary>
// Looking for a creature is not looking for a place, which is why this is not exploring.
// Revealing map does nothing to make a slime appear: Terraria rolls spawns near the player
// as it plays, so the way to find one is to be in the band it spawns in and keep moving.
//
// Where that band is comes out of the Bestiary rather than a table of ours, and it is a
// depth rather than a biome. A run can decide to dig to the caverns. It cannot decide that
// its world put a jungle within reach.
internal sealed class Hunt(
    ITerrain terrain,
    ICreatures creatures,
    IClock clock,
    IBody body,
    IReadOnlyList<int> types,
    string looking) : IJob
{
    private readonly ITerrain _terrain = terrain;
    private readonly ICreatures _creatures = creatures;
    private readonly IClock _clock = clock;
    private readonly IBody _body = body;

    /// <summary>How far to pace when already in the right band, in tiles.</summary>
    // Far enough that the spawner keeps rolling new ground and near enough that one search
    // plans the walk. Standing still is the one thing that never works: spawns are rolled off
    // screen and walk in, and a body that does not move sees the same empty screen.
    private const int Pace = 60;

    // Only somewhere to be, so there is nothing to be the same attempt about.
    public string Label => $"Hunting for {looking}";

    /// <summary>Two hunts for the same kinds of creature are one piece of work.</summary>
    // By the label, which is made of the same thing the work is: the item, or the kinds.
    public bool Equals(IJob? other) => other is Hunt same && same.Label == Label;

    public override bool Equals(object? other) => Equals(other as IJob);

    public override int GetHashCode() => System.HashCode.Combine(nameof(Hunt), Label);

    /// <summary>Done the moment one of them is in view.</summary>
    public bool Done => _creatures.Nearest(_body.Footing, types) is not null;

    // A wander has nowhere that can stop being work.
    public bool Workable(ITarget target) => true;

    public Offer? Nearest(Point from)
    {
        if (SpawnZones.Nearest(types, from.Y, _clock.Day) is not { } band)
        {
            return null;
        }

        // Already in the band, so the answer is to keep walking rather than to travel.
        // Which way alternates with where the body happens to be, so a run that reaches
        // one end comes back rather than pressing into the wall.
        // In the band already, so the answer is to keep walking along it. The ground is
        // known and the walk is sideways, so it goes in one piece.
        if (Layers.At(from.Y) == band)
        {
            Point wanted = new(from.X + (Pace * Way(from)), from.Y);
            Point ground = _terrain.Under(wanted);
            return _terrain.Standable(ground)
                ? new Offer(new TileTarget(ground), new Destination(ground, Destination.Slack))
                : null;
        }

        // Getting to it, which is a dig. The whole way down in one destination, because
        // how far a search may plan at a time is the search's own business: it runs to its
        // budget and hands back the way to the nearest footing it could stand on, and the
        // follower walks that and asks again.
        Point entry = new(from.X, Layers.EntryRow(band));
        Destination to = new(entry, Destination.Slack);
        return to.Reached(from) ? null : new Offer(new TileTarget(entry), to);
    }

    // Nothing to do on arrival. Being there is the work, and the game does the rest.
    public void Work(ITarget target)
    {
    }

    /// <summary>Which way to pace, decided by where the body is in the world.</summary>
    // Not remembered, because the job is rebuilt every time it is offered. Halving the
    // world and walking away from the nearer edge gives the same answer every tick from
    // the same place, which is what stops it turning round on the spot.
    private static int Way(Point from) => from.X < Terraria.Main.maxTilesX / 2 ? 1 : -1;
}
