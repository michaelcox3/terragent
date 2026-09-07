using System.Collections.Generic;

using Terragent.Work.Objectives;

namespace Terragent.Work;

/// <summary>The objectives of a run, in the order the world allows them.</summary>
// A graph rather than a list: a pickaxe needs no boss and a boss needs the pickaxe, so
// what may be worked on is whatever has all of its requirements behind it. Which of those
// to take is the graph's own business, since the run has to get to the Wall of Flesh and
// nothing else is asking.
//
// Whether a node is reached is remembered, not recomputed. An objective answers Met by
// looking at the world, and the world changes back: a hundred wood becomes ninety the
// moment the bench is built, and a run that re-derived its progress would go and chop
// more. Reached once is reached, and keeping what falls short in stock is what an upkeep
// is for.
internal interface IProgression
{
    /// <summary>What this character has finished, by key, for saving with it.</summary>
    // Keys rather than objectives, because this is what goes in the player file and comes
    // back out of it next session. The graph owns the naming; nothing else needs one.
    IReadOnlyCollection<string> Reached { get; }

    /// <summary>Every node of the run, in the order the file lists them.</summary>
    // For the panel, so the run can be read as a list rather than one line at a time.
    IReadOnlyList<Node> Nodes { get; }

    /// <summary>The objective to work on now, or null when the run is over.</summary>
    // The first unmet one with everything it requires already reached. No effect: asking
    // where the run is does not move it.
    IObjective? Next();

    /// <summary>Record every objective that is met right now, so spending it cannot undo it.</summary>
    // The one thing that moves the run forward, and it happens by looking rather than by
    // being told: an objective can come true on its own, from a chest, a drop, or work
    // done for something else entirely.
    void Record();

    /// <summary>Take back what a character had finished when it was last put down.</summary>
    // Restored from the character's own file, never re-derived from what it is carrying.
    // A character that spent its hundred wood on the thing the wood was for would
    // otherwise load with that objective unmet and go and chop it again.
    void Restore(IEnumerable<string> keys);
}
