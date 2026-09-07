using Microsoft.Xna.Framework;

namespace Terragent.Work.Jobs.Targets;

/// <summary>A place, which is its own identity and does not move.</summary>
// A tree, a vein, a station, a patch of ground to stand a bench on. Also what a job with no
// target at all uses, since somewhere and nowhere are the same shape.
internal sealed record TileTarget(Point? Tile) : ITarget
{
    /// <summary>No target in particular, for work that is only about being somewhere.</summary>
    // Null rather than a corner of the map, so nothing can mistake it for a place and
    // measure a distance to it.
    public static TileTarget Nowhere { get; } = new((Point?)null);
}
