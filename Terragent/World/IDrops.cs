using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.World;
/// <summary>What is lying about waiting to be walked into.</summary>
// Mostly the agent's own mining. Ore that is broken and not collected is ore that gets
// dug twice, and a drop within a few tiles is worth more than whatever the run was doing.
internal interface IDrops
{
    /// <summary>The closest dropped item of any of these kinds, or null when there is none.</summary>
    Drop? Nearest(Point from, IReadOnlyList<int> itemIDs);

    /// <summary>One drop by index, or null once it has been taken or has despawned.</summary>
    // What a job that chose one asks about it afterwards. Whether there is any wood about
    // is a different question, and answering that one instead is how a run comes to stand
    // over an empty patch of ground for ever.
    Drop? At(int index);
}
