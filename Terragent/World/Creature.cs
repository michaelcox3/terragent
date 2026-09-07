using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>Something alive, near enough to matter.</summary>
/// <param name="Index">Which one it is, so a fight can keep hitting the same thing.</param>
/// <param name="Type">What it is, which is how a boss is told from a slime.</param>
/// <param name="Middle">Where its body is, in pixels.</param>
/// <param name="Life">What it has left.</param>
/// <param name="Hostile">Whether it will hurt the agent.</param>
// Pixels rather than a tile, because reach is a distance and a swing either lands or does
// not. The index is what tells two identical slimes apart between ticks.
internal readonly record struct Creature(int Index, int Type, Vector2 Middle, int Life,
    bool Hostile);
