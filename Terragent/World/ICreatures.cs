using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.World;
/// <summary>What is alive nearby.</summary>
// Read by fighting and by nothing else. Whether a creature can actually be hit from here
// is the hand's question, since a clear line matters as much as the distance: the old mod
// spent a minute punching a jellyfish through a wall.
internal interface ICreatures
{
    /// <summary>The closest hostile creature, or null when nothing is about.</summary>
    Creature? Nearest(Point from);

    /// <summary>The closest creature of one of these kinds, or null when none is about.</summary>
    // By kind, because a boss objective is about one particular thing being alive and
    // every other question about what is nearby is answered by the plain Nearest.
    Creature? Nearest(Point from, IReadOnlyList<int> types);

    /// <summary>One creature by index, or null once it is dead or gone.</summary>
    // A fight holds an index between ticks, and the slot is reused the moment the thing
    // dies. Asking again by index is what stops a swing following whatever spawned into
    // its place.
    Creature? At(int index);
}
