namespace Terragent.Pathfinding;

/// <summary>What one search cost, counted rather than timed.</summary>
// Counts and milliseconds answer different questions and neither answers both. Counts are
// the same on any machine, so they say whether the algorithm changed; milliseconds say what
// that cost here. A change that leaves the counts alone and doubles the time made every
// expansion dearer, which is the kind of thing only the pair catches: water checks went
// into the inner loop and nothing about the search's shape moved at all.
//
// Written into by the search as it runs, so it is a tally and not a reading. One per
// search, handed out on the route, and never shared between two.
internal sealed class Effort
{
    /// <summary>Footings taken off the queue and had their moves worked out.</summary>
    // The unit search is measured in, and what the budget counts.
    public int Expanded { get; set; }

    /// <summary>Footings put on the queue.</summary>
    // Against Expanded this gives the branching: how many places each footing opened up.
    public int Generated { get; set; }

    /// <summary>Footings taken off the queue that had already been settled once.</summary>
    // There is no decrease-key, so a cheaper way to a footing already popped queues it
    // again. A few is the cost of that; a lot means the estimate is not consistent.
    public int Reexpanded { get; set; }

    /// <summary>The most the queue ever held at once.</summary>
    public int Peak { get; set; }

    /// <summary>Moves the expansions produced between them.</summary>
    public int Edges { get; set; }

    /// <summary>Times the search asked whether the body fits somewhere.</summary>
    // The hot path, by a distance: the jump loops ask this thousands of times per footing.
    public int Asked { get; set; }

    /// <summary>How many of those were answered from the footing's own cache.</summary>
    public int Remembered { get; set; }

    /// <summary>Times a sweep was checked for what it would have to break.</summary>
    public int Swept { get; set; }

    /// <summary>The expansion that last got nearer the destination than anything before.</summary>
    // Where the progress actually happened. Late means the search spent most of itself on
    // ground that got no closer, and cutting it short would have come back with nothing.
    public int Gained { get; set; }

    /// <summary>Why it stopped.</summary>
    public Ending Ending { get; set; }
}
