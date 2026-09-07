using Microsoft.Xna.Framework;
using Terragent.World;

namespace Terragent.Work.Jobs.Targets;

/// <summary>An item on the ground, by the slot the game keeps it in.</summary>
// It moves too, though only until it settles: a log bounces out of a felled tree and rolls
// a tile or two before it stops.
internal sealed record DropTarget(IDrops Drops, int Index) : ITarget
{
    public Point? Tile => Drops.At(Index) is { } drop
        ? new Point(drop.Box.Center.X / 16, drop.Box.Center.Y / 16)
        : null;

    public bool Equals(DropTarget? other) => other is not null && other.Index == Index;

    public override int GetHashCode() => Index;
}
