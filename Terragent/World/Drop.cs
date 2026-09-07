using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>An item lying on the ground.</summary>
/// <param name="Index">Which of the world's items it is, so it can be asked about again.</param>
/// <param name="ItemID">What it is.</param>
/// <param name="Count">How many are in the stack.</param>
/// <param name="Box">
/// The pixels the body has to overlap, which is how Terraria decides a pickup.
/// </param>
// A box rather than a tile: a drop is picked up by being touched, not by standing on the
// tile it looks like it is on, and the two differ by most of a tile when it has rolled
// against a wall.
internal readonly record struct Drop(int Index, int ItemID, int Count, Rectangle Box);
