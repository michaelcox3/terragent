using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Terragent.Work.Jobs;

namespace Terragent.Work.Objectives;

/// <summary>Something the run is trying to make true, and the work that would do it.</summary>
// The run is a list of these: have a pickaxe, have a house, have killed a boss. An
// objective is a statement about the world, not a task, which is why it answers Met by
// looking rather than by remembering what it did.
//
// It breaks itself down. What a pickaxe wants is a recipe walk, what a house wants is a
// shape, what a boss wants is gear, and a single breaker of objectives would be a switch
// over every kind there is.
internal interface IObjective
{
    /// <summary>What to call it on the panel.</summary>
    // Every objective can say what it is, so nothing above has to keep a table of names
    // beside the things it is naming.
    string Label { get; }

    /// <summary>Whether it already holds, in which case there is nothing to do.</summary>
    bool Met { get; }

    /// <summary>What is still missing for it, by item, and how many of each.</summary>
    // What the jobs are made from, shown so the panel can say the run is short sixty three
    // wood rather than only that it is chopping. Empty for an objective that is not about
    // having things.
    IReadOnlyDictionary<int, int> Missing();

    /// <summary>Every job that would advance it from where things stand.</summary>
    // Only jobs that can be done now: the anvil is not listed while the bars are still in
    // the ground. In no order, since which to do depends on where the body is and an
    // objective is not told that.
    //
    // A method, not a property: breaking a pickaxe down into ore, wood and three crafts
    // is real work, and a property that costs a recipe walk gets read in a loop.
    IReadOnlyList<IJob> Jobs();

}
