namespace Terragent.World;

/// <summary>What a tile is, as far as the agent knows.</summary>
internal enum TileKind
{
    /// <summary>Not on the player's map. Never confuse this with <see cref="Empty"/>.</summary>
    Unknown,
    Empty,
    Solid,

    /// <summary>Blocks from above only: jumped through from below, dropped through with Down.</summary>
    Platform,

    /// <summary>
    /// A half block or a floor slope: rock that fills the bottom of its cell.
    /// </summary>
    // Not Platform, though both hold the character up: a platform is a floor at the top
    // of its cell, a slab is rock at the bottom that pushes the body up onto it. Sharing
    // one kind let a sideways move sweep through a slab with nothing booked to break.
    Slab,
}
