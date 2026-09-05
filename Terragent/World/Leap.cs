namespace Terragent.World;

/// <summary>
/// What a jump from a standstill can reach: how high, and how far across by the time
/// it comes down to each height.
/// </summary>
/// <param name="Height">How many rows the apex clears, understated by one.</param>
/// <param name="ReachByLanding">
/// How many columns across the body can be when it comes down to a landing this many
/// rows above the takeoff, index zero being level ground.
/// </param>
// Height and reach trade off: a six-row rise peaks about twenty-four frames in, when a
// standing start has gained under two columns, so the two are tabulated together.
// From a standstill on purpose, since the search cannot know whether the takeoff has
// a run-up; understating a jump costs a dig, overstating one a fall. Plain numbers,
// so the headless harness gets the same table the game does.
internal readonly record struct Leap(int Height, int[] ReachByLanding)
{
    /// <summary>Columns across a jump can carry when it lands this many rows above the takeoff.</summary>
    public int Reach(int rowsUp) =>
        rowsUp < 0 || rowsUp >= ReachByLanding.Length ? 0 : ReachByLanding[rowsUp];

    /// <summary>The jump these numbers describe.</summary>
    /// <param name="jumpFrames">Frames the ascent stays powered while the key is held.</param>
    /// <param name="jumpSpeed">Pixels per frame of that ascent.</param>
    /// <param name="gravity">Pixels per frame squared once the power stops.</param>
    /// <param name="runAcceleration">Pixels per frame squared gained sideways while a direction is held.</param>
    /// <param name="maxRunSpeed">Pixels per frame sideways, at most.</param>
    public static Leap Of(int jumpFrames, float jumpSpeed, float gravity, float runAcceleration,
        float maxRunSpeed)
    {
        gravity = System.Math.Max(0.01f, gravity);
        float coast = jumpSpeed * jumpSpeed / (2f * gravity);
        float apexHeight = (jumpFrames * jumpSpeed) + coast;
        float apexFrame = jumpFrames + (jumpSpeed / gravity);
        int height = System.Math.Max(1, (int)(apexHeight / 16f));

        int[] reach = new int[height + 1];
        for (int rowsUp = 0; rowsUp <= height; rowsUp++)
        {
            float landing = rowsUp * 16f;
            if (landing > apexHeight)
            {
                continue;
            }

            // Down through the landing height on the far side of the apex. Terraria
            // steers in the air with the run acceleration, so sideways distance is
            // what a standing start gains in that many frames, capped by the run speed.
            float frames = apexFrame + (float)System.Math.Sqrt(2f * (apexHeight - landing) / gravity);
            float pixels = Sideways(frames, runAcceleration, maxRunSpeed);

            // Half a tile of slack: a footing is two columns and the body lands where
            // momentum leaves it, within a column of the tile named.
            reach[rowsUp] = System.Math.Max(0, (int)((pixels + 8f) / 16f));
        }

        return new Leap(height, reach);
    }

    private static float Sideways(float frames, float acceleration, float top)
    {
        if (acceleration <= 0f)
        {
            return top * frames;
        }

        float untilTop = top / acceleration;
        return frames <= untilTop
            ? 0.5f * acceleration * frames * frames
            : (0.5f * acceleration * untilTop * untilTop) + (top * (frames - untilTop));
    }
}
