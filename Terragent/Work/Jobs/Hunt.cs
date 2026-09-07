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
    // Far enough that the spawner keeps rolling new ground and near enough that a leg of it
    // is one route. Standing still is the one thing that never works: spawns are rolled off
    // screen and walk in, and a body that does not move sees the same empty screen.
    private const int Pace = 60;

    /// <summary>How near counts as having got there.</summary>
    private const int Roughly = 3;

    // Only somewhere to be, so there is nothing to be the same attempt about.
    public string Label => $"Hunting for {looking}";

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
        int row = Layers.At(from.Y) == band ? from.Y : Layers.EntryRow(band);
        int across = Layers.At(from.Y) == band ? Pace * Way(from) : 0;

        Point wanted = new(from.X + across, row);
        Point ground = _terrain.Under(wanted);
        return _terrain.Standable(ground)
            ? new Offer(new TileTarget(ground), new Destination(ground, Roughly))
            : null;
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
