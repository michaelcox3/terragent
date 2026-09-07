using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Report;
using Terragent.Work.Jobs;
using Terragent.World;

namespace Terragent.Work.Objectives;

/// <summary>Having killed something, which is what gates the run.</summary>
// Met is the game's own downed flag rather than anything the agent watched happen. A boss
// killed by falling in lava is a boss killed, and a run that only counted its own swings
// would fight it twice.
//
// The flag is monotone: once true it stays true for the world, which is exactly what the
// progression wants and why a boss node latches like any other.
internal sealed class Defeat(
    string key,
    string label,
    ICreatures creatures,
    IInventory bag,
    IHand hand,
    IJournal journal,
    IReadOnlyList<int> types,
    Func<bool> downed) : IObjective
{
    private readonly ICreatures _creatures = creatures;

    public string Key => key;

    public string Label => label;

    public bool Met => downed();

    /// <summary>The fight, when the thing is about, and nothing when it is not.</summary>
    // No summoning yet. The eye comes at night on its own often enough to be worth waiting
    // for, and a run that could craft its own summon would still need the fight first.
    // Nothing to gather. It wants something dead, not something had.
    public IReadOnlyDictionary<int, int> Missing() => new Dictionary<int, int>();

    public IReadOnlyList<IJob> Jobs() => [new Fight(_creatures, bag, hand, journal, types)];
}
