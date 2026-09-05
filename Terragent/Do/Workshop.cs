using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Do;

/// <summary>
/// The stations the agent is carrying about with it, put down to craft and taken back.
/// </summary>
// A work bench is ten wood and an anvil five iron bars, too much to abandon every time
// the run moves on, and a workshop left at spawn is a walk back for every recipe. So
// the agent carries its stations, and the only ones on the ground are the ones in use.
//
// All of them, not the last: smelting bars and then making an anvil puts a furnace and
// a bench down side by side, and remembering one forgets the other. No shortcut for
// "one is in the bag" either, since a spare says nothing about the one on the ground.
internal sealed class Workshop
{
    /// <summary>Frames a station may go unwanted before it is picked up again.</summary>
    // Half a second: enough that finishing one recipe and starting another that wants
    // the same station does not put it down twice, and short enough that the agent
    // takes it back before it has walked anywhere.
    internal const int Grace = 30;

    /// <summary>One station on the ground: what it is, where, and how long unwanted.</summary>
    internal sealed record Station(int ItemID, int TileID, Point At)
    {
        public int Idle { get; set; }
    }

    private readonly List<Station> _down = [];

    /// <summary>What is on the ground, for the panel.</summary>
    public IReadOnlyList<Station> Down => _down;

    /// <summary>Remember a station just placed, so it can be collected afterwards.</summary>
    public void PutDown(int itemID, int tileID, int x, int y)
    {
        _down.RemoveAll(station => station.At == new Point(x, y));
        _down.Add(new Station(itemID, tileID, new Point(x, y)));
    }

    /// <summary>Say that something still needs this kind of station this tick.</summary>
    public void Wanted(int tileID)
    {
        foreach (Station station in _down)
        {
            if (station.TileID == tileID)
            {
                station.Idle = 0;
            }
        }
    }

    /// <summary>
    /// The first station nothing has wanted for a while, or null when all are in use.
    /// </summary>
    // Deciding only. Walking to it, breaking it and picking it up is the executor's,
    // through the same search as any other job. Aiming and swinging here mines the
    // bench from the far edge of reach, dropping the item a tile out of the body's
    // way, and the job then counts as done because the tile went away.
    public Station? Due()
    {
        Station? owed = null;
        foreach (Station station in _down)
        {
            station.Idle++;
            if (owed is null && station.Idle >= Grace)
            {
                owed = station;
            }
        }

        return owed;
    }

    /// <summary>It is back in the bag, or gone for good: stop owing it.</summary>
    public void Taken(Station station) => _down.Remove(station);

    /// <summary>Give up on this one. It stays where it is and Stations remembers it.</summary>
    public void Leave(Station station) => _down.Remove(station);
}
