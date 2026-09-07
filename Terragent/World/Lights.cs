using Terraria.ID;

namespace Terragent.World;

/// <summary>What the run carries to see by.</summary>
// Two lists because water is the difference. A torch lights the dark and goes out under
// water, which stops the map revealing and leaves the agent swinging at holes it has
// already dug. A glowstick lights either, which is why it is in both.
internal static class Lights
{
    /// <summary>Items that light the dark on land.</summary>
    public static readonly int[] Dark = [ItemID.Torch, ItemID.Glowstick];

    /// <summary>Items that light under water.</summary>
    public static readonly int[] Wet = [ItemID.Glowstick];
}
