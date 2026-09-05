namespace Terragent.World;

// Apart from Sources on purpose: Mining, Drops and Recipes each produce one of these,
// and a producer depending on the collector is an arrow pointing up the layering.
/// <summary>What kind of thing an item comes off.</summary>
internal enum Origin
{
    /// <summary>Broken out of a tile with a tool.</summary>
    Tile,

    /// <summary>Dropped by something that has to be killed first.</summary>
    Creature,

    /// <summary>Made out of other things.</summary>
    Craft,

    /// <summary>
    /// Already on the floor, waiting to be walked onto.
    /// </summary>
    // Not a way the world makes the item, so SourcesOf never returns it: an opportunity
    // rather than an origin, known only to Gather, which can see what is lying about.
    Drop,
}

/// <summary>One way of coming by an item.</summary>
/// <param name="TileID">For <see cref="Origin.Tile"/>: what to break.</param>
/// <param name="CreatureID">For <see cref="Origin.Creature"/>: what drops it.</param>
internal readonly record struct Source(Origin From, int TileID = 0, int CreatureID = 0);
