using Terraria;

namespace Terragent.World;

/// <summary>The game's clock.</summary>
// Read fresh every time, never stored. A time kept in a field and read back is what made
// every route look freshly planned, so the reading is the only thing shared and the value
// is nobody to hold. Threading it through every signature was the alternative, and it put
// the same number in thirty five parameter lists.
internal sealed class Clock : IClock
{
    private const double TicksPerSecond = 60.0;

    // Frames since the game was started, which is what makes this a duration and not a
    // date. It does not advance while the game is paused, which is the behaviour anything
    // timing a stall wants.
    public double Now => Main.GameUpdateCount / TicksPerSecond;

    public bool Day => Main.dayTime;
}
