using Microsoft.Xna.Framework;
using Terragent.World;

namespace Terragent.Work.Jobs.Targets;

/// <summary>A creature, by the slot the game keeps it in.</summary>
// The slot is the only identity Terraria offers, and it is reused the moment something
// dies. That is a worse problem than it sounds and a better one than the alternative: a
// position goes stale every tick, and a slot goes stale only when its occupant does.
internal sealed record CreatureTarget(ICreatures Creatures, int Index) : ITarget
{
    public Point? Tile => Creatures.At(Index) is { } creature
        ? new Point((int)(creature.Middle.X / 16f), (int)(creature.Middle.Y / 16f))
        : null;

    // By slot alone. The same creature a second later is the same target, and it has moved.
    public bool Equals(CreatureTarget? other) => other is not null && other.Index == Index;

    public override int GetHashCode() => Index;
}
