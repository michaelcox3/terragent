using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>Where a kind of tile has been seen.</summary>
// The search answers what is at a coordinate. This answers the other question, where the
// nearest one of something is, and a job cannot name a site without it.
//
// Seen, not present. Everything here is bounded by what the player has actually revealed,
// or the agent walks straight to ore it has no business knowing about.
internal interface ISites
{
    /// <summary>The closest seen tile of any of these types, or null when none has been.</summary>
    // Several types because the answer is usually a group: any tree, any iron or lead,
    // any of the three tiles that count as a work bench.
    /// <param name="within">How far out to bother looking, in tiles.</param>
    // A caller that only cares about a bench it could walk to pays for the whole box
    // otherwise, and a miss is the expensive answer: the rings stop at the first hit, so
    // finding nothing is what walks every cell.
    Point? Nearest(Point from, IReadOnlyList<int> tileIDs, int within = int.MaxValue);
}
